#!/usr/bin/env python3
"""
ModeHCanonicalDigestGuard — Mode H 规范摘要守卫（设计提案 §20.2、§26.1）。

不变式：
- signatureAlgorithmVersion = 1，SHA-256，64 字符小写十六进制；
- gameBuildSignature / modBuildSignature 读当前实际加载程序集的原始文件字节，
  不使用版本号、时间戳或路径文本替代；路径为空/文件缺失/读取失败一律 fail-closed；
- contentSignature 排除根 contentSignature 属性后计算；
- contentCatalogSignature 按相对路径 ordinal 排序；
- payloadDigest 排除自身字段后计算；
- 规范 JSON：对象属性名 ordinal 升序、普通数组保序、集合语义字段排序去重、
  指定对象数组按指定键排序、invariant 数字、-0 规范为 0、拒绝 NaN/Infinity；
- 禁止直接哈希来源 JSON 文本或 Dictionary 默认输出（必须先解析成 token 再写出）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_modeh_group  # noqa: E402
DIGEST = os.path.join(REPO_ROOT, "ModeH", "ModeHCanonicalDigest.cs")
CONFIG = os.path.join(REPO_ROOT, "ModeH", "ModeHConfig.cs")

# §20.2 冻结的集合语义字段（写入前 ordinal 排序并去重）
SET_SEMANTIC_FIELDS = [
    "unlockedKitIds",
    "appliedEventTokenIds",
    "scarIds",
    "enteredProfileIds",
    "starterKitIds",
    "relayKitIds",
    "passedStableKeys",
    "commonVerifiedCommandIds",
    "failureReasonIds",
]

# §20.2 冻结的“按指定键稳定排序”的对象数组
SORTED_OBJECT_ARRAYS = [
    ("profiles", "profileId"),
    ("seasonRewardOperations", "operationId"),
    ("matchReports", "matchIndex"),
    ("behaviorStatuses", "entryId"),
    ("records", "stableKey"),
    ("commandStatuses", "commandId"),
]


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.relpath(path, REPO_ROOT))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    errors = []
    digest = read_modeh_group("ModeHCanonicalDigest.cs", "ModeHJsonValue.cs")
    if digest is None:
        errors.append("[File] 缺少 ModeH 规范摘要文件组")
        digest = ""
    config = read(CONFIG, errors)

    if digest:
        checks = [
            ("AlgorithmVersion",
             r"public const int SignatureAlgorithmVersion = 1;",
             "signatureAlgorithmVersion 冻结为 1"),
            ("DigestHexLength",
             r"public const int DigestHexLength = 64;",
             "摘要文本固定 64 字符"),
            ("Sha256Provider",
             r"SHA256\.Create\(\)",
             "使用 SHA-256"),
            ("LowercaseHex",
             r'ToString\("x2", CultureInfo\.InvariantCulture\)',
             "十六进制小写且 invariant"),
            ("Utf8NoBom",
             r"new UTF8Encoding\(false\)",
             "规范 JSON 使用 UTF-8 无 BOM"),
            ("GameAssemblyByName",
             r'private const string GameAssemblyName = "Assembly-CSharp";',
             "gameBuildSignature 来源为 Assembly-CSharp"),
            ("ModAssemblySelf",
             r"typeof\(ModeHCanonicalDigest\)\.Assembly",
             "modBuildSignature 来源为当前 Mod 程序集"),
            ("AssemblyRawBytes",
             r"File\.ReadAllBytes\(location\)",
             "读程序集原始字节而不是版本号/时间戳"),
            ("AssemblyLocationEmptyFailClosed",
             r'error = "assembly_location_empty";\s*\n\s*return false;',
             "路径为空 fail-closed"),
            ("AssemblyMissingFailClosed",
             r'error = "assembly_file_missing";\s*\n\s*return false;',
             "文件缺失 fail-closed"),
            ("ContentSignatureExcludesSelf",
             r'root\.RemoveProperty\("contentSignature"\)',
             "contentSignature 排除根自身属性"),
            ("ContentSignatureMismatchFailClosed",
             r'error = "content_signature_mismatch";',
             "contentSignature 不匹配 fail-closed"),
            ("CatalogOrdinalSort",
             r"entries\.Sort\([\s\S]{0,200}?string\.CompareOrdinal\(a\.Key, b\.Key\)",
             "contentCatalogSignature 按相对路径 ordinal 排序"),
            ("ObjectPropertyOrdinalSort",
             r"properties\.Sort\([\s\S]{0,320}?string\.CompareOrdinal\(na, nb\)",
             "对象属性名 ordinal 升序"),
            ("DuplicatePropertyRejected",
             r'error = "canonical_duplicate_property:"',
             "重复属性名 fail-closed"),
            ("NegativeZeroNormalized",
             r'string\.Equals\(text, "-0", StringComparison\.Ordinal\)',
             "-0 规范为 0"),
            ("NonFiniteRejected",
             r'error = "canonical_non_finite_number";',
             "拒绝 NaN/Infinity"),
            ("InvariantFloat",
             r'd\.ToString\("R", CultureInfo\.InvariantCulture\)',
             "浮点 round-trip + invariant"),
            ("InvariantInteger",
             r"IntegerValue\.ToString\(CultureInfo\.InvariantCulture\)",
             "整数 invariant"),
            ("DictionaryRejected",
             r'error = "canonical_dictionary_not_supported";',
             "禁止 Dictionary 默认输出参与摘要"),
            ("ParseBeforeHash",
             r"public static bool TryParse\(string json, out ModeHJsonValue root, out string error\)",
             "先解析成结构化 token 再写出"),
            ("DepthGuard",
             r"private const int MaxDepth = 32;",
             "递归深度上限存在"),
            ("NoThrowParse",
             r'error = "json_parse_exception:"',
             "解析异常转 error id，不抛出"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, digest):
                errors.append("[{}] 不满足: {}".format(name, desc))

        for field in SET_SEMANTIC_FIELDS:
            if not re.search(r'"{}"'.format(re.escape(field)), digest):
                errors.append("[SetSemantic] 集合语义字段未登记: " + field)

        for field, key in SORTED_OBJECT_ARRAYS:
            pattern = r'\{{\s*"{}",\s*"{}"\s*\}}'.format(re.escape(field), re.escape(key))
            if not re.search(pattern, digest):
                errors.append("[SortedArray] 对象数组排序键未登记: {} -> {}".format(field, key))

        # 禁止把来源 JSON 文本直接哈希
        if re.search(r"TryComputeSha256OfText\(\s*rawJson", digest):
            errors.append("[RawJsonHash] 禁止直接对来源 JSON 文本做哈希")

    if config:
        if not re.search(r"public const int CurrentSignatureAlgorithmVersion = 1;", config):
            errors.append("[ConfigAlgorithmVersion] ModeHConfig 未冻结 signatureAlgorithmVersion = 1")

    if errors:
        print("ModeHCanonicalDigestGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHCanonicalDigestGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
