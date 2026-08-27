#!/usr/bin/env python3
"""
modeh_stamp_data — 给 Assets/Data/ModeH/*.json 盖 contentSignature 并打印 contentCatalogSignature。

用法：
    python tools/modeh_stamp_data.py            # 盖章并写回
    python tools/modeh_stamp_data.py --check    # 只核对，不写回（CI/guard 用法）

规则见设计提案 §20.2：contentSignature = 移除根 contentSignature 属性后的规范 JSON SHA-256。
规范化实现与 C# 端 ModeH/ModeHCanonicalDigest.cs 保持一致，镜像在 tests/modeh_canonical_json.py。

Mode H 数据文件一律只使用整数字面量（倍率用千分之一整数），避免两端浮点格式差异。
"""
import argparse
import io
import json
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_canonical_json import (  # noqa: E402
    CanonicalError,
    content_catalog_signature,
    content_signature,
)

DATA_DIR = os.path.join(REPO_ROOT, "Assets", "Data", "ModeH")
REQUIRED_FILES = [
    "BossProfiles.json",
    "CommandCompatibility.json",
    "Commands.json",
    "LoadoutKits.json",
    "OddsWeights.json",
    "Scars.json",
    "ThreatPlans.json",
]


def load(path):
    with io.open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


def dump(path, document):
    text = json.dumps(document, ensure_ascii=False, indent=2, sort_keys=False)
    with io.open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text + "\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="只核对不写回")
    args = parser.parse_args()

    failures = []
    pairs = []
    for name in REQUIRED_FILES:
        path = os.path.join(DATA_DIR, name)
        if not os.path.exists(path):
            failures.append("缺少数据文件: " + name)
            continue
        try:
            document = load(path)
        except ValueError as exc:
            failures.append("{} JSON 解析失败: {}".format(name, exc))
            continue
        try:
            signature = content_signature(document)
        except CanonicalError as exc:
            failures.append("{} 规范化失败: {}".format(name, exc))
            continue

        declared = document.get("contentSignature", "")
        if declared != signature:
            if args.check:
                failures.append("{} contentSignature 不匹配: 声明={} 计算={}".format(
                    name, declared or "(空)", signature))
            else:
                document["contentSignature"] = signature
                dump(path, document)
                print("stamped {} -> {}".format(name, signature))
        else:
            print("ok      {} -> {}".format(name, signature))
        pairs.append((name, signature))

    if failures:
        print("modeh_stamp_data: FAIL ({} errors)".format(len(failures)))
        for f in failures:
            print("  - " + f)
        return 1

    catalog = content_catalog_signature(pairs)
    print("contentCatalogSignature = " + catalog)
    return 0


if __name__ == "__main__":
    sys.exit(main())
