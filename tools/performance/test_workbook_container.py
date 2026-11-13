"""工作簿容器外部边界合同；Docker由桩替换，实际文件引擎另有真实Node与200实测。"""
import base64
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import types
import unittest
from unittest import mock
import paired_workbook_container as oracle

RUN = 'a' * 32
OWNER = 'b' * 64


class WorkbookContainerTests(unittest.TestCase):
    def setUp(self):
        """创建临时公开代码输入和归属账本，不启动本机Docker。"""
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.path = self.root / 'native-state.json'
        self.folder = self.root / 'oracle'
        self.folder.mkdir()
        self.script = self.folder / 'paired_workbook_oracle.cjs'
        self.script.write_bytes(Path(oracle.__file__).with_name(self.script.name).read_bytes())
        self.library = self.folder / 'xlsx.full.min.js'
        self.library.write_bytes(b'test library boundary only')
        self.checksum = hashlib.sha256(self.library.read_bytes()).hexdigest()
        for path in (self.script, self.library):
            path.chmod(0o444)
            self.addCleanup(path.chmod, 0o666)
        self.state = dict(runId=RUN, ownerToken=OWNER, leaseStatus='running', jobs=[])
        self.value = oracle.descriptor(RUN)
        self.value['librarySha256'] = self.checksum
        self.request = dict(version=1, operation='create', headers=['合同名称', '合同附件'], rows=[['合同甲', '["9007199254740993"]']])
        self.registry = {}
        self.commands = []
        self.bytes = b'PK\x03\x04mock-external-output'
        self.output = dict(format='xlsx', byteLength=len(self.bytes), sha256=hashlib.sha256(self.bytes).hexdigest(),
                           sheetNames=['sheet1'], headers=self.request['headers'], rows=[dict(zip(self.request['headers'], self.request['rows'][0]))],
                           cells=[self.request['headers'], *self.request['rows']], sheetJsVersion='0.20.3', nodeVersion='v24.14.0',
                           workbookBase64=base64.b64encode(self.bytes).decode())
        self.start = mock.Mock(side_effect=lambda *args: types.SimpleNamespace(returncode=0, stderr=b'', stdout=json.dumps(self.output, ensure_ascii=False).encode()))
        patches = [mock.patch.object(oracle, 'LIBRARY_SHA256', self.checksum),
                   mock.patch.object(oracle.resources, 'root_for', return_value=self.root),
                   mock.patch.object(oracle.resources, 'regular', side_effect=lambda path, directory=False: path.stat()),
                   mock.patch.object(oracle.resources, 'inspect', side_effect=lambda kind, name: self.registry.get(name)),
                   mock.patch.object(oracle.resources, 'docker', side_effect=self.docker),
                   mock.patch.object(oracle, 'attached_start', self.start)]
        for patch in patches:
            patch.start()
            self.addCleanup(patch.stop)

    def docker(self, *args):
        """仅在模拟外部命令时核对归属已先落盘，失败仍按实际finally回收。"""
        self.commands.append(args)
        if args[:2] == ('image', 'inspect'):
            return oracle.NODE_IMAGE
        if args[:2] == ('container', 'create'):
            name = args[args.index('--name') + 1]
            ledger = json.loads(self.path.read_bytes())
            self.assertEqual(name, ledger['jobs'][-1]['name'])
            self.assertNotIn('id', ledger['jobs'][-1])
            target = name.rsplit('-', 2)[1:]
            self.registry[name] = ['c' * 64, RUN, OWNER, '-'.join(target)]
            return 'c' * 64
        if args[:3] == ('container', 'rm', '--force'):
            self.registry.pop(args[3])
            return args[3]
        self.fail('测试出现未声明外部命令')

    def invoke(self):
        """复用真实执行路径，仅替换进程和Docker外部边界。"""
        return oracle.invoke(self.value, self.request, self.root, self.state, self.path)

    def test_sealed_process_gets_only_two_public_files_and_is_removed(self):
        """实际参数不得含环境、端口、数据库根或网络，输出按输入格逐项核。"""
        result = self.invoke()
        command = next(cmd for cmd in self.commands if cmd[:2] == ('container', 'create'))
        for flag, expected in (('--network', 'none'), ('--user', '10001:10001'), ('--cap-drop', 'ALL'),
                               ('--memory', '268435456'), ('--memory-swap', '268435456'), ('--log-driver', 'none'),
                               ('--entrypoint', 'node'), ('--pids-limit', '64')):
            self.assertEqual(expected, command[command.index(flag) + 1])
        self.assertIn('--read-only', command)
        for flag in ('--env', '--env-file', '--publish', '--privileged', '--tmpfs'):
            self.assertNotIn(flag, command)
        mounts = [command[i + 1] for i, part in enumerate(command) if part == '--mount']
        self.assertEqual([f'type=bind,src={self.script},dst=/oracle/paired_workbook_oracle.cjs,readonly',
                          f'type=bind,src={self.library},dst=/oracle/xlsx.full.min.js,readonly'], mounts)
        self.assertEqual(self.request, json.loads(self.start.call_args.args[1]))
        self.assertEqual(result['sha256'], self.state['jobs'][0]['workbook']['fileSha256'])
        self.assertTrue(self.state['jobs'][0]['containerAbsent'])
        self.assertFalse(self.registry)

    def test_scope_owner_budget_or_live_previous_job_fails_before_files_and_docker(self):
        """错误租约／归属／自由路径／超量请求均不进入外部边界。"""
        cases = [('lease', lambda: self.value.update(authorizationStatus='pending')),
                 ('image', lambda: self.value.update(nodeImage='sha256:' + 'e' * 64)),
                 ('owner', lambda: self.state.update(ownerToken='foreign')),
                 ('root', lambda: setattr(self, 'path', self.root / 'other.json')),
                 ('version', lambda: self.request.update(version=True)),
                 ('size', lambda: self.request.update(rows=[['x' * (oracle.MAX_INPUT + 1), '']])),
                 ('live', lambda: self.state['jobs'].append(dict(target='oracle-01', containerAbsent=False))),
                 ('limit', lambda: self.state['jobs'].extend(dict(target='oracle-' + str(i), containerAbsent=True) for i in range(oracle.MAX_JOBS)))]
        for name, change in cases:
            with self.subTest(name=name):
                self.value = oracle.descriptor(RUN)
                self.state = dict(runId=RUN, ownerToken=OWNER, leaseStatus='running', jobs=[])
                self.request = dict(version=1, operation='create', headers=['名称'], rows=[['甲']])
                self.path = self.root / 'native-state.json'
                change()
                with mock.patch.object(oracle, 'read_inputs', side_effect=AssertionError('不允许读文件')), self.assertRaises(ValueError):
                    self.invoke()
        self.assertEqual([], self.commands)

    def test_read_batch_binds_every_output_to_its_input_and_closes_container(self):
        """双文件摘要按顺序逐个核验，缺项和交换回执不能通过。"""
        outputs = []
        requests = []
        for raw in (self.bytes, self.bytes + b'2'):
            checksum = hashlib.sha256(raw).hexdigest()
            requests.append(dict(version=1, operation='read', base64=base64.b64encode(raw).decode(), expectedSha256=checksum))
            outputs.append({**{k: v for k, v in self.output.items() if k != 'workbookBase64'},
                            'sha256': checksum, 'byteLength': len(raw)})
        self.request = dict(version=1, operation='read-batch', workbooks=requests)
        self.output = dict(workbooks=outputs)
        self.assertEqual(self.output, self.invoke())
        receipt = self.state['jobs'][0]['workbook']
        self.assertEqual((2, sum(item['byteLength'] for item in outputs)), (receipt['workbooks'], receipt['byteLength']))
        self.assertTrue(self.state['jobs'][0]['containerAbsent'])
        for wrong in (outputs[:1], outputs[::-1]):
            with self.subTest(wrong=len(wrong)), self.assertRaisesRegex(ValueError, 'output-(batch|digest)'):
                oracle.verify_result(json.dumps({'workbooks': wrong}).encode(), self.request)
        self.commands.clear()
        self.request['workbooks'] = [self.request.copy()]
        with self.assertRaisesRegex(ValueError, 'batch-item'):
            self.invoke()
        self.assertFalse(self.commands)

    def test_input_tampering_or_foreign_collision_is_not_adopted(self):
        """变更模块字节在创建前拒绝；外来同名容器不启动、不删除、不记成已清理。"""
        self.script.chmod(0o666)
        self.script.write_bytes(b'changed')
        self.script.chmod(0o444)
        with self.assertRaisesRegex(ValueError, 'input-digest'):
            self.invoke()
        self.assertFalse(self.commands)
        self.script.chmod(0o666)
        self.script.write_bytes(Path(oracle.__file__).with_name(self.script.name).read_bytes())
        self.script.chmod(0o444)
        name = 'metarill-perf-' + RUN + '-oracle-01'
        foreign = ['d' * 64, RUN, 'e' * 64, 'oracle-01']
        self.registry[name] = foreign
        with self.assertRaisesRegex(ValueError, 'collision'):
            self.invoke()
        self.assertEqual(foreign, self.registry[name])
        self.assertEqual([], self.state['jobs'])
        self.start.assert_not_called()

    def test_process_timeout_still_removes_only_owned_container(self):
        """Docker客户端超时不等于容器停止，finally须按标签强制回收并再查。"""
        self.start.side_effect = subprocess.TimeoutExpired('docker', 30)
        with self.assertRaises(subprocess.TimeoutExpired):
            self.invoke()
        self.assertEqual({}, self.registry)
        self.assertTrue(self.state['jobs'][0]['containerAbsent'])
        self.assertNotIn('workbook', self.state['jobs'][0])

    def test_changed_owner_after_start_refuses_deletion_and_retains_ledger(self):
        """归属漂移即停止清理，不伪造核零或接管容器。"""
        def drift(*args):
            self.registry[args[0]][2] = 'e' * 64
            return types.SimpleNamespace(returncode=1, stderr=b'not retained', stdout=b'')
        self.start.side_effect = drift
        with self.assertRaisesRegex(RuntimeError, '资源身份变化'):
            self.invoke()
        self.assertTrue(self.registry)
        self.assertFalse(any(cmd[:2] == ('container', 'rm') for cmd in self.commands))
        self.assertNotIn('containerAbsent', self.state['jobs'][0])
        self.assertEqual('c' * 64, json.loads(self.path.read_bytes())['jobs'][0]['id'])

    def test_forged_output_digest_runtime_or_cells_is_rejected_and_cleaned(self):
        """返回JSON不能以自报成功绕过文件摘要、原依赖版本或中文／长ID格值。"""
        changes = [('output-digest', dict(sha256='e' * 64)), ('runtime', dict(nodeVersion='v22.0.0')),
                   ('created-cells', dict(cells=[self.request['headers'], ['变更', '']])),
                   ('output-format', dict(format='xls')), ('output-shape', dict(extra='unexpected'))]
        for code, change in changes:
            with self.subTest(code=code):
                original = dict(self.output)
                self.output.update(change)
                with self.assertRaisesRegex(ValueError, code):
                    self.invoke()
                self.assertFalse(self.registry)
                self.assertTrue(self.state['jobs'][-1]['containerAbsent'])
                self.assertNotIn('workbook', self.state['jobs'][-1])
                self.output = original

    def test_process_negative_must_retain_the_exact_original_oracle_code(self):
        """输入错摘要不能用任意进程失败冒充该反例命中；只保留固定位置码。"""
        self.start.side_effect = lambda *args: types.SimpleNamespace(returncode=1, stdout=b'', stderr='工作簿核验失败：workbook-digest\n'.encode())
        with self.assertRaisesRegex(ValueError, 'oracle-workbook-digest'):
            self.invoke()
        self.assertEqual('workbook-digest', self.state['jobs'][-1]['oracleFailureCode'])
        self.assertTrue(self.state['jobs'][-1]['containerAbsent'])
        self.start.side_effect = lambda *args: types.SimpleNamespace(returncode=1, stdout=b'', stderr=b'arbitrary diagnostic')
        with self.assertRaisesRegex(ValueError, 'container-process$'):
            self.invoke()
        self.assertNotIn('oracleFailureCode', self.state['jobs'][-1])
        self.assertNotIn('arbitrary', self.path.read_text(encoding='utf-8'))

    def test_downloaded_biff_uses_the_original_bytes_and_expected_digest(self):
        """下载分支不生成新文件替代原文件，真实字节摘要须与捕获输入相同。"""
        data = bytes.fromhex('d0cf11e0a1b11ae1') + b'mock external biff'
        checksum = hashlib.sha256(data).hexdigest()
        self.request = dict(version=1, operation='read', base64=base64.b64encode(data).decode(), expectedSha256=checksum)
        self.output.pop('workbookBase64')
        self.output.update(format='xls', byteLength=len(data), sha256=checksum)
        self.assertEqual('xls', self.invoke()['format'])
        self.request['expectedSha256'] = 'f' * 64
        with self.assertRaisesRegex(ValueError, 'download-digest'):
            self.invoke()
        self.assertFalse(self.registry)

    def test_real_attached_transport_rechecks_server_and_uses_stdin_without_docker_env(self):
        """此处只检查命令边界，不执行任何本机Docker。"""
        with mock.patch.object(oracle.resources, 'verify_server') as verify, mock.patch.dict(oracle.os.environ, {'DOCKER_HOST': 'tcp://foreign:2375', 'DOCKER_CONTEXT': 'foreign'}), mock.patch.object(oracle.subprocess, 'run') as run:
            # setUp替换了attached_start，此处调用其原函数。
            WorkbookContainerTests.original_start('owned-name', b'{"version":1}')
        verify.assert_called_once_with()
        self.assertEqual(['docker', '-H', 'unix:///var/run/docker.sock', 'container', 'start', '--attach', '--interactive', 'owned-name'], run.call_args.args[0])
        self.assertEqual(b'{"version":1}', run.call_args.kwargs['input'])
        self.assertEqual(30, run.call_args.kwargs['timeout'])
        self.assertFalse(any(key.startswith('DOCKER_') for key in run.call_args.kwargs['env']))

    original_start = staticmethod(oracle.attached_start)


if __name__ == '__main__':
    unittest.main()
