"""新采集用途的固定镜像／key／原入口及正常停止合同；Docker与权限均为明确离线替身。"""
import copy
import hashlib
import json
import stat
import subprocess
import socket
from pathlib import Path
import unittest
from unittest import mock
import server200_paired_resources as subject
import test_server200_paired_resources as fixtures


class ObservedEngine(fixtures.Engine):
    """只模拟创建／停止和退出观察，不启动或连接任何真实服务。"""
    def __init__(self):
        super().__init__();self.exit_code=0;self.running=False;self.java_sigterm=False

    def __call__(self,*args):
        if args[:2]==('container','stop'):
            self.calls.append(args);return args[-1]
        if args[:2]==('container','inspect') and '"pid"' in args[args.index('--format')+1]:
            self.calls.append(args);return json.dumps(dict(running=True,pid=1234))
        if args[:2]==('container','inspect') and 'exitCode' in args[args.index('--format')+1]:
            self.calls.append(args);return json.dumps(dict(running=self.running,exitCode=143 if self.java_sigterm and args[-1].endswith('-java') else self.exit_code,oomKilled=False,error=''))
        return super().__call__(*args)


class ObservedResourceTests(unittest.TestCase):
    materialize=fixtures.Fixture.materialize
    tearDown=fixtures.Fixture.tearDown

    def setUp(self):
        """复用原三配置假值，独立新增一个32字节二进制key，不继承或重复原测试方法。"""
        fixtures.Fixture.setUp(self);self.value['purpose']='paired-observed-resources-v1';self.value['images']=dict(subject.OBSERVATION_IMAGES);self.engine=ObservedEngine()
        self.enterContext(mock.patch.object(subject,'docker',side_effect=self.engine))
        self.enterContext(mock.patch.object(subject,'regular',side_effect=lambda p,directory=False:type('Info',(),{'st_mode':(stat.S_IFDIR|0o700) if directory else (stat.S_IFREG|(0o444 if p.name in {'redis.conf','observation.key'} else 0o600))})()))
        self.key(bytes(range(224,256)))

    def key(self,data):
        """更新合成key及摘要，证明仅改摘要也不能绕过长度／用途白名单。"""
        path=self.root/'observation.key'
        if path.exists():path.chmod(0o600)
        path.write_bytes(data);path.chmod(0o444);self.refresh()

    def refresh(self):
        """冻结四输入清单；测试不会生成真实密钥或向外写入材料。"""
        manifest={name:hashlib.sha256((self.root/name).read_bytes()).hexdigest() for name in (*self.inputs,'observation.key')}
        raw=json.dumps(manifest).encode();(self.root/'materials.json').write_bytes(raw);self.value['materialsSha256']=hashlib.sha256(raw).hexdigest()

    def start(self):
        """显式模拟初始化已核验后才允许启动宿主。"""
        subject.manage(self.value,'create');subject.manage({**self.value,'databaseInitializationVerified':True},'start-hosts')

    def test_new_and_old_profile_images_cannot_be_interchanged(self):
        """未批准、旧用途接新镜像或新用途接旧镜像都在任何文件前拒绝。"""
        for changes in ({'authorizationStatus':'pending'},{'leaseStatus':'ended'},{'purpose':'paired-resources-v1'},{'images':dict(subject.IMAGES)}):
            value={**self.value,**changes}
            with self.subTest(changes=changes),mock.patch.object(subject,'root_for',side_effect=AssertionError('不读文件')),self.assertRaises(ValueError):subject.manage(value,'create')
        self.assertFalse(self.engine.calls)

    def test_fourth_key_file_is_required_and_binary_not_utf8(self):
        """合法非UTF8 key可以读取；长度错误即使重写摘要也拒绝。"""
        subject.read_materials(self.root,self.value)
        for data in (b'',b'a'*31,b'a'*33):
            self.key(data)
            with self.subTest(size=len(data)),self.assertRaises(ValueError):subject.manage(self.value,'create')
        self.assertFalse(self.engine.calls)

    def test_legacy_profile_rejects_observation_key_even_with_new_manifest(self):
        """给旧用途多传key不能默默启用采集。"""
        value={**self.value,'purpose':'paired-resources-v1','images':dict(subject.IMAGES)}
        with self.assertRaises(ValueError):subject.manage(value,'create')
        self.assertFalse(self.engine.calls)

    def test_create_uses_fixed_original_entrypoints_args_and_separate_roots(self):
        """只给新用途两宿主加固定路径配置，HMAC原值和数据库密码都不进命令参数。"""
        subject.manage(self.value,'create');commands=[call for call in self.engine.calls if call[:2]==('container','create')];self.assertEqual(3,len(commands))
        for target,command in zip(('redis','java','csharp'),commands):
            self.assertNotIn('--entrypoint',command)
            if target=='redis':self.assertNotIn('/run/metarill-observation.key',' '.join(command));continue
            offset=command.index(subject.OBSERVATION_IMAGES[target]);self.assertEqual(command[offset+1:],tuple(subject.observation_arguments(self.value,target)));self.assertIn('src='+str(self.root)+'/observation.key,dst=/run/metarill-observation.key,readonly',' '.join(command))
            owner=self.root/('data-'+target)/'sql-observation/.metarill-paired-observation-owner';self.assertEqual(self.value['runId'],owner.read_text(encoding='utf-8'))
            self.assertNotIn('1'*64,' '.join(command));self.assertNotIn(bytes(range(224,256)).hex(),' '.join(command))
        self.assertNotIn('ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=Metarill.Paired.SqlObservation',commands[1]);self.assertIn('ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=Metarill.Paired.SqlObservation',commands[2])
        self.assertEqual(['metarill-perf-'+self.value['runId']+'-redis'],[call[-1] for call in self.engine.calls if call[:2]==('container','start')])

    def test_client_cannot_override_fixed_capture_target_or_data_sha(self):
        """额外客户端字段不进入固定库名／程序集摘要或命令预算。"""
        value={**self.value,'ExpectedDatabases':'existing','ExpectedDataSha256':'f'*64,'maximum-commands':'999999999'}
        args=subject.observation_arguments(value,'csharp');self.assertIn('--Metarill:Paired:SqlObservation:ExpectedDatabases=metarill_fresh_'+self.value['runId'][:16]+'_paired_csharp',args);self.assertIn('--Metarill:Paired:SqlObservation:ExpectedDataSha256='+subject.OBSERVATION_DATA_SHA256,args);self.assertNotIn('999999999',' '.join(args))

    def test_graceful_stop_is_once_and_all_ownership_precedes_stop(self):
        """正常关闭必须两端退出0，重复关闭不能重放；之后仍由原流程清理。"""
        self.start();result=subject.manage(self.value,'stop-hosts');self.assertTrue(result['hostsStoppedGracefully']);self.assertEqual('stopped',json.loads((self.root/'resource-state.json').read_text(encoding='utf-8'))['status']);self.assertEqual(2,len([c for c in self.engine.calls if c[:2]==('container','stop')]))
        with self.assertRaises(ValueError):subject.manage(self.value,'stop-hosts')
        self.assertTrue(subject.manage(self.value,'cleanup')['resourcesCleaned'])

    def test_failed_or_malformed_exit_is_not_graceful_and_not_replayed(self):
        """退出137、仍运行或布尔冒充0都不得作为正常关闭。"""
        self.start();self.engine.exit_code=False
        with self.assertRaises(RuntimeError):subject.manage(self.value,'stop-hosts')
        count=len(self.engine.calls)
        with self.assertRaises(ValueError):subject.manage(self.value,'stop-hosts')
        self.assertEqual(count,len(self.engine.calls));self.assertTrue(subject.manage(self.value,'cleanup')['resourcesCleaned'])

    def test_original_jvm_sigterm_143_is_not_confused_with_forced_kill(self):
        """JDK原信号退出143只对Java允许；两端137或C#143仍拒绝。"""
        self.start();self.engine.java_sigterm=True;self.assertTrue(subject.manage(self.value,'stop-hosts')['hostsStoppedGracefully'])

    def test_forced_137_and_still_running_are_rejected(self):
        """强杀137不能伪装正常关闭，停止状态持久保留以防重放。"""
        self.start();self.engine.exit_code=137
        with self.assertRaises(RuntimeError):subject.manage(self.value,'stop-hosts')
        self.assertEqual('stopping',json.loads((self.root/'resource-state.json').read_text(encoding='utf-8'))['status'])

    def test_csharp_exit143_and_running_true_are_not_graceful(self):
        """143只适用于原JVM信号约定，C#同码必须拒绝。"""
        self.start();self.engine.exit_code=143
        with self.assertRaises(RuntimeError):subject.manage(self.value,'stop-hosts')

    def test_running_after_stop_does_not_become_a_closed_capture(self):
        """停止命令返回不代表进程已退出，必须重新观察Running=false。"""
        self.start();self.engine.running=True
        with self.assertRaises(RuntimeError):subject.manage(self.value,'stop-hosts')

    def test_missing_observation_key_in_manifest_rejects_before_docker(self):
        """新用途不能只给旧三配置后悄悄使用其它key。"""
        manifest=json.loads((self.root/'materials.json').read_text(encoding='utf-8'));manifest.pop('observation.key');raw=json.dumps(manifest).encode();(self.root/'materials.json').write_bytes(raw);self.value['materialsSha256']=hashlib.sha256(raw).hexdigest()
        with self.assertRaises(ValueError):subject.manage(self.value,'create')
        self.assertFalse(self.engine.calls)

    def test_foreign_replacement_refuses_before_any_stop(self):
        """同名资源被替换时，不能先停另一台本轮宿主。"""
        self.start();key=('container','metarill-perf-'+self.value['runId']+'-csharp');self.engine.objects[key][2]='foreign'
        with self.assertRaises(RuntimeError):subject.manage(self.value,'stop-hosts')
        self.assertFalse(any(call[:2]==('container','stop') for call in self.engine.calls))

    def test_stop_before_start_is_rejected(self):
        """创建和初始化阶段不能假造一个已finalized的请求采集。"""
        subject.manage(self.value,'create')
        with self.assertRaises(ValueError):subject.manage(self.value,'stop-hosts')
        self.assertFalse(any(call[:2]==('container','stop') for call in self.engine.calls))


    def ready_indices(self):
        """合成索引仅用于门槛测试，不代表真实应用已启动。"""
        for target in ('java','csharp'):
            folder=self.root/('data-'+target)/'sql-observation'/'instance';folder.mkdir()
            value={'runId':self.value['runId'],'sourcesVerified':True} if target=='java' else {'runId':self.value['runId'],'dataAssemblySha256':subject.OBSERVATION_DATA_SHA256,'expectedDatabases':['metarill_fresh_'+self.value['runId'][:16]+'_paired_csharp']}
            (folder/'index.json').write_text(json.dumps(value),encoding='utf-8')
            if target=='java':(folder.parent/'.metarill-paired-application-ready').write_text(json.dumps({'format':'metarill-paired-java-ready-v1','runId':self.value['runId'],'instance':folder.name}),encoding='utf-8')

    def test_wait_requires_actual_process_listener_even_when_indexes_exist(self):
        """发布端口可连不算就绪；内核监听未出现时必须等待且不发HTTP。"""
        self.start();self.ready_indices()
        with mock.patch.object(subject,'process_listening',side_effect=[False,True,True,True]) as listeners,mock.patch.object(subject.time,'sleep') as sleep:
            result=subject.manage(self.value,'wait-hosts')
        self.assertEqual(4,listeners.call_count);sleep.assert_called_once();self.assertEqual(0,result['httpRequestsSent']);self.assertTrue(result['originalProcessListenersVerified'])

    def test_java_listener_and_index_cannot_replace_application_ready_event(self):
        """原Boot监听后仍可能执行ApplicationRunner，缺标记时不发首个HTTP。"""
        self.start();self.ready_indices();(self.root/'data-java/sql-observation/.metarill-paired-application-ready').unlink()
        with mock.patch.object(subject,'process_listening',return_value=True),mock.patch.object(subject.time,'monotonic',side_effect=[0,91]),self.assertRaisesRegex(RuntimeError,'采集索引未就绪'):subject.manage(self.value,'wait-hosts')

    def test_listener_without_observation_index_cannot_pass(self):
        """真实端口已监听但采集配置尚未建立时仍拒绝，不能造成零记录假通过。"""
        self.start()
        with mock.patch.object(subject,'process_listening',return_value=True),mock.patch.object(subject.time,'monotonic',side_effect=[0,91]),self.assertRaisesRegex(RuntimeError,'采集索引未就绪'):subject.manage(self.value,'wait-hosts')

    def test_foreign_wait_refuses_before_proc_reads(self):
        """外来容器身份在读取PID网络状态前就拒绝。"""
        self.start();self.engine.objects['container','metarill-perf-'+self.value['runId']+'-java'][2]='foreign'
        with mock.patch.object(subject,'process_listening',side_effect=AssertionError('不得读取proc')),self.assertRaises(RuntimeError):subject.manage(self.value,'wait-hosts')

    def test_snapshot_keeps_both_private_streams_before_deletion(self):
        """日志只保存本轮私有目录，stderr首因不能因原docker包装只返stdout而丢失。"""
        self.start()
        with mock.patch.object(subject,'verify_server'),mock.patch.object(subject.subprocess,'run',return_value=subprocess.CompletedProcess([],0,b'private-out',b'private-error')) as run:
            result=subject.manage(self.value,'snapshot-hosts')
        self.assertTrue(result['privateLogsSaved']);self.assertEqual(2,run.call_count)
        for target in ('java','csharp'):self.assertEqual(b'private-error',(self.root/('host-'+target+'.stderr.log')).read_bytes())
        self.assertFalse(any(call[:2]==('container','rm') for call in self.engine.calls))
        self.assertTrue(all(call.args[0][:3]==['docker','-H','unix:///var/run/docker.sock'] for call in run.call_args_list))

    def test_snapshot_checks_all_owners_before_first_log_call(self):
        """第二端归属不符时，不先读取第一端日志。"""
        self.start();self.engine.objects['container','metarill-perf-'+self.value['runId']+'-csharp'][2]='foreign'
        with mock.patch.object(subject.subprocess,'run',side_effect=AssertionError('不得读日志')),self.assertRaises(RuntimeError):subject.manage(self.value,'snapshot-hosts')



class ListenerParserTests(unittest.TestCase):
    def test_kernel_state_and_port_must_both_match(self):
        """ESTABLISHED／其它端口和仅Docker发布回执都不是内核LISTEN。"""
        header='sl local_address rem_address st\n'
        self.assertTrue(subject.has_listener(header+'0: 00000000:1F90 00000000:0000 0A rest'))
        for row in ('0: 00000000:1F90 00000000:0000 01 rest','0: 00000000:1F91 00000000:0000 0A rest','127.0.0.1:28171->8080/tcp'):
            self.assertFalse(subject.has_listener(header+row))

    @unittest.skipUnless(Path('/proc/self/net/tcp').is_file(),'Windows没有Linux进程TCP表，真实内核监听另在Linux验证')
    def test_actual_linux_listener_appears_and_disappears(self):
        """真正打开本进程临时监听，不启动Docker／HTTP或访问外网。"""
        with socket.socket() as listener:
            listener.bind(('127.0.0.1',0));listener.listen();port=listener.getsockname()[1]
            self.assertTrue(subject.has_listener(Path('/proc/self/net/tcp').read_text(),port))
        self.assertFalse(subject.has_listener(Path('/proc/self/net/tcp').read_text(),port))


if __name__=='__main__':unittest.main()
