import com.alibaba.fastjson2.JSON;
import com.alibaba.fastjson2.JSONArray;
import com.alibaba.fastjson2.JSONObject;
import java.lang.reflect.Method;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.charset.StandardCharsets;

/** 调用固定Java原ButtonVisibilityBusiness的私有规则方法，不复制规则实现，不启动应用或数据库。 */
public final class ButtonVisibilityOracle {
    /** 类路径必须来自冻结样板；输出仅含通用样本名和比较结论。 */
    public static void main(String[] args) throws Exception {
        Class<?> type=Class.forName("com.yinzon.yzp.runtime.button.ButtonVisibilityBusiness");
        Object target=type.getDeclaredConstructors()[0].newInstance(new Object[]{null,null,null,null});
        Method method=type.getDeclaredMethod("matchesRule",String.class,java.util.Map.class);method.setAccessible(true);
        JSONArray samples=JSON.parseArray(Files.readString(Path.of(args[0]),StandardCharsets.UTF_8));
        for(int i=0;i<samples.size();i++) {
            JSONObject sample=samples.getJSONObject(i);
            JSONObject row=sample.getJSONObject("row");
            // 仅测试夹具把类型标签还原为真实JDBC值，原规则实现不认识这些标签。
            for(String key:java.util.List.copyOf(row.keySet())) {
                if(row.get(key) instanceof JSONObject typed) {
                    if(typed.containsKey("$jdbcTimestamp"))row.put(key,java.sql.Timestamp.valueOf(typed.getString("$jdbcTimestamp")));
                    if(typed.containsKey("$jdbcDate"))row.put(key,java.sql.Date.valueOf(typed.getString("$jdbcDate")));
                    if(typed.containsKey("$jdbcTime"))row.put(key,java.sql.Time.valueOf(typed.getString("$jdbcTime")));
                }
            }
            boolean actual=(boolean)method.invoke(target,JSON.toJSONString(sample.get("rule")),row);
            if(actual!=sample.getBooleanValue("expected"))throw new IllegalStateException("原Java对照不一致："+sample.getString("name"));
            System.out.println(JSON.toJSONString(java.util.Map.of("name",sample.getString("name"),"matched",actual)));
        }
    }
}
