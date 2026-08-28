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
// ============================================================================

using System;
using TMPro;
using UnityEngine;

namespace BossRush
{
    /// <summary>一帧的 HUD 快照。struct：避免每次刷新一次堆分配。</summary>
    internal struct PetNestHudModel
    {
        internal bool Visible;
        internal float HealthRatio;
        internal int ScarCount;
        internal bool Downed;

        internal bool SameAs(PetNestHudModel other)
        {
            return Visible == other.Visible
                && Mathf.Approximately(HealthRatio, other.HealthRatio)
                && ScarCount == other.ScarCount
                && Downed == other.Downed;
        }
    }

    /// <summary>随从 HUD。局内常驻但零分配；随从不在场时整块隐藏。</summary>
    internal sealed class PetNestCompanionHudView : MonoBehaviour
    {
        private const float RefreshIntervalSeconds = PetNestTuning.HudRefreshIntervalSeconds;

        private static PetNestCompanionHudView _instance;

        private Canvas _canvas;
        private GameObject _panel;
        private TextMeshProUGUI _nameText;
        private TextMeshProUGUI _statusText;
        private float _refreshTimer;
        private PetNestHudModel _lastModel;
        private string _lastPetId;

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

            _panel = ZombieModeUIHelper.CreateRect(
                "Panel", _canvas.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(180f, -120f), new Vector2(320f, 72f),
                new Vector2(0.5f, 0.5f));

            _nameText = ZombieModeUIHelper.CreateText(
                "Name", _panel.transform, string.Empty, 20f,
                new Vector2(0f, 18f), new Vector2(310f, 28f),
                TextAlignmentOptions.Left, BossRushUIColors.TextPrimary);
            BossRushUI.ApplyGameFont(_nameText);

            _statusText = ZombieModeUIHelper.CreateText(
                "Status", _panel.transform, string.Empty, 16f,
                new Vector2(0f, -12f), new Vector2(310f, 26f),
                TextAlignmentOptions.Left, BossRushUIColors.TextSecondary);
            BossRushUI.ApplyGameFont(_statusText);

            _panel.SetActive(false);
        }

        private void Update()
        {
            // 每帧唯一的工作：递减计时器
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
                }
                if (!model.Visible) return;

                string petId = PetNestCompanionRuntime.ActiveCompanionPetId;
                if (!string.Equals(petId, _lastPetId, StringComparison.Ordinal))
                {
                    _lastPetId = petId;
                    PetNestPetRecord pet = PetNestService.TryGetPet(petId);
                    if (_nameText != null)
                    {
                        _nameText.text = pet != null ? PetNestService.GetPetDisplayName(pet) : string.Empty;
                    }
                }

                if (_statusText != null)
                {
                    _statusText.text = BuildStatusText(model);
                }
            }
            catch (Exception)
            {
                // HUD 写入失败不得拖崩宿主 Update
            }
        }

        private static string BuildStatusText(PetNestHudModel model)
        {
            if (model.Downed)
            {
                return L10n.T("重伤退场", "Carried off");
            }
            int percent = (int)Mathf.Round(model.HealthRatio * 100f);
            string text = L10n.T("血量", "HP") + " " + percent + "%";
            if (model.ScarCount > 0)
            {
                text += "   " + L10n.T("战痕", "Scars") + " " + model.ScarCount;
            }
            return text;
        }

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            Destroy();
        }
    }
}
