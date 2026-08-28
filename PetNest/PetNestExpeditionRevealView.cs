// ============================================================================
// PetNestExpeditionRevealView.cs - 远征结算翻牌演出（实施计划 步骤 11）
// ============================================================================
// 借官方 DeathLottery 的翻牌节奏语言，自建实现（只借 UI 语言，不碰它的抽卡逻辑）。
//
// 硬约束（tests/PetNestRevealIdempotencyGuard.py 守卫）：
//   - **只回放已 settled 的结果**：本文件不得出现任何 roll 符号与写档符号；
//   - 唯一允许的服务层写调用是 MarkRevealed——那是"翻完牌把记录移出待翻列表"，
//     不是结算；
//   - 演出中断不影响结果：结果与 settled 标记在结算时就已落档，
//     下次回基地会重新弹出未翻的牌。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>远征结算翻牌演出。逐张翻，纯回放。</summary>
    internal sealed class PetNestExpeditionRevealView : MonoBehaviour
    {
        #region 节奏（时长草案）

        private const float CardEnterSeconds = 0.3f;
        private const float CardFlipSeconds = 0.55f;
        private const float CardHoldSeconds = 1.4f;

        #endregion

        private static PetNestExpeditionRevealView _instance;

        private Canvas _canvas;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _cardText;
        private TextMeshProUGUI _detailText;
        private List<PetNestExpeditionRecord> _pending;

        /// <summary>
        /// 把所有已结算未翻牌的远征逐张翻出来。没有待翻记录时直接返回。
        /// </summary>
        internal static void PlayPending()
        {
            try
            {
                List<PetNestExpeditionRecord> pending = PetNestExpeditionService.GetPendingReveals();
                if (pending == null || pending.Count == 0) return;

                Stop();
                GameObject host = new GameObject("BossRush_PetNestExpeditionReveal");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _instance = host.AddComponent<PetNestExpeditionRevealView>();
                _instance._pending = pending;
                _instance.Build();
                _instance.StartCoroutine(_instance.PlayRoutine());
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 远征翻牌启动失败: " + e.Message);
                Stop();
            }
        }

        /// <summary>中断并销毁演出。幂等；未翻的牌下次回基地会重新弹。</summary>
        internal static void Stop()
        {
            try
            {
                if (_instance == null) return;
                if (_instance.gameObject != null)
                {
                    UnityEngine.Object.Destroy(_instance.gameObject);
                }
            }
            catch (Exception)
            {
                // 销毁失败只丢引用
            }
            finally
            {
                _instance = null;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Build()
        {
            _canvas = BossRushUI.CreateCanvasRoot(
                "BossRush_PetNestExpeditionRevealCanvas", BossRushUILayers.PetNestModal, false);
            _canvas.transform.SetParent(transform, false);

            BossRushUI.CreateBackdrop(_canvas.transform);

            GameObject surface = ZombieModeUIHelper.CreateRect(
                "Surface", _canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(820f, 420f));
            Image image = surface.AddComponent<Image>();
            image.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(image, 14);

            _titleText = ZombieModeUIHelper.CreateText(
                "Title", surface.transform,
                LocalizationHelper.GetLocalizedText(PetNestTuning.LocalizationPrefix + "Page_Expedition"),
                30f, new Vector2(0f, 150f), new Vector2(760f, 46f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(_titleText);

            _cardText = ZombieModeUIHelper.CreateText(
                "Card", surface.transform, string.Empty, 36f,
                new Vector2(0f, 40f), new Vector2(760f, 60f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(_cardText);

            _detailText = ZombieModeUIHelper.CreateText(
                "Detail", surface.transform, string.Empty, 20f,
                new Vector2(0f, -60f), new Vector2(760f, 120f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(_detailText);

            BossRushUI.PlayOpenAnimation(surface);
        }

        private IEnumerator PlayRoutine()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                PetNestExpeditionRecord record = _pending[i];
                if (record == null) continue;

                SetText(_cardText, L10n.T("翻牌……", "Turning the card..."));
                SetText(_detailText, string.Empty);
                yield return new WaitForSecondsRealtime(CardEnterSeconds);

                SetText(_cardText, BuildCardTitle(record));
                yield return new WaitForSecondsRealtime(CardFlipSeconds);

                SetText(_detailText, BuildCardDetail(record));
                yield return new WaitForSecondsRealtime(CardHoldSeconds);

                // 翻完这张才把它移出待翻列表；中途退出的话下次回基地会重新弹
                string reason;
                PetNestExpeditionService.MarkRevealed(record, out reason);
            }

            Stop();
        }

        private static string BuildCardTitle(PetNestExpeditionRecord record)
        {
            PetNestPetRecord pet = PetNestService.TryGetPet(record.petId);
            string name = pet != null ? PetNestService.GetPetDisplayName(pet) : record.petId;

            if (record.outcomeDead)
            {
                // 黑边：没回来
                return "<color=#7A3434>" + name + " · " + L10n.T("没有回来", "Never came back") + "</color>";
            }
            if (record.outcomeInjured)
            {
                return name + " · " + L10n.T("负伤归来", "Returned wounded");
            }
            return name + " · " + L10n.T("平安归来", "Returned safely");
        }

        private static string BuildCardDetail(PetNestExpeditionRecord record)
        {
            string dest = LocalizationHelper.GetLocalizedText(
                PetNestTuning.LocalizationPrefix + "Dest_" + record.destinationId);
            string risk = LocalizationHelper.GetLocalizedText(
                PetNestTuning.LocalizationPrefix + "Risk_" + DescribeRiskSuffix(record.riskTier));
            string deathRateLabel = LocalizationHelper.GetLocalizedText(
                PetNestTuning.LocalizationPrefix + "DeathRateLabel");

            string text = dest + " · " + risk
                + " · " + deathRateLabel + " " + ((int)Mathf.Round(record.deathRate * 100f)) + "%";

            if (record.outcomeCash > 0L)
            {
                text += "\n" + L10n.T("现金", "Cash") + " +" + record.outcomeCash;
            }
            if (record.outcomeLootTypeIds != null && record.outcomeLootTypeIds.Count > 0)
            {
                text += "\n" + L10n.T("战利品", "Loot") + " ×" + record.outcomeLootTypeIds.Count;
            }
            if (record.outcomeDead)
            {
                text += "\n" + LocalizationHelper.GetLocalizedText(
                    PetNestTuning.LocalizationPrefix + "Page_Memorial");
            }
            return text;
        }

        private static string DescribeRiskSuffix(int riskTier)
        {
            switch ((PetNestRiskTier)riskTier)
            {
                case PetNestRiskTier.Rough: return "rough";
                case PetNestRiskTier.Desperate: return "desperate";
                default: return "safe";
            }
        }

        private static void SetText(TextMeshProUGUI target, string value)
        {
            if (target != null) target.text = value;
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            Stop();
        }
    }
}
