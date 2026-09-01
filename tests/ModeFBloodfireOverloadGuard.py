"""Guard: Mode F bloodfire overload must preserve its approved risk/reward contract."""

from pathlib import Path
import sys


MODELS = Path("ModeF/ModeFModels.cs")
PHASES = Path("ModeF/ModeFPhases.cs")
BOUNTY = Path("ModeF/ModeFBounty.cs")
UI = Path("ModeF/ModeFUI.cs")
BLOODFIRE = Path("ModeF/ModeFBloodfire.cs")
REWARD_BUBBLE = Path("ModeF/ModeFUI_KillRewardBubble.cs")


def fail(message: str) -> int:
    print("ModeFBloodfireOverloadGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : index + 1]

    return None


def require(text: str, needle: str, message: str) -> int | None:
    if needle not in text:
        return fail(message)
    return None


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    phases = PHASES.read_text(encoding="utf-8")
    bounty = BOUNTY.read_text(encoding="utf-8")
    ui = UI.read_text(encoding="utf-8")
    # 命火过载的常量与流程住在 ModeFBloodfire.cs，击杀气泡文本住在 ModeFUI_KillRewardBubble.cs。
    # 契约按“阶段 + 命火”两个文件合并断言：拆分文件可以，削弱任何一条契约不行。
    phases = phases + "\n" + BLOODFIRE.read_text(encoding="utf-8")
    reward_bubble = REWARD_BUBBLE.read_text(encoding="utf-8-sig")

    constants = (
        ("private const float MODEF_HEAL_NORMAL_KILL = 0.30f;", "normal kill heal must remain 30% of entry max health"),
        ("private const float MODEF_HEAL_BOUNTY_KILL = 0.45f;", "bounty heal must start at 45% of entry max health"),
        ("private const float MODEF_HEAL_BOUNTY_PER_EXTRA_MARK = 0.05f;", "each extra bounty mark must add 5% entry-max healing"),
        ("private const float MODEF_HEAL_BOUNTY_MAX = 0.60f;", "bounty healing must cap at 60%"),
        ("private const float MODEF_MAX_HP_GROWTH_RATIO_NORMAL = 0.04f;", "kill growth must stay a 4% share of entry max health"),
        ("private const float MODEF_MAX_HP_GROWTH_CAP_RATIO = 0.50f;", "max-health growth must cap at +50% of entry max"),
        ("private const float MODEF_BLOODFIRE_MAX_CHARGE = 100f;", "bloodfire charge cap must remain 100"),
        ("private const float MODEF_BLOODFIRE_OVERLOAD_DURATION = 15f;", "overload base duration must remain 15 seconds"),
        ("private const float MODEF_BLOODFIRE_BOUNTY_EXTENSION = 3f;", "bounty kill extension must remain 3 seconds"),
        ("private const float MODEF_BLOODFIRE_OVERLOAD_MAX_REMAINING = 24f;", "overload remaining time must cap at 24 seconds"),
        ("private const float MODEF_BLOODFIRE_AFTERGLOW_CHARGE = 25f;", "completed overload must retain 25 charge"),
        ("private const float MODEF_BLOODFIRE_GUN_DAMAGE_BONUS = 0.40f;", "gun bonus must remain 40%"),
        ("private const float MODEF_BLOODFIRE_MELEE_DAMAGE_BONUS = 0.40f;", "melee bonus must remain 40%"),
        ("private const float MODEF_BLOODFIRE_MOVE_SPEED_BONUS = 0.15f;", "move-speed bonus must remain 15%"),
        ("private const float MODEF_BLOODFIRE_BLEED_MULTIPLIER = 2f;", "overload bleed multiplier must remain x2"),
    )
    for needle, message in constants:
        result = require(phases, needle, message)
        if result is not None:
            return result

    for needle in (
        "public float BloodfireCharge;",
        "public bool BloodfireOverloadActive;",
        "public float BloodfireOverloadRemaining;",
        "BloodfireCharge = 0f;",
        "BloodfireOverloadActive = false;",
        "BloodfireOverloadRemaining = 0f;",
    ):
        result = require(models, needle, "ModeFState missing bloodfire state/reset -> " + needle)
        if result is not None:
            return result

    reward = extract_method_body(phases, "ApplyModeFKillReward(bool isBountyBoss, int victimMarks)")
    if reward is None:
        return fail("missing ApplyModeFKillReward")
    for needle, message in (
        ("float healAmount = initialMaxHealth * healPercent;", "healing must use entry max health instead of growing max health"),
        ("MODEF_HEAL_BOUNTY_KILL + extraMarks * MODEF_HEAL_BOUNTY_PER_EXTRA_MARK", "bounty marks must scale healing"),
        ("float growthUnit = initialMaxHealth * MODEF_MAX_HP_GROWTH_RATIO_NORMAL;", "kill growth must be measured against entry max health, not a flat hit point"),
        ("float growthCap = Mathf.Max(1f, initialMaxHealth * MODEF_MAX_HP_GROWTH_CAP_RATIO);", "max-health cap must derive from entry max health"),
        ("float overflowGrowth = Mathf.Max(0f, growthReward - appliedGrowth);", "growth above the cap must become overflow"),
        ("ApplyModeFBloodfireKillReward(", "overflow must feed the bloodfire reward path"),
    ):
        result = require(reward, needle, message)
        if result is not None:
            return result
    if "health.MaxHealth * healPercent" in reward:
        return fail("kill healing must not scale from the growing current maximum")

    bloodfire_reward = extract_method_body(phases, "private float ApplyModeFBloodfireKillReward(")
    if bloodfire_reward is None:
        return fail("missing bloodfire reward conversion")
    for needle, message in (
        ("overflowGrowth / growthCap * MODEF_BLOODFIRE_MAX_CHARGE", "overflow-to-charge conversion must normalize against the +50% health cap"),
        ("MODEF_BLOODFIRE_OVERLOAD_MAX_REMAINING", "bounty extension must respect the 24 second cap"),
        ("previousRemaining + MODEF_BLOODFIRE_BOUNTY_EXTENSION", "bounty kills must extend active overload"),
        ("overloadStarted = StartModeFBloodfireOverload(player);", "full charge must start overload"),
    ):
        result = require(bloodfire_reward, needle, message)
        if result is not None:
            return result

    start = extract_method_body(phases, "private bool StartModeFBloodfireOverload(")
    if start is None:
        return fail("missing StartModeFBloodfireOverload")
    for needle, message in (
        ('"GunDamageMultiplier", MODEF_BLOODFIRE_GUN_DAMAGE_BONUS', "overload must boost gun damage"),
        ('"MeleeDamageMultiplier", MODEF_BLOODFIRE_MELEE_DAMAGE_BONUS', "overload must boost melee damage"),
        # 官方角色只有 WalkSpeed / RunSpeed / Moveability 三个移动 stat；
        # "MoveSpeed" 是 Animator 参数名，挂上去会被当作缺失 stat 静默丢弃。
        ('"WalkSpeed", MODEF_BLOODFIRE_MOVE_SPEED_BONUS', "overload must boost walk speed"),
        ('"RunSpeed", MODEF_BLOODFIRE_MOVE_SPEED_BONUS', "overload must boost run speed"),
        ("GameplayDataSettings.Buffs.Burn", "overload must use the official Burn Buff"),
        ("player.AddBuff(burnBuff, player, 0);", "overload must apply Burn to the player"),
    ):
        result = require(start, needle, message)
        if result is not None:
            return result

    # 回归护栏：AddModeFBloodfireOverloadModifier 只能挂官方真实存在的 stat。
    # 历史 bug 是挂了 "MoveSpeed"（Animator 参数名），modifier 被静默丢弃。
    if 'AddModeFBloodfireOverloadModifier(player, "MoveSpeed"' in start:
        return fail('"MoveSpeed" is not a character stat key; use WalkSpeed/RunSpeed/Moveability')

    bleed = extract_method_body(phases, "private void ApplyModeFBleedDamage(")
    if bleed is None or "rate *= MODEF_BLOODFIRE_BLEED_MULTIPLIER;" not in bleed:
        return fail("active overload must double Mode F bleed")

    end = extract_method_body(phases, "private void EndModeFBloodfireOverload(")
    if end is None:
        return fail("missing overload end cleanup")
    for needle in (
        "MODEF_BLOODFIRE_AFTERGLOW_CHARGE",
        "ClearModeFBloodfireOverloadModifiers();",
    ):
        result = require(end, needle, "overload end missing cleanup/afterglow -> " + needle)
        if result is not None:
            return result

    exit_body = extract_method_body(phases, "private void ExitModeF(bool showEndMessage = true)")
    if exit_body is None or "EndModeFBloodfireOverload(false);" not in exit_body:
        return fail("ExitModeF must clear overload state and modifiers")

    for needle, message in (
        ("killReward.bloodfireGain", "bounty flow must forward bloodfire gain to the player bubble"),
        ("killReward.overloadStarted", "bounty flow must forward overload-start state"),
        ("killReward.overloadExtension", "bounty flow must forward overload extension"),
        ("killReward.overloadStarted ? 4f : 2.5f", "overload entry bubble must stay visible for four seconds"),
    ):
        result = require(bounty, needle, message)
        if result is not None:
            return result

    for needle, message in (
        ("if (overloadStarted)", "reward bubble must prioritize overload entry"),
        ("命火过载！", "reward bubble must display the Chinese overload warning"),
        ("Bloodfire Overload!", "reward bubble must display the English overload warning"),
        ("已被烧伤", "overload warning must mention the applied burn"),
        ("if (overloadExtension >= 0.5f)", "bounty extension must only be reported when it rounds to a visible second"),
        ("else if (bloodfireGain > 0.01f)", "reward bubble must report ordinary bloodfire gain"),
    ):
        result = require(reward_bubble, needle, message)
        if result is not None:
            return result

    for needle, message in (
        ("private string BuildModeFBloodfireStatusText()", "phase broadcast must expose bloodfire status"),
        ("modeFState.BloodfireOverloadRemaining", "phase broadcast must expose overload remaining time"),
        ("modeFState.BloodfireCharge", "phase broadcast must expose charge outside overload"),
        ("DialogueBubblesManager.Show(text, player.transform, duration", "bloodfire warning must use the existing player-head bubble path"),
    ):
        result = require(ui, needle, message)
        if result is not None:
            return result

    print("ModeFBloodfireOverloadGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
