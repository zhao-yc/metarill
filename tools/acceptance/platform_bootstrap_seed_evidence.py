#!/usr/bin/env python3
"""依据同轮冻结元数据独立复核平台空库用例的种子回执。"""

import argparse
import json
from pathlib import Path


# 与平台空库用例逐表查询的 11 类目录对应。
SEED_KEYS = (
    "configTables",
    "configColumns",
    "configPages",
    "configPageColumns",
    "yzAdminTypes",
    "configMenus",
    "configCustomPages",
    "configButtons",
    "configChildren",
    "configIndexes",
    "configIndexColumns",
)


def read_object(path: Path) -> dict:
    """读取固定输入，拒绝缺失或非对象的 JSON。"""
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError("元数据及用例回执必须为 JSON 对象。")
    return value


def verify_seed_receipt(metadata: Path, receipt: Path) -> dict[str, int]:
    """重算平台和示例的目录行数，并与已通过的 Production 用例对照。"""
    sources = [read_object(metadata / f"{name}.json") for name in ("platform", "demo")]
    counts = {}
    for key in SEED_KEYS:
        # 元数据契约允许省略空目录，显式给出时须为数组。
        entries = [source.get(key, []) for source in sources]
        if any(not isinstance(value, list) for value in entries):
            raise ValueError(f"目录 {key} 必须为数组。")
        counts[key] = sum(map(len, entries))

    proof = read_object(receipt)
    if proof.get("passed") is not True or proof.get("environment") != "Production":
        raise ValueError("缺少通过的 Production 空库用例回执。")
    for key, expected in (("seedCategories", len(counts)), ("seedRows", sum(counts.values()))):
        actual = proof.get(key)
        if type(actual) is not int or actual != expected:
            raise ValueError(f"种子回执 {key} 与冻结元数据不符，期望 {expected}。")
    return counts


def main() -> None:
    """仅校验本地输入并输出计数，不访问数据库或改变验收状态。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("metadata", type=Path, help="同轮冻结的 backend/metadata 目录")
    parser.add_argument("receipt", type=Path, help="同轮 Production 用例的 verified.json")
    args = parser.parse_args()
    try:
        counts = verify_seed_receipt(args.metadata, args.receipt)
    except (OSError, ValueError) as error:
        parser.exit(1, f"种子回执核对失败：{error}\n")
    print(json.dumps({
        "seedCategories": len(counts),
        "seedRows": sum(counts.values()),
        "seedCounts": counts,
    }, ensure_ascii=False))


if __name__ == "__main__":
    main()
