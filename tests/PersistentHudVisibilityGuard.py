# -*- coding: utf-8 -*-
"""常驻 HUD 必须跟随官方界面隐藏（2026-09-14）。

## 为什么要有这份

本库的常驻 HUD 画在 sortingOrder 240–1200（丧尸模式 28000），官方背包、地图、对话画在 100
（UnityPy 读 `resources.assets`）。不让位就会**压在它们上面**——`CampaignHud` 在 2026-09-10 就是这样。
判定早就收在两个共享函数里：

- `BossRushUI.IsOfficialHudHidden()`：照抄官方 `HUDManager.ShouldDisplay` 的公开部分
  （`View.ActiveView` / 官方对话 / 捏脸 / 拍照模式）；
- `BossRushUI.IsGamePaused()`：暂停菜单（`GameManager.Paused`，它是 UIPanel 不是 View，官方 HUD 不因它隐藏）。

可全仓只有天空岛与征程两块 HUD 在用：随机事件徽章、血月红罩、伴宠、Mode H / Mode G / Mode F 雷达、
词条浮层、丧尸模式要么完全不管，要么只管暂停菜单，而 `BossRushUI.cs` 已顶到 1200 行、加不了一个合并函数——
所以每块 HUD 在自己的每帧入口里调这两个现成函数。

## 钉住的三件事

1. 清单里每一块常驻 HUD 的每帧入口都**同时**经过两份判定，并且判定结果落到显隐上
   （开关画布 / 淡出 / 早退）。
2. 每帧入口真的被驱动：调用点存在；Unity 消息（`Update`）则要求类型确实是 MonoBehaviour。
3. **全仓凡是引用 HUD 层级常量的文件都必须归类**：要么进常驻清单，要么进排除清单并写明理由。
   新加一块常驻 HUD 而忘了登记，这里当场红——不必等有人在背包上面看见它。

模态面板与玩家主动打开的面板（Panel / Modal 层，含 Mode G 结算、Mode H 模态页与诊断页、遗种巢、
丧尸模式各面板、天空岛剧情面板）不在此列：它们本来就该画在官方界面之上，逐个理由见
`docs/天空岛_实机前减负_2026-09-14.md` 第三节。

守卫钉的是结构，证明不了实机观感：「打开背包时这块 HUD 真的不见了」仍须实机看。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from cs_source_util import clean_source  # noqa: E402

OFFICIAL = "BossRushUI.IsOfficialHudHidden()"
PAUSED = "BossRushUI.IsGamePaused()"
HUD_LAYERS = ("WorldOverlay", "ModeFBountyRadar", "ModeGHud", "ModeHHud", "PetNestCompanionHud",
              "Hud", "HudOverlay", "ZombieHud")
LAYER_PATTERN = re.compile(r"\bBossRushUILayers\.(?:" + "|".join(HUD_LAYERS) + r")\b")
SKIP_DIRS = {"鸭科夫源码", "Build", "tests", ".git", ".qoder", "wiki-site", "docs", "output", "ArtSource",
             "node_modules", "bin", "obj"}

# (文件, 类型锚点或 None, 每帧入口签名, 显隐落点, 驱动方式, 说明)
# 驱动方式：("call", 文件, 调用片段) 或 ("unity", 类声明正则)。
PERSISTENT = (
    ("RandomEvents/RandomEventHud.cs", None, "internal static void Tick(RandomEventDirector director)",
     "_canvas.enabled = visible;", ("call", "RandomEvents/RandomEventsRuntimeModule.cs", "RandomEventHud.Tick(_director);"),
     "随机事件徽章"),
    ("RandomEvents/RandomEventCatalog.cs", "internal sealed class RandomEventBloodMoon",
     "internal override void OnTick(RandomEventContext ctx, float deltaTime)",
     "_vignette.enabled = shown;", ("call", "RandomEvents/RandomEventDirector.cs", ".OnTick("),
     "血月全屏红罩"),
    ("PetNest/PetNestCompanionHudView.cs", None, "private void Update()",
     "_canvas.enabled = visible;", ("unity", r"class PetNestCompanionHudView\s*:\s*MonoBehaviour"),
     "伴宠状态条"),
    ("ModeH/ModeHUI.cs", None, "public void ApplyHudVisibility()",
     "_hudCanvas.enabled = visible;", ("call", "ModeH/ModeHRuntimeModule_MatchFlow.cs", "_ui.ApplyHudVisibility();"),
     "Mode H 观战 HUD"),
    ("ModeG/ModeGHUD.cs", None, "public void Update(float deltaTime)",
     "SetVisible(visible);", ("call", "ModeG/ModeGEntry.cs", "modeGHUD.Update(deltaTime);"),
     "Mode G 状态文本"),
    ("ModeF/ModeFUI_BountyRadarAndHealthBars.cs", None, "private bool IsModeFBountyRadarSuppressedByOverlay()",
     "return true;", ("call", "ModeF/ModeFUI_BountyRadarAndHealthBars.cs", "if (IsModeFBountyRadarSuppressedByOverlay())"),
     "Mode F 赏金雷达"),
    ("Integration/Mutators/MutatorUI.cs", None, "public static void Tick()",
     "suppressed = true;", ("call", "ModBehaviour.cs", "MutatorUI.Tick();"),
     "词条浮层"),
    ("ZombieMode/ZombieModeHudController.cs", None, "private void Update()",
     "SetPauseMenuHidden(hidden);", ("unity", r"class ZombieModeHudController\s*:\s*MonoBehaviour"),
     "丧尸模式 HUD"),
    ("Campaign/CampaignHud.cs", None, "internal static void Tick()",
     "_canvas.enabled = visible;", ("call", "Campaign/CampaignRuntimeModule.cs", "CampaignHud.Tick();"),
     "征程契约追踪条"),
    ("DebugAndTools/SkyIsland/SkyIslandHud.cs", None, "internal void Tick(float unscaledDelta, bool suppressed)",
     "rootGroup.alpha = visibility;",
     ("call", "DebugAndTools/SkyIsland/SkyIslandSession.cs", "hud.Tick(Time.unscaledDeltaTime, HudSuppressed())"),
     "天空岛右上卡片、区域大标题与字幕"),
)

# 引用了 HUD 层级常量、但不是屏幕常驻 HUD 的文件。每一条都要写明理由。
EXCLUDED = {
    "Integration/WishFountain/WishFountainUI.cs":
        "许愿台是玩家主动打开的界面，只借 HudOverlay 当宿主层（HOST_TOPMOST_SORTING_ORDER）",
    "DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs":
        "基地船点招牌：世界空间画布（WorldOverlay），挂在场景物体上，不是屏幕常驻 HUD",
    "DebugAndTools/SkyIsland/SkyIslandGates.cs":
        "桥口木牌：世界空间文字（WorldOverlay）",
    "DebugAndTools/ArenaPrototype/ArenaPrototypeControls.cs":
        "Dev 专用自建试验场的状态行：只在 Dev 构建出现，本轮只登记不改（见报告第三节待办）",
    "ModeF/ModeFUI.cs":
        "只定义雷达画布层级常量；画布与显隐在 ModeFUI_BountyRadarAndHealthBars.cs（已在常驻清单）",
}


def body_of(source, signature, start=0):
    """从 start 起找 signature，切出它的方法体；找不到返回 None。"""
    at = source.find(signature, start)
    if at < 0:
        return None
    brace = source.find("{", at)
    if brace < 0:
        return None
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:i]
    return None


def production_files():
    """仓库里的生产 .cs（相对路径，正斜杠）。先按原文粗筛再清洗，免得逐个跑状态机。"""
    found = {}
    for path in ROOT.rglob("*.cs"):
        rel = path.relative_to(ROOT)
        if rel.parts and rel.parts[0] in SKIP_DIRS:
            continue
        if any(part in ("bin", "obj") for part in rel.parts):
            continue
        raw = path.read_text(encoding="utf-8-sig", errors="replace")
        if "BossRushUILayers." not in raw:
            continue
        found[rel.as_posix()] = raw
    return found


def check(read, layer_files):
    """read(rel) -> 清洗过的源码或 None；layer_files: {相对路径: 原文}。返回错误清单。"""
    errors = []
    for rel, anchor, signature, effect, drive, label in PERSISTENT:
        source = read(rel)
        if source is None:
            errors.append("%s：找不到文件 %s" % (label, rel))
            continue
        start = 0
        if anchor:
            start = source.find(anchor)
            if start < 0:
                errors.append("%s：%s 里找不到类型 %s" % (label, rel, anchor))
                continue
        body = body_of(source, signature, start)
        if body is None:
            errors.append("%s：%s 里找不到每帧入口 %s" % (label, rel, signature))
            continue
        for token, why in ((OFFICIAL, "官方背包 / 地图 / 对话 / 捏脸 / 拍照模式"), (PAUSED, "暂停菜单")):
            if token not in body:
                errors.append("%s（%s）的每帧入口没有经过 %s：%s开着时它会压在上面"
                              % (label, rel, token, why))
        if effect not in body:
            errors.append("%s（%s）的每帧入口没有把显隐落到显示上（缺 %s）" % (label, rel, effect))
        if drive[0] == "call":
            host = read(drive[1])
            if host is None or drive[2] not in host:
                errors.append("%s 的每帧入口没有被驱动：%s 里缺 %s" % (label, drive[1], drive[2]))
        elif not re.search(drive[1], source):
            errors.append("%s 靠 Unity 的 Update 驱动，但 %s 里找不到 MonoBehaviour 声明" % (label, rel))

    listed = {entry[0] for entry in PERSISTENT}
    for rel in sorted(layer_files):
        cleaned = clean_source(layer_files[rel])
        if not LAYER_PATTERN.search(cleaned):
            continue
        if rel not in listed and rel not in EXCLUDED:
            errors.append("未归类的 HUD 层级画布：%s 引用了 HUD 层级常量。常驻就进 PERSISTENT 并接 %s 与 %s，"
                          "不是常驻就进 EXCLUDED 写明理由" % (rel, OFFICIAL, PAUSED))
    for rel in sorted(EXCLUDED):
        raw = layer_files.get(rel)
        if raw is None or not LAYER_PATTERN.search(clean_source(raw)):
            errors.append("排除清单过期：%s 已不再引用 HUD 层级常量，删掉这一条" % rel)
    for rel in listed & set(EXCLUDED):
        errors.append("%s 同时在常驻清单与排除清单里" % rel)
    return errors


def main():
    cache = {}

    def read_from(overrides):
        def read(rel):
            if rel in overrides:
                return overrides[rel]
            if rel not in cache:
                path = ROOT / rel
                cache[rel] = clean_source(path.read_text(encoding="utf-8-sig")) if path.is_file() else None
            return cache[rel]
        return read

    layer_files = production_files()
    errors = check(read_from({}), layer_files)

    # 反向检查：在内存里拆掉每一块的判定、或加一块没登记的 HUD，断言都必须转红。
    probes = 0
    for rel, anchor, signature, effect, drive, label in PERSISTENT:
        source = read_from({})(rel)
        if source is None:
            continue
        for token in (OFFICIAL, PAUSED):
            probes += 1
            if not check(read_from({rel: source.replace(token, "false")}), layer_files):
                errors.append("反向检查失效：拆掉 %s 的 %s 之后守卫仍然全绿" % (label, token))
        probes += 1
        if not check(read_from({rel: source.replace(effect, ";")}), layer_files):
            errors.append("反向检查失效：拆掉 %s 的显隐落点 %s 之后守卫仍然全绿" % (label, effect))
    probes += 1
    fake = dict(layer_files)
    fake["NewMode/NewModeHud.cs"] = 'var c = BossRushUI.CreateCanvasRoot("X", BossRushUILayers.HudOverlay, false);'
    if not check(read_from({}), fake):
        errors.append("反向检查失效：新加一块没登记的 HudOverlay 画布之后守卫仍然全绿")

    if errors:
        print("PersistentHudVisibilityGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("PersistentHudVisibilityGuard: PASS（%d 块常驻 HUD 都经过 IsOfficialHudHidden + IsGamePaused 并落到显隐；"
          "%d 个 HUD 层级文件已归类（排除 %d）；%d 个反向检查）"
          % (len(PERSISTENT), len(layer_files), len(EXCLUDED), probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
