package com.yinzon.samplehost;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.util.HexFormat;
import java.util.Set;

/** 使用真实固定元数据/原读取器的离线入口反例，不建立数据库或Redis连接。 */
public final class PairedJavaConfigurationBootstrapSelfTest {
    private static final ObjectMapper JSON=new ObjectMapper();
    private static int passed;

    /** 所有破坏性反例只作用于新的私有输入副本，失败/完成证据全部保留。 */
    public static void main(String[] args) throws Exception {
        if(args.length!=2)throw new IllegalArgumentException("需要原固定输入和全新用例目录");
        Path original=Path.of(args[0]).toRealPath();Path root=Path.of(args[1]);Files.createDirectory(root);
        ObjectNode valid=lease(original);
        var plan=PairedJavaConfigurationBootstrap.prepare(valid);
        check(plan.sources().size()==20&&plan.tables().size()==22&&plan.tables().containsAll(Set.of("sample_customer","sample_contract","sample_contract_line","yz_manager_log")));
        passed++;
        check(plan.digest().equals(PairedJavaConfigurationBootstrap.prepare(valid).digest()));passed++;
        for(String state:new String[]{"pending","ended"}) {
            ObjectNode invalid=valid.deepCopy();invalid.put("AuthorizationStatus",state);invalid.put("LeaseStatus",state);
            rejected(()->PairedJavaConfigurationBootstrap.requireRunning(invalid,plan));passed++;
        }
        ObjectNode running=valid.deepCopy();running.put("AuthorizationStatus","approved");running.put("LeaseStatus","running");
        PairedJavaConfigurationBootstrap.requireRunning(running,plan);passed++;
        ObjectNode foreign=valid.deepCopy();foreign.put("ExpectedDatabase","existing_platform");rejected(()->PairedJavaConfigurationBootstrap.prepare(foreign));passed++;
        Path drift=copy(original,root.resolve("drift"));Files.writeString(drift.resolve("business-schema.sql"),"CREATE TABLE foreign_data(id bigint);");
        rejected(()->PairedJavaConfigurationBootstrap.prepare(lease(drift)));passed++;
        Path rewritten=copy(original,root.resolve("rewritten-source"));Files.writeString(rewritten.resolve("business-schema.sql"),"CREATE TABLE foreign_data(id bigint);");
        ObjectNode files=(ObjectNode)JSON.readTree(Files.readAllBytes(rewritten.resolve("sources.json")));
        ((ObjectNode)files.path("business-schema.sql")).put("sha256",sha(Files.readAllBytes(rewritten.resolve("business-schema.sql"))));
        Files.write(rewritten.resolve("sources.json"),JSON.writeValueAsBytes(files));
        rejected(()->PairedJavaConfigurationBootstrap.prepare(lease(rewritten)));passed++;
        Path extra=copy(original,root.resolve("extra"));files=(ObjectNode)JSON.readTree(Files.readAllBytes(extra.resolve("sources.json")));
        files.putObject("../../outside.sql").put("sha256","a".repeat(64));Files.write(extra.resolve("sources.json"),JSON.writeValueAsBytes(files));
        rejected(()->PairedJavaConfigurationBootstrap.prepare(lease(extra)));passed++;
        Path metadata=copy(original,root.resolve("metadata"));String content=Files.readString(metadata.resolve("project-config.json"));
        Files.writeString(metadata.resolve("project-config.json"),content+" ");files=(ObjectNode)JSON.readTree(Files.readAllBytes(metadata.resolve("sources.json")));
        ((ObjectNode)files.path("project-config.json")).put("sha256",sha(Files.readAllBytes(metadata.resolve("project-config.json"))));Files.write(metadata.resolve("sources.json"),JSON.writeValueAsBytes(files));
        rejected(()->PairedJavaConfigurationBootstrap.prepare(lease(metadata)));passed++;
        Files.writeString(root.resolve("result.json"),"{\"passed\":"+passed+",\"databaseConnected\":false,\"scriptsExecuted\":false}");
        System.out.println("Java显式初始化离线"+passed+"项通过；未读取凭据、连接数据库或执行SQL。");
    }

    /** 测试只生成pending描述，不包含任何真实数据库配置。 */
    private static ObjectNode lease(Path root) throws Exception {
        String run="ab".repeat(16);ObjectNode node=JSON.createObjectNode();
        node.put("Version",1);node.put("Purpose","paired-runtime-bootstrap-v1");node.put("RunId",run);
        node.put("ExpectedDatabase","metarill_fresh_"+run.substring(0,16)+"_paired_java");node.put("BundleRoot",root.toString());
        node.put("SourcesManifestSha256",sha(Files.readAllBytes(root.resolve("sources.json"))));node.put("AuthorizationStatus","pending");node.put("LeaseStatus","not-started");return node;
    }

    /** 仅复制普通固定输入到新目录，不使用递归删除或覆盖原始证据。 */
    private static Path copy(Path from,Path to) throws Exception {
        Files.createDirectory(to);try(var entries=Files.walk(from)) {
            for(Path source:entries.toList()) {
                Path target=to.resolve(from.relativize(source));
                if(Files.isDirectory(source))Files.createDirectories(target);else Files.copy(source,target);
            }
        }return to.toRealPath();
    }

    /** 反例必须由真实边界拒绝，不把任意异常或未运行当作通过。 */
    private static void rejected(Action action) throws Exception {
        try{action.run();}catch(IllegalArgumentException expected){return;}
        throw new AssertionError("反例未拒绝");
    }
    private interface Action {void run() throws Exception;}

    /** 断言只验证行为，不输出源配置或散列前数据。 */
    private static void check(boolean value){if(!value)throw new AssertionError("离线合同不符");}
    /** 使用原始字节摘要区分同义JSON、换行与实际固定产物。 */
    private static String sha(byte[] value) throws Exception{return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(value));}
}
