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

        /// <summary>翻牌卡上最多点名的战利品件数，多出来的并成「+N」。</summary>
        private const int MaxLootNamesShown = 3;

        #endregion

        private static PetNestExpeditionRevealView _instance;

        private Canvas _canvas;
        private ZombieModeUIHelper.ModalInputLease _modalLease;
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
                _instance.ReleaseLease();
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
            ReleaseLease();
            if (_instance == this) _instance = null;
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

        private void Build()
        {
            _canvas = BossRushUI.CreateCanvasRoot(
                "BossRush_PetNestExpeditionRevealCanvas", BossRushUILayers.PetNestModal, true);
            _canvas.transform.SetParent(transform, false);

            BossRushUI.CreateBackdrop(_canvas.transform);

            GameObject surface = ZombieModeUIHelper.CreateRect(
                "Surface", _canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(820f, 420f));
            Image image = surface.AddComponent<Image>();
            image.color = BossRushUIColors.Surface;
            BossRushUI.ApplyFramedPanelSkin(image, 14, BossRushUISkinPart.Panel);

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

            // 跳过：一次派 6 只崽就是 13.5 秒全屏遮罩，没有跳过键是不可接受的
            ZombieModeUIHelper.CreateButton(
                "Skip", surface.transform, L10n.T("跳过", "Skip"),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(160f, 44f),
                BossRushUIColors.SurfaceRaised, 18f, new Vector2(150f, 40f),
                delegate { SkipAll(); }, true);

            // 接管输入：遮罩盖住画面却不拦输入的话，玩家会在看不见的情况下
            // 摸到交互点、打开面板、走进战斗
            _modalLease = ZombieModeUIHelper.ClaimModalInput(_canvas.gameObject, "PetNestExpeditionReveal");

            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>
        /// 跳过剩余翻牌：把还没翻的记录一次性标记为已翻，然后收工。
        /// 结果早已落档，跳过只是不看动画，不影响任何数据。
        /// </summary>
        private void SkipAll()
        {
            try
            {
                if (_pending != null)
                {
                    for (int i = 0; i < _pending.Count; i++)
                    {
                        if (_pending[i] == null) continue;
                        string reason;
                        PetNestExpeditionService.MarkRevealed(_pending[i], out reason);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 跳过翻牌失败: " + e.Message);
            }
            Stop();
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
            string name = PetNestExpeditionService.DescribePetName(record);

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
            // 目的地 / 档位 / 死亡率的文案口径与面板共用 PetNestLocalization 的单点入口，
            // 此前这里另写了一份 switch 与一份取整，加档位时必然漏改一处。
            string text = PetNestLocalization.DescribeDestination(record.destinationId)
                + " · " + PetNestLocalization.DescribeRisk(record.riskTier)
                + " · " + LocalizationHelper.GetLocalizedText(
                    PetNestTuning.LocalizationPrefix + "DeathRateLabel")
                + " " + PetNestLocalization.FormatPercent(record.deathRate);

            if (record.outcomeCash > 0L)
            {
                text += "\n" + L10n.T("现金", "Cash") + " +" + record.outcomeCash;
            }
            string loot = DescribeLoot(record);
            if (!string.IsNullOrEmpty(loot))
            {
                text += "\n" + L10n.T("战利品", "Loot") + " " + loot;
            }
            if (record.outcomeDead)
            {
                text += "\n" + LocalizationHelper.GetLocalizedText(
                    PetNestTuning.LocalizationPrefix + "Page_Memorial");
            }
            return text;
        }

        /// <summary>
        /// 战利品清单。**报名字而不是报件数**：翻牌是这趟远征唯一的结果画面，
        /// "战利品 ×1" 等于什么都没说。名字查不到时回落到件数，绝不空着。
        /// 最多列 MaxLootNamesShown 件，其余并成「+N」，避免一行撑爆卡片。
        /// </summary>
        private static string DescribeLoot(PetNestExpeditionRecord record)
        {
            if (record.outcomeLootTypeIds == null || record.outcomeLootTypeIds.Count == 0)
            {
                return null;
            }

            string text = null;
            int shown = 0;
            int hidden = 0;
            for (int i = 0; i < record.outcomeLootTypeIds.Count; i++)
            {
                if (shown >= MaxLootNamesShown)
                {
                    hidden++;
                    continue;
                }
                int count = i < record.outcomeLootCounts.Count ? record.outcomeLootCounts[i] : 1;
                string name = ResolveItemName(record.outcomeLootTypeIds[i]);
                if (string.IsNullOrEmpty(name)) continue;
                if (text != null) text += "，";
                text += count > 1 ? name + " ×" + count : name;
                shown++;
            }
            if (hidden > 0) text += " +" + hidden;

            // 一个名字都查不到（资源未就绪）时回落件数，保证这一行有内容
            return text ?? ("×" + record.outcomeLootTypeIds.Count);
        }

        /// <summary>单件战利品的显示名。查不到返回 null 由调用方回落。</summary>
        private static string ResolveItemName(int typeId)
        {
            if (typeId == RelicEggConfig.TYPE_ID) return RelicEggConfig.GetDisplayName();
            try
            {
                ItemStatsSystem.Item prefab = ItemStatsSystem.ItemAssetsCollection.GetPrefab(typeId);
                string name = prefab != null ? prefab.DisplayName : null;
                return string.IsNullOrEmpty(name) ? null : name;
            }
            catch (Exception)
            {
                // 物品表未就绪：交由调用方回落成件数
                return null;
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
