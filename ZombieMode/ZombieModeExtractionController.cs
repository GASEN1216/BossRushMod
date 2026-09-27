using System.Collections;
using Duckov.MiniMaps;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using Duckov.Economy;
using Duckov.UI;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace BossRush
{

    public sealed class ZombieModeExtractionController : MonoBehaviour
    {
        public int RunId;
        public bool IsChanneling;

        public void Initialize(int runId)
        {
            RunId = runId;
            IsChanneling = false;
        }
    }

    public sealed class ZombieModeExtractionOpportunityView : MonoBehaviour
    {
        private int runId;
        private ModBehaviour owner;
        private ZombieModeUIHelper.ModalInputLease inputLease;
        private PetNestCancelKey cancelKey;
        private Button extractButton;
        private bool decided;
        private bool closed;

        public void Initialize(int newRunId, ModBehaviour newOwner)
        {
            runId = newRunId;
            owner = newOwner;
            Build();
            ClaimInputAndPause();
            // ESC / 手柄取消 =「继续战斗」（B-28），并用掉事件，不穿透去开官方暂停菜单。
            cancelKey = PetNestCancelKey.Attach(gameObject, delegate { Choose(false); }, null);
        }

        /// <summary>
        /// 两张后果卡 + 一个主按钮（审美审查 UC-06）：旧版是两块 256×108 的半透明绿 / 橙平涂大按钮，没有任何说明。
        /// 撤离是本屏唯一的主操作（AccentFill），放在右边；继续战斗走次级按钮放左边。两张卡整张可点（B-28）。
        /// </summary>
        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = BossRushUILayers.ZombieModal;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
            gameObject.AddComponent<GraphicRaycaster>();

            GameObject panel = ZombieModeUIHelper.CreateModalSurface(
                "Panel",
                transform,
                new Vector2(680f, 340f),
                ZombieModeUIHelper.WarningColor);

            // 标题行不再垫底色条：直角色条会在 14px 圆角外露出方角（UC-21），分区交给下面的分隔线。
            TextMeshProUGUI titleText = ZombieModeUIHelper.CreateText(
                "Title", panel.transform, L10n.T("BossRush_ZombieMode_Extraction_Title"), 28,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -34f), new Vector2(-48f, 48f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            titleText.fontStyle = FontStyles.Bold;
            ZombieModeUIHelper.CreateSeparator(
                "HeaderDivider",
                panel.transform,
                Vector2.up,
                Vector2.one,
                new Vector2(0f, -68f),
                2f,
                ZombieModeUIHelper.WarningColor);

            int points = owner != null ? owner.GetZombieModePurificationPoints(runId) : 0;
            CreateChoiceCard(panel.transform, "Continue", new Vector2(-152f, -38f), false,
                L10n.T("BossRush_ZombieMode_Extraction_Continue"),
                L10n.T("BossRush_ZombieMode_Extraction_ContinueDesc"));
            CreateChoiceCard(panel.transform, "Extract", new Vector2(152f, -38f), true,
                L10n.T("BossRush_ZombieMode_Extraction_ExtractNow"),
                string.Format(L10n.T("BossRush_ZombieMode_Extraction_ExtractDesc"),
                    Mathf.CeilToInt(ZombieModeTuning.ExtractionCountdownSeconds), points.ToString("N0")));
            BossRushUI.PlayOpenAnimation(panel);
        }

        private void CreateChoiceCard(Transform parent, string name, Vector2 position, bool extract, string title, string description)
        {
            GameObject card = BossRushUI.CreateCard(name + "Card", parent, position, new Vector2(288f, 204f),
                BossRushUIColors.SurfaceRaised, extract ? BossRushUIColors.Accent : BossRushUIColors.Warning);
            TextMeshProUGUI titleText = ZombieModeUIHelper.CreateText("Title", card.transform, title, 20,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(4f, -26f), new Vector2(-40f, 30f),
                TextAlignmentOptions.MidlineLeft, BossRushUIColors.TextPrimary);
            titleText.fontStyle = FontStyles.Bold;
            ZombieModeUIHelper.CreateText("Desc", card.transform, description, 15,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(4f, -82f), new Vector2(-40f, 72f),
                TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);

            UnityEngine.Events.UnityAction choose = delegate { Choose(extract); };
            ZombieModeClickableCard.Make(card, choose);
            Button button = ZombieModeUIHelper.CreateButton(
                name,
                card.transform,
                title,
                new Vector2(0.5f, 0f),
                new Vector2(0f, 34f),
                new Vector2(236f, 42f),
                extract ? BossRushUIColors.AccentFill : BossRushUIColors.SurfaceRaised,
                17,
                new Vector2(220f, 36f),
                choose,
                true);
            if (extract)
            {
                extractButton = button;
            }
            else
            {
                BossRushUIKit.StyleSecondaryButton(button);
            }
            BossRushUIEntranceAnimation.Play(card, extract ? 0.11f : 0.06f, 0.22f, 12f);
        }

        /// <summary>
        /// 两条路的唯一入口（卡片、按钮、ESC）。租约只在宿主受理、调 <see cref="ReleaseInput"/> 收页时才还（B-01）：
        /// 宿主拒绝（信标引导中、撤离点建不出来）时页面与租约都留着，原因浮在「立即撤离」上方；
        /// 宿主既没受理也没给原因，说明页面已失效（阶段对不上），自己还租约收掉，免得时间停着困住玩家。
        /// </summary>
        private void Choose(bool extract)
        {
            if (owner == null || decided)
            {
                return;
            }

            decided = true;
            string refusalKey = null;
            if (extract)
            {
                refusalKey = owner.StartZombieModeExtractionFromUi(runId);
            }
            else
            {
                owner.ContinueZombieModeAfterExtractionOpportunity(runId);
            }

            if (closed)
            {
                return;
            }
            if (!string.IsNullOrEmpty(refusalKey))
            {
                decided = false;
                ZombieModeUiNudge.Flash(extractButton, L10n.T(refusalKey));
                return;
            }
            ReleaseInput();
            BossRushUIKit.PlayCloseAndDestroy(gameObject);
        }

        /// <summary>关闭前由宿主先还输入（UC-07：输入与时间流速当帧恢复，淡出只是表现）。</summary>
        internal void ReleaseInput()
        {
            closed = true;
            if (cancelKey != null)
            {
                cancelKey.Detach();
            }
            RestoreInputState();
        }

        private void ClaimInputAndPause()
        {
            inputLease = ZombieModeUIHelper.ClaimModalInput(gameObject, "ExtractionOpportunity");
        }

        private void RestoreInputState()
        {
            if (inputLease != null)
            {
                inputLease.Release();
                inputLease = null;
            }
        }

        private void OnDestroy()
        {
            RestoreInputState();
        }
    }
}
