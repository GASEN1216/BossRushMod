#!/usr/bin/env python3
"""
modeh_canonical_json — Mode H 规范 JSON 的 Python 镜像（设计提案 §20.2）。

用途：
- 给 Assets/Data/ModeH/*.json 盖 contentSignature；
- 供 guard 复核已盖签名是否自洽。

与 C# 端 ModeH/ModeHCanonicalDigest.cs 必须逐规则一致：
- UTF-8 无 BOM、无空白；
- 对象属性名 ordinal（码位）升序；
- 普通数组保序；
- 集合语义字段排序去重；
- 指定对象数组按指定键排序；
- 整数 invariant 输出；
- 字符串按 JSON 标准转义（控制字符 \\uXXXX，非 ASCII 原样输出）。

注意：数据文件一律只使用整数，禁止浮点字面量。倍率一律以千分之一整数（Milli）表达，
这样两端不会因浮点 round-trip 格式差异产生不同摘要。
"""
import hashlib

# 与 C# SetSemanticFields 保持一致
SET_SEMANTIC_FIELDS = frozenset([
    "unlockedKitIds",
    "appliedEventTokenIds",
    "scarIds",
    "enteredProfileIds",
    "starterKitIds",
    "relayKitIds",
    "passedStableKeys",
    "commonVerifiedCommandIds",
    "failureReasonIds",
])

# 与 C# SortedObjectArrayFields 保持一致
SORTED_OBJECT_ARRAY_FIELDS = {
    "profiles": "profileId",
    "seasonRewardOperations": "operationId",
    "matchReports": "matchIndex",
    "behaviorStatuses": "entryId",
    "effectStatuses": "entryId",
    "records": "stableKey",
    "commandStatuses": "commandId",
}

_ESCAPES = {
    '"': '\\"',
    "\\": "\\\\",
    "\b": "\\b",
    "\f": "\\f",
    "\n": "\\n",
    "\r": "\\r",
    "\t": "\\t",
}


class CanonicalError(Exception):
    """规范化失败（fail-closed）。"""


def _write_string(value, out):
    out.append('"')
    for ch in value:
        esc = _ESCAPES.get(ch)
        if esc is not None:
            out.append(esc)
        elif ch < " ":
            out.append("\\u%04x" % ord(ch))
        else:
            out.append(ch)
    out.append('"')


def _write_value(value, owner_field, out):
    if value is None:
        out.append("null")
        return
    if isinstance(value, bool):
        out.append("true" if value else "false")
        return
    if isinstance(value, int):
        out.append(str(value))
        return
    if isinstance(value, float):
        raise CanonicalError(
            "Mode H 数据文件禁止浮点字面量（请改用千分之一整数）: %r" % value)
    if isinstance(value, str):
        _write_string(value, out)
        return
    if isinstance(value, (list, tuple)):
        _write_array(list(value), owner_field, out)
        return
    if isinstance(value, dict):
        _write_object(value, out)
        return
    raise CanonicalError("不支持的类型: %s" % type(value).__name__)


def _write_array(items, owner_field, out):
    if owner_field in SET_SEMANTIC_FIELDS:
        texts = []
        for item in items:
            if not isinstance(item, str):
                raise CanonicalError("集合语义字段元素必须是字符串: %s" % owner_field)
            texts.append(item)
        texts.sort()
        out.append("[")
        previous = None
        first = True
        for text in texts:
            if previous is not None and text == previous:
                continue
            if not first:
                out.append(",")
            _write_string(text, out)
            previous = text
            first = False
        out.append("]")
        return

    sort_key = SORTED_OBJECT_ARRAY_FIELDS.get(owner_field)
    if sort_key is not None:
        for item in items:
            if not isinstance(item, dict) or sort_key not in item:
                raise CanonicalError("对象数组缺少排序键: %s -> %s" % (owner_field, sort_key))
        items = sorted(items, key=lambda it: it[sort_key])

    out.append("[")
    for index, item in enumerate(items):
        if index:
            out.append(",")
        _write_value(item, None, out)
    out.append("]")


def _write_object(obj, out):
    names = sorted(obj.keys())
    out.append("{")
    for index, name in enumerate(names):
        if not isinstance(name, str) or not name:
            raise CanonicalError("属性名必须是非空字符串")
        if index:
            out.append(",")
        _write_string(name, out)
        out.append(":")
        _write_value(obj[name], name, out)
    out.append("}")


def canonical_dumps(value):
    """把 Python 结构写成规范 JSON 文本。"""
    out = []
    _write_value(value, None, out)
    return "".join(out)


def sha256_hex(text):
    """UTF-8（无 BOM）文本的 SHA-256 小写十六进制摘要。"""
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def content_signature(document):
    """计算数据文件的 contentSignature（排除根 contentSignature 属性）。"""
    if not isinstance(document, dict):
        raise CanonicalError("数据文件根必须是对象")
    stripped = dict(document)
    stripped.pop("contentSignature", None)
    return sha256_hex(canonical_dumps(stripped))


def content_catalog_signature(path_signature_pairs):
    """计算 contentCatalogSignature（按相对路径 ordinal 排序）。"""
    entries = []
    seen = set()
    for path, signature in path_signature_pairs:
        if not path:
            raise CanonicalError("目录条目缺少路径")
        if len(signature or "") != 64:
            raise CanonicalError("目录条目签名非法: %s" % path)
        if path in seen:
            raise CanonicalError("目录条目路径重复: %s" % path)
        seen.add(path)
        entries.append({"contentSignature": signature, "path": path})
    entries.sort(key=lambda e: e["path"])
    return sha256_hex(canonical_dumps(entries))
