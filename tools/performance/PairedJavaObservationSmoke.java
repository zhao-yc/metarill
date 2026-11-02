package com.yinzon.samplehost;

import java.io.*;
import java.lang.reflect.Proxy;
import java.nio.file.*;
import java.security.*;
import java.sql.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import javax.sql.DataSource;
import com.fasterxml.jackson.databind.*;
import org.springframework.boot.*;
import org.springframework.context.annotation.*;
import org.springframework.jdbc.datasource.AbstractDataSource;

/** 禁网Linux探针只加载新JAR的原采集桥；不启动原业务、不开数据库、不生成真实秘密。 */
@Configuration(proxyBeanMethods=false)
@Import(PairedJdbcObservationConfiguration.class)
public class PairedJavaObservationSmoke {
    private static final AtomicBoolean RUNNER_COMPLETED=new AtomicBoolean();
    private static Path output;
    /** DataSource仅供真实Bean后处理核名；任何借连接均明确失败。 */
    @Bean public DataSource dataSource(){return new AbstractDataSource(){
        @Override public Connection getConnection(){throw new AssertionError("禁网探针不得打开数据库");}
        @Override public Connection getConnection(String user,String password){throw new AssertionError("禁网探针不得打开数据库");}
    };}
    /** 原Boot Ready事件必须晚于runner，不以刷新context或启动监听代替。 */
    @Bean public ApplicationRunner originalRunner(){return arguments->{if(Files.exists(output.resolve(".metarill-paired-application-ready")))throw new IllegalStateException("就绪信号早于runner");RUNNER_COMPLETED.set(true);};}
    /** 先核镜像内完整JAR，再用真实SpringApplication触发Ready及关闭索引。 */
    public static void main(String[] args)throws Exception{
        if(args.length!=3||!args[1].matches("[a-f0-9]{32}")||!args[2].matches("[a-f0-9]{64}"))throw new IllegalArgumentException("探针参数不符");
        MessageDigest hash=MessageDigest.getInstance("SHA-256");try(InputStream input=Files.newInputStream(Path.of("/app/application.jar"))){byte[] buffer=new byte[1024*1024];for(int size;(size=input.read(buffer))>=0;)hash.update(buffer,0,size);}
        if(!HexFormat.of().formatHex(hash.digest()).equals(args[2]))throw new IllegalStateException("完整JAR摘要不符");
        Path root=Path.of(args[0]);output=root.resolve("output");String prefix="metarill.paired.sql-observation.";
        var application=new SpringApplication(PairedJavaObservationSmoke.class);application.setWebApplicationType(WebApplicationType.NONE);application.setRegisterShutdownHook(false);application.setBannerMode(Banner.Mode.OFF);application.setLogStartupInfo(false);
        Map<String,Object> properties=new HashMap<>();properties.put(prefix+"enabled","true");properties.put(prefix+"run-id",args[1]);properties.put(prefix+"data-source-names","dataSource");properties.put(prefix+"key-file",root.resolve("key.bin").toString());properties.put(prefix+"output-root",output.toString());application.setDefaultProperties(properties);
        ObjectMapper json=new ObjectMapper();Path instance;
        try(var context=application.run()){
            if(!context.isActive()||!RUNNER_COMPLETED.get())throw new IllegalStateException("原Boot runner未完成");JsonNode ready=json.readTree(Files.readAllBytes(output.resolve(".metarill-paired-application-ready")));
            if(!ready.path("runId").asText().equals(args[1]))throw new IllegalStateException("就绪归属不符");instance=output.resolve(ready.path("instance").asText());
        }
        JsonNode index=json.readTree(Files.readAllBytes(instance.resolve("index.json")));if(!index.path("finalized").asBoolean()||!index.path("internallyConsistent").asBoolean()||index.path("started").asInt()!=0)throw new IllegalStateException("探针关闭索引不完整");
        metadata();System.out.println("结果："+json.writeValueAsString(Map.of("format","metarill-java-observation-smoke-v1","runId",args[1],"jarSha256",args[2],"realBootRunnerBeforeReady",true,"finalizedZeroHttp",true,"localMetadataReadsConsistent",true,"databaseOpened",false,"performanceAccepted",false)));
    }
    /** 另线程读取固定本地元数据并保持连接回指；不发送SQL或访问真实驱动会话。 */
    private static void metadata()throws Exception{
        var observer=new PairedJdbcObservation(new PairedJdbcObservation.Metadata(){@Override public List<PairedJdbcObservation.Binding> parameters(PreparedStatement command){return List.of();}@Override public boolean serverPrepared(PreparedStatement command){return false;}});
        var metadata=(DatabaseMetaData)Proxy.newProxyInstance(PairedJavaObservationSmoke.class.getClassLoader(),new Class<?>[]{DatabaseMetaData.class},(proxy,method,args)->switch(method.getName()){case "getURL"->"jdbc:mysql://127.0.0.1:1/offline";case "supportsMultipleResultSets"->true;default->null;});
        var raw=(Connection)Proxy.newProxyInstance(PairedJavaObservationSmoke.class.getClassLoader(),new Class<?>[]{Connection.class},(proxy,method,args)->method.getName().equals("getMetaData")?metadata:null);Connection connection=observer.wrap(raw);var scope=observer.begin(new byte[32],8);
        try(var executor=Executors.newSingleThreadExecutor()){if(!executor.submit(()->connection.getMetaData().getURL()).get().equals("jdbc:mysql://127.0.0.1:1/offline")||!connection.getMetaData().supportsMultipleResultSets()||connection.getMetaData().getConnection()!=connection)throw new IllegalStateException("元数据回指不符");}finally{scope.close();}
        if(!scope.snapshot().internallyConsistent())throw new IllegalStateException("本地元数据误报逃逸");
    }
}
