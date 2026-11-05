"""验证显式维护前置的精确写入边界与失败后停止。"""
import unittest
from unittest.mock import Mock, patch

import paired_maintenance_fixture as maintenance
import paired_native_bootstrap as native


RUN = "1234567890abcdef" * 2


def response(side, kind, affected):
    join = maintenance.JOINS[kind]
    return {"code": 200, "data": {"configTableId": join.table_id, "tableName": join.table,
            "joinColumnCount": 1, "affectedRows": affected, "details": [{
                "columnName": join.cache, "columnFullName": join.path if side == "java" else join.foreign + ".name",
                "relationColumnName": join.foreign, "targetTableName": join.target,
                "targetColumnName": "name", "affectedRows": affected}]}}


class MaintenanceFixtureTests(unittest.TestCase):
    def test_realistic_catalog_rejects_scope_or_mapping_changes(self):
        tables = [
            {"id": table_id, "tableName": name, "isRongyu": 1, "scopeKind": "global"}
            for table_id, name in (
                ("810000000000000011", "sample_customer"),
                ("810000000000000012", "sample_contract"),
                ("810000000000000013", "sample_contract_line"),
            )
        ]
        project = str(maintenance.identity.PROJECT)
        csharp = {"yzProjectId": project, "configTables": tables, "configColumns": [
            {"configTableId": table["id"], "columnName": column, "logicalType": "string"}
            for table in tables for column in maintenance.fixture.COLUMNS[table["tableName"]]
        ]}
        java = {"yzProjectId": project, "configTables": tables, "configColumns": [
            {"configTableId": table["id"], "columnName": column, "isJoinColumn": 0, "isVirtualColumn": 0}
            for table in tables for column in maintenance.fixture.COLUMNS[table["tableName"]]
            if column != "id" and column not in maintenance.fixture.AUDIT_COLUMNS
            and column not in {join.cache for join in maintenance.JOINS.values()}
        ]}
        columns = {column["columnName"]: column for column in csharp["configColumns"]}
        for join in maintenance.JOINS.values():
            columns[join.foreign].update(linkConfigTableId=join.target_id, columnLength="50")
            columns[join.cache].update(id=str(join.column_id), columnFullName=join.path, columnLength="50", isJoinColumn=1)
        maintenance.validate_catalogs(java, csharp)
        cache = columns["sample_customer_name"]
        cache["columnFullName"] = "sample_contract.name"
        with self.assertRaisesRegex(ValueError, "csharp-cache"):
            maintenance.validate_catalogs(java, csharp)
        cache["columnFullName"] = maintenance.JOINS["contract"].path
        tables[0]["scopeKind"] = "project"
        with self.assertRaisesRegex(ValueError, "csharp-scope"):
            maintenance.validate_catalogs(java, csharp)

    def test_native_scope_rejects_old_images_and_timing_budgets(self):
        r = native.resources
        value = dict(version=1, purpose=r.MAINTENANCE_PREFLIGHT_NATIVE,
                     authorizationStatus="approved", leaseStatus="not-started", runId=RUN,
                     images=r.MAINTENANCE_PREFLIGHT_IMAGES, ports={"java": 28367, "csharp": 28368},
                     materialsSha256="a"*64, bootstrapSha256="b"*64, identitySha256="c"*64,
                     databases=[maintenance.fixture.database_name(RUN, side) for side in ("java", "csharp")],
                     user="mtf_"+RUN[:16], instancePin=native.PIN,
                     maintenancePreflightRequests=8, maintenanceCatalogSha256=r.MAINTENANCE_CATALOG_SHA256,
                     relationIndexProfile=r.RELATION_INDEX_PROFILE)
        self.assertEqual(r.MAINTENANCE_PREFLIGHT_RESOURCES, native.validate(value)["purpose"])
        for changes in ({"maintenancePreflightRequests": 7}, {"images": r.UNICODE_OBSERVED_IMAGES},
                        {"readReplayRequests": 1}, {"maintenanceCatalogSha256": "a"*64},
                        {"authorizationStatus": "pending"}):
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                native.validate({**value, **changes})

    def test_exact_java_declarations_cannot_target_other_database(self):
        sql = maintenance.java_declarations_sql(RUN)
        self.assertEqual(1, sql.count("INSERT INTO "))
        self.assertIn("metarill_fresh_1234567890abcdef_paired_java.config_column", sql)
        self.assertNotIn("_paired_csharp", sql)
        self.assertIn("ROW_COUNT()=2", sql)
        for join in maintenance.JOINS.values():
            self.assertIn(str(join.column_id), sql)
            self.assertIn(join.path.encode().hex(), sql)
        with self.assertRaises(ValueError):
            maintenance.java_declarations_sql(RUN + "x")

    def test_damage_and_exact_physical_readback_cover_only_active_ids(self):
        for side in ("java", "csharp"):
            for kind, join in maintenance.JOINS.items():
                with self.subTest(side=side, kind=kind):
                    sql = maintenance.damage_sql(RUN, side, kind)
                    self.assertIn(f"_paired_{side}.{join.table}", sql)
                    self.assertIn("ROW_COUNT()=132", sql)
                    self.assertIn("delete_no IS NULL", sql)
                    records = maintenance.sample(kind)
                    self.assertEqual(132, len({row[0] for row in records}))
                    normal = "\n".join(f"{key}\t{name.encode().hex().upper()}" for key, name in records)
                    maintenance.verify_cache(RUN, side, kind, lambda _: normal, stale=False)
                    with self.assertRaisesRegex(ValueError, "stale-readback"):
                        maintenance.verify_cache(RUN, side, kind, lambda _: normal, stale=True)
        with self.assertRaises(ValueError):
            maintenance.damage_sql(RUN, "other", "line")

    def test_receipt_requires_exact_plan_and_csharp_change_count(self):
        for side in ("java", "csharp"):
            for kind in maintenance.JOINS:
                self.assertEqual(132, maintenance.verify_response(side, kind, response(side, kind, 132), repeated=False))
                self.assertEqual(0, maintenance.verify_response(side, kind, response(side, kind, 0), repeated=True))
                with self.assertRaisesRegex(ValueError, "affected-rows"):
                    maintenance.verify_response(side, kind, response(side, kind, 0), repeated=False)
        bad = response("java", "contract", 132)
        bad["data"]["details"][0]["relationColumnName"] = "wrong_id"
        with self.assertRaisesRegex(ValueError, "join-details"):
            maintenance.verify_response("java", "contract", bad, repeated=False)
        bad = response("csharp", "line", 132)
        bad["data"]["joinColumnCount"] = 0
        with self.assertRaisesRegex(ValueError, "published-plan"):
            maintenance.verify_response("csharp", "line", bad, repeated=False)

    def test_preflight_stops_before_second_write_when_first_repair_disagrees(self):
        execute = Mock(return_value="1\n1")
        request = Mock(return_value=response("java", "contract", 0))
        with (patch.object(maintenance, "validate_catalogs"),
              patch.object(maintenance.fixture, "verify_readback", return_value={}),
              patch.object(maintenance, "verify_cache")):
            with self.assertRaisesRegex(ValueError, "affected-rows"):
                maintenance.run_preflight(RUN, {}, {}, Mock(), execute, request)
        self.assertEqual(2, execute.call_count)
        request.assert_called_once_with("java", maintenance.PATH, {"configTableId": "810000000000000012"})

    def test_preflight_uses_each_original_endpoint_twice_and_rejects_seed_drift(self):
        request = Mock(side_effect=[response(side, kind, count)
                                   for side in ("java", "csharp") for kind in maintenance.JOINS for count in (132, 0)])
        with (patch.object(maintenance, "validate_catalogs"),
              patch.object(maintenance.fixture, "verify_readback", side_effect=[{}, {}, {"drift": True}, {}]),
              patch.object(maintenance, "verify_cache") as cache):
            with self.assertRaisesRegex(ValueError, "complete-seed-drift"):
                maintenance.run_preflight(RUN, {}, {}, Mock(), Mock(return_value="1\n1"), request)
        self.assertEqual(8, request.call_count)
        self.assertEqual(12, cache.call_count)
