"""离线严格编译Java采集器；只使用固定驱动JAR，不下载依赖或执行数据库。"""
from __future__ import annotations
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

DRIVER_SHA256 = "0353648eaa1c91e0f4020c959abf756bc866ffd583df22ae6b6f6e0cbd43eb44"

class PairedJdbcObservationTests(unittest.TestCase):
    def test_actual_compilation_and_offline_driver_bindings(self):
        """共同向量、原执行/异常/事务、预算与并发边界由Java实际运行验证。"""
        java, javac = shutil.which("java"), shutil.which("javac")
        driver = Path(os.environ.get("METARILL_JDBC_DRIVER_JAR", str(Path.home()/".m2/repository/com/mysql/mysql-connector-j/9.7.0/mysql-connector-j-9.7.0.jar")))
        if not java or not javac or not driver.is_file():
            self.skipTest("未提供JDK21及固定Connector/J；Java采集离线验收未执行")
        self.assertEqual(DRIVER_SHA256, hashlib.sha256(driver.read_bytes()).hexdigest(), "驱动JAR与固定9.7.0来源不符")
        source = Path(__file__).resolve().parent
        with tempfile.TemporaryDirectory(prefix="metarill-jdbc-observation-") as directory:
            command = [javac, "-encoding", "UTF-8", "-Xlint:all", "-Werror", "-cp", str(driver), "-d", directory]
            command += [str(source/name) for name in ("PairedJdbcObservation.java", "MySqlJdbcObservationMetadata.java", "PairedJdbcObservationSelfTest.java")]
            compiled = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=90)
            self.assertEqual(0, compiled.returncode, compiled.stdout+compiled.stderr)
            executed = subprocess.run([java, "-Dstdout.encoding=UTF-8", "-cp", directory+os.pathsep+str(driver), "com.yinzon.samplehost.PairedJdbcObservationSelfTest", str(source/"sql-value-proof-v1.tsv")], capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=60)
            self.assertEqual(0, executed.returncode, executed.stdout+executed.stderr)
            self.assertIn("通过：49项", executed.stdout)

if __name__ == "__main__":
    unittest.main()
