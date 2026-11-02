"""新采集用途的固定镜像／key／原入口及正常停止合同；Docker与权限均为明确离线替身。"""
import copy
import hashlib
import json
import stat
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


if __name__=='__main__':unittest.main()
