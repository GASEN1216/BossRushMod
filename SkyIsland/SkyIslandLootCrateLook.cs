using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// COMPAT：天空岛搜刮箱的外观（owner 2026-09-30 方案 A）。
    ///
    /// 交互、库存、鉴定与搜刮界面仍是官方 `InteractableLootbox`（<see cref="SkyIslandRewardCrate.Build"/>）；这里只把官方
    /// 「敌人死亡掉的包」模型藏起来，换成按档次的三种木箱：生活物资是桶箱堆、航务补给是一排长货箱、星工遗存是长着风晶的小货箱。
    /// - 网格在特效小包 skyisland_fx（<see cref="SkyIslandFxAssets.LootCrate"/>），拼自岛上已有的 Tripo 件；
    ///   材质用场景里已加载的同名材质（按名字在地图根下找一次），包里不带着色器。
    /// - 任一样缺失就不换，保留官方包的样子（可失败的装饰，箱子照样能搜）。
    /// - 只有视觉，不加实体碰撞：搜刮点落点只按中心 0.45 m 胶囊验过净空，整只箱子挡路会卡住窄桥。
    ///   朝向在三个候选里挑一个不压墙的，挑不到用第一个。
    /// - 箱子搜空后不跟着消失：官方包空了会隐藏整只（`hideIfEmpty`），木箱留在原地更像地上的东西。
    /// 由 <see cref="SkyIslandScavenging"/> 持有一个实例；材质引用随会话结束失效，不做静态缓存。
    /// </summary>
    internal sealed class SkyIslandLootCrateLook
    {
        internal const string CrateBarrelMaterial = "Sky_TripoCrateBarrel";
        internal const string CoverCratesMaterial = "Sky_TripoCoverCrates";
        internal const string CrystalMaterial = "Sky_TripoCrystalCluster";
        private static readonly Color StarworksGlow = new Color(0.45f, 0.92f, 0.86f, 1f);
        private static readonly float[] YawCandidates = { 0f, 90f, 45f };

        private readonly Transform sceneRoot;
        private Material crateBarrel, coverCrates, crystal;
        private bool resolved, warned;

        internal SkyIslandLootCrateLook(Transform sceneRoot)
        {
            this.sceneRoot = sceneRoot;
        }

        /// <summary>给一只刚建好的搜刮箱换外观；返回是否换成了。</summary>
        internal bool Apply(InteractableLootbox box, SkyIslandLootTier tier, float bearing)
        {
            if (box == null) return false;
            try
            {
                Mesh mesh = SkyIslandFxAssets.LootCrate(tier);
                Material[] materials = MaterialsFor(tier);
                if (mesh == null || materials == null || mesh.subMeshCount != materials.Length)
                {
                    if (!warned)
                    {
                        warned = true;
                        Debug.LogWarning("[SkyIslandLoot] 搜刮箱外观资源不全，保留官方包的样子：mesh=" + (mesh != null)
                            + " materials=" + (materials != null));
                    }
                    return false;
                }
                foreach (Renderer renderer in box.GetComponentsInChildren<Renderer>(true))
                    if (renderer != null && renderer.GetComponentInParent<InteractMarker>() == null) renderer.enabled = false;
                if (box.hideIfEmpty == box.transform) box.hideIfEmpty = null;

                GameObject look = new GameObject("SkyIslandCrateLook");
                look.transform.SetParent(box.transform, false);
                look.transform.rotation = Quaternion.Euler(0f, ClearYaw(box.transform.position, mesh.bounds, bearing), 0f);
                look.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer crate = look.AddComponent<MeshRenderer>();
                crate.sharedMaterials = materials;
                crate.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                crate.receiveShadows = true;
                if (tier == SkyIslandLootTier.Starworks) AddGlow(look.transform, mesh.bounds);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkyIslandLoot] 搜刮箱换外观失败（保留官方包）：" + e.Message);
                return false;
            }
        }

        private Material[] MaterialsFor(SkyIslandLootTier tier)
        {
            Resolve();
            if (tier == SkyIslandLootTier.Supply) return crateBarrel != null ? new[] { crateBarrel } : null;
            if (tier == SkyIslandLootTier.Voyage) return coverCrates != null ? new[] { coverCrates } : null;
            return coverCrates != null && crystal != null ? new[] { coverCrates, crystal } : null;
        }

        /// <summary>地图根下找一次三种材质（约 840 个渲染器，只在第一只箱子建出来时走一遍）。</summary>
        private void Resolve()
        {
            if (resolved || sceneRoot == null) return;
            resolved = true;
            foreach (MeshRenderer renderer in sceneRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == null) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    if (crateBarrel == null && material.name == CrateBarrelMaterial) crateBarrel = material;
                    else if (coverCrates == null && material.name == CoverCratesMaterial) coverCrates = material;
                    else if (crystal == null && material.name == CrystalMaterial) crystal = material;
                }
                if (crateBarrel != null && coverCrates != null && crystal != null) return;
            }
        }

        /// <summary>三个候选朝向里挑一个整只箱子不压墙的；都压墙就用第一个。</summary>
        private static float ClearYaw(Vector3 position, Bounds bounds, float bearing)
        {
            Vector3 half = bounds.extents + new Vector3(0.02f, -0.05f, 0.02f);
            for (int i = 0; i < YawCandidates.Length; i++)
            {
                Quaternion rotation = Quaternion.Euler(0f, bearing + YawCandidates[i], 0f);
                Vector3 centre = position + rotation * bounds.center + Vector3.up * 0.05f;
                if (!Physics.CheckBox(centre, half, rotation, Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask,
                    QueryTriggerInteraction.Ignore)) return bearing + YawCandidates[i];
            }
            return bearing;
        }

        /// <summary>星工遗存：风晶里透出一点青光，远处也认得出是深处的箱子。建一次、没有每帧成本。</summary>
        private static void AddGlow(Transform parent, Bounds bounds)
        {
            GameObject glow = new GameObject("CrateGlow");
            glow.transform.SetParent(parent, false);
            glow.transform.localPosition = new Vector3(bounds.center.x, bounds.max.y * 0.8f, bounds.center.z);
            Light light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = StarworksGlow;
            light.intensity = 0.9f;
            light.range = 3.5f;
            light.shadows = LightShadows.None;
        }
    }
}
