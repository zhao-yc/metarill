"""实际.NET客户端的200独占Redis探针；仅合成数据、回环SSH、不操作MySQL或共享Redis。"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
import pathlib
import re
import shlex
import socket
import ipaddress
import subprocess
import time
import uuid

REMOTE=r'''
import ipaddress,json,re,select,subprocess,sys,time
p=json.loads(sys.stdin.readline());run=p["runId"]
if not re.fullmatch("[a-f0-9]{32}",run):raise RuntimeError("编号无效")
image="sha256:ff02b58f971e7d7d156a1267e283fcbbeee91773b6aa36c49dac28ecfe28eadf"
name="metarill-login-client-"+run;label="metarill.login-client-probe";cid=None;nid=None

def host():
    if subprocess.check_output(["hostname"],text=True).strip()!="Server-200" or not re.search(r"\binet 192\.168\.31\.200/",subprocess.check_output(["ip","-4","addr","show"],text=True)):raise RuntimeError("拒绝非200")
def docker(*args,check=True):
    host();return subprocess.run(["docker","-H","unix:///var/run/docker.sock",*args],capture_output=True,text=True,check=check,timeout=20)
def absent(kind):
    result=docker(kind,"inspect",name,check=False)
    if result.returncode==0 or not re.search("No such|not found",result.stderr,re.I):raise RuntimeError("对象不为空或未核零")
try:
    absent("container");absent("network");docker("image","inspect",image)
    nid=docker("network","create","--internal","--label",label+"="+run,name).stdout.strip()
    if not re.fullmatch("[a-f0-9]{64}",nid):raise RuntimeError("网络归属无效")
    cid=docker("container","create","--name",name,"--label",label+"="+run,"--network",nid,"--user","10001:10001","--read-only","--cap-drop","ALL","--security-opt","no-new-privileges","--restart","no","--pids-limit","64","--memory","67108864","--memory-swap","67108864","--cpus","0.5","--tmpfs","/tmp:rw,nosuid,nodev,noexec,size=16777216,uid=10001,gid=10001,mode=0700",image,"redis-server","--bind","0.0.0.0","--port","6379","--protected-mode","no","--save","","--appendonly","no","--dir","/tmp").stdout.strip()
    if not re.fullmatch("[a-f0-9]{64}",cid):raise RuntimeError("容器归属无效")
    docker("start",cid)
    for _ in range(50):
        ping=docker("exec",cid,"redis-cli","PING",check=False)
        if ping.returncode==0 and ping.stdout.strip()=="PONG":break
        time.sleep(.05)
    else:raise RuntimeError("Redis未就绪")
    config=json.loads(docker("container","inspect",cid).stdout)[0]
    if config["HostConfig"]["PortBindings"]:raise RuntimeError("拒绝发布宿主端口")
    networks=list(config["NetworkSettings"]["Networks"].values())
    if len(networks)!=1 or networks[0]["NetworkID"]!=nid:raise RuntimeError("拒绝包外网络")
    address=ipaddress.ip_address(networks[0]["IPAddress"]);network=json.loads(docker("network","inspect",nid).stdout)[0]
    if not network["Internal"] or network["Labels"].get(label)!=run or not address.is_private or not any(address in ipaddress.ip_network(item["Subnet"]) for item in network["IPAM"]["Config"]):raise RuntimeError("内部地址归属不符")
    info=docker("exec",cid,"redis-cli","INFO","server").stdout;match=re.search(r"^run_id:([a-f0-9]{40})\r?$",info,re.M)
    if not match:raise RuntimeError("服务端身份缺失")
    print(json.dumps({"runId":run,"redisRunId":match[1],"host":str(address),"port":6379}),flush=True)
    if not select.select([sys.stdin],[],[],240)[0] or sys.stdin.readline().strip()!="cleanup":raise RuntimeError("探针控制结束或超时")
finally:
    if cid:
        c=json.loads(docker("container","inspect",cid).stdout)[0]
        if c["Id"]!=cid or c["Image"]!=image or c["Config"]["Labels"].get(label)!=run:raise RuntimeError("容器归属不符")
        docker("rm","-f",cid);absent("container")
    if nid:
        n=json.loads(docker("network","inspect",nid).stdout)[0]
        if n["Id"]!=nid or n["Labels"].get(label)!=run or not n["Internal"] or n["Containers"]:raise RuntimeError("网络归属不符")
        docker("network","rm",nid);absent("network")
print(json.dumps({"runId":run,"containerRemoved":True,"networkRemoved":True,"mysqlTouched":False,"sharedRedisTouched":False}),flush=True)
'''


def ssh(program):
    """固定严格SSH，不允许调用方替换目标服务器或关闭主机校验。"""
    return ["ssh","-o","BatchMode=yes","-o","StrictHostKeyChecking=yes","-o","ConnectTimeout=10","server-200","python3 -c "+shlex.quote(program)]


def main():
    """严格编译用例另行执行；显式run只执行该已编译用例并保证两端清理。"""
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("--run",action="store_true");parser.add_argument("--output",type=pathlib.Path,required=True);args=parser.parse_args()
    compile(REMOTE,"redis-client-remote","exec")
    if not args.run:print("探针源码检查通过，未执行SSH、Docker或外部连接。");return
    root=pathlib.Path(__file__).resolve().parents[2];output=args.output.resolve();output.mkdir(parents=True,exist_ok=False)
    run=uuid.uuid4().hex;flags=getattr(subprocess,"CREATE_NO_WINDOW",0);remote=None;tunnel=None;test_code=None;remote_tail="";remote_error="";ready=None;operation_error=None;cleanup_error=None
    (output/"intent.json").write_text(json.dumps({"runId":run,"containerName":"metarill-login-client-"+run,"networkName":"metarill-login-client-"+run,"hostPublishedPorts":False}),encoding="utf-8")
    try:
        remote=subprocess.Popen(ssh(REMOTE),stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding="utf-8",creationflags=flags)
        remote.stdin.write(json.dumps({"runId":run})+"\n");remote.stdin.flush();line=remote.stdout.readline()
        if not line:raise RuntimeError("200未提供独占实例，保留远端错误")
        ready=json.loads(line)
        if ready["runId"]!=run or not re.fullmatch("[a-f0-9]{40}",ready["redisRunId"]) or ready["port"]!=6379 or not ipaddress.ip_address(ready["host"]).is_private:raise RuntimeError("独占身份无效")
        with socket.socket() as lease:lease.bind(("127.0.0.1",0));port=lease.getsockname()[1]
        tunnel=subprocess.Popen(["ssh","-N","-o","BatchMode=yes","-o","StrictHostKeyChecking=yes","-o","ConnectTimeout=10","-o","ExitOnForwardFailure=yes","-L",f"127.0.0.1:{port}:{ready['host']}:{ready['port']}","server-200"],stdout=subprocess.PIPE,stderr=subprocess.PIPE,creationflags=flags)
        for _ in range(50):
            if tunnel.poll() is not None:raise RuntimeError("本轮SSH转发失败")
            try:
                with socket.create_connection(("127.0.0.1",port),timeout=.2):break
            except OSError:time.sleep(.1)
        else:raise RuntimeError("本轮SSH转发未就绪")
        env={k:v for k,v in os.environ.items() if not k.upper().startswith("METARILL")};env.update({"METARILL_LOGIN_REDIS_PROBE_CONNECTION":f"127.0.0.1:{port},abortConnect=false","METARILL_LOGIN_REDIS_PROBE_RUN_ID":ready["redisRunId"],"METARILL_LOGIN_REDIS_PROBE_SCOPE":"probe:"+run})
        with (output/"real-client.log").open("wb") as log:
            result=subprocess.run([str(root/".tools/dotnet/dotnet.exe"),"test",str(root/"backend/tests/Metarill.Security.Tests/Metarill.Security.Tests.csproj"),"--no-build","--no-restore","--nologo","--filter","FullyQualifiedName~RedisLoginAttemptStoreLiveTests","--logger","trx;LogFileName=real-client.trx","--results-directory",str(output),"--verbosity","normal"],cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT,timeout=120,creationflags=flags);test_code=result.returncode
    except Exception as error:
        operation_error=error
    finally:
        if tunnel:
            if tunnel.poll() is None:
                tunnel.terminate()
                try:tunnel.wait(timeout=15)
                except subprocess.TimeoutExpired:tunnel.kill();tunnel.wait(timeout=10)
            stdout,stderr=tunnel.communicate();(output/"tunnel.stderr.log").write_bytes(stderr)
        if remote:
            if remote.poll() is None:
                try:remote.stdin.write("cleanup\n");remote.stdin.flush()
                except (BrokenPipeError,OSError):pass
            remote_tail,remote_error=remote.communicate(timeout=45)
            (output/"remote.stdout.log").write_text(remote_tail,encoding="utf-8");(output/"remote.stderr.log").write_text(remote_error,encoding="utf-8")
    independent="import subprocess,re;assert subprocess.check_output(['hostname'],text=True).strip()=='Server-200';assert re.search(r'\\binet 192\\.168\\.31\\.200/',subprocess.check_output(['ip','-4','addr','show'],text=True));"+"\nfor kind in ('container','network'):\n r=subprocess.run(['docker','-H','unix:///var/run/docker.sock',kind,'inspect','metarill-login-client-"+run+"'],capture_output=True,text=True);assert r.returncode!=0 and re.search('No such|not found',r.stderr,re.I)\nprint('独立核零通过')"
    checked=subprocess.run(ssh(independent),capture_output=True,timeout=30);(output/"independent.stdout.log").write_bytes(checked.stdout);(output/"independent.stderr.log").write_bytes(checked.stderr)
    if checked.returncode:raise RuntimeError("独立核零未通过，不自动创建下一轮")
    if remote.returncode:raise RuntimeError("远端验收未通过；本轮容器与内部网络已独立核零")
    if operation_error is not None:raise RuntimeError("本轮客户端运行未完成；容器与内部网络已独立核零") from operation_error
    cleanup=json.loads(remote_tail.strip())
    if cleanup!={"runId":run,"containerRemoved":True,"networkRemoved":True,"mysqlTouched":False,"sharedRedisTouched":False}:raise RuntimeError("清理回执不符")
    import xml.etree.ElementTree as ET
    result=ET.parse(output/"real-client.trx").find("{*}ResultSummary/{*}Counters").attrib
    if test_code!=0 or result.get("total")!="1" or result.get("passed")!="1" or result.get("failed")!="0":raise RuntimeError("实际客户端未完成1过零败；不得以跳过计通过")
    proof={"runId":run,"productCommit":subprocess.check_output(["git","-C",str(root),"rev-parse","HEAD"],text=True).strip(),"testSha256":hashlib.sha256((root/"backend/tests/Metarill.Security.Tests/RedisLoginAttemptStoreLiveTests.cs").read_bytes()).hexdigest(),"passed":1,"failed":0,"skipped":0,"independentCleanup":True,"mysqlTouched":False,"sharedRedisTouched":False}
    (output/"proof.json").write_text(json.dumps(proof,ensure_ascii=False,indent=2),encoding="utf-8");print("实际.NET客户端及200独占Redis1项完整场景通过，零失败／跳过；SSH转发、容器及内部网络已关闭并独立核零。")

if __name__=="__main__":main()
