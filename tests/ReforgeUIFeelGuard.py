"""重铸 / 词缀锻造界面的反馈层结构不变式（2026-09-23 审美审查 UD-23…UD-30）。

守卫的是结构，不代替实机看图：
  1. 玩家可见的富文本颜色一律走 token（IntegrationUIFeedback.*Hex）：ReforgeUIManager*.cs 的字符串字面量里
     不再出现 `#RRGGBB`，也不再写 Color.white / Color.gray 一类纯色（UD-23 / UD-25 / UD-26）。
  2. 费用区不再把算式原样写给玩家（「0.20×a×b+c」那一行），UpdateProbabilityDisplay 交给 RenderReforgeCostText；
     钱不够时 UpdateReforgeButtonInteractable 不再整段刷红，而是回到同一个渲染入口（UD-23）。
  3. 属性行固定是两步：OnPointerClick 先走 BeginPropertyLockPending（第一次点直接 return），
     确认之后才 ConsumeItem；待确认在清理属性交互时撤回；清理时还原数值色，不再统一刷成 Color.white（UD-24）。
  4. 重铸揭晓挂在详情面板重建之后：RefreshUIAfterReforgeDelayed 在 AddPropertyLockIcons 之后调 PlayQueuedReforgeReveal；
     ShowPropertyChanges 为每条变化登记揭晓（UD-25）。
  5. 关闭界面的 Cleanup 调 CleanupReforgeFeel（撤回待确认、拆掉贴在官方对象池条目上的表现层）。
  6. 图标取不到时不再退回「◇」「◆」字符（UD-30）。
"""
from pathlib import Path
import re
import sys

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
REFORGE_DIR = ROOT / "Integration/Reforge"
MAIN = REFORGE_DIR / "ReforgeUIManager.cs"
STATE = REFORGE_DIR / "ReforgeUIManager_ComparisonAndState.cs"
RUNTIME = REFORGE_DIR / "ReforgeUIManager_RuntimeAndCleanup.cs"
FEEL = REFORGE_DIR / "ReforgeUIManager_Feel.cs"
FORGE = REFORGE_DIR / "ReforgeUIManager_AffixForge.cs"
PANEL = REFORGE_DIR / "ReforgeUIManager_AffixForgePanel.cs"

HEX_IN_STRING = re.compile(r'"[^"\n]*#[0-9A-Fa-f]{6}[^"\n]*"')
PURE_COLORS = re.compile(r"\bColor\.(white|gray|grey|red|green|yellow|cyan|magenta)\b")


def read(path):
    return clean_source(path.read_text(encoding="utf-8-sig"))


def method_body(text, signature):
    start = text.index(signature)
    begin = text.index("{", start)
    depth, end = 1, begin + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[begin:end]


def main():
    errors = []
    sources = {path: read(path) for path in sorted(REFORGE_DIR.glob("ReforgeUIManager*.cs"))}
    if FEEL not in sources:
        errors.append("缺少 ReforgeUIManager_Feel.cs（反馈层）")

    # 1. token 颜色
    for path, text in sources.items():
        for match in HEX_IN_STRING.finditer(text):
            errors.append("%s: 字符串里写死了颜色 %s（改用 IntegrationUIFeedback.*Hex）" % (path.name, match.group(0)[:60]))
        for match in PURE_COLORS.finditer(text):
            errors.append("%s: 使用了纯色 %s（改用 BossRushUIColors token）" % (path.name, match.group(0)))

    main_text = sources.get(MAIN, "")
    state = sources.get(STATE, "")
    runtime = sources.get(RUNTIME, "")
    feel = sources.get(FEEL, "")
    forge = sources.get(FORGE, "")
    panel = sources.get(PANEL, "")

    # 2. 费用区
    if "0.20×" in state or "0.20×" in feel:
        errors.append("费用区又把「0.20×a×b+c」算式写给玩家看了")
    probability = method_body(state, "private static void UpdateProbabilityDisplay()")
    if "RenderReforgeCostText();" not in probability:
        errors.append("UpdateProbabilityDisplay 必须交给 RenderReforgeCostText 渲染")
    affordability = method_body(state, "private static void UpdateReforgeButtonInteractable()")
    if "probabilityText.text" in affordability:
        errors.append("UpdateReforgeButtonInteractable 不能再整段改写费用区（钱不够只染总计那一行）")
    render = method_body(feel, "private static void RenderReforgeCostText()") if feel else ""
    if "shortfall" not in render or "IntegrationUIFeedback.DangerHex" not in render:
        errors.append("RenderReforgeCostText 必须在钱不够时只把总计那一行染成 DangerHex 并写还差多少")

    # 3. 两步固定
    click = method_body(main_text, "public void OnPointerClick(PointerEventData eventData)")
    begin = click.find("ReforgeUIManager.BeginPropertyLockPending(this);")
    consume = click.find("ItemFactory.ConsumeItem(")
    if begin < 0 or consume < 0 or begin > consume:
        errors.append("OnPointerClick 必须先进入待确认（BeginPropertyLockPending），确认之后才 ConsumeItem")
    else:
        # 紧跟在 BeginPropertyLockPending 后面的那一句必须是 return（中间别的 return 不算）
        if not re.search(r"ReforgeUIManager\.BeginPropertyLockPending\(this\);\s*return;", click) \
                or "ReforgeUIManager.IsPropertyLockPending(this)" not in click[:begin]:
            errors.append("第一次点击必须只进入待确认并 return，不能落到扣费")
    clear = method_body(runtime, "private static void ClearPropertyLockIcons()")
    if "CancelPendingPropertyLock();" not in clear:
        errors.append("ClearPropertyLockIcons 必须撤回待确认（切换物品 / 关闭界面时清掉二次确认计时）")
    if "ReleaseVisuals();" not in clear:
        errors.append("ClearPropertyLockIcons 必须还原官方数值色（ReleaseVisuals），不能统一刷色")
    if "PROPERTY_LOCK_CONFIRM_SECONDS" not in feel or "WaitForSecondsRealtime(PROPERTY_LOCK_CONFIRM_SECONDS)" not in feel:
        errors.append("待确认必须有 PROPERTY_LOCK_CONFIRM_SECONDS 的实时超时")

    # 4. 揭晓挂在重建之后
    refresh = method_body(runtime, "private static System.Collections.IEnumerator RefreshUIAfterReforgeDelayed()")
    add = refresh.find("AddPropertyLockIcons();")
    reveal = refresh.find("PlayQueuedReforgeReveal();")
    if add < 0 or reveal < 0 or reveal < add:
        errors.append("RefreshUIAfterReforgeDelayed 必须在 AddPropertyLockIcons 之后调 PlayQueuedReforgeReveal")
    if "QueueReforgeReveal(" not in method_body(state, "private static void ShowPropertyChanges()"):
        errors.append("ShowPropertyChanges 必须为每条变化登记揭晓（QueueReforgeReveal）")
    if "PlayAffixRollReveal();" not in method_body(forge, "internal static bool AffixForge_HandleButtonClick()"):
        errors.append("随机词缀成功后必须播词缀揭晓（PlayAffixRollReveal）")

    # 5. 清理
    if "CleanupReforgeFeel();" not in method_body(runtime, "public static void Cleanup()"):
        errors.append("Cleanup 必须调 CleanupReforgeFeel（拆掉贴在官方对象池条目上的表现层）")

    # 6. 字符占位图标
    for name, text in (("ReforgeUIManager_RuntimeAndCleanup.cs", runtime), ("ReforgeUIManager_AffixForgePanel.cs", panel)):
        if '"◇"' in text or '"◆"' in text:
            errors.append("%s: 图标又退回「◇/◆」字符占位（取不到就不画那一格）" % name)

    if errors:
        for error in errors:
            print("[FAIL] " + error)
        print("ReforgeUIFeelGuard: FAIL")
        return 1
    print("ReforgeUIFeelGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
