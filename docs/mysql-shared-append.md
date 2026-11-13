# MySQL 同父追加共享锁

此能力用于独立应用的固定父键追加导入。默认配置下走原父记录排他锁。只有符合单一物理缓存、叶子来源、默认 ORM 和无逐行副作用的页面会尝试共享锁。普通新增、更新、覆盖导入及有手写规则的导入继续使用原锁路径。存在宿主写入守卫时，每个守卫都须明确声明其准备及写入复核在当前表无副作用。内置安全守卫只对账号、角色、后台类型与角色授权之外的表允许缓冲，未声明的自定义守卫保持原路径。生成项目通过再生成取得此变更。

## 离线安装计划

以重新生成的正式独立包为准。先在私有配置准备下面的 `Database:MySqlSharedAppendProofs`，并将 `SharedAppendInstallation:RuntimeHost` 设为 MySQL 已有运行账号的准确 Host。计划从私有连接串读取用户名和预期库。不要在命令行传连接串或密码。

```sh
dotnet run --project backend/Application.Host -- --shared-append-plan
```

命令只输出 JSON，不连接 MySQL、不启动 Web，也不执行 SQL。计划复用运行时的固定父表和叶子明细判定。页面权限、守卫、观察者和最终效果仍由各自的运行验证覆盖。每项计划都要求新建独立的锁定定义者和例程，证明库应由部署管理员预先创建和管理。

在同一个明确的目标实例上，管理员先执行每项 `targetCheckSql`，只在 `target_ready` 为 `1` 时继续。检查包含实际 UUID、InnoDB 和单列主键、父表外键、明细触发器、准确运行账号、定义者及例程是否已占用。按 `installSql` 给出的顺序逐条执行，任一步错误立即停止并核对已成功的步骤。MySQL 这些 DDL 会分别生效。已有例程按原归属人工核验，不重复应用首次安装计划。

执行 `administratorReviewSql` 核对例程正文和定义者，再用真实运行账号执行 `runtimeProbeSql`，核对契约、业务库、明细表、父表和最后两列分别为 `1`、`0`。以下手工示例用于说明授权范围。

## 启用前复核

完成管理员审阅后，使用部署将采用的运行账号连接串、`SharedAppendInstallation:RuntimeHost` 和证明配置执行：

```sh
dotnet run --project backend/Application.Host -- --shared-append-check
```

命令逐表执行同一运行时的锁前证明。两条实际连接均核对库名、实例 UUID 和 MySQL 的 `CURRENT_USER()`。它会固定并核对表结构、调用已审阅的证明例程，随后释放结构锁。命令自身不发出 DDL 或业务写入，也不启动 Web。例程正文和部署账号的权限边界仍由管理员审阅。

仅全部条目 `ready: true` 时退出码为 0。任一条目不就绪时输出对应表并返回 1，配置或连接异常也返回 1，并且不会输出连接串或数据库异常原文。命令需要连接重置和允许用户变量。业务池上限可以为 1，服务器仍须允许同一账号同时建立业务及证明连接。复核结果只反映当前安装，后续导入仍根据当次证据选择锁路径。

## 手工 SQL 示例

部署管理员先核对实际业务库、两张实体表和实例 UUID。使用部署账号安装以下定向例程。示例中的账号、库名、表名和例程名应替换成实际部署值，运行账号只能调用已登记的例程。使用受部署管理员控制的证明库，业务运行账号在其中只需要例程执行权限。例程定义者不能登录，只授明细表 `TRIGGER` 及该例程 `EXECUTE`。运行账号无需获得 `TRIGGER`、`SHOW_ROUTINE`、全局 `SELECT` 或 `PROCESS`。 [MySQL 例程权限](https://dev.mysql.com/doc/refman/8.4/en/stored-routines-privileges.html)

```sql
CREATE USER 'metarill_proof_reader'@'localhost' ACCOUNT LOCK;
GRANT TRIGGER ON business_db.child_table TO 'metarill_proof_reader'@'localhost';

CREATE DEFINER = 'metarill_proof_reader'@'localhost'
PROCEDURE proof_db.read_child_append_proof()
READS SQL DATA
SQL SECURITY DEFINER
COMMENT 'metarill-shared-append-v1'
SELECT
    'metarill-shared-append-v1',
    'business_db',
    'child_table',
    'parent_table',
    EXISTS (
        SELECT 1
        FROM information_schema.TABLE_PRIVILEGES
        WHERE GRANTEE = CONCAT(QUOTE('metarill_proof_reader'), '@', QUOTE('localhost'))
          AND TABLE_SCHEMA = 'business_db'
          AND TABLE_NAME = 'child_table'
          AND PRIVILEGE_TYPE = 'TRIGGER'
    ),
    EXISTS (
        SELECT 1
        FROM information_schema.TRIGGERS
        WHERE EVENT_OBJECT_SCHEMA = 'business_db'
          AND EVENT_OBJECT_TABLE = 'child_table'
          AND EVENT_MANIPULATION IN ('INSERT', 'UPDATE')
    );

GRANT EXECUTE ON PROCEDURE proof_db.read_child_append_proof TO 'metarill_proof_reader'@'localhost';
GRANT EXECUTE ON PROCEDURE proof_db.read_child_append_proof TO 'business_runtime'@'%';
```

部署管理员用 `SHOW CREATE PROCEDURE` 核验定义者、例程全文、结果六列和目标表，并用普通运行账号执行一次已登记的例程。运行账号能核对公开例程属性与输出，但仅有 `EXECUTE` 时看不到例程正文。保持例程、定义者和授权由部署账号管理。变更例程或定义者授权前先停用该配置并等待在途请求结束，再按同样步骤核验并启用。 [MySQL ROUTINES 元数据](https://dev.mysql.com/doc/refman/8.4/en/information-schema-routines-table.html)

通过独立应用的 `Database` 配置节按物理明细表登记。每个可能使用共享追加的并发请求，在建立父锁前短暂借用独立的证明连接。证明池按需创建，沿用同一账号、目标和连接上限，最小连接数为 0，随数据库会话释放。容量规划时业务池与证明池分别计入，数据库账号的连接限额须覆盖两者及其他同账号客户端。不要将运行凭据写入示例或公开仓库。

```json
{
  "Database": {
    "MySqlSharedAppendProofs": {
      "child_table": {
        "ServerUuid": "00000000-0000-0000-0000-000000000000",
        "ParentTable": "parent_table",
        "Schema": "proof_db",
        "Procedure": "read_child_append_proof",
        "Definer": "metarill_proof_reader@localhost"
      }
    }
  },
  "SharedAppendInstallation": {
    "RuntimeHost": "%"
  }
}
```

运行时先绑定实际实例及业务库，并在原事务内固定父子表结构。另一个连接核验表结构和例程状态，避免提前建立业务一致性快照。父表存在外键、结构无法识别、例程或执行权缺失、定义者表级权限缺失、相关触发器存在或证明值不匹配时，继续原排他父锁路径。共享证明只在原事务内有效。候选重验、缓存读回和整批回滚仍按原导入契约执行。

停用时移除对应配置并等待在途请求结束。回到原排他锁后，可由部署管理员撤销运行账号该例程的执行权限，再按部署归属删除例程及专用定义者账号。应用启动与再生成均不执行这些 DDL。

已登记的例程可在同一事务为单次缓存写入提供触发器证明。运行时仍单独核验列、索引、约束和字符集。符合条件的八行导入只保留首行缓存 UPDATE，后七行在 INSERT 一次写入。保留已有例程和授权，更新生成项目的宿主运行时并重新发布即可生效。新增 CHECK 时保持双阶段写入，撤权或出现写触发器时回到排他父锁。见 [V703 部署证明复用](testing/inline-cache-shared-proof-v703.md)。

## 实测范围

当前生成合同应用的禁网计划、五条语句安装、真实账号缺失／安装／撤权／恢复四态及原八行导入已实测。见 [V882／V883 当前生成宿主安装与复核](testing/current-child-shared-proof-cli-v883.md)。

固定产品 ffcd80d3 复验现有配置。每笔缓存 UPDATE 从八条降为一条，binlog 减少 48.7%。四并发吞吐为 Java 的 134.1%–139.5%，p95 为 74.8%–86.4%。此次覆盖同父八行导入。[V880 共享追加与日志量](testing/current-child-shared-log-volume-v880.md)。

完整独立应用使用显式共享锁配置时，同父八行明细导入在并发 4 的三轮均达标。C# 吞吐为 Java 的 117.6%～122.3%，p95 为 Java 的 80.6%～90.1%。同一镜像的默认排他锁配置仍触发稳定退化。这里的性能结论只覆盖已实测的工作簿和配置。 [共享锁正式对照](testing/paired-child-shared-guard-scope-timing-v399.md) · [默认配置正式对照](testing/paired-child-default-guard-scope-timing-v398.md)

完整包禁网生成计划、实际 MySQL 安装、真实运行账号复核及原导入回滚已在固定独占样本验收。 [安装与原导入实库记录](testing/paired-child-installation-plan-mysql-v402.md)

证明例程位于独立库时，安装计划、最小授权及真实账号调用也已在固定独占样本验收。撤销该例程的执行权后，原导入实测回退排他锁，恢复执行权后回到共享锁。 [独立证明库与撤权实库验收](testing/paired-child-separated-proof-schema-v403.md)

当前产品 190a7da4 已复核同一镜像下的默认配置与显式共享配置。显式共享配置两个并发档均未触发稳定退化，四并发吞吐为 Java 的 111.0%／130.9%／105.8%。撤销并恢复本轮运行账号的例程执行权后，原导入实际从共享转为排他等待，再恢复共享，三次前检均回滚。 [当前构建正式对照](testing/paired-child-current-lock-modes-v434-v435.md)

V439 补充了证明连接池的容量验收。旧包在业务池上限为 1 时复现同池等待超时。改用独立证明池后，业务池上限为 1 和 4 的完整应用均通过原导入，回滚和证明撤权仍生效。该轮没有执行 Java／C# 正式计时。 [连接池实库验收](testing/paired-child-shared-proof-pool-v439.md)
