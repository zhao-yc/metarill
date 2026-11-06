# MySQL 同父追加共享锁

此能力用于独立应用的固定父键追加导入。默认配置下走原父记录排他锁。只有符合单一物理缓存、叶子来源、默认 ORM 和无逐行副作用的页面会尝试共享锁。普通新增、更新、覆盖导入及有手写规则的导入继续使用原锁路径。存在宿主写入守卫时，每个守卫都须明确声明其准备及写入复核在当前表无副作用。内置安全守卫只对账号、角色、后台类型与角色授权之外的表允许缓冲，未声明的自定义守卫保持原路径。生成项目通过再生成取得此变更。

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

通过独立应用的 `Database` 配置节按物理明细表登记。每个可能使用共享追加的并发请求，在建立父锁前短暂借用第二条连接读取证明，连接池应为此保留容量。不要将运行凭据写入示例或公开仓库。

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
  }
}
```

运行时先绑定实际实例及业务库，并在原事务内固定父子表结构。另一个连接核验表结构和例程状态，避免提前建立业务一致性快照。父表存在外键、结构无法识别、例程或执行权缺失、定义者表级权限缺失、相关触发器存在或证明值不匹配时，继续原排他父锁路径。共享证明只在原事务内有效。候选重验、缓存读回和整批回滚仍按原导入契约执行。

停用时移除对应配置并等待在途请求结束。回到原排他锁后，可由部署管理员撤销运行账号该例程的执行权限，再按部署归属删除例程及专用定义者账号。应用启动与再生成均不执行这些 DDL。
