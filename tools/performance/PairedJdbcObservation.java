package com.yinzon.samplehost;

import java.lang.reflect.*;
import java.math.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.sql.*;
import java.time.*;
import java.time.temporal.ChronoUnit;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/** 仅供独占配对验收宿主显式安装的JDBC观察器；不注册Bean、不写日志、不开连接。 */
public final class PairedJdbcObservation {
    /** 驱动适配器只读取最终绑定状态，不执行SQL、不读取stream或调用未知值的toString。 */
    public interface Metadata { List<Binding> parameters(PreparedStatement statement) throws Exception; boolean serverPrepared(PreparedStatement statement) throws Exception; }
    public record Binding(int position,String driverType,Object value,boolean unsupported) {}
    public record Parameter(int position,String driverType,String javaType,String kind,String valueHash) {}
    public record Command(long number,String operation,String sql,List<Parameter> parameters,long connection,Long transaction,boolean serverPrepared,boolean succeeded,String errorType,Integer errorCode,Long affectedRows,long startedNanos,long finishedNanos) {}
    public record Boundary(long number,String operation,long connection,Long transaction,Boolean booleanValue,Integer isolationLevel,boolean succeeded,String errorType,Integer errorCode,long startedNanos,long finishedNanos) {}
    public record Snapshot(String valueProofFormat,List<Command> commands,List<Boundary> transactions,Set<String> problems,long started,long completed,long startedTransactions,long completedTransactions,boolean closed,boolean internallyConsistent) {}
    private final Metadata metadata;private volatile boolean escaped;
    private final ThreadLocal<Scope> current=new ThreadLocal<>();
    private final Set<Scope> active=ConcurrentHashMap.newKeySet();

    /** 显式依赖只读驱动适配器；构造阶段不访问应用数据源。 */
    public PairedJdbcObservation(Metadata metadata){this.metadata=Objects.requireNonNull(metadata);}
    /** 每个实际请求线程显式开启；不把线程池子任务自动归为同一请求。 */
    public Scope begin(byte[] key,int maximum){
        Objects.requireNonNull(key);if(key.length!=32||maximum<1||maximum>65536)throw new IllegalArgumentException("密钥或预算不合法");
        if(current.get()!=null&&!current.get().closed)throw new IllegalStateException("同线程不能嵌套观察");
        Scope scope=new Scope(key.clone(),maximum);if(escaped)scope.problem("previous-escape");current.set(scope);active.add(scope);return scope;
    }
    /** 数据源自身的unwrap同样会使此宿主后续证据失效，原调用仍由数据源执行。 */
    void sourceEscape(){escaped=true;Scope scope=current.get();if(scope!=null)scope.problem("data-source-escape");}
    /** 可信宿主记录异步/请求运输等无法由JDBC本身判定的覆盖缺口。 */
    void rejectHost(Scope scope,String code){scope.problem(code);}
    /** 包装调用者提供的原连接，不开连接、不改变池配置；池仍负责原连接的借还。 */
    public Connection wrap(Connection original){
        Objects.requireNonNull(original);ConnectionState state=new ConnectionState(original,current.get());
        return (Connection)Proxy.newProxyInstance(PairedJdbcObservation.class.getClassLoader(),new Class<?>[]{Connection.class},(proxy,method,args)->connection(state,(Connection)proxy,method,args));
    }
    /** 所有反射调用只解开InvocationTargetException，原SQLException／结果不被诊断异常替代。 */
    private static Object call(Object target,Method method,Object[] args)throws Throwable{try{return method.invoke(target,args);}catch(InvocationTargetException error){throw error.getCause();}}
    private Scope scope(ConnectionState state){
        Scope value=current.get();if(value!=null)return value;
        if(state.origin!=null)state.origin.problem(state.origin.closed?"late-call":"unscoped-call");
        for(Scope other:active)other.problem("unscoped-call");return null;
    }
    private Object connection(ConnectionState state,Connection proxy,Method method,Object[] args)throws Throwable{
        String name=method.getName();
        if(name.equals("equals"))return proxy==args[0];if(name.equals("hashCode"))return System.identityHashCode(proxy);
        if(name.equals("toString"))return "MetarillJdbcConnection";
        Scope scope=scope(state);
        if(name.equals("unwrap")){escaped=true;if(scope!=null)scope.problem("jdbc-escape");}
        if(Set.of("setSavepoint","releaseSavepoint").contains(name)&&scope!=null)scope.problem("savepoint-api");
        if(Set.of("setAutoCommit","commit","rollback","setReadOnly","setTransactionIsolation").contains(name)){
            if(scope==null)return call(state.original,method,args);
            return scope.boundary(state,name,args,()->call(state.original,method,args));
        }
        if(Set.of("setCatalog","setSchema","setClientInfo").contains(name)){escaped=true;if(scope!=null)scope.problem("connection-session-change");}
        if((name.equals("close")||name.equals("abort"))&&scope!=null)scope.closing(state);
        Object result=call(state.original,method,args);
        if(result instanceof DatabaseMetaData databaseMetadata)return databaseMetadata(databaseMetadata,proxy,state);
        if(result instanceof Statement statement){
            String sql=args!=null&&args.length>0&&args[0] instanceof String text?text:null;
            if(statement instanceof CallableStatement&&scope!=null)scope.problem("callable-statement");
            return statement(state,proxy,statement,sql);
        }
        return result;
    }
    /** 固定Connector/J产品名是本地常量；框架错误翻译可读取，连接回指仍返回同一包装。其它元数据API仍明确拒绝完整证明。 */
    private DatabaseMetaData databaseMetadata(DatabaseMetaData original,Connection connection,ConnectionState state){
        return (DatabaseMetaData)Proxy.newProxyInstance(PairedJdbcObservation.class.getClassLoader(),new Class<?>[]{DatabaseMetaData.class},(proxy,method,args)->{
            String name=method.getName();if(name.equals("getConnection"))return connection;if(name.equals("getDatabaseProductName")||name.equals("isWrapperFor"))return call(original,method,args);
            if(name.equals("equals"))return proxy==args[0];if(name.equals("hashCode"))return System.identityHashCode(proxy);if(name.equals("toString"))return "MetarillJdbcMetadata";
            escaped=true;Scope scope=scope(state);if(scope!=null)scope.problem("jdbc-metadata-api");return call(original,method,args);
        });
    }
    /** 标准Statement及PreparedStatement均沿原执行路径；reader代理只阻止getStatement泄漏原命令。 */
    private Statement statement(ConnectionState state,Connection connection,Statement original,String preparedSql){
        Class<?> type=original instanceof CallableStatement?CallableStatement.class:original instanceof PreparedStatement?PreparedStatement.class:Statement.class;
        return (Statement)Proxy.newProxyInstance(PairedJdbcObservation.class.getClassLoader(),new Class<?>[]{type},(proxy,method,args)->{
            String name=method.getName();if(name.equals("getConnection"))return connection;
            if(name.equals("equals"))return proxy==args[0];if(name.equals("hashCode"))return System.identityHashCode(proxy);if(name.equals("toString"))return "MetarillJdbcStatement";
            Scope scope=scope(state);if(name.equals("unwrap")){escaped=true;if(scope!=null)scope.problem("jdbc-escape");}
            Object result;
            if(Set.of("execute","executeQuery","executeUpdate","executeLargeUpdate","executeBatch","executeLargeBatch").contains(name)&&scope!=null){
                String sql=preparedSql!=null?preparedSql:args!=null&&args.length>0&&args[0] instanceof String text?text:null;
                result=scope.execute(state,original,name,sql,()->call(original,method,args));
            }else result=call(original,method,args);
            return result instanceof ResultSet reader?reader(reader,(Statement)proxy,state):result;
        });
    }
    /** 不消费、缓存或转换行；unwrap会使当前证据明确不完整，原调用结果保持。 */
    private ResultSet reader(ResultSet original,Statement statement,ConnectionState state){
        return (ResultSet)Proxy.newProxyInstance(PairedJdbcObservation.class.getClassLoader(),new Class<?>[]{ResultSet.class},(proxy,method,args)->{
            if(method.getName().equals("getStatement"))return statement;
            if(method.getName().equals("equals"))return proxy==args[0];if(method.getName().equals("hashCode"))return System.identityHashCode(proxy);if(method.getName().equals("toString"))return "MetarillJdbcReader";
            if(method.getName().equals("unwrap")){escaped=true;Scope scope=scope(state);if(scope!=null)scope.problem("jdbc-escape");}
            return call(original,method,args);
        });
    }
    @FunctionalInterface private interface OriginalCall{Object run()throws Throwable;}
    private static final class ConnectionState{
        final Connection original;final Scope origin;
        ConnectionState(Connection original,Scope origin){this.original=original;this.origin=origin;}
    }
    /** 内存快照有界且不保留值、连接、statement或异常对象；关闭后立即清零密钥副本。 */
    public final class Scope implements AutoCloseable{
        private final byte[] key;private final int maximum;private volatile boolean closed;
        private long started,completed,startedTransactions,completedTransactions,sequence,nextConnection,nextTransaction;
        private final List<Command> commands=new ArrayList<>();private final List<Boundary> boundaries=new ArrayList<>();private final Set<String> problems=new TreeSet<>();
        private final IdentityHashMap<ConnectionState,Long> connections=new IdentityHashMap<>(),transactions=new IdentityHashMap<>();
        private Scope(byte[] key,int maximum){this.key=key;this.maximum=maximum;}
        private synchronized void problem(String value){problems.add(value);}
        private long connection(ConnectionState state){return closed?++nextConnection:connections.computeIfAbsent(state,ignored->++nextConnection);}
        private static String error(Throwable error){return error==null?null:error.getClass().getName();}
        private Integer code(Throwable error){try{return error instanceof SQLException sql?sql.getErrorCode():null;}catch(RuntimeException failure){problem("error-metadata-failure");return null;}}
        /** 编码只在明确白名单上运行；不读流、不读取结果集，也不生成未知对象文本。 */
        private Parameter parameter(Binding binding){
            Object value=binding.value();String kind="unsupported";byte[] bytes=null;
            try{
                if(binding.unsupported()){problem("unsupported-binding");return new Parameter(binding.position(),binding.driverType(),value==null?"null":value.getClass().getName(),kind,null);}
                if(value==null){kind="null";bytes=new byte[0];}
                else if(value instanceof byte[] data){kind="bytes";if(data.length<=65536)bytes=data.clone();else problem("value-limit");}
                else if(value instanceof String text){kind="string";if(text.length()>16384)problem("value-limit");else{boolean valid=true;for(int index=0;index<text.length();index++){char ch=text.charAt(index);if(Character.isHighSurrogate(ch)){if(index+1>=text.length()||!Character.isLowSurrogate(text.charAt(++index))){valid=false;break;}}else if(Character.isLowSurrogate(ch)){valid=false;break;}}if(valid)bytes=text.getBytes(StandardCharsets.UTF_8);else problem("invalid-utf16");}}
                else if(value instanceof Boolean flag){kind="boolean";bytes=(flag?"1":"0").getBytes(StandardCharsets.UTF_8);}
                else if(value instanceof Byte||value instanceof Short||value instanceof Integer||value instanceof Long){kind="integer";bytes=value.toString().getBytes(StandardCharsets.UTF_8);}
                else if(value instanceof BigInteger number&&value.getClass()==BigInteger.class){kind="integer";if(number.bitLength()>256)problem("value-limit");else bytes=number.toString().getBytes(StandardCharsets.UTF_8);}
                else if(value instanceof BigDecimal number&&value.getClass()==BigDecimal.class){kind="decimal";if(number.precision()>256||Math.abs((long)number.scale())>256)problem("value-limit");else bytes=(number.signum()==0?"0":number.stripTrailingZeros().toPlainString()).getBytes(StandardCharsets.UTF_8);}
                else if(value instanceof Double number&&Double.isFinite(number)){kind="double-bits";bytes=String.format(Locale.ROOT,"%016x",Double.doubleToRawLongBits(number)).getBytes(StandardCharsets.UTF_8);}
                else if(value instanceof Float number&&Float.isFinite(number)){kind="single-bits";bytes=String.format(Locale.ROOT,"%08x",Float.floatToRawIntBits(number)).getBytes(StandardCharsets.UTF_8);}
                else if(value instanceof UUID id){kind="guid";bytes=id.toString().getBytes(StandardCharsets.UTF_8);}
                else if(value instanceof LocalDate date){kind="date";if(date.getYear()<1||date.getYear()>9999)problem("unsupported-date");else bytes=date.toString().getBytes(StandardCharsets.UTF_8);}
                else if(value instanceof LocalTime time){kind="time";if(time.getNano()%100!=0)problem("sub-tick-value");else bytes=Long.toString(time.toNanoOfDay()/100).getBytes(StandardCharsets.UTF_8);}
                else if(value instanceof LocalDateTime date){kind="datetime:Unspecified";if(date.getNano()%100!=0||date.getYear()<1||date.getYear()>9999)problem("unsupported-date");else bytes=Long.toString(Math.addExact(Math.multiplyExact(ChronoUnit.DAYS.between(LocalDate.of(1,1,1),date.toLocalDate()),864000000000L),date.toLocalTime().toNanoOfDay()/100)).getBytes(StandardCharsets.UTF_8);}
                else if(value instanceof Duration duration){kind="timespan";if(duration.getNano()%100!=0)problem("sub-tick-value");else bytes=Long.toString(Math.addExact(Math.multiplyExact(duration.getSeconds(),10000000L),duration.getNano()/100)).getBytes(StandardCharsets.UTF_8);}
                else problem("unsupported-value");
                String hash=null;
                if(bytes!=null){Mac mac=Mac.getInstance("HmacSHA256");mac.init(new SecretKeySpec(key,"HmacSHA256"));mac.update((kind+"\0").getBytes(StandardCharsets.UTF_8));hash=HexFormat.of().formatHex(mac.doFinal(bytes));}
                return new Parameter(binding.position(),binding.driverType(),value==null?"null":value.getClass().getName(),kind,hash);
            }catch(Exception failure){problem("value-metadata-failure");return new Parameter(binding.position(),binding.driverType(),"unknown",kind,null);}
            finally{if(bytes!=null)Arrays.fill(bytes,(byte)0);}
        }
        /** 在原驱动执行之前快照实际绑定；元数据失败使证据失效，原调用仍执行且只执行一次。 */
        private Object execute(ConnectionState state,Statement statement,String operation,String sql,OriginalCall original)throws Throwable{
            long number,connection,start=System.nanoTime();Long transaction;boolean retained,prepared=false;List<Parameter> parameters=List.of();String text=sql;
            synchronized(this){started++;number=++sequence;connection=connection(state);transaction=transactions.get(state);retained=started<=maximum;if(closed)problem("late-call");if(!retained)problem("command-limit");}
            if(retained){
                try{
                    if(text==null||text.length()>32768){problem("sql-limit");text="";}
                    if(operation.endsWith("Batch"))problem("batch-command");
                    if(text!=null&&(text.contains(";")||text.stripLeading().startsWith("/*")||text.stripLeading().startsWith("--")||text.stripLeading().startsWith("#")))problem("unsupported-sql-shape");
                    if(text!=null){String head=text.stripLeading().split("\\s+",2)[0].toUpperCase(Locale.ROOT);if(Set.of("SET","START","BEGIN","COMMIT","ROLLBACK","SAVEPOINT","RELEASE","XA","USE").contains(head))problem("transaction-control-sql");if(transaction!=null&&Set.of("CREATE","ALTER","DROP","TRUNCATE","RENAME","LOCK","UNLOCK").contains(head))problem("implicit-commit-sql");}
                    if(statement instanceof CallableStatement)problem("callable-statement");
                    if(statement instanceof PreparedStatement ps){List<Binding> bindings=metadata.parameters(ps);if(bindings.size()>256)problem("parameter-limit");else{List<Parameter> captured=new ArrayList<>();for(Binding binding:bindings)captured.add(parameter(binding));parameters=List.copyOf(captured);}prepared=metadata.serverPrepared(ps);}
                    if(!state.original.getAutoCommit()&&transaction==null)problem("missing-transaction-begin");
                }catch(Exception failure){problem("metadata-failure");}
            }
            Throwable failure=null;Object result=null;
            try{result=original.run();return result;}catch(Throwable error){failure=error;throw error;}
            finally{synchronized(this){completed++;if(closed)problem("late-call");if(retained)commands.add(new Command(number,operation,text,parameters,connection,transaction,prepared,failure==null,error(failure),code(failure),result instanceof Number rows?rows.longValue():null,start,System.nanoTime()));}}
        }
        /** JDBC没有独立begin API：只记录实际setAutoCommit(false)，不虚构服务器BEGIN语句。 */
        private Object boundary(ConnectionState state,String operation,Object[] args,OriginalCall original)throws Throwable{
            long start=System.nanoTime(),number,connection;Long transaction;Boolean auto=Set.of("setAutoCommit","setReadOnly").contains(operation)?(Boolean)args[0]:null;Integer isolation=operation.equals("setTransactionIsolation")?(Integer)args[0]:null;
            boolean retained; synchronized(this){startedTransactions++;retained=startedTransactions<=maximum;number=++sequence;connection=connection(state);transaction=transactions.get(state);if(!retained)problem("transaction-limit");if(closed)problem("late-call");if(transaction==null&&Set.of("commit","rollback").contains(operation))problem("missing-transaction-begin");if(operation.equals("rollback")&&args!=null&&args.length>0)problem("savepoint-api");}
            Throwable failure=null;
            try{
                Object result=original.run();
                synchronized(this){
                    if(closed)problem("late-call");else if(operation.equals("setAutoCommit")&&Boolean.FALSE.equals(auto))transaction=transactions.computeIfAbsent(state,ignored->++nextTransaction);
                    else if((operation.equals("commit")||operation.equals("rollback"))&&(args==null||args.length==0)||operation.equals("setAutoCommit")&&Boolean.TRUE.equals(auto))transactions.remove(state);
                }
                return result;
            }catch(Throwable error){failure=error;throw error;}
            finally{synchronized(this){completedTransactions++;if(closed)problem("late-call");if(retained)boundaries.add(new Boundary(number,operation+(operation.equals("rollback")&&args!=null&&args.length>0?"-savepoint":""),connection,transaction,auto,isolation,failure==null,error(failure),code(failure),start,System.nanoTime()));}}
        }
        private synchronized void closing(ConnectionState state){if(transactions.containsKey(state))problem("close-with-transaction");}
        /** 完整性只表示本采集边界内部一致，不证明应用所有DataSource均已安装。 */
        public synchronized Snapshot snapshot(){Set<String> issues=new TreeSet<>(problems);if(!transactions.isEmpty())issues.add("unresolved-transaction");return new Snapshot("metarill-sql-values-v1",List.copyOf(commands),List.copyOf(boundaries),Set.copyOf(issues),started,completed,startedTransactions,completedTransactions,closed,closed&&started==completed&&started==commands.size()&&startedTransactions==completedTransactions&&startedTransactions==boundaries.size()&&issues.isEmpty());}
        @Override public synchronized void close(){if(closed){if(current.get()==this)current.remove();return;}closed=true;if(started!=completed||startedTransactions!=completedTransactions)problem("pending-call");if(!transactions.isEmpty())problem("unresolved-transaction");connections.clear();transactions.clear();if(Thread.currentThread().threadId()!=ownerThreadId)problem("cross-thread-close");if(current.get()==this)current.remove();active.remove(this);Arrays.fill(key,(byte)0);}
        private final long ownerThreadId=Thread.currentThread().threadId();
    }
}
