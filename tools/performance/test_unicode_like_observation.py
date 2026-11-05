"""当前Unicode候选的真实编排边界与原HTTP对照；本文件只使用显式外部替身。"""
import json
import unittest
from unittest import mock
import paired_native_bootstrap as native
import paired_observed_capture as capture
import paired_business_http as business
import test_paired_observed_native as previous
import test_observed_paired_resources as material


def settings():
    """合成租约只用于离线测试，不是外部执行授权。"""
    r=native.resources
    return {**previous.value(),'purpose':r.UNICODE_OBSERVED_NATIVE,'images':dict(r.UNICODE_OBSERVED_IMAGES)}


class UnicodeObservedContractTests(unittest.TestCase):
    def test_new_pins_and_eight_probes_preserve_all_original_requests(self):
        """新入口固定同一Data；额外只读步骤不能改写原46请求的顺序、采样或响应规则。"""
        r=native.resources;value=settings();descriptor=native.validate(value)
        self.assertEqual(r.UNICODE_OBSERVED_RESOURCES,descriptor['purpose']);self.assertEqual(value['runId'],capture.authorize(value))
        self.assertIn('--Metarill:Paired:SqlObservation:ExpectedDataSha256='+r.UNICODE_OBSERVATION_DATA_SHA256,r.observation_arguments(descriptor,'csharp'))
        old={**value,'purpose':r.PATTERN_OBSERVED_NATIVE,'images':dict(r.PATTERN_OBSERVED_IMAGES)}
        native.validate(old);self.assertEqual(r.PATTERN_OBSERVATION_DATA_SHA256,r.observation_data_sha(old))
        for target in ('java','csharp'):
            original=capture.expected_steps(old,target);expanded=capture.expected_steps(value,target)
            self.assertEqual(46,len(original));self.assertEqual(54,len(expanded))
            self.assertEqual(original,[row for row in expanded if not row['step'].startswith('unicode:')])
            index=next(i for i,row in enumerate(expanded)if row['step']=='business:customer-count')
            self.assertEqual(['unicode:count-'+code for code,_ in business.UNICODE_BLANK_CASES],[row['step']for row in expanded[index+1:index+9]])
        with self.assertRaises(ValueError):capture.Exchange(value,mock.Mock())

    def test_pending_old_images_and_performance_budgets_refuse_before_source_io(self):
        """待执行、错误镜像、结束状态或计时预算均在任何外部动作前拒绝。"""
        r=native.resources
        for change in [{'authorizationStatus':'pending'},{'leaseStatus':'ended'},{'images':dict(r.PATTERN_OBSERVED_IMAGES)},{'readReplayRequests':19336},{'writeReplayRequests':8624},{'fileReplayRequests':4834,'fileFixtureRequests':18}]:
            with self.subTest(change=change),mock.patch.object(native,'verify_tool_sources',side_effect=AssertionError('禁止读源')),self.assertRaises(ValueError):native.run({**settings(),**change})
        with self.assertRaises(ValueError):capture.Exchange({**settings(),'purpose':r.PATTERN_OBSERVED_NATIVE,'images':dict(r.PATTERN_OBSERVED_IMAGES)},mock.Mock(),unicode_counts=mock.Mock())

    def test_sql_baseline_only_reads_fixed_owned_table_and_requires_nonempty_fixture(self):
        """独立基线只生成固定SELECT与UTF8十六进制；空夹具不能使错误忽略输入也通过。"""
        r=native.resources;lease={**settings(),'purpose':r.UNICODE_OBSERVED_BUSINESS,'leaseStatus':'running'};commands=[]
        def query(sql):
            commands.append(sql)
            return business.PIN['hostname']+'\t'+business.PIN['serverUuid'] if '@@hostname' in sql else '0' if 'UNHEX(' in sql else '2'
        result=business.unicode_read_counts(lease,'java',query)
        self.assertEqual({code:2 if blank else 0 for code,blank in business.UNICODE_BLANK_CASES},result)
        self.assertEqual(6,len(commands));self.assertTrue(all(sql.startswith('SELECT ')for sql in commands));self.assertEqual(4,sum('UNHEX(' in sql for sql in commands))
        for change in [{'purpose':r.PATTERN_OBSERVED_BUSINESS},{'leaseStatus':'ended'},{'images':dict(r.PATTERN_OBSERVED_IMAGES)},{'databases':['existing_business','existing_platform']}]:
            callback=mock.Mock(side_effect=AssertionError('禁止查询'))
            with self.subTest(change=change),self.assertRaises(ValueError):business.unicode_read_counts({**lease,**change},'java',callback)
            callback.assert_not_called()
        with self.assertRaises(ValueError):business.unicode_read_counts(lease,'java',lambda sql:business.PIN['hostname']+'\t'+business.PIN['serverUuid'] if '@@hostname' in sql else '0')


class UnicodeExchangeTests(unittest.TestCase):
    def setUp(self):
        """发送器只模拟预定HTTP结果；真正调用Exchange的顺序、对照与收据逻辑。"""
        self.value=settings();self.calls=[];self.expected={code:2 if blank else 0 for code,blank in business.UNICODE_BLANK_CASES};self.force_bad=False
        self.lookup={row['sample']:row for target in ('java','csharp')for row in capture.expected_steps(self.value,target)}
        self.baseline=mock.Mock(side_effect=lambda target:dict(self.expected));self.exchange=capture.Exchange(self.value,self.send,unicode_counts=self.baseline)

    def send(self,port,method,path,body,token,*,form=False,sample):
        """仅记录固定步骤和假响应；不保存令牌、打开套接字或执行SQL。"""
        entry=self.lookup[sample];step=entry['step'];self.calls.append((step,body))
        count=2 if step=='business:customer-count' else self.expected[step[-4:]] if step.startswith('unicode:') else None
        if self.force_bad and step=='unicode:count-0085':count=2
        return entry['statusAtExit'],{'code':entry['expectedCode'],'data':count}

    def advance(self,target):
        """仅推进原计数之前的已声明调用，不能直接改写完成账本。"""
        for row in self.exchange.plans[target]:
            if row['step']=='business:customer-count':return
            self.exchange.call(target,row['step'],'POST','/offline-only',{},'opaque-test-token')

    def anchor(self,target):
        """保持原完整请求体，附加对照只替换其独立副本中的name。"""
        body={'pageNo':1,'pageSize':20,'filters':{'name':'配对客户-'+self.value['runId'][:16]},'orderBy':{'column':'name','order':'ascending'}}
        before=json.dumps(body,sort_keys=True)
        self.exchange.business_request(target,'customer-count','POST',business.ROOTS['customer']+'/count',body,'opaque-test-token')
        self.assertEqual(before,json.dumps(body,sort_keys=True))

    def test_actual_exchange_inserts_all_probes_and_closes_original_coverage(self):
        """两端各54个预定请求闭合，额外八项逐项核值，原业务正文保持。"""
        for target in ('java','csharp'):
            self.advance(target);self.anchor(target)
            for row in self.exchange.plans[target][len(self.exchange.sent[target]):]:self.exchange.call(target,row['step'],'POST','/offline-only',{},'opaque-test-token')
        self.exchange.complete();receipt=self.exchange.receipt();self.assertEqual({'java':54,'csharp':54},receipt['sent']);self.assertEqual(receipt['sent'],receipt['completed'])
        for target in ('java','csharp'):self.assertEqual(8,len(receipt['unicodeCounts'][target]))
        self.assertEqual(2,self.baseline.call_count);self.assertNotIn('opaque-test-token',json.dumps(receipt))
        self.exchange.unicode_facts['java'][0]['javaBlank']=0
        with self.assertRaises(ValueError):self.exchange.complete()

    def test_wrong_count_stops_without_replaying_or_continuing_probes(self):
        """错误忽略输入会返回全量2而基线为0，必须立刻失败；再次调用也不能重发原请求。"""
        self.advance('java');self.force_bad=True
        with self.assertRaises(ValueError):self.anchor('java')
        self.assertEqual('unicode:count-0085',self.calls[-1][0]);before=len(self.calls)
        with self.assertRaises(ValueError):self.anchor('java')
        self.assertEqual(before,len(self.calls))


class UnicodeObservedFlowTests(unittest.TestCase):
    database=previous.ObservedFlowTests.database
    job=previous.ObservedFlowTests.job
    resources=previous.ObservedFlowTests.resources
    capture=previous.ObservedFlowTests.capture
    run_scope=previous.ObservedFlowTests.run_scope

    def setUp(self):
        """复用显式外部替身，运行实际原生协调器及新用途分派。"""
        previous.ObservedFlowTests.setUp(self);self.value=settings()

    def run_business(self,lease,tokens,request,query):
        """新业务租约仍在六个初始化之后执行，镜像及三身份不可替换。"""
        self.events.append('business');self.assertEqual(native.resources.UNICODE_OBSERVED_BUSINESS,lease['purpose']);self.assertEqual(self.value['images'],lease['images']);self.assertEqual(6,self.jobs.call_count)
        return {'mockedOnly':True,'sqlSemanticAccepted':False}

    def test_current_native_flow_registers_baseline_and_stops_before_cleanup(self):
        """实际编排提供独立基线，正常停止后才采集，诊断不签发性能或发布通过。"""
        result=self.run_scope();self.assertTrue(result['passed']);self.assertFalse(result['performanceAccepted']);self.assertFalse(result['releaseAccepted'])
        self.assertTrue(callable(capture.Exchange.call_args.kwargs['unicode_counts']))
        for first,second in [('resources-wait-hosts','business'),('business','resources-stop-hosts'),('resources-stop-hosts','capture'),('capture','resources-cleanup'),('resources-cleanup','database-cleanup')]:self.assertLess(self.events.index(first),self.events.index(second))
        self.assertEqual(native.resources.UNICODE_OBSERVED_RESOURCES,native.resources.manage.call_args_list[0].args[0]['purpose'])


class UnicodeObservedMaterialTests(unittest.TestCase):
    materialize=material.ObservedResourceTests.materialize
    tearDown=material.ObservedResourceTests.tearDown
    refresh=material.ObservedResourceTests.refresh
    key=material.ObservedResourceTests.key

    def setUp(self):
        """实际解析材料并生成启动命令，只有Docker和文件权限边界是替身。"""
        material.ObservedResourceTests.setUp(self);self.value.update(purpose=native.resources.UNICODE_OBSERVED_RESOURCES,images=dict(native.resources.UNICODE_OBSERVED_IMAGES))

    def test_actual_materials_pin_current_data_and_only_csharp_additional_deps(self):
        """当前程序集摘要必须进入实际命令，旧模式镜像及Java参数保持原值。"""
        r=native.resources;r.read_materials(self.root,self.value);r.manage(self.value,'create');commands=[c for c in self.engine.calls if c[:2]==('container','create')];self.assertEqual(3,len(commands))
        for side,command in zip(('redis','java','csharp'),commands):
            self.assertIn(r.UNICODE_OBSERVED_IMAGES[side],command);self.assertEqual(side=='csharp','DOTNET_ADDITIONAL_DEPS=/app/Metarill.Paired.SqlObservation.deps.json' in command)
            if side=='csharp':self.assertIn('--Metarill:Paired:SqlObservation:ExpectedDataSha256='+r.UNICODE_OBSERVATION_DATA_SHA256,command)

if __name__=='__main__':unittest.main()