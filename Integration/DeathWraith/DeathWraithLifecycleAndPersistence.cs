// ============================================================================
// DeathWraithSystem partial - extracted from DeathWraithSystem.cs
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using Duckov;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using Saves;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 亡魂系统 — 击杀处理

        /// <summary>
        /// 亡魂被击杀时的处理
        /// </summary>
        private void OnWraithDied_DeathWraith(Health deadHealth, DamageInfo damageInfo)
        {
            try
            {
                if (deadHealth == null)
                {
                    return;
                }

                var character = deadHealth.TryGetCharacter();
                if (character == null)
                {
                    return;
                }

                uint raidID;
                if (!activeWraithRaidIdByCharacter.TryGetValue(character, out raidID))
                {
                    return;
                }

                ForgetActiveWraith_DeathWraith(character);
                DestroyOwnedWraithPresetClone_DeathWraith(character);
                DevLog("[DeathWraith] 亡魂已被击杀: raidID=" + raidID);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] OnWraithDied 异常: " + e.Message);
            }
        }

        #endregion

        #region 亡魂系统 — 场景清理

        /// <summary>
        /// 场景切换时清理亡魂状态
        /// </summary>
        private void ClearDeathWraithState_DeathWraith()
        {
            ClearPendingDeathWraithInfo_DeathWraith();
            pendingDeadBodySpawnContexts.Clear();
            spawningWraithRaidIds.Clear();

            List<CharacterMainControl> activeWraiths =
                new List<CharacterMainControl>(activeWraithsByRaidId.Values);
            activeWraithsByRaidId.Clear();
            activeWraithRaidIdByCharacter.Clear();

            for (int i = 0; i < activeWraiths.Count; i++)
            {
                CharacterMainControl wraith = activeWraiths[i];
                if (wraith != null)
                {
                    DestroyWraithInstance_DeathWraith(wraith, "场景清理");
                }
            }
        }

        #endregion

        #region 亡魂系统 — 等级分类与属性

        /// <summary>
        /// 根据掉落物价值与玩家总财产比例分类亡魂等级
        /// </summary>
        private static WraithTier ClassifyWraithTier_DeathWraith(int droppedValue, long totalWealth)
        {
            if (totalWealth <= 0) return WraithTier.Weak;
            float ratio = (float)droppedValue / totalWealth;
            if (ratio >= 0.5f) return WraithTier.Strong;
            if (ratio >= 0.1f) return WraithTier.Balanced;
            return WraithTier.Weak;
        }

        /// <summary>
        /// 生成亡魂显示名
        /// </summary>
        private static string GetWraithDisplayName_DeathWraith(string playerName, WraithTier tier)
        {
            if (string.IsNullOrEmpty(playerName))
            {
                playerName = "???";
            }

            string prefix;
            switch (tier)
            {
                case WraithTier.Strong:
                    prefix = L10n.T("强壮的", "Strong ");
                    break;
                case WraithTier.Balanced:
                    prefix = L10n.T("均衡的", "Balanced ");
                    break;
                default:
                    prefix = L10n.T("弱小的", "Weak ");
                    break;
            }

            string suffix = L10n.T("的亡魂", "'s Wraith");
            return prefix + playerName + suffix;
        }

        private string CreateWraithDisplayNameKey_DeathWraith(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return null;
            }

            try
            {
                string key = DEATH_WRAITH_NAME_KEY_PREFIX
                    + Guid.NewGuid().ToString("N").Substring(0, 8);
                if (LocalizationHelper.InjectLocalization(key, displayName))
                {
                    return key;
                }

                DevLog("[DeathWraith] [WARNING] 注入亡魂名字本地化失败: " + displayName);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] CreateWraithDisplayNameKey 异常: " + e.Message);
            }

            return null;
        }

        /// <summary>
        /// 根据等级应用属性加成（参考 Utilities.cs ApplyBossStatMultiplier 模式）
        /// </summary>
        private static float GetWraithMoveabilityTarget_DeathWraith(WraithTier tier)
        {
            switch (tier)
            {
                case WraithTier.Strong:
                    return 1f;
                case WraithTier.Balanced:
                    return 0.9f;
                default:
                    return 0.8f;
            }
        }

        private void ApplyWraithTierStats_DeathWraith(CharacterMainControl wraith, WraithTier tier)
        {
            if (wraith == null) return;

            try
            {
                var item = wraith.CharacterItem;
                if (item == null) return;

                float speedMult = tier == WraithTier.Strong ? 1.9f :
                    (tier == WraithTier.Balanced ? 1.5f : 1.2f);
                float dmgMult = tier == WraithTier.Strong ? 1.5f :
                    (tier == WraithTier.Balanced ? 1.25f : 1f);
                float hpMult = tier == WraithTier.Strong ? 10f :
                    (tier == WraithTier.Balanced ? 6f : 3f);

                try
                {
                    Stat hpStat = item.GetStat("MaxHealth".GetHashCode());
                    if (hpStat != null)
                    {
                        float old = hpStat.BaseValue;
                        hpStat.BaseValue *= hpMult;
                        DevLog("[DeathWraith] MaxHealth: " + old + " -> " + hpStat.BaseValue);
                    }
                }
                catch { }

                // 移速加成：龙裔二阶段会改 MoveSpeed + Moveability。
                // 亡魂额外同步 WalkSpeed/RunSpeed，确保原版移动控制真实生效。
                try
                {
                    Stat moveStat = item.GetStat("MoveSpeed".GetHashCode());
                    if (moveStat != null)
                    {
                        float old = moveStat.BaseValue;
                        moveStat.BaseValue *= speedMult;
                        DevLog("[DeathWraith] MoveSpeed: " + old + " -> " + moveStat.BaseValue);
                    }
                }
                catch { }

                try
                {
                    Stat walkStat = item.GetStat("WalkSpeed".GetHashCode());
                    if (walkStat != null)
                    {
                        float old = walkStat.BaseValue;
                        walkStat.BaseValue *= speedMult;
                        DevLog("[DeathWraith] WalkSpeed: " + old + " -> " + walkStat.BaseValue);
                    }
                }
                catch { }

                try
                {
                    Stat runStat = item.GetStat("RunSpeed".GetHashCode());
                    if (runStat != null)
                    {
                        float old = runStat.BaseValue;
                        runStat.BaseValue *= speedMult;
                        DevLog("[DeathWraith] RunSpeed: " + old + " -> " + runStat.BaseValue);
                    }
                }
                catch { }

                try
                {
                    Stat moveabilityStat = item.GetStat("Moveability".GetHashCode());
                    if (moveabilityStat != null)
                    {
                        float old = moveabilityStat.BaseValue;
                        moveabilityStat.BaseValue = GetWraithMoveabilityTarget_DeathWraith(tier);
                        DevLog("[DeathWraith] Moveability: " + old + " -> " + moveabilityStat.BaseValue);
                    }
                }
                catch { }

                // 攻击加成
                try
                {
                    Stat gunDmg = item.GetStat("GunDamageMultiplier".GetHashCode());
                    if (gunDmg != null)
                    {
                        float old = gunDmg.BaseValue;
                        gunDmg.BaseValue *= dmgMult;
                        DevLog("[DeathWraith] GunDamageMultiplier: " + old + " -> " + gunDmg.BaseValue);
                    }
                }
                catch { }

                try
                {
                    Stat meleeDmg = item.GetStat("MeleeDamageMultiplier".GetHashCode());
                    if (meleeDmg != null)
                    {
                        float old = meleeDmg.BaseValue;
                        meleeDmg.BaseValue *= dmgMult;
                        DevLog("[DeathWraith] MeleeDamageMultiplier: " + old + " -> " + meleeDmg.BaseValue);
                    }
                }
                catch { }

                try
                {
                    DevLog("[DeathWraith] 当前角色移速快照: walk=" + wraith.CharacterWalkSpeed
                        + ", run=" + wraith.CharacterRunSpeed);
                }
                catch { }

                DevLog("[DeathWraith] 属性加成完成: tier=" + tier
                    + " hpMult=" + hpMult
                    + " speedMult=" + speedMult
                    + " dmgMult=" + dmgMult);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] ApplyWraithTierStats 异常: " + e.Message);
            }
        }

        private void RestoreWraithMaxHealthSnapshot_DeathWraith(CharacterMainControl wraith, float savedMaxHealth)
        {
            if (wraith == null || savedMaxHealth <= 0f)
            {
                return;
            }

            try
            {
                if (wraith.Health == null)
                {
                    return;
                }

                float currentMaxHealth = wraith.Health.MaxHealth;
                if (currentMaxHealth <= 0.01f)
                {
                    return;
                }

                Item item = wraith.CharacterItem;
                if (item == null)
                {
                    return;
                }

                Stat hpStat = item.GetStat("MaxHealth".GetHashCode());
                if (hpStat == null)
                {
                    DevLog("[DeathWraith] [WARNING] 无法回填最大生命：缺少 MaxHealth Stat");
                    return;
                }

                float scale = savedMaxHealth / currentMaxHealth;
                if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
                {
                    DevLog("[DeathWraith] [WARNING] 无法回填最大生命：非法缩放值 " + scale);
                    return;
                }

                float oldBase = hpStat.BaseValue;
                hpStat.BaseValue *= scale;
                DevLog("[DeathWraith] 回填最大生命: current=" + currentMaxHealth
                    + " saved=" + savedMaxHealth
                    + " base=" + oldBase + " -> " + hpStat.BaseValue);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] RestoreWraithMaxHealthSnapshot 异常: " + e.Message);
            }
        }

        #endregion

        #region 亡魂系统 — 玩家名获取

        /// <summary>
        /// 获取玩家显示名（优先 Steam 人格名，回退默认名）
        /// </summary>
        private string GetWraithPlayerName_DeathWraith()
        {
            try
            {
                string steamName = TryGetSteamPersonaName();
                if (!string.IsNullOrEmpty(steamName))
                {
                    return steamName;
                }
            }
            catch { }

            return L10n.T("我", "Me");
        }

        private string GetActiveSceneName_DeathWraith()
        {
            try
            {
                Scene activeScene = SceneManager.GetActiveScene();
                return activeScene.name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetActiveSubSceneId_DeathWraith()
        {
            try
            {
                return MultiSceneCore.ActiveSubSceneID ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private uint GetCurrentRaidId_DeathWraith()
        {
            try
            {
                return RaidUtilities.CurrentRaid.ID;
            }
            catch
            {
                return 0U;
            }
        }

        private List<WraithInfo> LoadStoredDeathWraithInfos_DeathWraith()
        {
            // [性能优化] 返回内存中的权威副本；仅首次访问时从 ES3 反序列化。
            // 之前每次 Load 都同步反序列化整张含完整物品树的列表，死亡帧调用一次即卡。
            if (_deathWraithListCache != null)
            {
                return _deathWraithListCache;
            }

            try
            {
                List<WraithInfo> infos =
                    SavesSystem.Load<List<WraithInfo>>(DEATH_WRAITH_LIST_SAVE_KEY);
                _deathWraithListCache = infos ?? new List<WraithInfo>();
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 读取亡魂记录列表失败: " + e.Message);
                _deathWraithListCache = new List<WraithInfo>();
            }

            return _deathWraithListCache;
        }

        private void SaveStoredDeathWraithInfos_DeathWraith(List<WraithInfo> infos)
        {
            // [性能优化] 不在此处同步写 ES3。把列表认作权威内存副本并标脏，
            // 真正的序列化延后到官方存档点（OnCollectSaveData）或去抖 tick。
            // 这样死亡帧的 Append 不再触发 Load+Save 全表序列化。
            _deathWraithListCache = infos ?? new List<WraithInfo>();
            MarkDeathWraithListDirty_DeathWraith();
        }

        private void MarkDeathWraithListDirty_DeathWraith()
        {
            if (!_deathWraithListDirty)
            {
                _deathWraithListDirty = true;
                _deathWraithListDirtySince = Time.realtimeSinceStartup;
            }
        }

        /// <summary>
        /// 把内存中的亡魂列表真正写入 ES3 缓存（仅在有改动时）。
        /// 由游戏官方存档收集点 OnCollectSaveData 调用（撤离/切场景/退出都会触发），
        /// 因此死亡帧本身不需要做这次序列化。
        /// </summary>
        private void FlushDeathWraithListIfDirty_DeathWraith()
        {
            if (!_deathWraithListDirty)
            {
                return;
            }

            try
            {
                SavesSystem.Save<List<WraithInfo>>(
                    DEATH_WRAITH_LIST_SAVE_KEY,
                    _deathWraithListCache ?? new List<WraithInfo>());
                _deathWraithListDirty = false;
                _deathWraithListDirtySince = -1f;
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 保存亡魂记录列表失败: " + e.Message);
            }
        }

        /// <summary>
        /// 去抖兜底：变脏后超过 DEATH_WRAITH_SAVE_DELAY 仍未被官方存档点刷写时，主动写一次。
        /// 由 TickAlwaysOnRuntime 每帧调用（极轻量：未变脏时直接返回）。
        /// </summary>
        internal void UpdateDeferredDeathWraithSave_DeathWraith()
        {
            if (!_deathWraithListDirty)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _deathWraithListDirtySince >= DEATH_WRAITH_SAVE_DELAY)
            {
                FlushDeathWraithListIfDirty_DeathWraith();
            }
        }

        private int GetStoredDeathWraithLimit_DeathWraith()
        {
            try
            {
                return Mathf.Max(1, Duckov.Rules.GameRulesManager.Current.SaveDeadbodyCount);
            }
            catch
            {
                return 4;
            }
        }

        private void AppendStoredDeathWraithInfo_DeathWraith(WraithInfo info)
        {
            if (info == null)
            {
                return;
            }

            List<WraithInfo> infos = LoadStoredDeathWraithInfos_DeathWraith();
            for (int i = infos.Count - 1; i >= 0; i--)
            {
                WraithInfo existing = infos[i];
                if (existing != null && existing.raidID == info.raidID)
                {
                    infos.RemoveAt(i);
                }
            }

            infos.Add(info);

            int limit = GetStoredDeathWraithLimit_DeathWraith();
            while (infos.Count > limit)
            {
                WraithInfo removed = infos[0];
                if (removed != null)
                {
                    DestroyActiveWraithByRaidId_DeathWraith(removed.raidID, "超出原版遗失物记录上限");
                }
                infos.RemoveAt(0);
            }

            SaveStoredDeathWraithInfos_DeathWraith(infos);
        }

        private WraithInfo FindStoredDeathWraithInfoByRaidId_DeathWraith(uint raidID)
        {
            List<WraithInfo> infos = LoadStoredDeathWraithInfos_DeathWraith();
            for (int i = 0; i < infos.Count; i++)
            {
                WraithInfo info = infos[i];
                if (info != null && info.raidID == raidID && info.valid)
                {
                    return info;
                }
            }

            return null;
        }

        private bool RemoveStoredDeathWraithInfoByRaidId_DeathWraith(uint raidID, string reason)
        {
            bool removed = false;
            List<WraithInfo> infos = LoadStoredDeathWraithInfos_DeathWraith();
            for (int i = infos.Count - 1; i >= 0; i--)
            {
                WraithInfo info = infos[i];
                if (info != null && info.raidID == raidID)
                {
                    infos.RemoveAt(i);
                    removed = true;
                }
            }

            if (removed)
            {
                SaveStoredDeathWraithInfos_DeathWraith(infos);
                DevLog("[DeathWraith] 已移除亡魂记录: raidID=" + raidID
                    + (string.IsNullOrEmpty(reason) ? string.Empty : (", reason=" + reason)));
            }

            return removed;
        }

        private void InvalidateStoredDeathWraithRecords_DeathWraith(string reason)
        {
            SaveStoredDeathWraithInfos_DeathWraith(new List<WraithInfo>());
            DevLog("[DeathWraith] 已清空全部亡魂记录: " + reason);
        }

        private bool IsDeathWraithCharacter_DeathWraith(CharacterMainControl character)
        {
            if (character == null)
            {
                return false;
            }

            try
            {
                if (activeWraithRaidIdByCharacter.ContainsKey(character))
                {
                    return true;
                }
            }
            catch { }

            try
            {
                GameObject go = character.gameObject;
                if (go != null &&
                    !string.IsNullOrEmpty(go.name) &&
                    go.name.StartsWith("BossRush_DeathWraith_", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch { }

            try
            {
                CharacterRandomPreset preset = character.characterPreset;
                if (preset != null)
                {
                    string presetName = preset.name;
                    if (!string.IsNullOrEmpty(presetName) &&
                        presetName.StartsWith("BossRush_DeathWraithPreset", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    string nameKey = preset.nameKey;
                    if (!string.IsNullOrEmpty(nameKey) &&
                        nameKey.StartsWith(DEATH_WRAITH_NAME_KEY_PREFIX, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private void RegisterActiveWraith_DeathWraith(uint raidID, CharacterMainControl wraith)
        {
            if (wraith == null)
            {
                return;
            }

            activeWraithsByRaidId[raidID] = wraith;
            activeWraithRaidIdByCharacter[wraith] = raidID;
        }

        private void ForgetActiveWraith_DeathWraith(CharacterMainControl wraith)
        {
            if (wraith == null)
            {
                return;
            }

            uint raidID;
            if (activeWraithRaidIdByCharacter.TryGetValue(wraith, out raidID))
            {
                activeWraithRaidIdByCharacter.Remove(wraith);

                CharacterMainControl existing;
                if (activeWraithsByRaidId.TryGetValue(raidID, out existing) && existing == wraith)
                {
                    activeWraithsByRaidId.Remove(raidID);
                }
            }
        }

        private void DestroyActiveWraithByRaidId_DeathWraith(uint raidID, string reason)
        {
            CharacterMainControl wraith;
            if (!activeWraithsByRaidId.TryGetValue(raidID, out wraith) || wraith == null)
            {
                return;
            }

            ForgetActiveWraith_DeathWraith(wraith);
            DestroyWraithInstance_DeathWraith(wraith, reason);
        }

        private void EnsureWraithPresetCache_DeathWraith()
        {
            if (deathWraithPresetCacheByNameKey != null &&
                deathWraithPresetCacheByRuntimeName != null &&
                (deathWraithPresetCacheByNameKey.Count > 0 || deathWraithPresetCacheByRuntimeName.Count > 0))
            {
                return;
            }

            try
            {
                var allPresets = ObjectCache.GetCharacterPresets();
                if (allPresets == null || allPresets.Length == 0)
                {
                    DevLog("[DeathWraith] 未找到任何 CharacterRandomPreset");
                    return;
                }

                deathWraithPresetCacheByNameKey =
                    new Dictionary<string, CharacterRandomPreset>(StringComparer.Ordinal);
                deathWraithPresetCacheByRuntimeName =
                    new Dictionary<string, CharacterRandomPreset>(StringComparer.Ordinal);
                foreach (CharacterRandomPreset preset in allPresets)
                {
                    if (preset == null || IsRuntimeCharacterPresetClone(preset))
                    {
                        continue;
                    }

                    string nameKey = preset.nameKey;
                    if (!string.IsNullOrEmpty(nameKey) &&
                        !deathWraithPresetCacheByNameKey.ContainsKey(nameKey))
                    {
                        deathWraithPresetCacheByNameKey[nameKey] = preset;
                    }

                    string runtimeName = preset.name;
                    if (!string.IsNullOrEmpty(runtimeName) &&
                        !deathWraithPresetCacheByRuntimeName.ContainsKey(runtimeName))
                    {
                        deathWraithPresetCacheByRuntimeName[runtimeName] = preset;
                    }
                }

                DevLog("[DeathWraith] 已初始化亡魂预设缓存: nameKey="
                    + deathWraithPresetCacheByNameKey.Count
                    + ", runtimeName=" + deathWraithPresetCacheByRuntimeName.Count);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 初始化角色预设缓存异常: " + e.Message);
            }
        }

        private bool TryGetWraithPresetByNameKey_DeathWraith(
            string nameKey,
            out CharacterRandomPreset preset)
        {
            preset = null;
            return !string.IsNullOrEmpty(nameKey) &&
                deathWraithPresetCacheByNameKey != null &&
                deathWraithPresetCacheByNameKey.TryGetValue(nameKey, out preset) &&
                preset != null;
        }

        private bool TryGetWraithPresetByRuntimeName_DeathWraith(
            string runtimeName,
            out CharacterRandomPreset preset)
        {
            preset = null;
            if (deathWraithPresetCacheByRuntimeName == null)
            {
                return false;
            }

            string normalizedRuntimeName = NormalizeWraithRuntimePresetName_DeathWraith(runtimeName);
            return !string.IsNullOrEmpty(normalizedRuntimeName) &&
                deathWraithPresetCacheByRuntimeName.TryGetValue(normalizedRuntimeName, out preset) &&
                preset != null;
        }


        private string NormalizeWraithRuntimePresetName_DeathWraith(string runtimeName)
        {
            if (string.IsNullOrEmpty(runtimeName))
            {
                return string.Empty;
            }

            string normalized = runtimeName.Trim();
            int cloneIndex = normalized.IndexOf("(Clone)", StringComparison.Ordinal);
            if (cloneIndex >= 0)
            {
                normalized = normalized.Substring(0, cloneIndex).TrimEnd();
            }

            return normalized;
        }


        private void DestroyOwnedWraithPresetClone_DeathWraith(CharacterMainControl wraith)
        {
            if (wraith == null)
            {
                return;
            }

            try
            {
                CharacterRandomPreset preset = wraith.characterPreset;
                if (preset != null && IsRuntimeCharacterPresetClone(preset))
                {
                    UnityEngine.Object.Destroy(preset);
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 销毁亡魂预设副本异常: " + e.Message);
            }
        }

        private void DestroyWraithInstance_DeathWraith(CharacterMainControl wraith, string reason)
        {
            if (wraith == null)
            {
                return;
            }

            try
            {
                wraith.dropBoxOnDead = false;
            }
            catch { }

            // 销毁克隆的 characterPreset，防止 ScriptableObject 泄漏
            DestroyOwnedWraithPresetClone_DeathWraith(wraith);

            try
            {
                if (wraith.gameObject != null)
                {
                    UnityEngine.Object.Destroy(wraith.gameObject);
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(reason))
            {
                DevLog("[DeathWraith] 销毁亡魂实例: " + reason);
            }
        }

        #endregion
    }
}
