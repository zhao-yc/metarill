import java.lang.reflect.Method;
import java.math.BigDecimal;
import java.util.Set;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;

/** 直接调用原固定编译实体与子页VO，核对公开四字段；不启动Spring、数据库或复制VO实现。 */
public final class SampleChildProjectionOracle {
    /** 故意先为实体填入父键和缓存，再确认原列表与详情序列化不会泄漏这些字段。 */
    public static void main(String[] args) throws Exception {
        Class<?> entityType=Class.forName("com.yinzon.entity.SampleContractLine");Object row=entityType.getDeclaredConstructor().newInstance();
        entityType.getMethod("setId",String.class).invoke(row,"9007199254740993");entityType.getMethod("setName",String.class).invoke(row,"原子页明细");
        entityType.getMethod("setSampleContractId",String.class).invoke(row,"9007199254740995");entityType.getMethod("setSampleContractName",String.class).invoke(row,"实体真实父合同");
        entityType.getMethod("setAmount",BigDecimal.class).invoke(row,new BigDecimal("3.25"));entityType.getMethod("setRemarks",String.class).invoke(row,"原备注");
        ObjectMapper mapper=new ObjectMapper();Set<String> expected=Set.of("id","name","amount","remarks");
        for(String kind:new String[]{"Detail","List"}) {
            Class<?> vo=Class.forName("com.yinzon.generated.vo.SampleContractLine81003"+kind+"VO");Method convert=vo.getMethod("fromEntity",entityType);
            JsonNode json=mapper.valueToTree(convert.invoke(null,row));java.util.Set<String> actual=new java.util.HashSet<>();json.fieldNames().forEachRemaining(actual::add);
            if(!actual.equals(expected)||!json.path("id").isTextual()||!json.path("id").asText().equals("9007199254740993"))throw new IllegalStateException("原子页VO合同不符");
            System.out.println(mapper.writeValueAsString(json));
        }
    }
}
