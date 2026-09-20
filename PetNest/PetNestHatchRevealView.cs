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

        // owner 2026-09-20：「孵化动画能不能慢一点，以及显示信息能不能由玩家自己关掉，
        // 根本看不清显示了什么」——整条节奏放慢一倍左右，末段改为**等玩家点关闭**，
        // 不再自动收。跳过按钮保留：想快的人一键到结果。
        private const float BeginSeconds = 0.9f;
        private const float RollBeginSeconds = 1.0f;
        private const float RollStepSeconds = 0.26f;
        private const int RollStepCount = 9;
        private const float ShowResultSeconds = 1.6f;
        private const float PickupSeconds = 0.6f;

        #endregion

        private static PetNestHatchRevealView _instance;

        private Canvas _canvas;
        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private TextMeshProUGUI _rollText;
        private TextMeshProUGUI _resultText;
        private TextMeshProUGUI _detailText;
        private PetNestHatchResult _result;
        private UnityEngine.UI.Button _dismissButton;
        private TextMeshProUGUI _dismissLabel;
        private Coroutine _playRoutine;
        private bool _resultShown;
        private bool _finished;

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
                _instance._playRoutine = _instance.StartCoroutine(_instance.PlayRoutine());
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
                "Surface", _canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(760f, 540f));
            Image image = surface.AddComponent<Image>();
            image.color = BossRushUIColors.Surface;
            BossRushUI.ApplyFramedPanelSkin(image, 14, BossRushUISkinPart.Panel);

            _rollText = ZombieModeUIHelper.CreateText(
                "Roll", surface.transform, string.Empty, 40f,
                new Vector2(0f, 192f), new Vector2(700f, 60f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(_rollText);

            _resultText = ZombieModeUIHelper.CreateText(
                "Result", surface.transform, string.Empty, 34f,
                new Vector2(0f, 126f), new Vector2(700f, 52f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(_resultText);

            _detailText = ZombieModeUIHelper.CreateText(
                "Detail", surface.transform, string.Empty, 20f,
                new Vector2(0f, -35f), new Vector2(700f, 240f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(_detailText);

            // 演出期间是「跳过」，出完结果后改成「关闭」——面板不会自己消失，
            // 玩家可以把性格 / 天赋 / 炫彩慢慢看完再收。
            _dismissButton = ZombieModeUIHelper.CreateButton(
                "Dismiss", surface.transform, L10n.T("跳过", "Skip"),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -218f), new Vector2(180f, 46f),
                BossRushUIColors.SurfaceRaised, 18f, new Vector2(170f, 42f),
                OnDismiss, true);
            if (_dismissButton != null)
            {
                _dismissLabel = _dismissButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }

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
            yield return WaitForPresentation(BeginSeconds);

            // 2) onRollBegin
            SetText(_rollText, L10n.T("血脉正在显形", "A bloodline is taking shape"));
            yield return WaitForPresentation(RollBeginSeconds);

            // 3) onRollStep ×N：滚动展示血脉名，纯视觉，与结果无关
            IList<PetNestLineageInfo> lineages = PetNestLineageCatalog.All;
            for (int i = 0; i < RollStepCount; i++)
            {
                string sample = lineages.Count > 0
                    ? lineages[(i * 7 + 3) % lineages.Count].DisplayName
                    : "...";
                SetText(_rollText, sample);
                yield return WaitForPresentation(RollStepSeconds);
            }

            // 4) onShowResult：这里第一次显示真结果（已 commit 的那一份）
            ShowResult();
            yield return WaitForPresentation(ShowResultSeconds);

            // 5) onPickup：出身 / 性格 / 异色 / 炫彩
            SetText(_detailText, BuildDetailText());
            yield return WaitForPresentation(PickupSeconds);

            // 6) onEnd：**不自动关**。改成等玩家点「关闭」（owner 2026-09-20）。
            CompleteReveal();
            _playRoutine = null;
        }

        private static IEnumerator WaitForPresentation(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                yield return null;
                if (!BossRushUI.IsGamePaused()) elapsed += Time.unscaledDeltaTime;
            }
        }

        private void ShowResult()
        {
            if (_resultShown) return;
            _resultShown = true;
            SetText(_rollText, _result.LineageDisplayName);
            SetText(_resultText, BuildResultTitle());
            if (_result.Shiny) PlayJackpotMusic();
        }

        private void CompleteReveal()
        {
            ShowResult();
            SetText(_detailText, BuildDetailText());
            _finished = true;
            if (_dismissLabel != null)
            {
                _dismissLabel.text = L10n.T("关闭", "Close");
                ZombieModeUIHelper.SetButtonBaseColor(_dismissButton, BossRushUIColors.Accent);
            }
        }

        private void OnDismiss()
        {
            if (_finished) { Stop(); return; }
            if (_playRoutine != null) StopCoroutine(_playRoutine);
            _playRoutine = null;
            CompleteReveal();
        }

        private string BuildResultTitle()
        {
            // 炫彩 / 异色的富文本口径与巢页、HUD 共用 PetNestChroma，不在这里另写一套
            return PetNestService.GetDecoratedPetName(_result.Pet);
        }

        /// <summary>
        /// 抽到大奖的音乐。复用许愿台那一份 Assets/Sounds/lottery/special.mp3，
        /// 不新增音频资产；文件缺失时静默跳过（与许愿台同款判据）。
        /// </summary>
        private static void PlayJackpotMusic()
        {
            try
            {
                string modPath = ModBehaviour.GetModPath();
                if (string.IsNullOrEmpty(modPath)) return;
                string path = System.IO.Path.Combine(
                    System.IO.Path.Combine(System.IO.Path.Combine(modPath, "Assets"), "Sounds"),
                    System.IO.Path.Combine("lottery", "special.mp3"));
                if (!System.IO.File.Exists(path)) return;
                ModBehaviour owner = ModBehaviour.Instance;
                if (owner != null) owner.PlaySoundEffect(path);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 异色大奖音乐播放失败: " + e.Message);
            }
        }

        private string BuildDetailText()
        {
            // 文案口径与巢页共用 PetNestLocalization 的单点入口：
            // 此前这里直接拼英文 statKey，玩家孵出第一只崽看到的是 "PetCapcity+2"。
            string text = L10n.T("性格：", "Temperament: ")
                + PetNestLocalization.DescribePersonality(_result.Pet.personalityId);
            string personalityEffect =
                PetNestLocalization.DescribePersonalityEffect(_result.Pet.personalityId);
            if (!string.IsNullOrEmpty(personalityEffect))
            {
                text += "\n" + personalityEffect;
            }

            if (_result.Pet.talents != null)
            {
                for (int i = 0; i < _result.Pet.talents.Count; i++)
                {
                    PetNestTalentEntry t = _result.Pet.talents[i];
                    if (t == null) continue;
                    text += "\n" + PetNestLocalization.DescribeTalent(t);
                }
            }
            string chroma = PetNestChroma.DescribePair(_result.Pet, L10n.IsChinese);
            if (!string.IsNullOrEmpty(chroma))
            {
                text += "\n" + L10n.T("炫彩：", "Chroma: ") + chroma;
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
