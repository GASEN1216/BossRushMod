#!/usr/bin/env python3
"""BossMapBeaconGuard — 烽烟圈（2026-11-24 玩家许愿第 8 项「Boss 地图标记」）的接线不变式。

1. 规则数字只在 BossMapBeaconRules：35 / 20 / 10 米三档、偏移上限 15 米、每 60 秒安静收一档、40 米显形、
   同屏 8 个、8 秒重落、显形放大 1.3、光柱 1.5 秒；执行回归 BossMapBeaconRules 直接编译这份文件。
2. 数据只读现成登记表：竞技场 / Mode D 走 CopyMapBeaconBossesTo（掉落登记表 + 本波内容 Boss：冠军之影、
   蚀日花冠只进 CurrentBoss / CurrentWaveBosses，2026-11-24 F3 实测漏标），Mode E 走 ModeEAliveEnemies 并按
   Team.IsEnemy 过滤掉玩家自己阵营，Mode G 走 CollectTrackedBosses；不做场景查找。宿主注册一行接这四个来源。
3. 关时 O(1)：OnUpdate 先读三道门，都不在进行就清空（只在有残留时）并返回，之后才对登记表。
4. 生命周期：唯一 owner 是本模块；Health.OnHurt 只在有标记时订阅（私有布尔防重复），RemoveAt 清空时、
   ClearAll、换场景、模块销毁都会退订；OnDestroy 顺带销毁图标贴图。
5. 表现：烽烟段是不画中心图标的区域圈（圆心本就偏开）；显形段改点标记、换图标、放大，光柱按
   CanPlayPillar 全场一时一道。官方地图只在 Setup 时读半径 / 图标，所以换档与显形都重新 Setup。
"""
import io
import os
import re
import sys

from cs_source_util import clean_source

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BEACON = ("Utilities", "BossMapBeacon")


def read(*parts):
    path = os.path.join(ROOT, *parts)
    if not os.path.isfile(path):
        return None
    with io.open(path, "r", encoding="utf-8-sig", errors="ignore") as fh:
        text = fh.read()
    return clean_source(text) if path.endswith(".cs") else text


def compact(text):
    return re.sub(r"\s+", "", text or "")


def body_of(source, signature):
    if source is None:
        return None
    at = source.find(signature)
    if at < 0 or source.find(signature, at + 1) >= 0:
        return None
    brace = source.find("{", at)
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:i]
    return None


def ordered(text, tokens):
    at = 0
    for token in tokens:
        found = text.find(token, at)
        if found < 0:
            return False
        at = found + len(token)
    return True


def main():
    errors = []

    def require(ok, message):
        if not ok:
            errors.append(message)

    rules_src = read(*(BEACON + ("BossMapBeaconRules.cs",)))
    module_src = read(*(BEACON + ("BossMapBeaconRuntimeModule.cs",)))
    fx_src = read(*(BEACON + ("BossMapBeaconFx.cs",)))
    if rules_src is None or module_src is None or fx_src is None:
        print("FAIL BossMapBeaconGuard\n  [File] Utilities/BossMapBeacon 下的三个文件缺失")
        return 1

    rules = compact(rules_src)
    for token, message in (
            ("internalconstintMaxBeacons=8;", "同屏最多 8 个"),
            ("internalconstfloatRecenterSeconds=8f;", "每 8 秒重落圆心"),
            ("internalconstfloatQuietStageSeconds=60f;", "安静 60 秒收一档"),
            ("internalconstfloatRevealDistance=40f;", "40 米内显形"),
            ("internalconstfloatRevealedScale=1.3f;", "显形放大 1.3"),
            ("internalconstfloatPillarSeconds=1.5f;", "光柱 1.5 秒"),
            ("internalconstfloatMaxOffsetMeters=15f;", "偏移上限 15 米"),
            ("privatestaticreadonlyfloat[]StageRadii={35f,20f,10f};", "三档半径 35 / 20 / 10")):
        require(token in rules, "[规则] " + message)
    require("returnplayerDistance<=RevealDistance||(stage>=LastStage&&quietSeconds>=QuietStageSeconds);"
            in compact(body_of(rules_src, "internal static bool ShouldReveal(int stage, float quietSeconds, float playerDistance)")),
            "[规则] 显形：40 米内，或最后一档又安静 60 秒")
    require("usingUnityEngine;" not in rules, "[规则] 规则文件不依赖 Unity（执行回归直接编译它）")

    module = compact(module_src)
    for forbidden in ("FindObjectsOfType", "FindObjectOfType", "GameObject.Find(", "FindWithTag", "GetComponentsInChildren"):
        require(forbidden not in module, "[来源] 烽烟圈不做场景查找: " + forbidden)
    update = compact(body_of(module_src, "public override void OnUpdate(float deltaTime, float unscaledDeltaTime)"))
    require(ordered(update, ["boolarena=Read(arenaOrModeDActive);", "boolmodeE=Read(modeEActive);", "ModeGRunStatemodeG=ActiveModeG();",
                             "if(!arena&&!modeE&&modeG==null){if(beacons.Count>0||hurtSubscribed)ClearAll();return;}",
                             "Scan(main,arena,modeE,modeG);", "Tick(main);"]),
            "[O(1)] OnUpdate 先过三道模式门，都不在就清空返回，之后才对登记表")
    scan = compact(body_of(module_src, "private void Scan(CharacterMainControl main, bool arena, bool modeE, ModeGRunState modeG)"))
    require(ordered(scan, ["if(arena&&copyArenaBosses!=null)copyArenaBosses(candidates);", "modeEAliveEnemies();",
                           "if(modeG!=null)modeG.CollectTrackedBosses(candidates);"]),
            "[来源] 竞技场 / Mode D、Mode E、Mode G 三处登记表")
    require("Team.IsEnemy(main.Team,boss.Team)" in compact(body_of(module_src, "private static bool IsLiveHostile(CharacterMainControl boss, CharacterMainControl main)")),
            "[来源] 只标与主角敌对的（Mode E 玩家自己阵营不标）")
    require("while(beacons.Count<BossMapBeaconRules.MaxBeacons)" in scan, "[规则] 空位按上限补")

    host = compact(read("ModBehaviourRuntimeModules.cs"))
    require("runtimeModuleHost.Register(newBossMapBeaconRuntimeModule(()=>IsActive||modeDRuntime.IsActive,"
            "wavesArenaRuntime.CopyMapBeaconBossesTo,()=>modeERuntime.IsModeEActive,()=>modeERuntime.ModeEAliveEnemies));" in host,
            "[接线] 宿主注册烽烟圈模块并接竞技场 / Mode D / Mode E 来源")
    access = compact(body_of(read("WavesArena", "WavesArenaRuntimeModule_BossAccess.cs"),
                             "internal void CopyMapBeaconBossesTo(ICollection<CharacterMainControl> destination)"))
    require(ordered(access, ["CopyTrackedBossCharactersTo(destination);", "AddMapBeaconBoss(destination,CurrentBossasCharacterMainControl);",
                             "AddMapBeaconBoss(destination,CurrentWaveBosses[i]asCharacterMainControl);"]),
            "[来源] 竞技场来源 = 掉落登记表 + 本波内容 Boss（冠军之影、蚀日花冠不在掉落表里）")

    subscribe = compact(body_of(module_src, "private void SubscribeHurt()"))
    unsubscribe = compact(body_of(module_src, "private void UnsubscribeHurt()"))
    require(ordered(subscribe, ["if(hurtSubscribed)return;", "Health.OnHurt+=OnAnyHealthHurt;", "hurtSubscribed=true;"]),
            "[订阅] Health.OnHurt 幂等订阅（私有布尔）")
    require(ordered(unsubscribe, ["if(!hurtSubscribed)return;", "Health.OnHurt-=OnAnyHealthHurt;", "hurtSubscribed=false;"]),
            "[订阅] 命名方法退订")
    require("if(beacons.Count==0)UnsubscribeHurt();" in compact(body_of(module_src, "private void RemoveAt(int index)")),
            "[订阅] 最后一个标记收掉时退订")
    require("UnsubscribeHurt();" in compact(body_of(module_src, "private void ClearAll()")), "[订阅] ClearAll 退订")
    require("SubscribeHurt();" in compact(body_of(module_src, "private bool Create(CharacterMainControl boss)")),
            "[订阅] 只在建出标记时订阅")
    require(ordered(compact(body_of(module_src, "public override void OnDestroy()")), ["ClearAll();", "BossMapBeaconFx.ResetStaticCaches();"]),
            "[清理] OnDestroy 清空标记并销毁图标贴图")
    require("ClearAll();" in compact(body_of(module_src, "public override void OnSceneLoaded(SceneRuntimeContext context)")),
            "[清理] 换场景清空")

    area = compact(body_of(module_src, "private static void ApplyArea(Beacon beacon)"))
    require(ordered(area, ["poi.IsArea=true;", "poi.AreaRadius=BossMapBeaconRules.RadiusOf(beacon.Stage);", "poi.HideIcon=true;",
                           "poi.Setup(null,beacon.LabelKey,false,beacon.SceneId);"]),
            "[表现] 烽烟段：区域圈、半径随档、不画中心图标、重新 Setup")
    reveal = compact(body_of(module_src, "private void Reveal(Beacon beacon, Vector3 bossPosition, float now)"))
    require(ordered(reveal, ["poi.ScaleFactor=BossMapBeaconRules.RevealedScale;", "poi.IsArea=false;", "poi.HideIcon=false;",
                             "poi.Setup(BossMapBeaconFx.GetIcon(),beacon.LabelKey,false,beacon.SceneId);",
                             "if(BossMapBeaconRules.CanPlayPillar(now,pillarBusyUntil))",
                             "pillarBusyUntil=now+BossMapBeaconRules.PillarSeconds;", "BossMapBeaconFx.PlayPillar(bossPosition);"]),
            "[表现] 显形：点标记、烽火图标、放大，光柱全场一时一道")
    require("beacon.SceneId=MapPointSceneResolver.Resolve();" in module, "[表现] 场景参数走 MapPointSceneResolver")

    fx = compact(fx_src)
    require("newMaterial(" not in fx and "Shader.Find(" not in fx, "[表现] 光柱只用共享 BossRushFxKit，不新建材质 / 着色器")
    require("BossRushInkSkin.PlaySfx(BossRushInkSkin.SfxWarDrum);" in fx, "[表现] 显形一声战鼓")

    bat = read("compile_official.bat") or ""
    for name in ("BossMapBeaconRules.cs", "BossMapBeaconFx.cs", "BossMapBeaconRuntimeModule.cs"):
        require("Utilities\\BossMapBeacon\\" + name in bat, "[编译] compile_official.bat 缺少 " + name)
    runner = read("tools", "run_runtime_regressions.py") or ""
    fixture = read("tests", "fixtures", "BossMapBeaconRules", "run.py") or ""
    require('"BossMapBeaconRules"' in runner and 'Utilities/BossMapBeacon/BossMapBeaconRules.cs' in fixture,
            "[回归] 执行回归 BossMapBeaconRules 登记并编入生产规则文件")

    if errors:
        print("FAIL BossMapBeaconGuard")
        for line in errors:
            print("  " + line)
        return 1
    print("PASS BossMapBeaconGuard")
    return 0


if __name__ == "__main__":
    sys.exit(main())
