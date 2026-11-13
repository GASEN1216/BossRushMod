using System;
using System.Collections.Generic;
using System.Linq;
using BossRush;
using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static readonly int LampCount = Shader.PropertyToID("_LastShiftLampCount");
    private static readonly int LampPositions = Shader.PropertyToID("_LastShiftLampPositions");
    private static readonly int LightingEnabled = Shader.PropertyToID("_LastShiftLightingEnabled");
    private static readonly int SunColor = Shader.PropertyToID("_LastShiftSunColor");
    private static int passed;

    private static void Main()
    {
        Run("official source crosses scene unload and fills all three rooms", OfficialRooms);
        Run("leave and re-enter reuse lights while rain canopies remain unlit", LeaveAndReturn);
        Run("disposing before Bind releases only the persistent holder", DisposeBeforeBinding);
        Run("disposing after Bind shuts down all four lights and releases the holder", DisposeAfterBinding);
        Run("missing official source falls back once to real lamps", MissingSource);
        Run("native lamp template rejects unrelated objects and invalid renderer links", UnsafeSource);
        Run("the active renderer must own the correct official feature", WrongRenderer);
        Run("invalid official feature or camera mask causes the same fallback", InvalidFeatureOrLayer);
        Run("fallback takes the room's size into account", FallbackRoomChange);
        Run("the active room diagnostic reports native lamp parameters and clears on exit", IndoorFillDiagnostic);
        Run("environment follows official weather without retiming the station lamps", ReadOfficialLighting);
        Run("Tidegate builds, switches and cleans up its official room lamps", TidegateRooms);
        Run("Tidegate retains beacon and flashlight slots with four indoor lamps", TidegateLampBudget);
        Console.WriteLine("LastShiftLightingRuntime: " + passed + " scenarios passed");
    }

    private static void Run(string name, Action test)
    {
        SceneManager.LoadSingle(1);
        foreach (GameObject go in GameObject.All.Where(x => x != null).ToArray()) UnityEngine.Object.Destroy(go);
        GameObject.All.Clear();
        GameCamera.Instance = null;
        CharacterMainControl.Main = null;
        GameClock.Instance = new GameClock();
        Shader.Floats.Clear();
        Shader.Vectors.Clear();
        Shader.Arrays.Clear();
        Resources.Scans = 0;
        Debug.Warnings.Clear();
        Debug.Messages.Clear();
        Time.time = Time.unscaledTime = 0f;
        SkyIslandLighting.Hours = 12.0;
        RaidOfficialLighting.Sunlight = new Color(1f, 1f, 1f);
        RaidOfficialLighting.Ambient = new Color(0.5f, 0.5f, 0.5f);
        RaidOfficialLighting.Direction = Vector3.up;
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void OfficialRooms()
    {
        using (var setup = new Setup())
        {
            setup.Lighting.PrepareIndoorFill();
            setup.Lighting.PrepareIndoorFill();
            Check(Resources.Scans == 1, "official resources were scanned repeatedly");
            setup.BindInRaid();
            Check(setup.Source == null, "the original warehouse lamp must be destroyed by the single-scene load");
            Check(setup.Mesh != null && setup.Proxy != null, "the inactive holder did not protect the official shared assets");
            GameObject holder = Holder();
            Check(holder != null && !holder.activeSelf && holder.transform.parent == setup.World.Root.transform && holder.scene.handle == 2,
                "the shared-resource holder did not move into the raid scene");
            Check(Debug.Messages.Count(x => x.Contains("INDOOR_FILL_READY mode=SodaPointLight layer=15")) == 1 && Debug.Warnings.Count == 0,
                "official path was not identified exactly once");
            setup.Enter("signal");
            GameObject[] first = AssertSodaFill(setup);
            setup.Enter("repair");
            Check(first.SequenceEqual(AssertSodaFill(setup)), "switching rooms rebuilt the lamps");
            setup.Enter("bond");
            Check(first.SequenceEqual(AssertSodaFill(setup)), "third room did not reuse the same four lights");
        }
    }

    private static void LeaveAndReturn()
    {
        using (var setup = new Setup())
        {
            setup.Lighting.PrepareIndoorFill();
            setup.BindInRaid();
            setup.Enter("signal");
            GameObject[] first = AssertSodaFill(setup);
            setup.Enter(null);
            Check(first.All(go => !go.activeSelf) && Shader.GetGlobalFloat(LampCount) == 0f && setup.Roofs["signal"].shadowCastingMode == ShadowCastingMode.On,
                "leaving did not clear both light paths immediately and restore the official roof");
            setup.Enter("bondBay");
            Check(first.All(go => !go.activeSelf), "an exterior canopy lit the room lamps");
            setup.Enter("repair");
            Check(first.SequenceEqual(AssertSodaFill(setup)), "returning indoors did not reuse the same lamps");
            Check(Resources.Scans == 1, "room updates rescanned official assets");
        }
    }

    private static void DisposeBeforeBinding()
    {
        using (var setup = new Setup())
        {
            Shader.SetGlobalFloat(LampCount, 7f);
            setup.Lighting.PrepareIndoorFill();
            GameObject holder = Holder();
            Check(holder != null && holder.scene.handle == 0, "preload did not create a persistent holder");
            setup.Lighting.Dispose();
            setup.Lighting.Dispose();
            Check(holder == null && setup.Source != null && setup.Mesh != null && setup.Proxy != null, "cancelling disposed source assets or retained the holder");
            Check(Shader.GetGlobalFloat(LampCount) == 7f, "cancelling before Bind touched unrelated shader globals");
        }
    }

    private static void DisposeAfterBinding()
    {
        using (var setup = new Setup())
        {
            setup.Lighting.PrepareIndoorFill();
            setup.BindInRaid();
            setup.Enter("bond");
            GameObject[] first = AssertSodaFill(setup);
            GameObject holder = Holder();
            setup.Lighting.Dispose();
            setup.Lighting.Tick();
            Check(first.All(go => go == null) && holder == null && setup.Mesh != null && setup.Proxy != null,
                "raid teardown did not release objects or destroyed the shared source assets");
            Check(Shader.GetGlobalFloat(LampCount) == 0f, "raid teardown left the map lamp shader count set");
        }
    }

    private static void MissingSource()
    {
        using (var setup = new Setup(withSource: false))
        {
            setup.Lighting.PrepareIndoorFill();
            setup.BindInRaid();
            setup.Enter("bond");
            AssertPointLights();
            Check(Holder() == null && Debug.Warnings.Count == 1, "missing assets should emit one fallback warning and leave no holder");
        }
    }

    private static void WrongRenderer()
    {
        using (var setup = new Setup())
        {
            setup.Lighting.PrepareIndoorFill();
            setup.BindInRaid(useFeature: false);
            setup.Enter("signal");
            AssertPointLights();
            Check(Debug.Warnings.Count == 1 && Debug.Messages.Count == 0, "an unrelated feature incorrectly enabled official fill");
        }
    }

    private static void UnsafeSource()
    {
        foreach (Action<Setup> tamper in new Action<Setup>[]
        {
            setup => setup.Source.AddComponent<Light>(),
            setup => new GameObject("Extra child").transform.SetParent(setup.Source.transform, false),
            setup => setup.Source.GetComponent<SodaPointLight>().BindRenderer(null)
        })
        using (var setup = new Setup())
        {
            Debug.Warnings.Clear();
            tamper(setup);
            setup.Lighting.PrepareIndoorFill();
            setup.BindInRaid();
            setup.Enter("signal");
            AssertPointLights();
            Check(Holder() == null && Debug.Warnings.Count == 1, "an unsafe official object was retained as the lamp template");
        }
    }

    private static void ReadOfficialLighting()
    {
        using (var setup = new Setup())
        {
            setup.Lighting.PrepareIndoorFill();
            setup.BindInRaid();
            setup.Enter("signal");
            Check(Shader.GetGlobalFloat(LightingEnabled) == 1f && Shader.GetGlobalVector(SunColor).x == 1f,
                "the map shader did not receive the official sun snapshot");
            RaidOfficialLighting.Sunlight = new Color(0.12f, 0.15f, 0.2f);
            setup.Lighting.Tick();
            Check(Shader.GetGlobalVector(SunColor).x == 0.12f && setup.Lighting.NightFactor == 0f,
                "a change in weather should update sunlight without retiming the map lamps");
            SkyIslandLighting.Hours = 23.0;
            setup.Lighting.Tick();
            Check(setup.Lighting.NightFactor == 1f, "map lamps must still follow the clock");
        }
    }

    private static void InvalidFeatureOrLayer()
    {
        foreach (Action<Setup> tamper in new Action<Setup>[]
        {
            setup => setup.Feature.settings.Event = RenderPassEvent.AfterRenderingOpaques,
            setup => setup.Feature.settings.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.None,
            setup => setup.Feature.settings.overrideMaterial.shader.isSupported = false,
            setup => setup.Proxy.shader.isSupported = false,
            setup => setup.CameraMask = 1
        })
        using (var setup = new Setup())
        {
            Debug.Warnings.Clear();
            setup.Lighting.PrepareIndoorFill();
            tamper(setup);
            setup.BindInRaid();
            setup.Enter("repair");
            AssertPointLights();
            Check(Debug.Warnings.Count == 1, "invalid official configuration did not fall back exactly once");
        }
    }

    private static void FallbackRoomChange()
    {
        using (var setup = new Setup(withSource: false))
        {
            setup.Lighting.PrepareIndoorFill();
            setup.BindInRaid();
            setup.Enter("signal");
            Light[] first = AssertPointLights();
            float smallIntensity = first[0].intensity, smallRange = first[0].range;
            setup.Enter("bond");
            Light[] second = AssertPointLights();
            Check(first.SequenceEqual(second) && second[0].intensity > smallIntensity && second[0].range > smallRange,
                "fallback did not move and resize its existing lights");
            Check(smallIntensity < 12f && second[0].intensity == 24f,
                "fallback point lights retained their old four-lamp intensity or lost the large-room cap");
            Check(setup.Lighting.DescribeIndoorFill().StartsWith("room=bond mode=Point count=4 intensity=24.00 range=", StringComparison.Ordinal),
                "fallback diagnostics did not report live Unity point light parameters");
            setup.Enter(null);
            Check(first.All(light => !light.gameObject.activeSelf), "fallback remained visible outside the room");
        }
    }

    private static void IndoorFillDiagnostic()
    {
        using (var setup = new Setup())
        {
            setup.Lighting.PrepareIndoorFill();
            setup.BindInRaid();
            setup.Enter("signal");
            string signal = setup.Lighting.DescribeIndoorFill();
            Check(signal == "room=signal mode=SodaPointLight count=4 rgb=0.749,0.749,0.749 hardness=0.50 falloff=1.50 environmentTint=False radius=5.87,5.01,6.43",
                "native indoor diagnostic did not report the actual active lamp profile: " + signal);
            setup.Enter(null);
            Check(setup.Lighting.DescribeIndoorFill() == "room=none mode=none count=0", "leaving a room left active lights in the diagnostic");
        }
    }

    private static void TidegateRooms()
    {
        using (var setup = new Setup())
        using (var tide = new TidegateTide())
        {
            tide.PrepareIndoorFill();
            setup.OpenRaidCamera();
            var world = new TidegateWorld();
            Transform rooms = CreateRooms(world.Root, "TidegateIndoorRooms", out Transform buildings);
            var hall = new Bounds(new Vector3(-18f, 2.3f, 32.5f), new Vector3(20f, 4f, 7f));
            var cabin = new Bounds(new Vector3(38f, 2.25f, -60.5f), new Vector3(6f, 4f, 5f));
            MeshRenderer hallRoof = CreateRoom(rooms, buildings, "hall", hall);
            MeshRenderer cabinRoof = CreateRoom(rooms, buildings, "cabin", cabin);
            world.IndoorRoofs = new RaidIndoorRoofs(world.Root, false);
            tide.Bind(world);
            Check(setup.Source == null && setup.Mesh != null && setup.Proxy != null, "Tidegate did not preserve official lamp assets across the curtain");
            tide.SetViewFocus(hall.center);
            tide.Tick(0f);
            GameObject[] first = AssertTidegateSodaFill(hall);
            Check(tide.DescribeIndoorFill().StartsWith("room=hall mode=SodaPointLight count=4 rgb=0.749,0.749,0.749", StringComparison.Ordinal),
                "Tidegate did not report its active room and native lamp profile");
            Check(hallRoof.shadowCastingMode == ShadowCastingMode.ShadowsOnly && cabinRoof.shadowCastingMode == ShadowCastingMode.On,
                "only the active Tidegate roof should receive the official hide call");
            tide.SetViewFocus(cabin.center);
            tide.Tick(0f);
            Check(first.SequenceEqual(AssertTidegateSodaFill(cabin)) && hallRoof.shadowCastingMode == ShadowCastingMode.On
                && cabinRoof.shadowCastingMode == ShadowCastingMode.ShadowsOnly, "Tidegate did not reuse lamps or switch official roofs on the same tick");
            tide.SetViewFocus(cabin.center + Vector3.up * 6f);
            tide.Tick(0f);
            Check(ActiveTidegateLights().Length == 0 && Shader.GetGlobalFloat(Shader.PropertyToID("_TidegateLampCount")) == 0f
                && cabinRoof.shadowCastingMode == ShadowCastingMode.On, "foot point outside the vertical room should clear both indoor light paths and restore the roof");
            tide.Dispose();
            world.IndoorRoofs.Dispose();
            Check(first.All(lamp => lamp == null) && setup.Mesh != null && setup.Proxy != null
                && !GameObject.All.Any(go => go != null && go.name == "TidegateIndoorFillSource"), "Tidegate cleanup leaked lamps or destroyed a shared asset");
        }
    }

    private static void TidegateLampBudget()
    {
        using (var setup = new Setup())
        using (var tide = new TidegateTide())
        {
            tide.PrepareIndoorFill();
            setup.OpenRaidCamera();
            var world = new TidegateWorld();
            Transform rooms = CreateRooms(world.Root, "TidegateIndoorRooms", out Transform buildings);
            var hall = new Bounds(new Vector3(-18f, 2.3f, 32.5f), new Vector3(20f, 4f, 7f));
            CreateRoom(rooms, buildings, "hall", hall);
            world.IndoorRoofs = new RaidIndoorRoofs(world.Root, false);
            Transform markers = Child(world.Root.transform, "Markers"), lamps = Child(markers, "NightLamps");
            for (int i = 20; i > 0; i--)
                Child(lamps, "NightLamp_" + i).position = hall.center + new Vector3(i, 0f, 0f);
            tide.Bind(world);
            tide.SetBeacon(new Vector3(100f, 8f, 20f), 16f, new Color(1f, .9f, .7f));
            SkyIslandLighting.Hours = 23d;
            CharacterMainControl.Main = new GameObject("Player").AddComponent<CharacterMainControl>();
            CharacterMainControl.Main.transform.position = hall.center;
            CharacterMainControl.Main.FlashLight = true;
            tide.SetViewFocus(hall.center);
            tide.Tick(0f);
            int count = Shader.PropertyToID("_TidegateLampCount"), positions = Shader.PropertyToID("_TidegateLampPositions");
            Check(Shader.GetGlobalFloat(count) == 16f && Shader.Arrays[positions][0].x == 100f && Shader.Arrays[positions][15].w == 6.5f,
                "the beacon or flashlight was displaced by the indoor lights");
            for (int i = 0; i < 4; i++)
                Check(Shader.Arrays[positions][i + 1].x == ActiveTidegateLights()[i].transform.position.x, "room lights did not retain their four positions");
            Check(Shader.Arrays[positions][5].x == hall.center.x + 1f && Shader.Arrays[positions][14].x == hall.center.x + 10f,
                "night lamps did not fill remaining slots nearest-first");
            CharacterMainControl.Main.FlashLight = false;
            tide.Tick(0f);
            Check(Shader.GetGlobalFloat(count) == 15f, "turning off the flashlight did not remove only its slot");
            tide.Dispose();
            world.IndoorRoofs.Dispose();
            Check(Shader.GetGlobalFloat(count) == 0f, "Tidegate cleanup left shader lamps active");
        }
    }

    private static GameObject[] ActiveTidegateLights()
    {
        return GameObject.All.Where(go => go != null && go.activeSelf && go.name.StartsWith("TidegateIndoorFill", StringComparison.Ordinal)).ToArray();
    }

    private static GameObject[] AssertTidegateSodaFill(Bounds room)
    {
        GameObject[] lights = ActiveTidegateLights();
        Check(lights.Length == 4 && lights.All(go => go.GetComponent<SodaPointLight>() != null && go.GetComponent<Light>() == null),
            "Tidegate did not bind four official SodaPointLight components");
        int count = Shader.PropertyToID("_TidegateLampCount"), positions = Shader.PropertyToID("_TidegateLampPositions");
        Check(Shader.GetGlobalFloat(count) == 4f, "Tidegate did not send its four environmental lamps");
        for (int i = 0; i < lights.Length; i++)
        {
            Vector3 at = lights[i].transform.position;
            Vector4 shaderAt = Shader.Arrays[positions][i];
            Check(at.x == shaderAt.x && at.z == shaderAt.z && Math.Abs(at.y - (room.min.y + 3.1f)) < 1e-4 && at.y == shaderAt.y,
                "Tidegate roof offsets or room lamp positions do not match the environment array");
        }
        AssertNativeFillBalance(lights, room);
        return lights;
    }

    private static Transform Child(Transform parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private static Transform CreateRooms(GameObject world, string group, out Transform buildings)
    {
        buildings = Child(Child(world.transform, "Visual"), "VIS_buildings");
        return new GameObject(group).transform;
    }

    private static MeshRenderer CreateRoom(Transform rooms, Transform buildings, string room, Bounds bounds)
    {
        Child(rooms, room).gameObject.AddComponent<BoxCollider>().bounds = bounds;
        return Child(buildings, "BLD_" + room + "_Roof").gameObject.AddComponent<MeshRenderer>();
    }

    private static GameObject[] AssertSodaFill(Setup setup)
    {
        GameObject[] lights = Lights();
        Check(lights.Length == 4 && Shader.GetGlobalFloat(LampCount) == 4f, "expected four official light volumes and four environment lamps");
        Bounds room = setup.ActiveBounds;
        Vector4[] positions = Shader.Arrays[LampPositions];
        for (int i = 0; i < lights.Length; i++)
        {
            GameObject go = lights[i];
            SodaPointLight fill = go.GetComponent<SodaPointLight>();
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            Check(go.layer == 15 && go.GetComponent<Light>() == null && go.GetComponent<MeshFilter>().sharedMesh == setup.Mesh
                && renderer.sharedMaterial == setup.Proxy, "official camera feature was not given the original mesh/material/layer");
            Check(renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows && renderer.lightProbeUsage == LightProbeUsage.Off,
                "volume renderer participates in shadow or probe sampling");
            Check(fill != null && fill.Updates == 3 && !fill.enviromentTint,
                "official SodaPointLight setters did not update the cloned renderer exactly once per property");
            float hardness = (float)renderer.Properties[Shader.PropertyToID("_Hardness")];
            float falloff = (float)renderer.Properties[Shader.PropertyToID("_FallOff")];
            var color = (Color)renderer.Properties[Shader.PropertyToID("_LightColor")];
            Check(hardness == 0.5f && falloff == 1.5f && color.r == 191f / 255f && color.g == color.r && color.b == color.r
                && (float)renderer.Properties[Shader.PropertyToID("_EnviromentTintOn")] == 0f, "official SodaPointLight material properties were not set");
            Vector3 pos = go.transform.position;
            Check(pos.x == positions[i].x && pos.y == positions[i].y && pos.z == positions[i].z, "official lamps and map lamps are not at the same positions");
        }
        AssertNativeFillBalance(lights, room);
        return lights;
    }

    private static void AssertNativeFillBalance(GameObject[] lights, Bounds room)
    {
        Vector3 center = room.center;
        center.y = room.min.y + 1.3f;
        double top = NativeFillAt(lights, center, Vector3.up);
        double side = NativeFillAt(lights, center, new Vector3(1f, 0f, 0f));
        Check(top > 0.25 && side > 0.15 && top - side > 0.05,
            "the room center lost fill or the difference between upward and side-facing normals");

        for (int x = -10; x <= 10; x++)
            for (int z = -10; z <= 10; z++)
            {
                Vector3 at = center + new Vector3(room.extents.x * x / 10f, 0f, room.extents.z * z / 10f);
                Check(NativeFillAt(lights, at, Vector3.up) < 0.55, "additive native fill overexposes the sampled upper body plane");
            }

        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                var corner = new Vector3(center.x + x * room.extents.x, room.min.y + 0.75f, center.z + z * room.extents.z);
                Check(NativeFillAt(lights, corner, Vector3.up) > 0.05, "a lower room corner lost native fill coverage");
            }
    }

    // Installed SodaPointLight shader (globalgamemanagers.assets #89), before GBuffer material and weather multipliers.
    private static double NativeFillAt(GameObject[] lights, Vector3 at, Vector3 normal)
    {
        double total = 0d;
        foreach (GameObject light in lights)
        {
            var fill = light.GetComponent<SodaPointLight>();
            Vector3 delta = light.transform.position - at, scale = light.transform.lossyScale;
            var local = new Vector3(delta.x / scale.x, delta.y / scale.y, delta.z / scale.z);
            double radial = Math.Pow(Math.Max(0d, 1d - Math.Sqrt(local.sqrMagnitude) / 0.91), fill.FallOff);
            Vector3 direction = delta.normalized;
            double dot = direction.x * normal.x + direction.y * normal.y + direction.z * normal.z;
            double angular = Math.Max(0d, Math.Min(1d, 1d - fill.Hardness + fill.Hardness * dot));
            total += radial * angular * fill.LightColor.r;
        }
        return total;
    }

    private static Light[] AssertPointLights()
    {
        Light[] lights = Lights().Select(go => go.GetComponent<Light>()).ToArray();
        Check(lights.Length == 4 && lights.All(light => light != null && light.type == LightType.Point
            && light.shadows == LightShadows.None && light.renderMode == LightRenderMode.ForcePixel && light.intensity >= 6f && light.intensity <= 24f),
            "fallback did not create four active shadowless point lights");
        return lights;
    }

    private static GameObject Holder() { return GameObject.All.SingleOrDefault(go => go != null && go.name == "LastShiftIndoorFillSource"); }

    private static GameObject[] Lights()
    {
        return GameObject.All.Where(go => go != null && go.activeSelf && go.name.StartsWith("LastShiftIndoorFill", StringComparison.Ordinal)).ToArray();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Setup : IDisposable
    {
        internal readonly LastShiftLighting Lighting = new LastShiftLighting();
        internal readonly GameObject Source;
        internal readonly Mesh Mesh = new Mesh { name = "SodaPointLightMesh" };
        internal readonly Material Proxy = new Material { shader = new Shader { name = "ZeroPoint" } };
        internal readonly RenderObjects Feature = new RenderObjects { name = "SodaLight", isActive = true };
        internal int CameraMask = 1 << 15;
        internal LastShiftWorld World;
        internal Bounds ActiveBounds;
        internal readonly Dictionary<string, MeshRenderer> Roofs = new Dictionary<string, MeshRenderer>();
        private int entries;
        private bool roomsCreated;

        internal Setup(bool withSource = true)
        {
            SceneManager.LoadSingle(1);
            Feature.settings.Event = RenderPassEvent.BeforeRenderingDeferredLights;
            Feature.settings.filterSettings.RenderQueueType = RenderQueueType.Transparent;
            Feature.settings.filterSettings.LayerMask = new LayerMask { value = 1 << 15 };
            Feature.settings.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.Material;
            Feature.settings.overrideMaterialPassIndex = 0;
            Feature.settings.overrideMaterial = new Material { shader = new Shader { name = "SodaPointLight" } };
            if (!withSource) return;
            Source = new GameObject("Official Warehouse Lamp");
            var lamp = Source.AddComponent<SodaPointLight>();
            Source.AddComponent<MeshFilter>().sharedMesh = Mesh;
            MeshRenderer renderer = Source.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Proxy;
            lamp.BindRenderer(renderer);
        }

        internal void BindInRaid(bool useFeature = true)
        {
            OpenRaidCamera(useFeature);
            World = new LastShiftWorld();
            Lighting.Bind(World);
        }

        internal void OpenRaidCamera(bool useFeature = true)
        {
            SceneManager.LoadSingle(2);
            if (!GameObject.All.Any(g => g != null && g.GetComponent<MeshFilter>()?.sharedMesh == Mesh))
            {
                UnityEngine.Object.Destroy(Mesh);
                UnityEngine.Object.Destroy(Proxy);
            }
            var camera = new GameObject("GameCamera").AddComponent<Camera>();
            camera.cullingMask = CameraMask;
            var renderer = new ScriptableRenderer();
            if (useFeature) renderer.AddFeature(Feature);
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().scriptableRenderer = renderer;
            GameCamera.Instance = new GameCamera { renderCamera = camera };
        }

        internal void Enter(string room)
        {
            if (!roomsCreated)
            {
                Transform rooms = CreateRooms(World.Root, "LastShiftIndoorRooms", out Transform buildings);
                Roofs.Add("signal", CreateRoom(rooms, buildings, "signal", new Bounds(new Vector3(-10f, 6.69f, -8f), new Vector3(10.4f, 3.22f, 11.4f))));
                Roofs.Add("repair", CreateRoom(rooms, buildings, "repair", new Bounds(new Vector3(-49f, 2.14f, 6f), new Vector3(18.4f, 4.52f, 15.4f))));
                Roofs.Add("bond", CreateRoom(rooms, buildings, "bond", new Bounds(new Vector3(51f, 2.49f, 17f), new Vector3(20.4f, 5.22f, 16.4f))));
                Roofs.Add("bondBay", CreateRoom(rooms, buildings, "bondBay", new Bounds(new Vector3(70f, 0.8f, 9.02f), new Vector3(7.8f, 2.2f, 8.63f))));
                World.IndoorRoofs = new RaidIndoorRoofs(World.Root, false);
                roomsCreated = true;
            }
            Vector3 feet = Vector3.up * (100 + entries++);
            if (room != null)
            {
                GameObject volume = GameObject.All.Single(go => go != null && go.name == room);
                ActiveBounds = volume.GetComponent<BoxCollider>().bounds;
                feet = ActiveBounds.center;
            }
            World.IndoorRoofs.UpdateFeet(feet);
            Lighting.Tick();
        }

        public void Dispose()
        {
            Lighting.Dispose();
            if (World != null && World.IndoorRoofs != null) World.IndoorRoofs.Dispose();
        }
    }
}
