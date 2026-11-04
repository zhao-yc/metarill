"""真实业务用途／缓存故障边界及实际回环HTTP；外部Docker／数据库为桩，不能计正式业务。"""
import contextlib
import hashlib
import http.server
import io
import json
from pathlib import Path
import tempfile
import threading
import types
import unittest
from unittest import mock
import paired_extended_business as extended
import paired_native_bootstrap as native
from test_paired_native_bootstrap import FlowFixture, settings
import test_extended_readiness as readiness_fixture
import test_server200_paired_resources as resource_fixture


def value():
    """虚构运行号只供本机合同测试，不能代表真实服务器授权或资源。"""
    result=settings()
    result.update(purpose=native.resources.EXTENDED_BUSINESS_NATIVE,images=dict(native.resources.EXTENDED_READINESS_IMAGES),relationIndexProfile=native.resources.RELATION_INDEX_PROFILE,workbookOperations=10,maintenanceProfile=extended.MAINTENANCE_PROFILE)
    return result


class ContractTests(unittest.TestCase):
    def test_business_scope_does_not_enable_capture_or_performance_and_rejects_wrong_budget(self):
        """业务独立用途固定新镜像和十文件操作，不放宽原就绪或测量入口。"""
        v=value();descriptor=native.validate(v)
        self.assertEqual(native.resources.EXTENDED_BUSINESS_RESOURCES,descriptor['purpose'])
        self.assertTrue(native.resources.uses_relation_indexes(descriptor))
        self.assertFalse(native.resources.is_observed(descriptor));self.assertFalse(native.resources.is_measurement(descriptor))
        self.assertEqual(['--Metarill:Paired:SqlObservation:Enabled=false'],native.resources.observation_arguments(descriptor,'csharp'))
        for change in ({'maintenanceProfile':None},{'workbookOperations':9},{'workbookOperations':True},{'workbookOperations':None},{'readReplayRequests':19336},{'workbookReplayRequests':10},{'relationIndexProfile':None},{'images':native.resources.INDEXED_WRITE_IMAGES},{'authorizationStatus':'pending'},{'purpose':native.resources.EXTENDED_READINESS_NATIVE}):
            with self.subTest(change=change),mock.patch.object(native,'verify_tool_sources',side_effect=AssertionError('不允许文件或外部调用')),self.assertRaises(ValueError):native.run({**v,**change})

    def test_corrected_file_auth_images_have_their_own_scope_and_cannot_replace_history(self):
        """新旧用途精确镜像互换必须在读文件前拒绝；原就绪及旧业务仍固定de3镜像。"""
        resources=native.resources;v=value();v.update(purpose=resources.FILE_AUTH_BUSINESS_NATIVE,images=dict(resources.FILE_AUTH_BUSINESS_IMAGES))
        descriptor=native.validate(v)
        self.assertEqual(resources.FILE_AUTH_BUSINESS_RESOURCES,descriptor['purpose'])
        self.assertFalse(resources.is_measurement(descriptor));self.assertFalse(resources.is_observed(descriptor))
        self.assertEqual(resources.FILE_AUTH_BUSINESS_IMAGES,resources.images_for(descriptor))
        self.assertEqual('sha256:de3ea9e8217e0f8574a9912db16637be0a1767bc5f1759732486848886043e93',resources.images_for(value())['csharp'])
        for item in [{**v,'images':resources.EXTENDED_READINESS_IMAGES},{**value(),'images':resources.FILE_AUTH_BUSINESS_IMAGES}]:
            with self.subTest(purpose=item['purpose']),mock.patch.object(native,'verify_tool_sources',side_effect=AssertionError('不允许IO')),self.assertRaises(ValueError):native.run(item)

    def test_fault_fixture_only_matches_four_owned_named_rows_before_commit(self):
        """四个缓存更新限定本轮库／原值／行名与父键；每条均先通过ROW_COUNT保护。"""
        run='a'*32;contracts=['9007199254740993','9007199254740994'];lines=['9007199254740995','9007199254740996']
        sql=extended.maintenance_fixture_sql(run,'csharp',contracts,lines)
        self.assertTrue(sql.startswith('START TRANSACTION;'))
        self.assertEqual(4,sql.count('UPDATE `metarill_fresh_aaaaaaaaaaaaaaaa_paired_csharp`.'))
        self.assertEqual(4,sql.count('ROW_COUNT()'));self.assertEqual(4,sql.count('PREPARE mt_fixture_gate FROM'))
        self.assertEqual(4,sql.count('delete_no IS NULL'));self.assertEqual(2,sql.count('BINARY `sample_contract_id`=BINARY'))
        self.assertEqual(1,sql.count('COMMIT;'));self.assertGreater(sql.index('COMMIT;'),sql.rindex('DEALLOCATE PREPARE'))
        for token in ('DROP','DELETE','INSERT','GRANT','TRUNCATE','yz_file_ref'):
            self.assertNotIn(token,sql)
        for args in [('other',contracts,lines),('csharp',contracts,[contracts[0],lines[1]]),('java',['1','2'],lines),('java',contracts+[contracts[0]],lines)]:
            with self.subTest(args=args),self.assertRaises(ValueError):extended.maintenance_fixture_sql(run,*args)

    def test_baseline_catalog_requires_exact_original_columns_and_explicit_missing_join_flags(self):
        """预检不能把任意无效目录解释成已知原维护缺口，更不能吞掉新增联接列。"""
        definitions=[('810000000000000011','sample_customer',['name','remarks']),('810000000000000012','sample_contract',['name','sample_customer_id','amount','remarks','attachments']),('810000000000000013','sample_contract_line',['name','sample_contract_id','amount','remarks'])]
        catalog={'configTables':[dict(id=identifier,tableName=name,isRongyu=1) for identifier,name,_ in definitions],
                 'configColumns':[dict(configTableId=identifier,columnName=field,isJoinColumn=0,isVirtualColumn=0) for identifier,_,fields in definitions for field in fields]}
        proof=extended.validate_original_maintenance_catalog(catalog)
        self.assertFalse(proof['originalAutomaticCacheBackfillSupported']);self.assertEqual(0,proof['declaredJoinColumns'])
        import copy
        for change in ('new-join','missing-column','extra-column','wrong-table'):
            altered=copy.deepcopy(catalog)
            if change=='new-join':altered['configColumns'][0]['isJoinColumn']=1
            elif change=='missing-column':altered['configColumns'].pop()
            elif change=='extra-column':altered['configColumns'].append(dict(configTableId='810000000000000012',columnName='sample_customer_name',isJoinColumn=0,isVirtualColumn=0))
            else:altered['configTables'][0]['tableName']='other'
            with self.subTest(change=change),self.assertRaises(ValueError):extended.validate_original_maintenance_catalog(altered)

    def test_wrong_purpose_fails_before_oracle_files_or_directory_creation(self):
        """不能从旧用途／结束租约创建业务证据目录或读取模块。"""
        with tempfile.TemporaryDirectory() as temp,mock.patch.object(extended.oracle,'read_inputs',side_effect=AssertionError('不应读取')):
            root=Path(temp)
            for change in ({'purpose':native.resources.EXTENDED_READINESS_NATIVE},{'authorizationStatus':'pending'},{'leaseStatus':'ended'}):
                v={**value(),'purpose':extended.PURPOSE,'leaseStatus':'running',**change}
                with self.subTest(change=change),self.assertRaises(ValueError):extended.Exchange(v,root,{'runId':v['runId']},root/'native-state.json')
            self.assertFalse((root/'extended-evidence').exists())


class NativeFlowTests(FlowFixture):
    def setUp(self):
        """沿真实协调器执行十二初始化、登录、业务和清理，只替换外部边界。"""
        super().setUp();self.value=value();self.jobs.side_effect=lambda *args,**kwargs:readiness_fixture.ExtendedReadinessFlowTests.extended_job(self,*args,**kwargs)
        tokens={(target,kind):('a'*64 if target=='csharp' else 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa') for target in ('java','csharp') for kind in native.seed.KINDS}
        self.identity.return_value=([{'mockedOnly':True}],tokens)
        self.module=types.SimpleNamespace(validate_original_maintenance_catalog=mock.Mock(return_value={'mockedOnly':True}),oracle=types.SimpleNamespace(read_inputs=mock.Mock()),Exchange=mock.Mock(return_value=object()),run_extended=mock.Mock(side_effect=self.extended))
        self.enterContext(mock.patch.object(native.importlib,'import_module',side_effect=lambda name:self.module if name=='paired_extended_business' else (_ for _ in ()).throw(AssertionError('不能加载观测／测量'))))
        self.enterContext(mock.patch.object(native,'require_database_ownership'))
        (self.root/'bootstrap/java').mkdir(parents=True);(self.root/'bootstrap/java/project-config.json').write_text('{}',encoding='utf-8')

    def extended(self,*args):
        """记录原生调用确实位于登录后和清理前，不伪装真实业务结果。"""
        self.events.append('extended-business')
        return {'mockedOnly':True,'performanceAccepted':False}

    def test_original_initialization_and_ordinary_identity_precede_real_business_entry(self):
        """旧原六和六索引仍各一次，真实业务回调接收三普通身份和受限故障写入器。"""
        with contextlib.redirect_stdout(io.StringIO()):result=native.run(self.value)
        self.assertTrue(result['passed']);self.assertEqual(12,self.jobs.call_count);self.assertFalse(result['performanceAccepted'])
        self.assertEqual('paired-original-crud-excel-file-maintenance-functional-only',result['acceptedScope'])
        self.assertLess(self.events.index('resources-wait-hosts'),self.events.index('extended-business'))
        self.assertLess(self.events.index('extended-business'),self.events.index('resources-cleanup'))
        self.assertLess(self.events.index('resources-cleanup'),self.events.index('database-cleanup'))
        args=self.module.run_extended.call_args.args
        self.assertEqual(extended.PURPOSE,args[0]['purpose']);self.assertEqual({'writer','reader','denied'},set(args[1]['java']))
        self.assertTrue(callable(args[4]));self.assertIn('record_request',self.identity.call_args.kwargs)
        self.assertEqual([],result['cleanupErrors']);self.assertEqual('ended',result['leaseStatus'])

    def test_corrected_file_auth_scope_reaches_the_same_strict_business_after_identity(self):
        """真实协调器必须传新业务用途、同样三普通身份和原四格维护合同，不走测量路径。"""
        self.value.update(purpose=native.resources.FILE_AUTH_BUSINESS_NATIVE,images=dict(native.resources.FILE_AUTH_BUSINESS_IMAGES))
        with contextlib.redirect_stdout(io.StringIO()):result=native.run(self.value)
        self.assertTrue(result['passed']);self.assertEqual(12,self.jobs.call_count)
        args=self.module.run_extended.call_args.args
        self.assertEqual(native.resources.FILE_AUTH_BUSINESS,args[0]['purpose'])
        self.assertEqual(extended.MAINTENANCE_PROFILE,args[0]['maintenanceProfile'])
        self.assertEqual(10,args[0]['workbookOperations']);self.assertEqual({'writer','reader','denied'},set(args[1]['csharp']))
        self.assertEqual([],result['cleanupErrors']);self.assertFalse(result['performanceAccepted'])

    def test_business_failure_preserves_first_stage_diagnostics_and_cleanup(self):
        """文件／工作簿业务失败不能由回收成功改成通过，原首因仍留存。"""
        self.module.run_extended.side_effect=ValueError('business-extended-export-values-contract')
        with contextlib.redirect_stdout(io.StringIO()):result=native.run(self.value)
        self.assertFalse(result['passed']);self.assertIn('真实原工作簿',result['failedAtStage'])
        self.assertEqual('business-extended-export-values-contract',result['failure']['code'])
        self.assertIn('resources-snapshot-hosts',self.events);self.assertEqual([],result['cleanupErrors'])


class ExtendedResourceTests(unittest.TestCase):
    materialize=resource_fixture.Fixture.materialize
    tearDown=resource_fixture.Fixture.tearDown

    def setUp(self):
        """沿真实资源构造器，只用内存Docker桩；普通应用配置与根只读保持。"""
        resource_fixture.Fixture.setUp(self)
        self.value.update(purpose=native.resources.EXTENDED_BUSINESS_RESOURCES,images=dict(native.resources.EXTENDED_READINESS_IMAGES))
        self.inputs['csharp.env']+='Authentication__LoginFailures__ConnectionString=redis:6379,password='+'2'*64+',defaultDatabase=0\n'
        self.materialize()

    def test_original_java_file_root_uses_only_existing_owned_data_mount(self):
        """显式文件根只接既有/data，不放开只读根、不增加目录挂载或更换镜像。"""
        resources=native.resources;resources.manage(self.value,'create')
        commands=[command for command in self.engine.calls if command[:2]==('container','create')]
        java,csharp=commands[1:]
        self.assertEqual('--custom.file-storage.local.root-path=/data/files',java[-1])
        self.assertEqual('--metarill.paired.sql-observation.enabled=false',java[-2])
        self.assertFalse(any('file-storage' in part for part in csharp))
        for command in (java,csharp):
            self.assertIn('--read-only',command);self.assertEqual('10001:10001',command[command.index('--user')+1])
            mounts=[command[index+1] for index,part in enumerate(command) if part=='--mount']
            self.assertEqual(1,sum(mount.endswith(',dst=/data') for mount in mounts))
            self.assertFalse(any('/app/file-storage' in mount for mount in mounts))
        self.assertIn(resources.EXTENDED_READINESS_IMAGES['java'],java)
        self.assertTrue(resources.manage(self.value,'cleanup')['independentAbsenceVerified'])


class ActualLoopbackTests(unittest.TestCase):
    def setUp(self):
        """真实本机回环HTTP验证传输，不需要Docker、数据库或外部账号。"""
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup);self.requests=[]
        case=self
        class Handler(http.server.BaseHTTPRequestHandler):
            def do_POST(self):
                """原样捕获一次请求体，不写日志或回显凭据。"""
                data=self.rfile.read(int(self.headers.get('Content-Length','0')))
                case.requests.append((self.path,dict(self.headers),data))
                output=json.dumps({'code':200,'data':'上传成功'},ensure_ascii=False).encode()
                self.send_response(200);self.send_header('Content-Length',str(len(output)));self.end_headers();self.wfile.write(output)
            def do_GET(self):
                """重定向不会继续发到/next，二进制响应不经JSON转换。"""
                case.requests.append((self.path,dict(self.headers),None))
                if self.path.endswith('/redirect'):
                    self.send_response(302);self.send_header('Location','/api/admin/next');self.end_headers();return
                data=extended.FILE_BYTES;self.send_response(200);self.send_header('Content-Length',str(len(data)));self.end_headers();self.wfile.write(data)
            def log_message(self,*args):
                """避免测试请求与认证头写入终端。"""
                return
        self.server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler);threading.Thread(target=self.server.serve_forever,daemon=True).start()
        self.addCleanup(self.server.server_close);self.addCleanup(self.server.shutdown)
        self.exchange=extended.Exchange.__new__(extended.Exchange)
        self.exchange.value={'runId':'a'*32,'ports':{'java':self.server.server_port,'csharp':self.server.server_port}}
        self.exchange.folder=Path(self.temp.name);self.exchange.requests=[]

    def test_json_keeps_null_and_omission_multipart_keeps_file_bytes_no_auth_in_evidence(self):
        """请求体保持显式null，multipart只有一个file字段，证据不含认证头值。"""
        token='b'*64
        self.exchange.request('csharp','json','POST','/api/admin/test',{'id':'9007199254740993','remarks':None},token)
        self.assertEqual({'id':'9007199254740993','remarks':None},json.loads(self.requests[-1][2]))
        self.assertNotIn('name',json.loads(self.requests[-1][2]))
        self.exchange.upload('csharp','upload','9007199254740993',token)
        path,headers,data=self.requests[-1];self.assertIn('/local-upload/9007199254740993',path)
        self.assertEqual(1,data.count(b'Content-Disposition: form-data; name="file"'))
        self.assertEqual(extended.FILE_BYTES,data.split(b'\r\n\r\n',1)[1].rsplit(b'\r\n--',1)[0])
        for file in self.exchange.folder.iterdir():self.assertNotIn(token.encode(),file.read_bytes())
        self.assertEqual(2,len(self.exchange.requests))

    def test_binary_is_byte_identical_and_redirect_is_not_followed(self):
        """实际读取64KiB原字节；302只发一次并失败，禁止凭证随重定向转发。"""
        status,data=self.exchange.raw('java','binary','GET','/api/admin/content',token='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')
        self.assertEqual(200,status);self.assertEqual(extended.FILE_BYTES,data)
        with self.assertRaisesRegex(ValueError,'extended-redirect'):self.exchange.raw('java','redirect','GET','/api/admin/redirect')
        self.assertEqual(['/api/admin/content','/api/admin/redirect'],[entry[0] for entry in self.requests])
        self.assertEqual(1,len(self.exchange.requests))


if __name__=='__main__':unittest.main()
