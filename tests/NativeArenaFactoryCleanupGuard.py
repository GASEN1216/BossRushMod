"""原生异步角色生成结束前不得销毁/击杀；执行证据在 ArenaHostRemainder。"""
from pathlib import Path
import re
from ArchitectureStructureGuard import extract_method_body
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(name):
    return clean_source((ROOT / name).read_text(encoding="utf-8-sig"))


def body(source, signature):
    return re.sub(r"\s+", " ", extract_method_body(source, signature))


def check():
    maintenance = read("WavesArena/WavesArenaRuntimeModule_EnemyMaintenance.cs")
    registry = read("WavesArena/WavesArenaRuntimeModule_CharacterRegistry.cs")
    spawners = read("WavesArena/WavesArenaSpawnerControl.cs")
    failures = []
    for source, signature, kind, effect in (
        (maintenance, "internal void ForceKillAllEnemies()", "ForceKill", "c.Health.Hurt(dmgInfo);"),
        (maintenance, "internal void ClearEnemiesForBossRush()", "ClearEnemies", "_reusableDestroyList.Add(c.gameObject);"),
        (registry, "internal void TryCleanNonBossRushDaXingXing()", "ForeignDaXingXing", "UnityEngine.Object.Destroy(c.gameObject);"),
    ):
        method = body(source, signature)
        gate = "if (DeferNativeFactoryCleanup(c, NativeArenaCleanupKind." + kind + ")) continue;"
        if gate not in method or effect not in method or method.index(gate) > method.index(effect):
            failures.append(signature + ": 实际副作用前必须走同对象生成租约")

    defer = body(spawners, "private bool DeferNativeFactoryCleanup(")
    for token in (
        "character.GetComponent<NativeArenaFactoryLease>()", "!lease.IsCreating", "lease.Defer(kind, delegate",
        "owner != expectedOwner", "current != this", "CharacterMainControl.Main != player",
        "BossRushCoop.LocalAssetId != asset", "GetActiveScene().handle != scene",
        "character.gameObject.scene.handle != characterScene", "!BossRushCoop.HasWorldAuthority",
        "!BossRushCoop.IsConnected", "!BossRushCoop.IsHost", "BossRushCoop.RunId != room",
        "ForceKillAllEnemies();", "ClearEnemiesForBossRush();", "TryCleanNonBossRushDaXingXing();",
    ):
        if token not in defer:
            failures.append("生成收尾必须保留旧 owner/场景/资产/房间与原清理筛选: " + token)

    complete = body(spawners, "internal void Complete()")
    destroy = body(spawners, "private void OnDestroy()")
    if not complete.startswith("{ if (!IsCreating) return; IsCreating = false;"):
        failures.append("生成终态必须先幂等释放在途状态，再执行旧请求")
    if "deferred.Clear(); deferred = null;" not in complete or complete.index("deferred.Clear();") > complete.index("retry();"):
        failures.append("执行续作前必须释放全部请求，避免重入重复清场")
    if "deferred.Clear(); deferred = null; IsCreating = false;" not in destroy:
        failures.append("真实角色销毁必须取消待清场收据")
    request = body(spawners, "internal void Defer(")
    if "deferred.ContainsKey(kind)" not in request:
        failures.append("同一实际清场类型的重复请求必须合并")

    prefix = body(spawners, "private static void Prefix(")
    postfix = body(spawners, "private static void Postfix(")
    if "Prefix(object __instance, out CharacterMainControl __state)" not in spawners or "Postfix(object __instance, CharacterMainControl __state)" not in spawners:
        failures.append("必须按值观察真实状态机，不得回写 ref __instance")
    for token in ("characterField.GetValue(__instance)",):
        if token not in prefix:
            failures.append("必须保留真实终态前角色身份")
    for token in (
        "(int)stateField.GetValue(__instance) == -2", "if (character == null) character = __state;",
        "if (terminal) { if (lease != null) lease.Complete(); }",
        "else if (lease == null) character.gameObject.AddComponent<NativeArenaFactoryLease>();",
    ):
        if token not in postfix:
            failures.append("必须沿真实终态释放同一角色: " + token)
    patch = spawners[spawners.index("internal static class NativeArenaFactoryLifecyclePatch"):]
    if re.search(r"\b(?:__result|Transpiler|Finalizer|SetValue|GetAwaiter|GetResult|ContinueWith)\b", patch):
        failures.append("生成观察不得改返回任务/awaiter/状态机或吞异常")
    if "[HarmonyPatch]" not in spawners or 'AccessTools.Method(machine, "MoveNext")' not in patch:
        failures.append("必须接实际官方 MoveNext")
    return failures


if __name__ == "__main__":
    errors = check()
    for error in errors:
        print("NativeArenaFactoryCleanupGuard: " + error)
    print("NativeArenaFactoryCleanupGuard: " + ("FAIL" if errors else "PASS"))
    raise SystemExit(bool(errors))
