// ============================================================================
// PetNestReleaseConfirmModal.cs - 遗种巢放生确认弹窗
// ============================================================================
// 巢容量是硬墙：满了之后玩家此前唯一的腾位手段是押亡命远征的死亡率等崽死。
// 放生给出一个明确的、可预期的出口，代价是崽永久离开且只退回一部分同血脉遗魂
// （远低于凝一枚蛋所需，因此不构成刷遗魂的路径）。
//
// 纪律（形态照 PetNestRenameModal）：
//   - 只调服务层 TryReleasePets（单只也走它），不自己碰存档；失败原因回抛给面板显示；
//   - 批量放生同一个弹窗：名单与返还总数一起披露，一次事务要么全放要么全不动；
//   - 放生不可逆，必须二次确认，且确认页要把「不进纪念碑 + 返还多少遗魂」讲清楚；
//   - 层段用 BossRushUILayers.PetNestModal，压在主面板之上；
//   - **接管输入**：canvas interactive + 独占 modal lease，否则底下的巢面板照样能被点到；
//   - 关闭即销毁，不常驻。
//
// 2026-09-23 审美审查 UA-20 / UA-21 与复核第 11 / 12 项：
//   - 不可逆警告改成 18 号 WarningText，返还遗魂单独一行 SuccessText，正文按实测高度撑开（旧写法 17 号次色居中，最该看见的字最淡）；
//   - 名单用装饰名（炫彩渐变 / 异色金字），最多列 6 只、其余写「等 N 只」，异色 / 炫彩 / 出战中的崽单独提示一行；
//   - 单只异色崽的名字挂流光；ESC 取消；关闭淡出。
// ============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>放生确认弹窗。一次只存在一个。</summary>
    internal sealed class PetNestReleaseConfirmModal : MonoBehaviour
    {
        private const string RootName = "BossRush_PetNestReleaseModal";
        private const float PanelWidth = 680f;
        private const float TextWidth = 600f;
        /// <summary>批量名单最多点名几只，其余并成「等 N 只」。</summary>
        private const int MaxListed = 6;

        private static PetNestReleaseConfirmModal _instance;

        private Canvas _canvas;
        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private PetNestCancelKey _cancelKey;
        private readonly List<string> _petIds = new List<string>();
        private Action _onClosed;
        private bool _closing;

        /// <summary>
        /// 打开放生确认弹窗。petIds 里查不到的崽直接略过，一只都查不到时不弹空窗。
        /// onClosed 在关闭时回调，供面板刷新。
        /// </summary>
        internal static void Open(IList<string> petIds, Action onClosed)
        {
            try
            {
                List<PetNestPetRecord> pets = new List<PetNestPetRecord>();
                if (petIds != null)
                {
                    for (int i = 0; i < petIds.Count; i++)
                    {
                        PetNestPetRecord pet = PetNestService.TryGetPet(petIds[i]);
                        if (pet != null && !pets.Contains(pet)) pets.Add(pet);
                    }
                }
                if (pets.Count == 0) return;

                Close();
                GameObject host = new GameObject(RootName + "_Host");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _instance = host.AddComponent<PetNestReleaseConfirmModal>();
                for (int i = 0; i < pets.Count; i++) _instance._petIds.Add(pets[i].id);
                _instance._onClosed = onClosed;
                _instance.Build(pets);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 放生弹窗打开失败: " + e.Message);
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

        /// <summary>animated 只在玩家点按钮 / ESC 时为 true：画布 0.12 秒淡出（UA-21）。</summary>
        private static void CloseInternal(bool animated)
        {
            try
            {
                if (_instance == null) return;
                PetNestReleaseConfirmModal closing = _instance;
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

        private void Build(List<PetNestPetRecord> pets)
        {
            _canvas = BossRushUI.CreateCanvasRoot(RootName, BossRushUILayers.PetNestModal, true);
            _canvas.transform.SetParent(transform, false);

            BossRushUI.CreateBackdrop(_canvas.transform);

            GameObject surface = ZombieModeUIHelper.CreateRect(
                "Surface", _canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(PanelWidth, 360f));
            Image surfaceImage = surface.AddComponent<Image>();
            surfaceImage.color = BossRushUIColors.Surface;
            BossRushUI.ApplyFramedPanelSkin(surfaceImage, 14, BossRushUISkinPart.Panel);

            // 自上而下排：标题 → 名单 → 稀有提示 → 不可逆警告 → 返还 → 按钮。高度按实测撑开，面板跟着定高。
            float y = 28f;
            TextMeshProUGUI title = CreateBlock(surface.transform, "Title",
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Release_Title"),
                28f, BossRushUIColors.TextPrimary, TextAlignmentOptions.Center, ref y, 40f);
            title.fontStyle = FontStyles.Bold;
            y += 6f;

            // 放生对象名单独成段：避免玩家在多选状态下放错崽。用装饰名，炫彩 / 异色一眼可见（复核第 11 / 12 项）
            TextMeshProUGUI target = CreateBlock(surface.transform, "Target", DescribeTargets(pets),
                22f, BossRushUIColors.TextPrimary, TextAlignmentOptions.Center, ref y, 30f);
            if (pets.Count == 1 && pets[0] != null && pets[0].shiny) PetNestShinyTextShimmer.Attach(target);

            string rare = DescribeRareTargets(pets);
            if (!string.IsNullOrEmpty(rare))
            {
                y += 4f;
                CreateBlock(surface.transform, "Rare", rare,
                    17f, BossRushUIColors.WarningText, TextAlignmentOptions.Center, ref y, 24f);
            }
            y += 12f;

            // 强制披露：不可逆 + 不进纪念碑（WarningText，UA-20）；返还数量单独一行（SuccessText，数字取自 Tuning）
            CreateBlock(surface.transform, "Warn",
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Release_Warn"),
                18f, BossRushUIColors.WarningText, TextAlignmentOptions.Center, ref y, 26f);
            y += 4f;
            int refund = PetNestTuning.ReleaseSoulRefund * pets.Count;
            CreateBlock(surface.transform, "Refund",
                L10n.T("返还遗魂 ", "Souls returned ") + "+" + refund
                    + (pets.Count > 1 ? "（" + PetNestTuning.ReleaseSoulRefund + " × " + pets.Count + "）" : string.Empty),
                18f, BossRushUIColors.SuccessText, TextAlignmentOptions.Center, ref y, 26f);
            y += 22f;

            float panelHeight = Mathf.Max(300f, y + 48f + 28f);
            surface.GetComponent<RectTransform>().sizeDelta = new Vector2(PanelWidth, panelHeight);

            // 放生是危险且不可逆：Danger 实心；取消是次级（深底 + 描边）
            float buttonY = -panelHeight * 0.5f + 28f + 24f;
            ZombieModeUIHelper.CreateButton(
                "Confirm", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Release_Confirm"),
                new Vector2(0.5f, 0.5f), new Vector2(-130f, buttonY), new Vector2(220f, 48f),
                BossRushUIColors.Danger, 19f, new Vector2(210f, 44f),
                delegate { Confirm(); }, true);

            Button cancel = ZombieModeUIHelper.CreateButton(
                "Cancel", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Release_Cancel"),
                new Vector2(0.5f, 0.5f), new Vector2(130f, buttonY), new Vector2(220f, 48f),
                BossRushUIColors.SurfaceRaised, 19f, new Vector2(210f, 44f),
                delegate { Cancel(); }, true);
            BossRushUIKit.StyleSecondaryButton(cancel);

            _modalLease = ZombieModeUIHelper.ClaimModalInput(_canvas.gameObject, "PetNestRelease");
            // ESC / 手柄取消 = 取消（不放生）
            _cancelKey = PetNestCancelKey.Attach(_canvas.gameObject, delegate { Cancel(); },
                delegate { return PetNestHatchRevealView.IsOpen || PetNestExpeditionRevealView.IsOpen; });
            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>
        /// 从面板顶边往下排一段文字：关掉自动缩字、按实测高度撑开（不小于 minHeight），y 累加到这段的下沿。
        /// </summary>
        private static TextMeshProUGUI CreateBlock(Transform parent, string name, string text, float fontSize,
            Color color, TextAlignmentOptions alignment, ref float y, float minHeight)
        {
            TextMeshProUGUI label = ZombieModeUIHelper.CreateText(
                name, parent, text, fontSize,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -y), new Vector2(TextWidth, minHeight),
                alignment, color);
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -y);
            BossRushUI.ApplyGameFont(label);
            float height = BossRushUI.MeasureTextHeight(label, TextWidth, minHeight);
            y += height;
            return label;
        }

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
                ok = PetNestService.TryReleasePets(_petIds, out reason);
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

        /// <summary>
        /// 单只写装饰名；多只写「N 只：甲、乙、丙……等 M 只」，最多点名 MaxListed 只。
        /// 名单一律用装饰名（炫彩渐变 / 异色金字），出战中的崽后面标「（出战中）」（复核第 11 项：旧写法是裸名、只列 4 只）。
        /// </summary>
        private static string DescribeTargets(List<PetNestPetRecord> pets)
        {
            string deployedId = PetNestService.Nest.deployedPetId;
            if (pets.Count == 1) return DescribeOne(pets[0], deployedId);
            string names = string.Empty;
            int listed = 0;
            for (int i = 0; i < pets.Count && listed < MaxListed; i++)
            {
                if (pets[i] == null) continue;
                if (listed > 0) names += L10n.T("、", ", ");
                names += DescribeOne(pets[i], deployedId);
                listed++;
            }
            if (pets.Count > listed)
            {
                names += L10n.T("……等 " + pets.Count + " 只", " ... " + pets.Count + " in total");
            }
            return L10n.T(pets.Count + " 只：", pets.Count + " cubs: ") + names;
        }

        private static string DescribeOne(PetNestPetRecord pet, string deployedId)
        {
            if (pet == null) return string.Empty;
            string name = PetNestService.GetDecoratedPetName(pet);
            if (string.Equals(pet.id, deployedId, StringComparison.Ordinal))
            {
                name += L10n.T("（出战中）", " (deployed)");
            }
            return name;
        }

        /// <summary>名单里的稀有崽与出战崽单独点一行；都没有时返回 null（不占一行）。</summary>
        private static string DescribeRareTargets(List<PetNestPetRecord> pets)
        {
            int shiny = 0, chroma = 0, deployed = 0;
            string deployedId = PetNestService.Nest.deployedPetId;
            for (int i = 0; i < pets.Count; i++)
            {
                PetNestPetRecord pet = pets[i];
                if (pet == null) continue;
                if (pet.shiny) shiny++;
                if (PetNestChroma.HasChroma(pet)) chroma++;
                if (string.Equals(pet.id, deployedId, StringComparison.Ordinal)) deployed++;
            }
            if (shiny == 0 && chroma == 0 && deployed == 0) return null;
            string cn = "注意：名单里有", en = "Heads up: this includes";
            bool first = true;
            if (shiny > 0) { cn += " 异色 " + shiny + " 只"; en += " " + shiny + " shiny"; first = false; }
            if (chroma > 0) { cn += (first ? " " : "、") + "炫彩 " + chroma + " 只"; en += (first ? " " : ", ") + chroma + " chroma"; first = false; }
            if (deployed > 0) { cn += (first ? " " : "、") + "出战中 " + deployed + " 只"; en += (first ? " " : ", ") + deployed + " deployed"; }
            return L10n.T(cn, en + ".");
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
                ModBehaviour.DevLog("[PetNest] 放生后刷新失败: " + e.Message);
            }
        }
    }
}
