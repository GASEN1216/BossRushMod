using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed class ZombieModeHudRuntimeState
    {
        internal ZombieModeHudController Controller;
        internal string LastMainText;
        internal string LastSafeZoneText;
        internal string LastStageText;
        internal Color LastSafeZoneColor;
        internal bool HasLastSafeZoneColor;
        internal float NextRefreshTime;
        internal bool PauseMenuHidden;
        internal int LastActualPurification = -1;
        internal float ShownPurificationValue;
        internal int ShownPurification = -1;
        internal int PendingGain;
        internal float GainAge = 99f;
        internal float LastPreparationTimer;
        internal float PreparationTotal;
    }

    internal sealed partial class ZombieModeRuntimeModule
    {
        private const float ZombieModeHudRefreshInterval = 0.1f;
        private const float ZombieModeHudGainHoldSeconds = 1.0f;
        private const float ZombieModeHudGainFadeSeconds = 0.3f;
        private readonly Dictionary<int, ZombieModeHudRuntimeState> zombieModeHudStates =
            new Dictionary<int, ZombieModeHudRuntimeState>();

        /// <summary>
        /// 创建丧尸模式 HUD 并注册为 Run-only 对象
        /// </summary>
        internal void CreateZombieModeHud(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            GameObject root = new GameObject("ZombieMode_Hud");
            ZombieModeHudController controller = root.AddComponent<ZombieModeHudController>();
            controller.Initialize(runId);
            zombieModeHudStates[controller.GetInstanceID()] = new ZombieModeHudRuntimeState
            {
                Controller = controller
            };
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Hud, root, controller,
                delegate { CleanupZombieModeHud(controller); });
        }

        internal bool SetZombieModeHudVisibility(ZombieModeHudController controller, bool hidden)
        {
            ZombieModeHudRuntimeState state = GetZombieModeHudState(controller);
            if (state == null || state.PauseMenuHidden == hidden)
            {
                return false;
            }

            state.PauseMenuHidden = hidden;
            return true;
        }

        internal void CleanupZombieModeHud(ZombieModeHudController controller)
        {
            if (ReferenceEquals(controller, null))
            {
                return;
            }

            zombieModeHudStates.Remove(controller.GetInstanceID());
        }

        internal void TickZombieModeHud(ZombieModeHudController controller, float deltaTime)
        {
            ZombieModeHudRuntimeState state = GetZombieModeHudState(controller);
            if (state == null)
            {
                return;
            }

            int runId = controller.RunId;
            bool rolling = TickZombieModeHudPurification(controller, state, runId, deltaTime);
            TickZombieModeHudBars(controller, state, runId, deltaTime);
            if (controller.HasSafeZoneText && owner.IsZombieModeHudSafeZoneWarning(runId))
            {
                SetZombieModeHudSafeZoneColor(controller, state, owner.GetZombieModeHudSafeZoneColor(runId));
            }

            if (!rolling && Time.unscaledTime < state.NextRefreshTime)
            {
                return;
            }
            state.NextRefreshTime = Time.unscaledTime + ZombieModeHudRefreshInterval;

            if (controller.HasMainText)
            {
                string previousMain = state.LastMainText;
                bool mainChanged = SetZombieModeHudText(ref state.LastMainText,
                    owner.GetZombieModeHudMainText(runId, state.ShownPurification));
                if (mainChanged)
                {
                    controller.SetMainText(state.LastMainText, !ReferenceEquals(previousMain, state.LastMainText));
                }
            }

            if (controller.HasSafeZoneText)
            {
                string previousSafeZone = state.LastSafeZoneText;
                bool safeZoneChanged = SetZombieModeHudText(ref state.LastSafeZoneText,
                    owner.GetZombieModeHudSafeZoneText(runId));
                if (safeZoneChanged)
                {
                    controller.SetSafeZoneText(state.LastSafeZoneText,
                        !ReferenceEquals(previousSafeZone, state.LastSafeZoneText));
                }
                controller.SetSafeZonePanelVisible(!string.IsNullOrEmpty(state.LastSafeZoneText));
                SetZombieModeHudSafeZoneColor(controller, state, owner.GetZombieModeHudSafeZoneColor(runId));
            }

            if (controller.HasStageText && SetZombieModeHudText(ref state.LastStageText,
                owner.GetZombieModeHudStageText(runId)))
            {
                controller.SetStageText(state.LastStageText);
            }
        }

        private ZombieModeHudRuntimeState GetZombieModeHudState(ZombieModeHudController controller)
        {
            if (ReferenceEquals(controller, null))
            {
                return null;
            }

            ZombieModeHudRuntimeState state;
            return zombieModeHudStates.TryGetValue(controller.GetInstanceID(), out state) ? state : null;
        }

        private static bool SetZombieModeHudText(ref string lastValue, string value)
        {
            string nextValue = value ?? string.Empty;
            if (string.Equals(lastValue, nextValue, StringComparison.Ordinal))
            {
                return false;
            }

            lastValue = nextValue;
            return true;
        }

        private void SetZombieModeHudSafeZoneColor(
            ZombieModeHudController controller,
            ZombieModeHudRuntimeState state,
            Color value)
        {
            if (state.HasLastSafeZoneColor && state.LastSafeZoneColor == value)
            {
                return;
            }

            state.LastSafeZoneColor = value;
            state.HasLastSafeZoneColor = true;
            controller.SetSafeZoneColor(value);
        }

        private bool TickZombieModeHudPurification(
            ZombieModeHudController controller,
            ZombieModeHudRuntimeState state,
            int runId,
            float deltaTime)
        {
            int actual = owner.GetZombieModePurificationPoints(runId);
            if (state.LastActualPurification < 0)
            {
                state.LastActualPurification = actual;
                state.ShownPurificationValue = actual;
                state.ShownPurification = actual;
            }
            else if (actual != state.LastActualPurification)
            {
                if (actual > state.LastActualPurification && controller.HasGainText)
                {
                    state.PendingGain = (state.GainAge < ZombieModeHudGainHoldSeconds + ZombieModeHudGainFadeSeconds
                        ? state.PendingGain
                        : 0) + (actual - state.LastActualPurification);
                    state.GainAge = 0f;
                    controller.SetPurificationGainText(
                        string.Format(L10n.T("BossRush_ZombieMode_Reward_PurificationPoints"), state.PendingGain));
                }
                state.LastActualPurification = actual;
            }

            if (controller.HasGainGroup && state.GainAge < ZombieModeHudGainHoldSeconds + ZombieModeHudGainFadeSeconds)
            {
                state.GainAge += deltaTime;
                controller.SetPurificationGainAlpha(
                    1f - BossRushUI.SmoothStep((state.GainAge - ZombieModeHudGainHoldSeconds) / ZombieModeHudGainFadeSeconds));
            }

            if (state.ShownPurification == actual)
            {
                return false;
            }
            float speed = Mathf.Max(20f, Mathf.Abs(actual - state.ShownPurificationValue) * 6f);
            state.ShownPurificationValue = Mathf.MoveTowards(state.ShownPurificationValue, actual, speed * deltaTime);
            state.ShownPurification = Mathf.RoundToInt(state.ShownPurificationValue);
            return true;
        }

        private void TickZombieModeHudBars(
            ZombieModeHudController controller,
            ZombieModeHudRuntimeState state,
            int runId,
            float deltaTime)
        {
            int kills;
            int killTarget;
            float beaconFill;
            float preparationTimer;
            GetZombieModeHudBarState(runId, out kills, out killTarget, out beaconFill, out preparationTimer);

            if (preparationTimer > state.LastPreparationTimer + 0.5f)
            {
                state.PreparationTotal = preparationTimer;
            }
            state.LastPreparationTimer = preparationTimer;
            controller.TickBars(kills, killTarget, beaconFill, preparationTimer, state.PreparationTotal, deltaTime);
        }

        // HUD 富文本配色（审美审查 UC-04）：正文底色是 TextSecondary（标签），数值包成 TextPrimary，
        // 标题行 20 号、模式名用 Accent；阶位 / 警示用 token 预先转好的 hex，不每帧拼。
        private static readonly string ZombieModeHudValueOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.TextPrimary) + ">";
        private static readonly string ZombieModeHudAccentOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.Accent) + ">";
        private static readonly string ZombieModeHudWarningOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.WarningText) + ">";
        private static readonly string ZombieModeHudDangerOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.DangerText) + ">";
        private static readonly string ZombieModeHudSuccessOpen = "<color=#" + ColorUtility.ToHtmlStringRGB(BossRushUIColors.SuccessText) + ">";

        private static string ZombieModeHudValue(int value)
        {
            return ZombieModeHudValueOpen + value + "</color>";
        }

        public string GetZombieModeHudMainText(int runId)
        {
            return GetZombieModeHudMainText(runId, -1);
        }

        /// <summary>主面板文本。<paramref name="shownPurification"/> 是 HUD 滚动中的显示值（&lt;0 时用真实值）。</summary>
        public string GetZombieModeHudMainText(int runId, int shownPurification)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return string.Empty;
            }

            string wave = string.Format(L10n.T("BossRush_ZombieMode_Hud_Wave"), runState.CurrentWave);
            string pollution = string.Format(L10n.T("BossRush_ZombieMode_Hud_Pollution"), ZombieModeHudValue(runState.TotalPollution), GetZombieModePollutionTierText());
            int purificationValue = shownPurification >= 0 ? shownPurification : runState.PurificationPoints;
            // 数字等宽：滚动计数与跳变时整行不左右抖。
            string purification = string.Format(L10n.T("BossRush_ZombieMode_Hud_PurificationPoints"), ZombieModeHudValueOpen + "<mspace=0.56em>" + purificationValue + "</mspace></color>");
            string pressure = GetZombieModeHudPressureText();
            string kills = string.Empty;
            if (runState.CombatPhase == ZombieModeCombatPhase.Combat && runState.CurrentWaveKillTarget > 0)
            {
                kills = string.Format(L10n.T("BossRush_ZombieMode_Hud_KillProgress"), ZombieModeHudValue(runState.CurrentWaveKills), ZombieModeHudValue(runState.CurrentWaveKillTarget));
            }
            else if (runState.CurrentWaveBossesRemaining > 0)
            {
                kills = GetZombieModeBossProgressText();
            }

            string result = "<size=20><b>" + ZombieModeHudAccentOpen + L10n.T("BossRush_ZombieMode_EntryName") + "</color>  " + ZombieModeHudValueOpen + wave + "</color></b></size>\n" + pollution + "\n" + purification;
            if (!string.IsNullOrEmpty(pressure))
            {
                result += "\n" + pressure;
            }
            if (!string.IsNullOrEmpty(kills))
            {
                result += "\n" + kills;
            }
            result += "\n" + GetZombieModeNextBossText();
            return result;
        }

        public string GetZombieModeNextWavePreviewText(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return string.Empty;
            }

            int nextWave = Mathf.Max(1, runState.CurrentWave + 1);
            if (owner.IsZombieModeBossWaveForHud(nextWave))
            {
                return string.Format(
                    L10n.T("BossRush_ZombieMode_Reward_NextBossPreview"),
                    nextWave,
                    owner.GetZombieModeWaveCycleIndexForHud(nextWave) + 1,
                    owner.GetZombieModeBossCountForWaveForHud(nextWave),
                    Mathf.RoundToInt(owner.GetZombieModeBossHealthScaleForHud(nextWave) * 100f),
                    Mathf.RoundToInt(owner.GetZombieModeBossDamageScaleForHud(nextWave) * 100f),
                    owner.GetZombieModeWavePressureTargetForHud(nextWave),
                    Mathf.RoundToInt(owner.GetZombieModeBossRewardScaleForHud(nextWave) * 100f));
            }

            return string.Format(
                L10n.T("BossRush_ZombieMode_Reward_NextWavePreview"),
                nextWave,
                owner.GetZombieModeWavePressureTargetForHud(nextWave),
                Mathf.RoundToInt(owner.GetZombieModeWaveSpeedMultiplierForHud(nextWave) * 100f),
                GetZombieModeTideStageText(nextWave, false));
        }

        private string GetZombieModeHudPressureText()
        {
            if (!owner.IsZombieModeAmbientZombieSpawnPhaseForHud(runState.CombatPhase))
            {
                return string.Empty;
            }

            bool preparation = runState.CombatPhase != ZombieModeCombatPhase.Combat;
            int wave = owner.GetZombieModePacingWaveForHud();
            return string.Format(
                L10n.T("BossRush_ZombieMode_Hud_Pressure"),
                ZombieModeHudValue(runState.LivingNormalZombieCount),
                ZombieModeHudValue(owner.GetZombieModeAmbientPressureTargetForHud()),
                GetZombieModeTideStageText(wave, preparation));
        }

        private string GetZombieModeTideStageText(int wave, bool preparation)
        {
            if (preparation)
            {
                return L10n.T("BossRush_ZombieMode_Tide_Low");
            }

            if (owner.IsZombieModeBossWaveForHud(wave))
            {
                return L10n.T("BossRush_ZombieMode_Tide_Boss");
            }

            switch (owner.GetZombieModeNormalWaveStageIndexForHud(wave))
            {
                case 0:
                    return L10n.T("BossRush_ZombieMode_Tide_Low");
                case 1:
                    return L10n.T("BossRush_ZombieMode_Tide_Rising");
                case 2:
                    return L10n.T("BossRush_ZombieMode_Tide_High");
                default:
                    return L10n.T("BossRush_ZombieMode_Tide_Peak");
            }
        }

        public string GetZombieModeHudSafeZoneText(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return string.Empty;
            }

            if (!owner.IsAnyZombieModeSafeZoneActiveForHud())
            {
                return string.Empty;
            }

            string inside = runState.PlayerInsideSafeZone
                ? L10n.T("BossRush_ZombieMode_Hud_SafeZone_Inside")
                : L10n.T("BossRush_ZombieMode_Hud_SafeZone_Outside");
            // 旧版第二行恒为「安全区：有效」，面板显示本身就说明了这一点，删掉（审美审查 UC-23）。
            // 战斗期部署的便携安全区没有准备倒计时，不显示恒为 0 的那一行。
            if (runState.PreparationTimer <= 0f)
            {
                return inside;
            }

            string timer = string.Format(
                L10n.T("BossRush_ZombieMode_Hud_PreparationTimer"),
                Mathf.Max(0, Mathf.CeilToInt(runState.PreparationTimer)));
            return inside + "\n" + timer;
        }

        public string GetZombieModeHudStageText(int runId)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return string.Empty;
            }

            string stage = GetZombieModeStageText();
            string beacon = GetZombieModeBeaconHudText();
            string extraction = GetZombieModeExtractionHudText();
            string result = stage;
            if (!string.IsNullOrEmpty(beacon))
            {
                result += "  ·  " + beacon;
            }
            if (!string.IsNullOrEmpty(extraction))
            {
                result += "  ·  " + ZombieModeHudWarningOpen + extraction + "</color>";
            }
            return result;
        }

        /// <summary>
        /// HUD 读条取数（插值与绘制在 ZombieModeHudController）。纯读，不改状态。
        /// beaconFill &lt; 0 表示没在引导；引导起点在暂停时会顺延（见 ZombieModeBeaconChannelCoroutine），所以按墙钟算就是已引导时长。
        /// </summary>
        internal void GetZombieModeHudBarState(int runId, out int kills, out int killTarget, out float beaconFill, out float preparationTimer)
        {
            kills = 0;
            killTarget = 0;
            beaconFill = -1f;
            preparationTimer = 0f;
            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            if (runState.CombatPhase == ZombieModeCombatPhase.Combat)
            {
                kills = runState.CurrentWaveKills;
                killTarget = runState.CurrentWaveKillTarget;
            }
            if (runState.BeaconChanneling && runState.BeaconChannelDuration > 0f)
            {
                beaconFill = Mathf.Clamp01((Time.unscaledTime - runState.BeaconChannelStartTime) / runState.BeaconChannelDuration);
            }
            preparationTimer = runState.PreparationTimer;
        }

        /// <summary>Boss 奖励节点的净化收益倍率（百分比）。奖励卡按它分描边档位（≥150% 用传说色）。</summary>
        public int GetZombieModeBossRewardPercent(int runId)
        {
            return IsZombieModeRunValid(runId)
                ? Mathf.RoundToInt(owner.GetZombieModeBossRewardScaleForHud(runState.CurrentWave) * 100f)
                : 100;
        }

        // 安全区面板字色走 token（审美审查 UC-16）：区内 SuccessText、区外 WarningText；
        // 最后几秒在 WarningText 与 TextPrimary 之间平滑呼吸（SmoothStep），由 HUD 每帧取色，不再 0.1 s 一跳的线性三角波。
        private static readonly Color ZombieModeHudSafeZoneInactiveColor = BossRushUIColors.TextSecondary;
        private static readonly Color ZombieModeHudSafeZoneInsideColor = BossRushUIColors.SuccessText;
        private static readonly Color ZombieModeHudSafeZoneFlashTargetColor = BossRushUIColors.TextPrimary;
        private static readonly Color ZombieModeHudSafeZoneOutsideColor = BossRushUIColors.WarningText;

        internal bool IsZombieModeHudSafeZoneWarning(int runId)
        {
            return IsZombieModeRunValid(runId) && owner.IsAnyZombieModeSafeZoneActiveForHud() &&
                   runState.PreparationTimer > 0f &&
                   runState.PreparationTimer <= ZombieModeTuning.SafeZoneFlashStartSeconds;
        }

        public Color GetZombieModeHudSafeZoneColor(int runId)
        {
            if (!IsZombieModeRunValid(runId) || !owner.IsAnyZombieModeSafeZoneActiveForHud())
            {
                return ZombieModeHudSafeZoneInactiveColor;
            }

            if (IsZombieModeHudSafeZoneWarning(runId))
            {
                float flash = Mathf.PingPong(Time.unscaledTime / ZombieModeTuning.SafeZoneFlashCycleSeconds, 1f);
                return Color.Lerp(
                    ZombieModeHudSafeZoneOutsideColor,
                    ZombieModeHudSafeZoneFlashTargetColor,
                    BossRushUI.SmoothStep(flash));
            }

            return runState.PlayerInsideSafeZone
                ? ZombieModeHudSafeZoneInsideColor
                : ZombieModeHudSafeZoneOutsideColor;
        }

        private string GetZombieModeBossProgressText()
        {
            int total = owner.GetZombieModeBossCountForWaveForHud(runState.CurrentWave);
            int defeated = Mathf.Max(0, total - runState.CurrentWaveBossesRemaining);
            return string.Format(L10n.T("BossRush_ZombieMode_Hud_BossProgress"), ZombieModeHudValue(defeated), ZombieModeHudValue(total));
        }

        private string GetZombieModeNextBossText()
        {
            int currentWave = Mathf.Max(0, runState.CurrentWave);
            int wavesToBoss = 5 - (currentWave % 5);
            if (wavesToBoss <= 1)
            {
                return ZombieModeHudWarningOpen + L10n.T("BossRush_ZombieMode_Hud_NextBossNow") + "</color>";
            }

            return string.Format(L10n.T("BossRush_ZombieMode_Hud_NextBoss"), ZombieModeHudValue(wavesToBoss));
        }

        private string GetZombieModeBeaconHudText()
        {
            if (runState.BeaconChanneling)
            {
                // 引导中不再让「信标可用」直接消失：显示剩余秒数，读条在阶段栏下沿（审美审查 UC-17）。
                float remaining = Mathf.Max(0f, runState.BeaconChannelDuration - (Time.unscaledTime - runState.BeaconChannelStartTime));
                return ZombieModeHudAccentOpen + string.Format(L10n.T("BossRush_ZombieMode_Hud_BeaconChanneling"), remaining) + "</color>";
            }

            return owner.CanUseZombieModeBeacon()
                ? ZombieModeHudSuccessOpen + L10n.T("BossRush_ZombieMode_Hud_BeaconReady") + "</color>"
                : string.Empty;
        }

        private string GetZombieModeExtractionHudText()
        {
            if (runState.CombatPhase != ZombieModeCombatPhase.ExtractionOpportunity || runState.ActiveExtractionArea == null)
            {
                return string.Empty;
            }

            return L10n.T("BossRush_ZombieMode_Hud_ExtractionOpenHint");
        }

        private string GetZombieModePollutionTierText()
        {
            switch (runState.PollutionTier)
            {
                case 0:
                    return L10n.T("BossRush_ZombieMode_Hud_PollutionTier_Base");
                case 1:
                    return ZombieModeHudWarningOpen + L10n.T("BossRush_ZombieMode_Hud_PollutionTier_I") + "</color>";
                case 2:
                    return ZombieModeHudWarningOpen + L10n.T("BossRush_ZombieMode_Hud_PollutionTier_II") + "</color>";
                case 3:
                    return ZombieModeHudWarningOpen + L10n.T("BossRush_ZombieMode_Hud_PollutionTier_III") + "</color>";
                case 4:
                    return ZombieModeHudWarningOpen + L10n.T("BossRush_ZombieMode_Hud_PollutionTier_IV") + "</color>";
                default:
                    return ZombieModeHudDangerOpen + L10n.T("BossRush_ZombieMode_Hud_PollutionTier_Critical") + "</color>";
            }
        }

        private string GetZombieModeStageText()
        {
            switch (runState.CombatPhase)
            {
                case ZombieModeCombatPhase.Settling:
                    return L10n.T("BossRush_ZombieMode_Hud_StageSettling");
                case ZombieModeCombatPhase.RewardSelection:
                    return L10n.T("BossRush_ZombieMode_Hud_StageRewardSelection");
                case ZombieModeCombatPhase.Preparation:
                case ZombieModeCombatPhase.InitialPreparation:
                    return L10n.T("BossRush_ZombieMode_Hud_StagePreparation");
                case ZombieModeCombatPhase.ExtractionOpportunity:
                    return L10n.T("BossRush_ZombieMode_Hud_StageExtractionOpportunity");
                default:
                    return L10n.T("BossRush_ZombieMode_Hud_StageBattle");
            }
        }


    }
}
