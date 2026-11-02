"""新采集用途接回原生链的顺序／来源／失败清理回归；外部执行与业务/采集模块均显式替身。"""
import contextlib
import hashlib
import io
import http.server
import threading
import subprocess
import json
from pathlib import Path
import unittest
from unittest import mock
import paired_native_bootstrap as subject
import paired_business_http as business
import paired_observed_capture as capture
import test_paired_native_bootstrap as fixtures


def value():
    """合成范围只用于离线流程，不代表真实新双库授权。"""
    return {**fixtures.settings(),'purpose':'paired-native-observed-v1','images':dict(subject.resources.OBSERVATION_IMAGES)}


class ObservedFlowTests(unittest.TestCase):
    database=fixtures.FlowFixture.database
    job=fixtures.FlowFixture.job

    def setUp(self):
        """复用原外部替身但不继承/重复已有测试，显式标注采集与业务也是替身。"""
        fixtures.FlowFixture.setUp(self);self.value=value();self.ex=mock.Mock();self.ex.receipt.return_value={'mockedOnly':True};self.enterContext(mock.patch.object(capture,'Exchange',return_value=self.ex))
        self.identity.return_value=([{'mockedOnly':True}],{(target,kind):target+'-'+kind for target in ('java','csharp') for kind in subject.seed.KINDS})
        self.business=self.enterContext(mock.patch.object(business,'run_basic',side_effect=self.run_business));self.saved=self.enterContext(mock.patch.object(capture,'capture_for_review',side_effect=self.capture))
        self.limits=subject.http.equal_limits

    def resources(self,value,operation):
        """保持真实状态转移所需字段，模拟停止必须早于采集及DROP。"""
        self.events.append('resources-'+operation);path=self.root/'resource-state.json'
        if operation=='create':state=dict(runId=value['runId'],ownerToken=value['ownerToken'],status='created',hostsStarted=False,items=[])
        else:
            state=json.loads(path.read_text(encoding='utf-8'));state['status']={'start-hosts':'started','wait-hosts':'started','snapshot-hosts':'started','stop-hosts':'stopped','cleanup':'ended'}[operation]
            if operation=='start-hosts':state['hostsStarted']=True
        path.write_text(json.dumps(state),encoding='utf-8');return {'mockedOnly':True}

    def run_business(self,lease,tokens,request,query):
        """验证旧flat登录令牌被正确分组，固定用途和原六初始化都已完成。"""
        self.events.append('business');self.assertEqual('paired-observed-business-v1',lease['purpose']);self.assertEqual({'java','csharp'},set(tokens));self.assertEqual(set(subject.seed.KINDS),set(tokens['java']));self.assertEqual('java-writer',tokens['java']['writer']);self.assertEqual(6,self.jobs.call_count)
        return {'mockedOnly':True,'sqlSemanticAccepted':False}

    def capture(self,root,scope,exchange,graceful):
        """正常停止或已强制清理后才能保留，强杀原始文件不能变成通过。"""
        self.events.append('capture');state=json.loads((root/'resource-state.json').read_text(encoding='utf-8'));self.assertEqual('stopped' if graceful else 'ended',state['status'])
        if not graceful:raise ValueError('capture-unfinalized-exit')
        return {'mockedOnly':True,'sqlSemanticAccepted':False,'performanceAccepted':False}

    def run_scope(self):
        """运行实际编排逻辑，外部和验收模块边界的替身在setUp中明确列出。"""
        with contextlib.redirect_stdout(io.StringIO()):return subject.run(self.value)

    def test_full_observed_order_stops_captures_then_cleans_and_never_accepts_performance(self):
        """原六步→真实登录分支→业务→停止→采集→资源→数据库清理顺序不可颠倒。"""
        result=self.run_scope();self.assertTrue(result['passed']);self.assertEqual('paired-original-business-and-private-capture-for-review-only',result['acceptedScope']);self.assertFalse(result['performanceAccepted']);self.assertFalse(result['releaseAccepted']);self.assertEqual('ended',result['leaseStatus'])
        for first,second in (('resources-wait-hosts','business'),('business','resources-snapshot-hosts'),('resources-snapshot-hosts','resources-stop-hosts'),('resources-stop-hosts','capture'),('capture','resources-cleanup'),('resources-cleanup','database-cleanup')):self.assertLess(self.events.index(first),self.events.index(second))
        targets=self.limits.call_args.args[1];self.assertEqual(subject.resources.OBSERVATION_IMAGES['java'],targets['java']['expectedImageId']);self.assertEqual(subject.resources.OBSERVATION_IMAGES['csharp'],targets['csharp']['expectedImageId'])

    def test_business_failure_keeps_original_stage_and_still_preserves_capture(self):
        """业务失败不因关闭/清理成功改写，首因阶段在随后采集前保留。"""
        self.business.side_effect=ValueError('business-copy-id');result=self.run_scope();self.assertFalse(result['passed']);self.assertEqual('business-copy-id',result['failure']['code']);self.assertIn('普通账号',result['failedAtStage']);self.assertEqual([],result['cleanupErrors']);self.assertIn('capture',self.events)

    def test_invalid_capture_cannot_pass_only_because_business_succeeded(self):
        """两端业务结果成功仍要求真正关闭且结构/覆盖复核成功。"""
        self.saved.side_effect=ValueError('capture-request-count');result=self.run_scope();self.assertFalse(result['passed']);self.assertEqual('capture-request-count',result['captureError']['code']);self.assertEqual([],result['cleanupErrors'])

    def test_failed_graceful_stop_forces_owned_cleanup_before_saving_raw_and_is_failure(self):
        """停止失败只能保留未认可原始副本，已清理资源后才可DROP。"""
        original=self.resources
        def failed(scope,operation):
            if operation=='stop-hosts':self.events.append('resources-stop-hosts');raise RuntimeError('模拟停止失败')
            return original(scope,operation)
        subject.resources.manage.side_effect=failed;result=self.run_scope();self.assertFalse(result['passed']);self.assertEqual('capture-unfinalized-exit',result['captureError']['code']);self.assertLess(self.events.index('resources-cleanup'),self.events.index('capture'));self.assertEqual([],result['cleanupErrors'])


class SourceAndPendingTests(unittest.TestCase):
    def test_pending_or_wrong_images_do_not_import_new_modules_or_touch_files(self):
        """批准前和旧新镜像混用都不能先导入未核对模块或读取私有配置。"""
        for changes in ({'authorizationStatus':'pending'},{'leaseStatus':'ended'},{'version':True},{'images':dict(subject.resources.IMAGES)}):
            with self.subTest(changes=changes),mock.patch.object(subject.importlib,'import_module',side_effect=AssertionError('不能导入')),mock.patch.object(subject,'verify_tool_sources',side_effect=AssertionError('不能读源码')),self.assertRaises(ValueError):subject.run({**value(),**changes})

    def test_new_source_gate_requires_all_actual_eight_files(self):
        """新用途八文件缺一或任一漂移都拒绝，旧五文件清单不能充当新用途来源。"""
        scope=value();root=Path(subject.__file__).resolve().parents[1];names=('performance/paired_native_bootstrap.py','performance/paired_http.py','performance/paired_identity_seed.py','performance/server200_paired_resources.py','acceptance/server200-fresh-mysql.py','performance/paired_business_http.py','performance/paired_observed_capture.py','performance/audit_sql_artifacts.py');scope.update(toolSourceCommit='a'*40,toolSha256={name:hashlib.sha256((root/name).read_bytes()).hexdigest() for name in names});subject.verify_tool_sources(scope)
        scope['toolSha256'].pop(names[-1])
        with self.assertRaises(ValueError):subject.verify_tool_sources(scope)


class IdentityTests(unittest.TestCase):
    reply=fixtures.IdentityHttpTests.reply

    @classmethod
    def setUpClass(cls):
        """复用原三角色假登录响应，密码与会话均未连接实际宿主。"""
        fixtures.IdentityHttpTests.setUpClass.__func__(cls)

    def test_observed_identity_uses_exact_nine_steps_per_side_and_no_health_http(self):
        """新用途只TCP等就绪，每端九请求都由真实Exchange分配唯一标记。"""
        scope=value()
        def sender(port,method,path,body=None,token=None,form=False,sample=None):
            self.assertRegex(sample or '',r'^[a-f0-9]{32}$');return self.reply(port,method,path,body,token,form)
        exchange=capture.Exchange(scope,sender)
        with mock.patch.object(capture,'wait_ready') as ready:result,tokens=subject.identity_http(scope,self.fixture,self.creds,exchange)
        self.assertEqual(6,len(result));self.assertEqual(6,len(tokens));self.assertEqual(2,ready.call_count);self.assertEqual(9,len(exchange.finished['java']));self.assertEqual(9,len(exchange.finished['csharp']))
        self.assertEqual(tuple('identity:'+name for name in capture.IDENTITY_STEPS),tuple(row['step'] for row in exchange.finished['java']))


class OriginalInitializationTests(unittest.TestCase):
    """实际新镜像命令构造仍调用原初始化，采集只在正式宿主启用。"""
    def setUp(self):
        fixtures.JobTests.setUp(self);self.value=value()

    def docker(self,*args):
        """内存引擎按新用途返回真实预定镜像ID，不连接Docker。"""
        if args[:2]==('container','inspect') and args[args.index('--format')+1]=='{{.Image}}':return subject.resources.images_for(self.value)['java' if 'init-java' in args[-1] else 'csharp']
        return fixtures.JobTests.docker(self,*args)

    def test_original_java_and_csharp_initializers_use_candidate_images_without_observation(self):
        """两端原入口及计划不变，初始化不产生伪HTTP采样或读取采集key。"""
        for target,name in (('java','java-apply'),('csharp','csharp-preview')):
            self.capture.return_value=subprocess.CompletedProcess([],0,'结果：{"applied":true,"verifiedTables":22}\n'.encode() if target=='java' else b'{"changes":[]}',b'')
            subject.initialization_job(self.root,self.value,name,self.state,self.path)
            command=[c for c in self.engine.calls if c[:2]==('container','create')][-1]
            self.assertIn(subject.resources.OBSERVATION_IMAGES[target],command)
            self.assertNotIn('--metarill.paired.sql-observation.enabled=true',command)
            self.assertFalse(any('observation.key' in arg or 'HOSTINGSTARTUP' in arg for arg in command))
            self.assertIn('org.springframework.boot.loader.launch.PropertiesLauncher' if target=='java' else '--schema-preview',command)
        self.assertFalse(self.engine.objects)


class ActualSampleHeaderTests(unittest.TestCase):
    def test_real_loopback_sends_exact_header_and_rejects_invalid_sample_before_io(self):
        """真实HTTP传输不把采样值写入URL或JSON，也不让无效标签发出请求。"""
        seen=[]
        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self,*args):pass
            def do_POST(self):
                seen.append((self.path,self.headers.get('X-Metarill-Sql-Sample'),self.rfile.read(int(self.headers['Content-Length']))))
                self.send_response(200);self.end_headers();self.wfile.write(b'{"code":200}')
        server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler);thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
        try:
            marker='a'*32
            self.assertEqual((200,{'code':200}),subject.send(server.server_port,'POST','/only-test',{'name':'测试'},sample=marker))
            self.assertEqual('/only-test',seen[0][0]);self.assertEqual(marker,seen[0][1]);self.assertEqual({'name':'测试'},json.loads(seen[0][2]))
            for invalid in ('x'*32,'a'*31,True,marker+'\r\nother:bad'):
                with self.assertRaises(ValueError):subject.send(server.server_port,'POST','/only-test',{},sample=invalid)
            self.assertEqual(1,len(seen))
        finally:server.shutdown();server.server_close();thread.join()


if __name__=='__main__':unittest.main()
