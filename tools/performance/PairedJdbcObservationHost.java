package com.yinzon.samplehost;

import com.fasterxml.jackson.databind.ObjectMapper;
import jakarta.servlet.*;
import jakarta.servlet.http.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.nio.file.attribute.PosixFilePermissions;
import java.security.*;
import java.sql.*;
import java.util.*;
import javax.sql.DataSource;
import org.springframework.beans.BeansException;
import org.springframework.beans.factory.*;
import org.springframework.beans.factory.config.*;
import org.springframework.core.Ordered;
import org.springframework.context.ApplicationListener;
import org.springframework.boot.context.event.ApplicationReadyEvent;
import org.springframework.core.env.Environment;
import org.springframework.web.filter.OncePerRequestFilter;

/** 显式验收配置的唯一桥：包装实际Spring数据源，按请求私有留证，不改变普通业务认证或路由。 */
public final class PairedJdbcObservationHost implements BeanPostProcessor,BeanFactoryAware,SmartInitializingSingleton,Ordered,AutoCloseable,ApplicationListener<ApplicationReadyEvent> {
    private static final String PREFIX="metarill.paired.sql-observation.";
    private final PairedJdbcObservation observer=new PairedJdbcObservation(new MySqlJdbcObservationMetadata());
    private final ObjectMapper json=new ObjectMapper();
    private final byte[] key;private final Path output,base;private final String runId;
    private final int maximumRequests,maximumCommands;
    private final Set<String> expectedSources,wrappedSources=new TreeSet<>(),problems=new TreeSet<>(),labels=new HashSet<>();
    private final List<Map<String,Object>> receipts=new ArrayList<>();
    private ConfigurableListableBeanFactory beans;private int started,completed,observed;private boolean sourcesVerified,closed;

    /** 只接受私有目录所有权标记和32字节密钥；不读连接串、不打开数据源、不创建Bean外的后台任务。 */
    public PairedJdbcObservationHost(Environment environment)throws IOException {
        runId=required(environment,"run-id");if(!runId.matches("[a-f0-9]{32}"))throw new IllegalArgumentException("采集运行标识不合法");
        maximumRequests=budget(environment,"maximum-requests",64,4096);maximumCommands=budget(environment,"maximum-commands",4096,65536);
        expectedSources=new TreeSet<>(List.of(required(environment,"data-source-names").split(",",-1)));
        if(expectedSources.isEmpty()||expectedSources.size()>16||expectedSources.stream().anyMatch(name->!name.matches("[A-Za-z0-9_.-]{1,100}")))throw new IllegalArgumentException("必须明确固定数据源Bean名");
        base=ordinary(Path.of(required(environment,"output-root")),true);Path owner=ordinary(base.resolve(".metarill-paired-observation-owner"),false);
        if(Files.size(owner)>128||!Files.readString(owner,StandardCharsets.UTF_8).equals(runId))throw new IllegalArgumentException("采集目录不属于本轮运行");
        Path keyFile=ordinary(Path.of(required(environment,"key-file")),false);if(Files.size(keyFile)!=32)throw new IllegalArgumentException("采集密钥必须为32字节");
        key=Files.readAllBytes(keyFile);if(key.length!=32){Arrays.fill(key,(byte)0);throw new IllegalArgumentException("采集密钥读取期间发生变化");}
        Path fresh=base.resolve(UUID.randomUUID().toString());
        try{try{Files.createDirectory(fresh,PosixFilePermissions.asFileAttribute(PosixFilePermissions.fromString("rwx------")));}catch(UnsupportedOperationException unsupported){Files.createDirectory(fresh);}output=fresh;writeIndex(false);}
        catch(IOException|RuntimeException failure){Arrays.fill(key,(byte)0);throw failure;}
    }
    /** 固定预算不接受负数、溢出或无限保留。 */
    private static int budget(Environment environment,String name,int fallback,int maximum){int value=environment.getProperty(PREFIX+name,Integer.class,fallback);if(value<1||value>maximum)throw new IllegalArgumentException("采集预算不合法");return value;}
    private static String required(Environment environment,String name){String value=environment.getProperty(PREFIX+name);if(value==null||value.isBlank())throw new IllegalArgumentException("缺少采集设置："+name);return value;}
    /** 逐级拒绝符号链接/reparse路径，不跟随所有权、密钥或输出根跳出当前私有目录。 */
    private static Path ordinary(Path path,boolean directory)throws IOException{
        Path absolute=path.toAbsolutePath().normalize();for(Path part=absolute;part!=null;part=part.getParent())if(Files.isSymbolicLink(part)||Files.readAttributes(part,java.nio.file.attribute.BasicFileAttributes.class,LinkOption.NOFOLLOW_LINKS).isOther())throw new IOException("采集路径必须为普通本地路径");
        if(directory?!Files.isDirectory(absolute,LinkOption.NOFOLLOW_LINKS):!Files.isRegularFile(absolute,LinkOption.NOFOLLOW_LINKS))throw new IOException("采集路径类型不匹配");return absolute.toRealPath();
    }
    @Override public int getOrder(){return Ordered.LOWEST_PRECEDENCE;}
    @Override public void setBeanFactory(BeanFactory factory)throws BeansException{if(!(factory instanceof ConfigurableListableBeanFactory configured))throw new IllegalArgumentException("采集要求可核验的Spring工厂");beans=configured;}
    /** 所有DataSource Bean均过同一桥；不另建池，不接管原Bean的销毁生命周期。 */
    @Override public synchronized Object postProcessAfterInitialization(Object bean,String name)throws BeansException{
        if(!(bean instanceof DataSource source))return bean;if(source instanceof ObservedSource observedSource&&observedSource.host==this)return bean;
        if(sourcesVerified&&!expectedSources.contains(name))problems.add("late-data-source");wrappedSources.add(name);return new ObservedSource(source,this);
    }
    /** 启动前确认实际DataSource集合与批准的固定Bean名一致；不能只包装默认源就宣布全覆盖。 */
    @Override public synchronized void afterSingletonsInstantiated(){
        Set<String> actual=new TreeSet<>(Arrays.asList(beans.getBeanNamesForType(DataSource.class,false,true)));
        for(String name:actual)if(!(beans.getBean(name) instanceof ObservedSource))throw new IllegalStateException("数据源没有经过同一采集桥");
        if(!actual.equals(expectedSources)||!wrappedSources.equals(expectedSources))throw new IllegalStateException("实际数据源与固定采集范围不符");sourcesVerified=true;safeIndex(false);
    }
    /** 只有原Boot全部ApplicationRunner结束后才落私有标记；监听端口和Bean构造完成都不能替代。 */
    @Override public synchronized void onApplicationEvent(ApplicationReadyEvent event){
        if(event.getApplicationContext().getBeanFactory()!=beans)return;
        if(closed||!sourcesVerified){problems.add("ready-before-sources");return;}
        Path temporary=null;
        try{
            ordinary(base,true);Path ready=base.resolve(".metarill-paired-application-ready");if(Files.exists(ready,LinkOption.NOFOLLOW_LINKS))throw new IOException("重复就绪标记");
            try{temporary=Files.createTempFile(base,".ready-",".tmp",PosixFilePermissions.asFileAttribute(PosixFilePermissions.fromString("rw-------")));}catch(UnsupportedOperationException unsupported){temporary=Files.createTempFile(base,".ready-",".tmp");}
            Files.write(temporary,json.writeValueAsBytes(Map.of("format","metarill-paired-java-ready-v1","runId",runId,"instance",output.getFileName().toString())));ordinary(base,true);Files.move(temporary,ready,StandardCopyOption.ATOMIC_MOVE);temporary=null;
        }catch(IOException|RuntimeException failure){problems.add("ready-io-failure");safeIndex(false);}
        finally{if(temporary!=null)try{Files.deleteIfExists(temporary);}catch(IOException failure){problems.add("ready-io-failure");}}
    }
    /** 原池负责实际借还；指定用户/口令的原重载原样传递，观察器不读取或保存这些参数。 */
    private static final class ObservedSource implements DataSource{
        private final PairedJdbcObservationHost host;private final DataSource target;
        ObservedSource(DataSource source,PairedJdbcObservationHost host){target=source;this.host=host;}
        @Override public Connection getConnection()throws SQLException{return host.observer.wrap(target.getConnection());}
        @Override public Connection getConnection(String user,String password)throws SQLException{return host.observer.wrap(target.getConnection(user,password));}
        @Override public <T>T unwrap(Class<T> type)throws SQLException{host.observer.sourceEscape();return target.unwrap(type);}
        @Override public boolean isWrapperFor(Class<?> type)throws SQLException{return target.isWrapperFor(type);}
        @Override public PrintWriter getLogWriter()throws SQLException{return target.getLogWriter();}
        @Override public void setLogWriter(PrintWriter writer)throws SQLException{target.setLogWriter(writer);}
        @Override public void setLoginTimeout(int seconds)throws SQLException{target.setLoginTimeout(seconds);}
        @Override public int getLoginTimeout()throws SQLException{return target.getLoginTimeout();}
        @Override public java.util.logging.Logger getParentLogger()throws SQLFeatureNotSupportedException{return target.getParentLogger();}
        @Override public ConnectionBuilder createConnectionBuilder()throws SQLException{host.observer.sourceEscape();return target.createConnectionBuilder();}
        @Override public ShardingKeyBuilder createShardingKeyBuilder()throws SQLException{host.observer.sourceEscape();return target.createShardingKeyBuilder();}
    }
    /** 创建请求过滤器，不增加HTTP端点或返回头；原链始终由原宿主完成。 */
    public OncePerRequestFilter filter(){return new OncePerRequestFilter(){
        @Override protected void doFilterInternal(HttpServletRequest request,HttpServletResponse response,FilterChain chain)throws ServletException,IOException{capture(request,response,chain);}
    };}
    /** 限制采集总量，失败只使证据无效，不把采集错误改成业务响应或重试请求。 */
    private void capture(HttpServletRequest request,HttpServletResponse response,FilterChain chain)throws ServletException,IOException{
        String label=request.getHeader("X-Metarill-Sql-Sample");String id=UUID.randomUUID().toString();PairedJdbcObservation.Scope scope=null;boolean retained;
        synchronized(this){started++;retained=!closed&&sourcesVerified&&observed<maximumRequests;if(retained)observed++;else problems.add(closed?"late-request":!sourcesVerified?"sources-not-verified":"request-limit");}
        if(retained){try{scope=observer.begin(key,maximumCommands);if(label!=null){Enumeration<String> supplied=request.getHeaders("X-Metarill-Sql-Sample");if(supplied.hasMoreElements())supplied.nextElement();if(supplied.hasMoreElements()||!label.matches("[a-f0-9]{32}")){observer.rejectHost(scope,"invalid-sample-label");label=null;}else synchronized(this){if(!labels.add(label))observer.rejectHost(scope,"duplicate-sample-label");}}}catch(RuntimeException failure){synchronized(this){problems.add("scope-setup-failure");}}}
        Throwable businessFailure=null;
        try{chain.doFilter(request,response);}catch(IOException|ServletException|RuntimeException|Error failure){businessFailure=failure;throw failure;}
        finally{
            if(scope!=null){
                if(request.isAsyncStarted())observer.rejectHost(scope,"async-request");scope.close();var snapshot=scope.snapshot();
                synchronized(this){
                    if(!snapshot.internallyConsistent())problems.add("incomplete-request");
                    try{
                        Map<String,Object> record=new TreeMap<>();record.put("format","metarill-paired-jdbc-http-v2");record.put("runId",runId);record.put("id",id);record.put("sample",label);record.put("statusAtFilterExit",response.getStatus());record.put("chainSucceeded",businessFailure==null);record.put("errorType",businessFailure==null?null:businessFailure.getClass().getName());record.put("trace",snapshot);
                        byte[] bytes=json.writeValueAsBytes(record);atomic(id+".json",bytes);receipts.add(Map.of("id",id,"sha256",HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(bytes))));
                    }catch(IOException|RuntimeException|NoSuchAlgorithmException failure){problems.add("trace-io-failure");}
                }
            }
            synchronized(this){completed++;safeIndex(closed);}
        }
    }
    /** 每个宿主实例写独立私有目录；临时文件固定由本次写入创建，不接受客户端文件名。 */
    private void atomic(String name,byte[] bytes)throws IOException{
        ordinary(output,true);Path temporary=Files.createTempFile(output,".trace-",".tmp");try{Files.write(temporary,bytes);ordinary(output,true);Files.move(temporary,output.resolve(name),StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING);}finally{Files.deleteIfExists(temporary);}
    }
    /** 私有索引必须同时核请求数量、固定数据源和finalized；单个完整trace不代表整轮采集完成。 */
    private void writeIndex(boolean finalized)throws IOException{
        Map<String,Object> index=new TreeMap<>();index.put("format","metarill-paired-jdbc-index-v2");index.put("runId",runId);index.put("started",started);index.put("completed",completed);index.put("observed",observed);index.put("sourcesVerified",sourcesVerified);index.put("dataSources",List.copyOf(wrappedSources));index.put("problems",Set.copyOf(problems));index.put("requests",List.copyOf(receipts));index.put("finalized",finalized);index.put("internallyConsistent",finalized&&sourcesVerified&&started==completed&&completed==observed&&observed==receipts.size()&&problems.isEmpty());atomic("index.json",json.writeValueAsBytes(index));
    }
    private void safeIndex(boolean finalized){try{writeIndex(finalized);}catch(IOException|RuntimeException failure){problems.add("index-io-failure");}}
    /** 关闭先清零长期副本；宿主尚有请求则索引明确无效，不能用running索引宣告成功。 */
    @Override public synchronized void close(){if(closed)return;closed=true;Arrays.fill(key,(byte)0);if(started!=completed)problems.add("pending-request");safeIndex(true);}
}
