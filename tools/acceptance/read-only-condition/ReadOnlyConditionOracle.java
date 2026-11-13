import com.alibaba.fastjson2.JSON;
import com.alibaba.fastjson2.JSONArray;
import com.alibaba.fastjson2.JSONObject;
import java.lang.reflect.InvocationTargetException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

/** 直接执行冻结原Java只读守卫；不复制解析器，不启动Spring或连接数据库。 */
public final class ReadOnlyConditionOracle {
    /** 公共向量对照原方法的正常、只读拒绝与无法解释三种结果。 */
    public static void main(String[] args) throws Exception {
        Class<?> guardType=Class.forName("com.yinzon.yzp.runtime.agent.YzpAgentReadOnlyGuard");
        Class<?> pageType=Class.forName("com.yinzon.entity.ConfigPage");
        Object guard=guardType.getDeclaredConstructor().newInstance();
        var method=guardType.getMethod("requireWritable",pageType,Object.class);
        var setter=pageType.getMethod("setReadOnlyCondition",String.class);
        JSONArray samples=JSON.parseArray(Files.readString(Path.of(args[0]),StandardCharsets.UTF_8));
        int mismatches=0;
        for(int index=0;index<samples.size();index++) {
            JSONObject sample=samples.getJSONObject(index);Object page=pageType.getDeclaredConstructor().newInstance();
            setter.invoke(page,sample.getString("condition"));String outcome="writable";
            try {method.invoke(guard,page,sample.getJSONObject("row"));}
            catch(InvocationTargetException error) {
                if(!(error.getCause() instanceof IllegalArgumentException failure))throw error;
                outcome=failure.getMessage().startsWith("当前记录为只读")?"readonly":"invalid";
            }
            if(!outcome.equals(sample.getString("outcome")))mismatches++;
            System.out.println(JSON.toJSONString(java.util.Map.of("name",sample.getString("name"),"outcome",outcome,"expected",sample.getString("outcome"))));
        }
        if(mismatches!=0)throw new IllegalStateException("原Java对照不一致数量："+mismatches);
    }
}
