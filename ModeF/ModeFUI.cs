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
        private static MethodInfo modeFSteamFriendsGetPersonaNameMethod = null;
        private static MethodInfo modeFSteamManagerGetSteamDisplayMethod = null;
        private const int MODEF_BOUNTY_RADAR_MAX_TARGETS = 5;
        private const float MODEF_BOUNTY_RADAR_REFRESH_INTERVAL = 0.10f;
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
        /// <summary>缓存场景中的 HealthBar 数组，避免 UpdateModeFBossNameTags 每次 FindObjectsOfType</summary>
        private HealthBar[] modeFCachedAllHealthBars = null;
        private float modeFNextAllHealthBarsRefreshTime = 0f;
        private const float MODEF_ALL_HEALTHBARS_CACHE_INTERVAL = 2f;
        private string modeFCachedMarkLabel = null;
        private bool? modeFCachedMarkLabelIsChinese = null;
        private GameObject modeFBountyRadarCanvasObject = null;
        private RectTransform modeFBountyRadarRootRect = null;
        private RectTransform modeFBountyRadarCenterRect = null;
        private Image modeFBountyRadarGuideImage = null;
        private ModeFBountyRadarEntryUi modeFBountyLeaderRadarEntry = null;
        private readonly List<ModeFBountyRadarEntryUi> modeFBountyRadarEntries = new List<ModeFBountyRadarEntryUi>();
        private readonly List<ModeFBountyRadarTarget> modeFBountyRadarTargetScratch = new List<ModeFBountyRadarTarget>();
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
            modeFCachedAllHealthBars = null;
            modeFNextAllHealthBarsRefreshTime = 0f;
            modeFCachedMarkLabel = null;
            modeFCachedMarkLabelIsChinese = null;
            modeFNextBountyRadarRefreshTime = 0f;
        }

        private string GetModeFMarkLabel()
        {
            bool isChinese = L10n.IsChinese;
            if (!modeFCachedMarkLabelIsChinese.HasValue ||
                modeFCachedMarkLabelIsChinese.Value != isChinese ||
                string.IsNullOrEmpty(modeFCachedMarkLabel))
            {
                modeFCachedMarkLabel = isChinese ? "悬赏" : "Bounty";
                modeFCachedMarkLabelIsChinese = isChinese;
            }

            return modeFCachedMarkLabel;
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
            string markText = BuildModeFMarkText(marks, L10n.IsChinese);
            return string.IsNullOrEmpty(markText) ? null : " " + markText;
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

        private static MethodInfo GetModeFSteamFriendsGetPersonaNameMethod()
        {
            if (modeFSteamFriendsGetPersonaNameMethod != null)
            {
                return modeFSteamFriendsGetPersonaNameMethod;
            }

            Type steamFriendsType = AccessTools.TypeByName("Steamworks.SteamFriends");
            if (steamFriendsType != null)
            {
                modeFSteamFriendsGetPersonaNameMethod = steamFriendsType.GetMethod(
                    "GetPersonaName",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }

            return modeFSteamFriendsGetPersonaNameMethod;
        }

        private static MethodInfo GetModeFSteamManagerGetSteamDisplayMethod()
        {
            if (modeFSteamManagerGetSteamDisplayMethod != null)
            {
                return modeFSteamManagerGetSteamDisplayMethod;
            }

            Type steamManagerType = AccessTools.TypeByName("SteamManager");
            if (steamManagerType != null)
            {
                modeFSteamManagerGetSteamDisplayMethod = steamManagerType.GetMethod(
                    "GetSteamDisplay",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }

            return modeFSteamManagerGetSteamDisplayMethod;
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

            try
            {
                string steamName = TryGetModeFSteamPersonaName();
                modeFCachedPlayerName = !string.IsNullOrEmpty(steamName)
                    ? steamName
                    : L10n.T("我", "Me");
            }
            catch
            {
                modeFCachedPlayerName = L10n.T("我", "Me");
            }

            modeFNextPlayerNameRefreshTime = Time.unscaledTime + MODEF_PLAYER_NAME_CACHE_INTERVAL;
            return modeFCachedPlayerName;
        }

        internal string GetModeFActorDisplayName(CharacterMainControl actor, bool treatNullAsPlayer = false)
        {
            if (actor == null)
            {
                return treatNullAsPlayer ? GetModeFPlayerName() : L10n.T("未知目标", "Unknown");
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
                   trimmed.StartsWith("ModeF_", StringComparison.Ordinal) ||
                   trimmed.StartsWith("BossRush_", StringComparison.Ordinal);
        }

        private string TryGetModeFSteamPersonaName()
        {
            try
            {
                MethodInfo getPersonaName = GetModeFSteamFriendsGetPersonaNameMethod();
                if (getPersonaName != null)
                {
                    string personaName = getPersonaName.Invoke(null, null) as string;
                    if (!string.IsNullOrEmpty(personaName))
                    {
                        return personaName;
                    }
                }
            }
            catch { }

            try
            {
                MethodInfo getSteamDisplay = GetModeFSteamManagerGetSteamDisplayMethod();
                if (getSteamDisplay != null)
                {
                    object display = getSteamDisplay.IsStatic ? getSteamDisplay.Invoke(null, null) : null;
                    string displayName = display as string;
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        return displayName;
                    }
                }
            }
            catch { }

            return null;
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

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void MarkModeFPlayerNameTagDirty()
        {
            if (modeFActive)
            {
                EnsureModeFPlayerNameTag();
            }
        }

        private void EnsureModeFPlayerNameTag()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null)
            {
                return;
            }

            try
            {
                player.Health.showHealthBar = true;

                // 玩家血条已存在时只刷新名字，避免反复 RequestHealthBar 导致 UI 释放/重建抖动。
                HealthBar healthBar = FindModeFHealthBar(player.Health);
                if (healthBar != null)
                {
                    ForceRefreshModeFHealthBarName(healthBar);
                    return;
                }

                player.Health.RequestHealthBar();
            }
            catch { }
        }

        private string BuildModeFKillRewardBubbleText(bool isBountyBoss, float healAmount, float maxHealthGain)
        {
            List<string> parts = new List<string>(3);

            if (healAmount > 0.01f)
            {
                parts.Add(L10n.T(
                    "血量 <color=red>+" + Mathf.RoundToInt(healAmount) + "</color>",
                    "HP <color=red>+" + Mathf.RoundToInt(healAmount) + "</color>"));
            }

            if (maxHealthGain > 0.01f)
            {
                parts.Add(L10n.T(
                    "生命上限 <color=red>+" + Mathf.RoundToInt(maxHealthGain) + "</color>",
                    "Max HP <color=red>+" + Mathf.RoundToInt(maxHealthGain) + "</color>"));
            }

            if (isBountyBoss)
            {
                parts.Add(L10n.T(
                    "悬赏印记 <color=red>+1</color>",
                    "Bounty <color=red>+1</color>"));
            }

            if (parts.Count <= 0)
            {
                return L10n.T("奖励已结算", "Reward applied");
            }

            return string.Join(" | ", parts.ToArray());
        }

        private void UpdateModeFPlayerNameTag()
        {
            if (!modeFActive)
            {
                return;
            }

            if (Time.frameCount % 120 == 0)
            {
                EnsureModeFPlayerNameTag();
            }
        }

        internal bool ShouldForceModeFHealthBarName(CharacterMainControl character)
        {
            return modeFActive &&
                   character != null &&
                   (character.IsMainCharacter || IsTrackedModeFBoss(character));
        }

        internal void EnsureModeFBossNameTag(CharacterMainControl boss)
        {
            if (!modeFActive || boss == null || boss.Health == null)
            {
                return;
            }

            try
            {
                boss.Health.showHealthBar = true;
                boss.Health.RequestHealthBar();
            }
            catch { }
        }

        private void UpdateModeFBossNameTags()
        {
            if (!modeFActive || Time.frameCount % 120 != 0)
            {
                return;
            }

            // 使用缓存的 HealthBar 数组，每 MODEF_ALL_HEALTHBARS_CACHE_INTERVAL 秒刷新一次
            if (modeFCachedAllHealthBars == null || Time.unscaledTime >= modeFNextAllHealthBarsRefreshTime)
            {
                modeFCachedAllHealthBars = UnityEngine.Object.FindObjectsOfType<HealthBar>();
                modeFNextAllHealthBarsRefreshTime = Time.unscaledTime + MODEF_ALL_HEALTHBARS_CACHE_INTERVAL;
            }

            HealthBar[] healthBars = modeFCachedAllHealthBars;
            for (int i = 0; i < modeFState.ActiveBosses.Count; i++)
            {
                CharacterMainControl boss = modeFState.ActiveBosses[i];
                if (boss == null || boss.Health == null || boss.Health.IsDead)
                {
                    continue;
                }

                try
                {
                    boss.Health.showHealthBar = true;
                }
                catch { }

                HealthBar healthBar = FindModeFHealthBar(healthBars, boss.Health);
                if (healthBar != null)
                {
                    ForceRefreshModeFHealthBarName(healthBar);
                }
                else
                {
                    try { boss.Health.RequestHealthBar(); } catch { }
                }
            }
        }

        private void UpdateModeFBountyRadarUI()
        {
            if (!ShouldShowModeFBountyRadar())
            {
                HideModeFBountyRadarEntries();
                return;
            }

            if (Time.unscaledTime < modeFNextBountyRadarRefreshTime)
            {
                return;
            }

            modeFNextBountyRadarRefreshTime = Time.unscaledTime + MODEF_BOUNTY_RADAR_REFRESH_INTERVAL;

            CharacterMainControl player = CharacterMainControl.Main;
            Camera radarCamera = GetModeFBountyRadarCamera();
            if (player == null || player.transform == null || radarCamera == null)
            {
                HideModeFBountyRadarEntries();
                return;
            }

            EnsureModeFBountyRadarUI();
            if (modeFBountyRadarCenterRect == null)
            {
                return;
            }

            Vector3 playerPos = player.transform.position;
            modeFBountyRadarTargetScratch.Clear();

            for (int i = 0; i < modeFState.ActiveBosses.Count; i++)
            {
                CharacterMainControl boss = modeFState.ActiveBosses[i];
                if (boss == null || boss.transform == null || boss.Health == null || boss.Health.IsDead)
                {
                    continue;
                }

                int marks = 0;
                if (!modeFState.BountyMarksByCharacterId.TryGetValue(boss.GetInstanceID(), out marks) || marks <= 0)
                {
                    continue;
                }

                if (IsModeFBountyRadarTargetVisible(radarCamera, boss))
                {
                    continue;
                }

                Vector3 delta = boss.transform.position - playerPos;
                delta.y = 0f;
                modeFBountyRadarTargetScratch.Add(new ModeFBountyRadarTarget
                {
                    boss = boss,
                    marks = marks,
                    distanceSqr = delta.sqrMagnitude
                });
            }

            modeFBountyRadarTargetScratch.Sort((a, b) => a.distanceSqr.CompareTo(b.distanceSqr));

            int regularCount = Mathf.Min(MODEF_BOUNTY_RADAR_MAX_TARGETS, modeFBountyRadarTargetScratch.Count);
            for (int i = 0; i < modeFBountyRadarEntries.Count; i++)
            {
                if (i < regularCount)
                {
                    ModeFBountyRadarTarget target = modeFBountyRadarTargetScratch[i];
                    UpdateModeFBountyRadarEntry(
                        modeFBountyRadarEntries[i],
                        target.boss,
                        target.marks,
                        MODEF_BOUNTY_RADAR_REGULAR_RADIUS,
                        MODEF_BOUNTY_RADAR_REGULAR_SIZE,
                        false,
                        playerPos,
                        radarCamera);
                }
                else if (modeFBountyRadarEntries[i] != null && modeFBountyRadarEntries[i].root != null)
                {
                    modeFBountyRadarEntries[i].root.SetActive(false);
                }
            }

            int leaderMarks = 0;
            CharacterMainControl leader = GetModeFBountyRadarLeader(out leaderMarks);
            bool showLeader = leader != null &&
                              leaderMarks > 0 &&
                              leader.transform != null &&
                              !IsModeFBountyRadarTargetVisible(radarCamera, leader);
            if (showLeader)
            {
                UpdateModeFBountyRadarEntry(
                    modeFBountyLeaderRadarEntry,
                    leader,
                    leaderMarks,
                    MODEF_BOUNTY_RADAR_LEADER_RADIUS,
                    MODEF_BOUNTY_RADAR_LEADER_SIZE,
                    true,
                    playerPos,
                    radarCamera);
            }
            else if (modeFBountyLeaderRadarEntry != null && modeFBountyLeaderRadarEntry.root != null)
            {
                modeFBountyLeaderRadarEntry.root.SetActive(false);
            }

            if (modeFBountyRadarGuideImage != null)
            {
                modeFBountyRadarGuideImage.gameObject.SetActive(false);
            }
        }

        private bool ShouldShowModeFBountyRadar()
        {
            if (!modeFActive)
            {
                return false;
            }

            switch (modeFState.CurrentPhase)
            {
                case ModeFPhase.Bounty:
                case ModeFPhase.HuntStorm:
                case ModeFPhase.Extraction:
                    break;
                default:
                    return false;
            }

            return modeFState.PlayerBountyMarks > 0 ||
                   modeFState.CurrentBountyLeaderMarks > 0 ||
                   modeFState.BountyMarksByCharacterId.Count > 0;
        }

        private void EnsureModeFBountyRadarUI()
        {
            if (modeFBountyRadarCanvasObject != null &&
                modeFBountyRadarCenterRect != null &&
                modeFBountyRadarCanvasObject.activeInHierarchy)
            {
                return;
            }

            CleanupModeFBountyRadarUI();

            GameObject root = new GameObject("ModeF_BountyRadarCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = MODEF_BOUNTY_RADAR_CANVAS_ORDER;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GraphicRaycaster raycaster = root.GetComponent<GraphicRaycaster>();
            raycaster.enabled = false;

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            GameObject centerObject = new GameObject("Center", typeof(RectTransform));
            RectTransform centerRect = centerObject.GetComponent<RectTransform>();
            centerRect.SetParent(root.transform, false);
            centerRect.anchorMin = new Vector2(0.5f, 0.5f);
            centerRect.anchorMax = new Vector2(0.5f, 0.5f);
            centerRect.pivot = new Vector2(0.5f, 0.5f);
            centerRect.anchoredPosition = Vector2.zero;
            centerRect.sizeDelta = Vector2.zero;

            GameObject guideObject = new GameObject("GuideRing", typeof(RectTransform), typeof(Image));
            RectTransform guideRect = guideObject.GetComponent<RectTransform>();
            guideRect.SetParent(centerRect, false);
            guideRect.anchorMin = new Vector2(0.5f, 0.5f);
            guideRect.anchorMax = new Vector2(0.5f, 0.5f);
            guideRect.pivot = new Vector2(0.5f, 0.5f);
            guideRect.sizeDelta = new Vector2(MODEF_BOUNTY_RADAR_GUIDE_SIZE, MODEF_BOUNTY_RADAR_GUIDE_SIZE);

            Image guideImage = guideObject.GetComponent<Image>();
            guideImage.sprite = GetModeFBountyRadarGuideSprite();
            guideImage.raycastTarget = false;

            modeFBountyRadarCanvasObject = root;
            modeFBountyRadarRootRect = rootRect;
            modeFBountyRadarCenterRect = centerRect;
            modeFBountyRadarGuideImage = guideImage;
            modeFBountyRadarGuideImage.gameObject.SetActive(false);

            modeFBountyRadarEntries.Clear();
            for (int i = 0; i < MODEF_BOUNTY_RADAR_MAX_TARGETS; i++)
            {
                modeFBountyRadarEntries.Add(CreateModeFBountyRadarEntry("Regular_" + i, false));
            }

            modeFBountyLeaderRadarEntry = CreateModeFBountyRadarEntry("Leader", true);
        }

        private ModeFBountyRadarEntryUi CreateModeFBountyRadarEntry(string name, bool leaderStyle)
        {
            if (modeFBountyRadarCenterRect == null)
            {
                return null;
            }

            float size = leaderStyle ? MODEF_BOUNTY_RADAR_LEADER_SIZE : MODEF_BOUNTY_RADAR_REGULAR_SIZE;
            TMP_FontAsset font = GetModeFBountyRadarFont();

            GameObject root = new GameObject("ModeF_BountyRadar_" + name, typeof(RectTransform));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(modeFBountyRadarCenterRect, false);
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(size, size);

            GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.SetParent(rootRect, false);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;

            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = leaderStyle ? GetModeFBountyRadarLeaderSprite() : GetModeFBountyRadarRegularSprite();
            icon.raycastTarget = false;

            GameObject countObject = new GameObject("Count", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform countRect = countObject.GetComponent<RectTransform>();
            countRect.SetParent(iconRect, false);
            countRect.anchorMin = Vector2.zero;
            countRect.anchorMax = Vector2.one;
            countRect.offsetMin = Vector2.zero;
            countRect.offsetMax = Vector2.zero;

            TextMeshProUGUI countText = countObject.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                countText.font = font;
            }
            countText.alignment = TextAlignmentOptions.Center;
            countText.fontSize = leaderStyle ? 25f : 21f;
            countText.color = Color.white;
            countText.raycastTarget = false;

            GameObject distanceObject = new GameObject("Distance", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform distanceRect = distanceObject.GetComponent<RectTransform>();
            distanceRect.SetParent(rootRect, false);
            distanceRect.anchorMin = new Vector2(0.5f, 0.5f);
            distanceRect.anchorMax = new Vector2(0.5f, 0.5f);
            distanceRect.pivot = new Vector2(0.5f, 0.5f);
            distanceRect.anchoredPosition = new Vector2(0f, -size * 0.82f);
            distanceRect.sizeDelta = new Vector2(90f, 26f);

            TextMeshProUGUI distanceText = distanceObject.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                distanceText.font = font;
            }
            distanceText.alignment = TextAlignmentOptions.Center;
            distanceText.fontSize = leaderStyle ? 18f : 16f;
            distanceText.color = Color.white;
            distanceText.raycastTarget = false;

            root.SetActive(false);
            return new ModeFBountyRadarEntryUi
            {
                root = root,
                rect = rootRect,
                icon = icon,
                countText = countText,
                distanceRect = distanceRect,
                distanceText = distanceText
            };
        }

        private void UpdateModeFBountyRadarEntry(
            ModeFBountyRadarEntryUi entry,
            CharacterMainControl boss,
            int marks,
            float radius,
            float size,
            bool leaderStyle,
            Vector3 playerPos,
            Camera radarCamera)
        {
            if (entry == null || entry.root == null || boss == null || boss.transform == null || radarCamera == null)
            {
                return;
            }

            Vector2 direction = GetModeFBountyRadarDirection(playerPos, boss.transform.position, radarCamera.transform);
            entry.rect.sizeDelta = new Vector2(size, size);
            entry.rect.anchoredPosition = direction * radius;

            if (entry.icon != null)
            {
                entry.icon.sprite = leaderStyle ? GetModeFBountyRadarLeaderSprite() : GetModeFBountyRadarRegularSprite();
            }

            if (entry.countText != null)
            {
                entry.countText.fontSize = leaderStyle ? 25f : 21f;
                entry.countText.text = Mathf.Max(1, marks).ToString();
            }

            if (entry.distanceRect != null)
            {
                entry.distanceRect.anchoredPosition = new Vector2(0f, -size * 0.82f);
            }

            if (entry.distanceText != null)
            {
                entry.distanceText.fontSize = leaderStyle ? 18f : 16f;
                entry.distanceText.text = Mathf.RoundToInt(Vector3.Distance(playerPos, boss.transform.position)) + "m";
            }

            if (!entry.root.activeSelf)
            {
                entry.root.SetActive(true);
            }
        }

        private CharacterMainControl GetModeFBountyRadarLeader(out int leaderMarks)
        {
            leaderMarks = 0;

            CharacterMainControl currentLeader = modeFState.CurrentBountyLeader;
            if (currentLeader != null &&
                currentLeader.Health != null &&
                !currentLeader.Health.IsDead &&
                modeFState.BountyMarksByCharacterId.TryGetValue(currentLeader.GetInstanceID(), out leaderMarks) &&
                leaderMarks > 0)
            {
                return currentLeader;
            }

            CharacterMainControl bestLeader = null;
            int bestMarks = 0;
            for (int i = 0; i < modeFState.ActiveBosses.Count; i++)
            {
                CharacterMainControl boss = modeFState.ActiveBosses[i];
                if (boss == null || boss.Health == null || boss.Health.IsDead)
                {
                    continue;
                }

                int marks = 0;
                if (!modeFState.BountyMarksByCharacterId.TryGetValue(boss.GetInstanceID(), out marks) || marks <= 0)
                {
                    continue;
                }

                if (marks > bestMarks)
                {
                    bestMarks = marks;
                    bestLeader = boss;
                }
            }

            leaderMarks = bestMarks;
            return bestLeader;
        }

        private Camera GetModeFBountyRadarCamera()
        {
            if (GameCamera.Instance != null && GameCamera.Instance.renderCamera != null)
            {
                return GameCamera.Instance.renderCamera;
            }

            return Camera.main;
        }

        private void HideModeFBountyRadarEntries()
        {
            if (modeFBountyLeaderRadarEntry != null && modeFBountyLeaderRadarEntry.root != null)
            {
                modeFBountyLeaderRadarEntry.root.SetActive(false);
            }

            for (int i = 0; i < modeFBountyRadarEntries.Count; i++)
            {
                ModeFBountyRadarEntryUi entry = modeFBountyRadarEntries[i];
                if (entry != null && entry.root != null)
                {
                    entry.root.SetActive(false);
                }
            }

            if (modeFBountyRadarGuideImage != null)
            {
                modeFBountyRadarGuideImage.gameObject.SetActive(false);
            }
        }

        private void CleanupModeFBountyRadarUI()
        {
            if (modeFBountyRadarCanvasObject != null)
            {
                UnityEngine.Object.Destroy(modeFBountyRadarCanvasObject);
            }

            modeFBountyRadarCanvasObject = null;
            modeFBountyRadarRootRect = null;
            modeFBountyRadarCenterRect = null;
            modeFBountyRadarGuideImage = null;
            modeFBountyLeaderRadarEntry = null;
            modeFBountyRadarEntries.Clear();
            modeFBountyRadarTargetScratch.Clear();
        }

        private void CleanupModeFPlayerNameTag()
        {
            modeFCachedPlayerHealthBar = null;
            modeFNextHealthBarLookupTime = 0f;

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null)
            {
                return;
            }

            HealthBar healthBar = FindModeFHealthBar(player.Health);
            if (healthBar == null)
            {
                return;
            }

            ForceRefreshModeFHealthBarName(healthBar);
        }

        private HealthBar FindModeFHealthBar(Health health)
        {
            if (health == null)
            {
                return null;
            }

            if (modeFCachedPlayerHealthBar != null)
            {
                if (modeFCachedPlayerHealthBar.target == health)
                {
                    return modeFCachedPlayerHealthBar;
                }

                modeFCachedPlayerHealthBar = null;
            }

            if (Time.unscaledTime < modeFNextHealthBarLookupTime)
            {
                return null;
            }

            modeFNextHealthBarLookupTime = Time.unscaledTime + MODEF_HEALTHBAR_LOOKUP_INTERVAL;

            HealthBar[] healthBars = UnityEngine.Object.FindObjectsOfType<HealthBar>();
            for (int i = 0; i < healthBars.Length; i++)
            {
                HealthBar healthBar = healthBars[i];
                if (healthBar != null && healthBar.target == health)
                {
                    modeFCachedPlayerHealthBar = healthBar;
                    return healthBar;
                }
            }

            return null;
        }

        private static HealthBar FindModeFHealthBar(HealthBar[] healthBars, Health health)
        {
            if (healthBars == null || health == null)
            {
                return null;
            }

            for (int i = 0; i < healthBars.Length; i++)
            {
                HealthBar healthBar = healthBars[i];
                if (healthBar != null && healthBar.target == health)
                {
                    return healthBar;
                }
            }

            return null;
        }

        private static void ForceRefreshModeFHealthBarName(HealthBar healthBar)
        {
            if (healthBar == null)
            {
                return;
            }

            try
            {
                MethodInfo refreshCharacterIcon = GetModeFRefreshCharacterIconMethod();
                if (refreshCharacterIcon != null)
                {
                    refreshCharacterIcon.Invoke(healthBar, null);
                }
            }
            catch { }
        }

        private static bool IsModeFBountyRadarTargetVisible(Camera camera, CharacterMainControl boss)
        {
            if (camera == null || boss == null || boss.transform == null)
            {
                return false;
            }

            Vector3 viewport = camera.WorldToViewportPoint(boss.transform.position + Vector3.up * MODEF_BOUNTY_RADAR_WORLD_HEIGHT);
            return viewport.z > 0f &&
                   viewport.x >= 0f &&
                   viewport.x <= 1f &&
                   viewport.y >= 0f &&
                   viewport.y <= 1f;
        }

        private static Vector2 GetModeFBountyRadarDirection(Vector3 playerPos, Vector3 targetPos, Transform cameraTransform)
        {
            Vector3 toTarget = targetPos - playerPos;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= 0.001f)
            {
                return Vector2.up;
            }

            Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            Vector3 right = cameraTransform != null ? cameraTransform.right : Vector3.right;
            right.y = 0f;
            if (right.sqrMagnitude <= 0.001f)
            {
                right = Vector3.right;
            }
            right.Normalize();

            Vector2 direction = new Vector2(
                Vector3.Dot(toTarget.normalized, right),
                Vector3.Dot(toTarget.normalized, forward));
            return direction.sqrMagnitude <= 0.001f ? Vector2.up : direction.normalized;
        }

        private TMP_FontAsset GetModeFBountyRadarFont()
        {
            if (modeFBountyRadarFont != null)
            {
                return modeFBountyRadarFont;
            }

            try
            {
                if (TMP_Settings.defaultFontAsset != null)
                {
                    modeFBountyRadarFont = TMP_Settings.defaultFontAsset;
                    return modeFBountyRadarFont;
                }
            }
            catch { }

            try
            {
                TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (fonts != null && fonts.Length > 0)
                {
                    modeFBountyRadarFont = fonts[0];
                }
            }
            catch { }

            return modeFBountyRadarFont;
        }

        private static Sprite GetModeFBountyRadarRegularSprite()
        {
            if (modeFBountyRadarRegularSprite == null)
            {
                modeFBountyRadarRegularSprite = LoadModeFBountyRadarSpriteFromFile(
                    MODEF_BOUNTY_RADAR_REGULAR_SPRITE_PATH,
                    MODEF_BOUNTY_RADAR_REGULAR_SPRITE_PATH_LEGACY);
                if (modeFBountyRadarRegularSprite == null)
                {
                    modeFBountyRadarRegularSprite = CreateModeFBountyRadarSprite(
                        new Color(0.95f, 0.28f, 0.18f, 0.28f),
                        new Color(1f, 0.72f, 0.32f, 0.95f),
                        0.22f,
                        0.40f);
                }
            }

            return modeFBountyRadarRegularSprite;
        }

        private static Sprite GetModeFBountyRadarLeaderSprite()
        {
            if (modeFBountyRadarLeaderSprite == null)
            {
                modeFBountyRadarLeaderSprite = LoadModeFBountyRadarSpriteFromFile(
                    MODEF_BOUNTY_RADAR_LEADER_SPRITE_PATH,
                    MODEF_BOUNTY_RADAR_LEADER_SPRITE_PATH_LEGACY);
                if (modeFBountyRadarLeaderSprite == null)
                {
                    modeFBountyRadarLeaderSprite = CreateModeFBountyRadarSprite(
                        new Color(0.95f, 0.78f, 0.18f, 0.30f),
                        new Color(1f, 0.93f, 0.55f, 1f),
                        0.18f,
                        0.44f);
                }
            }

            return modeFBountyRadarLeaderSprite;
        }

        private static Sprite GetModeFBountyRadarGuideSprite()
        {
            if (modeFBountyRadarGuideSprite == null)
            {
                modeFBountyRadarGuideSprite = CreateModeFBountyRadarSprite(
                    new Color(1f, 1f, 1f, 0f),
                    new Color(1f, 1f, 1f, 0.20f),
                    0.47f,
                    0.50f);
            }

            return modeFBountyRadarGuideSprite;
        }

        private static Sprite CreateModeFBountyRadarSprite(Color fillColor, Color ringColor, float fillRadius, float ringRadius)
        {
            const int textureSize = 128;
            Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            float halfSize = (textureSize - 1) * 0.5f;
            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float normalizedX = (x - halfSize) / halfSize;
                    float normalizedY = (y - halfSize) / halfSize;
                    float distance = Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);

                    Color pixel = clear;
                    if (distance <= ringRadius)
                    {
                        if (distance <= fillRadius)
                        {
                            pixel = fillColor;
                        }
                        else
                        {
                            float ringBlend = Mathf.InverseLerp(fillRadius, ringRadius, distance);
                            pixel = Color.Lerp(fillColor, ringColor, Mathf.Clamp01(ringBlend));
                        }
                    }

                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply();
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width);
        }

        private static Sprite LoadModeFBountyRadarSpriteFromFile(params string[] relativePaths)
        {
            if (relativePaths == null || relativePaths.Length <= 0)
            {
                return null;
            }

            for (int i = 0; i < relativePaths.Length; i++)
            {
                string relativePath = relativePaths[i];
                if (string.IsNullOrEmpty(relativePath))
                {
                    continue;
                }

                try
                {
                    Sprite sprite = ItemFactory.GetSpriteFromFile(relativePath);
                    if (sprite != null)
                    {
                        DevLog("[ModeF] 已加载悬赏雷达贴图: " + relativePath);
                        return sprite;
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ModeF] 加载悬赏雷达贴图失败: " + relativePath + " - " + e.Message);
                }
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(HealthBar), "LateUpdate")]
    public static class ModeFHealthBarNamePatch
    {
        private static ModBehaviour cachedInstance;
        private static int lastRefreshFrame = -1;
        private const string ModeFMarkSuffixZhPrefix = " [印记: ";
        private const string ModeFMarkSuffixEnPrefix = " [Mark: ";
        private const string ModeFMarkSuffixZhRichPrefix = " <color=yellow>悬赏";
        private const string ModeFMarkSuffixEnRichPrefix = " <color=yellow>Bounty ";
        private const char ModeFMarkSuffixStar = '⭐';

        [HarmonyPostfix]
        public static void Postfix(HealthBar __instance, TextMeshProUGUI ___nameText)
        {
            int currentFrame = Time.frameCount;
            if (cachedInstance == null || currentFrame - lastRefreshFrame >= 60)
            {
                lastRefreshFrame = currentFrame;
                cachedInstance = ModBehaviour.Instance;
            }

            if (cachedInstance == null || !cachedInstance.IsModeFActive || ___nameText == null)
            {
                return;
            }

            Health target = __instance.target;
            if (target == null)
            {
                return;
            }

            CharacterMainControl character = target.TryGetCharacter();
            if (character == null)
            {
                return;
            }

            bool isPlayer = character.IsMainCharacter;
            bool forceShowName = cachedInstance.ShouldForceModeFHealthBarName(character);
            if (!forceShowName)
            {
                return;
            }

            string baseText = isPlayer
                ? cachedInstance.GetModeFPlayerName()
                : cachedInstance.GetModeFActorDisplayName(character);
            if (string.IsNullOrEmpty(baseText))
            {
                baseText = StripModeFMarkSuffix(___nameText.text);
            }

            string suffix = isPlayer
                ? cachedInstance.GetModeFPlayerMarkSuffix()
                : cachedInstance.GetModeFBountyMarkSuffix(character);
            string desiredText = string.IsNullOrEmpty(suffix) ? baseText : baseText + suffix;

            if (forceShowName && !___nameText.gameObject.activeSelf)
            {
                ___nameText.gameObject.SetActive(true);
            }

            if (!string.Equals(___nameText.text, desiredText, StringComparison.Ordinal))
            {
                ___nameText.text = desiredText;
            }
        }

        private static string StripModeFMarkSuffix(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            string sanitized = text;
            while (TryTrimTrailingModeFMarkSuffix(ref sanitized))
            {
            }

            return sanitized;
        }

        private static bool TryTrimTrailingModeFMarkSuffix(ref string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (text.EndsWith("</color>", StringComparison.Ordinal) &&
                (TryTrimTrailingModeFRichMarkSuffix(ref text, ModeFMarkSuffixZhRichPrefix) ||
                 TryTrimTrailingModeFRichMarkSuffix(ref text, ModeFMarkSuffixEnRichPrefix)))
            {
                return true;
            }

            if (text[text.Length - 1] == ModeFMarkSuffixStar &&
                TryTrimTrailingModeFStarSuffix(ref text))
            {
                return true;
            }

            if (text[text.Length - 1] != ']')
            {
                return false;
            }

            return TryTrimTrailingModeFMarkSuffix(ref text, ModeFMarkSuffixZhPrefix) ||
                   TryTrimTrailingModeFMarkSuffix(ref text, ModeFMarkSuffixEnPrefix);
        }

        private static bool TryTrimTrailingModeFStarSuffix(ref string text)
        {
            int endIndex = text.Length - 1;
            int digitEnd = endIndex - 1;
            if (digitEnd < 0)
            {
                return false;
            }

            int digitStart = digitEnd;
            while (digitStart >= 0 && char.IsDigit(text[digitStart]))
            {
                digitStart--;
            }

            if (digitStart == digitEnd || digitStart < 0 || text[digitStart] != ' ')
            {
                return false;
            }

            text = text.Substring(0, digitStart);
            return true;
        }

        private static bool TryTrimTrailingModeFRichMarkSuffix(ref string text, string prefix)
        {
            const string richTextEndTag = "</color>";
            if (!text.EndsWith(richTextEndTag, StringComparison.Ordinal))
            {
                return false;
            }

            int richSuffixEnd = text.Length - richTextEndTag.Length;
            int startIndex = text.LastIndexOf(prefix, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return false;
            }

            int digitsStart = startIndex + prefix.Length;
            if (digitsStart >= richSuffixEnd)
            {
                return false;
            }

            for (int i = digitsStart; i < richSuffixEnd; i++)
            {
                if (!char.IsDigit(text[i]))
                {
                    return false;
                }
            }

            text = text.Substring(0, startIndex);
            return true;
        }

        private static bool TryTrimTrailingModeFMarkSuffix(ref string text, string prefix)
        {
            int startIndex = text.LastIndexOf(prefix, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return false;
            }

            int digitsStart = startIndex + prefix.Length;
            if (digitsStart >= text.Length - 1)
            {
                return false;
            }

            for (int i = digitsStart; i < text.Length - 1; i++)
            {
                if (!char.IsDigit(text[i]))
                {
                    return false;
                }
            }

            text = text.Substring(0, startIndex);
            return true;
        }
    }
}
