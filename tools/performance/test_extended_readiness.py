"""新完整夹具的独立就绪用途，只验证初始化／身份编排，不运行真实容器或数据库。"""
import contextlib
import io
import unittest
from unittest import mock
import paired_native_bootstrap as native
import test_paired_native_bootstrap as previous
from test_indexed_write_measurement import receipt


def extended():
    """虚构范围仅供离线回归，不能作为正式资源授权凭据。"""
    value = previous.settings()
    value.update(purpose=native.resources.EXTENDED_READINESS_NATIVE,
                 images=dict(native.resources.EXTENDED_READINESS_IMAGES),
                 relationIndexProfile=native.resources.RELATION_INDEX_PROFILE)
    return value


class ExtendedReadinessContractTests(unittest.TestCase):
    def test_new_images_are_bound_to_readiness_without_sql_capture_or_measurement(self):
        """新镜像／Redis必须成套绑定，旧读写用途不得自动换镜像。"""
        value = extended()
        descriptor = native.validate(value)
        self.assertEqual(native.resources.EXTENDED_READINESS_RESOURCES, descriptor['purpose'])
        self.assertTrue(native.resources.uses_relation_indexes(descriptor))
        self.assertFalse(native.resources.is_observed(descriptor))
        self.assertFalse(native.resources.is_measurement(descriptor))
        self.assertEqual(['--metarill.paired.sql-observation.enabled=false'], native.resources.observation_arguments(descriptor, 'java'))
        self.assertEqual(['--Metarill:Paired:SqlObservation:Enabled=false'], native.resources.observation_arguments(descriptor, 'csharp'))
        self.assertEqual(native.resources.INDEXED_WRITE_IMAGES, native.resources.images_for({'purpose':native.resources.INDEXED_READ_MEASUREMENT_NATIVE}))

    def test_old_image_missing_profile_or_any_replay_budget_is_rejected_before_io(self):
        """未知用途及预算不能借就绪租约开启原业务或性能回放。"""
        value = extended()
        for change in ({'authorizationStatus':'pending'}, {'relationIndexProfile':None},
                       {'images':native.resources.INDEXED_WRITE_IMAGES},
                       {'readReplayRequests':19336}, {'readReplayRequests':None},
                       {'writeReplayRequests':8624}, {'workbookReplayRequests':0},
                       {'purpose':native.resources.INDEXED_READ_MEASUREMENT_NATIVE, 'readReplayRequests':19336}):
            with self.subTest(change=change), mock.patch.object(native, 'verify_tool_sources', side_effect=AssertionError('禁止IO')), self.assertRaises(ValueError):
                native.run({**value, **change})


class ExtendedReadinessFlowTests(previous.FlowFixture):
    def setUp(self):
        """沿真实协调器运行，只在外部边界替换，不将桩结果计为正式通过。"""
        super().setUp()
        self.value = extended()
        self.jobs.side_effect = self.extended_job

    def extended_job(self, root, value, name, state, path, *, expected_index_plan=None):
        """按真实账本登记原六及新增六索引进程，回执仍经实际校验器。"""
        state['jobs'].append({'kind':'container', 'target':'init-'+name,
                              'name':'metarill-perf-'+value['runId']+'-init-'+name,
                              'id':'e'*64, 'containerAbsent':True})
        if name in native.JOBS:
            return previous.FlowFixture.job(self, root, value, name, state, path)
        self.events.append(name)
        side, mode = name.split('-')[2:]
        if mode == 'apply':
            self.assertEqual('e'*64, expected_index_plan)
        return receipt(value, side, mode)

    def test_real_coordinator_upgrades_then_proves_hosts_and_identity_only(self):
        """十二初始化、真实进程就绪、身份和清理顺序保持，不导入测量或采集模块。"""
        with mock.patch.object(native, 'require_database_ownership'), mock.patch.object(native.importlib, 'import_module', side_effect=AssertionError('就绪不加载业务或测量模块')), contextlib.redirect_stdout(io.StringIO()):
            result = native.run(self.value)
        self.assertTrue(result['passed'])
        self.assertFalse(result['performanceAccepted'])
        self.assertFalse(result['releaseAccepted'])
        self.assertEqual('paired-extended-initialization-and-identity-only', result['acceptedScope'])
        self.assertEqual(12, self.jobs.call_count)
        self.assertEqual([], result['cleanupErrors'])
        self.assertNotIn('measurement', result)
        self.assertNotIn('businessFacts', result)
        self.assertLess(self.events.index(native.INDEX_JOBS[-1]), self.events.index('resources-start-hosts'))
        self.assertLess(self.events.index('resources-start-hosts'), self.events.index('resources-wait-hosts'))
        self.assertLess(self.events.index('resources-cleanup'), self.events.index('database-cleanup'))
        self.assertIn('record_request', self.identity.call_args.kwargs)

    def test_index_drift_stops_before_hosts_or_identity_and_cleans(self):
        """任一端索引不完整不得继续启动或登录，不能由另一侧成功掩盖。"""
        original = self.jobs.side_effect
        def broken(*args, **kwargs):
            """只注入最后一端核验漂移，保持其它真实校验器和清理。"""
            result = original(*args, **kwargs)
            if args[2] == 'csharp-index-csharp-verify':
                result['changes'] = native.INDEX_CHANGES
            return result
        self.jobs.side_effect = broken
        with mock.patch.object(native, 'require_database_ownership'), contextlib.redirect_stdout(io.StringIO()):
            result = native.run(self.value)
        self.assertFalse(result['passed'])
        self.identity.assert_not_called()
        self.assertNotIn('resources-start-hosts', self.events)
        self.assertEqual([], result['cleanupErrors'])

    def test_identity_failure_keeps_stage_and_owned_host_diagnostics(self):
        """原身份失败时保留首因位置和本轮宿主留证，仍完整回收后结束。"""
        self.identity.side_effect = RuntimeError('测试身份失败')
        with mock.patch.object(native, 'require_database_ownership'), contextlib.redirect_stdout(io.StringIO()):
            result = native.run(self.value)
        self.assertFalse(result['passed'])
        self.assertIn('真实form密码登录', result['failedAtStage'])
        self.assertIn('resources-snapshot-hosts', self.events)
        self.assertEqual([], result['cleanupErrors'])
        self.assertEqual('ended', result['leaseStatus'])


if __name__ == '__main__':
    unittest.main()