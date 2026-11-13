import com.sun.source.tree.ClassTree;
import com.sun.source.tree.CompilationUnitTree;
import com.sun.source.tree.MethodTree;
import com.sun.source.tree.Tree;
import com.sun.source.tree.VariableTree;
import com.sun.source.util.JavacTask;
import com.sun.source.util.Trees;
import javax.tools.Diagnostic;
import javax.tools.DiagnosticCollector;
import javax.tools.JavaFileObject;
import javax.tools.ToolProvider;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.HexFormat;
import java.util.List;
import java.util.Set;

/** 从固定Java基线逐字提取原登录入口；只写新宿主源码，不编译或启动应用。 */
public final class LoginHostSourceGenerator {
    private static final String COMMIT = "b6d64fb2ad4ac25b004eeaa326092de3cfb7f311";
    private static final Set<String> METHODS = Set.of("capConfig", "login", "handleLoginFailure", "increaseFailCountAndMaybeLock", "clearLoginFailState", "hasRedisKey", "deleteRedisKey", "buildRedisKey", "writeLoginFailLog");
    private static final Set<String> FIELDS = Set.of("ACCOUNT_FAIL_KEY_PREFIX", "IP_FAIL_KEY_PREFIX", "ACCOUNT_LOCK_KEY_PREFIX", "IP_LOCK_KEY_PREFIX", "ACCOUNT_FAIL_LIMIT", "IP_FAIL_LIMIT", "ACCOUNT_FAIL_WINDOW", "IP_FAIL_WINDOW", "ACCOUNT_LOCK_DURATION", "IP_LOCK_DURATION", "LOGIN_FAIL_MESSAGE", "ACCOUNT_LOCK_MESSAGE", "IP_LOCK_MESSAGE", "redisTemplate", "yzManagerService", "adminCapCaptchaVerifier", "adminCapCaptchaProperties", "yzManagerLogService", "sessionSeconds");
    private static final String CONTROLLER = "backend/src/main/java/com/yinzon/samplehost/PairedBaselineLoginController.java";
    private record Input(Path source, Path destination, byte[] content) {}
    private record Extracted(String controller, List<String> methodProof) {}

    /** 输入必须与版本化清单逐字一致；整批预检后才写入，旧文件不同就拒绝。 */
    public static void main(String[] arguments) throws Exception {
        if (arguments.length != 3) throw new IllegalArgumentException("用法：java LoginHostSourceGenerator.java <来源清单> <固定来源根> <新宿主根>");
        Path manifest = Path.of(arguments[0]);
        Path sourceRoot = Path.of(arguments[1]).toRealPath();
        Path host = Path.of(arguments[2]).toRealPath();
        if (host.startsWith(sourceRoot) || sourceRoot.startsWith(host)) throw new IllegalArgumentException("输入与新宿主必须是独立目录");
        List<String> lines = Files.readAllLines(manifest, StandardCharsets.UTF_8);
        if (!lines.contains("# sourceCommit=" + COMMIT)) throw new IllegalArgumentException("原Java来源版本不符");
        List<Input> inputs = new ArrayList<>();
        Set<Path> destinations = new HashSet<>();
        List<String> proof = new ArrayList<>();
        proof.add("# 原Java登录来源 " + COMMIT + "；不表示应用/登录已实际运行");
        int extracted = 0;
        for (String line : lines) {
            if (line.isBlank() || line.startsWith("#")) continue;
            String[] parts = line.split("\t", -1);
            if (parts.length != 3 || !parts[0].matches("[a-f0-9]{64}")) throw new IllegalArgumentException("清单格式错误");
            Path source = safe(sourceRoot, parts[1]);
            if (!source.toRealPath().startsWith(sourceRoot)) throw new IllegalArgumentException("来源越界");
            byte[] content = Files.readAllBytes(source);
            if (!sha(content).equals(parts[0])) throw new IllegalArgumentException("固定来源摘要不符：" + parts[1]);
            Path destination;
            if (parts[2].equals("@extract-login")) {
                if (++extracted != 1 || !parts[1].endsWith("/controller/admin/sys/YzManagerControllerSys.java")) throw new IllegalArgumentException("登录提取来源无效");
                Extracted result = extract(source, new String(content, StandardCharsets.UTF_8));
                content = result.controller().getBytes(StandardCharsets.UTF_8);
                destination = safe(host, CONTROLLER);
                proof.addAll(result.methodProof());
            } else if (parts[2].equals("@verify-existing-rest-template")) {
                // 该配置已在固定347文件中；Git源为LF、原样板为CRLF，只核对同源及原字节，不覆盖。
                destination = safe(host, "backend/src/main/java/com/yinzon/config/RestTemplateConfig.java");
                byte[] existing = Files.readAllBytes(destination);
                if (!sha(existing).equals("15a2149460f7aa57fea47461fdcae4fb3790a0a44047aca77f6200e0169f607c")
                        || !new String(existing, StandardCharsets.UTF_8).replace("\r\n", "\n").equals(new String(content, StandardCharsets.UTF_8).replace("\r\n", "\n"))) throw new IllegalArgumentException("既有HTTP配置不再是固定同源字节");
                content = existing;
            } else {
                if (!parts[2].startsWith("backend/src/") || !(parts[2].endsWith(".java") || parts[2].endsWith(".sql"))) throw new IllegalArgumentException("只允许原Java依赖或显式验收DDL");
                destination = safe(host, parts[2]);
            }
            if (!destinations.add(destination)) throw new IllegalArgumentException("重复输出路径");
            checkParents(host, destination);
            if (Files.exists(destination) && (Files.isSymbolicLink(destination) || !destination.toRealPath().startsWith(host))) throw new IllegalArgumentException("已有输出链接越界");
            if (Files.exists(destination) && !MessageDigest.isEqual(content, Files.readAllBytes(destination))) throw new IllegalArgumentException("已有宿主文件不同，拒绝覆盖：" + host.relativize(destination));
            inputs.add(new Input(source, destination, content));
            proof.add(parts[0] + "\t" + parts[1] + "\t" + sha(content) + "\t" + host.relativize(destination).toString().replace('\\', '/'));
        }
        if (extracted != 1 || inputs.size() != 14) throw new IllegalArgumentException("必须包含完整14项原登录清单");
        Path receipt = safe(host, "backend/paired-login/source-proof.tsv");
        if (Files.exists(receipt)) throw new IllegalArgumentException("已有提取回执，禁止覆盖重跑");
        for (Input input : inputs) {
            checkParents(host, input.destination());
            if (Files.exists(input.destination())) continue;
            Files.createDirectories(input.destination().getParent());
            Files.write(input.destination(), input.content(), StandardOpenOption.CREATE_NEW);
        }
        Files.createDirectories(receipt.getParent());
        Files.writeString(receipt, String.join("\n", proof) + "\n", StandardCharsets.UTF_8, StandardOpenOption.CREATE_NEW);
        System.out.println("原登录9个方法/19个字段已逐字提取，14项来源核对；既有HTTP配置未改写，未启动应用。");
    }

    /** 使用JDK语法树定位完整成员，不按括号正则或手工截取成功分支。 */
    private static Extracted extract(Path file, String source) throws Exception {
        var compiler = ToolProvider.getSystemJavaCompiler();
        if (compiler == null) throw new IllegalArgumentException("需要JDK21编译器，不能使用JRE");
        var diagnostics = new DiagnosticCollector<JavaFileObject>();
        try (var manager = compiler.getStandardFileManager(diagnostics, null, StandardCharsets.UTF_8)) {
            var task = (JavacTask) compiler.getTask(null, manager, diagnostics, List.of("-proc:none", "--release", "21"), null, manager.getJavaFileObjects(file));
            CompilationUnitTree unit = task.parse().iterator().next();
            if (diagnostics.getDiagnostics().stream().anyMatch(item -> item.getKind() == Diagnostic.Kind.ERROR)) throw new IllegalArgumentException("原Controller语法不符");
            ClassTree type = (ClassTree) unit.getTypeDecls().stream().filter(item -> item instanceof ClassTree c && c.getSimpleName().contentEquals("YzManagerControllerSys")).findFirst().orElseThrow();
            var positions = Trees.instance(task).getSourcePositions();
            StringBuilder fields = new StringBuilder();
            StringBuilder methods = new StringBuilder();
            Set<String> foundFields = new HashSet<>(), foundMethods = new HashSet<>();
            List<String> proof = new ArrayList<>();
            for (Tree member : type.getMembers()) {
                boolean field = member instanceof VariableTree variable && FIELDS.contains(variable.getName().toString());
                boolean method = member instanceof MethodTree value && METHODS.contains(value.getName().toString());
                if (!field && !method) continue;
                String name = field ? ((VariableTree) member).getName().toString() : ((MethodTree) member).getName().toString();
                if (!(field ? foundFields : foundMethods).add(name)) throw new IllegalArgumentException("原登录成员重复");
                int start = Math.toIntExact(positions.getStartPosition(unit, member));
                int end = Math.toIntExact(positions.getEndPosition(unit, member));
                String text = source.substring(start, end);
                if (method) methods.append("    /** 原方法逐字提取，来源与摘要见source-proof.tsv。 */\n");
                (field ? fields : methods).append(text).append("\n\n");
                proof.add("# " + (field ? "field" : "method") + "\t" + name + "\t" + sha(text.getBytes(StandardCharsets.UTF_8)));
            }
            if (!foundFields.equals(FIELDS) || !foundMethods.equals(METHODS)) throw new IllegalArgumentException("原登录字段或方法不完整");
            String header = """
                    package com.yinzon.samplehost;
                    import com.alibaba.fastjson2.JSON;
                    import com.alibaba.fastjson2.JSONObject;
                    import com.baomidou.mybatisplus.core.conditions.query.LambdaQueryWrapper;
                    import com.yinzon.business.captcha.AdminCapCaptchaException;
                    import com.yinzon.business.captcha.AdminCapCaptchaProperties;
                    import com.yinzon.business.captcha.AdminCapCaptchaVerifier;
                    import com.yinzon.common.IpUtils;
                    import com.yinzon.common.Md5Utils;
                    import com.yinzon.common.R;
                    import com.yinzon.entity.YzManager;
                    import com.yinzon.entity.YzManagerLog;
                    import com.yinzon.enums.ActionType;
                    import com.yinzon.service.YzManagerService;
                    import com.yinzon.service.YzManagerLogService;
                    import lombok.extern.slf4j.Slf4j;
                    import org.springframework.beans.factory.annotation.Autowired;
                    import org.springframework.beans.factory.annotation.Value;
                    import org.springframework.data.redis.core.RedisTemplate;
                    import jakarta.servlet.http.HttpServletRequest;
                    import org.springframework.web.bind.annotation.*;
                    import java.time.Duration;
                    import java.time.LocalDateTime;
                    import java.util.UUID;
                    /** 对照宿主仅接原密码登录；原三表CRUD和权限拦截器保持原生成来源。 */
                    @Slf4j
                    @RestController
                    @RequestMapping("/admin/sys/yz_manager")
                    public class PairedBaselineLoginController {
                    """;
            return new Extracted(header + fields + methods + "}\n", proof);
        }
    }

    /** 路径必须是规范相对形式，避免清单使读写越过指定根。 */
    private static Path safe(Path root, String relative) {
        if (relative.isBlank() || relative.contains("\\") || relative.contains(":")) throw new IllegalArgumentException("清单路径无效");
        Path path = Path.of(relative);
        if (path.isAbsolute() || !path.normalize().equals(path) || !path.toString().replace('\\', '/').equals(relative) || path.startsWith("..")) throw new IllegalArgumentException("清单路径越界");
        Path result = root.resolve(path).normalize();
        if (!result.startsWith(root) || result.equals(root)) throw new IllegalArgumentException("清单路径不在指定根内");
        return result;
    }

    /** 既有目录的真实位置必须仍在新宿主内，不能跟随junction写出边界。 */
    private static void checkParents(Path root, Path file) throws Exception {
        for (Path parent = file.getParent(); parent != null && parent.startsWith(root); parent = parent.getParent()) {
            if (Files.exists(parent) && !parent.toRealPath().startsWith(root)) throw new IllegalArgumentException("输出目录链接越界");
        }
    }

    /** 只记录源码摘要，不把凭据或环境配置写入提取记录。 */
    private static String sha(byte[] data) throws Exception {
        return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(data));
    }
}
