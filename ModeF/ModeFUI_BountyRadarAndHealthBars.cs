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
        private void MarkModeFPlayerNameTagDirty()
        {
            if (modeFActive)
            {
                MarkModeFHealthBarNamesDirty();
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
                HealthBar healthBar = FindModeFPlayerHealthBar(player.Health);
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

                HealthBar healthBar = FindModeFHealthBar(boss.Health);
                if (healthBar != null)
                {
                    ForceRefreshModeFHealthBarName(healthBar);
                    return;
                }

                boss.Health.RequestHealthBar();
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] EnsureModeFBossNameTag failed: " + e.Message);
            }
        }

        private string BuildModeFDesiredHealthBarText(CharacterMainControl character)
        {
            if (character == null)
            {
                return null;
            }

            string baseText = character.IsMainCharacter
                ? GetModeFPlayerName()
                : GetModeFActorDisplayName(character);
            if (string.IsNullOrEmpty(baseText))
            {
                return null;
            }

            string suffix = character.IsMainCharacter
                ? GetModeFPlayerMarkSuffix()
                : GetModeFBountyMarkSuffix(character);
            return string.IsNullOrEmpty(suffix) ? baseText : baseText + suffix;
        }

        internal bool ApplyModeFHealthBarNameOverride(HealthBar healthBar, TextMeshProUGUI nameText = null)
        {
            if (!modeFActive || healthBar == null)
            {
                return false;
            }

            RegisterModeFHealthBar(healthBar);
            nameText = nameText ?? GetModeFHealthBarNameText(healthBar);
            if (nameText == null)
            {
                return false;
            }

            Health target = healthBar.target;
            if (target == null)
            {
                ClearModeFHealthBarOverrideCache(healthBar);
                return false;
            }

            CharacterMainControl character = target.TryGetCharacter();
            if (character == null || !ShouldForceModeFHealthBarName(character))
            {
                ClearModeFHealthBarOverrideCache(healthBar);
                return false;
            }

            SyncModeFHealthBarNameLanguageState();
            int barId = healthBar.GetInstanceID();
            int targetId = target.GetInstanceID();
            string desiredText = null;
            int appliedVersion = 0;
            int cachedTargetId = 0;
            bool needsRebuild =
                !modeFHealthBarDesiredTextByBarId.TryGetValue(barId, out desiredText) ||
                string.IsNullOrEmpty(desiredText) ||
                !modeFHealthBarAppliedVersionByBarId.TryGetValue(barId, out appliedVersion) ||
                appliedVersion != modeFHealthBarNameVersion ||
                !modeFHealthBarTargetIdsByBarId.TryGetValue(barId, out cachedTargetId) ||
                cachedTargetId != targetId;

            if (needsRebuild)
            {
                desiredText = BuildModeFDesiredHealthBarText(character);
                if (string.IsNullOrEmpty(desiredText))
                {
                    ClearModeFHealthBarOverrideCache(healthBar);
                    return false;
                }

                modeFHealthBarDesiredTextByBarId[barId] = desiredText;
                modeFHealthBarAppliedVersionByBarId[barId] = modeFHealthBarNameVersion;
                modeFHealthBarTargetIdsByBarId[barId] = targetId;
            }

            if (!nameText.gameObject.activeSelf)
            {
                nameText.gameObject.SetActive(true);
            }

            if (!string.Equals(nameText.text, desiredText, StringComparison.Ordinal))
            {
                nameText.text = desiredText;
            }

            return true;
        }

        public void RefreshModeFActorNameText(CharacterMainControl actor)
        {
            if (!modeFActive || actor == null || actor.Health == null) return;

            try
            {
                HealthBar healthBar = FindModeFHealthBar(actor.Health);
                if (healthBar != null)
                {
                    ForceRefreshModeFHealthBarName(healthBar);
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] RefreshModeFActorNameText failed: " + e.Message);
            }
        }

        private HealthBar FindModeFHealthBar(Health health)
        {
            if (health == null)
            {
                return null;
            }

            HealthBar healthBar = null;
            if (TryGetCachedModeFHealthBar(health, out healthBar))
            {
                return healthBar;
            }

            ScanAndCacheModeFHealthBars();
            if (TryGetCachedModeFHealthBar(health, out healthBar))
            {
                return healthBar;
            }

            return null;
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
            int leaderMarks = 0;
            CharacterMainControl leader = GetModeFBountyRadarLeader(out leaderMarks);
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

                if (object.ReferenceEquals(boss, leader))
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

            if (IsModeFBountyRadarSuppressedByOverlay())
            {
                return false;
            }

            return modeFState.PlayerBountyMarks > 0 ||
                   modeFState.CurrentBountyLeaderMarks > 0 ||
                   modeFState.BountyMarksByCharacterId.Count > 0;
        }

        private bool IsModeFBountyRadarSuppressedByOverlay()
        {
            if (BossRush.Utils.NPCCommonUtils.IsAnyUIOpen())
            {
                return true;
            }

            try
            {
                Duckov.MiniMaps.UI.MiniMapView mapView = Duckov.MiniMaps.UI.MiniMapView.Instance;
                return mapView != null && mapView.open;
            }
            catch
            {
                return false;
            }
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
            if (currentLeader == null &&
                modeFState.CurrentBountyLeaderMarks > 0 &&
                modeFState.PlayerBountyMarks == modeFState.CurrentBountyLeaderMarks)
            {
                leaderMarks = modeFState.CurrentBountyLeaderMarks;
                return null;
            }

            if (currentLeader != null &&
                currentLeader.Health != null &&
                !currentLeader.Health.IsDead &&
                modeFState.BountyMarksByCharacterId.TryGetValue(currentLeader.GetInstanceID(), out leaderMarks) &&
                leaderMarks > 0)
            {
                return currentLeader;
            }

            CharacterMainControl bestLeader = null;
            int bestMarks = modeFState.PlayerBountyMarks;
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

            HealthBar healthBar = FindModeFPlayerHealthBar(player.Health);
            if (healthBar == null)
            {
                return;
            }

            ForceRefreshModeFHealthBarName(healthBar);
        }

        private HealthBar FindModeFPlayerHealthBar(Health health)
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

            HealthBar healthBar = FindModeFHealthBar(health);
            if (healthBar != null)
            {
                modeFCachedPlayerHealthBar = healthBar;
            }

            return healthBar;
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

            try
            {
                ModBehaviour instance = ModBehaviour.Instance;
                if (instance != null)
                {
                    instance.ApplyModeFHealthBarNameOverride(healthBar);
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
                modeFBountyRadarFont = ObjectCache.GetFirstTmpFont();
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
    public static partial class BossRushHealthBarNamePatch
    {
        private static ModBehaviour cachedInstance;
        private static int lastRefreshFrame = -1;
        private static readonly Dictionary<int, int> lastProcessedFrameByBarId = new Dictionary<int, int>();
        private static int lastCleanupFrame = -1;
        private const int HEALTHBAR_PATCH_FRAME_INTERVAL = 6;
        private const int HEALTHBAR_CACHE_STALE_FRAMES = 300;
        private const int HEALTHBAR_CLEANUP_INTERVAL = 600;

        /// <summary>
        /// 缓存玩家 HealthBar 的 InstanceID，避免每帧调用 TryGetCharacter。
        /// -1 表示尚未识别；识别后每 HEALTHBAR_CLEANUP_INTERVAL 帧重新校验一次。
        /// </summary>
        private static int cachedPlayerBarId = -1;
        private static int playerBarIdCheckFrame = -1;
        private static readonly List<int> staleBarIdScratch = new List<int>();

        [HarmonyPostfix]
        public static void Postfix(HealthBar __instance, TextMeshProUGUI ___nameText)
        {
            int currentFrame = Time.frameCount;
            if (cachedInstance == null || currentFrame - lastRefreshFrame >= 60)
            {
                lastRefreshFrame = currentFrame;
                cachedInstance = ModBehaviour.Instance;
            }

            if (cachedInstance == null)
            {
                if (lastProcessedFrameByBarId.Count > 0)
                    lastProcessedFrameByBarId.Clear();
                cachedPlayerBarId = -1;
                return;
            }

            bool isModeF = cachedInstance.IsModeFActive;
            bool isModeE = !isModeF && cachedInstance.IsModeEActive;

            if (!isModeF && !isModeE)
            {
                if (lastProcessedFrameByBarId.Count > 0)
                    lastProcessedFrameByBarId.Clear();
                cachedPlayerBarId = -1;
                return;
            }

            // 定期清理长期未更新的过期条目，防止无限积累失效 HealthBar ID
            if (currentFrame - lastCleanupFrame >= HEALTHBAR_CLEANUP_INTERVAL)
            {
                lastCleanupFrame = currentFrame;
                cachedPlayerBarId = -1;
                staleBarIdScratch.Clear();
                foreach (var kv in lastProcessedFrameByBarId)
                {
                    if (currentFrame - kv.Value >= HEALTHBAR_CACHE_STALE_FRAMES)
                        staleBarIdScratch.Add(kv.Key);
                }
                for (int ri = 0; ri < staleBarIdScratch.Count; ri++)
                    lastProcessedFrameByBarId.Remove(staleBarIdScratch[ri]);
                staleBarIdScratch.Clear();
            }

            int barId = __instance.GetInstanceID();

            // 玩家血条：原版 LateUpdate 每帧会隐藏玩家 nameText，必须每帧强制恢复，否则闪烁。
            // Boss 血条：原版不会隐藏，可以节流处理。
            // 用缓存的 InstanceID 判断，避免每帧 TryGetCharacter 开销。
            bool isPlayerBar = barId == cachedPlayerBarId;
            if (!isPlayerBar && (cachedPlayerBarId == -1 || currentFrame - playerBarIdCheckFrame >= HEALTHBAR_CLEANUP_INTERVAL))
            {
                Health patchTarget = __instance.target;
                CharacterMainControl patchChar = patchTarget != null ? patchTarget.TryGetCharacter() : null;
                if (patchChar != null && patchChar.IsMainCharacter)
                {
                    cachedPlayerBarId = barId;
                    playerBarIdCheckFrame = currentFrame;
                    isPlayerBar = true;
                }
            }

            if (!isPlayerBar)
            {
                int lastFrame;
                if (lastProcessedFrameByBarId.TryGetValue(barId, out lastFrame) &&
                    currentFrame - lastFrame < HEALTHBAR_PATCH_FRAME_INTERVAL)
                {
                    return;
                }
                lastProcessedFrameByBarId[barId] = currentFrame;
            }
            // 玩家血条不节流：原版 LateUpdate 每帧会隐藏/重置玩家 nameText，
            // 必须每帧执行 override 恢复。场上只有一个玩家血条，开销可忽略。

            if (isModeF)
                cachedInstance.ApplyModeFHealthBarNameOverride(__instance, ___nameText);
            else
                cachedInstance.ApplyModeEHealthBarNameOverride(__instance, ___nameText);
        }
    }

}
