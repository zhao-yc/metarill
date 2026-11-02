package com.yinzon.samplehost;

import java.lang.reflect.*;
import java.sql.*;
import java.util.*;
import java.util.concurrent.*;
import org.apache.ibatis.executor.resultset.DefaultResultSetHandler;
import org.apache.ibatis.builder.StaticSqlSource;
import org.apache.ibatis.mapping.*;
import org.apache.ibatis.session.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

/** 固定原MyBatis3.5.19实际结果处理器读取元数据，不执行网络或数据库。 */
class PairedMyBatisMetadataTests {
    private static final byte[] KEY=new byte[32];
    private final PairedJdbcObservation observer=new PairedJdbcObservation(new PairedJdbcObservation.Metadata(){
        @Override public java.util.List<PairedJdbcObservation.Binding> parameters(PreparedStatement value){return List.of();}
        @Override public boolean serverPrepared(PreparedStatement value){return false;}
    });
    /** 明确替身只模拟JDBC返回；实际MyBatis消费者代码不替换。 */
    private Connection original(){
        Connection[] raw=new Connection[1];
        DatabaseMetaData metadata=(DatabaseMetaData)Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{DatabaseMetaData.class},(proxy,method,args)->switch(method.getName()){
            case "getConnection"->raw[0];case "supportsMultipleResultSets"->true;case "getURL"->"jdbc:mysql://127.0.0.1:1/offline";case "getDatabaseProductName"->"MySQL";default->null;
        });
        raw[0]=(Connection)Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{Connection.class},(proxy,method,args)->switch(method.getName()){
            case "getMetaData"->metadata;case "getAutoCommit"->true;case "createStatement"->Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{Statement.class},(statement,call,values)->switch(call.getName()){case "getConnection"->proxy;case "getMoreResults"->false;case "getUpdateCount"->-1;default->null;});default->null;
        });return raw[0];
    }
    /** 原结果处理器在关闭结果集后询问supportsMultipleResultSets，不能误作连接逃逸。 */
    @Test void original_mybatis_next_result_metadata_does_not_invalidate_capture()throws Exception{
        var configuration=new Configuration();var statement=new MappedStatement.Builder(configuration,"offline.query",new StaticSqlSource(configuration,"SELECT 1"),SqlCommandType.SELECT).build();
        var handler=new DefaultResultSetHandler(null,statement,null,null,statement.getBoundSql(null),RowBounds.DEFAULT);Method next=DefaultResultSetHandler.class.getDeclaredMethod("getNextResultSet",Statement.class);next.setAccessible(true);
        var trace=observer.begin(KEY,8);try(Connection connection=observer.wrap(original());Statement command=connection.createStatement()){assertNull(next.invoke(handler,command));assertSame(connection,connection.getMetaData().getConnection());}finally{trace.close();}
        assertTrue(trace.snapshot().internallyConsistent(),trace.snapshot().problems().toString());assertEquals(0,trace.snapshot().commands().size());
    }
    /** 另线程读取固定驱动本地URL不执行SQL；不能污染正在采样的其它请求。 */
    @Test void local_metadata_getter_on_startup_connection_does_not_forge_unscoped_sql()throws Exception{
        Connection connection=observer.wrap(original());var trace=observer.begin(KEY,8);
        try(var executor=Executors.newSingleThreadExecutor()){assertEquals("jdbc:mysql://127.0.0.1:1/offline",executor.submit(()->connection.getMetaData().getURL()).get());}finally{trace.close();connection.close();}
        assertTrue(trace.snapshot().internallyConsistent(),trace.snapshot().problems().toString());
    }
    /** 元数据查询仍可能自行执行SQL或返回原对象，不能作为纯本地getter放行。 */
    @Test void metadata_query_still_invalidates_current_and_following_evidence()throws Exception{
        var trace=observer.begin(KEY,8);try(Connection connection=observer.wrap(original())){assertNull(connection.getMetaData().getTables(null,null,null,null));}finally{trace.close();}
        assertFalse(trace.snapshot().internallyConsistent());var later=observer.begin(KEY,8);later.close();assertTrue(later.snapshot().problems().contains("previous-escape"));
    }
}
