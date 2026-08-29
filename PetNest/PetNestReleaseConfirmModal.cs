// ============================================================================
// PetNestReleaseConfirmModal.cs - 遗种巢放生确认弹窗
// ============================================================================
// 巢容量是硬墙：满了之后玩家此前唯一的腾位手段是押 22% 死亡率的亡命远征等崽死。
// 放生给出一个明确的、可预期的出口，代价是崽永久离开且只退回一部分同血脉遗魂
// （远低于凝一枚蛋所需，因此不构成刷遗魂的路径）。
//
// 纪律（形态照 PetNestRenameModal）：
//   - 只调服务层 TryReleasePet，不自己碰存档；失败原因回抛给面板显示；
//   - 放生不可逆，必须二次确认，且确认页要把「不进纪念碑 + 返还多少遗魂」讲清楚；
//   - 层段用 BossRushUILayers.PetNestModal，压在主面板之上；
//   - **接管输入**：canvas interactive + 独占 modal lease，否则底下的巢面板照样能被点到；
//   - 关闭即销毁，不常驻。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>放生确认弹窗。一次只存在一个。</summary>
    internal sealed class PetNestReleaseConfirmModal : MonoBehaviour
    {
        private const string RootName = "BossRush_PetNestReleaseModal";
        private static readonly Vector2 PanelSize = new Vector2(640f, 320f);

        private static PetNestReleaseConfirmModal _instance;

        private Canvas _canvas;
        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private string _petId;
        private Action _onClosed;

        /// <summary>
        /// 打开放生确认弹窗。petId 查不到时直接返回（不弹空窗）。
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
                _instance = host.AddComponent<PetNestReleaseConfirmModal>();
                _instance._petId = petId;
                _instance._onClosed = onClosed;
                _instance.Build(pet);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 放生弹窗打开失败: " + e.Message);
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
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Release_Title"),
                26f, new Vector2(0f, 116f), new Vector2(580f, 44f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(title);

            // 放生对象名单独一行：避免玩家在多选状态下放错崽
            TextMeshProUGUI target = ZombieModeUIHelper.CreateText(
                "Target", surface.transform,
                PetNestService.GetPetDisplayName(pet),
                22f, new Vector2(0f, 72f), new Vector2(580f, 36f),
                TextAlignmentOptions.Center, BossRushUIColors.Accent);
            BossRushUI.ApplyGameFont(target);

            // 强制披露：不可逆 + 不进纪念碑 + 返还数量（数字取自 Tuning，避免文案与数值两套真相）
            string warn = LocalizationHelper.GetLocalizedText(
                              PetNestTuning.LocalizationPrefix + "Release_Warn")
                          + "  (+" + PetNestTuning.ReleaseSoulRefund + ")";
            TextMeshProUGUI warnText = ZombieModeUIHelper.CreateText(
                "Warn", surface.transform, warn,
                17f, new Vector2(0f, 12f), new Vector2(560f, 72f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(warnText);

            ZombieModeUIHelper.CreateButton(
                "Confirm", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Release_Confirm"),
                new Vector2(0.5f, 0.5f), new Vector2(-130f, -104f), new Vector2(220f, 48f),
                BossRushUIColors.Danger, 19f, new Vector2(210f, 44f),
                delegate { Confirm(); }, true);

            ZombieModeUIHelper.CreateButton(
                "Cancel", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Release_Cancel"),
                new Vector2(0.5f, 0.5f), new Vector2(130f, -104f), new Vector2(220f, 48f),
                BossRushUIColors.SurfaceRaised, 19f, new Vector2(210f, 44f),
                delegate { CloseAndNotify(); }, true);

            _modalLease = ZombieModeUIHelper.ClaimModalInput(_canvas.gameObject, "PetNestRelease");
            BossRushUI.PlayOpenAnimation(surface);
        }

        private void Confirm()
        {
            string reason = null;
            bool ok;
            try
            {
                ok = PetNestService.TryReleasePet(_petId, out reason);
            }
            catch (Exception e)
            {
                ok = false;
                reason = "release_failed:" + e.GetType().Name;
                ModBehaviour.DevLog("[PetNest] 放生失败: " + e.Message);
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
                ModBehaviour.DevLog("[PetNest] 放生后刷新失败: " + e.Message);
            }
        }
    }
}
