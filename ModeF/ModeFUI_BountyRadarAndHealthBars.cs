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

            AnimateModeFBountyRadarEntries();

            if (Time.unscaledTime < modeFNextBountyRadarRefreshTime)
            {
                return;
            }

            modeFNextBountyRadarRefreshTime = Time.unscaledTime + MODEF_BOUNTY_RADAR_REFRESH_INTERVAL;

            CharacterMainControl player = CharacterMainControl.Main;
            Camera radarCamera = GetModeFBountyRadarCamera();
            Transform playerTransform = player != null ? player.transform : null;
            if (playerTransform == null || radarCamera == null)
            {
                HideModeFBountyRadarEntries();
                return;
            }

            EnsureModeFBountyRadarUI();
            if (modeFBountyRadarCenterRect == null)
            {
                return;
            }

            Vector3 playerPos = playerTransform.position;
            Vector3 radarForward;
            Vector3 radarRight;
            GetModeFBountyRadarBasis(radarCamera.transform, out radarForward, out radarRight);
            int leaderMarks = 0;
            CharacterMainControl leader = GetModeFBountyRadarLeader(out leaderMarks);
            modeFBountyRadarTargetScratch.Clear();

            for (int i = 0; i < modeFState.ActiveBosses.Count; i++)
            {
                CharacterMainControl boss = modeFState.ActiveBosses[i];
                Transform bossTransform = boss != null ? boss.transform : null;
                if (boss == null || bossTransform == null || boss.Health == null || boss.Health.IsDead)
                {
                    continue;
                }

                int marks = 0;
                if (!modeFState.BountyMarksByCharacterId.TryGetValue(boss.GetInstanceID(), out marks) || marks <= 0)
                {
                    continue;
                }

                if (object.ReferenceEquals(boss, leader))
                {
                    continue;
                }

                Vector3 bossPos = bossTransform.position;
                if (IsModeFBountyRadarTargetVisible(radarCamera, bossPos))
                {
                    continue;
                }

                Vector3 delta = bossPos - playerPos;
                float displayDistanceSqr = delta.sqrMagnitude;
                delta.y = 0f;
                modeFBountyRadarTargetScratch.Add(new ModeFBountyRadarTarget
                {
                    boss = boss,
                    position = bossPos,
                    marks = marks,
                    distanceSqr = delta.sqrMagnitude,
                    displayDistanceSqr = displayDistanceSqr
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
                        target.position,
                        target.displayDistanceSqr,
                        playerPos,
                        radarForward,
                        radarRight);
                }
                else if (modeFBountyRadarEntries[i] != null && modeFBountyRadarEntries[i].root != null)
                {
                    modeFBountyRadarEntries[i].root.SetActive(false);
                }
            }

            Transform leaderTransform = leader != null ? leader.transform : null;
            Vector3 leaderPos = leaderTransform != null ? leaderTransform.position : Vector3.zero;
            bool showLeader = leader != null &&
                              leaderMarks > 0 &&
                              leaderTransform != null &&
                              !IsModeFBountyRadarTargetVisible(radarCamera, leaderPos);
            if (showLeader)
            {
                float leaderDisplayDistanceSqr = (leaderPos - playerPos).sqrMagnitude;
                UpdateModeFBountyRadarEntry(
                    modeFBountyLeaderRadarEntry,
                    leader,
                    leaderMarks,
                    MODEF_BOUNTY_RADAR_LEADER_RADIUS,
                    MODEF_BOUNTY_RADAR_LEADER_SIZE,
                    true,
                    leaderPos,
                    leaderDisplayDistanceSqr,
                    playerPos,
                    radarForward,
                    radarRight);
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
            // screenMatchMode 原本显式写成 MatchWidthOrHeight，而 CanvasScaler 是随
            // new GameObject(typeof(CanvasScaler)) 一起新建的，默认值本就是它，行为不变。
            ZombieModeUIHelper.ConfigureCanvasScaler(scaler);

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

            GameObject root = new GameObject("ModeF_BountyRadar_" + name, typeof(RectTransform), typeof(CanvasGroup));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(modeFBountyRadarCenterRect, false);
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(size, size);

            CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            RectTransform pulseRect = null;
            Image pulseImage = null;
            if (leaderStyle)
            {
                GameObject pulseObject = new GameObject("LeaderPulse", typeof(RectTransform), typeof(Image));
                pulseRect = pulseObject.GetComponent<RectTransform>();
                pulseRect.SetParent(rootRect, false);
                pulseRect.anchorMin = new Vector2(0.5f, 0.5f);
                pulseRect.anchorMax = new Vector2(0.5f, 0.5f);
                pulseRect.pivot = new Vector2(0.5f, 0.5f);
                pulseRect.sizeDelta = new Vector2(size * 1.34f, size * 1.34f);

                pulseImage = pulseObject.GetComponent<Image>();
                pulseImage.sprite = GetModeFBountyRadarGuideSprite();
                pulseImage.color = new Color(
                    ModeFBountyRadarLeaderColor.r,
                    ModeFBountyRadarLeaderColor.g,
                    ModeFBountyRadarLeaderColor.b,
                    0.12f);
                pulseImage.raycastTarget = false;
            }

            GameObject directionObject = new GameObject("Direction", typeof(RectTransform), typeof(Image));
            RectTransform directionRect = directionObject.GetComponent<RectTransform>();
            directionRect.SetParent(rootRect, false);
            directionRect.anchorMin = new Vector2(0.5f, 0.5f);
            directionRect.anchorMax = new Vector2(0.5f, 0.5f);
            directionRect.pivot = new Vector2(0.5f, 0.5f);
            directionRect.sizeDelta = leaderStyle ? new Vector2(18f, 13f) : new Vector2(15f, 11f);

            Image directionImage = directionObject.GetComponent<Image>();
            directionImage.sprite = GetModeFBountyRadarArrowSprite();
            directionImage.color = leaderStyle ? ModeFBountyRadarLeaderColor : ModeFBountyRadarRegularColor;
            directionImage.raycastTarget = false;

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
            countText.fontSize = leaderStyle ? 23f : 20f;
            countText.enableAutoSizing = true;
            countText.fontSizeMin = 11f;
            countText.fontSizeMax = leaderStyle ? 23f : 20f;
            countText.fontStyle = FontStyles.Bold;
            countText.color = Color.white;
            countText.raycastTarget = false;
            AddModeFBountyRadarTextOutline(countText, leaderStyle ? 1.5f : 1.2f);

            GameObject typeObject = new GameObject("Type", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform typeRect = typeObject.GetComponent<RectTransform>();
            typeRect.SetParent(iconRect, false);
            typeRect.anchorMin = new Vector2(0.5f, 0.5f);
            typeRect.anchorMax = new Vector2(0.5f, 0.5f);
            typeRect.pivot = new Vector2(0.5f, 0.5f);
            typeRect.anchoredPosition = new Vector2(0f, size * 0.23f);
            typeRect.sizeDelta = new Vector2(size * 0.86f, 14f);

            TextMeshProUGUI typeText = typeObject.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                typeText.font = font;
            }
            typeText.alignment = TextAlignmentOptions.Center;
            typeText.fontSize = 9f;
            typeText.fontStyle = FontStyles.Bold;
            typeText.color = leaderStyle ? ModeFBountyRadarLeaderColor : ModeFBountyRadarRegularColor;
            typeText.raycastTarget = false;
            typeText.gameObject.SetActive(leaderStyle);
            AddModeFBountyRadarTextOutline(typeText, 1f);

            GameObject distanceObject = new GameObject("Distance", typeof(RectTransform), typeof(Image));
            RectTransform distanceRect = distanceObject.GetComponent<RectTransform>();
            distanceRect.SetParent(rootRect, false);
            distanceRect.anchorMin = new Vector2(0.5f, 0.5f);
            distanceRect.anchorMax = new Vector2(0.5f, 0.5f);
            distanceRect.pivot = new Vector2(0.5f, 0.5f);
            distanceRect.anchoredPosition = new Vector2(0f, -size * 0.82f);
            distanceRect.sizeDelta = leaderStyle ? new Vector2(70f, 20f) : new Vector2(62f, 18f);

            Image distanceBackground = distanceObject.GetComponent<Image>();
            // 距离底板从 2x2 纯白硬边换成共享圆角九宫格，和其余界面同一套观感。
            BossRushUI.ApplyPanelSkin(distanceBackground, 6);
            distanceBackground.color = ModeFBountyRadarDistancePanelColor;
            distanceBackground.raycastTarget = false;

            GameObject distanceTextObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform distanceTextRect = distanceTextObject.GetComponent<RectTransform>();
            distanceTextRect.SetParent(distanceRect, false);
            distanceTextRect.anchorMin = Vector2.zero;
            distanceTextRect.anchorMax = Vector2.one;
            distanceTextRect.offsetMin = new Vector2(4f, 0f);
            distanceTextRect.offsetMax = new Vector2(-4f, 0f);

            TextMeshProUGUI distanceText = distanceTextObject.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                distanceText.font = font;
            }
            distanceText.alignment = TextAlignmentOptions.Center;
            distanceText.fontSize = leaderStyle ? 15f : 14f;
            distanceText.enableAutoSizing = true;
            distanceText.fontSizeMin = 10f;
            distanceText.fontSizeMax = leaderStyle ? 15f : 14f;
            distanceText.fontStyle = FontStyles.Bold;
            distanceText.color = leaderStyle ? ModeFBountyRadarLeaderColor : Color.white;
            distanceText.raycastTarget = false;
            AddModeFBountyRadarTextOutline(distanceText, 1f);

            root.SetActive(false);
            return new ModeFBountyRadarEntryUi
            {
                root = root,
                rect = rootRect,
                canvasGroup = canvasGroup,
                pulseRect = pulseRect,
                pulseImage = pulseImage,
                directionRect = directionRect,
                directionImage = directionImage,
                icon = icon,
                countText = countText,
                typeText = typeText,
                distanceRect = distanceRect,
                distanceBackground = distanceBackground,
                distanceText = distanceText,
                leaderStyle = leaderStyle
            };
        }

        private void UpdateModeFBountyRadarEntry(
            ModeFBountyRadarEntryUi entry,
            CharacterMainControl boss,
            int marks,
            float radius,
            float size,
            bool leaderStyle,
            Vector3 targetPos,
            float displayDistanceSqr,
            Vector3 playerPos,
            Vector3 radarForward,
            Vector3 radarRight)
        {
            if (entry == null || entry.root == null || boss == null)
            {
                return;
            }

            Vector2 direction = GetModeFBountyRadarDirection(playerPos, targetPos, radarForward, radarRight);
            entry.rect.sizeDelta = new Vector2(size, size);
            float safeRadius = GetModeFBountyRadarSafeRadius(radius, size);
            entry.rect.anchoredPosition = direction * safeRadius;

            if (entry.directionRect != null)
            {
                float directionAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
                entry.directionRect.anchoredPosition = direction * (size * 0.72f);
                entry.directionRect.localRotation = Quaternion.Euler(0f, 0f, directionAngle);
            }

            if (entry.directionImage != null)
            {
                entry.directionImage.color = leaderStyle ? ModeFBountyRadarLeaderColor : ModeFBountyRadarRegularColor;
            }

            if (entry.icon != null)
            {
                entry.icon.sprite = leaderStyle ? GetModeFBountyRadarLeaderSprite() : GetModeFBountyRadarRegularSprite();
                entry.icon.color = Color.white;
            }

            if (entry.countText != null)
            {
                entry.countText.fontSize = leaderStyle ? 23f : 20f;
                entry.countText.fontSizeMax = leaderStyle ? 23f : 20f;
                entry.countText.rectTransform.anchoredPosition = leaderStyle ? new Vector2(0f, -4f) : Vector2.zero;
                entry.countText.text = "x" + Mathf.Max(1, marks);
            }

            if (entry.typeText != null)
            {
                entry.typeText.text = L10n.T("首领", "LEADER");
                entry.typeText.gameObject.SetActive(leaderStyle);
            }

            if (entry.distanceRect != null)
            {
                float horizontalDistanceBias = Mathf.Abs(direction.x);
                float labelDistance = Mathf.Lerp(size * 0.82f, size * 1.25f, horizontalDistanceBias);
                entry.distanceRect.anchoredPosition = -direction * labelDistance;
                entry.distanceRect.sizeDelta = leaderStyle ? new Vector2(70f, 20f) : new Vector2(62f, 18f);
            }

            if (entry.distanceBackground != null)
            {
                entry.distanceBackground.color = ModeFBountyRadarDistancePanelColor;
            }

            if (entry.distanceText != null)
            {
                if (displayDistanceSqr < 0f)
                {
                    displayDistanceSqr = 0f;
                }
                entry.distanceText.fontSize = leaderStyle ? 15f : 14f;
                entry.distanceText.fontSizeMax = leaderStyle ? 15f : 14f;
                entry.distanceText.color = leaderStyle ? ModeFBountyRadarLeaderColor : Color.white;
                entry.distanceText.text = Mathf.RoundToInt(Mathf.Sqrt(displayDistanceSqr)) + "m";
            }

            if (!entry.root.activeSelf)
            {
                if (entry.canvasGroup != null)
                {
                    entry.canvasGroup.alpha = 0f;
                }
                entry.root.SetActive(true);
            }
        }

        private float GetModeFBountyRadarSafeRadius(float desiredRadius, float size)
        {
            if (modeFBountyRadarCenterRect == null)
            {
                return desiredRadius;
            }

            RectTransform canvasRect = modeFBountyRadarCenterRect.parent as RectTransform;
            if (canvasRect == null || canvasRect.rect.width <= 1f || canvasRect.rect.height <= 1f)
            {
                return desiredRadius;
            }

            float halfWidth = canvasRect.rect.width * 0.5f;
            float halfHeight = canvasRect.rect.height * 0.5f;
            float markerClearance = MODEF_BOUNTY_RADAR_EDGE_MARGIN + size * 0.9f;
            float maxRadius = Mathf.Max(72f, Mathf.Min(halfWidth, halfHeight) - markerClearance);
            return Mathf.Min(desiredRadius, maxRadius);
        }

        private void AnimateModeFBountyRadarEntries()
        {
            for (int i = 0; i < modeFBountyRadarEntries.Count; i++)
            {
                AnimateModeFBountyRadarEntry(modeFBountyRadarEntries[i]);
            }

            AnimateModeFBountyRadarEntry(modeFBountyLeaderRadarEntry);
        }

        private static void AnimateModeFBountyRadarEntry(ModeFBountyRadarEntryUi entry)
        {
            if (entry == null || entry.root == null || !entry.root.activeSelf)
            {
                return;
            }

            if (entry.canvasGroup != null)
            {
                entry.canvasGroup.alpha = Mathf.MoveTowards(
                    entry.canvasGroup.alpha,
                    1f,
                    Time.unscaledDeltaTime * 7f);
            }

            if (!entry.leaderStyle || entry.pulseRect == null || entry.pulseImage == null)
            {
                return;
            }

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.4f);
            float pulseScale = Mathf.Lerp(0.96f, 1.10f, pulse);
            entry.pulseRect.localScale = new Vector3(pulseScale, pulseScale, 1f);
            entry.pulseImage.color = new Color(
                ModeFBountyRadarLeaderColor.r,
                ModeFBountyRadarLeaderColor.g,
                ModeFBountyRadarLeaderColor.b,
                Mathf.Lerp(0.10f, 0.28f, pulse));
        }

        private static void AddModeFBountyRadarTextOutline(TextMeshProUGUI text, float distance)
        {
            if (text == null)
            {
                return;
            }

            Outline outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.82f);
            outline.effectDistance = new Vector2(distance, -distance);
            outline.useGraphicAlpha = true;
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

            if (modeFBountyRadarCachedMainCameraFrame != Time.frameCount || modeFBountyRadarCachedMainCamera == null)
            {
                modeFBountyRadarCachedMainCamera = Camera.main;
                modeFBountyRadarCachedMainCameraFrame = Time.frameCount;
            }

            return modeFBountyRadarCachedMainCamera;
        }

        private static void GetModeFBountyRadarBasis(Transform cameraTransform, out Vector3 radarForward, out Vector3 radarRight)
        {
            radarForward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
            radarForward.y = 0f;
            if (radarForward.sqrMagnitude <= 0.001f)
            {
                radarForward = Vector3.forward;
            }
            radarForward.Normalize();

            radarRight = cameraTransform != null ? cameraTransform.right : Vector3.right;
            radarRight.y = 0f;
            if (radarRight.sqrMagnitude <= 0.001f)
            {
                radarRight = Vector3.right;
            }
            radarRight.Normalize();
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

        private static bool IsModeFBountyRadarTargetVisible(Camera camera, Vector3 targetPos)
        {
            if (camera == null)
            {
                return false;
            }

            Vector3 viewport = camera.WorldToViewportPoint(targetPos + Vector3.up * MODEF_BOUNTY_RADAR_WORLD_HEIGHT);
            return viewport.z > 0f &&
                   viewport.x >= 0f &&
                   viewport.x <= 1f &&
                   viewport.y >= 0f &&
                   viewport.y <= 1f;
        }

        private static Vector2 GetModeFBountyRadarDirection(Vector3 playerPos, Vector3 targetPos, Vector3 radarForward, Vector3 radarRight)
        {
            Vector3 toTarget = targetPos - playerPos;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= 0.001f)
            {
                return Vector2.up;
            }

            Vector2 direction = new Vector2(
                Vector3.Dot(toTarget, radarRight),
                Vector3.Dot(toTarget, radarForward));
            float directionSqr = direction.sqrMagnitude;
            if (directionSqr <= 0.001f)
            {
                return Vector2.up;
            }

            float invDirectionMagnitude = 1f / Mathf.Sqrt(directionSqr);
            return direction * invDirectionMagnitude;
        }

        private TMP_FontAsset GetModeFBountyRadarFont()
        {
            if (modeFBountyRadarFont != null)
            {
                return modeFBountyRadarFont;
            }

            modeFBountyRadarFont = ZombieModeUIHelper.GetGameFont();
            return modeFBountyRadarFont;
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
