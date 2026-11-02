package com.yinzon.samplehost;

import java.io.InputStream;
import java.lang.reflect.*;
import java.math.*;
import java.nio.file.*;
import java.sql.*;
import java.time.*;
import java.util.*;
import java.util.concurrent.*;
import com.mysql.cj.*;
import com.mysql.cj.conf.*;
import com.mysql.cj.jdbc.JdbcPreparedStatement;

/** 原JDBC对象是明确替身，绑定对象使用真实固定Connector/J实现；不建连接或读取数据库。 */
public final class PairedJdbcObservationSelfTest {
    private static int passed;
    private static final byte[] KEY=new byte[32];
    static{Arrays.fill(KEY,(byte)11);}
    private PairedJdbcObservationSelfTest(){}
    /** 每个检查失败即中止，不重试或把异常转为跳过。 */
    private static void check(boolean condition){if(!condition)throw new AssertionError("JDBC采集断言失败");}
    private static void test(Checked action)throws Exception{action.run();passed++;}
    @FunctionalInterface private interface Checked{void run()throws Exception;}
    /** 明确替身只提供原结果／异常，计数原调用；不模拟SQL引擎或权限。 */
    private static final class Driver implements InvocationHandler {
        int executions,commits,rollbacks,reads;boolean auto=true;SQLException failure;String failing;CountDownLatch entered,release;
        List<PairedJdbcObservation.Binding> bindings=List.of();
        final Connection connection=(Connection)Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{Connection.class},this);
        final ResultSet reader=(ResultSet)Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{ResultSet.class},this);
        final Statement statement=(Statement)Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{Statement.class},this);
        PreparedStatement prepared=(PreparedStatement)Proxy.newProxyInstance(getClass().getClassLoader(),new Class<?>[]{PreparedStatement.class},this);
        final PairedJdbcObservation.Metadata metadata=new PairedJdbcObservation.Metadata(){public List<PairedJdbcObservation.Binding> parameters(PreparedStatement statement){return bindings;}public boolean serverPrepared(PreparedStatement statement){return false;}};
        @Override public Object invoke(Object proxy,Method method,Object[] args)throws Throwable{
            String name=method.getName();if(name.equals("toString"))throw new AssertionError("不能读取原驱动文本");
            if(name.equals("getAutoCommit"))return auto;
            if(name.equals("createStatement"))return statement;if(name.equals("prepareStatement"))return prepared;
            if(name.equals("getConnection"))return connection;if(name.equals("getStatement"))return statement;
            if(name.equals("isWrapperFor"))return ((Class<?>)args[0]).isInstance(proxy);if(name.equals("unwrap"))return proxy;
            if(name.equals("next")){reads++;return false;}
            if(name.equals("setAutoCommit")){if(name.equals(failing))throw failure;auto=(Boolean)args[0];return null;}
            if(name.equals("commit")){commits++;if(name.equals(failing))throw failure;return null;}
            if(name.equals("rollback")){rollbacks++;if(name.equals(failing))throw failure;return null;}
            if(name.startsWith("execute")){
                executions++;if(entered!=null){entered.countDown();if(!release.await(5,TimeUnit.SECONDS))throw new AssertionError("原调用等待超时");}
                if(name.equals(failing))throw failure;
                return switch(name){case "executeQuery"->reader;case "execute"->true;case "executeLargeUpdate"->9L;case "executeBatch"->new int[]{9};case "executeLargeBatch"->new long[]{9};default->9;};
            }
            if(method.getReturnType()==boolean.class)return false;if(method.getReturnType()==int.class)return 0;if(method.getReturnType()==long.class)return 0L;return null;
        }
    }
    /** 原结果、参数和异常各走一次，原reader在API返回时未被采集器读取。 */
    public static void main(String[] args)throws Exception{
        if(args.length!=1)throw new IllegalArgumentException("需要共同值证明向量路径");
        for(boolean observed:new boolean[]{false,true})for(boolean prepared:new boolean[]{false,true})for(String operation:new String[]{"executeQuery","executeUpdate","executeLargeUpdate","execute"})test(()->{
            Driver driver=new Driver();PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);PairedJdbcObservation.Scope trace=observed?observer.begin(KEY,16):null;
            try(Connection connection=observer.wrap(driver.connection);Statement statement=prepared?connection.prepareStatement("SELECT @value"):connection.createStatement()){
                Object result=prepared?PreparedStatement.class.getMethod(operation).invoke(statement):Statement.class.getMethod(operation,String.class).invoke(statement,"SELECT 1");
                check(driver.executions==1&&driver.reads==0);
                if(result instanceof ResultSet reader){check(reader.getStatement()==statement&&statement.getConnection()==connection);check(!reader.next()&&driver.reads==1);reader.close();}
                if(operation.equals("executeUpdate"))check(result.equals(9));if(operation.equals("executeLargeUpdate"))check(result.equals(9L));
            }finally{if(trace!=null)trace.close();}
            if(trace!=null)check(trace.snapshot().internallyConsistent()&&trace.snapshot().commands().size()==1);
        });
        for(String operation:new String[]{"executeQuery","executeUpdate","executeLargeUpdate","execute"})test(()->{
            Driver driver=new Driver();driver.failure=new SQLException("私有SQL正文",null,1213);driver.failing=operation;PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var trace=observer.begin(KEY,16);
            try(Connection connection=observer.wrap(driver.connection);PreparedStatement statement=connection.prepareStatement("SELECT @value")){
                try{PreparedStatement.class.getMethod(operation).invoke(statement);throw new AssertionError("原异常未抛出");}catch(InvocationTargetException failure){check(failure.getCause()==driver.failure);}
            }finally{trace.close();}
            var entry=trace.snapshot().commands().getFirst();check(driver.executions==1&&!entry.succeeded()&&entry.errorCode()==1213&&trace.snapshot().internallyConsistent());check(!trace.snapshot().toString().contains("私有SQL正文"));
        });
        test(()->{
            Driver driver=new Driver();PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var trace=observer.begin(KEY,16);
            try(Connection connection=observer.wrap(driver.connection)){connection.setAutoCommit(false);try(Statement statement=connection.createStatement()){statement.executeUpdate("UPDATE fixture SET value=1");}connection.commit();connection.setAutoCommit(true);}finally{trace.close();}
            var snapshot=trace.snapshot();check(snapshot.internallyConsistent()&&driver.commits==1);check(snapshot.transactions().size()==3);check(snapshot.commands().getFirst().transaction().equals(snapshot.transactions().getFirst().transaction()));
        });
        for(String failing:new String[]{"setAutoCommit","commit","rollback"})test(()->{
            Driver driver=new Driver();PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var trace=observer.begin(KEY,16);Connection connection=observer.wrap(driver.connection);
            if(!failing.equals("setAutoCommit"))connection.setAutoCommit(false);driver.failure=new SQLException("原事务失败",null,1205);driver.failing=failing;
            try{switch(failing){case "setAutoCommit"->connection.setAutoCommit(false);case "commit"->connection.commit();default->connection.rollback();}throw new AssertionError("原事务异常未抛出");}catch(SQLException error){check(error==driver.failure);}finally{trace.close();}
            check(!trace.snapshot().transactions().getLast().succeeded());check(trace.snapshot().internallyConsistent()==failing.equals("setAutoCommit"));
        });
        for(String defect:new String[]{"unknown","limit","batch","sql-control","escape","unscoped","preexisting","stream","large-integer","date-range","target"})test(()->{
            Driver driver=new Driver();if(defect.equals("unknown"))driver.bindings=List.of(new PairedJdbcObservation.Binding(1,"UNKNOWN",new Object(){@Override public String toString(){throw new AssertionError("未知值不能转文本");}},false));
            if(defect.equals("stream"))driver.bindings=List.of(new PairedJdbcObservation.Binding(1,"BLOB",new InputStream(){@Override public int read(){throw new AssertionError("采集不能读取流");}},true));
            if(defect.equals("large-integer"))driver.bindings=List.of(new PairedJdbcObservation.Binding(1,"BIGINT",BigInteger.ONE.shiftLeft(10000),false));
            if(defect.equals("date-range"))driver.bindings=List.of(new PairedJdbcObservation.Binding(1,"DATE",LocalDate.of(0,1,1),false));
            if(defect.equals("preexisting"))driver.auto=false;
            PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var trace=observer.begin(KEY,1);
            try(Connection connection=observer.wrap(driver.connection);PreparedStatement statement=connection.prepareStatement(defect.equals("sql-control")?"COMMIT":"SELECT @value")){
                if(defect.equals("escape"))connection.unwrap(Connection.class);if(defect.equals("target"))connection.setSchema("私有目标");
                if(defect.equals("unscoped")){try(var executor=Executors.newSingleThreadExecutor()){executor.submit(()->{try{statement.execute();}catch(SQLException failure){throw new AssertionError(failure);}}).get();}}
                else if(defect.equals("batch"))statement.executeBatch();else{statement.execute();if(defect.equals("limit"))statement.execute();}
            }finally{trace.close();}
            check(!trace.snapshot().internallyConsistent()&&!trace.snapshot().problems().isEmpty());
        });
        test(()->{
            Driver driver=new Driver();PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);Connection connection=observer.wrap(driver.connection);connection.unwrap(Connection.class);var trace=observer.begin(KEY,1);trace.close();check(trace.snapshot().problems().contains("previous-escape"));
        });
        test(()->{
            Driver driver=new Driver();PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var trace=observer.begin(KEY,2);Connection connection=observer.wrap(driver.connection);trace.close();connection.createStatement().execute("SELECT 1");check(trace.snapshot().problems().contains("late-call"));
        });
        test(()->{
            Driver driver=new Driver();PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var trace=observer.begin(KEY,2);try{observer.begin(KEY,2);throw new AssertionError("嵌套未拒绝");}catch(IllegalStateException expected){check(!trace.snapshot().closed());}finally{trace.close();}
        });
        test(()->{
            Driver driver=new Driver();driver.entered=new CountDownLatch(1);driver.release=new CountDownLatch(1);PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var reference=new java.util.concurrent.atomic.AtomicReference<PairedJdbcObservation.Scope>();
            try(var executor=Executors.newSingleThreadExecutor()){
                Future<?> work=executor.submit(()->{var trace=observer.begin(KEY,2);reference.set(trace);try(Connection connection=observer.wrap(driver.connection);Statement statement=connection.createStatement()){statement.execute("SELECT 1");}catch(SQLException failure){throw new AssertionError(failure);}finally{trace.close();}var fresh=observer.begin(KEY,2);fresh.close();});
                check(driver.entered.await(5,TimeUnit.SECONDS));reference.get().close();driver.release.countDown();work.get();
            }
            var snapshot=reference.get().snapshot();check(driver.executions==1&&!snapshot.internallyConsistent()&&snapshot.problems().contains("pending-call")&&snapshot.problems().contains("late-call"));
        });
        test(()->{
            Driver driver=new Driver();driver.bindings=List.of(new PairedJdbcObservation.Binding(1,"VARCHAR","\ud800",false));PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var trace=observer.begin(KEY,2);try(Connection connection=observer.wrap(driver.connection);PreparedStatement statement=connection.prepareStatement("SELECT @invalid")){statement.execute();}finally{trace.close();}check(trace.snapshot().problems().contains("invalid-utf16")&&!trace.snapshot().internallyConsistent());
        });
        test(()->{
            PairedJdbcObservation observer=new PairedJdbcObservation(new Driver().metadata);try(var executor=Executors.newFixedThreadPool(2)){
                List<Future<PairedJdbcObservation.Snapshot>> tasks=new ArrayList<>();for(int index=0;index<2;index++){final int id=index;tasks.add(executor.submit(()->{Driver driver=new Driver();var trace=observer.begin(KEY,2);try(Connection connection=observer.wrap(driver.connection);Statement statement=connection.createStatement()){statement.execute("SELECT "+id);}finally{trace.close();}return trace.snapshot();}));}
                for(int index=0;index<2;index++){var snapshot=tasks.get(index).get();check(snapshot.internallyConsistent()&&snapshot.commands().size()==1&&snapshot.commands().getFirst().sql().equals("SELECT "+index));}
            }
        });
        test(()->{
            Driver driver=new Driver();PairedJdbcObservation observer=new PairedJdbcObservation(new PairedJdbcObservation.Metadata(){public List<PairedJdbcObservation.Binding> parameters(PreparedStatement statement){throw new IllegalStateException("私有诊断异常");}public boolean serverPrepared(PreparedStatement statement){throw new AssertionError("失败后不继续读取");}});var trace=observer.begin(KEY,2);
            try(Connection connection=observer.wrap(driver.connection);PreparedStatement statement=connection.prepareStatement("SELECT @value")){check(statement.execute());}finally{trace.close();}
            check(driver.executions==1&&trace.snapshot().problems().contains("metadata-failure")&&!trace.snapshot().internallyConsistent());
        });
        test(()->{
            Driver driver=new Driver();PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var trace=observer.begin(KEY,1);try(Connection connection=observer.wrap(driver.connection)){connection.setAutoCommit(false);connection.commit();}finally{trace.close();}check(driver.commits==1&&trace.snapshot().transactions().size()==1&&trace.snapshot().startedTransactions()==2&&trace.snapshot().problems().contains("transaction-limit")&&!trace.snapshot().internallyConsistent());
        });
        test(()->{
            Driver driver=new Driver();PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var trace=observer.begin(KEY,4);try(Connection connection=observer.wrap(driver.connection)){connection.setAutoCommit(false);connection.setSavepoint("私有保存点");connection.rollback();}finally{trace.close();}check(driver.rollbacks==1&&trace.snapshot().problems().contains("savepoint-api")&&!trace.snapshot().internallyConsistent());check(!trace.snapshot().toString().contains("私有保存点"));
        });
        test(()->{
            Driver driver=new Driver();PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var trace=observer.begin(KEY,4);try(Connection connection=observer.wrap(driver.connection)){connection.setAutoCommit(false);driver.failure=new SQLException("原提交失败");driver.failing="commit";try{connection.commit();throw new AssertionError("没有抛出原提交异常");}catch(SQLException failure){check(failure==driver.failure);}driver.failing=null;connection.rollback();}finally{trace.close();}check(trace.snapshot().internallyConsistent()&&driver.commits==1&&driver.rollbacks==1&&trace.snapshot().transactions().size()==3&&!trace.snapshot().transactions().get(1).succeeded());
        });
        test(()->{
            Driver driver=new Driver();PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var trace=observer.begin(KEY,8);try(Connection connection=observer.wrap(driver.connection)){connection.setReadOnly(true);connection.setTransactionIsolation(Connection.TRANSACTION_READ_COMMITTED);connection.setAutoCommit(false);connection.commit();connection.setAutoCommit(true);connection.setReadOnly(false);}finally{trace.close();}
            var snapshot=trace.snapshot();check(snapshot.internallyConsistent()&&snapshot.transactions().size()==6&&snapshot.transactions().get(0).booleanValue()&&snapshot.transactions().get(1).isolationLevel()==Connection.TRANSACTION_READ_COMMITTED&&snapshot.transactions().get(1).transaction()==null&&snapshot.transactions().get(2).transaction()!=null);
        });
        test(()->vectors(Path.of(args[0])));
        test(()->actualBindings(Path.of(args[0])));
        test(()->actualTemporalBindings(Path.of(args[0]).resolveSibling("sql-native-java-values-v2.tsv")));
        System.out.println("Java JDBC离线采集通过："+passed+"项；真实数据库、HTTP及性能未执行。");
    }
    /** 同一固定向量还由C#测试读取，摘要使用独立Python标准库生成。 */
    private static void vectors(Path file)throws Exception{
        Map<String,Object> values=new LinkedHashMap<>();values.put("null",null);values.put("empty","");values.put("text","中文'\\常量");values.put("id",9007199254740993L);values.put("decimal",new BigDecimal("0.1234567890123456789012345678"));values.put("decimal-zero",new BigDecimal("0.00"));values.put("double",Math.scalb(1d,-958));values.put("negative-zero",-0d);values.put("single",1.25f);values.put("bytes",new byte[]{0,1,(byte)255});values.put("boolean",true);values.put("date",LocalDate.of(2026,11,2));values.put("datetime",LocalDateTime.of(2026,11,2,3,4,5,123456000));values.put("time",LocalTime.of(3,4,5,123456000));values.put("duration",Duration.ofHours(26).plusNanos(123456000));values.put("guid",UUID.fromString("00112233-4455-6677-8899-aabbccddeeff"));
        Driver driver=new Driver();List<String> names=new ArrayList<>(values.keySet());List<PairedJdbcObservation.Binding> bindings=new ArrayList<>();for(int i=0;i<names.size();i++)bindings.add(new PairedJdbcObservation.Binding(i+1,"FIXTURE",values.get(names.get(i)),false));driver.bindings=bindings;
        PairedJdbcObservation observer=new PairedJdbcObservation(driver.metadata);var trace=observer.begin(KEY,8);try(Connection connection=observer.wrap(driver.connection);PreparedStatement statement=connection.prepareStatement("SELECT @vectors")){statement.execute();}finally{trace.close();}
        check(trace.snapshot().internallyConsistent());List<String> lines=Files.readAllLines(file);check(lines.size()==names.size());var parameters=trace.snapshot().commands().getFirst().parameters();for(int i=0;i<lines.size();i++){String[] parts=lines.get(i).split("\t");check(parts[0].equals(names.get(i))&&parts[1].equals(parameters.get(i).kind())&&parts[2].equals(parameters.get(i).valueHash()));}
    }
    /** 原审计字段使用纳秒LocalDateTime，真实驱动绑定必须原样证明，不截到CLR精度。 */
    private static void actualTemporalBindings(Path file)throws Exception{
        DefaultPropertySet properties=new DefaultPropertySet();properties.initializeProperties(new Properties());var offlineServer=new com.mysql.cj.protocol.a.NativeServerSession(properties);var offlineProtocol=new com.mysql.cj.protocol.a.NativeProtocol(new com.mysql.cj.log.NullLogger("offline"));
        NativeSession session=new NativeSession(ConnectionUrl.getConnectionUrlInstance("jdbc:mysql://127.0.0.1:1/offline",new Properties()).getMainHost(),properties){@Override public com.mysql.cj.protocol.a.NativeProtocol getProtocol(){return offlineProtocol;}@Override public com.mysql.cj.protocol.ServerSession getServerSession(){return offlineServer;}};
        Object[] originals={LocalDateTime.of(2026,11,2,3,4,5,123456789),LocalTime.of(3,4,5,123456789),Duration.ofHours(26).plusNanos(123456789),Duration.ofNanos(-1),Duration.ofSeconds(Long.MAX_VALUE,999999999)};
        NativeQueryBindings bindings=new NativeQueryBindings(originals.length,session,NativeQueryBindValue::new);for(int i=0;i<originals.length;i++)bindings.setObject(i,originals[i]);
        PreparedStatement statement=(PreparedStatement)Proxy.newProxyInstance(PairedJdbcObservationSelfTest.class.getClassLoader(),new Class<?>[]{JdbcPreparedStatement.class},(proxy,method,args)->{if(method.getName().equals("getQueryBindings"))return bindings;if(method.getName().equals("execute"))return true;if(method.getName().equals("close"))return null;throw new AssertionError("时间采集调用了未声明驱动方法");});
        Driver driver=new Driver();driver.prepared=statement;var observer=new PairedJdbcObservation(new MySqlJdbcObservationMetadata());var trace=observer.begin(KEY,2);try(Connection connection=observer.wrap(driver.connection);PreparedStatement wrapped=connection.prepareStatement("SELECT @datetime,@time,@duration,@negative,@large")){wrapped.execute();}finally{trace.close();}
        var snapshot=trace.snapshot();check(snapshot.internallyConsistent()&&snapshot.valueProofFormat().equals("metarill-sql-values-v2"));var parameters=snapshot.commands().getFirst().parameters();List<String> expected=Files.readAllLines(file);check(expected.size()==originals.length);
        for(int i=0;i<originals.length;i++){String[] row=expected.get(i).split("\t");check(parameters.get(i).kind().equals(row[1])&&parameters.get(i).valueHash().equals(row[2]));check(bindings.getBindValues()[i].getValue().equals(originals[i]));}
        // 非CLR值只证明原生输入；不能将这种hash作为C#同值或数据库持久化证明。
        check(parameters.stream().allMatch(parameter->parameter.kind().startsWith("java-")));
    }
    /** 使用真实Connector/J最终绑定对象，不发送握手或SQL；网络执行仍是明确替身。 */
    private static void actualBindings(Path vectors)throws Exception{
        DefaultPropertySet properties=new DefaultPropertySet();properties.initializeProperties(new Properties());
        var offlineServer=new com.mysql.cj.protocol.a.NativeServerSession(properties);
        offlineServer.setCharsetSettings((CharsetSettings)Proxy.newProxyInstance(PairedJdbcObservationSelfTest.class.getClassLoader(),new Class<?>[]{CharsetSettings.class},(proxy,method,args)->{if(method.getName().equals("getRequiresEscapingEncoder"))return false;throw new AssertionError("离线用例未提供其它字符集能力");}));var offlineProtocol=new com.mysql.cj.protocol.a.NativeProtocol(new com.mysql.cj.log.NullLogger("offline"));
        // 只补构造器通常从握手取得的本地协议对象；没有connect、socket或服务器能力伪装。
        NativeSession session=new NativeSession(ConnectionUrl.getConnectionUrlInstance("jdbc:mysql://127.0.0.1:1/offline",new Properties()).getMainHost(),properties){
            @Override public com.mysql.cj.protocol.a.NativeProtocol getProtocol(){return offlineProtocol;}
            @Override public com.mysql.cj.protocol.ServerSession getServerSession(){return offlineServer;}
        };
        NativeQueryBindings bindings=new NativeQueryBindings(5,session,NativeQueryBindValue::new);bindings.setLong(0,9007199254740993L);bindings.setBigDecimal(1,new BigDecimal("0.1234567890123456789012345678"));bindings.setDouble(2,Math.scalb(1d,-958));bindings.setString(3,"中文");bindings.setNull(4);
        PreparedStatement statement=(PreparedStatement)Proxy.newProxyInstance(PairedJdbcObservationSelfTest.class.getClassLoader(),new Class<?>[]{JdbcPreparedStatement.class},(proxy,method,args)->{if(method.getName().equals("getQueryBindings"))return bindings;if(method.getName().equals("execute"))return true;if(method.getName().equals("close"))return null;throw new AssertionError("适配器调用了未声明驱动方法");});
        var adapter=new MySqlJdbcObservationMetadata();var result=adapter.parameters(statement);check(result.size()==5&&result.stream().noneMatch(PairedJdbcObservation.Binding::unsupported));check(result.get(0).driverType().equals("BIGINT")&&result.get(0).value().equals(9007199254740993L));check(result.get(2).driverType().equals("DOUBLE")&&result.get(2).value().equals(Math.scalb(1d,-958)));check(!adapter.serverPrepared(statement));
        Driver driver=new Driver();driver.prepared=statement;PairedJdbcObservation observer=new PairedJdbcObservation(adapter);var trace=observer.begin(KEY,2);
        try(Connection connection=observer.wrap(driver.connection);PreparedStatement wrapped=connection.prepareStatement("SELECT @id,@amount,@value,@name,@missing")){wrapped.execute();}finally{trace.close();}
        check(trace.snapshot().internallyConsistent());Map<String,String> expected=new HashMap<>();for(String line:Files.readAllLines(vectors)){String[] parts=line.split("\t");expected.put(parts[0],parts[2]);}
        var captured=trace.snapshot().commands().getFirst().parameters();check(captured.get(0).valueHash().equals(expected.get("id"))&&captured.get(1).valueHash().equals(expected.get("decimal"))&&captured.get(2).valueHash().equals(expected.get("double"))&&captured.get(4).valueHash().equals(expected.get("null")));
        bindings.setBinaryStream(0,new InputStream(){@Override public int read(){throw new AssertionError("真实驱动流绑定不能被观察读取");}},-1);check(adapter.parameters(statement).getFirst().unsupported());
        var streamed=observer.begin(KEY,2);try(Connection connection=observer.wrap(driver.connection);PreparedStatement wrapped=connection.prepareStatement("SELECT @stream")){wrapped.execute();}finally{streamed.close();}check(streamed.snapshot().problems().contains("unsupported-binding")&&!streamed.snapshot().internallyConsistent());
        bindings.getBindValues()[0].reset();check(adapter.parameters(statement).getFirst().unsupported());
    }
}
