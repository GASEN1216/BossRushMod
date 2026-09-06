#!/usr/bin/env python3
"""
ModeGSpawnTransactionGuard — Mode G 生成事务守卫（规格 §20 第 13 条）。

不变式：
- lease/nonce 分层：SpawnAttemptLease + CommitJournalEntry DTO；
  每槽至多 MaxAttemptsPerSlot=2 次尝试；已提交槽拒绝再 lease；
- spawnLeasesInvalidated 后全部 lease fail-closed（单向故障）；
- TryCommit 顺序：先 RegisterTrackedBoss 再 ResolveSlotOnce，失败回滚登记；
- Mode G 固定 options：HoldForExternalCommit=true、ApplySharedMutators=false、
  AllowRandomRetryFallback=false（official/managed 两路均冻结）；
- 唯一改变 Health.Hurt 准入/事务深度的补丁仍是 BossRushHealthHurtContextPatch；
  仅额外允许精确路径/类名的 SetBonusDamageObservation 只读贡献观察补丁，
  不允许第二个事务补丁、第三个 Hurt 补丁或借观察补丁改写伤害/返回值；
- __state 配对：BossLethalHealthProtectionPatch Prefix ref bool __state 与
  Finalizer bool __state 成对。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRANSACTION = os.path.join(REPO_ROOT, "ModeG", "ModeGSpawnTransaction.cs")
HURT_PATCH = os.path.join(REPO_ROOT, "Patches", "Combat", "BossLethalHealthProtectionPatch.cs")
HURT_PATCH_ATTR = "[HarmonyPatch(typeof(Health), nameof(Health.Hurt))]"
HURT_OWNERS = {
    "Patches/Combat/BossLethalHealthProtectionPatch.cs": "BossRushHealthHurtContextPatch",
    "Integration/Bonus/SetBonusDamageObservation.cs": "SetBonusDamageObservation",
}
HURT_PATCH_RE = re.compile(
    r'\[HarmonyPatch\s*\(\s*typeof\s*\(\s*(?:global::)?Health\s*\)\s*,\s*'
    r'(?:nameof\s*\(\s*(?:global::)?Health\.Hurt\s*\)|"Hurt")(?=\s*[,\)])')


def read(path, errors):
    if not os.path.exists(path):
        errors.append("文件不存在: " + os.path.basename(path))
        return ""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


# 非生产源码目录：构建产物、工具过程材料、官方反编译源码与守卫自身。
# 与 tests/EmptyCatchGuard.py 的 EXCLUDE_DIRS 同一口径。
# 不排除的话，Build/ 下的构建产物或外部工具落的源码快照
# （例如 Build/review-<commit>/tracked/... ）会被当成第二处 patch 误报。
HURT_SCAN_EXCLUDE_DIRS = {
    "Build", "output", "tmp", ".codex_tmp", ".git", ".kiro",
    "docs", "tests", "wiki-site", "鸭科夫源码",
}


def code_without_comments(text):
    """保留字符串（包括 Harmony 的 \"Hurt\" 形式），只屏蔽注释。"""
    tokens = r'(@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\')|//[^\n]*|/\*.*?\*/'
    return re.sub(tokens, lambda m: m.group(1) or " " * len(m.group()), text, flags=re.S)


def collect_hurt_patch_sources(errors, repo_root=REPO_ROOT):
    """按生产文件收集每个直接 Health.Hurt patch，不把构建副本算成新补丁。"""
    sources = {}
    for dirpath, dirnames, filenames in os.walk(repo_root):
        # 就地裁剪，避免走进构建产物与官方源码目录
        dirnames[:] = [d for d in dirnames if d not in HURT_SCAN_EXCLUDE_DIRS]
        if os.sep + "tests" in dirpath or dirpath.endswith("tests"):
            continue
        for f in filenames:
            if not f.endswith(".cs"):
                continue
            try:
                path = os.path.join(dirpath, f)
                with open(path, "r", encoding="utf-8", errors="replace") as fh:
                    text = fh.read()
                if HURT_PATCH_RE.search(code_without_comments(text)):
                    sources[os.path.relpath(path, repo_root).replace(os.sep, "/")] = text
            except OSError as exc:
                errors.append("[HurtPatchRead] 无法读取生产源码: " + str(exc))
    return sources


def validate_hurt_patch_owners(sources, errors):
    """允许的是两个不同职责的精确身份，绝不是把总数预算由 1 放宽成 2。"""
    for path in sorted(set(sources) - set(HURT_OWNERS)):
        errors.append("[HurtPatchOwner] 未授权的 Health.Hurt patch: " + path)
    for path, owner in HURT_OWNERS.items():
        text = code_without_comments(sources.get(path, ""))
        count = len(HURT_PATCH_RE.findall(text))
        # 每个路径都必须且只能有自己的一个直接补丁；同文件复制第二个类也必须失败。
        expected = re.escape(HURT_PATCH_ATTR) + r"\s*internal\s+static\s+class\s+" + owner + r"\b"
        if count != 1 or not re.search(expected, text):
            errors.append("[HurtPatchOwner] {} 必须只声明精确补丁 {}（当前 {} 处）".format(path, owner, count))

    observer = code_without_comments(sources.get("Integration/Bonus/SetBonusDamageObservation.cs", ""))
    if not observer:
        return
    for signature in (
        "private static void Begin(Health __instance, DamageInfo damageInfo, out Observation __state)",
        "private static Exception End(Exception __exception, Observation __state)",
        "private static void Observe(Health target, ElementTypes element, float damage)",
        "current = __state.Parent;",
        "return __exception;",
    ):
        if signature not in observer:
            errors.append("[ReadOnlyHurtObserver] 缺少只读/异常清理契约: " + signature)
    for kind in ("Prefix", "Transpiler", "Finalizer"):
        if len(re.findall(r"\[Harmony" + kind + r"\]", observer)) != 1:
            errors.append("[ReadOnlyHurtObserver] 必须唯一声明 Harmony" + kind)
    forbidden = (
        r"\b__result\b", r"\bref\s+(?:DamageInfo|Health)\b", r"\[HarmonyPostfix\]",
        r"\.(?:Hurt|SetHealth|AddHealth|SetItemAndCharacter)\s*\(",
        r"\b(?:target|health|__instance|info|damageInfo)\.\w+\s*(?:[+*/-]?=(?!=)|\+\+|--)",
        r"\b(?:ModeGRuntimeGates|ReverseScaleAbilityManager)\b",
    )
    for pattern in forbidden:
        if re.search(pattern, observer):
            errors.append("[ReadOnlyHurtObserver] 观察补丁不得修改伤害、生命、准入或返回值: " + pattern)
    # Transpiler 只在唯一匹配点加观察调用；禁止原地改 opcode/操作数或移除原伤害指令。
    start = observer.find("private static IEnumerable<CodeInstruction> Transpiler(")
    end = observer.find("internal static bool TryFindObservationPoint(", start)
    transpiler = observer[start:end] if start >= 0 and end > start else ""
    required = ("codes.InsertRange(sumAt, injected);", "TryFindObservationPoint(codes, out elementCall, out sumAt, out failure)",
                "new CodeInstruction(OpCodes.Ldarg_0)", "nameof(Observe)")
    if not all(fragment in transpiler for fragment in required):
        errors.append("[ReadOnlyHurtObserver] Transpiler 必须沿唯一官方累加点插入 Observe")
    if re.search(r"\.opcode\s*=(?!=)|\.operand\s*=(?!=)|codes\.(?:Remove\w*|Clear)\s*\(|codes\[[^]]+\]\s*=(?!=)", transpiler):
        errors.append("[ReadOnlyHurtObserver] Transpiler 不得删除或改写原 IL")


def main():
    errors = []
    tx = read(TRANSACTION, errors)
    patch = read(HURT_PATCH, errors)

    if tx:
        checks = [
            ("LeaseDto", r"public struct SpawnAttemptLease", "lease DTO 存在"),
            ("JournalDto", r"public struct CommitJournalEntry", "commit journal DTO 存在"),
            ("MaxAttempts", r"public const int MaxAttemptsPerSlot = 2", "每槽至多 2 次尝试"),
            ("LeaseFailClosed",
             r"if \(_state\.spawnLeasesInvalidated \|\| _committedSlots\.Contains\(slotIndex\)\)",
             "lease 单向故障/已提交槽 fail-closed"),
            ("CommitOrder",
             r"if \(!_state\.RegisterTrackedBoss\(health, character\)\) return false;"
             r"[\s\S]{0,200}?if \(!_state\.ResolveSlotOnce\(slotIndex, ModeGSlotOutcome\.Committed\)\)"
             r"[\s\S]{0,120}?_state\.UnregisterTrackedBoss\(health\);",
             "TryCommit 先登记后结案，失败回滚"),
            ("OfficialOptions",
             r"public static EnemySpawnCoreOptions CreateOfficialSpawnOptions\(\)"
             r"[\s\S]{0,400}?HoldForExternalCommit = true,"
             r"[\s\S]{0,200}?ApplySharedMutators = false,"
             r"[\s\S]{0,200}?AllowRandomRetryFallback = false,",
             "official options 三开关冻结"),
            ("ManagedOptions",
             r"internal static EnemySpawnCoreOptions CreateManagedSpawnOptions\(ManagedBossSpawnContext ctx\)",
             "managed options 构造入口存在"),
            ("ExhaustedOutcome",
             r"public bool MarkExhausted\(int slotIndex\)",
             "两次尝试耗尽结案入口"),
            ("KilledOutcome",
             r"public bool MarkKilled\(Health health\)",
             "死亡结案入口（exact Health 引用身份）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, tx):
                errors.append("[{}] 不满足: {}".format(name, desc))

        # managed options 同样三开关
        m = re.search(r"CreateManagedSpawnOptions\(ManagedBossSpawnContext ctx\)[\s\S]{0,500}?\}", tx)
        if m:
            body = m.group(0)
            for flag in ["HoldForExternalCommit = true",
                         "ApplySharedMutators = false",
                         "AllowRandomRetryFallback = false"]:
                if flag not in body:
                    errors.append("[ManagedOptionsFlags] managed options 缺少 {}".format(flag))

    if patch:
        if "ref bool __state" not in patch:
            errors.append("[StatePairPrefix] Prefix 缺少 ref bool __state")
        if not re.search(r"Finalizer\(Exception __exception, bool __state\)", patch):
            errors.append("[StatePairFinalizer] Finalizer 缺少 bool __state 配对")

    validate_hurt_patch_owners(collect_hurt_patch_sources(errors), errors)

    if errors:
        print("ModeGSpawnTransactionGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGSpawnTransactionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
