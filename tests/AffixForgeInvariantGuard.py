"""Guard: 词缀锻造的门控、互斥与保命不变式。

背景：词缀是「行为型」词条，靠订阅官方静态事件在运行时生效。三条最容易出事的线：

1. AGENTS.md 4.12 门控。词缀行为只允许在主玩家**实际手持/穿戴**时激活。
   官方 Effect 挂件做不到这点（Trigger 注册到持有者，NPC 手持同样触发），所以这里
   走集中式静态服务：context 只由主角专属事件重建，context 全空就立刻退订。
   一旦退订漏掉，仓库里、背包里、NPC 身上的同名武器都会开始结算词缀。

2. 与重铸的前缀互斥。词缀身份写在物品 KV 的 AFX_ 前缀上，必须被
   ReforgeSystem.IsRuntimeTrackingVariableKey 挡住，否则重铸会把词缀当成
   普通属性 roll 掉，或被属性锁定 UI 列出来。

3. 死契（DeathPact）的持续流失。致死路径上 Health.OnDead 先于 OnHurt 派发，
   所以流失绝不能走 Hurt()——那会先触发一轮击杀词缀再把玩家打死。必须直接
   写 CurrentHealth 并 clamp 在 1 点血。这是逆鳞留下的教训。

守卫内容：
  1. AFX_ 前缀已进重铸黑名单咽喉点。
  2. AffixRuntimeService 具备 EnsureRuntime / ShutdownRuntime / ResetStaticCaches 三件套。
  3. 静态事件订阅与退订**逐事件成对**，且用命名方法而非 lambda（AGENTS.md 4.6）。
  4. 12 条词缀 id 与定义表一一对应，本地化按 GetAll() 全量生成（AGENTS.md 4.4）。
  5. 死契流失方法内不得出现 Hurt(，且必须有 1 点血的 clamp。
  6. AffixForge 目录零新增 Harmony patch。
"""

from pathlib import Path
import re
import sys

AFFIX_DIR = Path("Integration/AffixForge")
SERVICE = AFFIX_DIR / "AffixRuntimeService.cs"
EFFECTS = AFFIX_DIR / "AffixRuntimeService_Effects.cs"
TICKER = AFFIX_DIR / "AffixRuntimeTicker.cs"
DEFS = AFFIX_DIR / "AffixDefinitions.cs"
REFORGE = Path("Integration/Reforge/ReforgeSystem.cs")
LOCALIZATION = Path("Localization/AffixForgeLocalization.cs")

PAIRED_EVENTS = (
    "Health.OnHurt",
    "Health.OnDead",
    "OnMainCharacterChangeHoldItemAgentEvent",
    "OnMainCharacterSlotContentChangedEvent",
)

EXPECTED_AFFIX_COUNT = 12


def fail(message):
    print("AffixForgeInvariantGuard: FAIL - " + message)
    return 1


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def main():
    for path in (SERVICE, DEFS, REFORGE, LOCALIZATION):
        if not path.is_file():
            return fail("找不到 " + path.as_posix())

    # ---- 1) 与重铸的前缀互斥 ----
    reforge = strip_comments(REFORGE.read_text(encoding="utf-8", errors="ignore"))
    chokepoint = re.search(
        r"IsRuntimeTrackingVariableKey\s*\([^)]*\)\s*\{(.*?)\n        \}",
        reforge, flags=re.S)
    if not chokepoint or 'StartsWith("AFX_"' not in chokepoint.group(1):
        return fail(
            "ReforgeSystem.IsRuntimeTrackingVariableKey 没有挡住 AFX_ 前缀。"
            "词缀 KV 会被重铸 roll 掉或被属性锁定 UI 列出来。")

    # ---- 2) 运行时服务三件套 ----
    service = strip_comments(SERVICE.read_text(encoding="utf-8", errors="ignore"))
    for name in ("EnsureRuntime", "ShutdownRuntime", "ResetStaticCaches"):
        if not re.search(r"static\s+void\s+" + name + r"\s*\(", service):
            return fail(SERVICE.as_posix() + " 缺少 " + name + "()（事件订阅生命周期约定）")

    # ---- 3) 订阅/退订逐事件成对 + 禁 lambda ----
    for event in PAIRED_EVENTS:
        subs = len(re.findall(re.escape(event) + r"\s*\+=", service))
        unsubs = len(re.findall(re.escape(event) + r"\s*-=", service))
        if subs == 0:
            return fail(SERVICE.as_posix() + " 没有订阅 " + event + "，词缀行为不会生效")
        if subs != unsubs:
            return fail(
                SERVICE.as_posix() + " 的 " + event + " 订阅 " + str(subs)
                + " 次但退订 " + str(unsubs) + " 次。不成对就是订阅泄漏："
                "离手/卸下/切图后仓库里的装备也会继续结算词缀（AGENTS.md 4.6/4.12）。")
    if re.search(r"(?:\+=|-=)\s*(?:\([^)]*\)\s*=>|delegate\s*\()", service):
        return fail(
            SERVICE.as_posix() + " 用 lambda 订阅静态事件。lambda 无法对称退订，"
            "必须用命名方法（AGENTS.md 4.6）。")

    # ---- 4) 词缀 id 与定义表、本地化一致 ----
    defs = strip_comments(DEFS.read_text(encoding="utf-8", errors="ignore"))
    ids = re.findall(r"public\s+const\s+string\s+(Id_[A-Za-z0-9_]+)\s*=", defs)
    if len(ids) != EXPECTED_AFFIX_COUNT:
        return fail(
            "词缀 id 常量有 " + str(len(ids)) + " 个，预期 " + str(EXPECTED_AFFIX_COUNT)
            + " 条（普通5 + 稀有4 + 诅咒3）")
    for affix_id in ids:
        if defs.count(affix_id) < 2:
            return fail("词缀 id " + affix_id + " 只声明未进定义表，锻造时永远抽不到它")
    localization = LOCALIZATION.read_text(encoding="utf-8", errors="ignore")
    if "GetAll()" not in localization:
        return fail(
            LOCALIZATION.as_posix() + " 没有按 AffixDefinitions.GetAll() 全量生成键。"
            "逐条手写会漏，漏掉的词缀会在装备详情页露出原始 key（AGENTS.md 4.4）。")

    # ---- 5) 死契保命 ----
    # 只看流失方法本身：荆棘反弹与灌能补伤在同一文件里合法调用 Hurt()，
    # 真正不能碰 Hurt() 的是死契那条每帧扣血路径。
    drain_sources = [q for q in (TICKER, EFFECTS) if q.is_file()]
    if not drain_sources:
        return fail("找不到死契流失的实现文件")
    drain_all = strip_comments(
        "\n".join(q.read_text(encoding="utf-8", errors="ignore") for q in drain_sources))
    body = re.search(r"TickDrainCore\s*\([^)]*\)\s*\{(.*?)\n        \}", drain_all, flags=re.S)
    if not body:
        return fail("找不到死契流失方法 TickDrainCore，无法校验保命逻辑")
    drain = body.group(1)
    if re.search(r"\.Hurt\s*\(", drain):
        return fail(
            "死契流失路径出现了 Hurt( 调用。致死时 Health.OnDead 先于 OnHurt 派发，"
            "走 Hurt 会先触发一轮击杀词缀再把玩家打死。必须直接写 CurrentHealth。")
    # clamp 允许两种写法：字面 Mathf.Max(1, ...) 或具名下限常量。
    # 用常量时额外校验它真的 >= 1——写成 0 就等于允许死契直接把玩家耗死。
    floor_const = re.search(r"DrainFloorHealth\s*=\s*([0-9.]+)f?", service)
    has_literal_clamp = re.search(r"Mathf\.Max\s*\(\s*1", drain) is not None
    has_named_clamp = "DrainFloorHealth" in drain and floor_const is not None
    if not (has_literal_clamp or has_named_clamp):
        return fail("死契流失路径缺少血量下限 clamp。诅咒词缀的代价不该是直接猝死。")
    if has_named_clamp and float(floor_const.group(1)) < 1.0:
        return fail(
            "死契血量下限 DrainFloorHealth=" + floor_const.group(1)
            + "，小于 1。下限必须 >= 1，否则死契会把玩家耗死。")

    # ---- 6) 零新增 Harmony ----
    for path in AFFIX_DIR.rglob("*.cs"):
        if "[HarmonyPatch" in path.read_text(encoding="utf-8", errors="ignore"):
            return fail(path.as_posix() + " 新增了 Harmony patch，本系统要求零新增")

    print("AffixForgeInvariantGuard: PASS（" + str(len(ids))
          + " 条词缀，订阅成对，AFX_ 互斥，死契保命）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
