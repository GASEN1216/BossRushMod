using System;
using System.Reflection;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
    }

    private static GameObject GetLegacyGoblinInstance(ModBehaviour owner)
    {
        PropertyInfo property = typeof(ModBehaviour).GetProperty("goblinNPCInstance", BindingFlags.Instance | BindingFlags.NonPublic);
        return property != null ? (GameObject)property.GetValue(owner) : null;
    }

    private static void Main()
    {
        SceneManager.ActiveScene = new Scene { name = "fixture_scene" };
        NPCSpawnConfig.HasNormalModeConfig = true;
        NPCSpawnConfig.SharedPositionCalls = 0;
        NPCAffinityInteractionHelper.SpawnCalls = 0;
        GameObject.InstanceCount = 0;
        Physics.ReturnHit = true;
        Physics.GroundPoint = new Vector3(7f, 0f, 9f);
        FixtureAssets.GoblinPrefab = new GameObject("GoblinPrefab");
        AffinityManager.Married = false;

        ModBehaviour host = new ModBehaviour();
        host.InitializeGoblinRuntime();
        GoblinNpcRuntimeModule runtime = host.GoblinNpcRuntime;
        Check(runtime != null && runtime.ModuleName == "GoblinNPC", "host owns the registered module instance");
        Check(typeof(ModBehaviour).GetField("goblinNPCInstance", BindingFlags.Instance | BindingFlags.NonPublic) == null,
            "host keeps no duplicate NPC instance field");
        PropertyInfo legacyInstance = typeof(ModBehaviour).GetProperty("goblinNPCInstance", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(legacyInstance != null && !legacyInstance.CanWrite, "old instance name remains a read-only host property");

        host.SpawnGoblinNPC(new Vector3(7f, 8f, 9f), true, true);
        GameObject spawned = runtime.GoblinNPCInstance;
        Check(spawned != null && GetLegacyGoblinInstance(host) == spawned, "old spawn entry forwards state to the module");
        Check(spawned.name == "GoblinNPC_BossRush" && spawned.activeSelf, "spawn keeps the original instance setup");
        Check(spawned.transform.position == new Vector3(7f, 0.1f, 9f), "spawn still applies ground raycast correction");
        GoblinNPCController controller = spawned.GetComponent<GoblinNPCController>();
        GoblinMovement movement = spawned.GetComponent<GoblinMovement>();
        Check(controller != null && movement != null && spawned.GetComponent<GoblinInteractable>() != null,
            "spawn attaches controller, movement and interaction components");
        Check(movement.Stopped && !movement.enabled && movement.SceneName == "fixture_scene"
              && controller.StationaryCalls == 1,
            "wedding spawn keeps stationary behavior and scene binding");
        Check(NPCAffinityInteractionHelper.SpawnCalls == 1, "daily decay remains before the spawn decision");

        host.SpawnGoblinNPC(null, false, true);
        Check(GameObject.InstanceCount == 1 && runtime.GoblinNPCInstance == spawned,
            "duplicate spawn is ignored while the existing instance lives");
        host.SummonGoblin();
        Check(controller.RunCalls == 1, "old summon entry reaches the active controller");

        MethodInfo load = typeof(ModBehaviour).GetMethod("LoadGoblinAssetBundle", BindingFlags.Instance | BindingFlags.NonPublic);
        PropertyInfo prefab = typeof(ModBehaviour).GetProperty("goblinPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(load != null && (bool)load.Invoke(host, null) && prefab != null
              && ReferenceEquals(prefab.GetValue(host), runtime.GoblinPrefab),
            "ZombieMode compatibility entry reads the module-owned prefab cache");

        host.DestroyGoblinNPC();
        Check(spawned == null && runtime.GoblinNPCInstance == null && GetLegacyGoblinInstance(host) == null
              && host.GetGoblinController() == null,
            "old destroy entry clears module state and read-only host views");

        int beforeMarried = GameObject.InstanceCount;
        AffinityManager.Married = true;
        host.SpawnGoblinNPC(null, false, false);
        Check(GameObject.InstanceCount == beforeMarried && runtime.GoblinNPCInstance == null,
            "married ordinary spawn remains suppressed");
        host.SpawnGoblinNPC(new Vector3(4f, 0f, 4f), false, true);
        Check(runtime.GoblinNPCInstance != null, "force spawn still bypasses the married and scene gates");
        host.DestroyGoblinNPC();

        AffinityManager.Married = false;
        NPCSpawnConfig.HasNormalModeConfig = false;
        host.RandomSelection = true;
        host.ValidArena = true;
        NPCSpawnConfig.LastAvoidPositions = null;
        GameObject courier = new GameObject("Courier");
        courier.transform.position = new Vector3(50f, 0f, 50f);
        host.SetCourierNpc(courier);
        ModBehaviour.SharedSpawnPoints = new[] { new Vector3(5f, 0f, 6f) };
        host.SpawnGoblinNPC();
        Check(runtime.GoblinNPCInstance != null, "arena support selection still permits a configured spawn");
        Check(NPCSpawnConfig.LastAvoidPositions != null && NPCSpawnConfig.LastAvoidPositions.Length == 1
              && NPCSpawnConfig.LastAvoidPositions[0] == new Vector3(50f, 0f, 50f)
              && NPCSpawnConfig.LastMinimumDistance == 10f && NPCSpawnConfig.LastRequireAvoidance,
            "spawn placement still avoids the courier with the existing distance contract");
        Check(runtime.GoblinNPCInstance.transform.position == new Vector3(7f, 0.1f, 9f),
            "shared spawn result retains the raycast correction path");

        Console.WriteLine("Goblin RuntimeModule regression PASS (" + checks + " checks)");
    }
}
