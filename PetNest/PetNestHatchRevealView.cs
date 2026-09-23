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
//
// 2026-09-23 审美审查 UA-11 / UA-12：中间一张 240 见方的卡——开场是遗种蛋的物品图标在晃
// （sin 摆动，确定性、不用随机数），滚动阶段逐张换各血脉立绘，出结果时换成结果立绘并弹一下；
// 结果名 40 号粗体（异色挂流光、卡片金边，炫彩两侧两色条），血脉名降为 20 号副标题；
// 详情左对齐、一条一行。所有动效走 unscaled 时间并过暂停门。取不到图的阶段整张卡收起，不画空框。
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

        #region 版式与动效

        private static readonly Vector2 SurfaceSize = new Vector2(760f, 640f);
        private const float CardSize = 240f;
        private const float CardCenterY = 165f;
        private const float DetailWidth = 520f;
        private const float DetailTop = -58f;
        private const float DetailMaxHeight = 186f;

        /// <summary>蛋在晃：角速度与摆幅（sin(t×28)×6°，确定性，不用随机数）。</summary>
        private const float ShakeSpeed = 28f;
        private const float ShakeDegrees = 6f;
        /// <summary>滚动阶段每换一张图：透明度先压到 0.4 再回来。</summary>
        private const float SwapDipAlpha = 0.4f;
        private const float SwapSeconds = 0.08f;
        /// <summary>出结果那一下：卡片从 0.88 弹到 1。</summary>
        private const float PopSeconds = 0.32f;
        private const float PopStartScale = 0.88f;

        #endregion

        private static PetNestHatchRevealView _instance;

        private Canvas _canvas;
        private ZombieModeUIHelper.ModalInputLease _modalLease;
        private PetNestCancelKey _cancelKey;
        private GameObject _card;
        private Image _cardImage;
        private Image _cardStroke;
        private RectTransform _cardContent;
        private TextMeshProUGUI _rollText;
        private TextMeshProUGUI _resultText;
        private TextMeshProUGUI _detailText;
        private PetNestHatchResult _result;
        private UnityEngine.UI.Button _dismissButton;
        private TextMeshProUGUI _dismissLabel;
        private Coroutine _playRoutine;
        private bool _resultShown;
        private bool _finished;

        // Update 驱动的表现状态（都走 unscaled 时间、过暂停门）
        private bool _shaking;
        private float _shakeTime;
        private float _swapElapsed = -1f;
        private float _popElapsed = -1f;

        /// <summary>演出是否在播（ESC 优先级判定用）。</summary>
        internal static bool IsOpen
        {
            get { return _instance != null; }
        }

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
                if (_instance._cancelKey != null) _instance._cancelKey.Detach();
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

        /// <summary>玩家点「关闭」/ 按 ESC：画布淡出 0.12 秒再销毁（UA-21）。切图等被动路径走 Stop()。</summary>
        private void CloseByPlayer()
        {
            if (_instance != this) return;
            ReleaseLease();
            if (_cancelKey != null) _cancelKey.Detach();
            _instance = null;
            PetNestUI.FadeOutAndDestroy(gameObject, _canvas);
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
                "Surface", _canvas.transform, new Vector2(0.5f, 0.5f), SurfaceSize);
            Image image = surface.AddComponent<Image>();
            image.color = BossRushUIColors.Surface;
            BossRushUI.ApplyFramedPanelSkin(image, 14, BossRushUISkinPart.Panel);

            BuildCard(surface.transform);

            _rollText = ZombieModeUIHelper.CreateText(
                "Roll", surface.transform, string.Empty, 22f,
                new Vector2(0f, 22f), new Vector2(700f, 34f),
                TextAlignmentOptions.Center, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(_rollText);

            _resultText = ZombieModeUIHelper.CreateText(
                "Result", surface.transform, string.Empty, 40f,
                new Vector2(0f, -20f), new Vector2(700f, 54f),
                TextAlignmentOptions.Center, BossRushUIColors.TextPrimary);
            _resultText.fontStyle = FontStyles.Bold;
            BossRushUI.ApplyGameFont(_resultText);

            // 详情：左对齐、一条一行、按实测高度撑开（UA-12：旧写法居中长段，行首对不齐）
            _detailText = ZombieModeUIHelper.CreateText(
                "Detail", surface.transform, string.Empty, 18f,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-DetailWidth * 0.5f, DetailTop), new Vector2(DetailWidth, 40f),
                TextAlignmentOptions.TopLeft, BossRushUIColors.TextSecondary);
            _detailText.rectTransform.pivot = new Vector2(0f, 1f);
            _detailText.rectTransform.anchoredPosition = new Vector2(-DetailWidth * 0.5f, DetailTop);
            BossRushUI.ApplyGameFont(_detailText);

            // 演出期间是「跳过」，出完结果后改成「关闭」——面板不会自己消失，
            // 玩家可以把性格 / 天赋 / 炫彩慢慢看完再收。
            _dismissButton = ZombieModeUIHelper.CreateButton(
                "Dismiss", surface.transform, L10n.T("跳过", "Skip"),
                new Vector2(0.5f, 0.5f), new Vector2(0f, -SurfaceSize.y * 0.5f + 42f), new Vector2(180f, 46f),
                BossRushUIColors.SurfaceRaised, 18f, new Vector2(170f, 42f),
                OnDismiss, true);
            if (_dismissButton != null)
            {
                BossRushUIKit.StyleSecondaryButton(_dismissButton);
                _dismissLabel = _dismissButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            // 接管输入：遮罩只是"看起来"挡住了，raycaster 关掉的话下面仍然活着的
            // 孵化面板照样能被盲点到——列表刚好在这一刻重排，玩家会静默连吞第二枚蛋。
            _modalLease = ZombieModeUIHelper.ClaimModalInput(_canvas.gameObject, "PetNestHatchReveal");
            // ESC / 手柄取消 = 跳过；出完结果后 = 关闭（与按钮同一个入口）
            _cancelKey = PetNestCancelKey.Attach(_canvas.gameObject, OnDismiss, null);

            BossRushUI.PlayOpenAnimation(surface);
        }

        /// <summary>
        /// 中间那张卡：Surface 深底 + 描边（结果是异色时换金边）+ 裁切层里的一张图。
        /// 图随阶段换：蛋 → 各血脉立绘 → 结果立绘。当前阶段没有图时整张卡收起。
        /// </summary>
        private void BuildCard(Transform parent)
        {
            _card = BossRushUI.CreateCard(
                "RevealCard", parent, new Vector2(0f, CardCenterY), new Vector2(CardSize, CardSize),
                BossRushUIColors.SurfaceRaised, BossRushUIColors.Accent, false);
            Transform stroke = _card.transform.Find("Stroke");
            _cardStroke = stroke != null ? stroke.GetComponent<Image>() : null;

            GameObject clip = ZombieModeUIHelper.CreateRect(
                "Clip", _card.transform, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-16f, -16f), new Vector2(0.5f, 0.5f));
            clip.AddComponent<RectMask2D>();

            GameObject content = ZombieModeUIHelper.CreateRect(
                "Picture", clip.transform, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            _cardContent = content.GetComponent<RectTransform>();
            _cardImage = content.AddComponent<Image>();
            _cardImage.preserveAspect = true;
            _cardImage.raycastTarget = false;
            _card.SetActive(false);
        }

        /// <summary>六段节奏。全程只读 _result，不 roll、不写档。</summary>
        private IEnumerator PlayRoutine()
        {
            // 1) onBegin：蛋在晃
            SetCardSprite(PetNestUIPages.ResolveItemIcon(RelicEggConfig.TYPE_ID), false);
            _shaking = true;
            SetText(_rollText, L10n.T("蛋壳在动……", "The shell is moving..."));
            yield return WaitForPresentation(BeginSeconds);

            // 2) onRollBegin
            SetText(_rollText, L10n.T("血脉正在显形", "A bloodline is taking shape"));
            yield return WaitForPresentation(RollBeginSeconds);
            _shaking = false;
            ResetCardTransform();

            // 3) onRollStep ×N：滚动展示血脉名与立绘，纯视觉，与结果无关（步长固定，不用随机数）
            IList<PetNestLineageInfo> lineages = PetNestLineageCatalog.All;
            for (int i = 0; i < RollStepCount; i++)
            {
                PetNestLineageInfo sample = lineages.Count > 0 ? lineages[(i * 7 + 3) % lineages.Count] : null;
                SetText(_rollText, sample != null ? sample.DisplayName : "...");
                Sprite portrait = sample != null ? PetNestUIPages.ResolveLineagePortrait(sample.LineageKey) : null;
                if (portrait != null) SetCardSprite(portrait, true);
                yield return WaitForPresentation(RollStepSeconds);
            }

            // 4) onShowResult：这里第一次显示真结果（已 commit 的那一份）
            ShowResult();
            yield return WaitForPresentation(ShowResultSeconds);

            // 5) onPickup：出身 / 性格 / 异色 / 炫彩
            SetDetail(BuildDetailText());
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

        /// <summary>
        /// 表现层每帧：蛋的摆动、换图时的透明度起伏、出结果时的弹一下。都没在播时第一行就返回。
        /// 走 unscaled 时间（模态租约把 timeScale 压到 0），暂停菜单开着时不推进。
        /// </summary>
        private void Update()
        {
            if (!_shaking && _swapElapsed < 0f && _popElapsed < 0f) return;
            if (BossRushUI.IsGamePaused() || _cardContent == null) return;
            float dt = Time.unscaledDeltaTime;

            if (_shaking)
            {
                _shakeTime += dt;
                _cardContent.localEulerAngles = new Vector3(0f, 0f, Mathf.Sin(_shakeTime * ShakeSpeed) * ShakeDegrees);
            }

            if (_swapElapsed >= 0f && _cardImage != null)
            {
                _swapElapsed += dt;
                float t = Mathf.Clamp01(_swapElapsed / SwapSeconds);
                Color c = _cardImage.color;
                c.a = Mathf.Lerp(SwapDipAlpha, 1f, BossRushUI.SmoothStep(t));
                _cardImage.color = c;
                if (t >= 1f) _swapElapsed = -1f;
            }

            if (_popElapsed >= 0f && _card != null)
            {
                _popElapsed += dt;
                float t = Mathf.Clamp01(_popElapsed / PopSeconds);
                _card.transform.localScale = Vector3.one * Mathf.Lerp(PopStartScale, 1f, BossRushUI.EaseOut(t));
                if (t >= 1f)
                {
                    _card.transform.localScale = Vector3.one;
                    _popElapsed = -1f;
                }
            }
        }

        /// <summary>换卡上的图；null 时整张卡收起（取不到图就不画那一格）。dip=true 时透明度先压一下再回来。</summary>
        private void SetCardSprite(Sprite sprite, bool dip)
        {
            if (_card == null || _cardImage == null) return;
            if (sprite == null)
            {
                _card.SetActive(false);
                return;
            }
            _cardImage.sprite = sprite;
            if (!_card.activeSelf) _card.SetActive(true);
            if (dip)
            {
                Color c = _cardImage.color;
                c.a = SwapDipAlpha;
                _cardImage.color = c;
                _swapElapsed = 0f;
            }
            else
            {
                _cardImage.color = Color.white;
                _swapElapsed = -1f;
            }
        }

        private void ResetCardTransform()
        {
            if (_cardContent != null) _cardContent.localEulerAngles = Vector3.zero;
        }

        private void ShowResult()
        {
            if (_resultShown) return;
            _resultShown = true;
            _shaking = false;
            ResetCardTransform();

            // 层级（UA-12）：结果名最大（40 粗体），血脉名降为 20 号副标题「血脉 · X」
            _rollText.fontSize = 20f;
            _rollText.fontSizeMax = 20f;
            SetText(_rollText, L10n.T("血脉 · ", "Bloodline · ") + _result.LineageDisplayName);
            SetText(_resultText, BuildResultTitle());
            BossRushUIEntranceAnimation.Play(_resultText.gameObject, 0f, 0.35f, 18f);

            // 结果立绘：取不到就收起卡片，只留文字
            SetCardSprite(PetNestUIPages.ResolveLineagePortrait(_result.Pet.lineageKey), false);
            if (_card != null && _card.activeSelf) _popElapsed = 0f;

            // 异色：名字挂流光、卡片金边（复核第 12 项：流光此前只在巢卡标题上有）；炫彩：卡片左右两条色条
            if (_result.Shiny)
            {
                PetNestShinyTextShimmer.Attach(_resultText);
                if (_cardStroke != null) _cardStroke.color = BossRushUIColors.RarityLegendary;
            }
            AddChromaRails();

            if (_result.Shiny) PlayJackpotMusic();
        }

        /// <summary>炫彩崽：卡片左右两侧各一条色条（第一色在左、第二色在右）。</summary>
        private void AddChromaRails()
        {
            if (_card == null || !_card.activeSelf) return;
            PetNestChromaColor a = PetNestChroma.Find(_result.Pet.chromaA);
            PetNestChromaColor b = PetNestChroma.Find(_result.Pet.chromaB);
            if (a == null || b == null) return;
            AddRail("ChromaLeft", new Vector2(0f, 0.5f), 4f, a.TextHex);
            AddRail("ChromaRight", new Vector2(1f, 0.5f), -4f, b.TextHex);
        }

        private void AddRail(string name, Vector2 anchor, float x, string hex)
        {
            int r, g, b;
            if (!PetNestChroma.TryParseHex(hex, out r, out g, out b)) return;
            GameObject rail = ZombieModeUIHelper.CreateRect(
                name, _card.transform, new Vector2(anchor.x, 0f), new Vector2(anchor.x, 1f),
                new Vector2(x, 0f), new Vector2(5f, -28f), new Vector2(0.5f, 0.5f));
            Image railImage = rail.AddComponent<Image>();
            railImage.color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            railImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(railImage, 2, BossRushUISkinPart.Hairline);
        }

        private void CompleteReveal()
        {
            ShowResult();
            SetDetail(BuildDetailText());
            _finished = true;
            if (_dismissLabel != null)
            {
                _dismissLabel.text = L10n.T("关闭", "Close");
                // 结果出来之后「关闭」是这一屏唯一的主操作：AccentFill（Accent 不再整块平涂）
                ZombieModeUIHelper.SetButtonBaseColor(_dismissButton, BossRushUIColors.AccentFill);
            }
        }

        private void OnDismiss()
        {
            if (_finished) { CloseByPlayer(); return; }
            if (_playRoutine != null) StopCoroutine(_playRoutine);
            _playRoutine = null;
            CompleteReveal();
        }

        private string BuildResultTitle()
        {
            // 炫彩 / 异色的富文本口径与巢页、HUD 共用 PetNestChroma，不在这里另写一套
            return PetNestService.GetDecoratedPetName(_result.Pet);
        }

        /// <summary>详情写进去之后按实测高度撑开；超出预算时降到 16 号再量一次，不压住底部按钮。</summary>
        private void SetDetail(string text)
        {
            if (_detailText == null) return;
            _detailText.text = text;
            _detailText.fontSize = 18f;
            float height = BossRushUI.MeasureTextHeight(_detailText, DetailWidth, 40f);
            if (height > DetailMaxHeight)
            {
                _detailText.fontSize = 16f;
                BossRushUI.MeasureTextHeight(_detailText, DetailWidth, 40f);
            }
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
            // 一条一行、行首「· 」（UA-12）；性格效果紧跟在性格后面缩进一格。
            string text = "· " + L10n.T("性格：", "Temperament: ")
                + PetNestLocalization.DescribePersonality(_result.Pet.personalityId);
            string personalityEffect =
                PetNestLocalization.DescribePersonalityEffect(_result.Pet.personalityId);
            if (!string.IsNullOrEmpty(personalityEffect))
            {
                text += "\n    " + personalityEffect;
            }

            if (_result.Pet.talents != null)
            {
                for (int i = 0; i < _result.Pet.talents.Count; i++)
                {
                    PetNestTalentEntry t = _result.Pet.talents[i];
                    if (t == null) continue;
                    text += "\n· " + PetNestLocalization.DescribeTalent(t);
                }
            }
            string chroma = PetNestChroma.DescribePair(_result.Pet, L10n.IsChinese);
            if (!string.IsNullOrEmpty(chroma))
            {
                text += "\n· " + L10n.T("炫彩：", "Chroma: ") + chroma;
            }
            if (_result.FromCondense)
            {
                text += "\n· " + LocalizationHelper.GetLocalizedText(
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
