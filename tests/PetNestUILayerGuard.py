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
- 页面组装层（PetNestUIPages.cs）不创建 canvas、不碰 sortingOrder。
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

    # 页面组装层不碰 canvas
    for forbidden in ["CreateCanvasRoot", "sortingOrder", "new GameObject(", "AddComponent<Canvas>"]:
        if forbidden in code:
            errors.append("[分层] 页面组装层不得创建 canvas / 碰 sortingOrder: " + forbidden)

    # 单向数据流的三个数据类
    for cls in ["PetNestPageContent", "PetNestCardData", "PetNestActionData"]:
        if "internal sealed class " + cls not in code:
            errors.append("[单向数据流] 缺少数据类: " + cls)

    # 四页齐全
    for builder in ["BuildNestPage", "BuildHatchPage", "BuildExpeditionPage", "BuildMuseumPage"]:
        if builder not in code:
            errors.append("[页面] 缺少构建器: " + builder)

    # 失败原因必须落到玩家可读文案，DescribeFailure 不能是死代码
    if "NoteFailure(" not in code:
        errors.append("[反馈] 按钮回调不得丢弃 failureReasonId")
    if "PetNestLocalization.DescribeFailure(" not in code:
        errors.append("[反馈] 失败原因必须经 DescribeFailure 转成玩家可读文案")
    if re.search(r"string reason;\s*\n\s*PetNest\w+\.(?:Try\w+|ClearDeployedPet)\([^;]*out reason\);", code):
        errors.append("[反馈] 存在直接丢弃 out reason 的调用")

    # 远征出发页必须明示死亡率
    depart = re.search(r"private static void AppendDepartActions\([\s\S]{0,2400}?\n        \}", code)
    if depart is None:
        errors.append("[明示] 缺少 AppendDepartActions")
    else:
        body = depart.group(0)
        if 'T("DeathRateLabel")' not in body or "FormatPercent(deathRate)" not in body:
            errors.append("[明示] 远征出发按钮必须写明死亡率（赌的知情权是底线）")

    # 纪念碑必须刻风险档位
    memorial = re.search(r"private static void AppendMemorialCards\([\s\S]{0,1600}?\n        \}", code)
    if memorial is not None and "DescribeRisk(m.riskTier)" not in memorial.group(0):
        errors.append("[纪念碑] 碑文必须刻风险档位")


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


def main():
    errors = []
    check_layers(errors)
    check_panel(errors)
    check_pages(errors)
    check_no_magic_numbers(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
