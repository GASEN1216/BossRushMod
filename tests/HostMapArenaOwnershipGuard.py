"""Map, Arena host-state and common NPC queries keep their dedicated owners."""
from pathlib import Path
import re
from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]


def read(relative):
    return clean_source((ROOT / relative).read_text(encoding="utf-8-sig"))


def compact(source):
    return re.sub(r"\s+", "", source)


def body(source, signature):
    assert source.count(signature) == 1, "missing or duplicate method: " + signature
    start = source.index("{", source.index(signature))
    depth = 0
    for end in range(start, len(source)):
        depth += (source[end] == "{") - (source[end] == "}")
        if depth == 0:
            return compact(source[start + 1:end])
    raise AssertionError("unclosed method: " + signature)


def main():
    host = read("ModBehaviour.cs")
    bindings = read("ModBehaviourRuntimeModules.cs")
    maps = read("MapSelection/BossRushMapRuntime.cs")
    arena = read("WavesArena/WavesArenaRuntimeModule_HostState.cs")
    npc = read("Integration/NPCs/Common/CommonNpcSpawnPointPolicy.cs")
    travel = read("Integration/BossRushIntegration_TravelAndSetup.cs")
    for source in (maps, arena, npc):
        assert "partial class ModBehaviour" not in source, "business state must not return to host partial"
    assert "internal sealed class BossRushMapRuntime" in maps, "map service type identity changed"
    assert compact("private readonly BossRushMapRuntime mapRuntime = new BossRushMapRuntime();") in compact(host), "each host needs one eager readonly map owner"
    assert compact("private static readonly MapSpawnPointRegistry _mapSpawnRegistry = new MapSpawnPointRegistry();") in compact(maps), "map registry keeps shared readonly identity"
    assert "private Vector3[] currentMapSpawnPoints = null;" in maps, "dynamic spawn points must be per map owner"
    assert not re.search(r"\b(?:MapSpawnPointRegistry|Vector3\[\])\s+(?:_mapSpawnRegistry|currentMapSpawnPoints)\s*(?:=|;)", host), "host must not shadow map-owned fields"
    static_methods = {
        "public static BossRushMapConfig GetMapConfigBySceneName(string sceneName)": "return BossRushMapRuntime.GetMapConfigBySceneName(sceneName);",
        "public static BossRushMapConfig GetMapConfigBySceneID(string sceneID)": "return BossRushMapRuntime.GetMapConfigBySceneID(sceneID);",
        "public static BossRushMapConfig GetCurrentMapConfig()": "return BossRushMapRuntime.GetCurrentMapConfig();",
        "public static BossRushMapConfig[] GetAllMapConfigs()": "return BossRushMapRuntime.GetAllMapConfigs();",
        "public static Vector3[] GetSpawnPointsForScene(string sceneName)": "return BossRushMapRuntime.GetSpawnPointsForScene(sceneName);",
        "public static Vector3 GetCurrentSceneDefaultPosition()": "return BossRushMapRuntime.GetCurrentSceneDefaultPosition();",
        "public static Vector3 GetDefaultPositionForScene(string sceneName)": "return BossRushMapRuntime.GetDefaultPositionForScene(sceneName);",
        "public bool IsValidBossRushArenaScene(string sceneName)": "return mapRuntime.IsValidBossRushArenaScene(sceneName);",
        "public bool IsCurrentSceneValidBossRushArena()": "return mapRuntime.IsCurrentSceneValidBossRushArena();",
        "public Vector3[] GetCurrentSceneSpawnPoints()": "return mapRuntime.GetCurrentSceneSpawnPoints();",
    }
    for signature, forwarding in static_methods.items():
        assert body(host, signature) == compact(forwarding), "map public entry must forward unchanged: " + signature
    assert "BossRushMapRuntime.Initialize(modPath);" in read("Utilities/AlwaysOnRuntimeHooks.cs"), "map owner must initialize from mod path"
    assert "mapRuntime.SetCurrentMapSpawnPoints(bossRushIntegrationRuntime.ResolveMapSpawnPointsForScene(sceneName));" in compact(travel), "scene setup must populate host's map owner"
    assert "BossRushMapRuntime.ResolveArenaSignPosition(currentMapConfig,playerPosition)" in compact(travel), "sign position must reach map policy"

    assert "internal sealed partial class WavesArenaRuntimeModule" in arena, "Arena state must extend registered runtime"
    assert "internal bool IsActive { get; private set; }" in arena, "active state must belong to Arena owner"
    assert "internal MonoBehaviour PlayerCharacter { get; set; }" in arena, "cached player must belong to Arena owner"
    for flag in ("bossRushArenaPlanned", "bossRushArenaActive"):
        assert "private static bool " + flag + " = false;" in arena, "Arena flags retain original static storage: " + flag
        assert not re.search(r"\bbool\s+" + flag + r"\s*(?:=|;)", host), "host must not shadow Arena flag: " + flag
    for signature, forwarding in (
        ("public void ConfigureBossRushMode(int bossesPerWave, bool useInfiniteHell)", "wavesArenaRuntime.ConfigureBossRushMode(bossesPerWave, useInfiniteHell);"),
        ("private void SetBossRushRuntimeActive(bool active)", "wavesArenaRuntime.SetBossRushRuntimeActive(active);"),
        ("public void ReturnToBossRushStart()", "wavesArenaRuntime.ReturnToBossRushStart();"),
    ):
        assert body(host, signature) == compact(forwarding), "Arena public entry must forward unchanged: " + signature
    assert body(host, "public void ConfigureBossRushMode(int bossesPerWave)") == "ConfigureBossRushMode(bossesPerWave,false);", "legacy configure overload must keep normal-mode default"
    assert compact("public bool IsActive { get { return wavesArenaRuntime != null && wavesArenaRuntime.IsActive; } }") in compact(host), "host active query keeps null-safe owner read"
    assert compact("private MonoBehaviour playerCharacter { get { return wavesArenaRuntime.PlayerCharacter; } set { wavesArenaRuntime.PlayerCharacter = value; } }") in compact(host), "host cached player must forward both directions"
    compact_bindings = compact(bindings)
    expected_arena = compact("wavesArenaRuntime = new WavesArenaRuntimeModule(); wavesArenaRuntime.BindHostStateServices(() => config != null ? (int?)config.infiniteHellBossesPerWave : null, () => uiAndSignsRuntime.SignInteract, ClearEnemyRecoveryMonitorState); runtimeModuleHost.Register(wavesArenaRuntime);")
    assert expected_arena in compact_bindings, "Arena callbacks must bind live config and sign before registration"

    assert "internal sealed partial class CommonNpcRuntimeModule" in npc, "NPC policy must extend common NPC owner"
    expected_npc = compact("commonNpcRuntime = new CommonNpcRuntimeModule(); commonNpcRuntime.BindSpawnPointQueries(UsesArenaSupportNpcPlacement, () => IsActive, () => modeDRuntime.IsActive, () => IsBossRushArenaActive); runtimeModuleHost.Register(commonNpcRuntime);")
    assert expected_npc in compact_bindings, "NPC four live queries must bind before registration"
    assert body(host, "public bool ShouldUseBossRushCommonNPCSpawnPoints(string sceneName = null)") == "returncommonNpcRuntime.ShouldUseBossRushCommonNPCSpawnPoints(sceneName);", "NPC host predicate must use its owner"
    assert body(host, "public static Vector3[] GetSharedCommonNPCSpawnPointsForScene(string sceneName)") == "ModBehaviourmod=Instance;returnCommonNpcRuntimeModule.GetSharedCommonNPCSpawnPointsForScene(sceneName,mod!=null?mod.commonNpcRuntime:null);", "NPC static compatibility entry must pass existing nullable owner"
    for callback in ("usesArenaSupportNpcPlacement", "isActive", "isModeDActive", "isBossRushArenaActive"):
        assert "private Func<bool> " + callback + ";" in npc, "NPC callback must remain instance-owned and unbound initially: " + callback
    assert body(npc, "internal void BindSpawnPointQueries(") == "usesArenaSupportNpcPlacement=useArenaSupportPlacement;isActive=active;isModeDActive=modeDActive;isBossRushArenaActive=arenaActive;", "NPC binding must retain all four live providers"
    return_point = read("WavesArena/WavesArenaRuntimeModule_ReturnPoint.cs")
    return_bridge = read("WavesArena/WavesArenaEntryAndTeleport.cs")
    assert "internal sealed partial class WavesArenaRuntimeModule" in return_point, "return-point creation must belong to Arena"
    assert body(return_bridge, "private void TryCreateReturnInteractable_WavesArena()") == "wavesArenaRuntime.TryCreateReturnInteractable_WavesArena();", "return-point entry must forward to Arena"
    print("HostMapArenaOwnershipGuard: PASS")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as error:
        print("HostMapArenaOwnershipGuard: FAIL - " + str(error))
        raise SystemExit(1)
