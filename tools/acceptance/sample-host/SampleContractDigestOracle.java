import java.lang.reflect.Constructor;
import java.lang.reflect.Method;
import java.math.BigDecimal;
import java.nio.charset.StandardCharsets;
import java.util.Base64;

/** 直接调用固定Java b6d64fb已编译样板的原私有摘要方法，不复制另一份摘要算法。 */
public final class SampleContractDigestOracle {
    /** 类路径必须来自原固定样板；只构造普通对象，不启动Spring或数据库。 */
    public static void main(String[] args) throws Exception {
        if (args.length % 4 != 0) throw new IllegalArgumentException("摘要对照参数必须成组");
        Class<?> entityType = Class.forName("com.yinzon.entity.SampleContract");
        Class<?> businessType = Class.forName("com.yinzon.samplehost.SampleContractAgentBusiness");
        Constructor<?> constructor = businessType.getDeclaredConstructors()[0];
        if (constructor.getParameterCount() != 4) throw new IllegalStateException("原样板构造合同已变化");
        Object business = constructor.newInstance(new Object[] { null, null, null, null });
        Method digest = businessType.getDeclaredMethod("digest", entityType); digest.setAccessible(true);
        for (int i = 0; i < args.length; i += 4) {
            Object row = entityType.getDeclaredConstructor().newInstance();
            entityType.getMethod("setId", String.class).invoke(row, args[i]);
            entityType.getMethod("setName", String.class).invoke(row, text(args[i + 1]));
            entityType.getMethod("setAmount", BigDecimal.class).invoke(row, args[i + 2].equals("-") ? null : new BigDecimal(args[i + 2]));
            entityType.getMethod("setRemarks", String.class).invoke(row, text(args[i + 3]));
            System.out.println(digest.invoke(business, row));
        }
    }
    /** 每组参数为ID、Base64名称、金额、Base64备注；短横线单独表示null。 */
    private static String text(String value) { return value.equals("-") ? null : new String(Base64.getDecoder().decode(value), StandardCharsets.UTF_8); }
}
