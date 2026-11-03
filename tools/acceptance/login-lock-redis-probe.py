"""原登录Lua的Server-200独占禁网Redis探针；不连接共享Redis、MySQL或创建库／账号。"""
from __future__ import annotations
import argparse
import hashlib
import json
import pathlib
import re
import shlex
import subprocess
import uuid

IMAGE="sha256:ff02b58f971e7d7d156a1267e283fcbbeee91773b6aa36c49dac28ecfe28eadf"
REMOTE=r'''
import concurrent.futures,hashlib,json,re,subprocess,sys,time
payload=json.load(sys.stdin)
run=payload["runId"];script=payload["script"];image=payload["image"]
if not re.fullmatch("[a-f0-9]{32}",run) or len(script)>4096 or hashlib.sha256(script.encode()).hexdigest()!=payload["scriptSha256"] or image!="sha256:ff02b58f971e7d7d156a1267e283fcbbeee91773b6aa36c49dac28ecfe28eadf":raise RuntimeError("探针输入不符")
name="metarill-login-lock-"+run;label="metarill.login-lock-probe";container=None;checks=[]
def host():
    if subprocess.check_output(["hostname"],text=True).strip()!="Server-200" or not re.search(r"\binet 192\.168\.31\.200/",subprocess.check_output(["ip","-4","addr","show"],text=True)):raise RuntimeError("拒绝非Server-200")
def docker(*args,input=None,check=True):
    host();return subprocess.run(["docker","-H","unix:///var/run/docker.sock",*args],input=input,text=True,capture_output=True,check=check,timeout=20)
def cli(*args,lua=False):
    result=docker("exec","-i",container,"redis-cli","--raw","-s","/tmp/login.sock",*( ["--eval","/dev/stdin",*args] if lua else args),input=script if lua else None)
    output=result.stdout.strip()
    if output.startswith(("ERR ","error:")):raise RuntimeError("Redis探针执行失败")
    return output.splitlines()
def keys(scope):
    return ["metarill:login:{"+run+"}:"+scope+":"+kind for kind in ("af","al","if","il")]
def operation(mode,scope="base",account=True,ip=True):return [int(x) for x in cli(*keys(scope),",",mode,"1" if account else "0","1" if ip else "0",lua=True)]
def require(value,description):
    if not value:raise RuntimeError("原登录规则不符："+description)
    checks.append(description)
try:
    old=docker("container","inspect",name,check=False)
    if old.returncode==0 or "No such" not in old.stderr:raise RuntimeError("目标容器名已存在或预检失败")
    docker("image","inspect",image)
    container=docker("container","create","--name",name,"--label",label+"="+run,"--network","none","--user","10001:10001","--read-only","--cap-drop","ALL","--security-opt","no-new-privileges","--restart","no","--pids-limit","64","--memory","67108864","--memory-swap","67108864","--cpus","0.5","--tmpfs","/tmp:rw,nosuid,nodev,noexec,size=16777216,uid=10001,gid=10001,mode=0700",image,"redis-server","--port","0","--unixsocket","/tmp/login.sock","--unixsocketperm","700","--save","","--appendonly","no","--dir","/tmp").stdout.strip()
    if not re.fullmatch("[a-f0-9]{64}",container):raise RuntimeError("容器ID无效")
    docker("start",container)
    for _ in range(40):
        ping=docker("exec",container,"redis-cli","-s","/tmp/login.sock","PING",check=False)
        if ping.returncode==0 and ping.stdout.strip()=="PONG":break
        time.sleep(.05)
    else:raise RuntimeError("Redis未就绪")
    require(operation("check")==[0,0],"初始未锁")
    for i in range(4):require(operation("fail")==[0,0],"账号前四次不锁"+str(i+1))
    ttl=int(cli("PTTL",keys("base")[0])[0]);require(0<ttl<=900000,"15分钟固定计数窗口")
    require(operation("fail")==[1,0],"账号第五次锁定")
    require(0<int(cli("PTTL",keys("base")[1])[0])<=900000,"账号15分钟锁")
    require(int(cli("PTTL",keys("base")[0])[0])<=ttl,"重复失败不滑动延长计数窗口")
    lockttl=int(cli("PTTL",keys("base")[1])[0]);require(operation("check")==[1,0],"账号锁优先")
    require(int(cli("PTTL",keys("base")[1])[0])<=lockttl,"已锁查询不延长锁")
    operation("clear");require(cli("EXISTS",keys("base")[0],keys("base")[2])==["0"],"成功清除两种计数")
    require(operation("check")==[1,0],"成功不删除并发产生的锁")
    for i in range(19):require(operation("fail","ip",account=False)==[0,0],"IP前十九次不锁"+str(i+1))
    require(operation("fail","ip",account=False)==[0,1],"IP第二十次锁定")
    require(0<int(cli("PTTL",keys("ip")[3])[0])<=1800000,"IP30分钟锁")
    require(operation("check","ip",account=False)==[0,1],"IP锁读取")
    before=cli("DBSIZE");require(operation("fail","blank",False,False)==[0,0],"空身份不计数")
    require(cli("DBSIZE")==before,"空身份不创建键")
    cli("PEXPIRE",keys("base")[1],"1");cli("PEXPIRE",keys("ip")[3],"1");time.sleep(.02)
    require(operation("check")==[0,0] and operation("check","ip",account=False)==[0,0],"锁实际过期后恢复")
    operation("fail","window");cli("PEXPIRE",keys("window")[0],"1");cli("PEXPIRE",keys("window")[2],"1");time.sleep(.02)
    require(operation("fail","window")==[0,0] and cli("GET",keys("window")[0])==["1"],"过期计数重新从一开始")
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as workers:results=list(workers.map(lambda _:operation("fail","parallel"),range(40)))
    require(cli("GET",keys("parallel")[0])==["40"] and cli("GET",keys("parallel")[2])==["40"],"40并发双计数无丢失")
    require(sum(x[0] for x in results)==36 and sum(x[1] for x in results)==21,"并发阈值只在5及20后触发")
    require(operation("check","parallel")==[1,0],"双锁仍按原账号优先")
finally:
    if container:
        current=json.loads(docker("container","inspect",container).stdout)[0]
        if current["Id"]!=container or current["Image"]!=image or current["Config"]["Labels"].get(label)!=run:raise RuntimeError("清理归属不符，保留现场")
        docker("rm","-f",container)
        absent=docker("container","inspect",name,check=False)
        if absent.returncode==0 or "No such" not in absent.stderr:raise RuntimeError("探针容器未核零")
print(json.dumps({"runId":run,"image":image,"scriptSha256":payload["scriptSha256"],"passed":len(checks),"checks":checks,"containerRemoved":True,"mysqlTouched":False,"sharedRedisTouched":False,"network":"none"},ensure_ascii=False))
'''

def main():
    """默认只检查本地脚本来源；显式run才在200创建一个独占、禁网、无凭据容器。"""
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("--run",action="store_true");parser.add_argument("--output",type=pathlib.Path,required=True);args=parser.parse_args()
    root=pathlib.Path(__file__).resolve().parents[2]
    source=(root/"backend/src/Metarill.Security/RedisLoginAttemptStore.cs").read_text(encoding="utf-8")
    matches=re.findall(r'internal const string Script="""\n(.*?)\n        """;',source,re.S)
    if len(matches)!=1:raise RuntimeError("原Lua来源缺失或不唯一")
    script="\n".join(line[8:] if line.startswith("        ") else line for line in matches[0].splitlines())
    run=uuid.uuid4().hex;payload={"runId":run,"image":IMAGE,"script":script,"scriptSha256":hashlib.sha256(script.encode()).hexdigest()}
    args.output.mkdir(parents=True,exist_ok=True)
    if not args.run:print("Lua来源检查通过，未执行SSH或Docker。");return
    result=subprocess.run(["ssh","-o","BatchMode=yes","-o","StrictHostKeyChecking=yes","server-200","python3 -c "+shlex.quote(REMOTE)],input=json.dumps(payload).encode(),capture_output=True,timeout=180)
    (args.output/"redis-probe.stdout.log").write_bytes(result.stdout);(args.output/"redis-probe.stderr.log").write_bytes(result.stderr)
    if result.returncode:raise RuntimeError("200探针未通过，保留私有日志；不得把未核零当成功")
    proof=json.loads(result.stdout);(args.output/"redis-probe.json").write_text(json.dumps(proof,ensure_ascii=False,indent=2),encoding="utf-8")
    # 新SSH独立复核唯一容器名，不复用原进程自报的清理结论。
    verify="import subprocess,re;assert subprocess.check_output(['hostname'],text=True).strip()=='Server-200';assert re.search(r'\\binet 192\\.168\\.31\\.200/',subprocess.check_output(['ip','-4','addr','show'],text=True));r=subprocess.run(['docker','-H','unix:///var/run/docker.sock','container','inspect','metarill-login-lock-"+run+"'],capture_output=True,text=True);assert r.returncode!=0 and 'No such' in r.stderr;print('独立核零通过')"
    independent=subprocess.run(["ssh","-o","BatchMode=yes","-o","StrictHostKeyChecking=yes","server-200","python3 -c "+shlex.quote(verify)],capture_output=True,timeout=30)
    (args.output/"independent.stdout.log").write_bytes(independent.stdout);(args.output/"independent.stderr.log").write_bytes(independent.stderr)
    if independent.returncode:raise RuntimeError("独立核零未通过")
    print("Server-200独占禁网Redis通过"+str(proof["passed"])+"项；临时容器独立核零，未操作MySQL或共享Redis。")

if __name__=="__main__":main()
