// ============================================================================
// F3GameplayValidationDiagnostics.cs - F3 实机验收的角色与清场诊断
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>验证 Mode D 登记对象同时可见、存活且真实敌对。</summary>
        internal int ValidationCountPlayableModeDEnemies(out string details)
        {
            int playable = 0;
            List<string> labels = new List<string>();
            for (int i = 0; i < modeDCurrentWaveEnemies.Count; i++)
            {
                CharacterMainControl enemy = modeDCurrentWaveEnemies[i];
                if (object.ReferenceEquals(enemy, null))
                {
                    labels.Add("index=" + i + ":managed_null");
                    continue;
                }

                try
                {
                    bool unityAlive = enemy != null && enemy.gameObject != null;
                    bool active = unityAlive && enemy.gameObject.activeInHierarchy;
                    bool healthAlive = unityAlive && enemy.Health != null && !enemy.Health.IsDead;
                    Teams team = unityAlive ? enemy.Team : Teams.player;
                    bool hostile = unityAlive && Team.IsEnemy(Teams.player, team);
                    if (active && healthAlive && hostile) playable++;
                    labels.Add("index=" + i + ":name="
                        + (unityAlive ? enemy.gameObject.name : "destroyed")
                        + ",active=" + active + ",health_alive=" + healthAlive
                        + ",team=" + team + ",hostile=" + hostile);
                }
                catch (Exception e)
                {
                    labels.Add("index=" + i + ":query_error=" + e.GetType().Name);
                }
            }
            details = string.Join(";", labels.ToArray());
            return playable;
        }

        /// <summary>统计即使 inactive 也不应跨用例遗留的模式角色对象。</summary>
        internal int ValidationCountModeOwnedCharacters(out string details)
        {
            int count = 0;
            List<string> labels = new List<string>();
            CharacterMainControl[] all = FindObjectsOfType<CharacterMainControl>(true);
            for (int i = 0; i < all.Length; i++)
            {
                CharacterMainControl character = all[i];
                if (character == null || character.gameObject == null) continue;
                string objectName = character.gameObject.name ?? string.Empty;
                if (!IsValidationOwnedCharacterName(objectName)) continue;

                count++;
                string preset = character.characterPreset != null
                    ? character.characterPreset.nameKey : "no_preset";
                labels.Add(objectName + "@active=" + character.gameObject.activeInHierarchy
                    + "/" + preset);
            }
            details = string.Join(";", labels.ToArray());
            return count;
        }

        /// <summary>
        /// 验收强清专用：把场上残留角色确定性清掉。
        /// 复用 F10 的 ForceKillAllEnemies（走 Health.Kill 触发死亡事件，
        /// 让各模式的死亡记账正常收尾），再补一次竞技场清场。
        /// 基地场景不做任何事——那里的 NPC 是设施，不是清场债务。
        /// </summary>
        internal void ValidationForceClearArenaEnemies()
        {
            if (string.Equals(SceneManager.GetActiveScene().name, BaseSceneName, StringComparison.Ordinal))
                return;
            try { ForceKillAllEnemies(); }
            catch (Exception e) { DevLog("[Validation] 强制清敌失败: " + e.Message); }
            try { ClearEnemiesForBossRush(); }
            catch (Exception e) { DevLog("[Validation] 竞技场清场失败: " + e.Message); }
        }

        internal bool ValidationTryGetArenaCleanState(out string metrics)
        {
            string mode;
            bool active = ValidationHasActiveMode(out mode);
            string hostileDetails;
            int enemies = ValidationCountHostileCharacters(out hostileDetails);
            string ownedDetails;
            int owned = ValidationCountModeOwnedCharacters(out ownedDetails);
            int modal = ZombieModeUIHelper.ModalInputLeaseCount;
            int bgm = BossBgmCoordinator.ActiveOwnerLeaseCount;
            int modifiers = ValidationTemporaryModifierCount;
            RandomEventDirector director = RandomEventsRuntime != null
                ? RandomEventsRuntime.Director : null;
            bool randomClear = director == null || director.ActiveEventId == RandomEventId.None;
            metrics = "active=" + active + ",mode=" + mode + ",enemies=" + enemies
                + ",owned=" + owned + ",modal=" + modal + ",bgm=" + bgm
                + ",modifiers=" + modifiers + ",random_clear=" + randomClear
                + ",hostiles=" + hostileDetails + ",owned_details=" + ownedDetails;
            return !active && enemies == 0 && owned == 0 && modal == 0 && bgm == 0
                && modifiers == 0 && randomClear;
        }

        private static bool IsValidationOwnedCharacterName(string objectName)
        {
            return objectName.StartsWith("BossRush_", StringComparison.Ordinal) ||
                objectName.StartsWith("ModeD_", StringComparison.Ordinal) ||
                objectName.StartsWith("ModeE_", StringComparison.Ordinal) ||
                objectName.StartsWith("ModeF_", StringComparison.Ordinal) ||
                objectName.StartsWith("RndEvt_", StringComparison.Ordinal) ||
                objectName.StartsWith("ZombieMode_", StringComparison.Ordinal);
        }
    }
}
