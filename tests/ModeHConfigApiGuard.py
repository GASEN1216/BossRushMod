#!/usr/bin/env python3
"""
ModeHConfigApiGuard — Mode H 配置与运行时门守卫（设计提案 §24.1、§22.1、§26.1）。

不变式：
- ModBehaviour.BossRushConfig **只**新增 modeHEnabled 一个字段；
- ModBehaviour 只新增 IsModeHConfiguredEnabled() 一个只读 getter，默认 false；
- ModConfig 镜像键固定为 BossRush_ModeHEnabled，且加载顺序为
  文件 -> ModConfig 覆盖 -> 回写；变更键必须进入 IsHandledModConfigOptionKey 白名单；
- ModeHRuntimeGates 暴露且只暴露五个 no-throw 只读结果；
- 编译期开发门 AllowDevRawPngFallback / AllowDevControlPointHarness 恒为 false；
- 全仓不得出现 modeHRealWarehouseStakeEnabled / IsModeHRealWarehouseStakeConfiguredEnabled /
  ModeHStakeJournal.GatePassed 三个符号（§22.1 明确禁止重新引入真实资产开关）；
- 不得存在第二个静态 Config 源，ModeHConfig 不得声明可写 Enabled。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from modeh_guard_util import read_text, strip_cs_comments  # noqa: E402

CONFIG_CS = os.path.join(REPO_ROOT, "Config", "Config.cs")
GATES = os.path.join(REPO_ROOT, "ModeH", "ModeHRuntimeGates.cs")
AVAILABILITY = os.path.join(REPO_ROOT, "ModeH", "ModeHAvailability.cs")
MODEH_CONFIG = os.path.join(REPO_ROOT, "ModeH", "ModeHConfig.cs")
MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")

FORBIDDEN_SYMBOLS = [
    "modeHRealWarehouseStakeEnabled",
    "IsModeHRealWarehouseStakeConfiguredEnabled",
    "GatePassed",
]

REQUIRED_GATES = [
    "IsModeHRunOwnerActive",
    "IsModeHRiskScanReady",
    "IsModeHContentReady",
    "IsModeHExternalAssetRiskBlocked",
    "IsModeHRecoveryOnlyBlocked",
]


def main():
    errors = []

    config = read_text(CONFIG_CS)
    if config is None:
        errors.append("[File] 缺少 Config/Config.cs")
    else:
        code = strip_cs_comments(config)
        # 1) 只新增一个字段
        field_matches = re.findall(r"public bool modeH\w*\s*=", code)
        if len(field_matches) != 1:
            errors.append("[Field] BossRushConfig 必须且只能新增一个 modeHEnabled 字段，实际 {} 个".format(
                len(field_matches)))
        if not re.search(r"public bool modeHEnabled = false;", code):
            errors.append("[Field] modeHEnabled 必须存在且默认 false")

        # 2) 只新增一个 getter
        getter_matches = re.findall(r"bool IsModeH\w*ConfiguredEnabled\(\)", code)
        if len(getter_matches) != 1:
            errors.append("[Getter] 必须且只能有一个 IsModeHConfiguredEnabled()，实际 {} 个".format(
                len(getter_matches)))
        if not re.search(r"internal bool IsModeHConfiguredEnabled\(\)", code):
            errors.append("[Getter] IsModeHConfiguredEnabled 必须是 internal 只读 owner getter")
        if not re.search(r"return config != null && config\.modeHEnabled;", code):
            errors.append("[Getter] getter 必须在 config 为 null 时返回 false")

        # 3) ModConfig 接线
        wiring = [
            (r'ModName \+ "_ModeHEnabled"', "ModConfig 镜像键 BossRush_ModeHEnabled"),
            (r"changedKey == ModName \+ \"_ModeHEnabled\"", "变更键进入白名单"),
            (r"addBoolMethod\.Invoke\(null, new object\[\] \{ ModName, modeHKey, modeHLabel, config\.modeHEnabled \}\)",
             "SetupModConfig 注册开关"),
            (r"config\.modeHEnabled = loadedModeH;", "批量加载覆盖文件值"),
            (r"config\.modeHEnabled = \(bool\)modeHResult;", "单键变更加载"),
        ]
        for pattern, desc in wiring:
            if not re.search(pattern, code):
                errors.append("[ModConfig] 不满足: " + desc)

        # 4) 禁止真实资产开关
        for symbol in FORBIDDEN_SYMBOLS:
            if symbol in code:
                errors.append("[Forbidden] Config 中出现被禁止的真实资产开关符号: " + symbol)

    gates = read_text(GATES)
    if gates is None:
        errors.append("[File] 缺少 ModeH/ModeHRuntimeGates.cs")
    else:
        gate_code = strip_cs_comments(gates)
        for gate in REQUIRED_GATES:
            if not re.search(r"public static bool {}\b".format(re.escape(gate)), gate_code):
                errors.append("[Gates] 缺少只读结果: " + gate)
        # 五个结果都必须 no-throw
        for gate in REQUIRED_GATES:
            pattern = r"public static bool {}[\s\S]{{0,260}}?catch \(Exception\)".format(re.escape(gate))
            if not re.search(pattern, gate_code):
                errors.append("[Gates] 结果不是 no-throw: " + gate)
        if not re.search(r"public static void InitializeRiskForSlot\(int slotGeneration\)", gate_code):
            errors.append("[Gates] 缺少 InitializeRiskForSlot")
        if not re.search(r"SavesSystem\.KeyExisits\(ModeHConfig\.StakeJournalStorageKey\)", gate_code):
            errors.append("[Gates] 风险扫描必须先用 KeyExisits 前置分类")
        if re.search(r"Load<ModeHStakeJournalDto>", gate_code):
            errors.append("[Gates] 轻量风险扫描不得加载完整 journal payload")
        if not re.search(r"Load<ModeHStakeJournalHeaderDto>", gate_code):
            errors.append("[Gates] 风险扫描必须只读 envelope header")
        if not re.search(r"public static bool IsLegacyModeEntryAllowed\(\)", gate_code):
            errors.append("[Gates] 缺少旧模式最终入口组合判定")
        legacy = re.search(r"public static bool IsLegacyModeEntryAllowed\(\)[\s\S]{0,240}?\}", gate_code)
        if legacy:
            body = legacy.group(0)
            for forbidden in ["IsModeHContentReady", "IsModeHRecoveryOnlyBlocked", "IsModeHRunOwnerActive"]:
                if forbidden in body:
                    errors.append("[Gates] 旧模式入口不得读取: " + forbidden)

    availability = read_text(AVAILABILITY)
    if availability is None:
        errors.append("[File] 缺少 ModeH/ModeHAvailability.cs")
    else:
        code = strip_cs_comments(availability)
        if not re.search(r"public const bool AllowDevRawPngFallback = false;", code):
            errors.append("[DevGate] AllowDevRawPngFallback 必须是恒 false 的编译期常量")
        if not re.search(r"public const bool AllowDevControlPointHarness = false;", code):
            errors.append("[DevGate] AllowDevControlPointHarness 必须是恒 false 的编译期常量")

    modeh_config = read_text(MODEH_CONFIG)
    if modeh_config:
        code = strip_cs_comments(modeh_config)
        if re.search(r"(public|internal)\s+static\s+bool\s+\w*Enabled", code):
            errors.append("[Config] ModeHConfig 不得声明可写 Enabled")
        if re.search(r"class\s+Config\b", code):
            errors.append("[Config] 不得引入第二个静态 Config 类型")

    # 全仓禁止符号扫描（跳过注释与本 guard 自身）
    if os.path.isdir(MODEH_DIR):
        for name in sorted(os.listdir(MODEH_DIR)):
            if not name.endswith(".cs"):
                continue
            text = read_text(os.path.join(MODEH_DIR, name))
            code = strip_cs_comments(text or "")
            for symbol in FORBIDDEN_SYMBOLS:
                if symbol in code:
                    errors.append("[Forbidden] {} 中出现被禁止的真实资产开关符号: {}".format(name, symbol))

    if errors:
        print("ModeHConfigApiGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeHConfigApiGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
