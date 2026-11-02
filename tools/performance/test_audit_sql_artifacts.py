"""采集文件读取合同回归：合成样本与故意伪造计数/摘要，不访问网络或数据库。"""
from __future__ import annotations
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from audit_sql_artifacts import audit, load
from paired_http import ContractError

RUN = '1234567890abcdef1234567890abcdef'
LABEL = 'abcdefabcdefabcdefabcdefabcdefab'
SHA = 'a'*64


def encoded(value):
    return json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode('utf-8')


def fixture(side):
    """固定独立预期；修改结果时不反推放宽预期。"""
    java = side == 'java'
    ident = '10000000-0000-4000-8000-000000000001' if java else '1'*32
    parameter = dict(position=1, driverType='VARCHAR', javaType='java.lang.String', kind='string', valueHash=SHA) if java else dict(name='@p', dbType='String', providerType='VarChar', clrType='System.String', valueKind='string', valueHash=SHA, direction='input', size=0, precision=0, scale=0)
    command = dict(number=2 if java else 1, operation='executeUpdate' if java else 'nonQuery', sql='UPDATE fixture\nSET name=?', parameters=[parameter], connection=1, transaction=1, succeeded=True, errorType=None, affectedRows=1)
    if java:
        command.update(serverPrepared=False, errorCode=None, startedNanos=30, finishedNanos=40)
        transactions = [dict(number=number, operation=operation, connection=1, transaction=transaction, booleanValue=value, isolationLevel=None, succeeded=True, errorType=None, errorCode=None, startedNanos=start, finishedNanos=start+10) for number,operation,transaction,value,start in [(1,'setAutoCommit',1,False,10),(3,'commit',1,None,50),(4,'setAutoCommit',None,True,70)]]
    else:
        command.update(database='offline', prepared=False, driverCalled=True, canceled=False, startedTimestamp=30, driverTimestamp=35, finishedTimestamp=40)
        transactions = [dict(number=number, operation=operation, connection=1, transaction=1, isolation='ReadCommitted', succeeded=True, canceled=False, errorType=None, startedTimestamp=start, finishedTimestamp=start+10) for number,operation,start in [(1,'begin',10),(2,'commit',50)]]
    trace = dict(valueProofFormat='metarill-sql-values-v2' if java else 'metarill-sql-values-v1', commands=[command], transactions=transactions, problems=[], started=1, completed=1, startedTransactions=len(transactions), completedTransactions=len(transactions), closed=True, internallyConsistent=True)
    record = dict(format='metarill-paired-jdbc-http-v1' if java else 'metarill-paired-dotnet-http-v1', runId=RUN, id=ident, sample=LABEL, chainSucceeded=True, errorType=None, trace=trace)
    record['statusAtFilterExit' if java else 'statusAtMiddlewareExit'] = 200
    index = dict(format='metarill-paired-jdbc-index-v1' if java else 'metarill-paired-dotnet-index-v1', runId=RUN, started=1, completed=1, observed=1, problems=[], requests=[dict(id=ident, sha256=SHA)], finalized=True, internallyConsistent=True)
    if java:
        index.update(sourcesVerified=True, dataSources=['dataSource']); source = dict(dataSources=['dataSource'])
    else:
        record.update(problems=[], internallyConsistent=True); trace.update(timestampFrequency=10**9)
        index.update(dataAssemblySha256=SHA, expectedDatabases=['offline'], retainedEntries=3); source = dict(dataAssemblySha256=SHA, databases=['offline'])
    expected = dict(format='metarill-sql-artifact-expectations-v1', side=side, runId=RUN, source=source, samples=[dict(sample=LABEL, statusAtExit=200, chainSucceeded=True, commands=1, transactions=len(transactions), failedCommands=0, failedTransactions=0)])
    return index, record, expected


class SqlArtifactAuditTests(unittest.TestCase):
    def setUp(self):
        """为每项回归准备独占临时目录，结束后只清理本项目录。"""
        self.temporary = tempfile.TemporaryDirectory(prefix='metarill-sql-audit-'); self.addCleanup(self.temporary.cleanup); self.root = Path(self.temporary.name); self.counter=0

    def write(self, index, record):
        """重新计算被篡改产物的SHA，确保失败来自内容不变量，而非只查摘要。"""
        self.counter+=1; directory = self.root / ('artifacts-'+str(self.counter)); directory.mkdir()
        raw = encoded(record); (directory/(index['requests'][0]['id']+'.json')).write_bytes(raw)
        index['requests'][0]['sha256'] = hashlib.sha256(raw).hexdigest()
        raw = encoded(index); (directory/'index.json').write_bytes(raw)
        return directory, hashlib.sha256(raw).hexdigest()

    def checked(self, side, change=None):
        """把固定夹具写为真实文件后调用读取器。"""
        index, record, expected = fixture(side)
        if change:
            change(index, record, expected)
        directory, digest = self.write(index, record)
        return audit(directory, expected, digest)

    def reject(self, side, change):
        """结果被篡改后即使更新SHA，也必须拒绝而非相信自报标志。"""
        with self.assertRaises(ContractError):
            self.checked(side, change)

    def test_closed_actual_shapes_do_not_claim_semantics_or_performance(self):
        """两种格式可以内部有效，同时所有语义、物理和发布结论保持未验。"""
        for side in ('java','csharp'):
            with self.subTest(side=side):
                result=self.checked(side); self.assertEqual(1,result['commands']); self.assertTrue(result['evidenceInternallyValid'])
                for key in ('semanticComparisonAccepted','physicalTargetVerified','performanceAccepted','releaseAccepted'):
                    self.assertIs(False,result[key])

    def test_forged_true_flags_cannot_hide_count_or_pending_errors(self):
        """重新散列后仍重算计数及关闭状态，不接受真值冒充。"""
        for side in ('java','csharp'):
            for key,value in [('started',2),('completed',0),('startedTransactions',99),('closed',False),('problems',['late-call']),('completed',True)]:
                with self.subTest(side=side,key=key,value=value): self.reject(side,lambda i,r,e:r['trace'].__setitem__(key,value))

    def test_root_source_and_finalization_are_frozen(self):
        """索引必须匹配独立冻结的来源和关闭状态。"""
        for side in ('java','csharp'):
            cases=[lambda i,r,e:i.__setitem__('finalized',False),lambda i,r,e:i.__setitem__('started',2),lambda i,r,e:i.__setitem__('observed',True),lambda i,r,e:i.__setitem__('runId','f'*32),lambda i,r,e:i.__setitem__('problems',['late-request'])]
            cases += [lambda i,r,e:i.__setitem__('sourcesVerified',False),lambda i,r,e:i.__setitem__('dataSources',['other'])] if side=='java' else [lambda i,r,e:i.__setitem__('dataAssemblySha256','b'*64),lambda i,r,e:i.__setitem__('retainedEntries',1),lambda i,r,e:i.__setitem__('expectedDatabases',['other'])]
            for n,change in enumerate(cases):
                with self.subTest(side=side,case=n): self.reject(side,change)

    def test_plans_reject_empty_duplicate_unsorted_and_malformed_sources(self):
        """不允许空名单、重复或畸形名字放宽来源边界。"""
        for side,key,values in [('java','dataSources',[[],['dataSource','dataSource'],['z','a'],[1],{},['..\\other']]),('csharp','databases',[[],['offline','offline'],['z','a'],[True],{},['x;DROP']])]:
            for value in values:
                with self.subTest(side=side,value=value): self.reject(side,lambda i,r,e:e['source'].__setitem__(key,value))

    def test_unplanned_missing_duplicate_labels_and_outcomes_reject(self):
        """记录关联及原链结果必须精确匹配，数值类型也不可混用。"""
        for side in ('java','csharp'):
            for key,value in [('sample',None),('sample','f'*32),('id','2'*32),('runId','0'*32),('chainSucceeded',False),('chainSucceeded',1),('errorType','PrivateError')]:
                # id单独篡改会影响写入文件，根引用保持原ID，因此仍须拒绝。
                with self.subTest(side=side,key=key,value=value): self.reject(side,lambda i,r,e:r.__setitem__(key,value))
            status='statusAtFilterExit' if side=='java' else 'statusAtMiddlewareExit'
            for value in (201,200.0,True):
                with self.subTest(side=side,status=value): self.reject(side,lambda i,r,e:r.__setitem__(status,value))

    def test_unknown_fields_and_wrong_value_types_reject(self):
        """未知字段与畸形JSON类型不得逃过固定版本结构。"""
        for side in ('java','csharp'):
            for change in [lambda i,r,e:r.__setitem__('unknown',True),lambda i,r,e:r['trace'].__setitem__('unknown',True),lambda i,r,e:r['trace']['commands'][0].__setitem__('operation',[]),lambda i,r,e:r['trace'].__setitem__('valueProofFormat',[])]:
                with self.subTest(side=side,change=change): self.reject(side,change)

    def test_parameter_hash_position_kind_and_output_are_not_optional(self):
        """缺失证明、错序参数和未验证输出参数必须拒绝。"""
        for side in ('java','csharp'):
            cases=[('valueHash',None),('valueHash','z'*64),('kind' if side=='java' else 'valueKind','unknown'),('kind' if side=='java' else 'valueKind',[])]
            cases += [('position',2),('position',True)] if side=='java' else [('direction','output'),('precision',256),('size',-2)]
            for key,value in cases:
                with self.subTest(side=side,key=key,value=value): self.reject(side,lambda i,r,e:r['trace']['commands'][0]['parameters'][0].__setitem__(key,value))
            self.reject(side,lambda i,r,e:r['trace']['commands'][0]['parameters'].append(copy.deepcopy(r['trace']['commands'][0]['parameters'][0])))

    def test_java_native_nanos_require_v2_and_exact_runtime_type(self):
        """原生精度必须明示版本和类型，不能伪装共同值。"""
        def native(i,r,e):
            r['trace']['commands'][0]['parameters'][0].update(kind='java-datetime-nanos',javaType='java.time.LocalDateTime')
        self.assertEqual(1,self.checked('java',native)['nativeTemporalProofs'])
        for version,kind in [('metarill-sql-values-v1','java.time.LocalDateTime'),('metarill-sql-values-v2','java.lang.String')]:
            def change(i,r,e):
                native(i,r,e); r['trace']['valueProofFormat']=version; r['trace']['commands'][0]['parameters'][0]['javaType']=kind
            self.reject('java',change)

    def test_sql_supports_newlines_but_not_nul_or_oversize(self):
        """支持实际多行SQL，同时保留明确文本预算。"""
        self.assertEqual(1,self.checked('java')['commands'])
        for sql in ('','SELECT\0private','X'*32769): self.reject('java',lambda i,r,e:r['trace']['commands'][0].__setitem__('sql',sql))

    def test_command_tx_reference_connection_and_interval_are_recomputed(self):
        """命令不得跨连接、跨事务时间区间或抹去活动事务引用。"""
        for side in ('java','csharp'):
            for key,value in [('transaction',None),('transaction',2),('connection',2),('number',True)]:
                with self.subTest(side=side,key=key,value=value): self.reject(side,lambda i,r,e:r['trace']['commands'][0].__setitem__(key,value))
            for key,value in [('startedNanos' if side=='java' else 'startedTimestamp',19),('finishedNanos' if side=='java' else 'finishedTimestamp',51)]:
                with self.subTest(side=side,key=key): self.reject(side,lambda i,r,e:r['trace']['commands'][0].__setitem__(key,value))

    def test_autocommit_command_before_or_after_transaction_is_valid(self):
        """事务区间之外的合法自动提交命令不能被误拒绝。"""
        for start in (1,65):
            def change(i,r,e):
                r['trace']['commands'][0].update(transaction=None,startedTimestamp=start,driverTimestamp=start+2,finishedTimestamp=start+4)
            with self.subTest(start=start): self.assertEqual(1,self.checked('csharp',change)['commands'])

    def test_java_single_thread_sequence_cannot_contradict_clock_order(self):
        """Java请求单线程序号与本地单调时间必须一致。"""
        def change(i,r,e):
            r['trace']['commands'][0].update(number=3); r['trace']['transactions'][1].update(number=2)
        self.reject('java',change)

    def test_unknown_or_unended_transaction_and_wrong_boundary_connection_reject(self):
        """结束必须回到已知同连接事务，失败不能冒充提交。"""
        for side in ('java','csharp'):
            for key,value in [('transaction',2),('transaction',None),('connection',2),('succeeded',False)]:
                def change(i,r,e):
                    boundary=r['trace']['transactions'][1]; boundary[key]=value
                    if key=='succeeded': boundary['errorType']='ExpectedFailure'; e['samples'][0]['failedTransactions']=1
                with self.subTest(side=side,key=key,value=value): self.reject(side,change)

    def test_failed_command_has_complete_evidence_but_must_match_plan(self):
        """预先约定的失败可以有完整证据，未计划失败不能通过。"""
        for side in ('java','csharp'):
            def change(i,r,e):
                r['trace']['commands'][0].update(succeeded=False,errorType='ExpectedDriverFailure',affectedRows=None);e['samples'][0]['failedCommands']=1
            self.assertEqual(1,self.checked(side,change)['failedCommands'])
            self.reject(side,lambda i,r,e:r['trace']['commands'][0].update(succeeded=False,errorType='ExpectedDriverFailure',affectedRows=None))

    def test_false_success_error_codes_rows_and_driver_flags_reject(self):
        """错误码、影响行数及驱动调用标记须与结果一致。"""
        for side in ('java','csharp'):
            cases=[('errorCode',0)] if side=='java' else [('driverCalled',False),('canceled',True),('driverTimestamp',41)]
            cases += [('affectedRows',True),('affectedRows',-2),('errorType','ExpectedError')]
            for key,value in cases:
                with self.subTest(side=side,key=key): self.reject(side,lambda i,r,e:r['trace']['commands'][0].__setitem__(key,value))

    def test_failed_commit_can_only_complete_after_known_rollback(self):
        """提交失败后允许原事务回滚闭合，不接受未知事务回指。"""
        def change(i,r,e):
            rows=r['trace']['transactions']; rows[1].update(succeeded=False,errorType='ExpectedFailure')
            rollback=copy.deepcopy(rows[1]); rollback.update(number=3,operation='rollback',succeeded=True,errorType=None,startedTimestamp=70,finishedTimestamp=80); rows.append(rollback)
            r['trace'].update(startedTransactions=3,completedTransactions=3); i['retainedEntries']=4; e['samples'][0].update(transactions=3,failedTransactions=1)
        self.assertEqual(1,self.checked('csharp',change)['failedTransactions'])
        def unknown(i,r,e): change(i,r,e); r['trace']['transactions'][1]['transaction']=2
        self.reject('csharp',unknown)

    def test_clock_wrap_is_local_and_negative_nano_origin_is_valid(self):
        """负原点及有符号回绕是合法本地计时，不进行跨进程换算。"""
        def change(i,r,e):
            for entry in r['trace']['commands']+r['trace']['transactions']:
                for key in list(entry):
                    if key.endswith('Nanos'): entry[key]-=1000
        self.assertEqual(1,self.checked('java',change)['commands'])
        def wrap(i,r,e):
            for entry in r['trace']['commands']+r['trace']['transactions']:
                for key in list(entry):
                    if key.endswith('Nanos'): entry[key]=((entry[key]+2**63-35+2**63) % 2**64)-2**63
        self.assertEqual(1,self.checked('java',wrap)['commands'])

    def test_hash_tampering_missing_extra_or_pending_files_reject(self):
        """错误摘要、缺失文件和残留临时文件都不能算完整采集。"""
        index,record,expected=fixture('java');directory,digest=self.write(index,record)
        with self.assertRaises(ContractError): audit(directory,expected,'0'*64)
        (directory/'unowned.tmp').write_text('pending')
        with self.assertRaises(ContractError): audit(directory,expected,digest)
        (directory/'unowned.tmp').unlink(); trace_path=directory/(record['id']+'.json'); trace_path.write_bytes(trace_path.read_bytes()+b' ')
        with self.assertRaises(ContractError): audit(directory,expected,digest)
        trace_path.unlink()
        with self.assertRaises(OSError): audit(directory,expected,digest)

    def test_strict_json_and_independent_expectation_digest(self):
        """预期文件同样拒绝重复键、非有限值及错误摘要。"""
        path=self.root/'bad.json'
        for raw in (b'{"x":1,"x":2}', b'{"x":NaN}'):
            path.write_bytes(raw)
            with self.assertRaises(ContractError): load(path,hashlib.sha256(raw).hexdigest(),1024)
        path.write_bytes(b'{}')
        with self.assertRaises(ContractError): load(path,'0'*64,1024)
        with self.assertRaises(ContractError): load(path,hashlib.sha256(b'{}').hexdigest(),1)

    def test_cli_creates_summary_once_outside_artifact_tree(self):
        """实际CLI只在采集树外创建全新报告，失败不覆盖旧证据。"""
        index,record,expected=fixture('csharp');directory,digest=self.write(index,record); plan=self.root/'plan.json';raw=encoded(expected);plan.write_bytes(raw); output=self.root/'report.json'
        args=[sys.executable,str(Path(__file__).with_name('audit_sql_artifacts.py')),'--directory',str(directory),'--expectations',str(plan),'--expectations-sha256',hashlib.sha256(raw).hexdigest(),'--index-sha256',digest,'--output',str(output)]
        child_env=dict(os.environ,PYTHONUTF8='0',PYTHONIOENCODING='cp1252:strict')
        completed=subprocess.run(args,capture_output=True,timeout=15,env=child_env);self.assertEqual(0,completed.returncode,completed.stderr);self.assertFalse(json.loads(output.read_text(encoding='utf-8'))['performanceAccepted'])
        self.assertIn('内部核验通过',completed.stdout.decode('utf-8'))
        original=output.read_bytes();failed=subprocess.run(args,capture_output=True,timeout=15,env=child_env);self.assertNotEqual(0,failed.returncode);self.assertEqual(original,output.read_bytes())
        self.assertIn('核验失败',failed.stderr.decode('utf-8'));self.assertNotIn(str(self.root),failed.stderr.decode('utf-8'))
        args[-1]=str(directory/'report.json');self.assertNotEqual(0,subprocess.run(args,capture_output=True,timeout=15,env=child_env).returncode);self.assertFalse((directory/'report.json').exists())
        nested=directory/'nested';nested.mkdir();args[-1]=str(nested/'report.json');self.assertNotEqual(0,subprocess.run(args,capture_output=True,timeout=15,env=child_env).returncode);self.assertFalse((nested/'report.json').exists())

    def test_hard_link_is_not_a_source_authenticity_proof_and_symlink_is_rejected(self):
        """明确文件类型合同边界，不把硬链接当来源真实性证明。"""
        index,record,expected=fixture('java');directory,digest=self.write(index,record);target=self.root/'private.json';target.write_bytes((directory/(record['id']+'.json')).read_bytes());(directory/(record['id']+'.json')).unlink()
        try: os.link(target,directory/(record['id']+'.json'))
        except OSError: self.skipTest('环境不支持硬链接')
        # 硬链接本身不是符号链接：读取合同只防路径逃逸，不声称校验NTFS链接数或来源真实性。
        self.assertTrue(audit(directory,expected,digest)['evidenceInternallyValid'])
        (directory/(record['id']+'.json')).unlink()
        try: (directory/(record['id']+'.json')).symlink_to(target)
        except OSError: return  # Windows未授符号链接权限时，硬链接边界已明确验证。
        with self.assertRaises(ContractError): audit(directory,expected,digest)


if __name__ == '__main__':
    unittest.main()
