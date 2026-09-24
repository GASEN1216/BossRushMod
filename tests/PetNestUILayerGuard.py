#!/usr/bin/env python3
"""
PetNestUILayerGuard — 遗种巢 UI 层段与共享库守卫（实施计划 步骤 10）。

不变式（AGENTS.md 4.14）：
- 三个层段常量存在且取值冻结：PetNestCompanionHud=990 / PetNestPanel=2100 /
  PetNestModal=3150；
- BossRushUILayers 整表**升序**（跨模式叠加时谁压谁不能靠运气）；
- 遗种巢的 canvas 一律引用 BossRushUILayers 常量，禁裸 sortingOrder 数字；
- Canvas 走 BossRushUI.CreateCanvasRoot（内部已调 ConfigureCanvasScaler），
  不得自己 AddComponent<CanvasScaler> 或写 uiScaleMode；
- 遮罩走 BossRushUI.CreateBackdrop / BossRushUIColors.Backdrop，禁第二套 (0,0,0,0.7)；
- 文本走 TMP + BossRushUI.ApplyGameFont，禁 legacy UI.Text 与内置 Arial；
- 主面板占用唯一模态输入 lease（ZombieModeUIHelper.ClaimModalInput）并成对释放；
- 页面组装层（PetNestUIPages.cs / PetNestUINestPage.cs）与面板的画法文件（PetNestUILayout.cs）
  不创建 canvas、不碰 sortingOrder；
- 亡命档（真死）出发必须先过确认弹窗（2026-09-24 交互重排：放生有确认，真死远征此前点一下就走）；
- 确认一律走共享 BossRushConfirmDialog（AGENTS §4.14「不再各写一份」，2026-09-24 放生 / 亡命出发从自绘框迁来）：
  服务调用只在 OnConfirm 里、Danger 确认、Anchor = 主面板宿主、面板的 ESC 让位给开着的确认框；
  遗种巢目录下不得再出现自绘的 *Confirm* 窗口类。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import (  # noqa: E402
    PETNEST_DIR,
    read_petnest,
    read_text,
    repo_path,
    report,
    strip_cs_comments,
)

GUARD = "PetNestUILayerGuard"

FROZEN_LAYERS = [
    ("PetNestCompanionHud", 990),
    ("PetNestPanel", 2100),
    ("PetNestModal", 3150),
]


def check_layers(errors):
    text = read_text(repo_path("Common", "UI", "BossRushUI.cs"))
    if text is None:
        errors.append("[File] 缺少 Common/UI/BossRushUI.cs")
        return
    code = strip_cs_comments(text)

    for name, value in FROZEN_LAYERS:
        if not re.search(r"internal const int " + name + r" = " + str(value) + r";", code):
            errors.append("[层段] 缺少或改动了冻结取值: " + name + " = " + str(value))

    # 整表升序
    block = re.search(r"internal static class BossRushUILayers[\s\S]*?\n    \}", code)
    if block is None:
        errors.append("[层段] 无法解析 BossRushUILayers")
        return
    entries = re.findall(r"internal const int (\w+) = (\d+);", block.group(0))
    values = [int(v) for _n, v in entries]
    if values != sorted(values):
        errors.append("[层段] BossRushUILayers 必须整表升序，当前顺序: " + repr(entries))


def check_panel(errors):
    text = read_petnest("PetNestUI.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestUI.cs")
        return
    code = strip_cs_comments(text)

    if "BossRushUILayers.PetNestPanel" not in code:
        errors.append("[层段] 主面板必须引用 BossRushUILayers.PetNestPanel")
    if "BossRushUI.CreateCanvasRoot(" not in code:
        errors.append("[共享库] Canvas 必须走 BossRushUI.CreateCanvasRoot")
    if "BossRushUI.CreateBackdrop(" not in code:
        errors.append("[共享库] 遮罩必须走 BossRushUI.CreateBackdrop")
    if "BossRushUI.ApplyPanelSkin(" not in code:
        errors.append("[共享库] 底图必须走 BossRushUI.ApplyPanelSkin")
    if "BossRushUI.ApplyGameFont(" not in code:
        errors.append("[共享库] 字体必须走 BossRushUI.ApplyGameFont")

    # 模态 lease 成对
    if "ZombieModeUIHelper.ClaimModalInput(" not in code:
        errors.append("[模态] 面板必须占用唯一模态输入 lease")
    if "_modalLease.Release();" not in code:
        errors.append("[模态] 模态 lease 必须成对释放")
    if not re.search(r"private void OnDestroy\(\)[\s\S]{0,200}?ReleaseLease\(\)", code):
        errors.append("[模态] OnDestroy 必须兜底释放 lease")

    # 惰性构建 + 关闭即销毁
    if "internal static void Close()" not in code:
        errors.append("[惰性] 缺少 Close()，面板必须关闭即销毁不常驻")

    # 内容区与动作区必须可滚动：巢容量上限 24、远征页 9 个档位按钮、
    # 博物馆的血脉卡 + 碑文都远超一屏。按固定 y 预算铺元素会静默截断——
    # 第 5 只之后的崽、第三个远征目的地、整段纪念碑都会在 UI 上凭空消失。
    if "private static Transform CreateScrollList(" not in code:
        errors.append("[滚动] 内容区必须是滚动列表，不能按固定 y 预算截断")
    if "ScrollRect" not in code or "RectMask2D" not in code:
        errors.append("[滚动] 滚动列表必须有 ScrollRect + RectMask2D")
    if "ContentSizeFitter" not in code or "VerticalLayoutGroup" not in code:
        errors.append("[滚动] 滚动内容必须由布局组 + ContentSizeFitter 自适应高度")
    if re.search(r"y > -240f", code):
        errors.append("[滚动] 不得再按裸 y 坐标预算截断元素")
    if re.search(r"i < actions\.Count && i < \d+", code):
        errors.append("[滚动] 动作按钮不得硬截断（远征页 3 目的地 × 3 档位 = 9 个）")

    # 失败反馈：不给提示的话，巢满 / 写屏障 / 远征锁定在界面上与"点歪了"无法区分
    if "PetNestUIPages.LastFailureText" not in code:
        errors.append("[反馈] 面板必须显示最近一次操作的失败原因")


def check_pages(errors):
    text = read_petnest("PetNestUIPages.cs")
    if text is None:
        errors.append("[File] 缺少 PetNest/PetNestUIPages.cs")
        return
    code = strip_cs_comments(text)

    # 页面组装层与面板画法文件不碰 canvas（画法文件是 PetNestUI 的 partial，只在既有 surface 里摆内容）
    assembly = ["CreateCanvasRoot", "sortingOrder", "new GameObject(", "AddComponent<Canvas>"]
    nest_code = ""
    for name, forbidden_list in (
            ("PetNestUIPages.cs", assembly),
            ("PetNestUINestPage.cs", assembly),
            ("PetNestUILayout.cs", ["CreateCanvasRoot", "sortingOrder", "AddComponent<Canvas>", "BossRushUILayers."])):
        other = read_petnest(name)
        if other is None:
            errors.append("[File] 缺少 PetNest/" + name)
            continue
        other_code = strip_cs_comments(other)
        if name == "PetNestUINestPage.cs":
            nest_code = other_code
        for forbidden in forbidden_list:
            if forbidden in other_code:
                errors.append("[分层] " + name + " 不得创建 canvas / 碰 sortingOrder: " + forbidden)

    # 单向数据流的三个数据类
    for cls in ["PetNestPageContent", "PetNestCardData", "PetNestActionData"]:
        if "internal sealed class " + cls not in code:
            errors.append("[单向数据流] 缺少数据类: " + cls)

    # 四页齐全（巢页的组装在同一个 partial 类的 PetNestUINestPage.cs）
    for builder in ["BuildNestPage", "BuildHatchPage", "BuildExpeditionPage", "BuildMuseumPage"]:
        if builder not in code + nest_code:
            errors.append("[页面] 缺少构建器: " + builder)

    # 失败原因必须落到玩家可读文案，DescribeFailure 不能是死代码
    if "NoteFailure(" not in code:
        errors.append("[反馈] 按钮回调不得丢弃 failureReasonId")
    if "PetNestLocalization.DescribeFailure(" not in code:
        errors.append("[反馈] 失败原因必须经 DescribeFailure 转成玩家可读文案")
    if re.search(r"string (?:reason|failure);\s*\n\s*PetNest\w+\.(?:Try\w+|ClearDeployedPet)\([^;]*out (?:reason|failure)\);",
                 code + nest_code):
        errors.append("[反馈] 存在直接丢弃 out reason 的调用")
    if nest_code and "NoteFailure(PetNestService.TrySetDeployedPet(" not in nest_code:
        errors.append("[反馈] 巢页的出战 / 取消出战必须把失败原因交给 NoteFailure")

    # 远征出发页必须明示死亡率。2026-09-20 改成目的地 -> 风险档两级卡片；
    # 2026-09-24 交互重排改成一屏选完（目的地分段按钮 + 风险档对比卡 + 一个「出发」），断言随结构一起改。
    depart = re.search(r"private static void AppendDepartCards\([\s\S]{0,6000}?\n        \}", code)
    if depart is None:
        errors.append("[明示] 缺少 AppendDepartCards")
    else:
        body = depart.group(0)
        if 'T("DeathRateLabel")' not in body or "FormatPercent(deathRate)" not in body:
            errors.append("[明示] 远征出发卡片必须写明死亡率（赌的知情权是底线）")
        if "FormatDuration(PetNestExpeditionService.GetDurationHours(tier))" not in body:
            errors.append("[明示] 远征出发卡片必须写明时长")
        if "_expeditionDestinationId" not in body or "_expeditionTier" not in body:
            errors.append("[选择] 远征出发必须先选定目的地与风险档，再按「出发」")
        # 真死不可逆：亡命档出发必须先过确认弹窗（PetNestUI.ConfirmDepart → 共享 BossRushConfirmDialog），
        # 不能在按钮回调里直接 TryDepart。确认弹窗那一侧的断言在 check_confirms。
        confirm = re.search(
            r"if \(picked == PetNestRiskTier\.Desperate\)\s*\{\s*PetNestUI\.ConfirmDepart\([^;]*\);\s*return;\s*\}",
            body)
        if confirm is None:
            errors.append("[确认] 亡命档出发必须先弹确认（PetNestUI.ConfirmDepart），确认后才出发")

    # 纪念碑必须刻风险档位
    memorial = re.search(r"private static void AppendMemorialCards\([\s\S]{0,1600}?\n        \}", code)
    if memorial is not None and "DescribeRisk(m.riskTier)" not in memorial.group(0):
        errors.append("[纪念碑] 碑文必须刻风险档位")


def method_body(code, signature):
    """取出 signature 开头的方法体（含外层花括号）；按括号配对，跳过字符串与字符字面量。找不到返回 None。"""
    start = code.find(signature)
    if start < 0:
        return None
    opening = code.find("{", start)
    if opening < 0:
        return None
    depth = 0
    i = opening
    n = len(code)
    while i < n:
        ch = code[i]
        if ch in "\"'":
            quote = ch
            i += 1
            while i < n and code[i] != quote:
                i += 2 if code[i] == "\\" else 1
        elif ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return code[start:i + 1]
        i += 1
    return None


def check_confirm_body(errors, label, body, service_call):
    """一个确认入口：组装共享 Options（Danger）、服务调用只在 OnConfirm 委托里、交给 ShowConfirm。"""
    if body is None:
        errors.append("[确认] 缺少" + label + "的确认入口")
        return
    if "new BossRushConfirmDialog.Options" not in body:
        errors.append("[确认] " + label + "必须组装共享 BossRushConfirmDialog.Options，不得自绘确认框")
    if not re.search(r"\bDanger\s*=\s*true\b", body):
        errors.append("[确认] " + label + "是不可逆操作，确认框必须 Danger = true")
    on_confirm = body.find("OnConfirm = delegate")
    call = body.find(service_call)
    if on_confirm < 0 or call < 0 or call < on_confirm:
        errors.append("[确认] " + label + "的服务调用 " + service_call + " 必须只写在 OnConfirm 委托里（确认后才执行）")
    elif body.count(service_call) != 1:
        errors.append("[确认] " + label + "只能在 OnConfirm 里调一次 " + service_call)
    if on_confirm < 0 or "PetNestUIPages.NoteExternalFailure(" not in body[on_confirm:]:
        errors.append("[反馈] " + label + "确认后的失败原因必须经 PetNestUIPages.NoteExternalFailure 回抛给面板")
    if not re.search(r"\bShowConfirm\(options, refresh\);", body):
        errors.append("[确认] " + label + "必须经 ShowConfirm 打开（统一挂 Anchor 与关闭后刷新）")


def check_confirms(errors):
    """遗种巢的确认一律走共享 BossRushConfirmDialog（AGENTS §4.14）。"""
    text = read_petnest("PetNestUI.cs")
    if text is None:
        return
    code = strip_cs_comments(text)

    release_open = method_body(code, "private void OpenRelease(")
    if release_open is None or not re.search(r"\bConfirmRelease\(petIds, Refresh\);", release_open):
        errors.append("[确认] 巢页的放生（单只 / 批量）必须先弹 ConfirmRelease，确认后才放生")
    check_confirm_body(errors, "放生", method_body(code, "internal static void ConfirmRelease("),
                       "PetNestService.TryReleasePets(ids, out reason)")
    check_confirm_body(errors, "亡命出发", method_body(code, "internal static void ConfirmDepart("),
                       "PetNestUIPages.TryDepartAndNote(petId, destinationId, tier)")

    show = method_body(code, "private static void ShowConfirm(")
    if show is None:
        errors.append("[确认] 缺少 ShowConfirm（遗种巢确认的统一出口）")
    else:
        if "BossRushConfirmDialog.Show(options);" not in show:
            errors.append("[确认] ShowConfirm 必须交给共享 BossRushConfirmDialog.Show")
        if not re.search(r"options\.Anchor\s*=\s*_instance != null \? _instance\.gameObject : null;", show):
            errors.append("[确认] 确认框的 Anchor 必须是主面板宿主（面板关掉时确认框跟着按取消收场）")
        if "options.OnCancel = " not in show:
            errors.append("[确认] 取消后也要刷新面板（原弹窗的 onClosed）")
        if "SortingOrder" in show and "BossRushUILayers.ModalConfirm" not in show:
            errors.append("[层段] 遗种巢确认框必须压在主面板与演出层之上（ModalConfirm），不得传更低的层")

    covered = method_body(code, "private static bool IsCoveredByChildWindow(")
    if covered is None or "BossRushConfirmDialog.IsOpen" not in covered:
        errors.append("[确认] 主面板的 ESC 判据必须在确认框开着时让位（BossRushConfirmDialog.IsOpen）")

    # 不再各写一份：遗种巢目录下不得出现自绘确认窗口类（改名框是输入框，不算确认）
    for name in sorted(os.listdir(PETNEST_DIR)):
        if not name.endswith(".cs"):
            continue
        other = strip_cs_comments(read_text(os.path.join(PETNEST_DIR, name)) or "")
        m = re.search(r"\bclass\s+(\w*Confirm\w*)\b", other)
        if m:
            errors.append("[确认] " + name + " 自绘了确认窗口类 " + m.group(1) + "，确认一律走共享 BossRushConfirmDialog")


def check_no_magic_numbers(errors):
    """遗种巢所有会建 canvas 的文件都必须用层段常量。"""
    for name in sorted(os.listdir(PETNEST_DIR)):
        if not name.endswith(".cs"):
            continue
        text = read_text(os.path.join(PETNEST_DIR, name))
        if text is None:
            continue
        code = strip_cs_comments(text)
        if "CreateCanvasRoot(" not in code:
            continue
        if "BossRushUILayers." not in code:
            errors.append("[层段] " + name + " 必须使用 BossRushUILayers 常量而不是魔法数字")
        for forbidden in ["AddComponent<CanvasScaler>", "uiScaleMode",
                          "new Color(0f, 0f, 0f, 0.7f)",
                          'Resources.GetBuiltinResource<Font>("Arial.ttf")']:
            if forbidden in code:
                errors.append("[共享库] " + name + " 不得出现: " + forbidden)


def check_failure_reasons(errors):
    """断言所有可达的 failureReasonId 都必须在 PetNestLocalization 中注册 Fail_ 键。"""
    loc_text = read_text(repo_path("Localization", "PetNestLocalization.cs"))
    if loc_text is None:
        errors.append("[File] 缺少 Localization/PetNestLocalization.cs")
        return
    loc_code = strip_cs_comments(loc_text)
    registered = set(re.findall(r'Add\(map,\s*"Fail_(\w+)"', loc_code))

    core_reasons = [
        "nest_full", "pet_not_found", "pet_duplicate", "pet_locked_by_expedition",
        "pet_downed", "pet_invalid", "souls_insufficient", "invalid_request",
        "lineage_unknown", "egg_missing", "egg_owner_missing", "egg_detach_failed",
        "roll_failed", "hatch_stats_failed", "add_pet_failed", "destination_unknown",
        "record_missing", "not_settled", "not_due", "depart_failed", "settle_failed",
        "mode_g_banned", "zombie_mode_banned", "mode_h_banned", "no_run_active",
        "mode_query_failed", "pet_downed_this_run", "lineage_preset_missing",
        "companion_handle_invalid", "companion_activate_failed", "save_write_barrier",
        "save_store_faulted", "transaction_missing", "nested_transaction",
        "transaction_clone_failed", "asset_save_not_ready", "commit_failed",
        "remove_pet_failed", "release_pet_failed", "rename_pet_failed",
        "set_deployed_failed", "clear_deployed_failed", "spend_souls_failed",
    ]
    for r in core_reasons:
        if r not in registered:
            errors.append("[本地化覆盖] 缺少核心失败原因本地化键: Fail_" + r)

    pattern = re.compile(r'(?:failureReason(?:Id)?|_lastBlockReasonId)\s*=\s*"([^"]+)"')
    for name in sorted(os.listdir(PETNEST_DIR)):
        if not name.endswith(".cs"):
            continue
        text = read_text(os.path.join(PETNEST_DIR, name))
        if text is None:
            continue
        code = strip_cs_comments(text)
        for m in pattern.finditer(code):
            val = m.group(1)
            if ":" in val:
                val = val[:val.index(":")]
            if val not in registered:
                errors.append("[本地化覆盖] " + name + " 中的受阻/失败原因 '" + val + "' 缺少 Fail_" + val + " 本地化键")


def main():
    errors = []
    check_layers(errors)
    check_panel(errors)
    check_pages(errors)
    check_confirms(errors)
    check_no_magic_numbers(errors)
    check_failure_reasons(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
