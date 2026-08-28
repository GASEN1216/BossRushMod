#!/usr/bin/env python3
"""
PetNestHudThrottleGuard — 随从 HUD 节流与零分配守卫（实施计划 步骤 11）。

不变式（AGENTS.md 4.12 + ModeG HUD 先例）：
- 刷新间隔常量绑定 PetNestTuning.HudRefreshIntervalSeconds（0.25s = 4Hz），冻结；
- Update 每帧只做一次 timer 递减，未到间隔必须早返；
- HUD 模型是 **struct**（每帧 new 一个 class 就是每帧一次 GC 分配）；
- 只有模型变化才写 TMP text（TMP 赋值会触发 mesh 重建）；
- 随从不在场时整块隐藏，不做任何组装；
- HUD 与随从同寿命：入场创建、离场销毁，不常驻；
- HUD canvas 不接收点击（raycaster 关掉，否则会挡住局内交互）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import read_petnest, report, strip_cs_comments  # noqa: E402

GUARD = "PetNestHudThrottleGuard"


def main():
    errors = []

    text = read_petnest("PetNestCompanionHudView.cs")
    if text is None:
        return report(GUARD, ["[File] 缺少 PetNest/PetNestCompanionHudView.cs"])
    code = strip_cs_comments(text)

    # 1. 节流常量绑定 Tuning
    if "private const float RefreshIntervalSeconds = PetNestTuning.HudRefreshIntervalSeconds;" not in code:
        errors.append("[节流] 刷新间隔必须绑定 PetNestTuning.HudRefreshIntervalSeconds")

    tuning = read_petnest("PetNestTuning.cs")
    if tuning is not None:
        tcode = strip_cs_comments(tuning)
        if "HudRefreshIntervalSeconds = 0.25f" not in tcode:
            errors.append("[节流] HUD 刷新间隔取值必须冻结为 0.25f（4Hz）")

    # 2. Update 早返
    update = re.search(r"private void Update\(\)[\s\S]{0,600}?\n        \}", code)
    if update is None:
        errors.append("[节流] 缺少 Update()")
    else:
        body = update.group(0)
        if "_refreshTimer -= Time.unscaledDeltaTime;" not in body:
            errors.append("[节流] 每帧只应做一次 timer 递减")
        if "if (_refreshTimer > 0f) return;" not in body:
            errors.append("[节流] 未到间隔必须早返")
        # 早返之前不得做组装
        early_pos = body.find("if (_refreshTimer > 0f) return;")
        build_pos = body.find("BuildHudModel()")
        if early_pos >= 0 and build_pos >= 0 and build_pos < early_pos:
            errors.append("[节流] 组装必须发生在节流早返之后")

    # 3. 模型必须是 struct
    if "internal struct PetNestHudModel" not in code:
        errors.append("[零分配] HUD 模型必须是 struct，不能是 class")
    if re.search(r"internal (sealed )?class PetNestHudModel", code):
        errors.append("[零分配] HUD 模型不得声明为 class")

    # 4. 只有变化才写
    if "if (model.SameAs(_lastModel)) return;" not in code:
        errors.append("[零分配] 模型未变化时必须直接返回，不重写 TMP text")
    if "internal bool SameAs(PetNestHudModel other)" not in code:
        errors.append("[零分配] 缺少值比较入口 SameAs")

    # 5. 不在场整块隐藏
    build = re.search(r"private PetNestHudModel BuildHudModel\(\)[\s\S]{0,1400}?\n        \}", code)
    if build is None:
        errors.append("[隐藏] 缺少 BuildHudModel()")
    else:
        body = build.group(0)
        if "if (!PetNestCompanionRuntime.HasCompanion)" not in body:
            errors.append("[隐藏] 随从不在场时必须直接返回 Visible=false")
    if "_panel.SetActive(model.Visible)" not in code:
        errors.append("[隐藏] 不可见时必须整块 SetActive(false)")

    # 6. 与随从同寿命
    if "internal static void EnsureCreated()" not in code:
        errors.append("[生命周期] 缺少 EnsureCreated()")
    if "internal static void Destroy()" not in code:
        errors.append("[生命周期] 缺少 Destroy()")
    runtime = read_petnest("PetNestCompanionRuntime.cs")
    if runtime is not None:
        rcode = strip_cs_comments(runtime)
        if "PetNestCompanionHudView.EnsureCreated()" not in rcode:
            errors.append("[生命周期] 随从入场必须创建 HUD")
        if "PetNestCompanionHudView.Destroy()" not in rcode:
            errors.append("[生命周期] 随从离场必须销毁 HUD")

    # 7. HUD 不接收点击
    if "BossRushUILayers.PetNestCompanionHud, false)" not in code:
        errors.append("[HUD] canvas 必须以 interactive=false 创建（否则会挡住局内交互）")

    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
