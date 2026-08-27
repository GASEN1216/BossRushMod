// ============================================================================
// ModeEUiAndHealthBars.cs - Mode E player name and healthbar UI
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using TMPro;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using HarmonyLib;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // ====================================================================
        // 与 ModeF 血条名牌那套的关系（读之前先看这段，别急着合并）
        // ====================================================================
        // ModeF/ModeFUI.cs + ModeFUI_BountyRadarAndHealthBars.cs 里有一组名字高度对称的
        // 方法（ResetModeFUiCaches / RegisterModeFHealthBar / FindModeFHealthBar ...），
        // 看上去像是本文件的 modeE→modeF 机械复制，但**两边行为已经分叉**，
        // 直接抽公共基类会改行为：
        //
        //   - RegisterHealthBar：本文件在换绑时多清一个 modeEHealthBarBaseTextByBarId，
        //     ModeF 没有这个字典（它不保存原始文本，用 desired/applied 两张表）。
        //   - 查找与节流：本文件用 modeECachedPlayerHealthBar 缓存玩家血条，并在
        //     FindModeEHealthBar 里按 modeENextHealthBarLookupTime 节流；
        //     ModeF 把节流放进 ScanAndCacheModeFHealthBars(force)，Find 本身不节流，
        //     而且另有一个 FindModeFPlayerHealthBar 与 FindModeFHealthBar 并存。
        //   - 失败处理：本文件走 LogModeEUiWarningLimited（按 MODEE_UI_WARNING_LOG_INTERVAL
        //     限流上报）；ModeF 用空 catch。
        //   - ModeF 还额外挂着悬赏后缀缓存与赏金雷达刷新节流，本文件没有。
        //
        // 真正逐字相同的只有三个小方法（TryGetCached*HealthBar、Mark*NamesDirty、
        // Sync*NameLanguageState），把它们抽出去的净收益接近零（省下的行数和新增的
        // helper 差不多），却要把上面那些差异一并绑进同一个抽象里。
        // 结论：**保持两套独立**。Harmony 层已经在 ModeE/ModeEHarmonyPatch.cs 合并过
        // （名字后缀 patch 统一走 ModeFUI 的 BossRushHealthBarNamePatch），C# 缓存层不再合。
        // 将来若要合并，先把上面四条差异逐条对齐并各自补 guard，再动手。

        #region Mode E UI

        private void ResetModeEUiCaches()
        {
            modeECachedPlayerName = null;
            modeENextPlayerNameRefreshTime = 0f;
            modeECachedPlayerHealthBar = null;
            modeENextHealthBarLookupTime = 0f;
            modeENextUiWarningLogTime = 0f;
            modeEHealthBarCacheByTargetId.Clear();
            modeEHealthBarBaseTextByBarId.Clear();
            modeEHealthBarDesiredTextByBarId.Clear();
            modeEHealthBarTargetIdsByBarId.Clear();
            modeEHealthBarAppliedVersionByBarId.Clear();
            modeEHealthBarNameVersion = 1;
            modeELastHealthBarLanguageIsChinese = null;
        }

        private void MarkModeEHealthBarNamesDirty()
        {
            if (modeEHealthBarNameVersion < int.MaxValue)
            {
                modeEHealthBarNameVersion++;
            }
            else
            {
                modeEHealthBarNameVersion = 1;
                modeEHealthBarAppliedVersionByBarId.Clear();
            }
        }

        private void SyncModeEHealthBarNameLanguageState()
        {
            bool isChinese = L10n.IsChinese;
            if (!modeELastHealthBarLanguageIsChinese.HasValue ||
                modeELastHealthBarLanguageIsChinese.Value != isChinese)
            {
                modeELastHealthBarLanguageIsChinese = isChinese;
                MarkModeEHealthBarNamesDirty();
            }
        }

        private void LogModeEUiWarningLimited(string message, Exception e = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (Time.unscaledTime < modeENextUiWarningLogTime)
            {
                return;
            }

            modeENextUiWarningLogTime = Time.unscaledTime + MODEE_UI_WARNING_LOG_INTERVAL;
            DevLog("[ModeE] [WARNING] " + message + (e != null ? ": " + e.Message : string.Empty));
        }

        private static MethodInfo GetModeERefreshCharacterIconMethod()
        {
            if (modeERefreshCharacterIconMethod == null)
            {
                modeERefreshCharacterIconMethod = typeof(HealthBar).GetMethod(
                    "RefreshCharacterIcon",
                    ModeEUiInstanceBindingFlags);
            }

            return modeERefreshCharacterIconMethod;
        }

        private static MethodInfo GetModeESteamFriendsGetPersonaNameMethod()
        {
            if (modeESteamFriendsGetPersonaNameMethod != null)
            {
                return modeESteamFriendsGetPersonaNameMethod;
            }

            Type steamFriendsType = AccessTools.TypeByName("Steamworks.SteamFriends");
            if (steamFriendsType != null)
            {
                modeESteamFriendsGetPersonaNameMethod = steamFriendsType.GetMethod(
                    "GetPersonaName",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }

            return modeESteamFriendsGetPersonaNameMethod;
        }

        private static MethodInfo GetModeESteamManagerGetSteamDisplayMethod()
        {
            if (modeESteamManagerGetSteamDisplayMethod != null)
            {
                return modeESteamManagerGetSteamDisplayMethod;
            }

            Type steamManagerType = AccessTools.TypeByName("SteamManager");
            if (steamManagerType != null)
            {
                modeESteamManagerGetSteamDisplayMethod = steamManagerType.GetMethod(
                    "GetSteamDisplay",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }

            return modeESteamManagerGetSteamDisplayMethod;
        }

        private string TryGetModeESteamPersonaName()
        {
            try
            {
                MethodInfo getPersonaName = GetModeESteamFriendsGetPersonaNameMethod();
                if (getPersonaName != null)
                {
                    string personaName = getPersonaName.Invoke(null, null) as string;
                    if (!string.IsNullOrEmpty(personaName))
                    {
                        return personaName;
                    }
                }
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("读取 Steam PersonaName 失败", e);
            }

            try
            {
                MethodInfo getSteamDisplay = GetModeESteamManagerGetSteamDisplayMethod();
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
            catch (Exception e)
            {
                LogModeEUiWarningLimited("读取 Steam 显示名失败", e);
            }

            return null;
        }

        internal string GetModeEPlayerName()
        {
            if (Time.unscaledTime < modeENextPlayerNameRefreshTime && !string.IsNullOrEmpty(modeECachedPlayerName))
            {
                return modeECachedPlayerName;
            }

            try
            {
                string steamName = TryGetModeESteamPersonaName();
                modeECachedPlayerName = !string.IsNullOrEmpty(steamName)
                    ? steamName
                    : L10n.T("我", "Me");
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("刷新 Mode E 玩家显示名失败，已回退默认名称", e);
                modeECachedPlayerName = L10n.T("我", "Me");
            }

            modeENextPlayerNameRefreshTime = Time.unscaledTime + MODEE_PLAYER_NAME_CACHE_INTERVAL;
            return modeECachedPlayerName;
        }

        internal string GetModeEActorDisplayName(CharacterMainControl actor, bool treatNullAsPlayer = false)
        {
            if (actor == null)
            {
                return treatNullAsPlayer ? GetModeEPlayerName() : L10n.T("未知目标", "Unknown");
            }

            try
            {
                if (actor.CharacterItem != null && !string.IsNullOrEmpty(actor.CharacterItem.DisplayName))
                {
                    return actor.CharacterItem.DisplayName;
                }
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("读取 Mode E 角色显示名失败", e);
            }

            try
            {
                if (actor.gameObject != null && !string.IsNullOrEmpty(actor.gameObject.name))
                {
                    return actor.gameObject.name;
                }
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("读取 Mode E 角色对象名失败", e);
            }

            return L10n.T("未知目标", "Unknown");
        }

        private void EnsureModeEPlayerNameTag()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null)
            {
                return;
            }

            try
            {
                player.Health.showHealthBar = true;

                HealthBar healthBar = FindModeEHealthBar(player.Health);
                if (healthBar != null)
                {
                    ForceRefreshModeEHealthBarName(healthBar);
                    return;
                }

                player.Health.RequestHealthBar();
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("刷新玩家血条名牌失败", e);
            }
        }

        private void UpdateModeEPlayerNameTag()
        {
            if (!modeEActive)
            {
                return;
            }

            if (Time.frameCount % 120 == 0)
            {
                EnsureModeEPlayerNameTag();
            }
        }

        private void CleanupModeEPlayerNameTag()
        {
            modeECachedPlayerHealthBar = null;
            modeENextHealthBarLookupTime = 0f;

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null)
            {
                return;
            }

            HealthBar healthBar = FindModeEHealthBar(player.Health);
            if (healthBar == null)
            {
                return;
            }

            ForceRefreshModeEHealthBarName(healthBar);
        }

        private HealthBar FindModeEHealthBar(Health health)
        {
            if (health == null)
            {
                return null;
            }

            if (modeECachedPlayerHealthBar != null)
            {
                if (modeECachedPlayerHealthBar.target == health)
                {
                    return modeECachedPlayerHealthBar;
                }

                modeECachedPlayerHealthBar = null;
            }

            HealthBar healthBar = null;
            if (TryGetCachedModeEHealthBar(health, out healthBar))
            {
                modeECachedPlayerHealthBar = healthBar;
                return healthBar;
            }

            if (Time.unscaledTime < modeENextHealthBarLookupTime)
            {
                return null;
            }

            modeENextHealthBarLookupTime = Time.unscaledTime + MODEE_HEALTHBAR_LOOKUP_INTERVAL;
            ScanAndCacheModeEHealthBars();
            if (TryGetCachedModeEHealthBar(health, out healthBar))
            {
                modeECachedPlayerHealthBar = healthBar;
                return healthBar;
            }

            return null;
        }

        private void ScanAndCacheModeEHealthBars()
        {
            HealthBar[] healthBars = UnityEngine.Object.FindObjectsOfType<HealthBar>();
            for (int i = 0; i < healthBars.Length; i++)
            {
                RegisterModeEHealthBar(healthBars[i]);
            }
        }

        internal void RegisterModeEHealthBar(HealthBar healthBar)
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
            if (modeEHealthBarTargetIdsByBarId.TryGetValue(barId, out previousTargetId) &&
                previousTargetId != targetId)
            {
                HealthBar previousBar = null;
                if (modeEHealthBarCacheByTargetId.TryGetValue(previousTargetId, out previousBar) &&
                    object.ReferenceEquals(previousBar, healthBar))
                {
                    modeEHealthBarCacheByTargetId.Remove(previousTargetId);
                }

                modeEHealthBarBaseTextByBarId.Remove(barId);
                modeEHealthBarDesiredTextByBarId.Remove(barId);
                modeEHealthBarAppliedVersionByBarId.Remove(barId);
            }

            modeEHealthBarCacheByTargetId[targetId] = healthBar;
            modeEHealthBarTargetIdsByBarId[barId] = targetId;
        }

        private bool TryGetCachedModeEHealthBar(Health health, out HealthBar healthBar)
        {
            healthBar = null;
            if (health == null)
            {
                return false;
            }

            int targetId = health.GetInstanceID();
            if (!modeEHealthBarCacheByTargetId.TryGetValue(targetId, out healthBar))
            {
                return false;
            }

            if (healthBar != null && healthBar.target == health)
            {
                return true;
            }

            modeEHealthBarCacheByTargetId.Remove(targetId);
            healthBar = null;
            return false;
        }

        private void ForceRefreshModeEHealthBarName(HealthBar healthBar)
        {
            if (healthBar == null)
            {
                return;
            }

            try
            {
                MethodInfo refreshCharacterIcon = GetModeERefreshCharacterIconMethod();
                if (refreshCharacterIcon != null)
                {
                    refreshCharacterIcon.Invoke(healthBar, null);
                }
            }
            catch (Exception e)
            {
                LogModeEUiWarningLimited("强制刷新玩家血条名字失败", e);
            }
        }

        #endregion

    }
}
