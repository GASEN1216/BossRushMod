// ============================================================================
// FriendlyNpcNameplates.cs - 友好 NPC 名牌：走近时在头顶显示名字，样式与原版一致
// ============================================================================
// 模块说明（2026-11-24 玩家许愿第 6 项；2026-11-25 owner 定案）：
//   CR-2026-10-21-003 撤掉过常驻头顶名牌（原版友好 NPC 没有）。玩家许愿想认人，先做成了默认关的 ModConfig 三档；
//   owner 2026-11-25 拍板：不要配置项，直接默认「走近时显示」，只显示名字。
//   owner 2026-11-25 实机：「名牌的样式和原版 NPC 的不一致」。原版头顶那行名字（小明、Boss）是官方血条预制体
//   HealthBar 上的 nameText，所以名牌直接克隆 HealthBarManager.healthBarPrefab：血条组件先拆、渐隐组件关掉，
//   除那行名字以外的图形一概不画，字体、字号、描边、名字相对锚点的位置都和原版逐项相同；画布缩放照抄官方 HUD 画布。
//   - 走进 8 米淡入，走出 10 米淡出（两道距离错开，站在边上不闪）。远处不显示，不会满屏是字。
//   - 目标只读 NPCNameTagHelper 的登记表（叮当、羽织、快递员、天空岛 / 潮闸镇等常驻居民都在里面登记过），
//     不做场景查找。锚点高度照官方 HealthBar.RefreshOffset：角色有头盔挂点就是挂点高 + 0.5 米，否则用登记高度 - 0.5 米。
//   - 每帧先过官方 HUD 显隐与暂停两道门（PersistentHudVisibilityGuard 登记）。不接收点击。
//   - 每帧成本：抄一次登记快照（场上友好 NPC 只有个位数到十几位）再逐个量距离；没有目标、也没有残留名牌时直接返回。
//   生命周期：每次进场景 Refresh() 建一份画布（幂等）；清理 owner 是 CommonNpcRuntimeModule.OnDestroy。
// ============================================================================

using System.Collections.Generic;
using System.Reflection;
using BossRush.Utils;
using Duckov.UI;
using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class FriendlyNpcNameplates : MonoBehaviour
    {
        private const string RootName = "BossRush_FriendlyNpcNameplates";
        internal const float NearShowDistance = 8f;
        internal const float NearHideDistance = 10f;
        private const float FadePerSecond = 4f;
        /// <summary>官方 HealthBar.screenYOffset：屏幕高的 2% 往上抬。</summary>
        private const float OfficialScreenYOffset = 0.02f;
        /// <summary>官方 HealthBar.RefreshOffset：头盔挂点之上再抬 0.5 米。</summary>
        private const float OfficialHelmetLift = 0.5f;

        private static readonly FieldInfo NameTextField = BossRush.Common.Utils.ReflectionCache.GetField(
            typeof(HealthBar), "nameText", BindingFlags.NonPublic | BindingFlags.Instance);

        private sealed class Plate
        {
            internal GameObject Root;
            internal RectTransform Rect;
            internal CanvasGroup Group;
            internal TextMeshProUGUI Label;
            internal string Name;
            internal float Lift;
            internal bool Shown;
            internal float Alpha;
            internal int Seen;
        }

        private static FriendlyNpcNameplates instance;

        private Canvas canvas;
        private readonly Dictionary<int, Plate> plates = new Dictionary<int, Plate>();
        private readonly List<NpcNameplateTarget> targets = new List<NpcNameplateTarget>();
        private readonly List<int> stale = new List<int>();
        private int frame;
        private int scalerSourceId;

        /// <summary>还没建就建一份名牌画布（跨场景常驻）。幂等。</summary>
        internal static void Refresh()
        {
            if (instance != null) return;
            try
            {
                Canvas root = BossRushUI.CreateCanvasRoot(RootName, BossRushUILayers.WorldOverlay, false);
                Object.DontDestroyOnLoad(root.gameObject);
                instance = root.gameObject.AddComponent<FriendlyNpcNameplates>();
                instance.canvas = root;
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[NpcNameplates] [WARNING] 名牌画布创建失败: " + e.Message);
                Shutdown();
            }
        }

        /// <summary>拆掉名牌（Mod 卸载）。</summary>
        internal static void Shutdown()
        {
            FriendlyNpcNameplates plates = instance;
            instance = null;
            if (plates != null && plates.gameObject != null) Object.Destroy(plates.gameObject);
        }

        /// <summary>
        /// 下一帧这块名牌该不该亮。两道距离：没亮时走进 8 米才亮，亮着时走出 10 米才灭。
        /// </summary>
        internal static bool NextShown(bool shown, float distance)
        {
            return shown ? distance < NearHideDistance : distance <= NearShowDistance;
        }

        private void Update()
        {
            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            canvas.enabled = visible;
            if (!visible) return;
            Tick();
        }

        private void Tick()
        {
            CharacterMainControl main = CharacterMainControl.Main;
            Camera camera = Camera.main;
            if (main == null || camera == null)
            {
                canvas.enabled = false;
                return;
            }
            NPCNameTagHelper.CopyNameplateTargets(targets);
            if (targets.Count == 0 && plates.Count == 0) return;
            frame++;
            Vector3 origin = main.transform.position;
            float step = Mathf.Min(Time.unscaledDeltaTime, 0.05f) * FadePerSecond;
            float screenLift = OfficialScreenYOffset * Screen.height;
            for (int i = 0; i < targets.Count; i++)
            {
                NpcNameplateTarget target = targets[i];
                int id = target.Target.GetInstanceID();
                Plate plate;
                plates.TryGetValue(id, out plate);
                float distance = Vector3.Distance(origin, target.Target.position);
                bool shown = NextShown(plate != null && plate.Shown, distance);
                if (plate == null)
                {
                    if (!shown) continue;
                    plate = CreatePlate(target.Name);
                    if (plate == null) continue;
                    plate.Lift = OfficialLift(target);
                    plates[id] = plate;
                }
                plate.Seen = frame;
                plate.Shown = shown;
                plate.Alpha = Mathf.MoveTowards(plate.Alpha, shown ? 1f : 0f, step);
                if (!string.Equals(plate.Name, target.Name, System.StringComparison.Ordinal))
                {
                    plate.Name = target.Name;
                    plate.Label.text = target.Name;
                }
                // 与官方 HealthBar.UpdatePosition 同一算法：锚点投到屏幕，再往上抬屏幕高的 2%
                Vector3 screen = camera.WorldToScreenPoint(target.Target.position + Vector3.up * plate.Lift);
                bool onScreen = screen.z > 0f && plate.Alpha > 0.001f;
                if (onScreen)
                {
                    screen.y += screenLift;
                    screen.z = 0f;
                    plate.Rect.position = screen;
                }
                plate.Group.alpha = onScreen ? plate.Alpha : 0f;
            }

            // 登记表里没有了（NPC 销毁 / 切图）或淡尽了的名牌收掉
            stale.Clear();
            foreach (KeyValuePair<int, Plate> pair in plates)
            {
                if (pair.Value.Seen != frame || (!pair.Value.Shown && pair.Value.Alpha <= 0f)) stale.Add(pair.Key);
            }
            for (int i = 0; i < stale.Count; i++)
            {
                Plate plate = plates[stale[i]];
                plates.Remove(stale[i]);
                if (plate.Root != null) Object.Destroy(plate.Root);
            }
        }

        /// <summary>官方 HealthBar.RefreshOffset 的口径；不是角色（叮当这类自建模型）就用登记高度往下收 0.5 米。</summary>
        private static float OfficialLift(NpcNameplateTarget target)
        {
            CharacterMainControl character = target.Target.GetComponent<CharacterMainControl>();
            Transform helmet = character != null && character.characterModel != null ? character.characterModel.HelmatSocket : null;
            if (helmet != null) return Vector3.Distance(character.transform.position, helmet.position) + OfficialHelmetLift;
            return Mathf.Max(0.1f, target.Height - OfficialHelmetLift);
        }

        /// <summary>
        /// 克隆一份官方血条，只留名字。拿不到官方血条（该场景没有 HUD）就不挂名牌：宁可不显示，也不画一行和原版不一样的字。
        /// </summary>
        private Plate CreatePlate(string name)
        {
            HealthBarManager manager = HealthBarManager.Instance;
            if (manager == null || manager.healthBarPrefab == null || NameTextField == null) return null;
            MatchOfficialScaler(manager);
            GameObject root = Object.Instantiate(manager.healthBarPrefab.gameObject, canvas.transform, false);
            root.name = "Nameplate";
            HealthBar bar = root.GetComponent<HealthBar>();
            TextMeshProUGUI label = bar != null ? NameTextField.GetValue(bar) as TextMeshProUGUI : null;
            if (label == null)
            {
                Object.Destroy(root);
                return null;
            }
            // 官方血条的逻辑一概不跑：它在 LateUpdate 里没有目标就把自己还给对象池
            bar.enabled = false;
            Object.Destroy(bar);
            FadeGroup[] fades = root.GetComponentsInChildren<FadeGroup>(true);
            for (int i = 0; i < fades.Length; i++) fades[i].enabled = false;
            CanvasGroup[] groups = root.GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i].alpha = 1f;
                groups[i].blocksRaycasts = false;
                groups[i].interactable = false;
            }
            // 只有那行名字：血条底、填充、等级图标、伤害条这些图形全部不画
            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] == label) continue;
                graphics[i].enabled = false;
                graphics[i].raycastTarget = false;
            }
            for (Transform t = label.transform; t != null && t != root.transform; t = t.parent)
            {
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            }
            label.text = name;
            label.raycastTarget = false;

            Plate plate = new Plate();
            plate.Root = root;
            plate.Rect = root.GetComponent<RectTransform>();
            plate.Group = root.GetComponent<CanvasGroup>();
            if (plate.Group == null) plate.Group = root.AddComponent<CanvasGroup>();
            plate.Group.alpha = 0f;
            plate.Group.blocksRaycasts = false;
            plate.Group.interactable = false;
            plate.Label = label;
            plate.Name = name;
            return plate;
        }

        /// <summary>名牌画布的缩放照抄官方血条所在的 HUD 画布，字才和原版一样大。每个场景的血条管理器只抄一次。</summary>
        private void MatchOfficialScaler(HealthBarManager manager)
        {
            int id = manager.GetInstanceID();
            if (id == scalerSourceId) return;
            scalerSourceId = id;
            Canvas official = manager.GetComponentInParent<Canvas>();
            if (official != null) official = official.rootCanvas;
            CanvasScaler from = official != null ? official.GetComponent<CanvasScaler>() : null;
            BossRushUIKit.MatchCanvasScaler(canvas.GetComponent<CanvasScaler>(), from);
        }

        /// <summary>F3 验收读：场上名牌数、名字以外还在画的图形数、名字是否就是官方血条那行（同字体同字号）。</summary>
        internal static void DescribeForValidation(out int plateCount, out int strayGraphics, out bool officialStyle)
        {
            plateCount = 0;
            strayGraphics = 0;
            officialStyle = true;
            FriendlyNpcNameplates self = instance;
            if (self == null) return;
            HealthBarManager manager = HealthBarManager.Instance;
            TextMeshProUGUI template = manager != null && manager.healthBarPrefab != null && NameTextField != null
                ? NameTextField.GetValue(manager.healthBarPrefab) as TextMeshProUGUI : null;
            foreach (Plate plate in self.plates.Values)
            {
                if (plate.Root == null) continue;
                plateCount++;
                Graphic[] graphics = plate.Root.GetComponentsInChildren<Graphic>(true);
                for (int i = 0; i < graphics.Length; i++)
                {
                    if (graphics[i] != plate.Label && graphics[i].enabled) strayGraphics++;
                }
                if (template == null || plate.Label.font != template.font || !Mathf.Approximately(plate.Label.fontSize, template.fontSize))
                    officialStyle = false;
            }
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            plates.Clear();
        }
    }
}
