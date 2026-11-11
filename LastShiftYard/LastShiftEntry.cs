using System;
using System.IO;
using BossRush.Utils;
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
        internal static readonly Vector3 InteractLocal = new Vector3(0f, 1.0f, 0.9f);
        internal const float InteractRadius = 1.3f;
    }

    /// <summary>
    /// 剪票处本体：从入口小包 `lastshift_entry` 实例化作者工程里的 `LastShiftEntry.prefab`（检票桌、打孔钳、站牌、接驳货车），
    /// 外加官方交互点「剪票上末班车」。包跟着根节点走（<see cref="LastShiftEntryBundleLease"/>）；包缺失、读不出或着色器被剥光时
    /// 整个入口不开，不拿几何体顶替（同潮闸镇竹筏）。余响本人（捏脸 NPC）在第三期接上。
    /// </summary>
    internal static class LastShiftEntry
    {
        internal const string BundleRelativePath = "Assets/arenas/lastshift_entry";
        internal const string PrefabPath = "Assets/LastShiftYard/Entry/LastShiftEntry.prefab";
        private const string EntryShader = "BossRush/LastShift/Environment";
        private static readonly Color Lamp = new Color(1f, 0.76f, 0.42f);

        internal static bool IsBundleDeployed() { return File.Exists(Path.Combine(ModBehaviour.GetModPath(), BundleRelativePath)); }

        internal static GameObject Create(Scene scene, ModBehaviour owner)
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
                Transform lamp = model.transform.Find("DeskLamp");
                if (lamp != null)
                {
                    Light light = lamp.gameObject.AddComponent<Light>();
                    light.type = LightType.Point; light.color = Lamp; light.range = 4.5f; light.intensity = 1.1f; light.shadows = LightShadows.None;
                }
                LastShiftEntryInteractable entry = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<LastShiftEntryInteractable>(
                    root.transform, "LastShiftEntry", LastShiftEntryPoint.InteractLocal, x =>
                    {
                        x.Bind(owner);
                        SphereCollider sensor = x.GetComponent<SphereCollider>();
                        if (sensor != null) { sensor.radius = LastShiftEntryPoint.InteractRadius; sensor.isTrigger = true; }
                    });
                if (entry == null) throw new InvalidOperationException("剪票处交互点创建失败");
                LastShiftYuXiang.AddTalk(root.transform, owner);
                GameObject marker = LastShiftYuXiang.AddMapMarker(root.transform);
                root.SetActive(true);
                // Setup 必须在激活后调用，避免 OnEnable 与 Setup 双重登记（口径同怪件快递）。
                LastShiftYuXiang.SetupMapMarker(marker);
                Debug.Log(LastShiftRaidScene.LogTag + " ENTRY_READY scene=" + scene.name + " position=" + LastShiftEntryPoint.Position);
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

    /// <summary>
    /// 「剪票上末班车」：每次都先弹确认，写明本趟不结算、只能从编组站撤离回基地（第七轮 owner 采纳）。确认后交给 <see cref="LastShiftSession"/>。
    /// 确认时顺手做三件事：身上带着末班票根就交给余响（剧情收尾那一页先弹出来，读完再上车）、塞一卷台边绷带（每趟一卷）、
    /// 带着押运钢印时提醒侧柜能开一次。
    /// </summary>
    public sealed class LastShiftEntryInteractable : BossRushBuildingInteractableBase
    {
        internal const string NameKey = "BossRush_LastShiftYard_Board";
        private ModBehaviour owner;
        protected override string InteractNameKey { get { return NameKey; } }
        protected override string LogPrefix { get { return LastShiftRaidScene.LogTag + " "; } }
        internal void Bind(ModBehaviour host) { owner = host; }

        protected override bool IsBuildingInteractable()
        {
            return owner != null && owner.GetComponent<LastShiftSession>() == null && owner.GetComponent<TidegateSession>() == null
                && owner.GetComponent<SkyIslandSession>() == null;
        }

        protected override void OnInteractCompleted()
        {
            if (owner == null) return;
            string reason;
            if (!LastShiftSession.CanEnter(owner, out reason)) { Report(reason, true); return; }
            CharacterMainControl player = CharacterMainControl.Main;
            string body = L10n.T("余响：“票递过来。你身上带的货一起走，到了站还得自己找路回来。”",
                "Yu Xiang: \"Hand me your ticket. Whatever you're carrying comes with you, and you'll find your own way back.\"");
            if (!LastShiftStory.Journal && TidegateInventory.Carries(player, BossRushItemIds.LastShiftTicketStub))
                body += L10n.T("\n她盯着你手里那张票：“那张……给我看看。”", "\nShe's looking at the ticket in your hand. \"That one... let me see it.\"");
            if (TidegateInventory.Carries(player, BossRushItemIds.LastShiftStamp))
                body += L10n.T("\n余响：“腰上那枚钢印还在？押运库那只封着的柜子，它能开一回。”", "\nYu Xiang: \"Still got the stamp? It'll open that sealed cabinet in the bond store, once.\"");
            body += L10n.T("\n台边纸箱里有绷带，剪票时她塞给你一卷。", "\nThere are bandages in a box on the desk. She hands you a roll with your ticket.");
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = L10n.T("上末班车去编组站？", "Take the last train to the yard?"),
                Body = body,
                Warning = L10n.T("本趟不结算：上车后只能从编组站撤离回基地。", "This run isn't settled here: once aboard, you can only get back to base by extracting from the yard."),
                ConfirmLabel = L10n.T("剪票上车", "Punch and board"),
                CancelLabel = L10n.T("再等等", "Not yet"),
                Danger = false,
                OnConfirm = Board,
            });
        }

        private void Board()
        {
            if (owner == null) return;
            string reason;
            if (!LastShiftSession.CanEnter(owner, out reason)) { Report(reason, true); return; }
            CharacterMainControl player = CharacterMainControl.Main;
            if (!LastShiftStory.TryHandIn(player)) { Depart(player); return; }
            LastShiftStory.ShowEnding(L10n.T("上车", "Board"), delegate { Depart(CharacterMainControl.Main); });
        }

        private void Depart(CharacterMainControl player)
        {
            if (owner == null) return;
            string reason;
            if (!LastShiftSession.CanEnter(owner, out reason)) { Report(reason, true); return; }
            LastShiftYuXiang.GiveBandage(player);
            LastShiftStory.MarkBoarded();
            LastShiftSession.Enter(owner, Report);
        }

        private void Report(string message, bool error)
        {
            if (owner != null) owner.ShowMessage(message);
            if (error) Debug.LogWarning(LastShiftRaidScene.LogTag + " " + message);
        }
    }
}
