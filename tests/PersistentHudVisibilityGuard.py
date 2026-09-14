# -*- coding: utf-8 -*-
"""常驻 HUD 必须跟随官方界面隐藏（2026-09-14）。

## 为什么要有这份

本库的常驻 HUD 画在 sortingOrder 240–1200（丧尸模式 28000），官方背包、地图、对话画在 100
（UnityPy 读 `resources.assets`）。不让位就会**压在它们上面**——`CampaignHud` 在 2026-09-10 就是这样。
判定收在两个共享函数里：

- `BossRushUI.IsOfficialHudHidden()`：照抄官方 `HUDManager.ShouldDisplay`（`View.ActiveView` / 官方对话 / 捏脸 /
  拍照模式 / 隐藏令牌）；
- `BossRushUI.IsGamePaused()`：暂停菜单（`GameManager.Paused`，它是 UIPanel 不是 View，官方 HUD 不因它隐藏）。

每块常驻 HUD 在自己的每帧入口里调这两个函数。

## 钉住的事

1. 清单里每一块常驻 HUD 的每帧入口都**同时**经过两份判定，并且判定结果真的落到显隐上。
   2026-09-14 UI 优化对照审核 F-08：旧版只做子串断言，7 个语义变异全部 PASS。现在加了语义判据——
   - 判定不能被包进与显隐无关的分支（外层只许是 `try` 或判空）；
   - 判定之前不能有一条无条件的 `return` 把它架空；
   - 显隐落点必须在判定之后，且中间不许把结果改回「显示」（`visible = true` / `suppressed = false` 一类）。
2. 每帧入口真的被驱动：宿主方法里的调用点、它外面套了哪几层、之前有几条提前返回，都按清单逐条核对——
   把调用挪进交战分支、在它前面加一条提前返回，这里都会红。Unity 消息（`Update`）则要求类型确实是 MonoBehaviour。
3. **全仓凡是引用 UI 层级常量的文件都必须归类**：常驻清单、排除清单（引用 HUD 层却不是常驻 HUD）、
   模态 / 主动打开清单，逐个写明理由。新加一块界面而忘了登记，这里当场红。
4. **画布层级不许写字面量**：`Canvas.sortingOrder = 1200`、`CreateCanvasRoot("X", 1200, …)` 绕开了层级表，
   上面的归类就查不到它。
5. **`OnGUI` 也要归类**：IMGUI 画在所有 uGUI 画布之上，常驻内容走 `OnGUI` 同样会压住官方界面。

模态面板与玩家主动打开的面板（Panel / Modal 层）本来就该画在官方界面之上，逐个理由见下面的 MODAL。
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
ANY_LAYER_PATTERN = re.compile(r"\bBossRushUILayers\.\w+\b")
SKIP_DIRS = {"鸭科夫源码", "Build", "tests", ".git", ".qoder", "wiki-site", "docs", "output", "ArtSource",
             "node_modules", "bin", "obj", "Assets", "skills", "codex-skills"}

# 判定外层允许套的块：try 与判空（`if (_vignette != null)` 一类）。
ALLOWED_HEADER = re.compile(r"^(?:try|if \(\s*[\w.]+\s*!=\s*null\s*\))$")
# 判定之后、显隐落点之前，不许把结果改回「显示」。
REVERT_TO_VISIBLE = re.compile(r"\b(?:visible|shown)\s*=\s*true\b|\b(?:suppressed|hidden)\s*=\s*false\b")

# (文件, 类型锚点或 None, 每帧入口签名, 显隐落点, 驱动方式, 说明)
# 驱动方式：("call", 宿主文件, 调用片段, 宿主方法签名, 调用外层的块, 调用前的单行判空 if, 宿主里调用之前的 return 条数)
#          或 ("unity", 类声明正则)。宿主那三项是按 2026-09-14 的生产代码记下的：宿主结构有意改了就一起改这里。
PERSISTENT = (
    ("RandomEvents/RandomEventHud.cs", None, "internal static void Tick(RandomEventDirector director)",
     "_canvas.enabled = visible;",
     ("call", "RandomEvents/RandomEventsRuntimeModule.cs", "RandomEventHud.Tick(_director);",
      "public override void OnUpdate(float deltaTime, float unscaledDeltaTime)", ["try"], "", 3),
     "随机事件徽章"),
    ("RandomEvents/RandomEventCatalog.cs", "internal sealed class RandomEventBloodMoon",
     "internal override void OnTick(RandomEventContext ctx, float deltaTime)",
     "_vignette.enabled = shown;",
     ("call", "RandomEvents/RandomEventDirector.cs", "evt.OnTick(ctx, dt);",
      "private void TickEventActive(float dt)", ["if (!_activeTickFaulted)", "try"], "", 1),
     "血月全屏红罩"),
    ("PetNest/PetNestCompanionHudView.cs", None, "private void Update()",
     "_canvas.enabled = visible;", ("unity", r"class PetNestCompanionHudView\s*:\s*MonoBehaviour"),
     "伴宠状态条"),
    ("ModeH/ModeHUI.cs", None, "public void ApplyHudVisibility()",
     "_hudCanvas.enabled = visible;",
     ("call", "ModeH/ModeHRuntimeModule_MatchFlow.cs", "_ui.ApplyHudVisibility();",
      "partial void OnUpdateInternal(float deltaTime, float unscaledDeltaTime)", [], "if (_ui != null)", 0),
     "Mode H 观战 HUD"),
    ("ModeG/ModeGHUD.cs", None, "public void Update(float deltaTime)",
     "SetVisible(visible);",
     ("call", "ModeG/ModeGEntry.cs", "modeGHUD.Update(deltaTime);",
      "private void UpdateModeG(float deltaTime)", ["try"], "if (modeGHUD != null)", 1),
     "Mode G 状态文本"),
    ("ModeF/ModeFUI_BountyRadarAndHealthBars.cs", None, "private bool IsModeFBountyRadarSuppressedByOverlay()",
     "return true;",
     ("call", "ModeF/ModeFUI_BountyRadarAndHealthBars.cs", "if (IsModeFBountyRadarSuppressedByOverlay())",
      "private bool ShouldShowModeFBountyRadar()", [], "", 2),
     "Mode F 赏金雷达"),
    ("Integration/Mutators/MutatorUI.cs", None, "public static void Tick()",
     "suppressed = true;",
     ("call", "ModBehaviour.cs", "MutatorUI.Tick();", "void Update()", [], "", 1),
     "词条浮层"),
    ("ZombieMode/ZombieModeHudController.cs", None, "private void Update()",
     "SetPauseMenuHidden(hidden);", ("unity", r"class ZombieModeHudController\s*:\s*MonoBehaviour"),
     "丧尸模式 HUD"),
    ("Campaign/CampaignHud.cs", None, "internal static void Tick()",
     "_canvas.enabled = visible;",
     ("call", "Campaign/CampaignRuntimeModule.cs", "CampaignHud.Tick();",
      "public override void OnUpdate(float deltaTime, float unscaledDeltaTime)", ["try"], "", 2),
     "征程契约追踪条"),
    ("DebugAndTools/SkyIsland/SkyIslandHud.cs", None, "internal void Tick(float unscaledDelta, bool suppressed)",
     "rootGroup.alpha = visibility;",
     ("call", "DebugAndTools/SkyIsland/SkyIslandSession.cs", "hud.Tick(Time.unscaledDeltaTime, HudSuppressed())",
      "private void Update()", [], "if (hud != null)", 1),
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

# 只引用非 HUD 层级（Panel / Modal / Toast / 过场……）的文件：模态、玩家主动打开的面板或一次性演出，
# 本来就该画在官方界面之上。2026-09-14 审核 F-08 ⑦：旧版只归类 HUD 层，新写一块常驻 HUD 却用 Panel 层就查不到。
MODAL = {
    "Achievement/SteamAchievementPopup.cs": "成就解锁提示（Toast）：几秒后自动消失的一次性提示",
    "Achievement/AchievementView.cs": "成就界面（Panel）：玩家主动打开",
    "BossFilter/BossFilterUi.cs": "Boss 筛选界面（Panel）：玩家主动打开",
    "Campaign/CampaignBoardView.cs": "征程契约板（Panel）：玩家主动打开",
    "DebugAndTools/F3DebugCheatMenuUi.cs": "F3 调试菜单（Modal）：Dev 构建里玩家主动打开",
    "DebugAndTools/NPCTeleportUI.cs": "NPC 传送调试界面（Panel）：玩家主动打开",
    "DebugAndTools/SkyIsland/SkyIslandStoryPresentation.cs": "天空岛剧情面板（Modal）：交互打开，注册官方 HUD 隐藏令牌",
    "Integration/BackMountain/ShowcaseUI.cs": "后山展柜（Panel）：交互打开",
    "Integration/Codex/CodexView.cs": "图鉴界面（Panel）：玩家主动打开",
    "Integration/Codex/CodexView_Grid.cs": "图鉴详情确认弹窗（ModalConfirm）",
    "Integration/DailyReport/DailyReportUI.cs": "日报界面（Panel）：玩家主动打开",
    "Integration/NPCs/Courier/OriginalConfirmDialogueAdapter.cs": "快递员确认对话（ModalConfirm）",
    "Integration/UI/ImageViewerUI.cs": "看图器（Modal）：玩家主动打开",
    "Integration/Wedding/NPCMarriageSystem.cs": "婚礼过场（WeddingCutscene）：全屏演出",
    "Integration/WishFountain/WishFountainRewardAnimationView.cs": "许愿奖励演出（Modal）：玩家操作后播放",
    "ModeG/ModeGInteractable.cs": "Mode G 入口确认页（ModeGEntry）：交互打开的模态",
    "ModeG/ModeGRecapPanel.cs": "Mode G 战后回顾（ModeGRecap）：模态",
    "ModeH/ModeHRecoveryPanel.cs": "Mode H 恢复壳（ModeHRecovery）：应急模态",
    "PetNest/PetNestUI.cs": "遗种巢主界面（PetNestPanel）：交互打开",
    "PetNest/PetNestRenameModal.cs": "遗种巢改名（PetNestModal）",
    "PetNest/PetNestReleaseConfirmModal.cs": "遗种巢放生确认（PetNestModal）",
    "PetNest/PetNestHatchRevealView.cs": "遗种巢孵化揭晓（PetNestModal）",
    "PetNest/PetNestExpeditionRevealView.cs": "遗种巢远征揭晓（PetNestModal）",
    "ZombieMode/ZombieModeCashInvestmentView.cs": "丧尸模式投资面板（ZombieModalInput）：占模态输入",
    "ZombieMode/ZombieModeEntry_StarterLoadout.cs": "丧尸模式开局配装（ZombieModal）",
    "ZombieMode/ZombieModeExtractionController.cs": "丧尸模式撤离确认（ZombieModal）",
    "ZombieMode/ZombieModeRewards.cs": "丧尸模式奖励与护士服务面板（ZombieModal / ZombieService）",
}

# 写了 OnGUI 的生产文件。IMGUI 画在所有 uGUI 画布之上，常驻内容走这里同样会压住官方界面。
ONGUI = {
    "ModBehaviour.cs": "只转发调试工具的 DrawDebugToolsRuntimeGui：F2–F12 调试面板，玩家主动开关",
    "DebugAndTools/InventoryInspector.cs": "背包检查器调试窗：调试工具，玩家主动开关",
}

CANVAS_VAR = (re.compile(r"\bCanvas\s+(\w+)\s*[;=,)]"),
              re.compile(r"\b(\w+)\s*=\s*[\w.]*(?:AddComponent|GetComponent)<Canvas>\(\)"))
SORTING_LITERAL = re.compile(r"\b(\w+)\.sortingOrder\s*=\s*-?\d+\s*;")
ROOT_LITERAL = re.compile(r"\bCreateCanvasRoot\(\s*[^,()]+,\s*-?\d+\s*,")


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


def header_chain(body, pos):
    """body 里 pos 处外面套着哪几层块（每层记它的头，如 `try`、`if (x != null)`），以及它所在语句 pos 之前的前缀。"""
    stack = []
    last = 0
    i = 0
    while i < pos:
        ch = body[i]
        if ch == '"':
            j = i + 1
            while j < len(body) and body[j] != '"':
                if body[j] == "\\":
                    j += 1
                j += 1
            i = j + 1
            continue
        if ch == "{":
            stack.append(" ".join(body[last:i].split()))
            last = i + 1
        elif ch == "}":
            if stack:
                stack.pop()
            last = i + 1
        elif ch == ";":
            last = i + 1
        i += 1
    return stack, " ".join(body[last:pos].split())


def leading_if(prefix):
    m = re.match(r"(if \(.*\))", prefix)
    return m.group(1) if m else ""


def gate_errors(label, rel, body, effect):
    """判定语义：外层只许 try / 判空；之前没有架空它的无条件 return；落点在判定之后且中间不改回显示。"""
    errors = []
    official_at = body.find(OFFICIAL)
    paused_at = body.find(PAUSED)
    for token, why in ((OFFICIAL, "官方背包 / 地图 / 对话 / 捏脸 / 拍照模式"), (PAUSED, "暂停菜单")):
        if token not in body:
            errors.append("%s（%s）的每帧入口没有经过 %s：%s开着时它会压在上面" % (label, rel, token, why))
    if official_at < 0 or paused_at < 0:
        return errors
    for token, at in ((OFFICIAL, official_at), (PAUSED, paused_at)):
        chain, _ = header_chain(body, at)
        bad = [header for header in chain if not ALLOWED_HEADER.match(header)]
        if bad:
            errors.append("%s（%s）的 %s 被包在与显隐无关的分支里（%s）：分支不成立的那些帧不跟随官方界面"
                          % (label, rel, token, " / ".join(bad)))
    gate_at = min(official_at, paused_at)
    gate_chain, _ = header_chain(body, gate_at)
    for m in re.finditer(r"\breturn\b", body[:gate_at]):
        chain, prefix = header_chain(body, m.start())
        if prefix == "" and gate_chain[:len(chain)] == chain:
            errors.append("%s（%s）的判定之前有一条无条件 return，判定永远走不到" % (label, rel))
            break
    effect_at = body.find(effect, max(official_at, paused_at))
    if effect_at < 0:
        errors.append("%s（%s）的每帧入口没有在判定之后把显隐落到显示上（缺 %s）" % (label, rel, effect))
        return errors
    revert = REVERT_TO_VISIBLE.search(body, gate_at, effect_at)
    if revert:
        errors.append("%s（%s）的判定结果在落到显示之前又被改回显示（%s）" % (label, rel, revert.group(0)))
    return errors


def host_errors(label, drive, read):
    _, host_rel, call, signature, expected_chain, expected_if, expected_returns = drive
    host = read(host_rel)
    if host is None:
        return ["%s 的宿主文件 %s 不存在" % (label, host_rel)]
    body = body_of(host, signature)
    if body is None:
        return ["%s 的每帧入口没有被驱动：%s 里找不到宿主方法 %s" % (label, host_rel, signature)]
    at = body.find(call)
    if at < 0:
        return ["%s 的每帧入口没有被驱动：%s 的 %s 里缺 %s" % (label, host_rel, signature, call)]
    errors = []
    chain, prefix = header_chain(body, at)
    if chain != expected_chain or leading_if(prefix) != expected_if:
        errors.append("%s 的宿主调用 %s 外面套的块变了：现在是 %s %s，清单记的是 %s %s——挪进分支的话有些帧不驱动"
                      % (label, call, chain, leading_if(prefix) or "-", expected_chain, expected_if or "-"))
    returns = len(re.findall(r"\breturn\b", body[:at]))
    if returns != expected_returns:
        errors.append("%s 的宿主调用 %s 之前有 %d 条 return，清单记的是 %d 条：新加的提前返回会让它有些帧不被驱动"
                      % (label, call, returns, expected_returns))
    return errors


def check(read, layer_files, canvas_files, ongui_files):
    """read(rel) -> 清洗过的源码或 None；三个 dict 都是 {相对路径: 原文}。返回错误清单。"""
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
        errors += gate_errors(label, rel, body, effect)
        if drive[0] == "call":
            errors += host_errors(label, drive, read)
        elif not re.search(drive[1], source):
            errors.append("%s 靠 Unity 的 Update 驱动，但 %s 里找不到 MonoBehaviour 声明" % (label, rel))

    listed = {entry[0] for entry in PERSISTENT}
    for rel in sorted(layer_files):
        cleaned = clean_source(layer_files[rel])
        if LAYER_PATTERN.search(cleaned):
            if rel not in listed and rel not in EXCLUDED:
                errors.append("未归类的 HUD 层级画布：%s 引用了 HUD 层级常量。常驻就进 PERSISTENT 并接 %s 与 %s，"
                              "不是常驻就进 EXCLUDED 写明理由" % (rel, OFFICIAL, PAUSED))
            if rel in MODAL:
                errors.append("%s 在模态清单里却引用了 HUD 层级常量：挪到 PERSISTENT 或 EXCLUDED" % rel)
        elif ANY_LAYER_PATTERN.search(cleaned) and rel not in listed and rel not in EXCLUDED and rel not in MODAL:
            errors.append("未归类的 UI 层级画布：%s 引用了层级常量却不在任何清单里。模态 / 主动打开的面板进 MODAL 写明理由；"
                          "常驻 HUD 要用 HUD 层并进 PERSISTENT" % rel)
    for rel in sorted(EXCLUDED):
        raw = layer_files.get(rel)
        if raw is None or not LAYER_PATTERN.search(clean_source(raw)):
            errors.append("排除清单过期：%s 已不再引用 HUD 层级常量，删掉这一条" % rel)
    for rel in sorted(MODAL):
        raw = layer_files.get(rel)
        if raw is None or not ANY_LAYER_PATTERN.search(clean_source(raw)):
            errors.append("模态清单过期：%s 已不再引用层级常量，删掉这一条" % rel)
    for rel in listed & set(EXCLUDED):
        errors.append("%s 同时在常驻清单与排除清单里" % rel)

    for rel in sorted(canvas_files):
        cleaned = clean_source(canvas_files[rel])
        names = set()
        for pattern in CANVAS_VAR:
            names.update(pattern.findall(cleaned))
        for m in SORTING_LITERAL.finditer(cleaned):
            if m.group(1) in names:
                errors.append("%s 给画布 %s 写了字面量层级（%s）：用 BossRushUILayers 常量，否则层级归类查不到它"
                              % (rel, m.group(1), m.group(0).strip()))
        for m in ROOT_LITERAL.finditer(cleaned):
            errors.append("%s 的 CreateCanvasRoot 传了字面量层级：用 BossRushUILayers 常量（%s）" % (rel, m.group(0).strip()))

    for rel in sorted(ongui_files):
        if re.search(r"\bvoid\s+OnGUI\s*\(", clean_source(ongui_files[rel])) and rel not in ONGUI:
            errors.append("未归类的 OnGUI：%s 画了 IMGUI（压在所有 uGUI 画布之上）。常驻内容要跟随官方界面，"
                          "调试 / 主动打开的窗口进 ONGUI 写明理由" % rel)
    for rel in sorted(ONGUI):
        raw = ongui_files.get(rel)
        if raw is None or not re.search(r"\bvoid\s+OnGUI\s*\(", clean_source(raw)):
            errors.append("OnGUI 清单过期：%s 已不再写 OnGUI，删掉这一条" % rel)
    return errors


def production_sources():
    """仓库里的生产 .cs（相对路径，正斜杠），按三种用途粗筛原文。"""
    layer, canvas, ongui = {}, {}, {}
    for path in ROOT.rglob("*.cs"):
        rel = path.relative_to(ROOT)
        if rel.parts and rel.parts[0] in SKIP_DIRS:
            continue
        if any(part in ("bin", "obj") for part in rel.parts):
            continue
        raw = path.read_text(encoding="utf-8-sig", errors="replace")
        key = rel.as_posix()
        if "BossRushUILayers." in raw:
            layer[key] = raw
        if "sortingOrder" in raw or "CreateCanvasRoot" in raw:
            canvas[key] = raw
        if "OnGUI" in raw:
            ongui[key] = raw
    return layer, canvas, ongui


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

    layer_files, canvas_files, ongui_files = production_sources()
    errors = check(read_from({}), layer_files, canvas_files, ongui_files)

    def expect_red(description, overrides=None, layers=None, canvases=None, onguis=None):
        if not check(read_from(overrides or {}), layers or layer_files, canvases or canvas_files, onguis or ongui_files):
            errors.append("反向检查失效：" + description + " 之后守卫仍然全绿")

    def mutate(rel, pattern, replacement):
        source = read_from({})(rel)
        mutated, count = re.subn(pattern, replacement, source, count=1)
        if count != 1:
            errors.append("反向检查锚点失效：%s -> %s" % (rel, pattern))
            return None
        return {rel: mutated}

    # 反向检查：在内存里拆掉每一块的判定或落点、复现审核 F-08 的 7 个语义变异，断言都必须转红。
    probes = 0
    for rel, anchor, signature, effect, drive, label in PERSISTENT:
        source = read_from({})(rel)
        if source is None:
            continue
        for token in (OFFICIAL, PAUSED):
            probes += 1
            expect_red("拆掉 %s 的 %s" % (label, token), {rel: source.replace(token, "false")})
        probes += 1
        expect_red("拆掉 %s 的显隐落点 %s" % (label, effect), {rel: source.replace(effect, ";")})

    semantic = [
        ("① 词条浮层的闸门改成 suppressed = false", "Integration/Mutators/MutatorUI.cs",
         r"(BossRushUI\.IsGamePaused\(\)\)\)\s*\{\s*)suppressed = true;", r"\1suppressed = false;"),
        ("② Mode F 雷达的判定前加一条 return false", "ModeF/ModeFUI_BountyRadarAndHealthBars.cs",
         r"(private bool IsModeFBountyRadarSuppressedByOverlay\(\)\s*\{)", r"\1 return false;"),
        ("③ Mode H 的宿主调用挪进交战分支", "ModeH/ModeHRuntimeModule_MatchFlow.cs",
         r"if \(_ui != null\)\s*_ui\.ApplyHudVisibility\(\);",
         "if (_runState != null && _runState.engaged) { _ui.ApplyHudVisibility(); }"),
        ("③' Mode H 的宿主调用前加一条提前返回", "ModeH/ModeHRuntimeModule_MatchFlow.cs",
         r"if \(_ui != null\)\s*_ui\.ApplyHudVisibility\(\);",
         "if (_commandsClosed) return; if (_ui != null) _ui.ApplyHudVisibility();"),
        ("④ 伴宠状态条的判定结果被覆盖", "PetNest/PetNestCompanionHudView.cs",
         r"(bool visible = !BossRushUI\.IsOfficialHudHidden\(\) && !BossRushUI\.IsGamePaused\(\);)",
         r"\1 visible = true;"),
        ("⑥ 随机事件徽章的判定挪进事件切换分支", "RandomEvents/RandomEventHud.cs",
         r"bool visible = !BossRushUI\.IsOfficialHudHidden\(\) && !BossRushUI\.IsGamePaused\(\);",
         "bool visible = true; if (_shownId != activeId) { visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused(); }"),
    ]
    for description, rel, pattern, replacement in semantic:
        overrides = mutate(rel, pattern, replacement)
        if overrides is not None:
            probes += 1
            expect_red(description, overrides)

    probes += 1
    fake = dict(layer_files)
    fake["NewMode/NewModeHud.cs"] = 'var c = BossRushUI.CreateCanvasRoot("X", BossRushUILayers.HudOverlay, false);'
    expect_red("新加一块没登记的 HudOverlay 画布", layers=fake)
    probes += 1
    fake = dict(layer_files)
    fake["NewMode/NewModePanelHud.cs"] = 'var c = BossRushUI.CreateCanvasRoot("X", BossRushUILayers.Panel, false);'
    expect_red("⑦ 新加一块用 Panel 层、没登记的画布", layers=fake)
    probes += 1
    fake = dict(canvas_files)
    fake["NewMode/NewModeLiteralHud.cs"] = ("internal sealed class NewHud : MonoBehaviour { private Canvas hudCanvas; "
                                            "void Awake() { hudCanvas = gameObject.AddComponent<Canvas>(); hudCanvas.sortingOrder = 1200; } }")
    expect_red("⑤ 新 HUD 给画布写字面量层级 1200", canvases=fake)
    probes += 1
    fake = dict(canvas_files)
    fake["NewMode/NewModeRootHud.cs"] = 'var c = BossRushUI.CreateCanvasRoot("X", 1200, false);'
    expect_red("⑤' CreateCanvasRoot 传字面量层级", canvases=fake)
    probes += 1
    fake = dict(ongui_files)
    fake["NewMode/NewModeGuiHud.cs"] = 'internal sealed class G : MonoBehaviour { void OnGUI() { GUI.Label(new Rect(0, 0, 100, 20), "x"); } }'
    expect_red("新加一块没登记的 OnGUI", onguis=fake)

    if errors:
        print("PersistentHudVisibilityGuard: FAIL")
        for error in errors:
            print("  - " + error)
        return 1
    print("PersistentHudVisibilityGuard: PASS（%d 块常驻 HUD 经过 IsOfficialHudHidden + IsGamePaused，判定语义与宿主驱动逐条核对；"
          "%d 个层级文件已归类（排除 %d / 模态 %d）；字面量层级与 OnGUI 已扫描；%d 个反向检查）"
          % (len(PERSISTENT), len(layer_files), len(EXCLUDED), len(MODAL), probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
