using System;
using System.IO;
using BossRush.Utils;
using Duckov.MiniMaps;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 末班车的剪票处：官方仓库区（`Level_HiddenWarehouse`）西场第二座龙门吊 `ENV/Prfb_Crane_02` 的轨外。
    /// 坐标来自 2026-11-07 离线读官方场景（`docs/design/last-shift-yard/README.md`「官方出击地图选址」）：台在 (148, 0, 245)，
    /// 余响站在 (150, 0, 245)；两点半径 0.55 m、高 1.8 m 内没有可解析的静态碰撞，落在原版导航上。实机要用 F5 探针复核站位与交互提示，
    /// 最近的原版刷怪候选点离台 12.9 m——这里不是安全区。
    /// </summary>
    internal static class LastShiftEntryPoint
    {
        internal const string SceneName = "Level_HiddenWarehouse";
        internal static readonly Vector3 Position = new Vector3(148f, 0f, 245f);
        /// <summary>台面朝东（+X），余响站在台的东侧 2 m；朝向待实机复核。</summary>
        internal const float Yaw = 90f;
        /// <summary>剪票交互就在台上的打孔钳（转轴在模型本地 (-0.55, 1.53, -0.13)，见 LastShiftTicketPunch；钳口约低 0.3 m），
        /// 2026-11-12 实机「交互点不在物体中心」前是台前 0.9 m 的半空中。</summary>
        internal static readonly Vector3 InteractLocal = new Vector3(-0.55f, 1.2f, -0.13f);
        internal const float InteractRadius = 1.3f;
    }

    /// <summary>
    /// 剪票处本体：从入口小包 `lastshift_entry` 实例化作者工程里的 `LastShiftEntry.prefab`（检票桌、打孔钳、站牌、接驳货车），
    /// 外加官方交互点「剪票上末班车」。包跟着根节点走（<see cref="LastShiftEntryBundleLease"/>）；包缺失、读不出或着色器被剥光时
    /// 整个入口不开，不拿几何体顶替（同潮闸镇竹筏）。
    /// </summary>
    internal static class LastShiftEntry
    {
        internal const string BundleRelativePath = "Assets/arenas/lastshift_entry";
        internal const string PrefabPath = "Assets/LastShiftYard/Entry/LastShiftEntry.prefab";
        private const string EntryShader = "BossRush/LastShift/Environment";
        private static readonly Color Lamp = new Color(1f, 0.76f, 0.42f);

        internal static bool IsBundleDeployed() { return File.Exists(Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath)); }

        internal static GameObject Create(Scene scene, ModBehaviour owner, LastShiftYuXiang yuXiang)
        {
            GameObject root = new GameObject("BossRush_LastShiftEntry");
            try
            {
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                root.transform.position = LastShiftEntryPoint.Position;
                root.transform.rotation = Quaternion.Euler(0f, LastShiftEntryPoint.Yaw, 0f);
                var lease = root.AddComponent<LastShiftEntryBundleLease>();
                GameObject prefab = lease.Load(Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath));
                GameObject model = UnityEngine.Object.Instantiate(prefab, root.transform, false);
                model.name = "Entry_Model";
                CheckShaders(model);
                LastShiftEntryCollision.Attach(model.transform);
                LastShiftTicketPunch.Attach(model.transform);
                Transform lamp = model.transform.Find("DeskLamp");
                if (lamp != null)
                {
                    Light light = lamp.gameObject.AddComponent<Light>();
                    light.type = LightType.Point; light.color = Lamp; light.range = 4.5f; light.intensity = 1.1f; light.shadows = LightShadows.None;
                }
                LastShiftEntryInteractable entry = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<LastShiftEntryInteractable>(
                    root.transform, "LastShiftEntry", LastShiftEntryPoint.InteractLocal, x =>
                    {
                        x.Bind(owner, yuXiang);
                        SphereCollider sensor = x.GetComponent<SphereCollider>();
                        if (sensor != null) { sensor.radius = LastShiftEntryPoint.InteractRadius; sensor.isTrigger = true; }
                    });
                if (entry == null) throw new InvalidOperationException("剪票处交互点创建失败");
                yuXiang.AddTalk(root.transform, entry);
                SimplePointOfInterest marker = LastShiftYuXiang.AddMapMarker(root.transform);
                root.SetActive(true);
                // Setup 会刷新原版地图已生成的标签。
                LastShiftYuXiang.SetupMapMarker(marker);
                Debug.Log(LastShiftRaidScene.LogTag + " ENTRY_READY scene=" + scene.name + " handle=" + scene.handle
                    + " raid=" + RaidUtilities.CurrentRaid.ID + " position=" + LastShiftEntryPoint.Position);
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

        internal static void Release(GameObject root)
        {
            if (root == null) return;
            LastShiftEntryBundleLease lease = root.GetComponent<LastShiftEntryBundleLease>();
            if (lease != null) lease.Release();
        }

        /// <summary>口径同 `LastShiftWorld.ApplyLayers`：只认本图环境着色器，且当前显卡能用。</summary>
        private static void CheckShaders(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("剪票处模型没有渲染器");
            foreach (Renderer renderer in renderers)
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) throw new InvalidOperationException("剪票处模型缺少材质：" + renderer.name);
                    Shader shader = material.shader;
                    if (shader == null || shader.name != EntryShader)
                        throw new InvalidOperationException("剪票处材质使用了未登记的着色器 " + (shader == null ? "null" : shader.name) + "：" + material.name);
                    if (!shader.isSupported)
                        throw new InvalidOperationException("当前显卡不支持剪票处着色器 " + shader.name + "，请更新 Mod 资源包");
                }
        }
    }

    internal sealed class LastShiftEntryBundleLease : MonoBehaviour
    {
        private AssetBundle bundle;

        internal GameObject Load(string path)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("缺少剪票处模型包 " + path);
            bundle = ResourceBundleLoader.LoadFromFile(path);
            if (bundle == null) throw new InvalidOperationException("剪票处模型包读不出来 " + path);
            GameObject prefab = bundle.LoadAsset<GameObject>(LastShiftEntry.PrefabPath);
            if (prefab == null) throw new InvalidOperationException("剪票处模型包里没有 " + LastShiftEntry.PrefabPath);
            return prefab;
        }

        internal void Release()
        {
            if (bundle != null) bundle.Unload(true);
            bundle = null;
        }

        private void OnDestroy() { Release(); }
    }

    /// <summary>「剪票上末班车」经余响的官方对话确认。读完交票收尾才发绷带、剪票、上车。</summary>
    public sealed class LastShiftEntryInteractable : BossRushBuildingInteractableBase
    {
        internal const string NameKey = "BossRush_LastShiftYard_Board";
        private ModBehaviour owner;
        private LastShiftYuXiang yuXiang;
        private bool punching;
        protected override string InteractNameKey { get { return NameKey; } }
        protected override string LogPrefix { get { return LastShiftRaidScene.LogTag + " "; } }

        internal void Bind(ModBehaviour host, LastShiftYuXiang conductor)
        {
            owner = host;
            yuXiang = conductor;
        }

        internal bool CanTalk()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (this == null || !isActiveAndEnabled || owner == null || punching || SceneLoader.IsSceneLoading
                || player == null || player.Health == null || player.Health.IsDead) return false;
            LastShiftSession session = owner.GetComponent<LastShiftSession>();
            return (session == null || session.IsClosed)
                && owner.GetComponent<TidegateSession>() == null && owner.GetComponent<SkyIslandSession>() == null;
        }

        protected override bool IsBuildingInteractable()
        {
            return CanTalk() && yuXiang != null && yuXiang.IsPresent && !yuXiang.IsTalking;
        }

        protected override void OnInteractCompleted()
        {
            if (!IsBuildingInteractable() || !CanBoard()) return;
            yuXiang.Talk(this, true);
        }

        internal bool CanBoard()
        {
            if (!CanTalk()) return false;
            string reason;
            if (LastShiftSession.CanEnter(owner, out reason)) return true;
            Report(reason, true);
            return false;
        }

        internal void Depart(CharacterMainControl player)
        {
            if (!CanBoard() || !yuXiang.IsPresent || player != CharacterMainControl.Main) return;
            punching = true;
            yuXiang.GiveBandage(player);
            // 剪票：钳子压下去「咔哒」一声，看清楚了再转车（0.6 秒）。
            LastShiftEntryBundleLease entryRoot = GetComponentInParent<LastShiftEntryBundleLease>();
            LastShiftTicketPunch punch = entryRoot == null ? null : entryRoot.GetComponentInChildren<LastShiftTicketPunch>();
            if (punch != null) punch.Press();
            LastShiftSounds.Play(LastShiftSounds.TicketPunch, gameObject);
            StartCoroutine(EnterAfterPunch(player));
        }

        private System.Collections.IEnumerator EnterAfterPunch(CharacterMainControl player)
        {
            yield return new WaitForSecondsRealtime(0.6f);
            punching = false;
            if (!CanBoard() || !yuXiang.IsPresent || player != CharacterMainControl.Main) yield break;
            LastShiftSession.Enter(owner, Report);
        }

        private void Report(string message, bool error)
        {
            if (owner != null) owner.ShowMessage(message);
            if (error) Debug.LogWarning(LastShiftRaidScene.LogTag + " " + message);
        }
    }
}
