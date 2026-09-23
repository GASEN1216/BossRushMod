// ============================================================================
// PetNestRenameModal.cs - 遗种巢崽命名弹窗（实施计划 步骤 5 / 步骤 10 接缝）
// ============================================================================
// 计划把「孵化 roll + **命名**」列为本系统卖点，本地化文案也对玩家承诺「起个名字」，
// 但数据层的 PetNestHatchService.TryRename 一度没有任何调用者——写了玩家永远调不到。
// 本文件就是那个缺失的入口。
//
// 纪律：
//   - 只调服务层 TryRename，不自己碰存档；失败原因回抛给面板显示；
//   - 输入框走 TMP_InputField（AGENTS.md 4.14：新建文本一律 TMP，禁 legacy UI.Text），
//     形态照 ZombieMode/ZombieModeCashInvestmentView.cs:217-240 的既有先例；
//   - 层段用 BossRushUILayers.PetNestModal，压在主面板（PetNestPanel）之上；
//   - **接管输入**：canvas interactive + 独占 modal lease，否则底下的巢面板
//     照样能被点到（同 PetNestHatchRevealView 的教训）；
//   - 关闭即销毁，不常驻。
//
// 2026-09-23 审美审查 UA-19 / UA-21：输入框改成圆角底 + Accent 描边（旧写法是没有 sprite 的直角亮青方框）；
// 按钮改成「取消 / 恢复默认 / 确认」三颗，确认是唯一的 AccentFill 主按钮；回车提交、ESC 取消；关闭淡出。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>崽命名弹窗。一次只存在一个。</summary>
    internal sealed class PetNestRenameModal : MonoBehaviour
    {
        private const string RootName = "BossRush_PetNestRenameModal";
        private static readonly Vector2 PanelSize = new Vector2(620f, 300f);

        private static PetNestRenameModal _instance;

        private Canvas _canvas;
        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private PetNestCancelKey _cancelKey;
        private bool _closing;
        private TMP_InputField _field;
        private string _petId;
        private Action _onClosed;

        /// <summary>
        /// 打开命名弹窗。petId 查不到时直接返回（不弹空窗）。
        /// onClosed 在关闭时回调，供面板刷新。
        /// </summary>
        internal static void Open(string petId, Action onClosed)
        {
            try
            {
                PetNestPetRecord pet = PetNestService.TryGetPet(petId);
                if (pet == null) return;

                Close();
                GameObject host = new GameObject(RootName + "_Host");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _instance = host.AddComponent<PetNestRenameModal>();
                _instance._petId = petId;
                _instance._onClosed = onClosed;
                _instance.Build(pet);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 命名弹窗打开失败: " + e.Message);
                Close();
            }
        }

        /// <summary>弹窗是否开着（ESC 优先级判定用）。</summary>
        internal static bool IsOpen
        {
            get { return _instance != null; }
        }

        /// <summary>关闭并销毁。幂等。异常清理 / 宿主销毁走这里（立即销毁）。</summary>
        internal static void Close()
        {
            CloseInternal(false);
        }

        /// <summary>animated 只在玩家点按钮 / 回车 / ESC 时为 true：画布 0.12 秒淡出（UA-21）。</summary>
        private static void CloseInternal(bool animated)
        {
            try
            {
                if (_instance == null) return;
                PetNestRenameModal closing = _instance;
                closing.ReleaseLease();
                if (closing._cancelKey != null) closing._cancelKey.Detach();
                if (closing.gameObject != null)
                {
                    if (animated) PetNestUI.FadeOutAndDestroy(closing.gameObject, closing._canvas);
                    else UnityEngine.Object.Destroy(closing.gameObject);
                }
            }
            catch (Exception)
            {
                // 销毁失败也要丢引用，避免二次 Close
            }
            finally
            {
                _instance = null;
            }
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            Close();
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
                // 释放失败也要丢引用，避免二次 Release
            }
        }

        private void OnDestroy()
        {
            ReleaseLease();
            if (_instance == this) _instance = null;
        }

        private void Build(PetNestPetRecord pet)
        {
            _canvas = BossRushUI.CreateCanvasRoot(RootName, BossRushUILayers.PetNestModal, true);
            _canvas.transform.SetParent(transform, false);

            BossRushUI.CreateBackdrop(_canvas.transform);

            GameObject surface = ZombieModeUIHelper.CreateRect(
                "Surface", _canvas.transform, new Vector2(0.5f, 0.5f), PanelSize);
            Image surfaceImage = surface.AddComponent<Image>();
            surfaceImage.color = BossRushUIColors.Surface;
            BossRushUI.ApplyFramedPanelSkin(surfaceImage, 14, BossRushUISkinPart.Panel);

            TextMeshProUGUI title = ZombieModeUIHelper.CreateText(
                "Title", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Rename_Title"),
                28f, new Vector2(0f, 104f), new Vector2(560f, 44f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            title.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(title);

            TextMeshProUGUI hint = ZombieModeUIHelper.CreateText(
                "Hint", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Rename_Hint"),
                17f, new Vector2(0f, 62f), new Vector2(560f, 34f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(hint);

            BuildInput(surface.transform, pet);

            // 三颗按钮：取消（次级）/ 恢复默认（次级）/ 确认（唯一主按钮 AccentFill）。
            // 旧写法只有「确认」「恢复默认」两颗同色按钮，想不改名只能原样提交（UA-19）。
            Button cancel = ZombieModeUIHelper.CreateButton(
                "Cancel", surface.transform, L10n.T("取消", "Cancel"),
                new Vector2(0.5f, 0.5f), new Vector2(-190f, -96f), new Vector2(170f, 48f),
                BossRushUIColors.SurfaceRaised, 18f, new Vector2(160f, 44f),
                delegate { Cancel(); }, true);
            BossRushUIKit.StyleSecondaryButton(cancel);

            Button reset = ZombieModeUIHelper.CreateButton(
                "Reset", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Rename_Reset"),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -96f), new Vector2(170f, 48f),
                BossRushUIColors.SurfaceRaised, 18f, new Vector2(160f, 44f),
                delegate { ResetToDefault(); }, true);
            BossRushUIKit.StyleSecondaryButton(reset);

            ZombieModeUIHelper.CreateButton(
                "Confirm", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Rename_Confirm"),
                new Vector2(0.5f, 0.5f), new Vector2(190f, -96f), new Vector2(170f, 48f),
                BossRushUIColors.AccentFill, 18f, new Vector2(160f, 44f),
                delegate { Confirm(); }, true);

            _modalLease = ZombieModeUIHelper.ClaimModalInput(_canvas.gameObject, "PetNestRename");
            // ESC / 手柄取消 = 取消（不改名）；上面压着揭晓演出时让给它
            _cancelKey = PetNestCancelKey.Attach(_canvas.gameObject, delegate { Cancel(); },
                delegate { return PetNestHatchRevealView.IsOpen || PetNestExpeditionRevealView.IsOpen; });
            BossRushUI.PlayOpenAnimation(surface);
        }

        private void BuildInput(Transform parent, PetNestPetRecord pet)
        {
            // 圆角输入底 + 一圈 Accent 描边（焦点色）。旧写法外面套一个没有 sprite 的直角亮青方框，
            // 圆角面板里夹着一道 2px 直角线（UA-19）。
            GameObject inputObj = ZombieModeUIHelper.CreateRect(
                "Input", parent, new Vector2(0.5f, 0.5f), new Vector2(520f, 52f));
            inputObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 8f);
            Image inputBg = inputObj.AddComponent<Image>();
            inputBg.color = BossRushUIColors.SurfaceRaised;
            BossRushUI.ApplyPanelSkin(inputBg, 8, BossRushUISkinPart.Button);
            BossRushUI.ApplyPanelStroke(inputBg, 8, BossRushUISkinPart.Button, BossRushUIColors.Accent);

            _field = inputObj.AddComponent<TMP_InputField>();
            _field.contentType = TMP_InputField.ContentType.Standard;
            _field.lineType = TMP_InputField.LineType.SingleLine;
            _field.characterLimit = PetNestTuning.MaxPetNameLength;

            GameObject textArea = ZombieModeUIHelper.CreateRect(
                "TextArea", inputObj.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-16f, -6f), new Vector2(0.5f, 0.5f));
            TextMeshProUGUI inputText = ZombieModeUIHelper.CreateTMPText(
                textArea, string.Empty, 20f, TextAlignmentOptions.MidlineLeft,
                BossRushUIColors.TextPrimary);
            inputText.raycastTarget = false;
            BossRushUI.ApplyGameFont(inputText);

            _field.targetGraphic = inputBg;
            _field.textComponent = inputText;
            _field.textViewport = textArea.GetComponent<RectTransform>();
            _field.customCaretColor = true;
            _field.caretColor = BossRushUIColors.Accent;
            // 预填当前显示名：玩家改名多半是微调，不是从零打
            _field.text = PetNestService.GetPetDisplayName(pet);
            // 回车提交（单行输入框的 onSubmit 只在回车时触发）
            _field.onSubmit.AddListener(delegate { Confirm(); });
            _field.ActivateInputField();
        }

        /// <summary>取消：不改名，直接关（刷新面板也无妨，名字没变）。</summary>
        private void Cancel()
        {
            if (_closing) return;
            CloseAndNotify();
        }

        private void Confirm()
        {
            if (_closing) return;
            string reason = null;
            bool ok;
            try
            {
                ok = PetNestHatchService.TryRename(
                    _petId, _field != null ? _field.text : null, out reason);
            }
            catch (Exception e)
            {
                ok = false;
                reason = "rename_failed:" + e.GetType().Name;
                ModBehaviour.DevLog("[PetNest] 改名失败: " + e.Message);
            }

            PetNestUIPages.NoteExternalFailure(ok, reason);
            CloseAndNotify();
        }

        /// <summary>清空名字 = 恢复血脉默认名（服务层对空名的既有语义）。</summary>
        private void ResetToDefault()
        {
            if (_closing) return;
            string reason = null;
            bool ok;
            try
            {
                ok = PetNestHatchService.TryRename(_petId, null, out reason);
            }
            catch (Exception e)
            {
                ok = false;
                reason = "rename_failed:" + e.GetType().Name;
            }

            PetNestUIPages.NoteExternalFailure(ok, reason);
            CloseAndNotify();
        }

        private void CloseAndNotify()
        {
            _closing = true;
            Action callback = _onClosed;
            CloseInternal(true);
            try
            {
                if (callback != null) callback();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 改名后刷新失败: " + e.Message);
            }
        }
    }
}
