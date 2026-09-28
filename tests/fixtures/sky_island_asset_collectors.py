"""共享剧情门面夹具的真实资产采集依赖；不生成永远成功的替身。"""
import hashlib
import json
from pathlib import Path
import re


def _member(source, signature):
    """按唯一签名和成员缩进保留原文，内层块不会提前结束成员。"""
    matches = list(re.finditer(r"^([ \t]*)" + re.escape(signature), source, re.M))
    if len(matches) != 1:
        raise AssertionError("资产采集成员锚点必须唯一：" + signature)
    match = matches[0]
    opening = source.index("{", match.end())
    closing = re.search(r"^" + re.escape(match.group(1)) + r"\}", source[opening:], re.M)
    if closing is None:
        raise AssertionError("资产采集成员未闭合：" + signature)
    return source[match.start():opening + closing.end()]


def generate_asset_collectors(root: Path, output: Path):
    """三组夹具各自生成到本次输出目录；只有宿主对象和 SavesSystem 是内存替身。"""
    item_path = root / "Utilities/OfficialQuests/OfficialQuestItems.cs"
    buffer_path = root / "鸭科夫源码/TeamSoda.Duckov.Core/PlayerStorageBuffer.cs"
    items = item_path.read_text(encoding="utf-8-sig")
    buffer = buffer_path.read_text(encoding="utf-8-sig")
    members = [_member(items, signature) for signature in (
        "internal static bool CanCollectAssets(bool inboxOnly)",
        "internal static Func<bool> AssetCollector(bool inboxOnly)")]
    source = ("using System; using System.Collections.Generic; using Saves; using ItemStatsSystem.Data;\n"
              "internal sealed partial class PlayerStorageBuffer {\n"
              + _member(buffer, "public static void SaveBuffer()") + "\n}\n"
              "namespace BossRush { internal static class OfficialQuestItems {\n"
              + "\n".join(members) + "\n} }\n")
    output.mkdir(parents=True, exist_ok=True)
    (output / "AssetCollectors.cs").write_text(source, encoding="utf-8")
    hashes = {str(path.relative_to(root)).replace("\\", "/"): hashlib.sha256(path.read_bytes()).hexdigest()
              for path in (item_path, buffer_path)}
    (output / "asset-collector-source-sha256.json").write_text(json.dumps(hashes, indent=2) + "\n", encoding="utf-8")
