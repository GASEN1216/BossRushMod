"""Guard: ModConfig option-change handling stays factored and complete."""

from pathlib import Path
import re
import sys


SOURCE = Path("Config/Config.cs")
CONFIG_DIR = Path("Config")
# IsHandledModConfigOptionKey 从 Config.cs 提取到了这里（同一 partial 类，
# 拆分只为 LargeFileBudgetGuard 的 1200 行预算）。两处都找，避免将来再挪时守卫瞎掉。
HANDLED_KEY_SOURCES = [SOURCE, Path("Config/ConfigModConfigKeys.cs")]

# Config.cs 里直接以字面量分发的老键：白名单与单键 loader 两处都要有。
CONFIG_KEYS = [
    "_waveIntervalSeconds",
    "_EnableRandomBossLoot",
    "_UseLegacyBossLootProbabilities",
    "_UseInteractBetweenWaves",
    "_LootBoxBlocksBullets",
    "_InfiniteHellBossesPerWave",
    "_BossStatMultiplier",
    "_milestoneRestBonusSeconds",
    "_EnableDragonDash",
    "_UseWolfModelForWildHorn",
    "_EnableDeathWraithSystem",
    "_EnableMutators",
    "_MutatorCount",
    "_ModeDEnemiesPerWave",
    "_AchievementHotkey",
    "_ModeHEnabled",
]

# 子系统自带 KeySuffix 常量的键由下面的扫描自动发现，不再手工登记——
# 手工登记正是当初漏掉 PetNest / 图鉴 / 词缀锻造 / 随机事件的原因。
# 这里只钉住“扫描至少应该发现这些”，防止正则失效后守卫变成空转。
EXPECTED_DELEGATED_KEYS = {
    "_RandomEventsFrequency",
    "_ModeGAbandonHotkey",
}

# 内容系统总开关（owner 2026-08-30 定）：属于默认内容，恒为开启。
# 既不许注册进 ModConfig UI，默认值也不许是 false，
# 且必须被 ForceContentSystemSwitchesOn 抹平历史残留的 false。
# 键名 -> BossRushConfig 字段名
CONTENT_SYSTEM_SWITCHES = {
    "_PetNestEnabled": "petNestEnabled",
    "_DailyReportEnabled": "dailyReportEnabled",
    "_CodexEnabled": "codexEnabled",
    "_AffixForgeEnabled": "affixForgeEnabled",
    "_RandomEventsEnabled": "randomEventsEnabled",
    "_CampaignEnabled": "campaignEnabled",
    "_BackMountainEnabled": "backMountainEnabled",
}

KEY_CONST_RE = re.compile(r'private const string (\w*ModConfigKeySuffix)\s*=\s*"([^"]+)"\s*;')
REGISTER_RE = re.compile(r'private void (Register\w+)\s*\(')
LOADER_RE = re.compile(r'private bool (TryLoad\w*SingleModConfigValue)\s*\(')


def fail(message: str) -> int:
    print("ModConfigOptionChangeGuard: FAIL - " + message)
    return 1


def extract_method(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace_start = text.find("{", start)
    if brace_start < 0:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def scan_delegated_keys(config_text: str):
    """扫出所有“已经接进 ModConfig UI”的子系统开关键。

    判定活跃的口径：某个 Config/ConfigXxx.cs 里的 Register 方法引用了该 KeySuffix
    常量，且这个 Register 方法在 Config.cs 里被调用了。只有活跃键才参与断言——
    在建但还没接线的系统（Register 方法尚未被调用）不该卡住别的会话。
    """
    found = []
    for source in sorted(CONFIG_DIR.glob("Config*.cs")):
        if source.name == "Config.cs":
            continue
        text = source.read_text(encoding="utf-8")
        consts = KEY_CONST_RE.findall(text)
        if not consts:
            continue

        loaders = LOADER_RE.findall(text)
        for register_name in REGISTER_RE.findall(text):
            if (register_name + "(") not in config_text:
                continue
            body = extract_method(text, "private void " + register_name)
            for const_name, key in consts:
                if const_name in body:
                    found.append((const_name, key, source, loaders))
    return found


def check_default_is_true(all_text, key, field):
    """字段默认值必须是 true，允许经由 `= XxxDefaultEnabled` 这类常量间接指定。"""
    match = re.search(r'public bool ' + re.escape(field) + r'\s*=\s*([^;]+);', all_text)
    if not match:
        return "content system switch field not found: public bool " + field
    value = match.group(1).strip()
    if value == "true":
        return None
    if re.search(r'private const bool ' + re.escape(value) + r'\s*=\s*true\s*;', all_text):
        return None
    return ("content system switch must default to true: " + field + " = " + value +
            "（" + key + " 属于默认内容，默认值不许是 false）")


def check_content_system_switches(scanned_keys):
    """内容系统总开关：不注册进 UI、默认 true、且被强制拉回 true。"""
    all_text = "\n".join(
        path.read_text(encoding="utf-8") for path in sorted(CONFIG_DIR.glob("Config*.cs")))

    for key, field in sorted(CONTENT_SYSTEM_SWITCHES.items()):
        if key in scanned_keys:
            return ("content system switch re-exposed in ModConfig UI: " + key +
                    " - 它属于默认内容，不该注册；若确要放出，请同时更新 "
                    "CONTENT_SYSTEM_SWITCHES 与 Config/ConfigContentSystemSwitches.cs")

        problem = check_default_is_true(all_text, key, field)
        if problem:
            return problem

    force = extract_method(all_text, "private void ForceContentSystemSwitchesOn()")
    if not force:
        return "missing method: ForceContentSystemSwitchesOn"
    for key, field in sorted(CONTENT_SYSTEM_SWITCHES.items()):
        if ("config." + field + " = true;") not in force:
            return ("ForceContentSystemSwitchesOn does not force " + field +
                    "：老版本存下的 false 会把玩家永久关在 " + key + " 外面")

    load_from_file = extract_method(SOURCE.read_text(encoding="utf-8"),
                                    "private void LoadConfigFromFile()")
    if not load_from_file:
        return "missing method: LoadConfigFromFile"
    if "ForceContentSystemSwitchesOn();" not in load_from_file:
        return "LoadConfigFromFile must call ForceContentSystemSwitchesOn();"
    return None


def main() -> int:
    if not SOURCE.exists():
        return fail("missing Config.cs")

    text = SOURCE.read_text(encoding="utf-8")
    on_changed = extract_method(text, "private void OnModConfigOptionsChanged(string changedKey)")
    handled = ""
    for handled_source in HANDLED_KEY_SOURCES:
        if not handled_source.exists():
            continue
        handled = extract_method(
            handled_source.read_text(encoding="utf-8"),
            "private bool IsHandledModConfigOptionKey(string changedKey)")
        if handled:
            break
    log_change = extract_method(text, "private void LogModConfigOptionChanged(string changedKey)")
    post_change = extract_method(text, "private void ApplyPostModConfigOptionChange(string changedKey)")
    loader = extract_method(text, "private bool TryLoadSingleModConfigValue(string changedKey)")

    for method, label in [
        (on_changed, "OnModConfigOptionsChanged"),
        (handled, "IsHandledModConfigOptionKey"),
        (log_change, "LogModConfigOptionChanged"),
        (post_change, "ApplyPostModConfigOptionChange"),
        (loader, "TryLoadSingleModConfigValue"),
    ]:
        if not method:
            return fail("missing method: " + label)

    for snippet in [
        "if (!IsHandledModConfigOptionKey(changedKey))",
        "LogModConfigOptionChanged(changedKey);",
        "if (TryLoadSingleModConfigValue(changedKey))",
        "ApplyPostModConfigOptionChange(changedKey);",
        "StartNextWaveCountdown(false, true);",
    ]:
        if snippet not in on_changed:
            return fail("OnModConfigOptionsChanged missing token: " + snippet)

    if "changedKey == waveKey ||" in on_changed:
        return fail("OnModConfigOptionsChanged regressed to a giant key OR-chain")

    for key in CONFIG_KEYS:
        token = 'ModName + "' + key + '"'
        if token not in handled:
            return fail("handled-key helper missing: " + token)
        if token not in loader:
            return fail("loader missing: " + token)

    delegated = scan_delegated_keys(text)
    scanned_keys = set(key for _, key, _, _ in delegated)
    missing_scan = EXPECTED_DELEGATED_KEYS - scanned_keys
    if missing_scan:
        return fail(
            "delegated-key scan went blind (regex or wiring changed?), "
            "did not rediscover: " + ", ".join(sorted(missing_scan)))

    for const_name, key, source, loaders in delegated:
        # 注册进 UI 的键必须能被 OnModConfigOptionsChanged 的前置过滤放行，
        # 否则玩家在 ModConfig 里拨动开关不会即时生效。
        token = "ModName + " + const_name
        if token not in handled:
            return fail(
                "registered ModConfig key not whitelisted: " + key + " (" +
                str(source).replace("\\", "/") + "); add `changedKey == " +
                token + "` to IsHandledModConfigOptionKey")

        if not loaders:
            return fail("no TryLoad...SingleModConfigValue in " + str(source).replace("\\", "/"))
        if not any(("if (" + name + "(changedKey, loadMethod)) return true;") in loader
                   for name in loaders):
            return fail(
                "TryLoadSingleModConfigValue does not delegate to any of " +
                ", ".join(loaders) + " for key " + key)

    problem = check_content_system_switches(scanned_keys)
    if problem:
        return fail(problem)

    for snippet in [
        'ModName + "_EnableRandomBossLoot"',
        'ModName + "_UseLegacyBossLootProbabilities"',
        "RefreshBossRushLootboxPathTrackingForTrackedBosses();",
        'ModName + "_EnableDeathWraithSystem"',
        "HandleDeathWraithConfigChanged_DeathWraith();",
        'ModName + "_InfiniteHellBossesPerWave"',
        "bossesPerWave = config.infiniteHellBossesPerWave;",
    ]:
        if snippet not in post_change:
            return fail("post-change helper missing: " + snippet)

    print("ModConfigOptionChangeGuard: PASS (exposed delegated keys: " +
          str(len(scanned_keys)) + ", content systems forced on: " +
          str(len(CONTENT_SYSTEM_SWITCHES)) + ")")
    return 0


if __name__ == "__main__":
    sys.exit(main())
