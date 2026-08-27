#!/usr/bin/env python3
"""
ModeHOddsGuard — Mode H 赔率权重守卫（设计提案 §17.5、§26.1）。

不变式：
- OddsWeights.json 有 schemaVersion、稳定 ID 与自洽的 contentSignature；
- x1-x5 五档边界与 §17.5 冻结阈值一致且互不重叠、无空隙；
- 三个公开分差测试向量存在且结论正确（23-4=19 -> x2；-21-26=-47 -> x5；13-14=-1 -> x3）；
- 玩家侧/敌方侧权重逐项齐全；
- 品质评分表覆盖 Q1-Q8；
- 通用口令标签映射覆盖八条口令；
- C# 侧存在同版本内置 fallback，且 ResolveOddsTier 阈值与 JSON 一致。
"""
import io
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_canonical_json import content_signature  # noqa: E402

ODDS_JSON = os.path.join(REPO_ROOT, "Assets", "Data", "ModeH", "OddsWeights.json")
STATE_MODEL = os.path.join(REPO_ROOT, "ModeH", "ModeHStateModel.cs")
CONFIG = os.path.join(REPO_ROOT, "ModeH", "ModeHConfig.cs")
CATALOG = os.path.join(REPO_ROOT, "ModeH", "ModeHContentCatalog.cs")

EXPECTED_TIERS = [
    (1, 20, 9999),
    (2, 5, 19),
    (3, -9, 4),
    (4, -24, -10),
    (5, -9999, -25),
]

PLAYER_WEIGHT_FIELDS = [
    "relayAvailable", "relayEmpty", "starterCounters", "starterCountered",
    "relayCounters", "relayCountered", "kitQualityByGameQuality", "kitQualityTotalCap",
    "equipmentTagCounters", "equipmentTagCountered", "starterInjured", "relayInjured",
    "anomalyBlood", "anomalyCrowd", "anomalyStrong", "anomalyError",
    "scarBenefit", "scarCost", "scarTotalMin", "scarTotalMax",
    "commandAligned", "commandConflicted", "signatureCommandStarter", "signatureCommandRelay",
    "arenaFavorable", "arenaUnfavorable",
]

ENEMY_WEIGHT_FIELDS = [
    "stageByMatchIndex", "countUpperBound", "highThreatCore", "synergyPerCategory",
    "synergyCap", "woundedEnemy", "anomalyBlood", "anomalyCrowd", "anomalyStrong", "anomalyError",
]

COMMON_COMMANDS = ["all_in", "center", "finish", "guard", "hold", "press", "spread", "steady"]

EXPECTED_VECTORS = [
    (23, 4, 2),
    (-21, 26, 5),
    (13, 14, 3),
]


def read_text(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.relpath(path, REPO_ROOT))
        return ""
    with io.open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def resolve_tier(edge):
    if edge >= 20:
        return 1
    if edge >= 5:
        return 2
    if edge >= -9:
        return 3
    if edge >= -24:
        return 4
    return 5


def main():
    errors = []

    if not os.path.exists(ODDS_JSON):
        print("ModeHOddsGuard: FAIL (1 errors)")
        print("  - 缺少 Assets/Data/ModeH/OddsWeights.json")
        return 1

    with io.open(ODDS_JSON, "r", encoding="utf-8") as fh:
        try:
            document = json.load(fh)
        except ValueError as exc:
            print("ModeHOddsGuard: FAIL (1 errors)")
            print("  - OddsWeights.json 解析失败: {}".format(exc))
            return 1

    if document.get("schemaVersion") != 1:
        errors.append("[Schema] schemaVersion 必须为 1")
    if not document.get("documentId"):
        errors.append("[Schema] 缺少稳定 documentId")
    declared = document.get("contentSignature", "")
    computed = content_signature(document)
    if declared != computed:
        errors.append("[Signature] contentSignature 不自洽: 声明={} 计算={}".format(
            declared or "(空)", computed))

    tiers = document.get("oddsTiers") or []
    if len(tiers) != 5:
        errors.append("[Tiers] 必须恰好五个赔率档位")
    else:
        for index, (odds, min_edge, max_edge) in enumerate(EXPECTED_TIERS):
            tier = tiers[index]
            if tier.get("odds") != odds:
                errors.append("[Tiers] 第 {} 档 odds 应为 {}".format(index + 1, odds))
            if tier.get("minPublicEdge") != min_edge:
                errors.append("[Tiers] x{} minPublicEdge 应为 {}".format(odds, min_edge))
            if tier.get("maxPublicEdge") != max_edge:
                errors.append("[Tiers] x{} maxPublicEdge 应为 {}".format(odds, max_edge))
        # 相邻档位必须无空隙、不重叠
        for index in range(len(EXPECTED_TIERS) - 1):
            upper = tiers[index]
            lower = tiers[index + 1]
            if lower.get("maxPublicEdge", 0) + 1 != upper.get("minPublicEdge", 0):
                errors.append("[Tiers] x{} 与 x{} 之间存在空隙或重叠".format(
                    upper.get("odds"), lower.get("odds")))

    player = document.get("playerWeights") or {}
    for field in PLAYER_WEIGHT_FIELDS:
        if field not in player:
            errors.append("[PlayerWeights] 缺少权重: " + field)
    quality_scores = player.get("kitQualityByGameQuality")
    if not isinstance(quality_scores, list) or len(quality_scores) != 8:
        errors.append("[PlayerWeights] kitQualityByGameQuality 必须覆盖 Q1-Q8 共 8 项")

    enemy = document.get("enemyWeights") or {}
    for field in ENEMY_WEIGHT_FIELDS:
        if field not in enemy:
            errors.append("[EnemyWeights] 缺少权重: " + field)
    stage = enemy.get("stageByMatchIndex")
    if not isinstance(stage, list) or len(stage) != 6:
        errors.append("[EnemyWeights] stageByMatchIndex 必须覆盖六场")

    matrix = document.get("archetypeMatrix") or []
    pairs = set()
    for entry in matrix:
        pairs.add((entry.get("attacker"), entry.get("defender")))
    required_pairs = [
        ("assault", "ranged"), ("ranged", "sustain"), ("sustain", "tank"),
        ("tank", "assault"), ("finisher", "sustain"), ("ranged", "finisher"),
    ]
    for pair in required_pairs:
        if pair not in pairs:
            errors.append("[Matrix] 缺少原型克制关系: {} > {}".format(pair[0], pair[1]))

    tag_map = document.get("commandTagMap") or []
    mapped = set(entry.get("commandId") for entry in tag_map)
    for command in COMMON_COMMANDS:
        if command not in mapped:
            errors.append("[TagMap] 缺少口令标签映射: " + command)

    vectors = document.get("testVectors") or []
    if len(vectors) < 3:
        errors.append("[Vectors] 必须至少三个公开分差测试向量")
    for vector in vectors:
        player_score = vector.get("playerPublicScore")
        enemy_score = vector.get("enemyPublicScore")
        expected = vector.get("expectedOdds")
        if player_score is None or enemy_score is None or expected is None:
            errors.append("[Vectors] 向量字段缺失: " + str(vector.get("vectorId")))
            continue
        actual = resolve_tier(player_score - enemy_score)
        if actual != expected:
            errors.append("[Vectors] {} 结论错误: {}-{}={} 应为 x{}，声明 x{}".format(
                vector.get("vectorId"), player_score, enemy_score,
                player_score - enemy_score, actual, expected))
    triples = set((v.get("playerPublicScore"), v.get("enemyPublicScore"), v.get("expectedOdds"))
                  for v in vectors)
    for triple in EXPECTED_VECTORS:
        if triple not in triples:
            errors.append("[Vectors] 缺少冻结向量: {}-{} -> x{}".format(*triple))

    model = read_text(STATE_MODEL, errors)
    if model and not re.search(r"public static int ResolveOddsTier\(int publicEdge\)", model):
        errors.append("[Code] 缺少 ResolveOddsTier 单点实现")

    config = read_text(CONFIG, errors)
    if config:
        thresholds = [
            (r"OddsThresholdX1MinEdge = 20;", "x1 阈值"),
            (r"OddsThresholdX2MinEdge = 5;", "x2 阈值"),
            (r"OddsThresholdX3MinEdge = -9;", "x3 阈值"),
            (r"OddsThresholdX4MinEdge = -24;", "x4 阈值"),
        ]
        for pattern, desc in thresholds:
            if not re.search(pattern, config):
                errors.append("[Code] 冻结阈值缺失: " + desc)

    catalog = read_text(CATALOG, errors)
    if catalog:
        if not re.search(r"private static void ApplyBuiltInOddsFallback\(\)", catalog):
            errors.append("[Code] 缺少赔率权重的同版本内置 fallback")
        if not re.search(r"_usedOddsFallback", catalog):
            errors.append("[Code] 未记录是否使用了 fallback")
        # 只有纯数值权重允许 fallback：preset/command/kit 审计表不得有 fallback
        for forbidden in ["ApplyBuiltInProfileFallback", "ApplyBuiltInKitFallback",
                          "ApplyBuiltInCommandFallback"]:
            if forbidden in catalog:
                errors.append("[Code] 审计数据不得有跨构建 fallback: " + forbidden)

    if errors:
        print("ModeHOddsGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHOddsGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
