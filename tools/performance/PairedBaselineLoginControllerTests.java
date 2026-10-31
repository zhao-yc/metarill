package com.yinzon.samplehost;

import com.alibaba.fastjson2.JSON;
import com.alibaba.fastjson2.JSONObject;
import com.baomidou.mybatisplus.core.conditions.Wrapper;
import com.yinzon.business.captcha.AdminCapCaptchaProperties;
import com.yinzon.business.captcha.AdminCapCaptchaVerifier;
import com.yinzon.common.Md5Utils;
import com.yinzon.entity.YzManager;
import com.yinzon.entity.YzManagerLog;
import com.yinzon.service.YzManagerService;
import com.yinzon.service.YzManagerLogService;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.mockito.ArgumentCaptor;
import org.springframework.data.redis.core.RedisTemplate;
import org.springframework.data.redis.core.ValueOperations;
import org.springframework.http.MediaType;
import org.springframework.test.util.ReflectionTestUtils;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.setup.MockMvcBuilders;
import org.springframework.web.client.RestTemplate;
import java.time.Duration;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;
import static org.junit.jupiter.api.Assertions.*;
import static org.mockito.ArgumentMatchers.*;
import static org.mockito.Mockito.*;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;

/** 原方法的进程内MVC/组件回归；账号、Redis和Cap传输使用桩，不代表正式登录验收。 */
class PairedBaselineLoginControllerTests {
    private YzManagerService managers;
    private YzManagerLogService audit;
    private RedisTemplate<String,String> redis;
    private ValueOperations<String,String> values;
    private AdminCapCaptchaProperties properties;
    private RestTemplate capTransport;
    private MockMvc mvc;
    private final Map<String,Long> increments = new HashMap<>();
    private final Map<String,String> cached = new HashMap<>();
    private final Map<String,Duration> lifetimes = new HashMap<>();

    /** 原Controller与原验证码实现均实际实例化，只隔离存储及远端传输。 */
    @BeforeEach
    @SuppressWarnings("unchecked")
    void setup() {
        managers = mock(YzManagerService.class); audit = mock(YzManagerLogService.class);
        redis = mock(RedisTemplate.class); values = mock(ValueOperations.class);
        properties = new AdminCapCaptchaProperties(); capTransport = mock(RestTemplate.class);
        properties.setServerUrl("https://cap.example.invalid"); properties.setSiteKey("test-site"); properties.setSecretKey("unit-test-secret");
        when(redis.opsForValue()).thenReturn(values);
        when(redis.hasKey(anyString())).thenAnswer(call -> cached.containsKey(call.getArgument(0)));
        when(redis.getExpire(anyString())).thenReturn(-1L);
        when(redis.expire(anyString(), any(Duration.class))).thenAnswer(call -> {lifetimes.put(call.getArgument(0),call.getArgument(1)); return true;});
        when(values.increment(anyString(),eq(1L))).thenAnswer(call -> increments.merge(call.getArgument(0),1L,Long::sum));
        doAnswer(call -> {cached.put(call.getArgument(0),call.getArgument(1)); lifetimes.put(call.getArgument(0),call.getArgument(2)); return null;}).when(values).set(anyString(),anyString(),any(Duration.class));
        var controller = new PairedBaselineLoginController();
        ReflectionTestUtils.setField(controller,"redisTemplate",redis);
        ReflectionTestUtils.setField(controller,"yzManagerService",managers);
        ReflectionTestUtils.setField(controller,"yzManagerLogService",audit);
        ReflectionTestUtils.setField(controller,"adminCapCaptchaProperties",properties);
        ReflectionTestUtils.setField(controller,"adminCapCaptchaVerifier",new AdminCapCaptchaVerifier(properties,capTransport));
        ReflectionTestUtils.setField(controller,"sessionSeconds",1800);
        mvc = MockMvcBuilders.standaloneSetup(controller).build();
    }

    /** 成功流程保留真实MD5盐校验、原form协议、字符串ID、TTL及响应敏感字段清空。 */
    @Test
    void original_form_login_preserves_digest_session_and_redaction() throws Exception {
        var user = account(); when(managers.getOne(org.mockito.ArgumentMatchers.<Wrapper<YzManager>>any())).thenReturn(user);
        JSONObject result = login(" owner ","client-password","");
        assertEquals(200,result.getIntValue("code"));
        String session = result.getJSONObject("map").getString("loginStateId");
        assertEquals(session,UUID.fromString(session).toString());
        var stored = JSON.parseObject(cached.get(session));
        assertEquals(Md5Utils.Md5("client-password","unit-salt"),stored.getString("password"));
        assertEquals("unit-salt",stored.getString("salt"));
        assertEquals(Duration.ofSeconds(1800),lifetimes.get(session));
        assertEquals("9007199254740993",result.getJSONObject("data").getString("id"));
        assertEquals("",result.getJSONObject("data").getString("password"));
        assertEquals("",result.getJSONObject("data").getString("salt"));
        assertEquals("",result.getJSONObject("data").getString("oldPassword"));
        assertNotNull(user.getLastLoginTime()); assertEquals("192.0.2.10",user.getLoginIp());
        verify(managers).updateById(user); verify(redis).delete("admin:login:fail:account:owner");
    }

    /** 密码错误不得生成会话或更新登录审计，原失败计数和失败日志仍被调用。 */
    @Test
    void wrong_password_counts_and_audits_without_session() throws Exception {
        when(managers.getOne(org.mockito.ArgumentMatchers.<Wrapper<YzManager>>any())).thenReturn(account());
        assertEquals(500,login("owner","wrong-password","").getIntValue("code"));
        assertEquals(1L,increments.get("admin:login:fail:account:owner"));
        assertEquals(1L,increments.get("admin:login:fail:ip:192.0.2.10"));
        assertTrue(cached.isEmpty()); verify(managers,never()).updateById(any(YzManager.class));
        var log = ArgumentCaptor.forClass(YzManagerLog.class); verify(audit).save(log.capture());
        assertEquals(8,log.getValue().getActionType()); assertEquals(2,log.getValue().getExecuteResult());
        assertFalse(log.getValue().getDetail().contains("wrong-password"));
    }

    /** 已锁账号必须在验证码和账号查询之前拒绝，顺序保持原源码。 */
    @Test
    void locked_account_stops_before_cap_and_lookup() throws Exception {
        properties.setEnabled(true); cached.put("admin:login:lock:account:owner","1");
        assertTrue(login("owner","client-password","").getString("msg").contains("15分钟"));
        verifyNoInteractions(managers,capTransport);
    }

    /** 启用Cap后缺少token不得查询密码、写会话或退化到免验证码登录。 */
    @Test
    void enabled_cap_rejects_missing_token_before_lookup() throws Exception {
        properties.setEnabled(true);
        assertEquals("请先完成安全验证",login("owner","client-password","").getString("msg"));
        verifyNoInteractions(managers,capTransport); assertTrue(cached.isEmpty());
    }

    /** 原Cap服务明确拒绝时，原登录方法不能继续验证账号。 */
    @Test
    void enabled_cap_rejects_invalid_token_before_lookup() throws Exception {
        properties.setEnabled(true); when(capTransport.postForObject(anyString(),any(),eq(JSONObject.class))).thenReturn(JSONObject.of("success",false));
        assertEquals("安全验证失败，请重试",login("owner","client-password","invalid").getString("msg"));
        verifyNoInteractions(managers); assertTrue(cached.isEmpty());
    }

    /** 正常Cap响应通过原验证器后才进入正常密码与会话流程。 */
    @Test
    void enabled_cap_success_reaches_original_password_flow() throws Exception {
        properties.setEnabled(true); when(capTransport.postForObject(anyString(),any(),eq(JSONObject.class))).thenReturn(JSONObject.of("success",true));
        when(managers.getOne(org.mockito.ArgumentMatchers.<Wrapper<YzManager>>any())).thenReturn(account());
        assertEquals(200,login("owner","client-password","valid").getIntValue("code"));
        verify(capTransport).postForObject(eq("https://cap.example.invalid/test-site/siteverify"),any(),eq(JSONObject.class));
        assertEquals(1,cached.size());
    }

    /** 公开配置只返回站点endpoint，不能包含私有secret。 */
    @Test
    void cap_public_config_never_leaks_secret() throws Exception {
        properties.setEnabled(true);
        String body=mvc.perform(get("/admin/sys/yz_manager/cap-config")).andReturn().getResponse().getContentAsString();
        assertFalse(body.contains("unit-test-secret"));
        assertEquals("https://cap.example.invalid/test-site/",JSON.parseObject(body).getJSONObject("data").getString("apiEndpoint"));
    }

    /** 连续失败达到原账号阈值后进入15分钟锁，后续不再查询密码。 */
    @Test
    void account_threshold_keeps_original_five_attempt_lock() throws Exception {
        when(managers.getOne(org.mockito.ArgumentMatchers.<Wrapper<YzManager>>any())).thenReturn(account());
        for(int index=0;index<5;index++) assertEquals(500,login("owner","wrong","").getIntValue("code"));
        assertEquals(Duration.ofMinutes(15),lifetimes.get("admin:login:lock:account:owner"));
        assertTrue(login("owner","wrong","").getString("msg").contains("15分钟"));
        verify(managers,times(5)).getOne(org.mockito.ArgumentMatchers.<Wrapper<YzManager>>any());
        assertFalse(cached.keySet().stream().anyMatch(key -> key.matches("[a-f0-9-]{36}")));
    }

    /** 独立IP阈值保留30分钟锁，不能被账号阈值或验证码流程覆盖。 */
    @Test
    void ip_threshold_keeps_original_twenty_attempt_policy() throws Exception {
        when(managers.getOne(org.mockito.ArgumentMatchers.<Wrapper<YzManager>>any())).thenReturn(account());
        when(values.increment(anyString(),eq(1L))).thenAnswer(call -> ((String)call.getArgument(0)).startsWith("admin:login:fail:ip:")?20L:1L);
        assertTrue(login("owner","wrong","").getString("msg").contains("30分钟"));
        assertEquals(Duration.ofMinutes(30),lifetimes.get("admin:login:lock:ip:192.0.2.10"));
        assertTrue(login("owner","wrong","").getString("msg").contains("30分钟"));
        verify(managers).getOne(org.mockito.ArgumentMatchers.<Wrapper<YzManager>>any());
    }

    /** 仅构造合成账号；真实数据库和Redis登录在另行批准的完整宿主验收中执行。 */
    private YzManager account() {
        var user=new YzManager();user.setId("9007199254740993");user.setName("owner");user.setSalt("unit-salt");user.setPassword(Md5Utils.Md5("client-password","unit-salt"));user.setOldPassword("old-digest");return user;
    }

    /** 通过实际MVC form绑定调用原方法，不直接伪造返回R或会话结果。 */
    private JSONObject login(String name,String password,String captcha) throws Exception {
        var response=mvc.perform(post("/admin/sys/yz_manager/login").contentType(MediaType.APPLICATION_FORM_URLENCODED).param("username",name).param("password",password).param("captchaVerification",captcha).with(request -> {request.setRemoteAddr("192.0.2.10");return request;})).andReturn().getResponse();
        assertEquals(200,response.getStatus());return JSON.parseObject(response.getContentAsString());
    }
}
