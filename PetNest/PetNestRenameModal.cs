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

        /// <summary>关闭并销毁。幂等。</summary>
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
            BossRushUI.ApplyPanelSkin(surfaceImage, 14);

            TextMeshProUGUI title = ZombieModeUIHelper.CreateText(
                "Title", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Rename_Title"),
                26f, new Vector2(0f, 104f), new Vector2(560f, 44f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(title);

            TextMeshProUGUI hint = ZombieModeUIHelper.CreateText(
                "Hint", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Rename_Hint"),
                17f, new Vector2(0f, 62f), new Vector2(560f, 34f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(hint);

            BuildInput(surface.transform, pet);

            ZombieModeUIHelper.CreateButton(
                "Confirm", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Rename_Confirm"),
                new Vector2(0.5f, 0.5f), new Vector2(-130f, -96f), new Vector2(220f, 48f),
                BossRushUIColors.SurfaceRaised, 19f, new Vector2(210f, 44f),
                delegate { Confirm(); }, true);

            ZombieModeUIHelper.CreateButton(
                "Reset", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Rename_Reset"),
                new Vector2(0.5f, 0.5f), new Vector2(130f, -96f), new Vector2(220f, 48f),
                BossRushUIColors.SurfaceRaised, 19f, new Vector2(210f, 44f),
                delegate { ResetToDefault(); }, true);

            _modalLease = ZombieModeUIHelper.ClaimModalInput(_canvas.gameObject, "PetNestRename");
            BossRushUI.PlayOpenAnimation(surface);
        }

        private void BuildInput(Transform parent, PetNestPetRecord pet)
        {
            GameObject border = ZombieModeUIHelper.CreateRect(
                "InputBorder", parent, new Vector2(0.5f, 0.5f), new Vector2(520f, 52f));
            border.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 8f);
            Image borderImage = border.AddComponent<Image>();
            borderImage.color = BossRushUIColors.Accent;

            GameObject inputObj = ZombieModeUIHelper.CreateRect(
                "Input", border.transform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, new Vector2(-4f, -4f), new Vector2(0.5f, 0.5f));
            Image inputBg = inputObj.AddComponent<Image>();
            inputBg.color = BossRushUIColors.SurfaceRaised;

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
            _field.ActivateInputField();
        }

        private void Confirm()
        {
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
            Action callback = _onClosed;
            Close();
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
