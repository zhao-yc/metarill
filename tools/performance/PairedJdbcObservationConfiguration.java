package com.yinzon.samplehost;

import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
import org.springframework.boot.web.servlet.FilterRegistrationBean;
import org.springframework.context.annotation.*;
import org.springframework.core.Ordered;
import org.springframework.core.env.Environment;
import org.springframework.web.filter.OncePerRequestFilter;
import java.io.IOException;

/** 仅私有验收宿主显式启用；普通样板、平台和生成项目均无新增必选配置。 */
@Configuration(proxyBeanMethods=false)
@ConditionalOnProperty(name="metarill.paired.sql-observation.enabled",havingValue="true")
public class PairedJdbcObservationConfiguration {
    /** 静态基础设施Bean提前安装，避免数据源先注入业务服务后才被替换。 */
    @Bean(destroyMethod="close") public static PairedJdbcObservationHost pairedJdbcObservationHost(Environment environment)throws IOException{return new PairedJdbcObservationHost(environment);}
    /** 排在业务认证之前，覆盖原登录鉴权SQL；不读取表单正文、不增加角色或绕过原过滤链。 */
    @Bean public FilterRegistrationBean<OncePerRequestFilter> pairedJdbcObservationFilter(PairedJdbcObservationHost host){
        FilterRegistrationBean<OncePerRequestFilter> registration=new FilterRegistrationBean<>(host.filter());registration.setName("pairedJdbcSqlObservation");registration.setOrder(Ordered.HIGHEST_PRECEDENCE+10);registration.setAsyncSupported(true);return registration;
    }
}
