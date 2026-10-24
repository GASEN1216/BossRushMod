using System;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>野外趣事实体材质的按需租约。专用包自含 URP Deferred shader，不依赖别的 Boss 先出现。</summary>
    internal static class FieldAdventureVisualAssets
    {
        internal const string BundleRelativePath = "Assets/ui/field_adventures_visuals";
        internal const string SolidMaterialPath = "assets/fieldadventures/materials/fieldadventures_solid.mat";
        private static AssetBundle bundle;
        private static Material solid;
        private static int references;
        internal static int ActiveLeaseCount { get { return references; } }
        internal static Material Solid
        {
            get
            {
                if (solid == null || references <= 0) throw new InvalidOperationException("field_visual_material_without_owner");
                return solid;
            }
        }
        internal static Material Acquire(GameObject owner)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            FieldAdventureVisualLease existing = owner.GetComponent<FieldAdventureVisualLease>();
            if (existing != null && existing.IsLeased) return Solid;
            try
            {
                if (bundle == null)
                {
                    string path = Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath);
                    if (!File.Exists(path)) throw new InvalidOperationException("field_visual_bundle_missing");
                    bundle = ResourceBundleLoader.LoadFromFile(path);
                    if (bundle == null) throw new InvalidOperationException("field_visual_bundle_unreadable");
                    solid = bundle.LoadAsset<Material>(SolidMaterialPath);
                    if (solid == null || solid.shader == null || !solid.shader.isSupported
                        || solid.shader.name != "BossRush/FieldAdventures/Solid"
                        || solid.FindPass("GBuffer") < 0 || solid.GetFloat("_ZWrite") < 0.5f
                        || solid.GetFloat("_Surface") > 0.5f)
                        throw new InvalidOperationException("field_visual_opaque_deferred_contract");
                }
                FieldAdventureVisualLease lease = existing != null ? existing : owner.AddComponent<FieldAdventureVisualLease>();
                references++;
                lease.Bind();
                return solid;
            }
            catch { if (references == 0) ResetStaticCaches(); throw; }
        }
        internal static void ReleaseOwner(GameObject owner)
        {
            if (owner == null) return;
            FieldAdventureVisualLease lease = owner.GetComponent<FieldAdventureVisualLease>();
            if (lease != null) lease.Release();
        }
        internal static void Release()
        {
            if (references > 0) references--;
            if (references == 0) ResetStaticCaches();
        }
        internal static void ResetStaticCaches()
        {
            // 仍存活的实体持有租约，最后一个显式释放或 OnDestroy 才卸载。
            if (references != 0) return;
            solid = null;
            if (bundle != null) bundle.Unload(true);
            bundle = null;
        }
    }

    /// <summary>租约跟随实体根生命周期，显式 Dispose 与宿主直接销毁都归同一条释放路径。</summary>
    internal sealed class FieldAdventureVisualLease : MonoBehaviour
    {
        internal bool IsLeased { get; private set; }
        internal void Bind() { IsLeased = true; }
        internal void Release()
        {
            if (!IsLeased) return;
            IsLeased = false;
            FieldAdventureVisualAssets.Release();
        }
        private void OnDestroy() { Release(); }
    }
}
