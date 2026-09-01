#!/usr/bin/env python3
"""Guard: ModeH certification, shared catalogs, Campaign JSON and BGM owner leases."""

from pathlib import Path
import json
import sys


def text(path):
    return Path(path).read_text(encoding="utf-8", errors="ignore")


def main():
    errors = []
    modeh = text("ModeH/ModeHRuntimeModule_SceneFlow.cs")
    for token in ("TryUseCachedReport", "GetProductionStableKeys", "RequestCertificationCacheWrite",
                  "_lastCertificationUsedCache", "StartCertification"):
        if token not in modeh:
            errors.append("ModeH 认证计划缺失: " + token)
    if "ModeHPresetRegistry.ProductionKeys" in modeh:
        errors.append("ModeH 首次认证不得依赖尚未产生的 ProductionKeys")

    waves = text("WavesArena/WavesArena.cs")
    codex = text("Integration/Codex/CodexRuntimeModule.cs")
    petnest = text("PetNest/PetNestRuntimeModule.cs")
    bossfilter = text("BossFilter/BossFilter.cs")
    shared = "EnsureEnemyPresetsReadyForGameplayCatalogs"
    if waves.count("InitializeEnemyPresets();") < 1 or shared not in waves:
        errors.append("图鉴/遗种巢缺少共享幂等预设初始化")
    if shared not in codex or shared not in petnest:
        errors.append("图鉴和遗种巢未共用同一预设初始化入口")
    if "CodexRuntime.NotifyEnemyPresetsRefreshed" not in bossfilter or "PetNestRuntime.NotifyEnemyPresetsRefreshed" not in bossfilter:
        errors.append("过滤变更未同时失效图鉴与遗种巢缓存")

    campaign_source = text("Campaign/CampaignContentCatalog.cs")
    campaign_path = Path("Assets/Data/Campaign/Chapters.json")
    if not campaign_path.is_file():
        errors.append("缺少 Campaign Chapters.json")
    else:
        data = json.loads(campaign_path.read_text(encoding="utf-8-sig"))
        chapters = data.get("chapters", [])
        if data.get("version") != 1 or len(chapters) != 6:
            errors.append("Campaign JSON 必须是 version=1 且恰好六章")
        if [c.get("order") for c in chapters] != [1, 2, 3, 4, 5, 6]:
            errors.append("Campaign JSON 章节顺序必须为 1..6")
        tokens = [c.get("facilityToken") for c in chapters]
        clues = [c.get("clueId") for c in chapters]
        if len(set(tokens)) != 6 or len(set(clues)) != 6:
            errors.append("Campaign token/线索必须全表唯一")
    for token in ("Source", "ContentSignature", "ExpectedContentSignature", '"Json"', '"Fallback"',
                  "TryLoadFromJson", "ComputeContentSignature", "BuildHardcodedChapters",
                  "ModeHCanonicalDigest.TryParse", 'TryGetArray("chapters"'):
        if token not in campaign_source:
            errors.append("Campaign JSON 来源/签名/整表回退缺失: " + token)
    if "JsonUtility.FromJson" in campaign_source:
        errors.append("Campaign 多层数据表不得退回实机已证实会静默丢 chapters 的 JsonUtility")
    compile_text = text("compile_official.bat")
    if 'xcopy /Y /I "Assets\\Data\\Campaign\\*.json"' not in compile_text:
        errors.append("Campaign JSON 未加入正式构建部署")

    bgm = text("Audio/BossBgmCoordinator.cs")
    for token in ("AcquireBossBgm(string bossKey, UnityEngine.Object owner)",
                  "ReleaseBossBgm(string bossKey, UnityEngine.Object owner)",
                  "ActiveOwnerLeaseCount", "PlayingBossKey", "EnsureOwnerLeasesForCurrentScene",
                  "GetMostRecentLiveLease", "NotifySceneChanged", "_ownerLeases.Clear()"):
        if token not in bgm:
            errors.append("BGM owner 租约不变式缺失: " + token)

    if errors:
        for error in errors:
            print("  - " + error)
        print("GameplayDataAndAudioReliabilityGuard: FAIL")
        return 1
    print("GameplayDataAndAudioReliabilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
