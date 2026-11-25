#!/usr/bin/env python3
"""FriendlyNpcNameplateGuard — 友好 NPC 名牌（2026-11-24 玩家许愿第 6 项；2026-11-25 owner 定案）的接线不变式。

owner 2026-11-25：不要配置项，直接默认「走近时显示」；名字下面的墨笔横条去掉，只显示名字。

1. 没有配置项：不存在 FriendlyNpcNameplateSettings / ConfigFriendlyNpcNameplate.cs，Config.cs 与白名单里没有名牌键，
   名牌也不读任何选项（没有 Mode 常量、没有 CurrentMode）。
2. 只显示名字，且样式与原版一致（owner 2026-11-25「名牌的样式和原版 NPC 的不一致」）：CreatePlate 克隆官方
   HealthBarManager.healthBarPrefab，取它的 nameText，拆掉血条组件，名字以外的图形一律不画；不自己画字、不画线。
   画布缩放照抄官方 HUD 画布；锚点高度与屏幕上抬照官方 HealthBar（头盔挂点 + 0.5 米、屏幕高 2%）。
3. 每帧入口先过官方 HUD 显隐与暂停两道门；没有目标也没有残留名牌时直接返回。
   NPCNameTagHelper 的登记路径不碰名牌（登记本身不建任何东西）。
4. 目标只读 NPCNameTagHelper.CopyNameplateTargets 的登记快照，不做场景查找。
5. 两道距离：8 米内淡入、10 米外淡出。
6. 生命周期：进场景 Refresh()（幂等，已建就返回）；唯一清理 owner 是 CommonNpcRuntimeModule.OnDestroy → Shutdown()。
7. 执行回归 SkyIslandInteraction 编入 CopyNameplateTargets（快照读最新名字与登记高度、不建东西）。

常驻 HUD 登记由 PersistentHudVisibilityGuard 守；名牌实际观感（淡入淡出、位置、不压官方界面）靠实机清单 M_NPC_NAMEPLATE_01。
"""
import io
import os
import re
import sys

from cs_source_util import clean_source

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


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

    plates_src = read("Integration", "Utils", "FriendlyNpcNameplates.cs")
    if plates_src is None:
        print("FAIL FriendlyNpcNameplateGuard\n  [File] 缺少 Integration/Utils/FriendlyNpcNameplates.cs")
        return 1
    plates = compact(plates_src)
    require("internalconstfloatNearShowDistance=8f;" in plates and "internalconstfloatNearHideDistance=10f;" in plates,
            "[距离] 8 米淡入、10 米淡出")

    # 1. 没有配置项
    for token in ("ModeOff", "ModeNear", "ModeAlways", "CurrentMode", "FriendlyNpcNameplateSettings", "AlwaysMaxDistance"):
        require(token not in plates, "[无配置] 名牌不读任何选项，不该再有 " + token + "（owner 2026-11-25：默认走近显示、去掉配置）")
    require(read("Config", "ConfigFriendlyNpcNameplate.cs") is None, "[无配置] Config/ConfigFriendlyNpcNameplate.cs 应已删除")
    bat = read("compile_official.bat") or ""
    require("ConfigFriendlyNpcNameplate" not in bat, "[无配置] 编译清单里不该再有 ConfigFriendlyNpcNameplate.cs")
    for rel in (("Config", "Config.cs"), ("Config", "ConfigModConfigKeys.cs")):
        text = read(*rel) or ""
        require("FriendlyNpcNameplate" not in text, "[无配置] %s 里不该再有名牌配置的接线或白名单" % "/".join(rel))

    refresh = compact(body_of(plates_src, "internal static void Refresh()"))
    require(ordered(refresh, ["if(instance!=null)return;", "BossRushUI.CreateCanvasRoot("]),
            "[生命周期] Refresh 幂等：已建就返回，否则建一份画布")
    update = compact(body_of(plates_src, "private void Update()"))
    require(ordered(update, ["boolvisible=!BossRushUI.IsOfficialHudHidden()&&!BossRushUI.IsGamePaused();",
                             "canvas.enabled=visible;", "if(!visible)return;", "Tick();"]),
            "[每帧] 先过官方 HUD 显隐与暂停两道门再 Tick")
    shown = compact(body_of(plates_src, "internal static bool NextShown(bool shown, float distance)"))
    require(shown == "returnshown?distance<NearHideDistance:distance<=NearShowDistance;",
            "[距离] NextShown：两道距离（亮着 10 米才灭，灭着 8 米才亮），不看任何选项")
    tick = compact(body_of(plates_src, "private void Tick()"))
    require(ordered(tick, ["NPCNameTagHelper.CopyNameplateTargets(targets);", "if(targets.Count==0&&plates.Count==0)return;"]),
            "[目标] 名牌只读 NPCNameTagHelper 的登记快照；没目标也没残留名牌时直接返回")
    for forbidden in ("FindObjectsOfType", "FindObjectOfType", "GameObject.Find(", "FindWithTag"):
        require(forbidden not in plates, "[目标] 名牌不做场景查找: " + forbidden)

    # 2. 只显示名字
    create = compact(body_of(plates_src, "private Plate CreatePlate(string name)"))
    require(ordered(create, ["HealthBarManagermanager=HealthBarManager.Instance;",
                             "if(manager==null||manager.healthBarPrefab==null||NameTextField==null)returnnull;",
                             "MatchOfficialScaler(manager);",
                             "Object.Instantiate(manager.healthBarPrefab.gameObject,canvas.transform,false);",
                             "NameTextField.GetValue(bar)asTextMeshProUGUI",
                             "bar.enabled=false;", "Object.Destroy(bar);",
                             "if(graphics[i].transform.IsChildOf(label.transform))continue;", "graphics[i].enabled=false;",
                             "label.text=name;"]),
            "[外观] 名牌克隆官方血条、只留官方那行名字（血条逻辑拆掉、别的图形不画；"
            "名字自己与它底下回退字体的 TMP_SubMeshUI 整棵子树都不关）")
    for forbidden in ("CreateBrushStroke", "AddComponent<Image>", "CreateImage", "BossRushInkSkin.", "ZombieModeUIHelper.CreateText",
                      "ApplyGameFont", "fontSize="):
        require(forbidden not in create, "[外观] 名牌用官方那行名字，不自己画字、不画线、不改字号：" + forbidden)
    scaler = compact(body_of(plates_src, "private void MatchOfficialScaler(HealthBarManager manager)"))
    foundation = compact(body_of(read("Common", "UI", "BossRushUIFoundation.cs"),
                                 "internal static void MatchCanvasScaler(CanvasScaler target, CanvasScaler source)"))
    require(ordered(scaler, ["manager.GetComponentInParent<Canvas>()", "official.rootCanvas",
                             "BossRushUIKit.MatchCanvasScaler(canvas.GetComponent<CanvasScaler>(),from);"])
            and ordered(foundation, ["target.uiScaleMode=source.uiScaleMode;", "target.referenceResolution=source.referenceResolution;",
                                     "target.matchWidthOrHeight=source.matchWidthOrHeight;", "target.scaleFactor=source.scaleFactor;"]),
            "[外观] 名牌画布缩放照抄官方 HUD 画布（经共享库 BossRushUIKit.MatchCanvasScaler）")
    require("screen.y+=screenLift;" in tick and "privateconstfloatOfficialScreenYOffset=0.02f;" in plates
            and "privateconstfloatOfficialHelmetLift=0.5f;" in plates,
            "[位置] 名牌位置照官方 HealthBar：头盔挂点 + 0.5 米、屏幕上抬屏幕高的 2%")

    module_src = read("Integration", "NPCs", "Common", "CommonNpcRuntimeModule.cs")
    require("FriendlyNpcNameplates.Shutdown();" in compact(body_of(module_src, "public override void OnDestroy()")),
            "[清理] CommonNpcRuntimeModule.OnDestroy 是名牌唯一清理 owner")
    require("FriendlyNpcNameplates.Refresh();" in compact(body_of(module_src, "public override void OnSceneLoaded(SceneRuntimeContext context)")),
            "[生命周期] 进场景确保名牌画布在")

    helper_src = read("Integration", "Utils", "NPCNameTagHelper.cs")
    register = compact(body_of(helper_src, "public static bool RegisterOriginalHealthBarName("))
    require(bool(register) and "FriendlyNpcNameplates" not in register and "newGameObject" not in register,
            "[O(1)] NPC 登记路径不碰名牌、不建任何东西")
    copy = compact(body_of(helper_src, "internal static void CopyNameplateTargets(List<NpcNameplateTarget> into)"))
    require("into.Clear();" in copy and "entry.Target==null" in copy and "Height=entry.Height" in copy,
            "[目标] 快照清空重抄、跳过已销毁目标、带上登记高度")

    fixture = read("tests", "fixtures", "SkyIslandInteraction", "run.py") or ""
    require('"internal static void CopyNameplateTargets("' in fixture and '"internal struct NpcNameplateTarget"' in fixture,
            "[回归] SkyIslandInteraction 要原样编入名牌快照")

    if errors:
        print("FAIL FriendlyNpcNameplateGuard")
        for line in errors:
            print("  " + line)
        return 1
    print("PASS FriendlyNpcNameplateGuard")
    return 0


if __name__ == "__main__":
    sys.exit(main())
