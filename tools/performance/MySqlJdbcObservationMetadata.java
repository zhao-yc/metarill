package com.yinzon.samplehost;

import com.mysql.cj.BindValue;
import com.mysql.cj.jdbc.JdbcPreparedStatement;
import com.mysql.cj.jdbc.ServerPreparedStatement;
import java.sql.PreparedStatement;
import java.util.ArrayList;
import java.util.List;

/** 固定Connector/J驱动适配器；读取已绑定值和枚举，不调用参数元数据SQL或消费流。 */
public final class MySqlJdbcObservationMetadata implements PairedJdbcObservation.Metadata {
    private JdbcPreparedStatement original(PreparedStatement statement)throws Exception{return statement instanceof JdbcPreparedStatement direct?direct:statement.unwrap(JdbcPreparedStatement.class);}
    /** 只访问驱动内存绑定；超过预算、未赋值和流类型明确拒绝完整证明。 */
    @Override public List<PairedJdbcObservation.Binding> parameters(PreparedStatement statement)throws Exception{
        BindValue[] values=original(statement).getQueryBindings().getBindValues();
        if(values.length>256)throw new IllegalArgumentException("绑定参数超过采集预算");
        List<PairedJdbcObservation.Binding> result=new ArrayList<>();
        for(int index=0;index<values.length;index++){
            BindValue binding=values[index];boolean unsupported=binding==null||!binding.isSet()||binding.isStream();
            result.add(new PairedJdbcObservation.Binding(index+1,binding==null?null:binding.getMysqlType().name(),unsupported||binding.isNull()?null:binding.getValue(),unsupported));
        }
        return List.copyOf(result);
    }
    /** 读取实际驱动实现种类，不把客户端预处理误报成服务器已准备。 */
    @Override public boolean serverPrepared(PreparedStatement statement)throws Exception{return original(statement) instanceof ServerPreparedStatement;}
}
