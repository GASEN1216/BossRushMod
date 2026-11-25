// ============================================================================
// FriendlyNpcNameplates.cs - 友好 NPC 名牌：走近时在头顶显示名字
// ============================================================================
// 模块说明（2026-11-24 玩家许愿第 6 项；2026-11-25 owner 定案）：
//   CR-2026-10-21-003 撤掉过常驻头顶名牌（原版友好 NPC 没有）。玩家许愿想认人，先做成了默认关的 ModConfig 三档；
//   owner 2026-11-25 拍板：不要配置项，直接默认「走近时显示」，名字下面的墨笔横条也去掉，只显示名字。
//   - 走进 8 米淡入，走出 10 米淡出（两道距离错开，站在边上不闪）。远处不显示，不会满屏是字。
//   - 目标只读 NPCNameTagHelper 的登记表（叮当、羽织、快递员、天空岛 / 潮闸镇等常驻居民都在里面登记过），
//     不做场景查找。名牌是屏幕层的一行宣纸色名字（带官方世界文字描边），不画底、不画线。
//   - 每帧先过官方 HUD 显隐与暂停两道门（PersistentHudVisibilityGuard 登记）。不接收点击。
//   - 每帧成本：抄一次登记快照（场上友好 NPC 只有个位数到十几位）再逐个量距离；没有目标、也没有残留名牌时直接返回。
//   生命周期：每次进场景 Refresh() 建一份画布（幂等）；清理 owner 是 CommonNpcRuntimeModule.OnDestroy。
// ============================================================================

using System.Collections.Generic;
using BossRush.Utils;
using TMPro;
using UnityEngine;

namespace BossRush
{
    internal sealed class FriendlyNpcNameplates : MonoBehaviour
    {
        private const string RootName = "BossRush_FriendlyNpcNameplates";
        internal const float NearShowDistance = 8f;
        internal const float NearHideDistance = 10f;
        private const float FadePerSecond = 4f;
        private const float NameFontSize = 18f;
        private const float PlateWidth = 220f;

        private sealed class Plate
        {
            internal GameObject Root;
            internal RectTransform Rect;
            internal CanvasGroup Group;
            internal TextMeshProUGUI Label;
            internal string Name;
            internal bool Shown;
            internal float Alpha;
            internal int Seen;
        }

        private static FriendlyNpcNameplates instance;

        private Canvas canvas;
        private RectTransform canvasRect;
        private readonly Dictionary<int, Plate> plates = new Dictionary<int, Plate>();
        private readonly List<NpcNameplateTarget> targets = new List<NpcNameplateTarget>();
        private readonly List<int> stale = new List<int>();
        private int frame;

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
                instance.canvasRect = root.transform as RectTransform;
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
                Vector3 screen = camera.WorldToScreenPoint(target.Target.position + Vector3.up * target.Height);
                Vector2 local = Vector2.zero;
                bool onScreen = screen.z > 0f && plate.Alpha > 0.001f
                    && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
                if (onScreen) plate.Rect.anchoredPosition = local;
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

        private Plate CreatePlate(string name)
        {
            Plate plate = new Plate();
            plate.Root = ZombieModeUIHelper.CreateRect("Nameplate", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(PlateWidth, 40f));
            plate.Rect = plate.Root.GetComponent<RectTransform>();
            plate.Group = plate.Root.AddComponent<CanvasGroup>();
            plate.Group.alpha = 0f;
            plate.Group.blocksRaycasts = false;
            plate.Group.interactable = false;
            // 只有一行名字：不画底框、不画线（原版场景里不挂牌子，owner 2026-11-25 去掉了名字下的墨笔横条）
            plate.Label = ZombieModeUIHelper.CreateText("Name", plate.Root.transform, name, NameFontSize,
                Vector2.zero, new Vector2(PlateWidth, 30f), TextAlignmentOptions.Center, BossRushInkColors.Paper);
            plate.Label.enableWordWrapping = false;
            plate.Label.overflowMode = TextOverflowModes.Overflow;
            plate.Label.raycastTarget = false;
            BossRushUI.ApplyGameFont(plate.Label);
            BossRushUIKit.ApplyWorldTextOutline(plate.Label);
            plate.Name = name;
            return plate;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            plates.Clear();
        }
    }
}
