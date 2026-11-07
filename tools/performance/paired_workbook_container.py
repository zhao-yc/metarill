"""在Server-200禁网容器中运行原前端工作簿模块；无数据库、网络或凭据挂载。"""
from __future__ import annotations
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import server200_paired_resources as resources

PURPOSE = 'paired-workbook-oracle-v1'
NODE_IMAGE = 'sha256:d8e448a56fc63242f70026718378bd4b00f8c82e78d20eefb199224a4d8e33d8'
LIBRARY_SHA256 = 'cc015130aa8521e7f088f88898eba949ccdcbfb38df0bd129b44b7273c3a6f41'
MAX_INPUT = 6 * 1024 * 1024
MAX_OUTPUT = 16 * 1024 * 1024
MAX_JOBS = 12
MAX_BATCH = 128


def require(ok, code):
    """只抛固定位置码，不将工作簿数据、私有路径或进程输出混入异常。"""
    if not ok:
        raise ValueError('workbook-container-' + code)


def descriptor(run):
    """上层已核归属的合成测试用途拆出无业务连接的工作簿执行范围。"""
    require(type(run) is str and re.fullmatch('[a-f0-9]{32}', run), 'run')
    return dict(version=1, purpose=PURPOSE, runId=run, authorizationStatus='approved',
                leaseStatus='running', nodeImage=NODE_IMAGE, librarySha256=LIBRARY_SHA256)


def validate_request(request):
    """先验证批次结构；单项读取沿用固定字段，批次不能嵌套。"""
    require(type(request) is dict and type(request.get('version')) is int and request['version'] == 1, 'request')
    operation = request.get('operation')
    keys = {'version', 'operation'}
    if operation == 'read-batch':
        require(set(request) == keys | {'workbooks'} and type(request['workbooks']) is list
                and 0 < len(request['workbooks']) <= MAX_BATCH, 'batch')
        require(all(type(item) is dict and item.get('operation') == 'read' for item in request['workbooks']), 'batch-item')
        for item in request['workbooks']:
            validate_request(item)
    else:
        require(operation in ('create', 'read'), 'request')
        expected = {'headers', 'rows'} if operation == 'create' else {'base64', 'expectedSha256'}
        require(set(request) == keys | expected, 'request')


def validate(value, state, root, state_path, request):
    """先纯内存核租约／归属和请求上限，未知输入不能触碰文件或Docker。"""
    run = value.get('runId')
    require(type(value.get('version')) is int and value == descriptor(run), 'scope')
    require(state.get('runId') == run and state.get('leaseStatus') == 'running' and
            type(state.get('ownerToken')) is str and re.fullmatch('[a-f0-9]{64}', state['ownerToken']) and
            type(state.get('jobs')) is list, 'owner')
    require(root == resources.root_for(run) and state_path == root / 'native-state.json', 'root')
    validate_request(request)
    try:
        raw = json.dumps(request, ensure_ascii=False, allow_nan=False, separators=(',', ':')).encode('utf-8')
    except (TypeError, ValueError):
        raise ValueError('workbook-container-json') from None
    require(0 < len(raw) <= MAX_INPUT, 'input-size')
    previous = [job for job in state['jobs'] if str(job.get('target', '')).startswith('oracle-')]
    require(len(previous) < MAX_JOBS and all(job.get('containerAbsent') is True for job in previous), 'job-limit-or-live')
    return raw, len(previous) + 1


def read_inputs(root):
    """只挂载两份逐字核对的公开代码，不把上层含连接口令的私有根挂入容器。"""
    resources.regular(root, True)
    folder = root / 'oracle'
    resources.regular(folder, True)
    script = folder / 'paired_workbook_oracle.cjs'
    library = folder / 'xlsx.full.min.js'
    expected_script = Path(__file__).with_name('paired_workbook_oracle.cjs').read_bytes()
    for path, checksum in ((script, hashlib.sha256(expected_script).hexdigest()), (library, LIBRARY_SHA256)):
        info = resources.regular(path)
        require(info.st_mode & 0o777 == 0o444 and path.resolve().is_relative_to(root.resolve()), 'input-file')
        require(hashlib.sha256(path.read_bytes()).hexdigest() == checksum, 'input-digest')
    return script, library


def verify_result(raw, request):
    """独立核对实际输出字节、版本、格式、工作表与输入，不能相信工具自报摘要。"""
    require(0 < len(raw) <= MAX_OUTPUT, 'output-size')
    try:
        result = json.loads(raw)
    except (ValueError, UnicodeError):
        raise ValueError('workbook-container-output-json') from None
    require(type(result) is dict, 'output-shape')
    if request['operation'] == 'read-batch':
        workbooks = result.get('workbooks')
        require(set(result) == {'workbooks'} and type(workbooks) is list
                and len(workbooks) == len(request['workbooks']), 'output-batch')
        return {'workbooks': [verify_result(json.dumps(item, ensure_ascii=False).encode('utf-8'), source)
                              for item, source in zip(workbooks, request['workbooks'], strict=True)]}
    keys = {'format', 'byteLength', 'sha256', 'sheetNames', 'headers', 'rows', 'cells', 'sheetJsVersion', 'nodeVersion'}
    require(set(result) == keys | ({'workbookBase64'} if request['operation'] == 'create' else set()), 'output-shape')
    require(result['sheetJsVersion'] == '0.20.3' and result['nodeVersion'] == 'v24.14.0', 'runtime')
    require(type(result['byteLength']) is int and 0 < result['byteLength'] <= 4 * 1024 * 1024 and
            type(result['sha256']) is str and re.fullmatch('[a-f0-9]{64}', result['sha256']), 'output-digest')
    encoded = result.get('workbookBase64') if request['operation'] == 'create' else request['base64']
    try:
        data = base64.b64decode(encoded, validate=True)
    except (ValueError, TypeError):
        raise ValueError('workbook-container-output-base64') from None
    require(base64.b64encode(data).decode() == encoded and len(data) == result['byteLength'] and
            hashlib.sha256(data).hexdigest() == result['sha256'], 'output-digest')
    kind = 'xlsx' if data.startswith(b'PK\x03\x04') else 'xls' if data.startswith(bytes.fromhex('d0cf11e0a1b11ae1')) else None
    require(kind is not None and result['format'] == kind, 'output-format')
    require(type(result['sheetNames']) is list and len(result['sheetNames']) == 1 and
            all(type(name) is str for name in result['sheetNames']), 'output-sheet')
    headers, rows, cells = result['headers'], result['rows'], result['cells']
    require(type(headers) is list and 0 < len(headers) <= 32 and all(type(h) is str and h for h in headers) and len(set(headers)) == len(headers), 'output-headers')
    require(type(rows) is list and len(rows) <= 256 and all(type(row) is dict and set(row) <= set(headers) for row in rows), 'output-rows')
    require(type(cells) is list and 1 <= len(cells) <= 257 and cells[0] == headers and
            all(type(row) is list and len(row) <= len(headers) for row in cells), 'output-cells')
    if request['operation'] == 'create':
        # aoa_to_sheet省略null及整行空白，完整原始格仍需按原默认选项逐格一致。
        expected_rows = [row for row in request['rows'] if any(value is not None for value in row)]
        require(headers == request['headers'] and cells[1:] == expected_rows and kind == 'xlsx', 'created-cells')
        require(rows == [{key: value for key, value in zip(headers, row) if value is not None} for row in expected_rows], 'created-rows')
    else:
        require(result['sha256'] == request['expectedSha256'], 'download-digest')
    return result


def attached_start(name, raw):
    """Docker stdin只传工作簿JSON；每条命令仍重新核服务器，不继承客户端Docker上下文。"""
    resources.verify_server()
    env = {key: value for key, value in os.environ.items() if not key.startswith('DOCKER_')}
    return subprocess.run(['docker', '-H', 'unix:///var/run/docker.sock', 'container', 'start', '--attach', '--interactive', name],
                          input=raw, capture_output=True, timeout=30, env=env)


def invoke(value, request, root, state, state_path):
    """先登记待创建对象；成功／失败／超时均按随机token回收，外来同名对象不接管。"""
    raw, number = validate(value, state, root, state_path, request)
    script, library = read_inputs(root)
    target = 'oracle-' + format(number, '02d')
    item = dict(kind='container', target=target, name='metarill-perf-' + value['runId'] + '-' + target)
    require(resources.inspect('container', item['name']) is None, 'collision')
    require(resources.docker('image', 'inspect', '--format', '{{.Id}}', NODE_IMAGE) == NODE_IMAGE, 'image')
    state['jobs'].append(item)
    resources.write_state(state_path, state)
    labels = sum((['--label', resources.LABEL + key + '=' + text] for key, text in
                  {'run': state['runId'], 'owner': state['ownerToken'], 'target': target}.items()), [])
    try:
        args = ['container', 'create', '--interactive', '--name', item['name'], *labels,
                '--network', 'none', '--user', '10001:10001', '--read-only', '--cap-drop', 'ALL',
                '--security-opt', 'no-new-privileges', '--restart', 'no', '--log-driver', 'none',
                '--pids-limit', '64', '--memory', '268435456', '--memory-swap', '268435456',
                '--cpu-period', '100000', '--cpu-quota', '100000', '--cpuset-cpus', '0',
                '--mount', f'type=bind,src={script},dst=/oracle/paired_workbook_oracle.cjs,readonly',
                '--mount', f'type=bind,src={library},dst=/oracle/xlsx.full.min.js,readonly',
                '--entrypoint', 'node', NODE_IMAGE, '--max-old-space-size=192',
                '/oracle/paired_workbook_oracle.cjs', '/oracle/xlsx.full.min.js']
        item['id'] = resources.docker(*args)
        require(re.fullmatch('[a-f0-9]{64}', item['id']), 'id')
        resources.write_state(state_path, state)
        actual = resources.inspect('container', item['name'])
        require(actual is not None, 'missing-created')
        resources.require_owned(actual, item, state)
        completed = attached_start(item['name'], raw)
        if completed.returncode != 0:
            # 仅接收原工具固定格式的位置码；错误正文和输入数据不进入公共状态。
            reason = re.fullmatch('工作簿核验失败：(workbook-[a-z-]{1,64})\\n', completed.stderr.decode('utf-8', errors='replace'))
            if reason:
                item['oracleFailureCode'] = reason.group(1)
                raise ValueError('workbook-container-oracle-' + reason.group(1))
            raise ValueError('workbook-container-process')
        require(not completed.stderr, 'stderr')
        result = verify_result(completed.stdout, request)
        receipt = dict(operation=request['operation'], inputSha256=hashlib.sha256(raw).hexdigest(),
                       outputSha256=hashlib.sha256(completed.stdout).hexdigest(), nodeImage=NODE_IMAGE, librarySha256=LIBRARY_SHA256)
        if request['operation'] == 'read-batch':
            receipt.update(workbooks=len(result['workbooks']), byteLength=sum(w['byteLength'] for w in result['workbooks']))
        else:
            receipt.update(fileSha256=result['sha256'], byteLength=result['byteLength'], format=result['format'], rows=len(result['rows']))
        item['workbook'] = receipt
        return result
    finally:
        # create返回丢失时也先检查标签，绝不能把未知容器算作本轮或直接删掉。
        actual = resources.inspect('container', item['name'])
        if actual is not None:
            resources.require_owned(actual, item, state)
            resources.docker('container', 'rm', '--force', item['name'])
        item['containerAbsent'] = resources.inspect('container', item['name']) is None
        resources.write_state(state_path, state)
        require(item['containerAbsent'], 'cleanup')
