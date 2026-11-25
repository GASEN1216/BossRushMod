// ============================================================================
// ArenaExpeditionHud.cs - 出征令局内角标：右侧一枚小朱印写着战帖，下面一行小字写征程
// ============================================================================
// 常驻 HUD（AGENTS §4.17）：每帧经官方 HUD 显隐与暂停两道门（PersistentHudVisibilityGuard 登记）。
// 只在竞技场 Legacy 波次进行中显示；开波时落印一次，通关 / 离场 / 切图由 ArenaExpeditionState.End 收掉。
// 不接收点击（画布 raycaster 关闭）。
// ============================================================================

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    internal sealed class ArenaExpeditionHud : MonoBehaviour
    {
        private const string RootName = "BossRush_ArenaExpeditionHud";
        private const float SealSize = 58f;

        private static ArenaExpeditionHud instance;

        private Canvas canvas;

        internal static void Show()
        {
            ArenaExpeditionRun run = ArenaExpeditionState.Current;
            if (run == null) return;
            Hide();
            try
            {
                Canvas root = BossRushUI.CreateCanvasRoot(RootName, BossRushUILayers.HudOverlay, false);
                instance = root.gameObject.AddComponent<ArenaExpeditionHud>();
                instance.canvas = root;
                instance.Build(run);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[BossRush] [WARNING] 出征令角标创建失败（不影响战斗）: " + e.Message);
                Hide();
            }
        }

        internal static void Hide()
        {
            ArenaExpeditionHud hud = instance;
            instance = null;
            if (hud != null && hud.gameObject != null) Object.Destroy(hud.gameObject);
        }

        private void Build(ArenaExpeditionRun run)
        {
            GameObject anchor = ZombieModeUIHelper.CreateRect("Anchor", canvas.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-30f, 170f), new Vector2(120f, 110f), new Vector2(1f, 0.5f));
            RectTransform anchorRect = anchor.GetComponent<RectTransform>();

            RectTransform seal = BossRushInkSkin.CreateSeal(anchor.transform, ArenaExpeditionTuning.TierSeal(run.Order.Tier), SealSize);
            seal.anchoredPosition = new Vector2(0f, 14f);
            if (run.Order.Tier == ArenaExpeditionTier.Nightmare)
            {
                Image sealImage = seal.GetComponent<Image>();
                if (sealImage != null) sealImage.color = new Color(0.52f, 0.10f, 0.08f, 1f);
            }

            string line = run.Order.IsHell
                ? ArenaExpeditionTuning.LineupName(run.Order.Lineup)
                : (run.HasQueue ? ArenaExpeditionTuning.LengthName(run.Order.Length) : ArenaExpeditionTuning.LengthName(ArenaExpeditionLength.Full));
            if (L10n.IsChinese) line += "\n" + ArenaExpeditionTuning.TierNameEn(run.Order.Tier).ToUpperInvariant();
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText("Line", anchor.transform, line, 14f,
                new Vector2(0f, -36f), new Vector2(120f, 44f), TextAlignmentOptions.Center, BossRushInkColors.Paper);
            BossRushUI.ApplyGameFont(label);
            label.characterSpacing = 4f;
            label.lineSpacing = -8f;
            BossRushUIKit.ApplyWorldTextOutline(label);

            BossRushInkSkin.StampSeal(seal, anchorRect, 0.25f);
        }

        private void Update()
        {
            bool visible = ArenaExpeditionState.Current != null && IsRunLive()
                && !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            canvas.enabled = visible;
        }

        private static bool IsRunLive()
        {
            ModBehaviour host = ModBehaviour.Instance;
            return host != null && host.IsActive;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
