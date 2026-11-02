package com.yinzon.samplehost;

import com.fasterxml.jackson.databind.*;
import com.mysql.cj.*;
import com.mysql.cj.conf.*;
import com.mysql.cj.jdbc.JdbcPreparedStatement;
import jakarta.servlet.*;
import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.sql.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicInteger;
import javax.sql.DataSource;
import javax.sql.rowset.*;
import org.junit.jupiter.api.*;
import org.junit.jupiter.api.io.TempDir;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import org.springframework.boot.web.servlet.FilterRegistrationBean;
import org.springframework.context.annotation.AnnotationConfigApplicationContext;
import org.springframework.core.env.MapPropertySource;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.datasource.*;
import org.springframework.mock.web.*;
import org.springframework.transaction.support.TransactionTemplate;
import static org.junit.jupiter.api.Assertions.*;

/** 真实Spring Bean后处理／事务／Servlet过滤链，网络和数据库执行为明确替身。 */
class PairedJdbcObservationHostTests {
    @TempDir Path temporary;
    private static final ObjectMapper JSON=new ObjectMapper();
    private static final String RUN="1234567890abcdef1234567890abcdef",PREFIX="metarill.paired.sql-observation.";
    private final TrackingSource source=new TrackingSource();

    /** 测试私有目录和合成key不用于真实部署；禁用时配置路径故意不存在。 */
    private Map<String,Object> properties(boolean enabled)throws IOException{
        Path output=temporary.resolve("output");if(!Files.exists(output)){Files.createDirectory(output);Files.writeString(output.resolve(".metarill-paired-observation-owner"),RUN,StandardCharsets.UTF_8);byte[] key=new byte[32];Arrays.fill(key,(byte)11);Files.write(temporary.resolve("key.bin"),key);}
        Map<String,Object> values=new HashMap<>();values.put(PREFIX+"enabled",Boolean.toString(enabled));values.put(PREFIX+"run-id",RUN);values.put(PREFIX+"output-root",output.toString());values.put(PREFIX+"key-file",temporary.resolve("key.bin").toString());values.put(PREFIX+"data-source-names","dataSource");return values;
    }
    private AnnotationConfigApplicationContext context(Map<String,Object> properties){
        var context=new AnnotationConfigApplicationContext();context.getEnvironment().getPropertySources().addFirst(new MapPropertySource("test",properties));context.register(PairedJdbcObservationConfiguration.class);
        context.registerBean("dataSource",TrackingSource.class,()->source,definition->definition.setDestroyMethodName("close"));return context;
    }
    private static Filter filter(AnnotationConfigApplicationContext context){return context.getBean(FilterRegistrationBean.class).getFilter();}
    private MockHttpServletResponse request(Filter filter,String label,FilterChain chain)throws Exception{
        var request=new MockHttpServletRequest("POST","/api/original/fixture");if(label!=null)request.addHeader("X-Metarill-Sql-Sample",label);request.setContent("password=PRIVATE-REQUEST-BODY".getBytes(StandardCharsets.UTF_8));var response=new MockHttpServletResponse();filter.doFilter(request,response,chain);return response;
    }
    private Path output()throws IOException{try(var list=Files.list(temporary.resolve("output"))){return list.filter(Files::isDirectory).findFirst().orElseThrow();}}
    private JsonNode index()throws IOException{return JSON.readTree(output().resolve("index.json").toFile());}
    private JsonNode trace()throws IOException{try(var list=Files.list(output())){Path file=list.filter(path->path.getFileName().toString().endsWith(".json")&&!path.getFileName().toString().equals("index.json")).findFirst().orElseThrow();return JSON.readTree(file.toFile());}}

    /** 未配置和明确关闭都保持原数据源，且不读取私有路径。 */
    @ParameterizedTest @ValueSource(strings={"false","absent"})
    void disabled_configuration_does_not_read_private_files_or_wrap_original_source(String value)throws Exception{
        var properties=new HashMap<String,Object>();if(!value.equals("absent"))properties.put(PREFIX+"enabled",value);try(var context=context(properties)){context.refresh();assertSame(source,context.getBean(DataSource.class));assertFalse(context.containsBean("pairedJdbcObservationHost"));assertEquals(0,source.opens.get());}assertEquals(1,source.closes.get());assertFalse(Files.exists(temporary.resolve("output")));
    }
    /** 真实Spring事务下核对三条命令、同一连接及原响应正文。 */
    @Test void actual_spring_transaction_jdbc_template_and_direct_statement_share_one_request_and_connection()throws Exception{
        var context=context(properties(true));context.refresh();DataSource wrapped=context.getBean(DataSource.class);assertNotSame(source,wrapped);JdbcTemplate jdbc=new JdbcTemplate(wrapped);var transaction=new TransactionTemplate(new DataSourceTransactionManager(wrapped));transaction.setIsolationLevel(Connection.TRANSACTION_READ_COMMITTED);
        var response=request(filter(context),RUN,(request,servletResponse)->{
            transaction.executeWithoutResult(status->{assertEquals(1,jdbc.queryForObject("SELECT 1",Integer.class));assertEquals(1,jdbc.update("UPDATE fixture SET name=?","PRIVATE-PARAMETER"));Connection connection=DataSourceUtils.getConnection(wrapped);try(Statement statement=connection.createStatement();ResultSet reader=statement.executeQuery("SELECT 2")){assertTrue(reader.next());}catch(SQLException failure){throw new IllegalStateException(failure);}finally{DataSourceUtils.releaseConnection(connection,wrapped);}});
            assertArrayEquals("password=PRIVATE-REQUEST-BODY".getBytes(StandardCharsets.UTF_8),request.getInputStream().readAllBytes());servletResponse.setCharacterEncoding("UTF-8");servletResponse.getWriter().write("原业务返回");
        });
        assertEquals("原业务返回",response.getContentAsString());JsonNode record=trace(),sql=record.path("trace");assertTrue(sql.path("internallyConsistent").asBoolean());assertEquals(3,sql.path("commands").size());assertEquals(5,sql.path("transactions").size());Set<Long> connections=new HashSet<>(),transactions=new HashSet<>();for(JsonNode command:sql.path("commands")){connections.add(command.path("connection").asLong());transactions.add(command.path("transaction").asLong());assertTrue(command.path("succeeded").asBoolean());}assertEquals(1,connections.size());assertEquals(1,transactions.size());assertFalse(record.toString().contains("PRIVATE-"));assertEquals(RUN,record.path("sample").asText());assertFalse(index().path("finalized").asBoolean());
        context.close();assertTrue(index().path("internallyConsistent").asBoolean());assertEquals(1,source.opens.get());assertEquals(1,source.connectionCloses.get());assertEquals(1,source.commits.get());assertEquals(0,source.rollbacks.get());assertEquals(1,source.closes.get());exportSyntheticArtifacts();
    }
    /** 仅显式测试属性可保留合成采集供独立读取器核对；新目录不覆盖，绝不导出key。 */
    private void exportSyntheticArtifacts()throws IOException{
        String configured=System.getProperty("metarill.paired.test-evidence");if(configured==null)return;
        Path target=Path.of(configured).toAbsolutePath().normalize();assertFalse(Files.exists(target));
        for(Path part=target.getParent();part!=null;part=part.getParent())assertFalse(Files.isSymbolicLink(part)||Files.readAttributes(part,java.nio.file.attribute.BasicFileAttributes.class,LinkOption.NOFOLLOW_LINKS).isOther());
        Files.createDirectory(target);
        try(var list=Files.list(output())){for(Path file:list.toList()){assertTrue(Files.isRegularFile(file,LinkOption.NOFOLLOW_LINKS));Files.copy(file,target.resolve(file.getFileName()));}}
    }
    /** 原Servlet异常对象保持，私有正文不写入采集。 */
    @Test void original_servlet_exception_is_preserved_without_its_message_in_trace()throws Exception{
        var context=context(properties(true));context.refresh();ServletException original=new ServletException("PRIVATE-ERROR-MESSAGE");assertSame(original,assertThrows(ServletException.class,()->request(filter(context),RUN,(request,response)->{throw original;})));context.close();assertFalse(trace().path("chainSucceeded").asBoolean());assertFalse(trace().toString().contains("PRIVATE-ERROR"));assertTrue(index().path("internallyConsistent").asBoolean());
    }
    /** 原业务异常仍触发原事务回滚，失败业务可以有完整采集证据。 */
    @Test void rollback_keeps_original_spring_transaction_and_complete_evidence()throws Exception{
        var context=context(properties(true));context.refresh();DataSource wrapped=context.getBean(DataSource.class);var transaction=new TransactionTemplate(new DataSourceTransactionManager(wrapped));var jdbc=new JdbcTemplate(wrapped);RuntimeException original=new IllegalStateException("PRIVATE-BUSINESS");
        assertSame(original,assertThrows(IllegalStateException.class,()->request(filter(context),RUN,(request,response)->transaction.executeWithoutResult(status->{jdbc.update("UPDATE fixture SET name=?","原值");throw original;}))));context.close();assertEquals(1,source.rollbacks.get());assertEquals(0,source.commits.get());assertTrue(index().path("internallyConsistent").asBoolean());assertTrue(trace().path("trace").path("internallyConsistent").asBoolean());
    }
    /** 批准名单外的数据源不能通过准备检查，检查本身不开连接。 */
    @Test void unexpected_extra_data_source_fails_readiness_without_opening_a_connection()throws Exception{
        var context=context(properties(true));context.registerBean("otherSource",DataSource.class,TrackingSource::new);assertThrows(RuntimeException.class,context::refresh);context.close();assertEquals(0,source.opens.get());assertFalse(index().path("sourcesVerified").asBoolean());
    }
    /** 声明的数据源缺失时拒绝准备成功。 */
    @Test void missing_expected_data_source_fails_readiness()throws Exception{
        var properties=properties(true);properties.put(PREFIX+"data-source-names","dataSource,missing");var context=context(properties);assertThrows(RuntimeException.class,context::refresh);context.close();assertEquals(0,source.opens.get());
    }
    /** 超过样本预算继续原链，整轮索引不能报告完整。 */
    @Test void sample_budget_does_not_change_original_chain_but_final_index_rejects_missing_coverage()throws Exception{
        var properties=properties(true);properties.put(PREFIX+"maximum-requests","1");var context=context(properties);context.refresh();AtomicInteger calls=new AtomicInteger();request(filter(context),RUN,(request,response)->calls.incrementAndGet());request(filter(context),null,(request,response)->calls.incrementAndGet());context.close();assertEquals(2,calls.get());assertEquals(2,index().path("completed").asInt());assertEquals(1,index().path("observed").asInt());assertFalse(index().path("internallyConsistent").asBoolean());assertTrue(index().path("problems").toString().contains("request-limit"));
    }
    /** 重复或路径形态的客户端标记不能成为输出文件名。 */
    @Test void duplicate_or_invalid_sample_labels_never_become_output_file_paths()throws Exception{
        var context=context(properties(true));context.refresh();AtomicInteger calls=new AtomicInteger();request(filter(context),RUN,(request,response)->calls.incrementAndGet());request(filter(context),RUN,(request,response)->calls.incrementAndGet());request(filter(context),"../../PRIVATE",(request,response)->calls.incrementAndGet());context.close();assertEquals(3,calls.get());assertFalse(index().path("internallyConsistent").asBoolean());try(var list=Files.list(output())){assertEquals(4,list.count());}assertFalse(index().toString().contains("PRIVATE"));
    }
    /** 私有索引不可写时保留原业务执行，不伪造最终索引。 */
    @Test void evidence_io_failure_does_not_replace_original_business_result()throws Exception{
        var context=context(properties(true));context.refresh();Path index=output().resolve("index.json");assertTrue(index.normalize().startsWith(temporary.normalize()));Files.delete(index);Files.createDirectory(index);AtomicInteger calls=new AtomicInteger();request(filter(context),RUN,(request,response)->{calls.incrementAndGet();response.getWriter().write("原响应");});context.close();assertEquals(1,calls.get());assertTrue(Files.isDirectory(index));assertEquals(0,source.opens.get());
    }
    /** 所有权、密钥、预算或固定源声明错误均在连接前拒绝。 */
    @ParameterizedTest @ValueSource(strings={"key","owner","budget","missing-source-names"})
    void wrong_owner_or_key_length_fails_before_connecting(String defect)throws Exception{
        var properties=properties(true);switch(defect){case "key"->Files.write(temporary.resolve("key.bin"),new byte[31]);case "owner"->Files.writeString(temporary.resolve("output/.metarill-paired-observation-owner"),"other");case "budget"->properties.put(PREFIX+"maximum-requests","0");default->properties.remove(PREFIX+"data-source-names");}var context=context(properties);assertThrows(RuntimeException.class,context::refresh);context.close();assertEquals(0,source.opens.get());
    }
    /** 数据源逃逸不改变原unwrap结果，但采集不得再报告完整。 */
    @Test void data_source_unwrap_invalidates_request_and_future_scope()throws Exception{
        var context=context(properties(true));context.refresh();DataSource wrapped=context.getBean(DataSource.class);request(filter(context),RUN,(request,response)->{try{assertSame(source,wrapped.unwrap(DataSource.class));}catch(SQLException failure){throw new ServletException(failure);}});context.close();assertFalse(trace().path("trace").path("internallyConsistent").asBoolean());assertFalse(index().path("internallyConsistent").asBoolean());
    }
    /** 两个明确数据源和原带凭据重载都经过同一桥，凭据不留证。 */
    @Test void all_declared_sources_and_both_connection_overloads_are_wrapped()throws Exception{
        var properties=properties(true);properties.put(PREFIX+"data-source-names","dataSource,otherSource");var context=context(properties);TrackingSource other=new TrackingSource();context.registerBean("otherSource",TrackingSource.class,()->other,definition->definition.setDestroyMethodName("close"));context.refresh();
        request(filter(context),RUN,(request,response)->{try(Connection first=context.getBean("dataSource",DataSource.class).getConnection("PRIVATE-USER","PRIVATE-PASSWORD");Connection second=context.getBean("otherSource",DataSource.class).getConnection();Statement one=first.createStatement();Statement two=second.createStatement()){one.executeQuery("SELECT 1").close();two.executeQuery("SELECT 2").close();}catch(SQLException failure){throw new ServletException(failure);}});context.close();
        assertTrue(index().path("internallyConsistent").asBoolean());assertEquals(2,index().path("dataSources").size());assertEquals(1,source.opens.get());assertEquals(1,source.withCredentials.get());assertEquals(1,other.opens.get());assertEquals(1,source.closes.get());assertEquals(1,other.closes.get());assertFalse(trace().toString().contains("PRIVATE-"));
    }
    /** 异步状态保留，未覆盖的后续执行明确使采集无效。 */
    @Test void async_request_keeps_original_async_state_and_is_not_marked_complete()throws Exception{
        var context=context(properties(true));context.refresh();var request=new MockHttpServletRequest("GET","/api/original/async");request.setAsyncSupported(true);var response=new MockHttpServletResponse();filter(context).doFilter(request,response,(original,result)->original.startAsync(original,result));assertTrue(request.isAsyncStarted());request.getAsyncContext().complete();context.close();assertFalse(index().path("internallyConsistent").asBoolean());assertTrue(trace().path("trace").path("problems").toString().contains("async-request"));
    }
    /** 多个采样头保留原请求，但拒绝含歧义的关联证明。 */
    @Test void ambiguous_transport_label_is_rejected_as_evidence_without_blocking_chain()throws Exception{
        var context=context(properties(true));context.refresh();var request=new MockHttpServletRequest("GET","/api/original");request.addHeader("X-Metarill-Sql-Sample",RUN);request.addHeader("X-Metarill-Sql-Sample","abcdefabcdefabcdefabcdefabcdefab");AtomicInteger calls=new AtomicInteger();filter(context).doFilter(request,new MockHttpServletResponse(),(original,result)->calls.incrementAndGet());context.close();assertEquals(1,calls.get());assertFalse(index().path("internallyConsistent").asBoolean());assertTrue(trace().path("trace").path("problems").toString().contains("invalid-sample-label"));
    }
    /** 原Boot无脚本初始化不额外开连接，后续请求仍正常采集。 */
    @Test void default_boot_sql_initializer_without_scripts_does_not_change_request_evidence()throws Exception{
        var context=context(properties(true));context.register(org.springframework.boot.autoconfigure.sql.init.SqlInitializationAutoConfiguration.class);context.refresh();int startup=source.opens.get();assertEquals(0,startup);request(filter(context),RUN,(request,response)->assertEquals(1,new JdbcTemplate(context.getBean(DataSource.class)).queryForObject("SELECT 1",Integer.class)));context.close();assertTrue(trace().path("trace").path("internallyConsistent").asBoolean());assertEquals(startup+1,source.opens.get());
    }
    /** 真实Spring错误翻译读取产品名，元数据连接回指不能泄漏原连接。 */
    @Test void real_spring_error_code_metadata_keeps_the_same_wrapped_connection()throws Exception{
        var context=context(properties(true));context.refresh();DataSource wrapped=context.getBean(DataSource.class);request(filter(context),RUN,(request,response)->{
            assertNotNull(new org.springframework.jdbc.support.SQLErrorCodeSQLExceptionTranslator(wrapped).getSqlErrorCodes());
            try(Connection connection=wrapped.getConnection()){DatabaseMetaData metadata=connection.getMetaData();assertSame(connection,metadata.getConnection());assertEquals("MySQL",metadata.getDatabaseProductName());}catch(SQLException failure){throw new ServletException(failure);}
        });context.close();assertTrue(index().path("internallyConsistent").asBoolean());assertTrue(trace().path("trace").path("internallyConsistent").asBoolean());
    }
    /** 真实SpringApplication在ApplicationRunner完成后发布Ready事件，不打开DataSource。 */
    @Test void actual_boot_ready_marker_follows_original_application_runners()throws Exception{
        var configured=properties(true);Path marker=temporary.resolve("output/.metarill-paired-application-ready");var completed=new java.util.concurrent.atomic.AtomicBoolean();
        var application=new org.springframework.boot.SpringApplication(PairedJdbcObservationConfiguration.class);application.setWebApplicationType(org.springframework.boot.WebApplicationType.NONE);application.setBannerMode(org.springframework.boot.Banner.Mode.OFF);application.setLogStartupInfo(false);application.setRegisterShutdownHook(false);application.setDefaultProperties(configured);
        application.addInitializers(context->{var generic=(org.springframework.context.support.GenericApplicationContext)context;generic.registerBean("dataSource",TrackingSource.class,()->source);generic.registerBean("originalRunner",org.springframework.boot.ApplicationRunner.class,()->arguments->{assertFalse(Files.exists(marker));completed.set(true);});});
        try(var context=application.run()){
            assertTrue(completed.get());assertTrue(Files.isRegularFile(marker));JsonNode ready=JSON.readTree(Files.readAllBytes(marker));assertEquals(RUN,ready.path("runId").asText());assertTrue(Files.isDirectory(marker.getParent().resolve(ready.path("instance").asText())));assertEquals(0,source.opens.get());assertTrue(context.isActive());
        }
    }
    /** 单纯refresh及另一个context的事件都不代表本宿主的原Boot启动已完成。 */
    @Test void bean_refresh_and_foreign_ready_event_do_not_write_marker()throws Exception{
        var context=context(properties(true));context.refresh();Path marker=temporary.resolve("output/.metarill-paired-application-ready");assertFalse(Files.exists(marker));
        try(var other=new AnnotationConfigApplicationContext()){
            var event=new org.springframework.boot.context.event.ApplicationReadyEvent(new org.springframework.boot.SpringApplication(PairedJdbcObservationConfiguration.class),new String[0],other,java.time.Duration.ZERO);context.getBean(PairedJdbcObservationHost.class).onApplicationEvent(event);assertFalse(Files.exists(marker));
        }context.close();
    }
    /** 明确替身只记录实际框架API；NativeQueryBindings为真实固定驱动对象，无网络会话。 */
    static final class TrackingSource extends AbstractDataSource implements AutoCloseable{
        final AtomicInteger withCredentials=new AtomicInteger(),opens=new AtomicInteger(),connectionCloses=new AtomicInteger(),closes=new AtomicInteger(),commits=new AtomicInteger(),rollbacks=new AtomicInteger();
        @Override public Connection getConnection(){opens.incrementAndGet();return connection();}@Override public Connection getConnection(String user,String password){assertEquals("PRIVATE-USER",user);assertEquals("PRIVATE-PASSWORD",password);withCredentials.incrementAndGet();return getConnection();}@Override public void close(){closes.incrementAndGet();}
        @Override public <T>T unwrap(Class<T> type)throws SQLException{if(type.isInstance(this))return type.cast(this);throw new SQLException("未知类型");}
        /** 每次借出连接保留自己的自动提交／只读／隔离状态，真实事务管理器直接使用。 */
        private Connection connection(){
            boolean[] auto={true},readOnly={false};int[] isolation={Connection.TRANSACTION_REPEATABLE_READ};
            return (Connection)Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{Connection.class},(proxy,method,args)->switch(method.getName()){
                case "getAutoCommit"->auto[0];case "setAutoCommit"->{auto[0]=(Boolean)args[0];yield null;}case "isReadOnly"->readOnly[0];case "setReadOnly"->{readOnly[0]=(Boolean)args[0];yield null;}case "getTransactionIsolation"->isolation[0];case "setTransactionIsolation"->{isolation[0]=(Integer)args[0];yield null;}
                case "commit"->{commits.incrementAndGet();yield null;}case "rollback"->{rollbacks.incrementAndGet();yield null;}case "close"->{connectionCloses.incrementAndGet();yield null;}case "createStatement"->statement((Connection)proxy,null);case "prepareStatement"->statement((Connection)proxy,(String)args[0]);case "getMetaData"->Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{DatabaseMetaData.class},(metadata,call,parameters)->switch(call.getName()){case "getDatabaseProductName"->"MySQL";case "getConnection"->proxy;default->defaultValue(call.getReturnType());});case "isClosed"->false;case "toString"->"明确替身连接";case "hashCode"->System.identityHashCode(proxy);case "equals"->proxy==args[0];default->defaultValue(method.getReturnType());
            });
        }
        /** 原执行只返回合成一行；绑定通过真实驱动setter实现，不解析或执行SQL。 */
        private Statement statement(Connection connection,String sql){
            NativeQueryBindings bindings=sql==null?null:bindings();Class<?> type=sql==null?Statement.class:JdbcPreparedStatement.class;
            return (Statement)Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{type},(proxy,method,args)->switch(method.getName()){
                case "getQueryBindings"->bindings;case "setString"->{bindings.setString((Integer)args[0]-1,(String)args[1]);yield null;}case "setObject"->{bindings.setObject((Integer)args[0]-1,args[1]);yield null;}case "executeUpdate"->1;case "executeQuery"->rows();case "getConnection"->connection;case "toString"->"明确替身命令";case "hashCode"->System.identityHashCode(proxy);case "equals"->proxy==args[0];default->defaultValue(method.getReturnType());
            });
        }
        private static Object defaultValue(Class<?> type){if(type==boolean.class)return false;if(type==int.class)return 0;if(type==long.class)return 0L;return null;}
        private static CachedRowSet rows()throws SQLException{var rows=RowSetProvider.newFactory().createCachedRowSet();var metadata=new RowSetMetaDataImpl();metadata.setColumnCount(1);metadata.setColumnName(1,"value");metadata.setColumnType(1,Types.INTEGER);rows.setMetaData(metadata);rows.moveToInsertRow();rows.updateInt(1,1);rows.insertRow();rows.moveToCurrentRow();rows.beforeFirst();return rows;}
        /** 本地协议仅满足驱动值编码初始化；没有connect、socket或服务器能力声明。 */
        private static NativeQueryBindings bindings(){
            DefaultPropertySet properties=new DefaultPropertySet();properties.initializeProperties(new Properties());var server=new com.mysql.cj.protocol.a.NativeServerSession(properties);server.setCharsetSettings((CharsetSettings)Proxy.newProxyInstance(TrackingSource.class.getClassLoader(),new Class<?>[]{CharsetSettings.class},(proxy,method,args)->{if(method.getName().equals("getRequiresEscapingEncoder"))return false;throw new AssertionError("未提供其它字符集能力");}));var protocolValue=new com.mysql.cj.protocol.a.NativeProtocol(new com.mysql.cj.log.NullLogger("offline"));
            NativeSession session=new NativeSession(ConnectionUrl.getConnectionUrlInstance("jdbc:mysql://127.0.0.1:1/offline",new Properties()).getMainHost(),properties){@Override public com.mysql.cj.protocol.a.NativeProtocol getProtocol(){return protocolValue;}@Override public com.mysql.cj.protocol.ServerSession getServerSession(){return server;}};
            return new NativeQueryBindings(1,session,NativeQueryBindValue::new);
        }
    }
}
