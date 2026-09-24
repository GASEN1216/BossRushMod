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
        private const float MODEF_BOUNTY_RADAR_EDGE_MARGIN = 54f;
        private const int MODEF_BOUNTY_RADAR_CANVAS_ORDER = BossRushUILayers.ModeFBountyRadar;
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
        private static Camera modeFBountyRadarCachedMainCamera = null;
        private static int modeFBountyRadarCachedMainCameraFrame = -1;
        private static Sprite modeFBountyRadarRegularSprite = null;
        private static Sprite modeFBountyRadarLeaderSprite = null;
        private static Sprite modeFBountyRadarGuideSprite = null;
        private static Sprite modeFBountyRadarArrowSprite = null;
        // 雷达配色走 token（UI 共识对照审查 B-31）：普通悬赏 DangerText、榜首 WarningText（与状态卡的红竖条、金进度条同色系），
        // 距离底板是 Surface 压到 0.55。
        private static readonly Color ModeFBountyRadarRegularColor = BossRushUIColors.DangerText;
        private static readonly Color ModeFBountyRadarLeaderColor = BossRushUIColors.WarningText;
        private static readonly Color ModeFBountyRadarDistancePanelColor = new Color(
            BossRushUIColors.Surface.r, BossRushUIColors.Surface.g, BossRushUIColors.Surface.b, 0.55f);

        private struct ModeFBountyRadarTarget
        {
            public CharacterMainControl boss;
            public Vector3 position;
            public int marks;
            public float distanceSqr;
            public float displayDistanceSqr;
        }

        private sealed class ModeFBountyRadarEntryUi
        {
            public GameObject root;
            public RectTransform rect;
            public CanvasGroup canvasGroup;
            public RectTransform pulseRect;
            public Image pulseImage;
            public RectTransform directionRect;
            public Image directionImage;
            public Image icon;
            public TextMeshProUGUI countText;
            public RectTransform distanceRect;
            public Image distanceBackground;
            public TextMeshProUGUI distanceText;
            public bool leaderStyle;
            /// <summary>正在淡出（目标进入视野 / 名额被挤掉），alpha 到 0 再 SetActive(false)。</summary>
            public bool hiding;
        }

        // 距离标签：榜首的写「首领 · 42m」，比普通目标宽一截（UB-24 把 9 号「首领」字并进来）。
        // 框高补足到 ≥ 字号×1.45+4（B-31）：旧的 22 / 18 比一行还矮，自动缩字会把 15 / 14 号压到下限 10 号。
        private static readonly Vector2 ModeFBountyRadarLeaderLabelSize = new Vector2(100f, 26f);
        private static readonly Vector2 ModeFBountyRadarRegularLabelSize = new Vector2(62f, 25f);

        // ====================================================================
        // 与 ModeE 血条名牌那套的关系：**保持两套独立，不要合并**
        // ====================================================================
        // ModeE/ModeEUiAndHealthBars.cs 有一组名字对称的方法，但两边行为已分叉：
        // 本文件不保存原始文本（没有 BaseText 表）、节流放在 ScanAndCacheModeFHealthBars(force)
        // 而不是 Find 里、失败走空 catch 而非限流日志，并且额外挂着悬赏后缀缓存与赏金雷达节流。
        // 详细差异清单见 ModeEUiAndHealthBars.cs 顶部注释。
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
                ? RichWarningTag + "悬赏" + marks + "</color>"
                : RichWarningTag + "Bounty " + marks + "</color>";
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

        // 阶段 / 剩余时间 / 命火的持续状态在常驻状态卡 ModeFStatusHud（2026-09-23 审美审查 UB-06），
        // 不再每 15 秒广播一条彩色长横幅；横幅只留阶段切换、榜首变更与胜负这类事件。

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
                        RichWarningTag + leaderName + "</color> 成为悬赏榜首！ " + markTextZh,
                        RichWarningTag + leaderName + "</color> is now the Bounty Leader! " + markTextEn
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
                    RichWarningTag + killerName + "</color> 啃噬了 " + RichDangerTag + victimName
                        + "</color> 的命火！" + RichWarningTag + "最大生命与火力 +" + growthValue + "%</color>",
                    RichWarningTag + killerName + "</color> devoured " + RichDangerTag + victimName
                        + "</color> and stole its life! " + RichWarningTag + "Max HP and firepower +" + growthValue + "%</color>"
                ));
            }
            catch { }
        }

        /// <summary>
        /// 显示奖励气泡
        /// </summary>
        private void ShowModeFRewardBubble(string text, float duration = 2.5f)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.transform == null) return;

                DialogueBubblesManager.Show(text, player.transform, duration, false, false, -1f, 3f);
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

        internal void SetModeFBossDisplayName(CharacterMainControl actor, string displayName, Teams originalFaction, string nameKey = null)
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
            marker.NameKey = nameKey;
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

            return string.IsNullOrEmpty(marker.NameKey) ? marker.DisplayName : L10n.T(marker.NameKey);
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
