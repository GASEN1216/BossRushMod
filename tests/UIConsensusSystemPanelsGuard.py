"""系统 / 集成类界面对照「UI 制作共识」的结构不变式（2026-09-24 对照审查 A-08…A-43，重铸与日报另见各自守卫）。

守卫的是结构（不代替实机看图，也不证明手感）：
  - 许愿台：主按钮在最右、常驻提醒用次色、0 字时写出「至少 N 字」、兜底奖励名双语（A-08 / A-10…A-12）；
  - 图鉴：筛选是「全部 / 待收集」两颗分段（共享选中态），未解锁卡只留剪影与名字（A-18 / A-19）；
  - 成就：已领取不留按钮、没有可领就不挂「一键领取」、可领取排最前、累计进度是 Filled 进度条（A-21…A-24）；
  - Boss 池：×、ESC、Ctrl+F10、「保存并关闭」走同一条先保存再关，0 个启用时不存不关，
    两个页签，「全部恢复默认」先确认，主操作 AccentFill（A-26…A-30）；
  - 百科：外链不进分类列表（有页眉按钮时）、空正文双语、条目名缩字下限 14、打开淡入（A-31…A-34）；
  - 好感度面板死代码已删（A-35）；词条详情能点击固定（A-36）；
  - 通用确认框：标题由调用方给、不可逆时换 Danger 实心键并拉开间距（A-37 / A-38），扫箱确认键写价钱（A-39）；
  - 成就 / 图鉴 / Boss 池 / 百科走模态租约 ClaimModalInput，不再直接 InputManager.DisableInput（A-43），
    成就与图鉴的 ESC 用掉官方取消事件（PetNestCancelKey），不再 Input.GetKeyDown(Escape)。
"""
from pathlib import Path
import re
import sys

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(rel):
    return clean_source((ROOT / rel).read_text(encoding="utf-8-sig"))


def body(text, signature):
    start = text.index(signature)
    begin = text.index("{", start)
    depth, end = 1, begin + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[begin:end]


def check_wish(errors):
    ui = read("Integration/WishFountain/WishFountainUI.cs")
    feel = read("Integration/WishFountain/WishFountainUI_Feel.cs")
    layout = body(ui, "private void BuildLayout(RectTransform rootRect)")
    cancel = layout.find("cancelButton = CreateButton(")
    confirm = layout.find("confirmButton = CreateButton(")
    if cancel < 0 or confirm < 0 or cancel > confirm:
        errors.append("许愿台：主操作「许愿」必须排在「取消」右边（先建取消、再建许愿，A-08）")
    sizes = set(re.findall(r"private const float (\w+_FONT_SIZE) = ", ui))
    if len(sizes) > 4:
        errors.append("许愿台：字号常量超过四级（A-09）: %s" % sorted(sizes))
    if re.search(r"CreateText\([^;]*?,\s*\d+\s*,\s*FontStyles", layout):
        errors.append("许愿台：又写回了字面量字号（A-09）")
    visual = body(feel, "private void ApplyInputVisualState(bool inputFocused)")
    if "inputFocusHintText.color = BossRushUIColors.WarningText" in visual:
        errors.append("许愿台：常驻提醒又用了警告色（A-10）")
    state = body(ui, "private void RefreshUIState()")
    if "charCount == 0" not in state or "MIN_CHARS" not in state[state.index("charCount == 0"):]:
        errors.append("许愿台：0 字时必须写出「至少 N 个字」（A-11）")
    anim = read("Integration/WishFountain/WishFountainRewardAnimationView.cs")
    if '"Unknown Reward"' in anim:
        errors.append("许愿台：兜底奖励名写死了英文（A-12）")


def check_codex(errors):
    view = read("Integration/Codex/CodexView.cs")
    grid = read("Integration/Codex/CodexView_Grid.cs")
    progress = body(view, "private void UpdateProgress(CodexData data)")
    for token in ("IntegrationUIFeedback.StyleSegment(_filterAllButton, !_onlyMissing);",
                  "IntegrationUIFeedback.StyleSegment(_filterMissingButton, _onlyMissing);"):
        if token not in progress:
            errors.append("图鉴：筛选必须是「全部 / 待收集」两颗分段、选中态走共享口径（A-18）")
            break
    if "ToggleMissingFilter" in view + grid:
        errors.append("图鉴：又回到改文案的单颗筛选开关（A-18）")
    card = body(grid, "private GameObject CreateCardFor(CodexBossInfo info, CodexEntry entry, Transform parent)")
    locked_return = card.find("if (locked)")
    kills = card.find('"Kills"')
    if locked_return < 0 or kills < 0 or locked_return > kills or "未记录" in card:
        errors.append("图鉴：未解锁卡只留剪影与名字，不再写「未记录」/「—」占位行（A-19）")
    check_modal_lease(errors, "图鉴", view, "ClaimModalInput(gameObject, \"Codex\")", cancel_key=True)


def check_achievement(errors):
    entry = read("Achievement/AchievementEntryUI.cs")
    view = read("Achievement/AchievementView.cs")
    visuals = body(entry, "private void UpdateVisuals()")
    claimed = visuals[visuals.index("case AchievementEntryState.UnlockedClaimed:"):]
    claimed = claimed[:claimed.index("break;")]
    if "SetStatusSlot(false, true, false);" not in claimed or "claimButton.interactable = false" in claimed:
        errors.append("成就：已领取不留灰按钮，改写「√ 已领取」（A-21）")
    buttons = body(view, "private void UpdateClaimAllButton()")
    if "claimAllButton.gameObject.SetActive(hasClaimable)" not in buttons:
        errors.append("成就：没有可领的就不挂「一键领取」（A-22）")
    populate = body(view, "private void PopulateEntries()")
    claimable = populate.find("if (aClaimable && !bClaimable) return -1;")
    unlocked = populate.find("if (aUnlocked && !bUnlocked) return -1;")
    if claimable < 0 or unlocked < 0 or claimable > unlocked:
        errors.append("成就：可领取的必须排在最前（A-23）")
    bar = body(entry, "private void CreateSlotProgressBar(Transform parent)")
    if "progressFill.sprite = BossRushUI.GetSolidSprite();" not in bar or "Image.Type.Filled" not in bar:
        errors.append("成就：累计进度必须是配纯色 sprite 的 Filled 进度条（A-24）")
    if re.search(r'baseDesc \+ " \(" \+', entry):
        errors.append("成就：累计进度又括回了描述末尾（A-24）")
    check_modal_lease(errors, "成就", view, "ClaimModalInput(gameObject, \"Achievement\")", cancel_key=True)


def check_boss_pool(errors):
    core = read("BossFilter/BossFilter.cs")
    ui = read("BossFilter/BossFilterUi.cs")
    save_close = body(core, "private void SaveAndCloseBossPoolWindow()")
    if "!bossEnabledStates.ContainsValue(true)" not in save_close or save_close.find("return;") > save_close.find(
            "SyncBossPoolToConfig();"):
        errors.append("Boss 池：一个都没启用时必须先拒绝，再保存（A-30）")
    if save_close.find("SyncBossPoolToConfig();") > save_close.find("CloseBossPoolWindow();"):
        errors.append("Boss 池：必须先保存再关（A-27）")
    title = body(ui, "private void CreateTitleBar(Transform parent)")
    hotkey = body(core, "private void CheckBossPoolWindowHotkey()")
    bottom = body(core, "private void CreateBottomButtons(Transform parent)")
    open_window = body(core, "public void OpenBossPoolWindow()")
    for name, text in (("×", title), ("Ctrl+F10", hotkey), ("保存并关闭", bottom)):
        if "SaveAndCloseBossPoolWindow" not in text or "CloseBossPoolWindow()" in text.replace("SaveAndCloseBossPoolWindow()", ""):
            errors.append("Boss 池：%s 必须走 SaveAndCloseBossPoolWindow（A-27）" % name)
    if "PetNestCancelKey.Attach(bossPoolCanvas, SaveAndCloseBossPoolWindow" not in open_window:
        errors.append("Boss 池：ESC / 手柄取消必须接上并走保存关闭（A-26）")
    if "BossRushUIColors.AccentFill" not in bottom:
        errors.append("Boss 池：「保存并关闭」是这屏唯一主操作，用 AccentFill（A-29）")
    toolbar = body(ui, "private void CreateToolbar(Transform parent)")
    for token in ("ShowBossPoolTab(false)", "ShowBossPoolTab(true)", "RequestResetAllBossFactors",
                  "IntegrationUIFeedback.StyleDangerSecondary(resetFactorsButton);"):
        if token not in toolbar:
            errors.append("Boss 池：工具栏缺页签 / 危险次级的「全部恢复默认」: " + token)
    reset = body(core, "private void RequestResetAllBossFactors()")
    if "BossRushConfirmDialog.Show(" not in reset or "OnConfirm = ResetAllBossFactors" not in reset:
        errors.append("Boss 池：「全部恢复默认」必须先确认（A-28）")
    if len(re.findall(r"\bResetAllBossFactors\(\)", core)) > 1:
        errors.append("Boss 池：ResetAllBossFactors 只能由确认回调触发（A-28）")
    check_modal_lease(errors, "Boss 池", core, "ClaimModalInput(bossPoolCanvas, \"BossPool\")", cancel_key=False)
    release = body(ui, "private void ReleaseBossPoolUIReferences()")
    if "bossPoolModalLease.Release()" not in release:
        errors.append("Boss 池：关闭与卸载共用的 ReleaseBossPoolUIReferences 必须归还模态租约（A-43）")


def check_wiki(errors):
    wiki = read("Integration/WikiUIManager.cs")
    categories = body(wiki, "private void RefreshCategoryList()")
    if "category.Id == ExternalWikiCategoryId && btnOnlineWiki != null" not in categories:
        errors.append("百科：外链有页眉按钮时不得再进分类列表（A-31）")
    if "SetActive(showIndex)" not in body(wiki, "private void SetPageVisibility(bool showIndex, bool showArticle)"):
        errors.append("百科：「在线 Wiki」只在目录页显示（A-31）")
    if "[内容为空]" in wiki:
        errors.append("百科：空正文提示只有中文（A-32）")
    if "text.fontSizeMin = 10f" in wiki:
        errors.append("百科：条目名缩字下限又回到 10（A-33）")
    if "BossRush.PlayOpenAnimation(uiRoot)" not in wiki.replace("BossRushUI.", "BossRush.") \
            or "FadeOutAndDeactivate" not in body(wiki, "public void CloseUI()"):
        errors.append("百科：打开淡入 / 关闭淡出缺失（A-34）")
    check_modal_lease(errors, "百科", wiki, "ClaimModalInput(uiRoot, \"Wiki\")", cancel_key=False)


def check_misc(errors):
    affinity = read("Integration/Affinity/AffinityUIManager.cs")
    for dead in ("ShowAffinityPanel", "CreateAffinityPanel", "UpdateAffinityDisplay", "HideAffinityPanel"):
        if dead in affinity:
            errors.append("好感：从未被打开的好感度面板又回来了（A-35）: " + dead)
    mutator = read("Integration/Mutators/MutatorUI.cs")
    hover = body(mutator, "private static void AttachHoverHandler(GameObject row, int index)")
    if "EventTriggerType.PointerClick" not in hover or "TogglePinned(" not in hover:
        errors.append("词条：详情必须能点击固定，不能只靠悬停（A-36）")
    adapter = read("Integration/NPCs/Courier/OriginalConfirmDialogueAdapter.cs")
    if '"扫箱确认"' in adapter:
        errors.append("通用确认框：标题又写死成「扫箱确认」（A-38）")
    execute = body(adapter, "public static UniTask<OriginalConfirmDialogueResult> Execute(")
    if "ExecuteCore(title," not in execute:
        errors.append("通用确认框：Execute 必须由调用方给标题（A-38）")
    show = body(adapter, "private static void ShowDialog(string title, string message, string confirmText, string cancelText)")
    for token in ("dangerConfirmButton.gameObject.SetActive(destructive)", "confirmButton.gameObject.SetActive(!destructive)",
                  "DestructiveFooterSpacing"):
        if token not in show:
            errors.append("通用确认框：不可逆时确认键要换 Danger 实心并与取消拉开（A-37）: " + token)
    if "BossRushUIColors.Danger," not in body(adapter, "private static void CreateFooter(Transform parent, Button buttonPrefab)"):
        errors.append("通用确认框：缺 Danger 实心的危险确认键（A-37）")
    sweep = read("Integration/NPCs/Courier/CourierPaidLootSweepService.cs")
    prompt = body(sweep, "private static bool ShowFreshSweepPrompt(Transform npcTransform, List<PaidSweepBoxPlan> plans, int cost)")
    if "string confirmText = " not in prompt or "price" not in prompt[prompt.index("string confirmText = "):]:
        errors.append("扫箱：确认键必须写价钱（A-39）")


def check_modal_lease(errors, name, text, claim_token, cancel_key):
    """A-43：模态租约替代裸 InputManager.DisableInput；自处理 ESC 的要用掉官方取消事件。"""
    if claim_token not in text:
        errors.append("%s：打开时必须占模态租约 %s（A-43）" % (name, claim_token))
    if "InputManager.DisableInput(" in text or "InputManager.ActiveInput(" in text:
        errors.append("%s：不得再直接 InputManager.DisableInput / ActiveInput（A-43）" % name)
    if cancel_key:
        if "PetNestCancelKey.Attach(" not in text:
            errors.append("%s：ESC 必须走 PetNestCancelKey 并用掉官方取消事件（A-43）" % name)
        if "KeyCode.Escape" in text:
            errors.append("%s：ESC 又回到 Input.GetKeyDown，官方暂停菜单会一起弹出（A-43）" % name)


def main():
    errors = []
    for check in (check_wish, check_codex, check_achievement, check_boss_pool, check_wiki, check_misc):
        try:
            check(errors)
        except ValueError as error:
            errors.append("%s：锚点丢失（%s）" % (check.__name__, error))
    if errors:
        for error in errors:
            print("[FAIL] " + error)
        print("UIConsensusSystemPanelsGuard: FAIL")
        return 1
    print("UIConsensusSystemPanelsGuard: PASS（许愿台 / 图鉴 / 成就 / Boss 池 / 百科 / 好感 / 词条 / 通用确认框 / 扫箱）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
