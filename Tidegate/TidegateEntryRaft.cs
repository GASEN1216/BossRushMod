using System;
using System.IO;
using BossRush.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 「闸伯的竹筏」停靠点。坐标来自对官方基地场景文件的离线读取，实机要用 F5 探针复核（见 `docs/design/tidegate-town/README.md`）。
    /// </summary>
    internal static class TidegateEntryPoint
    {
        /// <summary>竹筏所在的基地子场景：地堡下水道（船点所在子场景，`Duckov_Data/level6`），玩家进下水道时才加载，入口随之挂上。</summary>
        internal const string SceneName = "Base_SceneV2_Sub_01";
        /// <summary>
        /// 下水道东侧水道里、玩家能走到的最北端（再往北被官方水箱与倒地水桶堵死），离官方船点交互约 21 m，
        /// 3 m 内没有别的官方交互点。水面在 y = -0.58，筏子浮在水面上；长边沿水道（z 向），转 180° 让马灯一侧靠走道。
        /// 2026-11-05 离线读官方场景文件得出，实机要用 F5 探针复核站位与交互提示。
        /// </summary>
        internal static readonly Vector3 Position = new Vector3(98.05f, -0.60f, -38.9f);
        internal const float Yaw = 180f;
        /// <summary>交互感应放在走道栏杆上方（世界约 (96.9, 0.4, -38.9)）：官方从玩家脚边 0.5 m 处检索交互，放在筏子中心够不着。</summary>
        internal static readonly Vector3 InteractLocal = new Vector3(1.15f, 1.0f, 0f);
        internal const float InteractRadius = 1.2f;
    }

    /// <summary>
    /// 竹筏本体：从基地入口小包 `tidegate_entry` 实例化作者工程里的 `TidegateRaft.prefab`（竹排、横撑、撑杆、马灯，原点在底面中心、
    /// 长边沿 +Z、马灯在 -Z 端靠 +X 走道一侧），在马灯位 `LanternLight` 挂一盏点光，外加官方交互点。
    /// 包跟着筏子根节点走（<see cref="TidegateEntryBundleLease"/>），筏子销毁即卸包；包缺失、读不出或着色器被剥光时整个入口不开，不拿几何体顶替。
    /// </summary>
    internal static class TidegateEntryRaft
    {
        internal const string BundleRelativePath = "Assets/arenas/tidegate_entry";
        internal const string PrefabPath = "Assets/Tidegate/Entry/TidegateRaft.prefab";
        private const string EntryShader = "BossRush/Tidegate/Environment";
        private static readonly Color Lantern = new Color(1f, 0.72f, 0.38f);

        internal static bool IsBundleDeployed()
        {
            return File.Exists(Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath));
        }

        internal static GameObject Create(Scene scene, ModBehaviour owner, Action<RaidOfficialLighting> prepareLighting)
        {
            GameObject root = new GameObject("BossRush_TidegateRaft");
            try
            {
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = TidegateEntryPoint.Position;
                root.transform.rotation = Quaternion.Euler(0f, TidegateEntryPoint.Yaw, 0f);
                var lease = root.AddComponent<TidegateEntryBundleLease>();
                GameObject prefab = lease.Load(Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath));
                GameObject model = UnityEngine.Object.Instantiate(prefab, root.transform, false);
                model.name = "Raft_Model";
                CheckShaders(model);
                Transform lamp = model.transform.Find("LanternLight");
                if (lamp == null) throw new InvalidOperationException("竹筏模型缺少 LanternLight 挂点");
                Light light = lamp.gameObject.AddComponent<Light>();
                light.type = LightType.Point; light.color = Lantern; light.range = 4f; light.intensity = 1.2f; light.shadows = LightShadows.None;
                TidegateRaftInteractable entry = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<TidegateRaftInteractable>(
                    root.transform, "TidegateRaftEntry", TidegateEntryPoint.InteractLocal, x =>
                    {
                        x.Bind(owner, prepareLighting);
                        // 感应球在走道栏杆上方，站在岸上就够得着；不覆盖到水道里。
                        SphereCollider sensor = x.GetComponent<SphereCollider>();
                        if (sensor != null) { sensor.radius = TidegateEntryPoint.InteractRadius; sensor.isTrigger = true; }
                    });
                if (entry == null) throw new InvalidOperationException("竹筏交互点创建失败");
                // 同一个交互组里的第二项：交还潮汐怀表（官方交互组：主项 + 同组子项，滚轮切换）。
                var group = NPCInteractionGroupHelper.PrepareGroupedInteractionOwner(entry, TidegateRaidScene.LogTag);
                if (group != null && !TidegateStory.WatchDelivered)
                    NPCInteractionGroupHelper.AddSubInteractable<TidegateWatchInteractable>(entry.transform, "TidegateGiveWatch", group, x => x.Bind(owner));
                TidegateZhabo.Spawn(scene);
                root.SetActive(true);
                Debug.Log(TidegateRaidScene.LogTag + " ENTRY_READY scene=" + scene.name + " position=" + TidegateEntryPoint.Position);
                return root;
            }
            catch
            {
                root.SetActive(false);
                Release(root);
                UnityEngine.Object.Destroy(root);
                throw;
            }
        }

        /// <summary>先停用、再卸包、再销毁：从没激活过的根节点不会收到 OnDestroy，所以显式释放。</summary>
        internal static void Release(GameObject root)
        {
            if (root == null) return;
            TidegateEntryBundleLease lease = root.GetComponent<TidegateEntryBundleLease>();
            if (lease != null) lease.Release();
        }

        /// <summary>口径同 `TidegateWorld.ApplyLayers`：只认环境着色器，且当前显卡能用（包里变体被 URP 剥光时 isSupported 为 false）。</summary>
        private static void CheckShaders(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("竹筏模型没有渲染器");
            foreach (Renderer renderer in renderers)
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) throw new InvalidOperationException("竹筏模型缺少材质：" + renderer.name);
                    Shader shader = material.shader;
                    if (shader == null || shader.name != EntryShader)
                        throw new InvalidOperationException("竹筏材质使用了未登记的着色器 " + (shader == null ? "null" : shader.name) + "：" + material.name);
                    if (!shader.isSupported)
                        throw new InvalidOperationException("当前显卡不支持竹筏着色器 " + shader.name + "，请更新 Mod 资源包");
                }
            }
        }
    }

    /// <summary>竹筏模型包的租约：一只筏子一份，跟着筏子根节点走；筏子随下水道子场景卸载或被模块清掉时卸包。</summary>
    internal sealed class TidegateEntryBundleLease : MonoBehaviour
    {
        private AssetBundle bundle;

        internal GameObject Load(string path)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("缺少竹筏模型包 " + path);
            bundle = ResourceBundleLoader.LoadFromFile(path);
            if (bundle == null) throw new InvalidOperationException("竹筏模型包读不出来 " + path);
            GameObject prefab = bundle.LoadAsset<GameObject>(TidegateEntryRaft.PrefabPath);
            if (prefab == null) throw new InvalidOperationException("竹筏模型包里没有 " + TidegateEntryRaft.PrefabPath);
            return prefab;
        }

        internal void Release()
        {
            if (bundle != null) bundle.Unload(true);
            bundle = null;
        }

        private void OnDestroy() { Release(); }
    }
}
