"""在冻结的 Java 样板 JAR 中仅替换已编译的子页导入类。"""
import argparse
from copy import copy
from hashlib import sha256
import json
from pathlib import Path
from zipfile import ZIP_STORED, ZipFile

ORIGINAL = "41b3054841338c91844c21ed1149c9a781603217d8b45e777862955b87ba24da"
ENTRY = "BOOT-INF/classes/com/yinzon/generated/page/SampleContractLine81003ImportSupport.class"


def pack(original, replacement, target):
    """保持所有其他条目内容和嵌套依赖压缩方式，避免改变参考包的运行栈。"""
    if sha256(original.read_bytes()).hexdigest() != ORIGINAL:
        raise ValueError("原 Java JAR 摘要不匹配")
    new_class = replacement.read_bytes()
    if not new_class.startswith(b"\xca\xfe\xba\xbe") or b"getParentId" not in new_class:
        raise ValueError("替换类未包含父 ID 读取")

    with ZipFile(original) as source:
        names = source.namelist()
        nested = [entry for entry in source.infolist() if entry.filename.startswith("BOOT-INF/lib/") and entry.filename.endswith(".jar")]
        if len(set(names)) != len(names) or ENTRY not in names or len(nested) != 154 or any(
            entry.compress_type != ZIP_STORED for entry in nested
        ):
            raise ValueError("原 JAR 结构不匹配")
        with ZipFile(target, "x") as output:
            output.comment = source.comment
            for entry in source.infolist():
                data = new_class if entry.filename == ENTRY else source.read(entry)
                output.writestr(copy(entry), data)

        with ZipFile(target) as output:
            changed = [name for name in names if source.read(name) != output.read(name)]
            if output.namelist() != names or changed != [ENTRY] or any(
                output.getinfo(name).compress_type != source.getinfo(name).compress_type for name in names
            ):
                raise ValueError("参考包只能替换子页导入类")

    return {"originalSha256": ORIGINAL, "referenceSha256": sha256(target.read_bytes()).hexdigest(),
            "replacementSha256": sha256(new_class).hexdigest(), "changedEntries": changed, "nestedDependencies": len(nested)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for argument in ("original", "replacement", "target"):
        parser.add_argument(argument, type=Path)
    args = parser.parse_args()
    print(json.dumps(pack(args.original, args.replacement, args.target)))
