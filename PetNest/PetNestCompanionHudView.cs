// ============================================================================
// PetNestCompanionHudView.cs - 随从 HUD（实施计划 步骤 11）
// ============================================================================
// 形态照 ModeG HUD 的 BuildHudModel struct + 4Hz 节流：
//   每帧只做一次 timer 递减；到点才组装一个**值类型快照**，和上一帧比对，
//   只有变化的字段才写 TMP text。热路径零分配、零字符串拼接。
//
// 硬约束（tests/PetNestHudThrottleGuard.py 守卫）：
//   - 刷新间隔常量 = PetNestTuning.HudRefreshIntervalSeconds（0.25s，4Hz）；
//   - 未到间隔必须早返；
//   - 模型是 struct，不是 class（每帧 new 一个 class 就是每帧一次 GC 分配）；
//   - 只有值变化才赋值 text（TMP 赋值会触发重建 mesh）；
//   - 随从不在场时整块 HUD 隐藏，不做任何组装。
//
// 2026-09-23 审美审查 UA-01：两行裸字浮在世界画面上、血量只有「血量 72%」、4Hz 跳数，
// 还和随机事件徽章重叠。现在是一张卡：左侧立绘、名字、血条（按血量分三档颜色、平滑过渡）、
// 「Lv · 战痕」一行；左对齐排在随机事件徽章正下方，与徽章同左缘。
// 血条的平滑由独立的 PetNestHudBarSmoother 做（只改 fillAmount，到位就停），不动 Update 的节流早返结构。
// ============================================================================

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BossRush
{
    /// <summary>一帧的 HUD 快照。struct：避免每次刷新一次堆分配。</summary>
    internal struct PetNestHudModel
    {
        internal bool Visible;
        internal float HealthRatio;
        internal int ScarCount;
        internal int Level;
        internal bool Downed;
        internal bool Chinese;

        internal bool SameAs(PetNestHudModel other)
        {
            return Visible == other.Visible
                && Mathf.Approximately(HealthRatio, other.HealthRatio)
                && ScarCount == other.ScarCount
                && Level == other.Level
                && Downed == other.Downed
                && Chinese == other.Chinese;
        }
    }

    /// <summary>随从 HUD。局内常驻但零分配；随从不在场时整块隐藏。</summary>
    internal sealed class PetNestCompanionHudView : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = PetNestTuning.HudRefreshIntervalSeconds;

        // 版式（UA-01）：与随机事件徽章同左缘（RandomEventsTuning.HudBadgeMarginX），顶边在徽章底边下 8。
        private const float PanelWidth = 300f;
        private const float PanelHeight = 76f;
        private const float PortraitSize = 56f;
        private const float BarHeight = 6f;

        private static PetNestCompanionHudView _instance;

        private Canvas _canvas;
        private GameObject _panel;
        private Image _rail;
        private Image _portrait;
        private GameObject _portraitFrame;
        private TextMeshProUGUI _nameText;
        private TextMeshProUGUI _statusText;
        private Image _barFill;
        private PetNestHudBarSmoother _barSmoother;
        private int _barTier = -1;
        private float _refreshTimer;
        private PetNestHudModel _lastModel;
        private string _lastPetId;
        private bool _nameChinese;

        /// <summary>确保 HUD 存在。幂等。</summary>
        internal static void EnsureCreated()
        {
            if (_instance != null) return;
            try
            {
                GameObject host = new GameObject("BossRush_PetNestCompanionHud");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _instance = host.AddComponent<PetNestCompanionHudView>();
                _instance.Build();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 随从 HUD 创建失败: " + e.Message);
                Destroy();
            }
        }

        /// <summary>销毁 HUD。幂等。</summary>
        internal static void Destroy()
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
            // HUD 不接收点击：raycaster 关掉，否则会挡住局内交互
            _canvas = BossRushUI.CreateCanvasRoot(
                "BossRush_PetNestCompanionHudCanvas", BossRushUILayers.PetNestCompanionHud, false);
            _canvas.transform.SetParent(transform, false);

            // 卡片底板（Card 档 + 描边 + 投影）：亮场景里字不再裸浮在世界上。左侧色条是这只崽的身份色。
            _panel = BossRushUI.CreateCard(
                "Panel", _canvas.transform, Vector2.zero, new Vector2(PanelWidth, PanelHeight),
                BossRushUIColors.Surface, BossRushUIColors.Accent, true);
            RectTransform panelRect = _panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            // 随机事件徽章占 marginY..marginY-height（左上角锚、轴心在左上）；排在它正下方 8，不再互相压住
            panelRect.anchoredPosition = new Vector2(
                RandomEventsTuning.HudBadgeMarginX,
                RandomEventsTuning.HudBadgeMarginY - RandomEventsTuning.HudBadgeHeight - 8f);
            Image plate = _panel.GetComponent<Image>();
            if (plate != null) plate.raycastTarget = false;
            Transform rail = _panel.transform.Find("Panel_Accent");
            _rail = rail != null ? rail.GetComponent<Image>() : null;

            // 左侧立绘（取不到就不画，名字与血条左移）
            _portraitFrame = ZombieModeUIHelper.CreateRect(
                "Portrait", _panel.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(PortraitSize, PortraitSize), new Vector2(0f, 0.5f));
            Image frameImage = _portraitFrame.AddComponent<Image>();
            frameImage.color = BossRushUIColors.SurfaceRaised;
            frameImage.raycastTarget = false;
            BossRushUI.ApplyFramedPanelSkin(frameImage, 6, BossRushUISkinPart.Button);
            GameObject clip = ZombieModeUIHelper.CreateRect(
                "Clip", _portraitFrame.transform, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-4f, -4f), new Vector2(0.5f, 0.5f));
            clip.AddComponent<RectMask2D>();
            GameObject picture = ZombieModeUIHelper.CreateRect(
                "Image", clip.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
            _portrait = picture.AddComponent<Image>();
            _portrait.preserveAspect = true;
            _portrait.raycastTarget = false;
            _portraitFrame.SetActive(false);

            // 名字 17（常驻 HUD 正文 15–18）、「Lv · 战痕」14；单行框高 ≥ 字号×1.45+4
            _nameText = ZombieModeUIHelper.CreateText(
                "Name", _panel.transform, string.Empty, 17f,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -7f), new Vector2(200f, 30f),
                TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            _nameText.rectTransform.pivot = new Vector2(0f, 1f);
            BossRushUI.ApplyGameFont(_nameText);

            // 血条：底轨 Divider + 填充（Filled 必须有 sprite，否则 fillAmount 被忽略、永远满格）
            GameObject track = ZombieModeUIHelper.CreateRect(
                "HpTrack", _panel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -40f), new Vector2(200f, BarHeight), new Vector2(0f, 1f));
            Image trackImage = track.AddComponent<Image>();
            trackImage.color = BossRushUIColors.Divider;
            trackImage.raycastTarget = false;
            BossRushUI.ApplyPanelSkin(trackImage, 3, BossRushUISkinPart.Hairline);
            GameObject fill = ZombieModeUIHelper.CreateRect(
                "HpFill", track.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0f, 0.5f));
            _barFill = fill.AddComponent<Image>();
            _barFill.sprite = BossRushUI.GetSolidSprite();
            _barFill.type = Image.Type.Filled;
            _barFill.fillMethod = Image.FillMethod.Horizontal;
            _barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _barFill.fillAmount = 1f;
            _barFill.color = BossRushUIColors.SuccessText;
            _barFill.raycastTarget = false;
            _barSmoother = fill.AddComponent<PetNestHudBarSmoother>();

            _statusText = ZombieModeUIHelper.CreateText(
                "Status", _panel.transform, string.Empty, 14f,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -50f), new Vector2(200f, 24f),
                TextAlignmentOptions.Left, BossRushUIColors.TextSecondary);
            _statusText.rectTransform.pivot = new Vector2(0f, 1f);
            BossRushUI.ApplyGameFont(_statusText);

            LayoutText(false);
            _panel.SetActive(false);
        }

        /// <summary>有立绘时文字与血条从立绘右侧起排，没有时贴左边。只在换崽时调。</summary>
        private void LayoutText(bool hasPortrait)
        {
            float left = hasPortrait ? 12f + PortraitSize + 12f : 16f;
            float width = PanelWidth - left - 14f;
            SetRow(_nameText != null ? _nameText.rectTransform : null, left, width);
            SetRow(_barFill != null ? _barFill.rectTransform.parent as RectTransform : null, left, width);
            SetRow(_statusText != null ? _statusText.rectTransform : null, left, width);
        }

        private static void SetRow(RectTransform rect, float left, float width)
        {
            if (rect == null) return;
            rect.anchoredPosition = new Vector2(left, rect.anchoredPosition.y);
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        }

        private void Update()
        {
            // 常驻 HUD 跟随官方界面与暂停菜单收起（口径同 CampaignHud，2026-09-14）：只开关画布，下面的节流刷新照旧。
            bool visible = !BossRushUI.IsOfficialHudHidden() && !BossRushUI.IsGamePaused();
            if (_canvas != null && _canvas.enabled != visible) _canvas.enabled = visible;

            // 其余的每帧工作只有递减计时器
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = RefreshIntervalSeconds;

            PetNestHudModel model = BuildHudModel();
            if (model.SameAs(_lastModel)) return;
            _lastModel = model;
            Apply(model);
        }

        /// <summary>组装一帧快照。随从不在场时只填 Visible=false，不碰其它字段。</summary>
        private PetNestHudModel BuildHudModel()
        {
            PetNestHudModel model = new PetNestHudModel();
            try
            {
                if (!PetNestCompanionRuntime.HasCompanion)
                {
                    model.Visible = false;
                    return model;
                }

                CharacterMainControl companion = PetNestCompanionRuntime.CompanionCharacter;
                if (companion == null)
                {
                    model.Visible = false;
                    return model;
                }

                model.Visible = true;
                model.Chinese = L10n.IsChinese;

                Health health = companion.Health;
                if (health != null && health.MaxHealth > 0f)
                {
                    model.HealthRatio = Mathf.Clamp01(health.CurrentHealth / health.MaxHealth);
                }

                PetNestPetRecord pet = PetNestService.TryGetPet(
                    PetNestCompanionRuntime.ActiveCompanionPetId);
                if (pet != null)
                {
                    model.ScarCount = (pet.scars != null ? pet.scars.Count : 0) + pet.mergedOldScarCount;
                    model.Level = pet.level;
                    model.Downed = pet.state == (int)PetNestPetState.Downed;
                }
            }
            catch (Exception)
            {
                model.Visible = false;
            }
            return model;
        }

        private void Apply(PetNestHudModel model)
        {
            try
            {
                if (_panel != null && _panel.activeSelf != model.Visible)
                {
                    _panel.SetActive(model.Visible);
                    // 从隐藏到显示：淡入 + 微放大，不再一帧切出来（轴心在左上角）
                    if (model.Visible) BossRushUI.PlayOpenAnimation(_panel);
                }
                if (!model.Visible) return;

                string petId = PetNestCompanionRuntime.ActiveCompanionPetId;
                if (!string.Equals(petId, _lastPetId, StringComparison.Ordinal) || _nameChinese != model.Chinese)
                {
                    bool petChanged = !string.Equals(petId, _lastPetId, StringComparison.Ordinal);
                    _lastPetId = petId;
                    _nameChinese = model.Chinese;
                    PetNestPetRecord pet = PetNestService.TryGetPet(petId);
                    if (_nameText != null)
                    {
                        _nameText.text = pet != null ? PetNestService.GetDecoratedPetName(pet) : string.Empty;
                    }
                    if (petChanged) ApplyIdentity(pet);
                }

                ApplyHealthBar(model);

                if (_statusText != null)
                {
                    _statusText.text = BuildStatusText(model);
                    _statusText.color = model.Downed ? BossRushUIColors.DangerText : BossRushUIColors.TextSecondary;
                }
            }
            catch (Exception)
            {
                // HUD 写入失败不得拖崩宿主 Update
            }
        }

        /// <summary>换崽时：立绘（取不到就收起那一格）与身份色条（异色金、炫彩第一色、其余 Accent）。</summary>
        private void ApplyIdentity(PetNestPetRecord pet)
        {
            // 局内不为一张 HUD 小图去同步加载整个立绘包（约 5 MB）：包没加载过就只用官方角色图标。
            // 玩家在基地开过遗种巢 / 图鉴之后包已经在内存里（卸 Mod 时才卸），出击时直接复用。
            Sprite portrait = null;
            if (pet != null)
            {
                portrait = CodexPortraitCache.IsBundleLoaded
                    ? PetNestUIPages.ResolveLineagePortrait(pet.lineageKey)
                    : CodexPortraitCache.GetOfficialIcon(pet.lineageKey);
            }
            if (_portrait != null) _portrait.sprite = portrait;
            if (_portraitFrame != null) _portraitFrame.SetActive(portrait != null);
            LayoutText(portrait != null);

            if (_rail == null) return;
            Color identity = BossRushUIColors.Accent;
            if (pet != null && pet.shiny)
            {
                identity = BossRushUIColors.RarityLegendary;
            }
            else if (pet != null)
            {
                PetNestChromaColor a = PetNestChroma.Find(pet.chromaA);
                int r, g, b;
                if (a != null && PetNestChroma.HasChroma(pet) && PetNestChroma.TryParseHex(a.TextHex, out r, out g, out b))
                {
                    identity = new Color(r / 255f, g / 255f, b / 255f, 1f);
                }
            }
            _rail.color = identity;
        }

        /// <summary>
        /// 血条：目标值交给平滑组件（MoveTowards，unscaled，暂停时不动），颜色按三档只在跨档时改：
        /// &gt;50% SuccessText、25–50% WarningText、&lt;25% 或重伤 DangerText。
        /// </summary>
        private void ApplyHealthBar(PetNestHudModel model)
        {
            if (_barFill == null) return;
            float ratio = model.Downed ? 0f : Mathf.Clamp01(model.HealthRatio);
            if (_barSmoother != null) _barSmoother.SetTarget(ratio);
            else _barFill.fillAmount = ratio;

            int tier = model.Downed || ratio < 0.25f ? 2 : (ratio <= 0.5f ? 1 : 0);
            if (tier == _barTier) return;
            _barTier = tier;
            _barFill.color = tier == 0 ? BossRushUIColors.SuccessText
                : (tier == 1 ? BossRushUIColors.WarningText : BossRushUIColors.DangerText);
        }

        private static string BuildStatusText(PetNestHudModel model)
        {
            if (model.Downed)
            {
                return L10n.T("重伤退场", "Carried off");
            }
            // 血量交给血条，文字只留等级与战痕（UA-01：「血量 72%」4Hz 跳数读起来吃力）。
            // 等级摆在最前：养成的回报现在真的作用在属性上，局内得看得见它长到几级了
            string text = "Lv" + model.Level;
            if (model.ScarCount > 0)
            {
                text += " · " + L10n.T("战痕", "Scars") + " " + model.ScarCount;
            }
            return text;
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            Destroy();
        }
    }

    /// <summary>
    /// 随从血条的平滑：fillAmount 以每秒 1.6 的速度 MoveTowards 目标值（与帧率无关），到位就 enabled=false，
    /// 常态零开销。HUD 的 4Hz 节流刷新只写目标值。走 unscaled 时间，暂停菜单开着时不动。
    /// </summary>
    internal sealed class PetNestHudBarSmoother : MonoBehaviour
    {
        private const float UnitsPerSecond = 1.6f;

        private Image _image;
        private float _target = 1f;

        internal void SetTarget(float target)
        {
            if (_image == null) _image = GetComponent<Image>();
            _target = Mathf.Clamp01(target);
            if (_image != null && !Mathf.Approximately(_image.fillAmount, _target)) enabled = true;
        }

        private void Update()
        {
            if (_image == null)
            {
                enabled = false;
                return;
            }
            if (BossRushUI.IsGamePaused()) return;
            float next = Mathf.MoveTowards(_image.fillAmount, _target, UnitsPerSecond * Time.unscaledDeltaTime);
            _image.fillAmount = next;
            if (Mathf.Approximately(next, _target)) enabled = false;
        }
    }
}
