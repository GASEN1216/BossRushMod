#!/usr/bin/env python3
"""
ManagedBossSpawnOwnershipGuard — 托管 Boss 所有权守卫（规格 §20 第 14 条）。

不变式：
- public 生成器签名/时序不变：三个 Boss 的
  public async UniTask<CharacterMainControl> SpawnXxx 原样保留（null/Legacy 走原 body）；
- 每个 adapter 提供 internal PrepareManagedXxxAsync 返回 ManagedBossPrepareResult；
- Create → 冻结 → Configure 硬顺序（适配实际符号：factory 返回后先
  RegisterStagingBoss 登记 exact 身份，再 SetInvincible(true) + SetActive(false) 冻结，
  冻结失败即 return null；规格中的 FreezeReturnedCharacter 以实现等价物表达）；
- Prepare/Activate 分离：Prepare 体内不得调用 handle.Activate/ActivateOnce
  （激活统一由 RuntimeModule 批量触发）；
- 失败路径幂等清理：catch 分支清理托管 Character；handle CleanupOnce 幂等。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

BOSS_FILES = {
    "Integration/DragonDescendant/DragonDescendantBoss.cs": r"public async UniTask<CharacterMainControl> SpawnDragonDescendant",
    "Integration/DragonKing/DragonKingBoss.cs": r"public async UniTask<CharacterMainControl> SpawnDragonKing",
    "Integration/PhantomWitch/PhantomWitchBoss.cs": r"public async UniTask<CharacterMainControl> SpawnPhantomWitch",
}
ADAPTERS = {
    "Integration/DragonDescendant/DragonDescendantBoss_ModeGAdapter.cs":
        "PrepareManagedDragonDescendantAsync",
    "Integration/DragonKing/DragonKingBoss_ModeGAdapter.cs":
        "PrepareManagedDragonKingAsync",
    "Integration/PhantomWitch/PhantomWitchBoss_ModeGAdapter.cs":
        "PrepareManagedPhantomWitchAsync",
}
CONTRACTS = os.path.join(REPO_ROOT, "Utilities", "ManagedBossSpawnContracts.cs")


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.relpath(path, REPO_ROOT))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    errors = []

    # 1. public 签名保留
    for rel, pattern in BOSS_FILES.items():
        content = read(os.path.join(REPO_ROOT, rel.replace("/", os.sep)), errors)
        if content and not re.search(pattern, content):
            errors.append("[PublicSignature] {} 丢失 public Spawn 签名".format(rel))

    # 2. adapter PrepareManaged 签名
    adapter_contents = {}
    for rel, method in ADAPTERS.items():
        content = read(os.path.join(REPO_ROOT, rel.replace("/", os.sep)), errors)
        adapter_contents[rel] = content
        if content:
            if not re.search(
                    r"internal async UniTask<ManagedBossPrepareResult> " + method, content):
                errors.append("[PrepareSignature] {} 缺少 {}".format(rel, method))
            # Prepare/Activate 分离：Prepare 体不得调用激活
            code = re.sub(r"//[^\n]*", "", content)
            if re.search(r"handle\.ActivateOnce\(\)|handle\.Activate\(\)", code):
                errors.append("[PrepareActivateSeparation] {} 在 Prepare 内调用激活".format(rel))
            # 失败路径清理
            if "catch" in code and "CleanupModeGManagedCharacter" not in code \
                    and "CleanupModeGManagedDragonKing" not in code:
                errors.append("[FailureCleanup] {} catch 分支缺少托管清理".format(rel))

    # 3. Create → 冻结 → Configure 硬顺序（共享 helper 位于龙裔 adapter）
    dd_adapter = adapter_contents.get(
        "Integration/DragonDescendant/DragonDescendantBoss_ModeGAdapter.cs", "")
    if dd_adapter:
        create_body = dd_adapter
        i_register = create_body.find("stagingBossRegistered = state.RegisterStagingBoss(character.Health, character);")
        i_invincible = create_body.find("character.Health.SetInvincible(true);")
        i_deactivate = create_body.find("character.gameObject.SetActive(false);")
        if -1 in (i_register, i_invincible, i_deactivate):
            errors.append("[FreezeSequence] Create→冻结序列符号缺失")
        elif not (i_register < i_invincible < i_deactivate):
            errors.append("[FreezeSequence] 必须先登记 exact 身份再冻结（RegisterStagingBoss -> "
                          "SetInvincible(true) -> SetActive(false)）")
        if not re.search(
                r"if \(character\.gameObject\.activeSelf \|\| !character\.Health\.Invincible\) return null;",
                create_body):
            errors.append("[FreezeVerify] 冻结后未回验 activeSelf/Invincible")

    # 4. contracts：CleanupOnce/ActivateOnce 幂等 + 八开关
    contracts = read(CONTRACTS, errors)
    if contracts:
        checks = [
            ("CleanupOnce", r"public void CleanupOnce\(ManagedBossCleanupReason reason\)",
             "CleanupOnce 幂等清理入口"),
            ("ActivateOnce", r"public bool ActivateOnce\(\)", "ActivateOnce 幂等激活入口"),
            ("CleanupIdempotentFlag", r"_cleanupInvoked", "cleanup 一次性标记"),
            ("EightLegacySwitches", r"public bool WriteStandardWaveState = true;"
             r"[\s\S]*?public bool ShowLegacyMessages = true;", "八个 Legacy 行为开关默认全开"),
            ("ModeGPrimaryAllOff",
             r"ctx\.WriteStandardWaveState = false;[\s\S]*?ctx\.ShowLegacyMessages = false;",
             "CreateModeGPrimary 八开关全关"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, contracts):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if errors:
        print("ManagedBossSpawnOwnershipGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ManagedBossSpawnOwnershipGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
