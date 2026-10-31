// 由 tools/metadata/derive-agent.mjs 从平台元数据派生；禁止手工修改。
namespace Metarill.Runtime.Agent;

public static partial class AgentFoundationCatalog
{
    private const string Definition = """
{
  "schemaVersion": 1,
  "backendTarget": "csharp",
  "yzProjectId": "1608097412978577409",
  "yzProjectName": "Metarill",
  "databaseProvider": "mysql",
  "configTables": [
    {
      "id": "2200000000000000323",
      "tableName": "sys_agent_grant",
      "isFunctionTable": 0,
      "isView": 0
    },
    {
      "id": "2200000000000000342",
      "tableName": "sys_agent_task",
      "isFunctionTable": 0,
      "isView": 0
    }
  ],
  "configColumns": [
    {
      "id": "2200000000000000324",
      "configTableId": "2200000000000000323",
      "columnName": "id",
      "logicalType": "int64",
      "columnLength": "",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000325",
      "configTableId": "2200000000000000323",
      "columnName": "insert_time",
      "logicalType": "datetime",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000326",
      "configTableId": "2200000000000000323",
      "columnName": "update_time",
      "logicalType": "datetime",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000327",
      "configTableId": "2200000000000000323",
      "columnName": "insert_manager_id",
      "logicalType": "int64",
      "columnLength": "",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000328",
      "configTableId": "2200000000000000323",
      "columnName": "update_manager_id",
      "logicalType": "int64",
      "columnLength": "",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000329",
      "configTableId": "2200000000000000323",
      "columnName": "insert_user_name",
      "logicalType": "string",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000330",
      "configTableId": "2200000000000000323",
      "columnName": "update_user_name",
      "logicalType": "string",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000331",
      "configTableId": "2200000000000000323",
      "columnName": "insert_manager_real_name",
      "logicalType": "string",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000332",
      "configTableId": "2200000000000000323",
      "columnName": "update_manager_real_name",
      "logicalType": "string",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000333",
      "configTableId": "2200000000000000323",
      "columnName": "delete_no",
      "logicalType": "datetime",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000334",
      "configTableId": "2200000000000000323",
      "columnName": "account_id",
      "logicalType": "int64",
      "columnLength": "",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000335",
      "configTableId": "2200000000000000323",
      "columnName": "project_id",
      "logicalType": "int64",
      "columnLength": "",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000336",
      "configTableId": "2200000000000000323",
      "columnName": "device_code_hash",
      "logicalType": "string",
      "columnLength": "64",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000337",
      "configTableId": "2200000000000000323",
      "columnName": "user_code",
      "logicalType": "string",
      "columnLength": "16",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000338",
      "configTableId": "2200000000000000323",
      "columnName": "client_name",
      "logicalType": "string",
      "columnLength": "100",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000339",
      "configTableId": "2200000000000000323",
      "columnName": "status",
      "logicalType": "string",
      "columnLength": "20",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000340",
      "configTableId": "2200000000000000323",
      "columnName": "expires_at",
      "logicalType": "datetime",
      "columnLength": "255",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000341",
      "configTableId": "2200000000000000323",
      "columnName": "refresh_hash",
      "logicalType": "string",
      "columnLength": "64",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000343",
      "configTableId": "2200000000000000342",
      "columnName": "id",
      "logicalType": "int64",
      "columnLength": "",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000344",
      "configTableId": "2200000000000000342",
      "columnName": "insert_time",
      "logicalType": "datetime",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000345",
      "configTableId": "2200000000000000342",
      "columnName": "update_time",
      "logicalType": "datetime",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000346",
      "configTableId": "2200000000000000342",
      "columnName": "insert_manager_id",
      "logicalType": "int64",
      "columnLength": "",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000347",
      "configTableId": "2200000000000000342",
      "columnName": "update_manager_id",
      "logicalType": "int64",
      "columnLength": "",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000348",
      "configTableId": "2200000000000000342",
      "columnName": "insert_user_name",
      "logicalType": "string",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000349",
      "configTableId": "2200000000000000342",
      "columnName": "update_user_name",
      "logicalType": "string",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000350",
      "configTableId": "2200000000000000342",
      "columnName": "insert_manager_real_name",
      "logicalType": "string",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000351",
      "configTableId": "2200000000000000342",
      "columnName": "update_manager_real_name",
      "logicalType": "string",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000352",
      "configTableId": "2200000000000000342",
      "columnName": "delete_no",
      "logicalType": "datetime",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000353",
      "configTableId": "2200000000000000342",
      "columnName": "account_id",
      "logicalType": "int64",
      "columnLength": "",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000354",
      "configTableId": "2200000000000000342",
      "columnName": "project_id",
      "logicalType": "int64",
      "columnLength": "",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000355",
      "configTableId": "2200000000000000342",
      "columnName": "request_id",
      "logicalType": "string",
      "columnLength": "36",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000356",
      "configTableId": "2200000000000000342",
      "columnName": "capability",
      "logicalType": "string",
      "columnLength": "150",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000357",
      "configTableId": "2200000000000000342",
      "columnName": "request_hash",
      "logicalType": "string",
      "columnLength": "64",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000358",
      "configTableId": "2200000000000000342",
      "columnName": "status",
      "logicalType": "string",
      "columnLength": "20",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000359",
      "configTableId": "2200000000000000342",
      "columnName": "outcome",
      "logicalType": "string",
      "columnLength": "20",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000360",
      "configTableId": "2200000000000000342",
      "columnName": "result_id",
      "logicalType": "int64",
      "columnLength": "",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000361",
      "configTableId": "2200000000000000342",
      "columnName": "http_status",
      "logicalType": "int32",
      "columnLength": "",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000362",
      "configTableId": "2200000000000000342",
      "columnName": "business_code",
      "logicalType": "int32",
      "columnLength": "",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000000363",
      "configTableId": "2200000000000000342",
      "columnName": "expires_at",
      "logicalType": "datetime",
      "columnLength": "255",
      "isDbNotNull": 1,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000101001",
      "configTableId": "2200000000000000323",
      "columnName": "client_id",
      "logicalType": "string",
      "columnLength": "100",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000101002",
      "configTableId": "2200000000000000323",
      "columnName": "last_poll_at",
      "logicalType": "datetime",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000101003",
      "configTableId": "2200000000000000323",
      "columnName": "poll_interval_seconds",
      "logicalType": "int32",
      "columnLength": "",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000101004",
      "configTableId": "2200000000000000323",
      "columnName": "device_redeemed_at",
      "logicalType": "datetime",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    },
    {
      "id": "2200000000000101005",
      "configTableId": "2200000000000000323",
      "columnName": "refresh_expires_at",
      "logicalType": "datetime",
      "columnLength": "255",
      "isDbNotNull": 0,
      "isVirtualColumn": 0,
      "precision": 18,
      "scale": 2,
      "defaultValue": ""
    }
  ],
  "configPages": [],
  "configPageColumns": [],
  "yzAdminTypes": [],
  "configMenus": [],
  "configCustomPages": [],
  "configButtons": [],
  "configChildren": [],
  "configIndexes": [
    {
      "id": "2200000000000000738",
      "configTableId": "2200000000000000323",
      "indexName": "ux_grant_device",
      "isUnique": 1,
      "indexType": "UNIQUE",
      "isEnabled": 1
    },
    {
      "id": "2200000000000000740",
      "configTableId": "2200000000000000323",
      "indexName": "ux_grant_user",
      "isUnique": 1,
      "indexType": "UNIQUE",
      "isEnabled": 1
    },
    {
      "id": "2200000000000000742",
      "configTableId": "2200000000000000342",
      "indexName": "ux_task_request",
      "isUnique": 1,
      "indexType": "UNIQUE",
      "isEnabled": 1
    }
  ],
  "configIndexColumns": [
    {
      "id": "2200000000000000739",
      "configIndexId": "2200000000000000738",
      "configColumnId": "2200000000000000336",
      "columnName": "device_code_hash",
      "sortNo": 0,
      "sortDirection": "ASC"
    },
    {
      "id": "2200000000000000741",
      "configIndexId": "2200000000000000740",
      "configColumnId": "2200000000000000337",
      "columnName": "user_code",
      "sortNo": 0,
      "sortDirection": "ASC"
    },
    {
      "id": "2200000000000000743",
      "configIndexId": "2200000000000000742",
      "configColumnId": "2200000000000000353",
      "columnName": "account_id",
      "sortNo": 0,
      "sortDirection": "ASC"
    },
    {
      "id": "2200000000000000744",
      "configIndexId": "2200000000000000742",
      "configColumnId": "2200000000000000354",
      "columnName": "project_id",
      "sortNo": 10,
      "sortDirection": "ASC"
    },
    {
      "id": "2200000000000000745",
      "configIndexId": "2200000000000000742",
      "configColumnId": "2200000000000000355",
      "columnName": "request_id",
      "sortNo": 20,
      "sortDirection": "ASC"
    }
  ]
}
""";
}
