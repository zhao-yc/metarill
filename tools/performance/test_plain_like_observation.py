"""普通LIKE完整候选的独立采集用途，覆盖真实编排和外部执行前的拒绝边界。"""
import unittest
from unittest import mock
import paired_native_bootstrap as native
import paired_observed_capture as capture
import test_paired_observed_native as previous
import test_observed_paired_resources as material


def settings():
    """只构造离线合成范围，不代表可执行真实测试资源。"""
    r=native.resources
    return {**previous.value(),'purpose':r.PATTERN_OBSERVED_NATIVE,'images':dict(r.PATTERN_OBSERVED_IMAGES)}


class PlainLikeObservedContractTests(unittest.TestCase):
    def test_candidate_pins_do_not_replace_historical_or_measurement_images(self):
        """当前Data和新镜像绑定新用途，旧采集及所有计时用途保持原内容。"""
        r=native.resources;value=settings();descriptor=native.validate(value)
        self.assertEqual(r.PATTERN_OBSERVED_RESOURCES,descriptor['purpose'])
        self.assertEqual(value['runId'],capture.authorize(value))
        self.assertTrue(r.is_full_observed(value));self.assertFalse(r.is_measurement(value))
        self.assertIn('--Metarill:Paired:SqlObservation:ExpectedDataSha256='+r.PATTERN_OBSERVATION_DATA_SHA256,r.observation_arguments(descriptor,'csharp'))
        self.assertNotEqual(r.FULL_OBSERVED_IMAGES,r.images_for(value))
        old={**previous.value(),'purpose':r.FULL_OBSERVED_NATIVE,'images':dict(r.FULL_OBSERVED_IMAGES)}
        native.validate(old);self.assertEqual(r.FULL_OBSERVATION_DATA_SHA256,r.observation_data_sha(old))
        measured={**previous.value(),'purpose':r.CURRENT_MEASUREMENT_NATIVE,'images':dict(r.CURRENT_MEASUREMENT_IMAGES)}
        self.assertFalse(r.is_observed(measured));self.assertEqual(['--Metarill:Paired:SqlObservation:Enabled=false'],r.observation_arguments(measured,'csharp'))

    def test_pending_foreign_images_and_replay_budgets_refuse_before_source_io(self):
        """旧镜像、结束租约和混入性能预算均在读源、凭据与网络前拒绝。"""
        r=native.resources
        changes=[{'authorizationStatus':'pending'},{'leaseStatus':'ended'},{'images':dict(r.FULL_OBSERVED_IMAGES)},
                 {'purpose':r.FULL_OBSERVED_NATIVE},{'readReplayRequests':19336},{'writeReplayRequests':8624},
                 {'fileReplayRequests':4834,'fileFixtureRequests':18},{'workbookReplayRequests':12}]
        for change in changes:
            with self.subTest(change=change),mock.patch.object(native,'verify_tool_sources',side_effect=AssertionError('禁止读源')),self.assertRaises(ValueError):native.run({**settings(),**change})

    def test_business_role_tokens_remain_bound_to_new_scope_and_original_steps(self):
        """真实业务入口保留三普通角色和原37步，不能借已有明细的额外夹具权限。"""
        r=native.resources;value={**settings(),'purpose':r.PATTERN_OBSERVED_BUSINESS,'leaseStatus':'running'}
        tokens={side:{kind:(str(index)*8+'-'+str(index)*4+'-'+str(index)*4+'-'+str(index)*4+'-'+str(index)*12 if side=='java' else str(index)*64)for index,kind in enumerate(native.seed.KINDS,1)}for side in ('java','csharp')}
        self.assertEqual(value['runId'],previous.business.authorize(value,tokens)[0])
        with self.assertRaises(ValueError):previous.business.authorize(value,tokens,existing_lines=True)
        self.assertEqual(46,len(capture.expected_steps(settings(),'java')))
        self.assertEqual(46,len(capture.expected_steps(settings(),'csharp')))


class PlainLikeObservedFlowTests(unittest.TestCase):
    database=previous.ObservedFlowTests.database
    job=previous.ObservedFlowTests.job
    resources=previous.ObservedFlowTests.resources
    capture=previous.ObservedFlowTests.capture
    run_scope=previous.ObservedFlowTests.run_scope

    def setUp(self):
        """外部、业务与采集均为显式替身，实际执行当前原生协调器。"""
        previous.ObservedFlowTests.setUp(self);self.value=settings()

    def run_business(self,lease,tokens,request,query):
        """编排应在六个原初始化完成后传入新业务用途及同一镜像。"""
        self.events.append('business');self.assertEqual(native.resources.PATTERN_OBSERVED_BUSINESS,lease['purpose'])
        self.assertEqual(self.value['images'],lease['images']);self.assertEqual(6,self.jobs.call_count)
        return {'mockedOnly':True,'sqlSemanticAccepted':False}

    def test_graceful_capture_and_cleanup_do_not_issue_performance_acceptance(self):
        """正常停止先于采集，采集先于清理；诊断不能生成性能或发布通过。"""
        result=self.run_scope();self.assertTrue(result['passed']);self.assertFalse(result['performanceAccepted']);self.assertFalse(result['releaseAccepted'])
        self.assertEqual('ended',result['leaseStatus'])
        for first,second in [('resources-wait-hosts','business'),('business','resources-stop-hosts'),('resources-stop-hosts','capture'),('capture','resources-cleanup'),('resources-cleanup','database-cleanup')]:self.assertLess(self.events.index(first),self.events.index(second))
        self.assertEqual(native.resources.PATTERN_OBSERVED_RESOURCES,native.resources.manage.call_args_list[0].args[0]['purpose'])


class PlainLikeObservedMaterialTests(unittest.TestCase):
    materialize=material.ObservedResourceTests.materialize
    tearDown=material.ObservedResourceTests.tearDown
    refresh=material.ObservedResourceTests.refresh
    key=material.ObservedResourceTests.key

    def setUp(self):
        """实际材料解析与容器参数生成，仅替换Docker与权限边界。"""
        material.ObservedResourceTests.setUp(self);self.value.update(purpose=native.resources.PATTERN_OBSERVED_RESOURCES,images=dict(native.resources.PATTERN_OBSERVED_IMAGES))

    def test_actual_reader_and_container_arguments_bind_new_data_and_dependencies(self):
        """只有C#注册补充deps，实际启动参数必须携带当前固定Data摘要。"""
        r=native.resources;r.read_materials(self.root,self.value);r.manage(self.value,'create')
        commands=[c for c in self.engine.calls if c[:2]==('container','create')];self.assertEqual(3,len(commands))
        for side,command in zip(('redis','java','csharp'),commands):
            self.assertIn(r.PATTERN_OBSERVED_IMAGES[side],command)
            self.assertEqual(side=='csharp','DOTNET_ADDITIONAL_DEPS=/app/Metarill.Paired.SqlObservation.deps.json' in command)
            if side=='csharp':self.assertIn('--Metarill:Paired:SqlObservation:ExpectedDataSha256='+r.PATTERN_OBSERVATION_DATA_SHA256,command)

    def test_redis_target_or_database_change_refuses_before_docker(self):
        """自报材料摘要不能放宽原真实Redis范围或数据库编号。"""
        r=native.resources;original=self.inputs['csharp.env'];before=len(self.engine.calls)
        for changed in [original.replace('defaultDatabase=0','defaultDatabase=1'),original.replace('redis:6379','outside:6379')]:
            self.inputs['csharp.env']=changed;self.materialize();self.refresh()
            with self.assertRaises(ValueError):r.read_materials(self.root,self.value)
        self.assertEqual(before,len(self.engine.calls))

if __name__=='__main__':unittest.main()