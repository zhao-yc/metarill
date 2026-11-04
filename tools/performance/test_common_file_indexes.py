"""共同文件索引的完整回执／真实协调器边界；外部桩不计MySQL或性能通过。"""
import contextlib
import copy
import hashlib
import io
import json
import subprocess
from pathlib import Path
import unittest
from unittest import mock
import paired_common_file_indexes as common
import paired_file_measurement as files
import paired_measurement_fixture as fixture
import paired_native_bootstrap as native
import test_file_measurement as original_file
import test_indexed_write_measurement as relation
import test_paired_native_bootstrap as previous


def value():
    """仅本机合同测试的虚构范围，不授权外部对象。"""
    v=original_file.value();v.update(purpose=native.resources.COMMON_FILE_MEASUREMENT_NATIVE,images=dict(native.resources.COMMON_FILE_IMAGES),fileIndexProfile=native.resources.COMMON_FILE_INDEX_PROFILE);return v


def schema(v,side,desired):
    """最小内存快照仅供回执协议测试；正式C#工具另强制完整原字段及物理类型。"""
    definitions=common.desired(side) if desired else common.original(side);tables=[]
    for table in common.TABLES:
        names={'id'}|{c for pairs in [common.original(side)[table],common.desired(side)[table]] for _,columns in pairs for c in columns}
        columns=[dict(name=name,storeType='bigint' if name!='status' else 'varchar(50)',nullable=name!='id',primaryKey=name=='id',defaultSql=None,generated=False) for name in sorted(names)]
        indexes=[dict(name=name,unique=False,columns=columns) for name,columns in definitions[table]]
        ddl='CREATE TABLE `'+table+'` (\n `id` bigint NOT NULL,\n PRIMARY KEY (`id`),\n '+',\n '.join('KEY `'+name+'` ('+','.join('`'+c+'`' for c in cols)+')' for name,cols in definitions[table])+'\n) ENGINE=InnoDB'
        tables.append(dict(name=table,columns=columns,snapshotDefinition=ddl,indexes=indexes))
    return dict(database='metarill_fresh_'+v['runId'][:16]+'_paired_'+side,tables=tables)


def receipt(v,side,mode):
    """回执含完整模式、两份结构、实际索引属性和已执行条数；不提供真数据库或实际DDL成绩。"""
    before=schema(v,side,mode=='verify');after=schema(v,side,mode!='preview')
    return dict(format=native.resources.COMMON_FILE_INDEX_PROFILE,runId=v['runId'],target=side,operation=mode,database=before['database'],planSha256='a'*64,changes=[] if mode=='verify' else common.expected_changes(side),succeeded=True,appliedStatements=len(common.expected_changes(side)) if mode=='apply' else 0,protectedFingerprint='b'*64,beforeSchema=before,schema=after,indexSnapshot=common.expected_statistics(side,mode!='preview'),emptyTablesVerified=True,columnsAndNonIndexDdlPreserved=True)


def wire(side):
    """模拟独立STATISTICS连接的九列文本，不跳过真实解析与归一函数。"""
    return '\n'.join('\t'.join(str(row[key]) for key in ['table','index','nonUnique','sequence','column','direction','prefix','kind','visible']) for row in common.expected_statistics(side,True))


class CommonFileIndexContractTests(unittest.TestCase):
    def test_purpose_profile_images_and_full_download_budget_are_bound_before_io(self):
        """原AE首测不升级；共同用途固定D109、同4834／18预算及索引profile。"""
        v=value();d=native.validate(v);self.assertEqual(native.resources.COMMON_FILE_MEASUREMENT_RESOURCES,d['purpose']);self.assertEqual(native.resources.COMMON_FILE_IMAGES,native.resources.images_for(d));self.assertTrue(native.resources.is_file_measurement(d));self.assertFalse(native.resources.is_extended_business(d));self.assertFalse(native.resources.is_observed(d));files.config_for(v,original_file.targets(v),original_file.tokens())
        old=original_file.value();native.validate(old);self.assertEqual(native.resources.IMPORT_RULE_IMAGES,native.resources.images_for(old))
        for wrong in [{**v,'images':native.resources.IMPORT_RULE_IMAGES},{**v,'fileIndexProfile':'wrong'},{**v,'fileReplayRequests':4833},{**old,'fileIndexProfile':native.resources.COMMON_FILE_INDEX_PROFILE},{**old,'images':native.resources.COMMON_FILE_IMAGES}]:
            with self.subTest(purpose=wrong['purpose']),mock.patch.object(native,'verify_tool_sources',side_effect=AssertionError('禁止IO')),self.assertRaises(ValueError):native.run(wrong)

    def test_receipts_close_exact_ddl_confirmation_schema_and_statistics(self):
        """Java11／C#10动作及空表、结构保护、CAS和完整索引都逐字段核对。"""
        v=value()
        for side in ['java','csharp']:
            before=receipt(v,side,'preview');common.require_receipt(v,side,'preview',before);common.require_receipt(v,side,'apply',receipt(v,side,'apply'),before);common.require_receipt(v,side,'verify',receipt(v,side,'verify'),before)
            for change in [{'succeeded':False},{'emptyTablesVerified':False},{'columnsAndNonIndexDdlPreserved':False},{'appliedStatements':0},{'changes':common.expected_changes(side)[:-1]},{'database':'metarill_business_test'},{'planSha256':'x'*64},{'format':'old'},{'target':'other'},{'extra':'not-allowed'}]:
                with self.subTest(side=side,change=change),self.assertRaises(ValueError):common.require_receipt(v,side,'apply',{**receipt(v,side,'apply'),**change},before)
            bad=receipt(v,side,'apply');bad['planSha256']='c'*64
            with self.assertRaisesRegex(ValueError,'confirmation'):common.require_receipt(v,side,'apply',bad,before)
            bad=receipt(v,side,'verify');bad['changes']=common.expected_changes(side)
            with self.assertRaises(ValueError):common.require_receipt(v,side,'verify',bad,before)

    def test_non_index_schema_mutation_or_one_sided_snapshot_cannot_hide_behind_booleans(self):
        """两个true标志和相同摘要不能掩盖字段、默认、主键、约束或表选项变化。"""
        v=value();before=receipt(v,'csharp','preview')
        for mutate in [lambda s:s['tables'][0]['columns'][0].update(defaultSql="'changed'"),lambda s:s['tables'][0].update(snapshotDefinition=s['tables'][0]['snapshotDefinition'].replace('ENGINE=InnoDB','ENGINE=MyISAM')),lambda s:s['tables'][0].update(snapshotDefinition=s['tables'][0]['snapshotDefinition']+' CHECK (id>0)'),lambda s:s['tables'][0]['columns'][0].update(nullable=False),lambda s:s['tables'][0]['indexes'][0].update(directions=['bad','ASC'])]:
            bad=receipt(v,'csharp','apply');mutate(bad['schema'])
            with self.subTest(mutate=mutate),self.assertRaises(ValueError):common.require_receipt(v,'csharp','apply',bad,before)
        bad=receipt(v,'java','preview');bad['beforeSchema']['tables'][0]['indexes']=[]
        with self.assertRaisesRegex(ValueError,'read-no-change'):common.require_receipt(v,'java','preview',bad)

    def test_every_statistics_attribute_and_primary_key_are_required_before_alias_mapping(self):
        """不能只映射名字或忽略额外索引、表达式、唯一性、方向及可见性。"""
        self.assertEqual(common.normalize_statistics(common.expected_statistics('java',True),'java'),common.normalize_statistics(common.expected_statistics('csharp',True),'csharp'));self.assertEqual(17,len(common.expected_statistics('java',True)))
        for change in [{'prefix':'8'},{'kind':'HASH'},{'visible':'NO'},{'direction':'D'},{'nonUnique':True},{'nonUnique':1},{'column':''},{'sequence':99}]:
            rows=common.expected_statistics('java',True);rows[0].update(change)
            with self.subTest(change=change),self.assertRaises(ValueError):common.normalize_statistics(rows,'java')
        for rows in [common.expected_statistics('java',True)[1:],common.expected_statistics('java',True)*2,common.expected_statistics('java',False)]:
            with self.assertRaises(ValueError):common.normalize_statistics(rows,'java')

    def test_independent_pre_post_query_is_exact_bounded_and_rejects_malformed_or_extra_rows(self):
        """只查询本轮两表；无效run／目标在SQL前拒绝，额外行不能被LIMIT或映射吞掉。"""
        v=value();queries=[]
        def query(sql):queries.append(sql);return wire('java')
        result=common.inspect_indexes(query,v['runId'],'java');self.assertTrue(result['physicalAttributesVerified']);self.assertEqual(17,len(result['rows']));self.assertIn("TABLE_NAME IN ('yz_file_library','yz_file_ref')",queries[0]);self.assertTrue(queries[0].endswith(' LIMIT 65;'));self.assertEqual(1,queries[0].count(';'))
        for run,side in [(v['runId']+'\n','java'),(v['runId'],'foreign'),('x'*32,'csharp')]:
            with self.subTest(run=run,side=side),self.assertRaises(ValueError):common.inspect_indexes(mock.Mock(side_effect=AssertionError('禁止查询')),run,side)
        with self.assertRaises(ValueError):common.inspect_indexes(lambda sql:wire('java')+'\n'+wire('java').splitlines()[0],v['runId'],'java')

    def test_tenth_actual_source_module_cannot_be_missing_or_replaced(self):
        """共同用途必须固定实际执行的第十份Python模块，不能借旧九文件版本号启动。"""
        v=value();v['toolSourceCommit']='a'*40;root=Path(native.__file__).resolve().parents[1];names=['acceptance/server200-fresh-mysql.py','performance/paired_native_bootstrap.py','performance/paired_http.py','performance/paired_identity_seed.py','performance/server200_paired_resources.py','performance/paired_business_http.py','performance/paired_measurement_fixture.py','performance/paired_measurement.py','performance/paired_file_measurement.py','performance/paired_common_file_indexes.py'];v['toolSha256']={n:hashlib.sha256((root/n).read_bytes()).hexdigest() for n in names};native.verify_tool_sources(v);del v['toolSha256']['performance/paired_common_file_indexes.py']
        with self.assertRaises(ValueError):native.verify_tool_sources(v)

    def test_real_seed_entry_supports_native_common_profile_but_not_resource_or_ended_scope(self):
        """直接跑真实种子入口，避免再由外部桩漏掉白名单登记。"""
        v=value();db,model=mock.Mock(),mock.Mock();db.mysql.return_value='1\n1';model.verify_readback.return_value={'verified':True}
        with mock.patch.object(native,'require_database_ownership') as own:
            self.assertEqual({'verified':True},native.seed_measurement_database(db,v,'owner',model,'java'));own.assert_called_once();model.verify_readback.assert_called_once_with(v['runId'],'java',db.mysql,relation_indexes=True)
        for changes in [{'purpose':native.resources.COMMON_FILE_MEASUREMENT_RESOURCES},{'leaseStatus':'ended'}]:
            with self.assertRaises(ValueError):native.seed_measurement_database(db,{**v,**changes},'owner',model,'java')

    def test_upgrade_never_creates_short_processes_before_original_twelve_or_on_old_scope(self):
        """缺原初始化／索引清理或误用旧用途时，不能先取口令／归属或发DDL。"""
        for v in [value(),original_file.value()]:
            with self.assertRaises(ValueError):common.upgrade(Path('.'),v,{'jobs':[]},Path('state'),mock.Mock(side_effect=AssertionError('禁止外调')),mock.Mock(side_effect=AssertionError('禁止命令')),(*native.JOBS,*native.INDEX_JOBS))


class CommonFileIndexFlowTests(previous.FlowFixture):
    def setUp(self):
        """跑真实十八短进程协调和前后STATISTICS解析，仅替换系统边界与测量负载。"""
        super().setUp();self.value=value();self.jobs.side_effect=self.index_job;self.identity.return_value=([{'mockedOnly':True}],original_file.tokens());self.enterContext(mock.patch.object(native,'require_database_ownership'));self.measured=False;self.drift=False
        self.enterContext(mock.patch.object(native,'seed_measurement_database',return_value={'mockedOnly':True}));self.enterContext(mock.patch.object(fixture,'verify_readback',return_value={'mockedOnly':True}));self.enterContext(mock.patch.object(files,'run_file_measurement',side_effect=self.measure));self.mysql.mysql.side_effect=self.query

    def index_job(self,root,v,name,state,path,*,expected_index_plan=None):
        """完整登记十八个回执；确认摘要由真实helper校验，不替换共同索引验证函数。"""
        state['jobs'].append(dict(kind='container',target='init-'+name,name='metarill-perf-'+v['runId']+'-init-'+name,id='e'*64,containerAbsent=True))
        if name in native.JOBS:return self.job(root,v,name,state,path)
        if name in native.INDEX_JOBS:
            side,mode=name.split('-')[2:];return relation.receipt(v,side,mode)
        self.events.append(name);side,mode=name.split('-')[3:]
        if mode=='apply':self.assertEqual('a'*64,expected_index_plan)
        return receipt(v,side,mode)

    def query(self,sql):
        """输出固定九列线格式，不替换真实的独立STATISTICS校验或别名归一。"""
        if sql=='SELECT @@hostname,@@server_uuid;':return native.PIN['hostname']+'\t'+native.PIN['serverUuid']
        if 'FROM information_schema.STATISTICS' in sql:
            side='java' if '_paired_java' in sql else 'csharp';result=wire(side)
            return result+'\n'+result.splitlines()[0] if self.measured and self.drift else result
        return '0'

    def measure(self,*args):
        """只供编排测试的假测量，不产生或冒充下载／性能成绩。"""
        self.measured=True;self.events.append('file-measurement');return dict(measurementValid=True,stableRegression=False,fileFixture={'targets':{'java':{'contractId':'905900000000000001'},'csharp':{'contractId':'905900000000000002'}}})

    def test_complete_flow_adds_six_fixed_jobs_before_seed_and_reads_indexes_after_download(self):
        """完整链通过十八初始化和实际前后索引核验；旧首测用途和整体性能标志不改变。"""
        with contextlib.redirect_stdout(io.StringIO()):result=native.run(self.value)
        self.assertTrue(result['passed']);self.assertEqual(18,self.jobs.call_count);self.assertEqual('paired-common-file-index-protected-measurement-only',result['acceptedScope']);self.assertEqual(result['fileIndexesBefore'],result['fileIndexesAfter']);self.assertTrue(result['measurement']['commonFileIndexAttributesVerified']);self.assertFalse(result['performanceAccepted']);self.assertEqual([],result['cleanupErrors']);self.assertEqual('ended',result['leaseStatus'])
        self.assertEqual(11,result['fileIndexPlans']['java']['applied']['appliedStatements']);self.assertEqual(10,result['fileIndexPlans']['csharp']['applied']['appliedStatements']);self.assertLess(self.events.index('csharp-file-index-csharp-verify'),self.events.index('file-measurement'));self.assertLess(self.events.index('file-measurement'),self.events.index('resources-cleanup'))

    def test_post_measurement_index_drift_invalidates_whole_run_and_still_cleans(self):
        """即使所有下载返回成功，负载后额外索引属性行仍使整轮无效；清理成功不能改写失败。"""
        self.drift=True
        with contextlib.redirect_stdout(io.StringIO()):result=native.run(self.value)
        self.assertFalse(result['passed']);self.assertEqual('measurement-file-index-statistics-drift',result['failure']['code']);self.assertEqual([],result['cleanupErrors']);self.assertEqual('ended',result['leaseStatus']);self.assertIn('database-cleanup',self.events)


class CommonFileIndexCommandTests(unittest.TestCase):
    def setUp(self):
        """真实构造并解析短进程命令，系统调用只交给内存Docker替身。"""
        previous.JobTests.setUp(self);self.value=value()

    def docker(self,*args):
        """固定新索引工具镜像，不能在此测试中混入旧AE镜像。"""
        if args[:2]==('container','inspect') and args[args.index('--format')+1]=='{{.Image}}':return native.resources.COMMON_FILE_IMAGES['csharp']
        return previous.JobTests.docker(self,*args)

    def test_actual_file_job_passes_dedicated_scope_profile_and_confirmation_without_secret_or_sql_argv(self):
        """不mock初始化入口；新动作、scope、profile和摘要全部实际进入同一依赖闭包。"""
        expected=receipt(self.value,'java','apply');self.capture.return_value=subprocess.CompletedProcess([],0,json.dumps(expected).encode(),b'')
        actual=native.initialization_job(self.root,self.value,'csharp-file-index-java-apply',self.state,self.path,expected_index_plan='a'*64);common.require_receipt(self.value,'java','apply',actual,receipt(self.value,'java','preview'));self.assertEqual(1,self.capture.call_count)
        command=next(c for c in self.engine.calls if c[:2]==('container','create'));self.assertEqual('file-apply-java',command[-1]);self.assertIn('METARILL_FILE_INDEX_SCOPE='+native.resources.COMMON_FILE_MEASUREMENT_NATIVE,command);self.assertIn('METARILL_FILE_INDEX_PROFILE='+native.resources.COMMON_FILE_INDEX_PROFILE,command);self.assertIn('METARILL_INDEX_EXPECTED_PLAN='+'a'*64,command);self.assertIn('/app/Application.Host.deps.json',command);self.assertNotIn('Password=',' '.join(command));self.assertNotIn('ALTER TABLE',' '.join(command));self.assertFalse(self.engine.objects);self.assertTrue(self.state['jobs'][0]['containerAbsent'])

    def test_old_scope_or_missing_confirmation_fails_before_any_resource_or_command(self):
        """旧文件首测、额外动作、缺摘要都不能读归属文件或启动新索引工具。"""
        for v,name,confirmation in [(original_file.value(),'csharp-file-index-java-preview',None),(self.value,'csharp-file-index-java-apply',None),(self.value,'csharp-file-index-java-preview-extra',None)]:
            with self.subTest(name=name),mock.patch.object(native.http,'read_json',side_effect=AssertionError('禁止读资源')),self.assertRaises(ValueError):native.initialization_job(self.root,v,name,self.state,self.path,expected_index_plan=confirmation)
        self.capture.assert_not_called();self.assertEqual([],self.engine.calls)


if __name__=='__main__':unittest.main()
