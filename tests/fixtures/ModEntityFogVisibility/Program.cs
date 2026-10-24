using System;
using System.Linq;
using UnityEngine;
using FOW;
using BossRush;
using UObject = UnityEngine.Object;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception("FAIL " + label);
        Console.WriteLine("PASS " + label);
    }
    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }
    private static FogOfWarHider Existing(GameObject root, int observers = 0, bool permanent = false)
    {
        var child = Child(root, "native hider");
        child.SetActive(false);
        var hider = child.AddComponent<FogOfWarHider>();
        hider.SamplePoints = new[] { root.transform };
        hider.NumObservers = observers;
        hider.PermanentlyReveal = permanent;
        child.SetActive(true);
        return hider;
    }
    private static void ResetWorld()
    {
        foreach (GameObject go in GameObject.Created.ToArray()) if (go != null) UObject.Destroy(go);
        Check(LevelManager.Subscribers == 0, "scene destruction removes all death subscriptions");
        GameObject.Created.Clear();
        LevelManager.Instance = new LevelManager { IsRaidMap = true };
        var player = new GameObject("player").AddComponent<CharacterMainControl>();
        player.Health = new Health();
        CharacterMainControl.Main = player;
    }
    private static void ExistingHiderAndRendering()
    {
        ResetWorld();
        var root = new GameObject("friendly model");
        root.layer = 0;
        var renderer = root.AddComponent<Renderer>();
        renderer.enabled = false;
        var preHidden = Child(root, "skill hidden").AddComponent<Renderer>();
        preHidden.forceRenderingOff = true;
        var light = root.AddComponent<Light>(); light.cullingMask = 123;
        var darkLight = root.AddComponent<Light>(); darkLight.cullingMask = 0;
        var collider = root.AddComponent<Collider>();
        var hider = Existing(root);
        int before = GameObject.Created.Count;
        var binding = ModEntityFogVisibility.Attach(root);
        Check(binding != null && renderer.forceRenderingOff && preHidden.forceRenderingOff && light.cullingMask == 0,
              "unknown first-frame visibility hides renderers and lights in raids");
        Check(!renderer.enabled && collider.enabled && root.activeSelf && root.layer == 0,
              "fog preserves skill enabled state root collider and layer");
        Check(GameObject.Created.Count == before && hider.Subscribers == 1 && LevelManager.Subscribers == 1,
              "existing native hider is reused with one subscription");
        int scans = GameObject.Scans;
        Check(ReferenceEquals(binding, ModEntityFogVisibility.Attach(root)) && GameObject.Scans == scans,
              "repeated attachment reuses binding without tree scans");
        hider.Emit(false);
        renderer.forceRenderingOff = false; light.cullingMask = 255;
        scans = GameObject.Scans;
        for (int i = 0; i < 120; i++) UObject.Invoke(binding, "LateUpdate");
        Check(renderer.forceRenderingOff && light.cullingMask == 0 && GameObject.Scans == scans,
              "hidden late updates reassert cached visuals without scans");
        hider.Emit(true);
        Check(!renderer.forceRenderingOff && preHidden.forceRenderingOff && light.cullingMask == 123 && darkLight.cullingMask == 0,
              "reveal restores original force-off and exact light masks");
        Check(!renderer.enabled && root.activeSelf && collider.enabled, "reveal does not reopen skill renderers or disable logic");
        renderer.forceRenderingOff = true; light.cullingMask = 77;
        UObject.Invoke(binding, "LateUpdate");
        Check(renderer.forceRenderingOff && light.cullingMask == 77, "visible late update leaves other visual owners alone");
        renderer.forceRenderingOff = false; hider.Emit(false);
        root.SetActive(false);
        Check(!renderer.forceRenderingOff && light.cullingMask == 77 && hider.Subscribers == 0 && LevelManager.Subscribers == 0,
              "disable restores snapshots and unsubscribes both events");
        root.SetActive(true);
        Check(renderer.forceRenderingOff && hider.Subscribers == 1 && LevelManager.Subscribers == 1,
              "reenable reevaluates native visibility and subscribes once");
        UObject.Destroy(binding);
        Check(binding == null && !renderer.forceRenderingOff && hider != null && hider.Subscribers == 0 && LevelManager.Subscribers == 0,
              "destroying binding restores rendering and preserves native hider");
    }
    private static void OwnedProbeAndLifecycle()
    {
        ResetWorld();
        var root = new GameObject("standalone NPC");
        var renderer = root.AddComponent<Renderer>();
        var sample = Child(root, "sample");
        var binding = ModEntityFogVisibility.Attach(root, sample.transform);
        var hider = root.GetComponentInChildren<FogOfWarHider>(true);
        Check(hider != null && hider.ConfiguredOnEnable && hider.EnableCalls == 1 && !hider.PermanentlyReveal
              && hider.SamplePoints.Length == 3 && hider.SamplePoints[0] == hider.transform
              && hider.transform.localPosition.y == 0.526f
              && hider.SamplePoints[1].localPosition.x == -0.15f && hider.SamplePoints[2].localPosition.x == 0.15f,
              "owned probe configures samples before first enable");
        Check(hider.gameObject != root && hider.transform.Parent == root.transform && renderer.forceRenderingOff,
              "owned probe keeps service root active and starts hidden");
        UObject.Destroy(binding);
        Check(hider == null && !renderer.forceRenderingOff && root != null && root.activeSelf,
              "destroying owner removes only its probe and restores visuals");
        root.SetActive(false);
        binding = ModEntityFogVisibility.Attach(root);
        hider = root.GetComponentInChildren<FogOfWarHider>(true);
        Check(hider != null && hider.EnableCalls == 0 && hider.Subscribers == 0 && !renderer.forceRenderingOff,
              "inactive attachment waits for hierarchy enable");
        root.SetActive(true);
        Check(hider.ConfiguredOnEnable && hider.Subscribers == 1 && renderer.forceRenderingOff,
              "inactive attachment initializes safely when root enables");
        UObject.Destroy(root);
        Check(root == null && renderer == null && hider == null && LevelManager.Subscribers == 0,
              "scene destruction cascades components and retires subscriptions");
        Check(ModEntityFogVisibility.Attach(root) == null && ModEntityFogVisibility.Attach(null) == null,
              "attachment rejects destroyed and null roots");
    }
    private static void VisibilityPolicy()
    {
        ResetWorld();
        var root = new GameObject("observed"); var r = root.AddComponent<Renderer>();
        var hider = Existing(root, 1);
        var binding = ModEntityFogVisibility.Attach(root);
        Check(!r.forceRenderingOff, "native observers reveal immediately");
        hider.Emit(false);
        Check(r.forceRenderingOff, "native loss of sight hides immediately");
        LevelManager.EmitDeath();
        Check(!r.forceRenderingOff, "main character death restores rendering");
        hider.Emit(false);
        Check(!r.forceRenderingOff, "post-death fog events keep models visible");
        UObject.Destroy(root);
        root = new GameObject("permanent"); r = root.AddComponent<Renderer>(); hider = Existing(root, 0, true);
        ModEntityFogVisibility.Attach(root);
        Check(r.forceRenderingOff, "permanent reveal still waits for first native observation");
        UObject.Destroy(root);
        root = new GameObject("disabled native hider"); r = root.AddComponent<Renderer>(); hider = Existing(root);
        hider.enabled = false;
        ModEntityFogVisibility.Attach(root);
        Check(!r.forceRenderingOff, "disabled native hider represents an already-revealed object");
        UObject.Destroy(root);
        LevelManager.Instance.IsRaidMap = false;
        root = new GameObject("base NPC"); r = root.AddComponent<Renderer>(); hider = Existing(root);
        ModEntityFogVisibility.Attach(root); hider.Emit(false);
        Check(!r.forceRenderingOff, "non-raid scenes preserve friendly visibility");
        UObject.Destroy(root);
        LevelManager.Instance = null;
        root = new GameObject("no level"); r = root.AddComponent<Renderer>(); ModEntityFogVisibility.Attach(root);
        Check(!r.forceRenderingOff, "missing level does not hide models");
        UObject.Destroy(root);
        LevelManager.Instance = new LevelManager { IsRaidMap = true };
        CharacterMainControl.Main.Health.IsDead = true;
        root = new GameObject("already dead player"); r = root.AddComponent<Renderer>(); ModEntityFogVisibility.Attach(root);
        Check(!r.forceRenderingOff, "attachment observes already-dead main character");
    }
    private static void EquipmentVisuals()
    {
        ResetWorld();
        EquipmentVisualFixture.Ensure(null);
        var root = new GameObject("equipment"); var renderer = root.AddComponent<Renderer>();
        EquipmentVisualFixture.Ensure(root);
        var csv = root.GetComponent<CharacterSubVisuals>();
        Check(csv != null && csv.SetRenderersCalls == 1 && csv.SavedRenderers.Contains(renderer),
              "equipment missing official subvisuals is initialized once");
        var owned = new GameObject("owner cached renderer").AddComponent<Renderer>();
        csv.SavedRenderers.Add(owned);
        EquipmentVisualFixture.Ensure(root);
        Check(csv.SetRenderersCalls == 1 && csv.SavedRenderers.Contains(owned)
              && root.GetComponentsInChildren<CharacterSubVisuals>(true).Length == 1,
              "equipment preserves existing official subvisual caches");
        foreach (Action<GameObject> prepare in new Action<GameObject>[] { HalberdVisualFixture.PrepareRuntimeHoldAgentVisual, ScytheVisualFixture.PrepareRuntimeHoldAgentVisual })
        {
            prepare(null);
            var holder = new GameObject("hidden holder").AddComponent<CharacterMainControl>();
            var weapon = Child(holder.gameObject, "melee runtime");
            var r = weapon.AddComponent<Renderer>(); r.enabled = false; r.forceRenderingOff = true;
            holder.characterModel = new CharacterModel { Visual = weapon, Hidden = true };
            VisualTrace.Events.Clear(); prepare(weapon);
            Check(r.enabled && !r.forceRenderingOff && weapon.layer == 23 && holder.characterModel.SyncCalls == 1
                  && string.Join(",", VisualTrace.Events) == "motion,graphics,sync",
                  "melee visual initialization finishes by restoring official holder fog layer");
            var prefab = new GameObject("unowned prefab");
            prepare(prefab);
            Check(holder.characterModel.SyncCalls == 1 && prefab.layer == 0, "unowned melee prefab has no fake character binding");
        }
    }
    private static int Main()
    {
        try
        {
            ExistingHiderAndRendering(); OwnedProbeAndLifecycle(); VisibilityPolicy(); EquipmentVisuals(); ResetWorld();
            Console.WriteLine("ModEntityFogVisibility: " + assertions + " assertions passed"); return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
}
