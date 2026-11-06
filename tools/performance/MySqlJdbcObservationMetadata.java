package com.yinzon.samplehost;

import java.lang.reflect.*;
import java.sql.PreparedStatement;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.List;

/** 固定Connector/J只读适配器；原宿主驱动为runtime依赖，编译不强制改变其POM。 */
public final class MySqlJdbcObservationMetadata implements PairedJdbcObservation.Metadata {
    private volatile Api cached;
    /** 每个实际驱动类加载器只解析公共API；未知驱动或签名使采集明确失败，不猜测实现。 */
    private Api api(Statement statement)throws ReflectiveOperationException{
        ClassLoader loader=statement.getClass().getClassLoader();Api value=cached;if(value==null||value.loader()!=loader){value=new Api(loader);cached=value;}return value;
    }
    private record Api(ClassLoader loader,Class<?> statement,Class<?> serverPrepared,Class<?> jdbcStatement,Method database,Method bindings,Method values,Method set,Method stream,Method nil,Method type,Method value){
        Api(ClassLoader loader)throws ReflectiveOperationException{this(loader,Class.forName("com.mysql.cj.jdbc.JdbcPreparedStatement",false,loader),Class.forName("com.mysql.cj.jdbc.ServerPreparedStatement",false,loader),Class.forName("com.mysql.cj.jdbc.JdbcStatement",false,loader),method(loader,"com.mysql.cj.jdbc.JdbcStatement","getCurrentDatabase"),method(loader,"com.mysql.cj.jdbc.JdbcPreparedStatement","getQueryBindings"),method(loader,"com.mysql.cj.QueryBindings","getBindValues"),method(loader,"com.mysql.cj.BindValue","isSet"),method(loader,"com.mysql.cj.BindValue","isStream"),method(loader,"com.mysql.cj.BindValue","isNull"),method(loader,"com.mysql.cj.BindValue","getMysqlType"),method(loader,"com.mysql.cj.BindValue","getValue"));}
        private static Method method(ClassLoader loader,String type,String name)throws ReflectiveOperationException{return Class.forName(type,false,loader).getMethod(name);}
    }
    /** 只解开反射包装，诊断原异常仍交外层标记metadata-failure，不替换业务SQL执行异常。 */
    private static Object read(Method method,Object target)throws Exception{
        try{return method.invoke(target);}catch(InvocationTargetException failure){Throwable cause=failure.getCause();if(cause instanceof Exception error)throw error;if(cause instanceof Error error)throw error;throw failure;}
    }
    private static Object original(PreparedStatement statement,Api api)throws Exception{return api.statement().isInstance(statement)?statement:statement.unwrap(api.statement());}
    /** 只访问驱动内存绑定；超预算、未赋值和流类型不读取值且明确拒绝完整证明。 */
    @Override public List<PairedJdbcObservation.Binding> parameters(PreparedStatement statement)throws Exception{
        Api api=api(statement);Object[] values=(Object[])read(api.values(),read(api.bindings(),original(statement,api)));if(values.length>256)throw new IllegalArgumentException("绑定参数超过采集预算");
        List<PairedJdbcObservation.Binding> result=new ArrayList<>();
        for(int index=0;index<values.length;index++){
            Object binding=values[index];boolean unsupported=binding==null||!Boolean.TRUE.equals(read(api.set(),binding))||Boolean.TRUE.equals(read(api.stream(),binding));String type=null;
            if(binding!=null){Object driverType=read(api.type(),binding);if(!(driverType instanceof Enum<?> code)||!code.getDeclaringClass().getName().equals("com.mysql.cj.MysqlType"))throw new IllegalArgumentException("未知MySQL绑定类型");type=code.name();}
            Object value=unsupported||Boolean.TRUE.equals(read(api.nil(),binding))?null:read(api.value(),binding);result.add(new PairedJdbcObservation.Binding(index+1,type,value,unsupported));
        }
        return List.copyOf(result);
    }
    /** 读取实际语句关联的默认库，连接池代理只用于解包，不查询数据库。 */
    @Override public String database(Statement statement)throws Exception{
        Api api=api(statement);
        Object driver=api.jdbcStatement().isInstance(statement)?statement:statement.unwrap(api.jdbcStatement());
        return (String)read(api.database(),driver);
    }
    /** 读取实际驱动实现种类，不把客户端预处理误报成服务器已准备。 */
    @Override public boolean serverPrepared(PreparedStatement statement)throws Exception{Api api=api(statement);return api.serverPrepared().isInstance(original(statement,api));}
}
