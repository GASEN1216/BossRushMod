#!/usr/bin/env python3
"""SilenceBellWiringGuard — 止语钟罩（500220，2026-11-24 玩家许愿第 10 项「自充能护盾」）的接线不变式。

1. 数值只在 SilenceBellConfig：80 点护罩、挨打 5 秒后回、打碎后至少 9 秒、每秒 20；钟守 30 枚贝壳。
   护罩状态机 SilenceBellRules 由执行回归 SilenceBellRules 直接编译。
2. 伤害入口：ZombieModeDamageRuntime.ReduceFinalDamage 里先残日盾片、再钟罩，都在丧尸模式门之前（对全模式生效）。
   未佩戴时首行返回；只吃敌方直接与爆炸伤害，持续伤害 / 装备效果不吃；只护主角，基地不生效。
3. 生命周期（AGENTS 4.6 / 4.12）：唯一 owner 是 SilenceBellRuntime；驱动只在实际佩戴时存在。
   卸下只撤驱动、护罩数值留着（不能靠穿脱补满）；换场景、死亡、宿主销毁才满罩。NewWeaponRuntime 统一订阅 / 退订。
4. 来路：钟守的第二只匣子 SilenceBellShopInteractable，人在场时挂在永久 NPC 上，不在时挂在岛上装置上，
   与节拍器共用同一条贝壳事务 ReloadMetronomeTrade.TryExchangeForShells；不进叮当商店。
5. 表现：头顶条登记 PersistentHudVisibilityGuard；只用共享 FX 材质，不新建材质 / 着色器。
6. 金钟罩（owner 2026-11-25「带着的时候周身弄个金钟罩」）：戴着就常驻、跟着主角走（驱动建好就 Follow 主角），
   钟身高度跟护罩余量（打碎归零）；它是世界特效，在 HUD 显隐门之前更新、只随暂停定格；驱动撤掉时立即收起。
"""
import io
import os
import re
import sys

from cs_source_util import clean_source

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = "Integration/NewWeapons/SilenceBell/"


def read(path):
    full = os.path.join(ROOT, *path.split("/"))
    if not os.path.isfile(full):
        return None
    with io.open(full, "r", encoding="utf-8-sig", errors="ignore") as fh:
        text = fh.read()
    return clean_source(text) if full.endswith(".cs") else text


def compact(text):
    return re.sub(r"\s+", "", text or "")


def body_of(source, signature):
    if source is None:
        return ""
    at = source.find(signature)
    if at < 0 or source.find(signature, at + 1) >= 0:
        return ""
    brace = source.find("{", at)
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return compact(source[brace + 1:i])
    return ""


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

    names = ("SilenceBellConfig.cs", "SilenceBellRules.cs", "SilenceBellRuntime.cs", "SilenceBellFx.cs",
             "SilenceBellWeaponConfig.cs", "SilenceBellShop.cs")
    files = dict((n, read(BASE + n)) for n in names)
    missing = [n for n, v in files.items() if v is None]
    if missing:
        print("FAIL SilenceBellWiringGuard\n  [File] 缺少 " + ", ".join(missing))
        return 1

    config = compact(files["SilenceBellConfig.cs"])
    for token, message in (
            ("publicconstfloatCapacity=80f;", "护罩 80 点"),
            ("publicconstfloatRechargeDelaySeconds=5f;", "挨打 5 秒后回"),
            ("publicconstfloatBrokenDelaySeconds=9f;", "打碎后至少 9 秒"),
            ("publicconstfloatRechargePerSecond=20f;", "每秒回 20"),
            ("internalconststringVendorNpcId=ReloadMetronomeConfig.VendorNpcId;", "卖家是无声钟守"),
            ("internalconstintShellCost=30;", "30 枚贝壳")):
        require(token in config, "[数值] " + message)
    require("publicconstintSilenceBell=500220;" in compact(read("Config/ConfigItemIds.cs")), "[TypeID] SilenceBell = 500220")

    rules = files["SilenceBellRules.cs"]
    require("usingUnityEngine;" not in compact(rules), "[规则] 规则文件不依赖 Unity（执行回归直接编译它）")
    absorb = body_of(rules, "internal float Absorb(float damage, float now, out bool broke)")
    require(ordered(absorb, ["Delay(now+SilenceBellConfig.RechargeDelaySeconds);", "if(!(Shield>0f))return0f;",
                             "Delay(now+SilenceBellConfig.BrokenDelaySeconds);"]),
            "[规则] 每次命中顺延 5 秒（碎着也顺延），打碎再顺延 9 秒")

    damage = body_of(read("ZombieMode/ZombieModeDamageRuntime.cs"), "internal static void ReduceFinalDamage(Health health, ref DamageInfo info)")
    require(ordered(damage, ["ResidualSunShieldRuntime.ModifyFinalDamage(health,refinfo);",
                             "SilenceBellRuntime.ModifyFinalDamage(health,refinfo);", "ModBehaviourowner=ModBehaviour.Instance;"]),
            "[入口] 致死前入口先残日盾片、再钟罩，且在丧尸模式门之前")

    runtime = files["SilenceBellRuntime.cs"]
    modify = body_of(runtime, "internal static void ModifyFinalDamage(Health health, ref DamageInfo info)")
    require(modify.startswith("if(!isSubscribed||owner==null||health==null||health.IsDead||info.isFromBuffOrEffect"),
            "[O(1)] 未佩戴时首行返回；持续伤害 / 装备效果不吃")
    require(ordered(modify, ["if(!ReferenceEquals(health,owner.Health))return;", "!IsEnemy(info.fromCharacter)",
                             "rules.Absorb(info.finalDamage,Time.time,outbroke);",
                             "info.finalDamage=Mathf.Max(0f,info.finalDamage-absorbed);"]),
            "[入口] 只护主角、只吃敌方，改的是本击最终伤害")
    require("Team.IsEnemy(owner.Team,character.Team)" in body_of(runtime, "private static bool IsEnemy(CharacterMainControl character)"),
            "[入口] 敌我按官方阵营判定")
    usable = body_of(runtime, "private static bool IsUsableOwner(CharacterMainControl player)")
    require("!LevelManager.Instance.IsBaseLevel" in usable and "NewWeaponEquipState.IsTotemEquipped(BossRushItemIds.SilenceBell)" in usable,
            "[门控] 只在出击场景、实际佩戴时生效")
    require("rules.ResetFull" not in body_of(runtime, "private static void Deactivate()"),
            "[门控] 卸下只撤驱动，不重置护罩（不能靠穿脱补满）")
    require("rules.ResetFull();" in body_of(runtime, "internal static void ResetStaticCaches()"), "[生命周期] 换场景 / 死亡 / 销毁满罩")
    require(ordered(body_of(runtime, "internal static void Subscribe()"), ["if(isSubscribed)return;", "isSubscribed=true;",
                                                                        "Health.OnDead+=OnAnyDead;"]),
            "[订阅] 私有布尔幂等订阅")
    unsub = body_of(runtime, "internal static void Unsubscribe()")
    for handler in ("OnHoldChanged", "OnSlotChanged", "OnAnyDead", "OnSceneLoadingStarted", "OnSceneLoadingFinished",
                    "OnLevelBeginInitializing", "OnAfterLevelInitialized"):
        require("-=" + handler + ";" in unsub, "[订阅] Unsubscribe 退订 " + handler)
    require("ResetStaticCaches();" in unsub, "[订阅] 退订后清状态")

    host = read("Integration/NewWeapons/Common/NewWeaponRuntime.cs")
    for token, message in (("SilenceBellRuntime.Subscribe();", "订阅"), ("SilenceBellRuntime.SetupForScene();", "换场景重置"),
                           ("SilenceBellRuntime.Unsubscribe();", "退订"), ("SilenceBellRuntime.ResetStaticCaches();", "销毁清理")):
        require(token in compact(host), "[生命周期] NewWeaponRuntime " + message)
    require("BossRushItemIds.SilenceBell" in compact(read("Integration/NewWeapons/Common/NewWeaponEquipState.cs")),
            "[门控] TrackedTotemTypeIds 登记钟罩")

    npc = read("Integration/NPCs/DuckNpc/Permanent/PermanentDuckNpcInteractable.cs")
    group = body_of(npc, "private void EnsureGroupedInteractionOptions()")
    require("npcId==SilenceBellConfig.VendorNpcId" in group and "(SilenceBellShopInteractablecomponent)=>component.NpcId=npcId);" in group,
            "[来路] 钟守本人身上挂第二只匣子")
    sky = read("SkyIsland/SkyIslandWorldStoryServices.cs")
    require('AttachDeviceShop<SilenceBellShopInteractable>("Search_H","BellKeeperWardOption",SilenceBellConfig.VendorNpcId,refbellWardShop,refbellWardShopGroup);'
            in body_of(sky, "private void AttachDockShop()"), "[来路] 钟守不在时岛上装置也能换")
    require("DisposeDeviceShop(refbellWardShop,refbellWardShopGroup);" in body_of(sky, "private void DisposeDockShop()"),
            "[来路] 装置匣子随会话清理")
    shop = files["SilenceBellShop.cs"]
    require("ReloadMetronomeTrade.TryExchangeForShells(player,()=>CanTrade(player),BossRushItemIds.SilenceBell,SilenceBellConfig.ShellCost,"
            in body_of(shop, "private void Exchange(CharacterMainControl player)"), "[来路] 与节拍器共用同一条贝壳事务")
    require("returnReloadMetronomeTrade.CanTradeAt(this,npcId,SilenceBellConfig.VendorNpcId,player);"
            in body_of(shop, "private bool CanTrade(CharacterMainControl player)"), "[来路] 同一道好感 / 距离门槛")
    require("BossRushItemIds.SilenceBell" not in compact(read("Integration/Affinity/NPCs/GoblinAffinityConfig.cs")),
            "[来路] 新商品不进叮当商店")
    require("caseBossRushItemIds.SilenceBell:return" in compact(read("Integration/ModItemIcons.cs")), "[图标] 声明物品图标")

    fx = compact(files["SilenceBellFx.cs"])
    require("newMaterial(" not in fx and "Shader.Find(" not in fx, "[表现] 只用共享 FX 材质")
    for sfx in ("SfxBellHum", "SfxBellShatter", "SfxBellDing"):
        require("BossRushInkSkin.PlaySfx(BossRushInkSkin." + sfx + ");" in fx, "[表现] 钟声 " + sfx)
    require("SilenceBell/SilenceBellFx.cs" in (read("tests/PersistentHudVisibilityGuard.py") or ""), "[HUD] 头顶条登记常驻 HUD 守卫")

    fx_src = files["SilenceBellFx.cs"]
    require("fx=SilenceBellFx.Create(go.transform);if(fx!=null&&owner!=null)fx.Follow(owner.transform);"
            in body_of(runtime, "private static void EnsureDriver()"), "[金钟] 驱动建好就让金钟跟着主角")
    require("follow=target;" in body_of(fx_src, "internal void Follow(Transform target)"), "[金钟] Follow 记下跟随目标")
    late = body_of(fx_src, "private void LateUpdate()")
    require(ordered(late, ["floatdt=paused?0f:Time.deltaTime;", "UpdateBell(dt);",
                           "boolvisible=!BossRushUI.IsOfficialHudHidden()&&!BossRushUI.IsGamePaused();"]),
            "[金钟] 金钟在 HUD 显隐门之前更新（世界特效不随 HUD 隐藏），暂停时 dt 为 0 定格")
    bell = body_of(fx_src, "private void UpdateBell(float dt)")
    require(ordered(bell, ["if(!hasState){", "SetActive(false);", "return;}", "if(follow!=null)footPosition=follow.position;",
                           "floattarget=broken?0f:Mathf.Clamp01(fraction);"]),
            "[金钟] 没状态就不显示；跟着主角走；钟身高度 = 护罩余量，打碎归零")
    require("bellAnchor.gameObject.SetActive(false);" in body_of(fx_src, "internal void Release()"),
            "[金钟] 驱动撤掉（卸下 / 死亡 / 切图）时金钟立即收起")

    bat = read("compile_official.bat") or ""
    for n in names:
        require("Integration\\NewWeapons\\SilenceBell\\" + n in bat, "[编译] compile_official.bat 缺少 " + n)
    runner = read("tools/run_runtime_regressions.py") or ""
    fixture = read("tests/fixtures/SilenceBellRules/run.py") or ""
    require('"SilenceBellRules"' in runner and "Integration/NewWeapons/SilenceBell/SilenceBellRules.cs" in fixture,
            "[回归] 执行回归 SilenceBellRules 登记并编入生产规则文件")

    if errors:
        print("FAIL SilenceBellWiringGuard")
        for line in errors:
            print("  " + line)
        return 1
    print("PASS SilenceBellWiringGuard")
    return 0


if __name__ == "__main__":
    sys.exit(main())
