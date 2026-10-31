package com.yinzon.samplehost;

import com.fasterxml.jackson.core.JsonParser;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.yinzon.yzp.project.config.YzpProjectConfigDatabaseSyncService;
import com.yinzon.yzp.project.config.YzpProjectConfigProperties;
import com.yinzon.yzp.project.config.YzpProjectConfigService;
import org.springframework.boot.DefaultApplicationArguments;
import org.springframework.core.io.ByteArrayResource;
import org.springframework.core.io.DefaultResourceLoader;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.datasource.DriverManagerDataSource;
import org.springframework.jdbc.datasource.init.ScriptUtils;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.HexFormat;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.TreeSet;

/** 独占双端夹具的显式Java配置安装器；不注册Bean，普通应用启动不会执行。 */
public final class PairedJavaConfigurationBootstrap {
    private static final ObjectMapper JSON = new ObjectMapper().enable(JsonParser.Feature.STRICT_DUPLICATE_DETECTION);
    private static final List<String> CONFIG_TABLES = List.of("config_button","config_child","config_column","config_first_menu","config_index_column","config_index","config_page_column","config_page","config_second_menu","config_table","yz_admin_type","yz_file_library_folder","yz_file_library","yz_file_ref");
    private static final List<String> ACCOUNT_TABLES = List.of("yz_manager","yz_role","yz_role_permission","yz_manager_log");
    private static final Set<String> BUSINESS_TABLES = Set.of("sample_customer","sample_contract","sample_contract_line");
    public record Plan(String runId, String database, String digest, Path bundle, Map<String,String> sources, Set<String> tables, byte[] metadata) {}
    private record Configuration(DefaultResourceLoader resources, YzpProjectConfigProperties properties, YzpProjectConfigService service) {}

    /** 只有显式入口可执行；离线预览不读取凭据，真实预览/执行必须另有本轮运行授权。 */
    public static void main(String[] args) {
        String stage="arguments";
        try {
            if (args.length != 2 || !Set.of("preview-offline","preview","apply-initial").contains(args[0])) throw new IllegalArgumentException("只接受preview-offline、preview或apply-initial及本轮清单路径");
            JsonNode lease = JSON.readTree(Files.readAllBytes(Path.of(args[1])));
            stage="offline-inputs";Plan plan = prepare(lease);
            Map<String,Object> result = new TreeMap<>();
            result.put("offline",args[0].equals("preview-offline")); result.put("database",plan.database()); result.put("planSha256",plan.digest()); result.put("sourceFiles",plan.sources().size()); result.put("expectedTables",new TreeSet<>(plan.tables())); result.put("applied",false);
            if (!args[0].equals("preview-offline")) {
                stage="lease";requireRunning(lease,plan);
                stage="connection-configuration";var database = database(lease,plan);
                JdbcTemplate jdbc = new JdbcTemplate(database);
                stage="instance";verifyInstance(jdbc,lease,plan);
                long count = jdbc.queryForObject("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()",Long.class);
                result.put("observedTableCount",count);
                result.put("initialApplyAllowed",count == 0);
                if (args[0].equals("apply-initial")) {
                    if (count != 0 || !plan.digest().equals(lease.path("ExpectedPlanSha256").asText())) throw new IllegalStateException("仅允许与已预览摘要一致的本轮全新空库");
                    stage="initial-apply";applyInitial(database,jdbc,plan);
                    Set<String> actual = new TreeSet<>(jdbc.queryForList("SELECT table_name FROM information_schema.tables WHERE table_schema=DATABASE()",String.class));
                    if (!actual.equals(plan.tables())) throw new IllegalStateException("安装后实际表集合与原固定输入不符");
                    result.put("applied",true); result.put("verifiedTables",actual.size());
                }
            }
            System.out.println("结果："+JSON.writeValueAsString(result));
        } catch (Exception error) {
            // SQL/连接正文可能含私有信息，只记录类型，原单次协调器负责归属和失败清理。
            System.err.println("Java配置安装未通过："+error.getClass().getSimpleName()+"；阶段="+stage+"；不自动重试或回退DDL。");
            System.exit(1);
        }
    }

    /** 先核固定文件集合及实际Jar内配置字节，再调用原配置读取器，不接受新造业务模型。 */
    public static Plan prepare(JsonNode lease) throws Exception {
        String run = lease.path("RunId").asText();
        String database = "metarill_fresh_" + (run.length() >= 16 ? run.substring(0,16) : "") + "_paired_java";
        if (lease.path("Version").asInt() != 1 || !lease.path("Purpose").asText().equals("paired-runtime-bootstrap-v1") || !run.matches("[a-f0-9]{32}") || !database.equals(lease.path("ExpectedDatabase").asText())) throw new IllegalArgumentException("双端初始化用途、运行号或唯一库名不符");
        Path bundle = Path.of(lease.path("BundleRoot").asText()).toRealPath();
        byte[] manifest = Files.readAllBytes(bundle.resolve("sources.json"));
        if (!sha(manifest).equals(lease.path("SourcesManifestSha256").asText())) throw new IllegalArgumentException("固定输入清单摘要不符");
        JsonNode files = JSON.readTree(manifest);
        Set<String> expected = new TreeSet<>(Set.of("project-config.json","business-schema.sql"));
        for (String table : CONFIG_TABLES) expected.add("configuration-schema/"+table+".sql");
        for (String table : ACCOUNT_TABLES) expected.add("account-schema/"+table+".sql");
        Set<String> actual = new TreeSet<>();files.fieldNames().forEachRemaining(actual::add);
        if (!actual.equals(expected)) throw new IllegalArgumentException("初始化输入集合必须等于原20项");
        Map<String,String> hashes = new TreeMap<>();
        for (String name : expected) {
            Path file = bundle.resolve(name);
            if (!file.toRealPath().startsWith(bundle) || Files.isSymbolicLink(file)) throw new IllegalArgumentException("初始化来源链接越界");
            byte[] bytes = Files.readAllBytes(file);String checksum=sha(bytes);
            if (!checksum.equals(files.path(name).path("sha256").asText())) throw new IllegalArgumentException("初始化输入字节变化");
            hashes.put(name,checksum);
            String resource = name.equals("project-config.json") ? "yzp-config/project-config.json" : name.startsWith("configuration-schema/") ? "yzp-config/schema/"+file.getFileName() : null;
            if (resource != null) try (var stream = Thread.currentThread().getContextClassLoader().getResourceAsStream(resource)) {
                if (stream == null || !MessageDigest.isEqual(bytes,stream.readAllBytes())) throw new IllegalArgumentException("随Jar配置与固定输入不一致");
            }
        }
        // 该业务DDL由原ConfigTableDbBusiness生成，不能替换为另一个容易通过的三表模型。
        if (!hashes.get("business-schema.sql").equals("6f4fa61800f04505868293f79df5f474f28645a64a4251db6e243dac712af772")) throw new IllegalArgumentException("原三表业务DDL身份不符");
        byte[] metadata=Files.readAllBytes(bundle.resolve("project-config.json"));
        if(!sha(metadata).equals(hashes.get("project-config.json")))throw new IllegalArgumentException("准备期间元数据变化");
        var configuration=configuration(metadata).service();
        if (!"810000000000000001".equals(configuration.getProjectConfig().getYzProjectId()) || configuration.getProjectConfig().getConfigTables().size()!=3 || configuration.getProjectConfig().getConfigPages().size()!=3 || configuration.getProjectConfig().getConfigButtons().size()!=3) throw new IllegalArgumentException("原完整业务目录不符");
        Set<String> tables = new TreeSet<>(CONFIG_TABLES);tables.addAll(ACCOUNT_TABLES);tables.addAll(BUSINESS_TABLES);tables.add("yz_project_config_sync_state");
        String digest=sha(JSON.writeValueAsBytes(new TreeMap<>(Map.of("runId",run,"database",database,"sources",hashes,"tables",tables))));
        return new Plan(run,database,digest,bundle,Map.copyOf(hashes),Set.copyOf(tables),metadata.clone());
    }

    /** 在读取数据库凭据/建立连接前拒绝pending、结束或来源不一致的租约。 */
    public static void requireRunning(JsonNode lease, Plan plan) {
        if (!lease.path("AuthorizationStatus").asText().equals("approved") || !lease.path("LeaseStatus").asText().equals("running") || !lease.path("RunId").asText().equals(plan.runId())) throw new IllegalArgumentException("本轮没有可执行的双端初始化租约");
    }

    /** 仅使用固定200端点与精确本轮受限账号；密码只从私有进程环境读取。 */
    private static DriverManagerDataSource database(JsonNode lease, Plan plan) {
        String url=System.getenv("YZP_SAMPLE_JDBC_URL"),user=System.getenv("YZP_SAMPLE_DB_USER"),password=System.getenv("YZP_SAMPLE_DB_PASSWORD");
        if (url==null || !url.startsWith("jdbc:mysql://")) throw new IllegalArgumentException("缺少本轮JDBC配置");
        URI uri=URI.create(url.substring(5));
        if (!"192.168.31.200".equals(uri.getHost()) || uri.getPort()!=3306 || uri.getUserInfo()!=null || !uri.getPath().equals("/"+plan.database()) || !("mtf_"+plan.runId().substring(0,16)).equals(user) || password==null || !password.matches("[a-f0-9]{64}")) throw new IllegalArgumentException("仅允许固定200实例及本轮受限凭据");
        var source=new DriverManagerDataSource();source.setDriverClassName("com.mysql.cj.jdbc.Driver");source.setUrl(url);source.setUsername(user);source.setPassword(password);return source;
    }

    /** 连接后先核数据库/实例pin，不能仅凭URL名称认定接线正确。 */
    private static void verifyInstance(JdbcTemplate jdbc, JsonNode lease, Plan plan) {
        Map<String,Object> actual=jdbc.queryForMap("SELECT DATABASE() AS d,@@hostname AS h,@@server_uuid AS u");
        JsonNode pin=lease.path("InstancePin");
        if (!plan.database().equals(actual.get("d")) || !pin.path("hostname").asText().equals(actual.get("h")) || !pin.path("serverUuid").asText().equals(actual.get("u"))) throw new IllegalArgumentException("本轮数据库或实例pin不符");
    }

    /** 只安装授权的全新空库；元数据仍由原同步服务写入，普通应用入口不执行此类。 */
    private static void applyInitial(DriverManagerDataSource database, JdbcTemplate jdbc, Plan plan) throws Exception {
        List<String> scripts=new ArrayList<>();scripts.add("business-schema.sql");
        for(String table:ACCOUNT_TABLES)scripts.add("account-schema/"+table+".sql");
        for(String table:List.of("yz_file_library_folder","yz_file_library","yz_file_ref"))scripts.add("configuration-schema/"+table+".sql");
        try(var connection=database.getConnection()) {
            for(String name:scripts) {
                byte[] bytes=Files.readAllBytes(plan.bundle().resolve(name));
                if(!sha(bytes).equals(plan.sources().get(name)))throw new IllegalStateException("执行前来源变化");
                String sql=new String(bytes,StandardCharsets.UTF_8).replace("__YZP_SAMPLE_DATABASE__",plan.database());
                ScriptUtils.executeSqlScript(connection,new ByteArrayResource(sql.getBytes(StandardCharsets.UTF_8)));
            }
        }
        var configuration=configuration(plan.metadata());
        new YzpProjectConfigDatabaseSyncService(configuration.service(),configuration.properties(),jdbc,configuration.resources()).run(new DefaultApplicationArguments());
    }

    /** 原配置服务读取已核对的内存快照，执行阶段不重新消费可变文件或改写原元数据。 */
    private static Configuration configuration(byte[] bytes) {
        byte[] snapshot=bytes.clone();String location="snapshot:metarill-paired-project";
        var resources=new DefaultResourceLoader(){
            @Override public org.springframework.core.io.Resource getResource(String path) {
                return location.equals(path)?new ByteArrayResource(snapshot):super.getResource(path);
            }
        };
        var properties=new YzpProjectConfigProperties();properties.setLocation(location);
        var service=new YzpProjectConfigService(resources,properties);service.loadProjectConfig();
        return new Configuration(resources,properties,service);
    }

    /** 只输出输入与计划摘要，不输出配置或数据库凭据。 */
    private static String sha(byte[] bytes) throws Exception {
        return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(bytes));
    }
}
