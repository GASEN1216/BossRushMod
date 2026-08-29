// ============================================================================
// PetNestHatchRevealView.cs - 孵化揭晓演出（实施计划 步骤 11）
// ============================================================================
// 借官方 LotteryBox 的**六段事件流语言**（onBegin → onRollBegin → onRollStep →
// onShowResult → onPickup → onEnd）自建节奏。不复用 LotteryBox 本体：
// 它的奖池与开启全是私有序列化字段，六段 UnityEvent 存在但无法注入自定义物品。
//
// 硬约束（tests/PetNestRevealIdempotencyGuard.py 守卫）：
//   - **只回放已 commit 的结果**：本文件不得出现任何 roll 符号
//     （Random.value / Random.Range）与任何写档符号（Commit / Store / SavesSystem）；
//   - 结果对象是服务层给的只读快照，演出层不改它；
//   - 演出中断（切图、关面板、宿主销毁）不影响已落档的结果——崽已经在巢里了。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>孵化揭晓演出。六段节奏，纯回放。</summary>
    internal sealed class PetNestHatchRevealView : MonoBehaviour
    {
        #region 六段节奏（时长草案，待 owner 审定）

        private const float BeginSeconds = 0.35f;
        private const float RollBeginSeconds = 0.4f;
        private const float RollStepSeconds = 0.12f;
        private const int RollStepCount = 9;
        private const float ShowResultSeconds = 1.1f;
        private const float PickupSeconds = 0.8f;

        #endregion

        private static PetNestHatchRevealView _instance;

        private Canvas _canvas;
        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private TextMeshProUGUI _rollText;
        private TextMeshProUGUI _resultText;
        private TextMeshProUGUI _detailText;
        private PetNestHatchResult _result;

        /// <summary>播放一次孵化揭晓。result 为 null 时直接返回。</summary>
        internal static void Play(PetNestHatchResult result)
        {
            if (result == null || result.Pet == null) return;
            try
            {
                Stop();
                GameObject host = new GameObject("BossRush_PetNestHatchReveal");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _instance = host.AddComponent<PetNestHatchRevealView>();
                _instance._result = result;
                _instance.Build();
                _instance.StartCoroutine(_instance.PlayRoutine());
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 孵化演出启动失败: " + e.Message);
                Stop();
            }
        }

        /// <summary>中断并销毁演出。幂等。结果已落档，中断无副作用。</summary>
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
                "BossRush_PetNestHatchRevealCanvas", BossRushUILayers.PetNestModal, true);
            _canvas.transform.SetParent(transform, false);

            BossRushUI.CreateBackdrop(_canvas.transform);

            GameObject surface = ZombieModeUIHelper.CreateRect(
                "Surface", _canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(760f, 380f));
            Image image = surface.AddComponent<Image>();
            image.color = BossRushUIColors.Surface;
            BossRushUI.ApplyPanelSkin(image, 14);

            _rollText = ZombieModeUIHelper.CreateText(
                "Roll", surface.transform, string.Empty, 40f,
                new Vector2(0f, 70f), new Vector2(700f, 60f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(_rollText);

            _resultText = ZombieModeUIHelper.CreateText(
                "Result", surface.transform, string.Empty, 34f,
                new Vector2(0f, 0f), new Vector2(700f, 52f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(_resultText);

            _detailText = ZombieModeUIHelper.CreateText(
                "Detail", surface.transform, string.Empty, 20f,
                new Vector2(0f, -70f), new Vector2(700f, 90f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(_detailText);

            ZombieModeUIHelper.CreateButton(
                "Skip", surface.transform, L10n.T("跳过", "Skip"),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(160f, 44f),
                BossRushUIColors.SurfaceRaised, 18f, new Vector2(150f, 40f),
                delegate { Stop(); }, true);

            // 接管输入：遮罩只是"看起来"挡住了，raycaster 关掉的话下面仍然活着的
            // 孵化面板照样能被盲点到——列表刚好在这一刻重排，玩家会静默连吞第二枚蛋。
            _modalLease = ZombieModeUIHelper.ClaimModalInput(_canvas.gameObject, "PetNestHatchReveal");

            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>六段节奏。全程只读 _result，不 roll、不写档。</summary>
        private IEnumerator PlayRoutine()
        {
            // 1) onBegin
            SetText(_rollText, L10n.T("蛋壳在动……", "The shell is moving..."));
            yield return new WaitForSecondsRealtime(BeginSeconds);

            // 2) onRollBegin
            SetText(_rollText, L10n.T("血脉正在显形", "A bloodline is taking shape"));
            yield return new WaitForSecondsRealtime(RollBeginSeconds);

            // 3) onRollStep ×N：滚动展示血脉名，纯视觉，与结果无关
            IList<PetNestLineageInfo> lineages = PetNestLineageCatalog.All;
            for (int i = 0; i < RollStepCount; i++)
            {
                string sample = lineages.Count > 0
                    ? lineages[(i * 7 + 3) % lineages.Count].DisplayName
                    : "...";
                SetText(_rollText, sample);
                yield return new WaitForSecondsRealtime(RollStepSeconds);
            }

            // 4) onShowResult：这里第一次显示真结果（已 commit 的那一份）
            SetText(_rollText, _result.LineageDisplayName);
            SetText(_resultText, BuildResultTitle());
            yield return new WaitForSecondsRealtime(ShowResultSeconds);

            // 5) onPickup：出身 / 性格 / 异色
            SetText(_detailText, BuildDetailText());
            yield return new WaitForSecondsRealtime(PickupSeconds);

            // 6) onEnd
            Stop();
        }

        private string BuildResultTitle()
        {
            string name = PetNestService.GetPetDisplayName(_result.Pet);
            if (_result.Shiny)
            {
                return "<color=#F0AE38>" + name + " · "
                    + L10n.T("异色", "Shiny") + "</color>";
            }
            return name;
        }

        private string BuildDetailText()
        {
            string personality = LocalizationHelper.GetLocalizedText(
                PetNestTuning.LocalizationPrefix + "Personality_"
                + (_result.Pet.personalityId ?? string.Empty));

            string text = L10n.T("性格：", "Temperament: ") + personality;
            if (_result.Pet.talents != null)
            {
                for (int i = 0; i < _result.Pet.talents.Count; i++)
                {
                    PetNestTalentEntry t = _result.Pet.talents[i];
                    if (t == null) continue;
                    // 复用面板的同一口径：百分比项存的是小数，直接拼 "%" 会变成 "+0.08%"
                    text += "\n" + t.statKey + PetNestUIPages.FormatModifierValue(t.value, t.percentage);
                }
            }
            if (_result.FromCondense)
            {
                text += "\n" + LocalizationHelper.GetLocalizedText(
                    PetNestTuning.LocalizationPrefix + "CondenseEgg");
            }
            return text;
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
