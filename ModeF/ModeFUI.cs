using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;
using TMPro;
using Duckov.UI;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F UI 与提示

        private const float MODEF_PLAYER_NAME_CACHE_INTERVAL = 5f;
        private const float MODEF_HEALTHBAR_LOOKUP_INTERVAL = 1f;
        private static readonly BindingFlags ModeFUiInstanceBindingFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static MethodInfo modeFRefreshCharacterIconMethod = null;
        private static readonly FieldInfo modeFHealthBarNameTextField =
            typeof(HealthBar).GetField("nameText", ModeFUiInstanceBindingFlags);
        private const int MODEF_BOUNTY_RADAR_MAX_TARGETS = 5;
        private const float MODEF_BOUNTY_RADAR_REFRESH_INTERVAL = 0.20f;
        private const float MODEF_BOUNTY_RADAR_REGULAR_RADIUS = 250f;
        private const float MODEF_BOUNTY_RADAR_LEADER_RADIUS = 320f;
        private const float MODEF_BOUNTY_RADAR_REGULAR_SIZE = 52f;
        private const float MODEF_BOUNTY_RADAR_LEADER_SIZE = 64f;
        private const float MODEF_BOUNTY_RADAR_WORLD_HEIGHT = 1.4f;
        private const float MODEF_BOUNTY_RADAR_GUIDE_SIZE = 760f;
        private const int MODEF_BOUNTY_RADAR_CANVAS_ORDER = 240;
        private const string MODEF_BOUNTY_RADAR_REGULAR_SPRITE_PATH = "Assets/ui/modef_bounty_radar/bounty_circle_regular.png";
        private const string MODEF_BOUNTY_RADAR_LEADER_SPRITE_PATH = "Assets/ui/modef_bounty_radar/bounty_circle_leader.png";
        private const string MODEF_BOUNTY_RADAR_REGULAR_SPRITE_PATH_LEGACY = "Assets/ui/bounty_circle_regular.png";
        private const string MODEF_BOUNTY_RADAR_LEADER_SPRITE_PATH_LEGACY = "Assets/ui/bounty_circle_leader.png";

        private string modeFCachedPlayerName = null;
        private float modeFNextPlayerNameRefreshTime = 0f;
        private HealthBar modeFCachedPlayerHealthBar = null;
        private float modeFNextHealthBarLookupTime = 0f;
        private readonly Dictionary<int, HealthBar> modeFHealthBarCacheByTargetId = new Dictionary<int, HealthBar>();
        private readonly Dictionary<int, int> modeFHealthBarTargetIdsByBarId = new Dictionary<int, int>();
        private readonly Dictionary<int, string> modeFHealthBarDesiredTextByBarId = new Dictionary<int, string>();
        private readonly Dictionary<int, int> modeFHealthBarAppliedVersionByBarId = new Dictionary<int, int>();
        private int modeFHealthBarNameVersion = 1;
        private bool? modeFLastHealthBarLanguageIsChinese = null;

        private GameObject modeFBountyRadarCanvasObject = null;
        private RectTransform modeFBountyRadarCenterRect = null;
        private Image modeFBountyRadarGuideImage = null;
        private ModeFBountyRadarEntryUi modeFBountyLeaderRadarEntry = null;
        private readonly List<ModeFBountyRadarEntryUi> modeFBountyRadarEntries = new List<ModeFBountyRadarEntryUi>();
        private readonly List<ModeFBountyRadarTarget> modeFBountyRadarTargetScratch = new List<ModeFBountyRadarTarget>();
        private readonly Dictionary<int, string> modeFMarkSuffixZhCache = new Dictionary<int, string>();
        private readonly Dictionary<int, string> modeFMarkSuffixEnCache = new Dictionary<int, string>();
        private float modeFNextBountyRadarRefreshTime = 0f;
        private TMP_FontAsset modeFBountyRadarFont = null;
        private static Sprite modeFBountyRadarRegularSprite = null;
        private static Sprite modeFBountyRadarLeaderSprite = null;
        private static Sprite modeFBountyRadarGuideSprite = null;

        private struct ModeFBountyRadarTarget
        {
            public CharacterMainControl boss;
            public int marks;
            public float distanceSqr;
        }

        private sealed class ModeFBountyRadarEntryUi
        {
            public GameObject root;
            public RectTransform rect;
            public Image icon;
            public TextMeshProUGUI countText;
            public RectTransform distanceRect;
            public TextMeshProUGUI distanceText;
        }

        private void ResetModeFUiCaches()
        {
            modeFCachedPlayerName = null;
            modeFNextPlayerNameRefreshTime = 0f;
            modeFCachedPlayerHealthBar = null;
            modeFNextHealthBarLookupTime = 0f;
            modeFHealthBarCacheByTargetId.Clear();
            modeFHealthBarTargetIdsByBarId.Clear();
            modeFHealthBarDesiredTextByBarId.Clear();
            modeFHealthBarAppliedVersionByBarId.Clear();
            modeFHealthBarNameVersion = 1;
            modeFLastHealthBarLanguageIsChinese = null;

            modeFMarkSuffixZhCache.Clear();
            modeFMarkSuffixEnCache.Clear();
            modeFNextBountyRadarRefreshTime = 0f;
        }

        internal void MarkModeFHealthBarNamesDirty()
        {
            if (modeFHealthBarNameVersion < int.MaxValue)
            {
                modeFHealthBarNameVersion++;
            }
            else
            {
                modeFHealthBarNameVersion = 1;
                modeFHealthBarAppliedVersionByBarId.Clear();
            }
        }

        private void SyncModeFHealthBarNameLanguageState()
        {
            bool isChinese = L10n.IsChinese;
            if (!modeFLastHealthBarLanguageIsChinese.HasValue ||
                modeFLastHealthBarLanguageIsChinese.Value != isChinese)
            {
                modeFLastHealthBarLanguageIsChinese = isChinese;
                MarkModeFHealthBarNamesDirty();
            }
        }

        internal void RegisterModeFHealthBar(HealthBar healthBar)
        {
            if (healthBar == null)
            {
                return;
            }

            Health target = healthBar.target;
            if (target == null)
            {
                return;
            }

            int targetId = target.GetInstanceID();
            int barId = healthBar.GetInstanceID();
            int previousTargetId = 0;
            if (modeFHealthBarTargetIdsByBarId.TryGetValue(barId, out previousTargetId) &&
                previousTargetId != targetId)
            {
                HealthBar previousBar = null;
                if (modeFHealthBarCacheByTargetId.TryGetValue(previousTargetId, out previousBar) &&
                    object.ReferenceEquals(previousBar, healthBar))
                {
                    modeFHealthBarCacheByTargetId.Remove(previousTargetId);
                }

                modeFHealthBarDesiredTextByBarId.Remove(barId);
                modeFHealthBarAppliedVersionByBarId.Remove(barId);
            }

            modeFHealthBarCacheByTargetId[targetId] = healthBar;
            modeFHealthBarTargetIdsByBarId[barId] = targetId;
        }

        private void ClearModeFHealthBarOverrideCache(HealthBar healthBar)
        {
            if (healthBar == null)
            {
                return;
            }

            int barId = healthBar.GetInstanceID();
            modeFHealthBarDesiredTextByBarId.Remove(barId);
            modeFHealthBarAppliedVersionByBarId.Remove(barId);
        }

        private bool TryGetCachedModeFHealthBar(Health health, out HealthBar healthBar)
        {
            healthBar = null;
            if (health == null)
            {
                return false;
            }

            int targetId = health.GetInstanceID();
            if (!modeFHealthBarCacheByTargetId.TryGetValue(targetId, out healthBar))
            {
                return false;
            }

            if (healthBar != null && healthBar.target == health)
            {
                return true;
            }

            modeFHealthBarCacheByTargetId.Remove(targetId);
            healthBar = null;
            return false;
        }

        private void ScanAndCacheModeFHealthBars(bool force = false)
        {
            if (!force && Time.unscaledTime < modeFNextHealthBarLookupTime)
            {
                return;
            }

            modeFNextHealthBarLookupTime = Time.unscaledTime + MODEF_HEALTHBAR_LOOKUP_INTERVAL;
            modeFHealthBarCacheByTargetId.Clear();

            HealthBar[] healthBars = UnityEngine.Object.FindObjectsOfType<HealthBar>();
            for (int i = 0; i < healthBars.Length; i++)
            {
                RegisterModeFHealthBar(healthBars[i]);
            }
        }

        private static string BuildModeFMarkText(int marks, bool chinese)
        {
            if (marks <= 0)
            {
                return null;
            }

            return chinese
                ? "<color=yellow>悬赏" + marks + "</color>"
                : "<color=yellow>Bounty " + marks + "</color>";
        }

        private string BuildModeFMarkSuffix(int marks)
        {
            if (marks <= 0)
            {
                return null;
            }

            bool isChinese = L10n.IsChinese;
            Dictionary<int, string> suffixCache = isChinese ? modeFMarkSuffixZhCache : modeFMarkSuffixEnCache;
            string suffix = null;
            if (suffixCache.TryGetValue(marks, out suffix) && !string.IsNullOrEmpty(suffix))
            {
                return suffix;
            }

            string markText = BuildModeFMarkText(marks, isChinese);
            if (string.IsNullOrEmpty(markText))
            {
                return null;
            }

            suffix = " " + markText;
            suffixCache[marks] = suffix;
            return suffix;
        }

        private static MethodInfo GetModeFRefreshCharacterIconMethod()
        {
            if (modeFRefreshCharacterIconMethod == null)
            {
                modeFRefreshCharacterIconMethod = typeof(HealthBar).GetMethod(
                    "RefreshCharacterIcon",
                    ModeFUiInstanceBindingFlags);
            }

            return modeFRefreshCharacterIconMethod;
        }

        private static TextMeshProUGUI GetModeFHealthBarNameText(HealthBar healthBar)
        {
            if (healthBar == null || modeFHealthBarNameTextField == null)
            {
                return null;
            }

            return modeFHealthBarNameTextField.GetValue(healthBar) as TextMeshProUGUI;
        }

        /// <summary>
        /// 广播当前阶段状态（每 15s 调用）
        /// </summary>
        private void BroadcastModeFPhaseStatus()
        {
            try
            {
                if (!modeFActive) return;

                string modeName = L10n.T("<color=red>血猎追击</color>", "<color=red>Bloodhunt</color>");
                string phaseName = GetModeFPhaseName(modeFState.CurrentPhase);
                float remaining = modeFState.PhaseDuration - modeFState.PhaseElapsed;

                if (modeFState.CurrentPhase == ModeFPhase.Extraction)
                {
                    ShowBigBanner(L10n.T(
                        modeName + " | <color=green>" + phaseName + "</color> | 掉血率: 3%/s | 速速撤离！",
                        modeName + " | <color=green>" + phaseName + "</color> | Bleed: 3%/s | Evacuate now!"
                    ));
                }
                else
                {
                    int remainSec = Mathf.CeilToInt(remaining);
                    ShowBigBanner(L10n.T(
                        modeName + " | " + phaseName + " | 剩余 <color=yellow>" + remainSec + "</color> 秒",
                        modeName + " | " + phaseName + " | <color=yellow>" + remainSec + "</color>s remaining"
                    ));
                }
            }
            catch { }
        }

        /// <summary>
        /// 广播榜首变化
        /// </summary>
        private void BroadcastModeFLeaderChange(CharacterMainControl newLeader, int marks)
        {
            try
            {
                // 约定：CheckAndBroadcastLeaderChange 中，玩家为榜首时 newLeader 设为 null
                bool leaderIsPlayer = newLeader == null;
                string leaderName = GetModeFActorDisplayName(newLeader, leaderIsPlayer);
                string markTextZh = BuildModeFMarkText(marks, true);
                string markTextEn = BuildModeFMarkText(marks, false);
                string contextZh;
                string contextEn;
                if (TryConsumeModeFLeaderChangeContext(out contextZh, out contextEn))
                {
                    ShowBigBanner(L10n.T(
                        contextZh + " " + markTextZh,
                        contextEn + " " + markTextEn
                    ));
                }
                else
                {
                    ShowBigBanner(L10n.T(
                        "<color=orange>" + leaderName + "</color> 成为悬赏榜首！ " + markTextZh,
                        "<color=orange>" + leaderName + "</color> is now the Bounty Leader! " + markTextEn
                    ));
                }

                DevLog("[ModeF] 榜首切换: " + leaderName + " (marks=" + marks + ")");
            }
            catch { }
        }

        private void BroadcastModeFBossGrowth(CharacterMainControl killer, CharacterMainControl victim, float growthPercent)
        {
            try
            {
                if (growthPercent <= 0.001f)
                {
                    return;
                }

                string killerName = GetModeFActorDisplayName(killer, false);
                string victimName = GetModeFActorDisplayName(victim, false);
                int growthValue = Mathf.RoundToInt(growthPercent * 100f);

                ShowBigBanner(L10n.T(
                    "<color=orange>" + killerName + "</color> 啃噬了 <color=red>" + victimName
                        + "</color> 的命火！<color=yellow>最大生命与火力 +" + growthValue + "%</color>",
                    "<color=orange>" + killerName + "</color> devoured <color=red>" + victimName
                        + "</color> and stole its life! <color=yellow>Max HP and firepower +" + growthValue + "%</color>"
                ));
            }
            catch { }
        }

        /// <summary>
        /// 显示奖励气泡
        /// </summary>
        private void ShowModeFRewardBubble(string text)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.transform == null) return;

                DialogueBubblesManager.Show(text, player.transform, 2.5f, false, false, -1f, 3f);
            }
            catch { }
        }

        /// <summary>
        /// 获取玩家显示名
        /// </summary>
        internal string GetModeFPlayerName()
        {
            if (Time.unscaledTime < modeFNextPlayerNameRefreshTime && !string.IsNullOrEmpty(modeFCachedPlayerName))
            {
                return modeFCachedPlayerName;
            }

            string previousName = modeFCachedPlayerName;
            try
            {
                string steamName = TryGetSteamPersonaName();
                modeFCachedPlayerName = !string.IsNullOrEmpty(steamName)
                    ? steamName
                    : L10n.T("我", "Me");
            }
            catch
            {
                modeFCachedPlayerName = L10n.T("我", "Me");
            }

            modeFNextPlayerNameRefreshTime = Time.unscaledTime + MODEF_PLAYER_NAME_CACHE_INTERVAL;
            if (!string.Equals(previousName, modeFCachedPlayerName, StringComparison.Ordinal))
            {
                MarkModeFHealthBarNamesDirty();
            }

            return modeFCachedPlayerName;
        }

        internal void SetModeFBossDisplayName(CharacterMainControl actor, string displayName, Teams originalFaction)
        {
            if (actor == null || actor.gameObject == null || string.IsNullOrWhiteSpace(displayName))
            {
                return;
            }

            ModeFBossDisplayNameMarker marker = actor.GetComponent<ModeFBossDisplayNameMarker>();
            if (marker == null)
            {
                marker = actor.gameObject.AddComponent<ModeFBossDisplayNameMarker>();
            }

            marker.DisplayName = displayName;
            marker.OriginalFaction = originalFaction;
            MarkModeFHealthBarNamesDirty();
        }

        private string TryGetModeFBossDisplayName(CharacterMainControl actor)
        {
            if (actor == null)
            {
                return null;
            }

            ModeFBossDisplayNameMarker marker = actor.GetComponent<ModeFBossDisplayNameMarker>();
            if (marker == null || IsModeFPlaceholderActorName(marker.DisplayName))
            {
                return null;
            }

            return marker.DisplayName;
        }

        internal string GetModeFActorDisplayName(CharacterMainControl actor, bool treatNullAsPlayer = false)
        {
            if (actor == null)
            {
                return treatNullAsPlayer ? GetModeFPlayerName() : L10n.T("未知目标", "Unknown");
            }

            try
            {
                if (actor == CharacterMainControl.Main || actor.IsMainCharacter)
                {
                    return GetModeFPlayerName();
                }
            }
            catch { }

            string trackedDisplayName = TryGetModeFBossDisplayName(actor);
            if (!string.IsNullOrEmpty(trackedDisplayName))
            {
                return trackedDisplayName;
            }

            string presetDisplayName = null;
            try
            {
                if (actor.characterPreset != null)
                {
                    presetDisplayName = actor.characterPreset.DisplayName;
                    if (!IsModeFPlaceholderActorName(presetDisplayName))
                    {
                        return presetDisplayName;
                    }
                }
            }
            catch { }

            string itemDisplayName = null;
            try
            {
                if (actor.CharacterItem != null)
                {
                    itemDisplayName = actor.CharacterItem.DisplayName;
                    if (!IsModeFPlaceholderActorName(itemDisplayName))
                    {
                        return itemDisplayName;
                    }
                }
            }
            catch { }

            string objectName = null;
            try
            {
                if (actor.gameObject != null)
                {
                    objectName = actor.gameObject.name;
                    if (!IsModeFPlaceholderActorName(objectName))
                    {
                        return objectName;
                    }
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(presetDisplayName))
            {
                return presetDisplayName;
            }

            if (!string.IsNullOrEmpty(itemDisplayName))
            {
                return itemDisplayName;
            }

            if (!string.IsNullOrEmpty(objectName))
            {
                return objectName;
            }

            return L10n.T("未知目标", "Unknown");
        }

        private static bool IsModeFPlaceholderActorName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            string trimmed = name.Trim();
            return string.Equals(trimmed, "躯壳", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Shell", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Character(Clone)", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Character", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.StartsWith("ModeF_", StringComparison.Ordinal) ||
                   trimmed.StartsWith("BossRush_", StringComparison.Ordinal) ||
                   trimmed.StartsWith("Character(", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取 Boss 的印记后缀显示
        /// </summary>
        public string GetModeFBountyMarkSuffix(CharacterMainControl character)
        {
            try
            {
                if (!modeFActive || character == null) return null;

                int charId = character.GetInstanceID();
                int marks = 0;
                if (modeFState.BountyMarksByCharacterId.TryGetValue(charId, out marks) && marks > 0)
                {
                    return BuildModeFMarkSuffix(marks);
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 获取玩家的印记后缀显示
        /// </summary>
        public string GetModeFPlayerMarkSuffix()
        {
            return !modeFActive ? null : BuildModeFMarkSuffix(modeFState.PlayerBountyMarks);
        }

        #endregion
    }
}
