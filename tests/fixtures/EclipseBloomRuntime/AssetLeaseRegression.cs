using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BossRush;
using UnityEngine;

internal static class AssetLeaseProgram
{
    private static int assertions;
    private static string validPath;
    private const string MeshPrefix = "assets/eclipsebloom/meshes/eclipsebloom_";
    private const string MaterialPrefix = "assets/eclipsebloom/materials/eclipsebloom_";
    private static int References { get { return (int)typeof(EclipseBloomAuthoredAssets)
        .GetField("references", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null); } }
    private static void Check(bool condition, string text)
    { assertions++; if (!condition) throw new InvalidOperationException(text); }
    internal static int Main()
    {
        try
        {
            validPath = Path.Combine(Directory.GetCurrentDirectory(), "Build/runtime-regressions/EclipseBloomRuntime/LeaseIO", Guid.NewGuid().ToString("N"));
            string bundlePath = Path.Combine(validPath, EclipseBloomAuthoredAssets.BundleRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)); File.WriteAllText(bundlePath, "host bundle IO boundary");
            ModBehaviour.ModPath = validPath;
            IndependentOwners(); FailedLoads(); VisibleGroundWarning(); ShieldFactoryBorrowing(); FactoryOwnedAssetBorrowing(); ShieldIconBorrowing();
            Console.WriteLine("PASS EclipseBloomAuthoredAssets: " + assertions + " assertions");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { EclipseBloomAuthoredAssets.ResetStaticCaches(); }
    }
    private static AssetBundle Healthy()
    {
        var bundle = new AssetBundle();
        foreach (string part in new[] { "petal", "core", "fragment" })
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.up, new Vector3(1f, 0f, 0f), Vector3.forward } };
            mesh.RecalculateBounds(); bundle.Assets.Add(MeshPrefix + part + ".asset", mesh);
        }
        foreach (string name in new[] { "obsidian", "gold" })
            bundle.Assets.Add(MaterialPrefix + name + ".mat", new Material { shader = new Shader() });
        return bundle;
    }
    private static void IndependentOwners()
    {
        Check(ResourceBundleLoader.Loads == 0 && EclipseBloomAuthoredAssets.Petal == null && References == 0,
            "idle content cannot eagerly load authored resources");
        AssetBundle bundle = Healthy(); ResourceBundleLoader.Next = bundle;
        var first = new EclipseBloomAssetManager(null); var second = new EclipseBloomAssetManager(null);
        Check(ResourceBundleLoader.Loads == 1 && References == 2 && first.Petal == second.Petal && first.Gold == second.Gold,
            "concurrent flower instances borrow one loaded authored bundle with two leases");
        Check(bundle.Requests.Count == 5 && bundle.Requests.Distinct().Count() == 5
            && ResourceBundleLoader.LastPath == Path.Combine(validPath, EclipseBloomAuthoredAssets.BundleRelativePath),
            "first lease resolves all exact authored mesh and material names from the mod resource path");
        Mesh dynamicFirst = first.Own(new Mesh()); Mesh dynamicSecond = second.Own(new Mesh());
        first.Dispose(); first.Dispose();
        Check(dynamicFirst == null && dynamicSecond != null && first.Petal != null && second.Gold != null
            && References == 1 && bundle.Unloads == 0,
            "disposing one flower only destroys its dynamic mesh and keeps other borrowed assets alive");
        second.Dispose();
        Check(dynamicSecond == null && first.Petal == null && second.Gold == null && References == 0
            && EclipseBloomAuthoredAssets.Core == null && bundle.Unloads == 1 && bundle.UnloadedAll,
            "last flower lease unloads all authored assets exactly once after dynamic resources are disposed");
        EclipseBloomAuthoredAssets.Release(); EclipseBloomAuthoredAssets.Release(); second.Dispose();
        Check(bundle.Unloads == 1 && References == 0, "repeated Release and Dispose cannot underflow or unload a second time");
    }
    private static void Fails(AssetBundle bundle, string expected)
    {
        ResourceBundleLoader.Next = bundle;
        bool rejected = false;
        try { new EclipseBloomAssetManager(null); }
        catch (InvalidOperationException error) { rejected = error.Message == expected; }
        Check(rejected, "invalid authored resource must fail the real Acquire path: " + expected);
        Check(References == 0 && EclipseBloomAuthoredAssets.Petal == null && EclipseBloomAuthoredAssets.Core == null
            && EclipseBloomAuthoredAssets.Fragment == null && EclipseBloomAuthoredAssets.Gold == null && EclipseBloomAuthoredAssets.Obsidian == null,
            "partial authored load failure must clear every cached asset and cannot acquire a lease");
        if (!ReferenceEquals(bundle, null))
            Check(bundle.Unloads == 1 && bundle.UnloadedAll, "partial authored load must unload the incomplete bundle");
        ResourceBundleLoader.Next = Healthy();
        var recovered = new EclipseBloomAssetManager(null);
        Check(References == 1 && recovered.Petal != null && recovered.Gold != null,
            "a failed authored load must permit a later clean load without stale references");
        recovered.Dispose();
    }
    private static void FailedLoads()
    {
        ModBehaviour.ModPath = Path.Combine(validPath, "missing-root");
        int before = ResourceBundleLoader.Loads; bool missing = false;
        try { EclipseBloomAuthoredAssets.Acquire(); }
        catch (InvalidOperationException error) { missing = error.Message == "eclipse_visual_bundle_missing"; }
        Check(missing && ResourceBundleLoader.Loads == before && References == 0,
            "missing authored bundle is rejected before the Unity loader and without a lease");
        ModBehaviour.ModPath = validPath;
        Fails(null, "eclipse_visual_bundle_unreadable");
        foreach (string part in new[] { "petal", "core", "fragment" })
        {
            AssetBundle bundle = Healthy(); bundle.Assets.Remove(MeshPrefix + part + ".asset");
            Fails(bundle, "eclipse_authored_mesh_missing_" + part);
        }
        AssetBundle tooSmall = Healthy(); ((Mesh)tooSmall.Assets[MeshPrefix + "petal.asset"]).vertices = new Vector3[3];
        Fails(tooSmall, "eclipse_authored_mesh_missing_petal");
        AssetBundle flat = Healthy(); ((Mesh)flat.Assets[MeshPrefix + "core.asset"]).bounds = new Bounds();
        Fails(flat, "eclipse_authored_mesh_missing_core");
        foreach (string material in new[] { "obsidian", "gold" })
        {
            AssetBundle bundle = Healthy(); bundle.Assets.Remove(MaterialPrefix + material + ".mat");
            Fails(bundle, "eclipse_authored_material_missing_" + material);
        }
        AssetBundle noShader = Healthy(); ((Material)noShader.Assets[MaterialPrefix + "gold.mat"]).shader = null;
        Fails(noShader, "eclipse_authored_material_missing_gold");
        AssetBundle unsupported = Healthy(); ((Material)unsupported.Assets[MaterialPrefix + "obsidian.mat"]).shader.isSupported = false;
        Fails(unsupported, "eclipse_authored_material_missing_obsidian");
    }
    private static void VisibleGroundWarning()
    {
        ResourceBundleLoader.Next = Healthy();
        var assets = new EclipseBloomAssetManager(null);
        GameObject parent = new GameObject("actual sector owner");
        var sectors = new EclipseBloomSectorVisual(parent.transform, assets);
        Vector3 point = new Vector3(0f, 0.045f, 6f);
        Check(!sectors.CoversPoint(point), "unprepared sector cannot claim a visible warning plane");
        for (int frame = 0; frame < 12; frame++) { Time.frameCount++; sectors.Apply(15, 15, 0f, 1f, true); }
        Check(sectors.CoversPoint(point), "real projected sector triangles cover the visible ground danger point");
        Renderer fill = ((Renderer[])typeof(EclipseBloomSectorVisual).GetField("_fills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sectors))[0];
        fill.enabled = false;
        Check(!sectors.CoversPoint(point), "disabled ground fill renderer cannot permit invisible damage");
        fill.enabled = true; fill.forceRenderingOff = true;
        Check(!sectors.CoversPoint(point), "fog forced off ground fill renderer cannot permit invisible damage");
        fill.forceRenderingOff = false; parent.SetActive(false);
        Check(!sectors.CoversPoint(point), "inactive warning ancestor cannot permit invisible damage");
        parent.SetActive(true); Check(sectors.CoversPoint(point), "visible renderer recovery restores real ground coverage");
        sectors.Hide(); Check(!sectors.CoversPoint(point), "hidden sectors cannot retain warning coverage");
        UnityEngine.Object.Destroy(parent); assets.Dispose();
    }
    private static void ShieldFactoryBorrowing()
    {
        ResidualSunShieldModel.ResetStaticCaches(); EquipmentFactory.Reset();
        Check(!ResidualSunShieldModel.Bind(null), "null shield item cannot claim a model binding");
        Check(ResidualSunShieldModel.GetTemplate() == null && EquipmentFactory.Loads == 1
            && EquipmentFactory.LastBundle == "residual_sun_shield_model" && EquipmentFactory.LastModel == "ResidualSunShield_Totem",
            "missing shield model requests its exact authored bundle and never substitutes a procedural model");
        var wrongType = new GameObject("unsupported item agent").AddComponent<ItemAgent>();
        EquipmentFactory.Loaded = wrongType;
        Check(ResidualSunShieldModel.GetTemplate() == null, "shield binding rejects an authored wrapper without the native Duckov item agent");
        EquipmentFactory.ThrowQuery = true;
        Check(ResidualSunShieldModel.GetTemplate() == null, "factory query errors reject shield preparation without crashing the host");
        EquipmentFactory.Reset();
        var missingGround = ShieldTemplate("shield without authored ground point", false, 6);
        EquipmentFactory.Loaded = missingGround;
        Check(ResidualSunShieldModel.GetTemplate() == null && missingGround != null
            && missingGround.GetComponent<ResidualSunShieldIcon>() == null,
            "shield template without the authored GroundPoint is rejected before caching or icon attachment");
        UnityEngine.Object.Destroy(missingGround.gameObject);
        var missingPart = ShieldTemplate("shield missing one authored mesh part", true, 5);
        EquipmentFactory.Loaded = missingPart;
        Check(ResidualSunShieldModel.GetTemplate() == null && missingPart != null
            && missingPart.GetComponent<ResidualSunShieldIcon>() == null,
            "shield template missing an authored mesh part is rejected before caching or icon attachment");
        UnityEngine.Object.Destroy(missingPart.gameObject);
        EquipmentFactory.Reset();
        var model = ShieldTemplate("factory authored shield wrapper");
        var graphic = model.GetComponent<ItemGraphicInfo>();
        EquipmentFactory.OnLoad = model;
        Check(ResidualSunShieldModel.GetTemplate() == model && EquipmentFactory.Loads == 1,
            "shield lazily borrows the model from the established factory bundle owner");
        int queries = EquipmentFactory.Queries;
        Check(ResidualSunShieldModel.GetTemplate() == model && EquipmentFactory.Queries == queries
            && model.GetComponents<ResidualSunShieldIcon>().Length == 1,
            "repeated shield template use reuses one factory wrapper and one icon component");
        var item = new GameObject("runtime shield item").AddComponent<ItemStatsSystem.Item>(); item.TypeID = 500110;
        Check(ResidualSunShieldModel.Bind(item) && !item.useSpriteForPickup && item.ItemGraphic == graphic
            && item.AgentUtilities.GetPrefab("EquipmentModel") == model && EquipmentFactory.loadedModels[500110] == model
            && EquipmentFactory.SubVisuals == 1,
            "real shield bind and factory registration connect the native pickup graphic and equipment model before disabling sprite pickup");
        var rejected = new GameObject("failed shield item").AddComponent<ItemStatsSystem.Item>(); rejected.TypeID = 500110;
        EquipmentFactory.RejectGraphic = true;
        Check(!ResidualSunShieldModel.Bind(rejected) && rejected.useSpriteForPickup && rejected.ItemGraphic == null,
            "failed native graphic registration cannot masquerade as a usable shield model");
        EquipmentFactory.RejectGraphic = false;
        ResidualSunShieldModel.ResetStaticCaches();
        Check(model != null && graphic != null && item.ItemGraphic == graphic,
            "shield cache reset cannot destroy the factory wrapper still borrowed by existing item agents");
        Check(ResidualSunShieldModel.GetTemplate() == model && model.GetComponents<ResidualSunShieldIcon>().Length == 1,
            "shield cache reset reborrows the factory wrapper without duplicating its icon");
        UnityEngine.Object.Destroy(model.gameObject); ResidualSunShieldModel.ResetStaticCaches(); EquipmentFactory.Reset();
    }
    private static DuckovItemAgent ShieldTemplate(string name, bool withGroundPoint = true, int parts = 6)
    {
        var model = new GameObject(name).AddComponent<DuckovItemAgent>();
        var graphic = model.gameObject.AddComponent<ItemGraphicInfo>();
        if (withGroundPoint)
        {
            graphic.groundPoint = new GameObject("GroundPoint").transform;
            graphic.groundPoint.SetParent(model.transform, false);
        }
        for (int i = 0; i < parts; i++)
        {
            var part = new GameObject("AuthoredPart" + i);
            part.transform.SetParent(model.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = new Mesh();
            if (i == 0) part.SetActive(false);
        }
        return model;
    }
    private static void FactoryOwnedAssetBorrowing()
    {
        const string path = "assets/eclipsebloom/ui/residual_sun_shield_icon.png";
        Check(EquipmentFactory.GetLoadedEquipmentAsset<Sprite>(null, path) == null
            && EquipmentFactory.GetLoadedEquipmentAsset<Sprite>("residual_sun_shield_model", null) == null,
            "factory owned asset lookup rejects empty identity without loading any bundle");
        var foreign = new AssetBundle { name = "unrelated_equipment" };
        var expected = new AssetBundle { name = "RESIDUAL_SUN_SHIELD_MODEL" };
        var sprite = new Sprite(); var wrongSprite = new Sprite();
        foreign.Assets.Add(path, wrongSprite); expected.Assets.Add(path, sprite);
        EquipmentFactory.ownedBundles.Add(foreign); EquipmentFactory.ownedBundles.Add(expected);
        int loads = ResourceBundleLoader.Loads;
        Check(EquipmentFactory.GetLoadedEquipmentAsset<Sprite>("residual_sun_shield_model", path) == sprite
            && foreign.Requests.Count == 0 && expected.Requests.Count == 1 && ResourceBundleLoader.Loads == loads,
            "factory lookup borrows the exact asset from its named owned bundle without acquiring a new bundle");
        Check(EquipmentFactory.GetLoadedEquipmentAsset<Sprite>("residual_sun_shield_model", path) == sprite
            && expected.Unloads == 0 && sprite != null,
            "repeated factory asset queries share the existing resource without unloading its owner");
        Check(EquipmentFactory.GetLoadedEquipmentAsset<Sprite>("missing-bundle", path) == null
            && EquipmentFactory.GetLoadedEquipmentAsset<Sprite>("residual_sun_shield_model", "missing.asset") == null
            && EquipmentFactory.GetLoadedEquipmentAsset<Mesh>("residual_sun_shield_model", path) == null,
            "missing bundle asset or incompatible type cannot borrow an unrelated fallback");
        expected.Unload(true);
        Check(EquipmentFactory.GetLoadedEquipmentAsset<Sprite>("residual_sun_shield_model", path) == null
            && expected.Unloads == 1,
            "destroyed factory bundle is skipped without an extra unload or stale resource return");
        foreign.Unload(true); EquipmentFactory.Reset();
    }
    private static void ShieldIconBorrowing()
    {
        ResidualSunShieldModel.ResetStaticCaches(); EquipmentFactory.Reset();
        var model = ShieldTemplate("authored shield icon owner");
        EquipmentFactory.Loaded = model;
        var item = new GameObject("shield icon item").AddComponent<ItemStatsSystem.Item>();
        var correct = new AssetBundle { name = ResidualSunShieldModel.BundleName };
        var stale = new AssetBundle { name = "production_icons" };
        var texture = new Texture2D { width = 512, height = 512 };
        var sprite = new Sprite { texture = texture };
        correct.Assets.Add(ResidualSunShieldIcon.BundleIconAssetPath, sprite);
        stale.Assets.Add(ResidualSunShieldIcon.BundleIconAssetPath, new Sprite { texture = new Texture2D { width = 512, height = 512 } });
        EquipmentFactory.ownedBundles.Add(stale); EquipmentFactory.ownedBundles.Add(correct);
        Check(ResidualSunShieldIcon.GetSprite(null) == null && ResidualSunShieldIcon.GetSprite(item) == sprite
            && stale.Requests.Count == 0 && correct.Requests.Count == 1,
            "real shield icon borrows the authored 512 Sprite from its dedicated bundle and cannot use a stale production icon");
        Check(ResidualSunShieldIcon.GetSprite(item) == sprite && correct.Requests.Count == 1,
            "repeated shield icon requests reuse one borrowed Sprite without reload or allocation");
        UnityEngine.Object.Destroy(model.GetComponent<ResidualSunShieldIcon>());
        Check(sprite != null && texture != null && correct.Unloads == 0,
            "shield icon component destruction cannot destroy the Sprite texture or equipment factory bundle it borrowed");
        ResidualSunShieldModel.ResetStaticCaches();
        texture.width = 256;
        Check(ResidualSunShieldIcon.GetSprite(item) == null && sprite != null && texture != null,
            "real shield icon rejects incorrect production dimensions without destroying the borrowed resource");
        texture.width = 512;
        Check(ResidualSunShieldIcon.GetSprite(item) == sprite && correct.Unloads == 0,
            "invalid icon dimensions remain retryable when the authored resource becomes valid");
        UnityEngine.Object.Destroy(model.gameObject);
        Check(sprite != null && texture != null && correct.Unloads == 0,
            "destroying all shield wrappers leaves icon ownership with the equipment factory");
        correct.Unload(true); stale.Unload(true); EquipmentFactory.Reset(); ResidualSunShieldModel.ResetStaticCaches();
    }
}
