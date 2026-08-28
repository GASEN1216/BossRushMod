// ============================================================================
// PetNestUI.cs - 遗种巢主面板（实施计划 步骤 10）
// ============================================================================
// 唯一一个会创建 canvas 的遗种巢界面文件（PetNestUIPages.cs 只在既有 surface 内
// 摆内容，不碰 sortingOrder）。
//
// 共享 UI 库纪律（AGENTS.md 4.14）：
//   - sortingOrder 一律引用 BossRushUILayers 常量，禁裸数字；
//   - 颜色走 BossRushUIColors token，遮罩必须是 Backdrop；
//   - 底图走 BossRushUI.ApplyPanelSkin，字体走 ApplyGameFont / GetGameFont；
//   - Canvas 走 BossRushUI.CreateCanvasRoot（内部已调 ConfigureCanvasScaler）；
//   - 模态输入走 ZombieModeUIHelper.ClaimModalInput 的唯一 lease。
//
// 惰性构建：面板只在玩家第一次交互时装配，关闭即销毁 canvas，不常驻。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>遗种巢主面板。四页共用一个 canvas，切页只重画内容区。</summary>
    internal sealed class PetNestUI : MonoBehaviour
    {
        #region 常量与状态

        private const string RootName = "BossRush_PetNestPanel";
        private static readonly Vector2 PanelSize = new Vector2(1180f, 760f);
        private static readonly Vector2 CardSize = new Vector2(1080f, 118f);

        private static PetNestUI _instance;

        private Canvas _canvas;
        private Transform _contentRoot;
        private Transform _actionRoot;
        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private PetNestUIPage _page;
        private string _selectedPetId;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        #endregion

        #region 打开 / 关闭

        /// <summary>由运行时模块在 bootstrap 时把打开器注册进桥。</summary>
        internal static void RegisterOpener()
        {
            PetNestUIBridge.RegisterPageOpener(Open);
        }

        /// <summary>打开指定页。惰性构建：第一次调用才装配 canvas。</summary>
        internal static void Open(PetNestUIPage page)
        {
            try
            {
                if (_instance == null)
                {
                    GameObject host = new GameObject(RootName + "_Host");
                    UnityEngine.Object.DontDestroyOnLoad(host);
                    _instance = host.AddComponent<PetNestUI>();
                    _instance.Build();
                }
                _instance._page = page;
                _instance.Refresh();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 面板打开失败: " + e.Message);
                Close();
            }
        }

        /// <summary>关闭并销毁面板。幂等。</summary>
        internal static void Close()
        {
            try
            {
                if (_instance == null) return;
                _instance.ReleaseLease();
                if (_instance.gameObject != null)
                {
                    UnityEngine.Object.Destroy(_instance.gameObject);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 面板关闭失败: " + e.Message);
            }
            finally
            {
                _instance = null;
            }
        }

        private void ReleaseLease()
        {
            try
            {
                if (_modalLease != null)
                {
                    _modalLease.Release();
                    _modalLease = null;
                }
            }
            catch (Exception)
            {
                // 释放失败也要把引用丢掉，避免二次 Release
            }
        }

        private void OnDestroy()
        {
            // 子弹窗不能比宿主活得久，否则会留一个抢着 modal lease 的孤儿
            PetNestRenameModal.Close();
            ReleaseLease();
            if (_instance == this) _instance = null;
        }

        /// <summary>模块关停时注销打开器，与 RegisterOpener 成对。</summary>
        internal static void UnregisterOpener()
        {
            Close();
            PetNestUIBridge.UnregisterPageOpener();
        }

        #endregion

        #region 构建

        private void Build()
        {
            _canvas = BossRushUI.CreateCanvasRoot(RootName, BossRushUILayers.PetNestPanel, true);
            _canvas.transform.SetParent(transform, false);

            BossRushUI.CreateBackdrop(_canvas.transform);

            GameObject surface = ZombieModeUIHelper.CreateRect(
                "Surface", _canvas.transform, new Vector2(0.5f, 0.5f), PanelSize);
            Image surfaceImage = surface.AddComponent<Image>();
            surfaceImage.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(surfaceImage, 14);

            BuildHeader(surface.transform);
            BuildTabs(surface.transform);

            // 内容区必须能滚动：巢容量上限 24、远征页 9 个档位按钮、博物馆的血脉卡 +
            // 碑文都远超一屏。早先按固定 y 预算铺元素会静默截断——第 5 只之后的崽、
            // 第三个远征目的地、整段纪念碑都会在 UI 上凭空消失。
            _contentRoot = CreateScrollList(
                surface.transform, "Content", new Vector2(0f, -20f), new Vector2(1120f, 520f));
            _actionRoot = CreateScrollList(
                surface.transform, "Actions", new Vector2(0f, -308f), new Vector2(1120f, 130f));

            _modalLease = ZombieModeUIHelper.ClaimModalInput(_canvas.gameObject, "PetNestPanel");
            BossRushUI.PlayOpenAnimation(surface);
        }

        private void BuildHeader(Transform parent)
        {
            TextMeshProUGUI title = ZombieModeUIHelper.CreateText(
                "Title", parent,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "SystemName"),
                34f, new Vector2(-420f, 330f), new Vector2(420f, 52f),
                TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(title);

            ZombieModeUIHelper.CreateButton(
                "Close", parent, L10n.T("关闭", "Close"),
                new Vector2(0.5f, 0.5f), new Vector2(520f, 330f), new Vector2(110f, 44f),
                BossRushUIColors.SurfaceRaised, 20f, new Vector2(100f, 40f),
                delegate { Close(); }, true);
        }

        private void BuildTabs(Transform parent)
        {
            PetNestUIPage[] pages =
            {
                PetNestUIPage.Nest, PetNestUIPage.Hatch,
                PetNestUIPage.Expedition, PetNestUIPage.Museum,
            };
            string[] keys = { "Page_Nest", "Page_Hatch", "Page_Expedition", "Page_Museum" };

            for (int i = 0; i < pages.Length; i++)
            {
                PetNestUIPage page = pages[i];
                ZombieModeUIHelper.CreateButton(
                    "Tab_" + page, parent,
                    LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + keys[i]),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(-420f + i * 200f, 268f), new Vector2(190f, 44f),
                    BossRushUIColors.SurfaceRaised, 20f, new Vector2(180f, 40f),
                    delegate { _page = page; PetNestUIPages.ClearFailure(); Refresh(); }, true);
            }
        }

        #endregion

        #region 刷新

        private void Refresh()
        {
            try
            {
                ClearSpawned();
                PetNestPageContent content = BuildPageContent();
                if (content == null) return;

                // 失败提示排在最前：不给反馈的话，巢满 / 写屏障 / 远征锁定这些失败
                // 在界面上与"点歪了"完全无法区分
                if (!string.IsNullOrEmpty(PetNestUIPages.LastFailureText))
                {
                    SpawnNotice(PetNestUIPages.LastFailureText, BossRushUIColors.Danger);
                }
                if (!string.IsNullOrEmpty(content.Notice))
                {
                    SpawnNotice(content.Notice, BossRushUIColors.Warning);
                }
                if (!string.IsNullOrEmpty(content.Body))
                {
                    SpawnLine(content.Body, BossRushUIColors.TextPrimary);
                }

                // 不再按 y 预算截断：内容区是滚动列表，全部铺出来
                for (int i = 0; i < content.Cards.Count; i++)
                {
                    SpawnCard(content.Cards[i]);
                }
                for (int i = 0; i < content.Lines.Count; i++)
                {
                    SpawnLine(content.Lines[i], BossRushUIColors.TextSecondary);
                }

                SpawnActions(content.Actions);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 面板刷新失败: " + e.Message);
            }
        }

        /// <summary>
        /// 建一个纵向滚动列表：ScrollRect + RectMask2D + VerticalLayoutGroup +
        /// ContentSizeFitter。内容超出可视区时可滚动，绝不静默截断。
        /// </summary>
        private static Transform CreateScrollList(
            Transform parent, string name, Vector2 position, Vector2 size)
        {
            GameObject viewport = ZombieModeUIHelper.CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            viewport.GetComponent<RectTransform>().anchoredPosition = position;
            viewport.AddComponent<RectMask2D>();

            ScrollRect scroll = viewport.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            GameObject content = ZombieModeUIHelper.CreateRect(
                name + "_Content", viewport.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(0f, 0f), new Vector2(0.5f, 1f));

            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = content.GetComponent<RectTransform>();
            return content.transform;
        }

        private PetNestPageContent BuildPageContent()
        {
            switch (_page)
            {
                case PetNestUIPage.Hatch:
                    return PetNestUIPages.BuildHatchPage(Refresh, OnHatched);
                case PetNestUIPage.Expedition:
                    return PetNestUIPages.BuildExpeditionPage(Refresh, ResolveSelectedPetId());
                case PetNestUIPage.Museum:
                    return PetNestUIPages.BuildMuseumPage();
                default:
                    return PetNestUIPages.BuildNestPage(
                        Refresh, ResolveSelectedPetId(), SelectPet, OpenRename);
            }
        }

        /// <summary>选中一只崽作为远征目标。</summary>
        private void SelectPet(string petId)
        {
            _selectedPetId = petId;
        }

        /// <summary>打开命名弹窗。关闭后刷新面板，让新名字立刻可见。</summary>
        private void OpenRename(string petId)
        {
            PetNestRenameModal.Open(petId, Refresh);
        }

        private string ResolveSelectedPetId()
        {
            if (!string.IsNullOrEmpty(_selectedPetId)) return _selectedPetId;
            PetNestPetRecord deployed = PetNestService.DeployedPet;
            if (deployed != null) return deployed.id;
            List<PetNestPetRecord> pets = PetNestService.Pets;
            for (int i = 0; i < pets.Count; i++)
            {
                if (pets[i] != null && pets[i].state == (int)PetNestPetState.InNest)
                {
                    return pets[i].id;
                }
            }
            return null;
        }

        /// <summary>
        /// 孵化成功回调。结果已经 commit，这里只交给演出层回放。
        /// </summary>
        private void OnHatched(PetNestHatchResult result)
        {
            PetNestHatchRevealView.Play(result);
        }

        #endregion

        #region 元素

        private void ClearSpawned()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null) UnityEngine.Object.Destroy(_spawned[i]);
            }
            _spawned.Clear();
        }

        /// <summary>给布局组里的元素设固定高度（VerticalLayoutGroup 按它排布）。</summary>
        private static void SetLayoutHeight(GameObject go, float height)
        {
            LayoutElement element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
        }

        private void SpawnNotice(string text, Color color)
        {
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                "Notice", _contentRoot, text, 20f,
                Vector2.zero, new Vector2(1080f, 52f),
                TextAlignmentOptions.Left, color);
            BossRushUI.ApplyGameFont(label);
            SetLayoutHeight(label.gameObject, 52f);
            _spawned.Add(label.gameObject);
        }

        private void SpawnLine(string text, Color color)
        {
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                "Line", _contentRoot, text, 19f,
                Vector2.zero, new Vector2(1080f, 34f),
                TextAlignmentOptions.Left, color);
            BossRushUI.ApplyGameFont(label);
            SetLayoutHeight(label.gameObject, 34f);
            _spawned.Add(label.gameObject);
        }

        private void SpawnCard(PetNestCardData data)
        {
            if (data == null) return;

            // 选中态（远征目标）用 Warning 描边点出来，否则玩家无从判断"派的是哪只"
            Color accent = data.Selected
                ? BossRushUIColors.Warning
                : (data.Shiny
                    ? BossRushUIColors.RarityLegendary
                    : (data.IsDanger ? BossRushUIColors.Danger : BossRushUIColors.Accent));

            GameObject card = BossRushUI.CreateCard(
                "Card", _contentRoot, Vector2.zero, CardSize,
                BossRushUIColors.SurfaceRaised, accent, true);
            SetLayoutHeight(card, CardSize.y);
            _spawned.Add(card);

            TextMeshProUGUI title = ZombieModeUIHelper.CreateText(
                "Title", card.transform, data.Title, 24f,
                new Vector2(-320f, 36f), new Vector2(680f, 32f),
                TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(title);

            if (!string.IsNullOrEmpty(data.Subtitle))
            {
                TextMeshProUGUI subtitle = ZombieModeUIHelper.CreateText(
                    "Subtitle", card.transform, data.Subtitle, 18f,
                    new Vector2(-320f, 8f), new Vector2(680f, 26f),
                    TextAlignmentOptions.Left, BossRushUIColors.TextSecondary);
                BossRushUI.ApplyGameFont(subtitle);
            }

            if (!string.IsNullOrEmpty(data.Body))
            {
                TextMeshProUGUI body = ZombieModeUIHelper.CreateText(
                    "Body", card.transform, data.Body, 16f,
                    new Vector2(-320f, -28f), new Vector2(680f, 52f),
                    TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
                BossRushUI.ApplyGameFont(body);
            }

            bool hasSecondary = !string.IsNullOrEmpty(data.SecondaryLabel);

            if (!string.IsNullOrEmpty(data.ActionLabel))
            {
                ZombieModeUIHelper.CreateButton(
                    "CardAction", card.transform, data.ActionLabel,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(430f, hasSecondary ? 26f : 0f),
                    new Vector2(180f, hasSecondary ? 44f : 48f),
                    data.OnClick != null ? BossRushUIColors.Accent : BossRushUIColors.Disabled,
                    19f, new Vector2(170f, hasSecondary ? 40f : 44f),
                    data.OnClick != null ? new UnityEngine.Events.UnityAction(data.OnClick) : null,
                    data.OnClick != null);
            }

            if (hasSecondary)
            {
                ZombieModeUIHelper.CreateButton(
                    "CardSecondary", card.transform, data.SecondaryLabel,
                    new Vector2(0.5f, 0.5f), new Vector2(430f, -26f), new Vector2(180f, 44f),
                    data.OnSecondary != null ? BossRushUIColors.SurfaceRaised : BossRushUIColors.Disabled,
                    18f, new Vector2(170f, 40f),
                    data.OnSecondary != null ? new UnityEngine.Events.UnityAction(data.OnSecondary) : null,
                    data.OnSecondary != null);
            }
        }

        /// <summary>
        /// 底部动作按钮。**不截断**：远征页是 3 目的地 × 3 档位 = 9 个按钮，
        /// 早先硬截断 6 个会让第三个目的地「极寒荒原」在整个游戏里不可达。
        /// 动作区是滚动列表，多出来的往下排。
        /// </summary>
        private void SpawnActions(List<PetNestActionData> actions)
        {
            if (actions == null) return;
            for (int i = 0; i < actions.Count; i++)
            {
                PetNestActionData action = actions[i];
                if (action == null) continue;
                Button button = ZombieModeUIHelper.CreateButton(
                    "Action_" + i, _actionRoot, action.Label,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1060f, 46f),
                    action.IsDanger ? BossRushUIColors.Danger : BossRushUIColors.SurfaceRaised,
                    18f, new Vector2(1040f, 42f),
                    action.OnClick != null ? new UnityEngine.Events.UnityAction(action.OnClick) : null,
                    action.Interactable && action.OnClick != null);
                SetLayoutHeight(button.gameObject, 46f);
                _spawned.Add(button.gameObject);
            }
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            Close();
        }

        #endregion
    }
}
