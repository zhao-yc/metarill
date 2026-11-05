"""只读审阅固定配对样本的单表SELECT；拒绝不透明表达式，不执行或翻译SQL。"""
from __future__ import annotations
import hmac
import re


class ReadReviewError(ValueError):
    """仅返回固定错误位置，不能把SQL、实际值或key写入异常。"""


def require(ok,code):
    """校验失败保留固定诊断，调用方自行保管原始私有证据。"""
    if not ok:raise ReadReviewError('read-review-'+code)


def parameter(command,side,token,position):
    """按JDBC位置或已确认MySqlConnector名字绑定，拒绝重复及未绑定参数。"""
    parameters=command.get('parameters');require(type(parameters) is list,'parameters')
    if side=='java':
        require(token=='?','jdbc-placeholder');matches=[p for p in parameters if p.get('position')==position]
    else:
        require(re.fullmatch(r'@[A-Za-z_][A-Za-z_0-9]*',token) is not None,'named-placeholder')
        matches=[p for p in parameters if p.get('name','').removeprefix('@')==token[1:]]
    require(len(matches)==1,'parameter-binding');p=matches[0];kind=p.get('kind' if side=='java' else 'valueKind')
    require(kind in {'string','integer','null'},'parameter-kind');require(re.fullmatch('[a-f0-9]{64}',p.get('valueHash','')) is not None,'parameter-proof')
    return dict(kind=kind,valueHash=p['valueHash'])


def parse_read(command,side):
    """仅接受样本单表AND条件／物理投影／COUNT／SUM；OR、子查询和任意表达式均拒绝。"""
    require(side in {'java','csharp'} and type(command) is dict and command.get('succeeded') is True,'command')
    require(command.get('operation') in ({'execute','executeQuery'} if side=='java' else {'reader','scalar'}),'driver-operation')
    sql=command.get('sql');require(type(sql) is str and 0<len(sql)<=16384 and not any(x in sql for x in [';','--','/*','*/','\x00']),'sql-text')
    sql=re.sub(r'`([A-Za-z_][A-Za-z_0-9]*)`',r'\1',sql);require('`' not in sql,'identifier')
    match=re.fullmatch(r'\s*SELECT\s+(.+?)\s+FROM\s+(sample_customer|sample_contract_line|sample_contract)(?:\s+([a-z]))?\s+WHERE\s+(.+?)\s*',sql,re.I|re.S);require(match is not None,'single-table-select')
    projection,table,alias,tail=match.groups();alias=alias or ''
    # 别名只消除当前表的一个确定前缀，不能把任意联接目标映射成主表。
    if alias:
        projection=re.sub(r'\b'+re.escape(alias)+r'\.', '',projection)
        tail=re.sub(r'\b'+re.escape(alias)+r'\.', '',tail)
    projection=re.sub(r'\s+AS\s+[A-Za-z_][A-Za-z_0-9]*\s*(?=,|$)','',projection.strip(),flags=re.I)
    projection=re.sub(r'\s+as[0-9]+\s*(?=,|$)','',projection,flags=re.I)
    compact=re.sub(r'\s+','',projection).lower()
    if compact in {'count(*)','count(1)'}:kind='count';fields=[]
    elif compact in {'sum(amount)','coalesce(sum(amount),0)'}:kind='sum';fields=['amount']
    else:
        kind='rows';fields=[s.strip() for s in projection.split(',')];require(fields and all(re.fullmatch('[A-Za-z_][A-Za-z_0-9]*',f) is not None for f in fields) and len(fields)==len(set(fields)),'projection')
    locked=None
    lock=re.search(r'\s+FOR\s+(UPDATE|SHARE)\s*$',tail,re.I)
    if lock:locked=lock[1].lower();tail=tail[:lock.start()]
    limit=None;limit_parameters=[]
    bound=re.search(r'\s+LIMIT\s+(.+?)\s*$',tail,re.I)
    if bound:
        raw=bound[1].strip();tail=tail[:bound.start()]
        if re.fullmatch(r'[0-9]+\s*,\s*[0-9]+',raw):start,size=map(int,re.split(r'\s*,\s*',raw));limit=(start,size)
        elif re.fullmatch('[0-9]+',raw):limit=(0,int(raw))
        elif raw=='?':limit_parameters=['?'];limit=(0,None)
        else:require(False,'limit-shape')
    order=[];ordered=re.search(r'\s+ORDER\s+BY\s+(.+?)\s*$',tail,re.I)
    if ordered:
        for item in ordered[1].split(','):
            item=item.strip();m=re.fullmatch(r'([A-Za-z_][A-Za-z_0-9]*)(?:\s+(ASC|DESC))?',item,re.I);require(m is not None,'order-shape');order.append((m[1],(m[2] or 'ASC').lower()))
        tail=tail[:ordered.start()]
    # 本合同只准AND连接的简单原子条件，括号没有OR/函数语义，去括号前也不接受任何其它字符。
    depth=0
    for char in tail:
        depth+=(char=='(')-(char==')');require(depth>=0,'parentheses')
    require(depth==0,'parentheses');conditions=re.split(r'\bAND\b',tail.replace('(','').replace(')',''),flags=re.I);predicates=[];position=0
    for atom in conditions:
        atom=atom.strip()
        if re.fullmatch(r'1\s*=\s*1',atom):continue
        m=re.fullmatch(r'([A-Za-z_][A-Za-z_0-9]*)\s+IS\s+NULL',atom,re.I)
        if m:predicates.append(dict(column=m[1],operator='is-null',value=None));continue
        m=re.fullmatch(r'([A-Za-z_][A-Za-z_0-9]*)\s*(=|<>|!=|LIKE)\s*(\?|@[A-Za-z_][A-Za-z_0-9]*)',atom,re.I);require(m is not None,'predicate-shape')
        position+=1;predicates.append(dict(column=m[1],operator='ne' if m[2] in {'<>','!='} else m[2].lower(),value=parameter(command,side,m[3],position)))
    if limit_parameters:
        position+=1;limit_value=parameter(command,side,'?',position)
        limit=(0,limit_value)
    else:limit_value=None
    require(predicates,'empty-predicate')
    if side=='java':require(len(command['parameters'])==position and sorted(p.get('position') for p in command['parameters'])==list(range(1,position+1)),'jdbc-parameter-coverage')
    else:
        references=set(re.findall('@([A-Za-z_][A-Za-z_0-9]*)',sql));actual=[p.get('name','').removeprefix('@') for p in command['parameters']]
        require(len(actual)==len(set(actual)) and references==set(actual),'named-parameter-coverage')
    return dict(table=table,kind=kind,fields=fields,predicates=predicates,order=order,limit=limit,locked=locked)


def proves(parameter,key,value,kinds):
    """按预先固定输入重算真实值证明；不比较不同轮次的HMAC，不从实际值推导预期。"""
    require(type(key) is bytes and len(key)==32,'review-key');require(type(value) is str,'expected-value')
    require(parameter['kind'] in kinds,'expected-kind')
    if parameter['kind']=='integer':require(re.fullmatch(r'-?(0|[1-9][0-9]*)',value) is not None and -(2**63)<=int(value)<2**63,'integer-value')
    expected=hmac.digest(key,(parameter['kind']+'\0'+value).encode('utf-8'),'sha256').hex();require(hmac.compare_digest(parameter['valueHash'],expected),'expected-value-proof')


def verify_read(command,side,key,*,table,kind,fields,predicates,order,limit,locked=None):
    """按调用方冻结的用途条件精确核表、投影、筛选、排序及分页；隐藏条件不能被忽略。"""
    actual=parse_read(command,side);require(actual['table']==table and actual['kind']==kind,'purpose')
    require(set(actual['fields'])==set(fields) and len(actual['fields'])==len(fields),'expected-projection')
    require(actual['order']==order and actual['locked']==locked,'expected-order-lock')
    require(len(actual['predicates'])==len(predicates),'predicate-count')
    remaining=list(actual['predicates'])
    for column,operator,value,kinds in predicates:
        matches=[p for p in remaining if p['column']==column and p['operator']==operator];require(len(matches)==1,'expected-predicate');p=matches[0];remaining.remove(p)
        if operator=='is-null':require(p['value'] is None and value is None,'null-predicate')
        else:proves(p['value'],key,value,kinds)
    if actual['limit'] is not None and isinstance(actual['limit'][1],dict):
        require(limit is not None and actual['limit'][0]==limit[0],'limit-offset');proves(actual['limit'][1],key,str(limit[1]),{'integer'})
    else:require(actual['limit']==limit,'expected-limit')
    return dict(table=table,kind=kind,physicalFieldsVerified=len(fields),predicatesVerified=len(predicates),parameterValuesVerified=sum(p['value'] is not None for p in actual['predicates'])+int(limit is not None and isinstance(actual['limit'][1],dict)),orderAndPaginationVerified=True,readOnly=True)
