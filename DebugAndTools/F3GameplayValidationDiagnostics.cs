// ============================================================================
// F3GameplayValidationDiagnostics.cs - F3 实机验收的角色与清场诊断
// ============================================================================

using System;
using System.Collections;
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
                        + ",active_self=" + (unityAlive && enemy.gameObject.activeSelf)
                        + ",object_scene=" + (unityAlive ? enemy.gameObject.scene.name : "destroyed")
                        + ",parent_active=" + (unityAlive && (enemy.transform.parent == null
                            || enemy.transform.parent.gameObject.activeInHierarchy))
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
        /// 复用 F10 的 ForceKillAllEnemies（走 Health.Hurt 触发死亡事件，
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

        /// <summary>只读取已被标准波次登记、完成同步初始化的单 Boss。</summary>
        internal CharacterMainControl ValidationGetCommittedStandardBoss()
        {
            CharacterMainControl boss = currentBoss as CharacterMainControl;
            return IsActive && bossesPerWave == 1 && boss != null && boss.Health != null
                && !boss.Health.IsDead && boss.gameObject.activeInHierarchy ? boss : null;
        }

        /// <summary>胜利验收必须观察真实死亡，不能用 Destroy 清场冒充击杀。</summary>
        internal bool ValidationKillCommittedStandardBoss(out string reason)
        {
            reason = null;
            if (!DevModeEnabled) { reason = "dev_mode_disabled"; return false; }
            CharacterMainControl boss = ValidationGetCommittedStandardBoss();
            if (boss == null) { reason = "committed_boss_not_ready"; return false; }
            try
            {
                DamageInfo damage = new DamageInfo(CharacterMainControl.Main);
                damage.damageValue = boss.Health.MaxHealth * 10f;
                damage.ignoreArmor = true;
                boss.Health.SetInvincible(false);
                boss.Health.Hurt(damage);
                if (boss.Health.IsDead) return true;
                reason = "committed_boss_did_not_die";
            }
            catch (Exception e) { reason = "boss_kill_exception:" + e.GetType().Name; }
            return false;
        }

        /// <summary>快照当前波并逐帧击杀，让死亡订阅者的 UI/tween 有机会完成帧更新。</summary>
        internal IEnumerator ValidationDefeatModeDWave(Action<bool, string> onCompleted)
        {
            if (onCompleted == null) yield break;
            if (!DevModeEnabled || !IsModeDActive)
            {
                onCompleted(false, "mode_d_not_ready");
                yield break;
            }
            CharacterMainControl[] enemies = modeDCurrentWaveEnemies.ToArray();
            if (enemies.Length == 0)
            {
                onCompleted(false, "mode_d_wave_empty");
                yield break;
            }
            foreach (CharacterMainControl enemy in enemies)
            {
                string reason;
                if (!ValidationDefeatModeDEnemy(enemy, out reason))
                {
                    onCompleted(false, reason);
                    yield break;
                }
                // 击杀提示首个 tween 的 setter 尚未运行时，同帧第二次击杀可能读到非数字经验文本。
                yield return null;
            }
            onCompleted(true, null);
        }

        private bool ValidationDefeatModeDEnemy(CharacterMainControl enemy, out string reason)
        {
            reason = null;
            try
            {
                if (enemy == null || enemy.Health == null) { reason = "mode_d_enemy_missing"; return false; }
                if (enemy.Health.IsDead) return true;
                if (!IsModeDActive) { reason = "mode_d_not_active"; return false; }
                DamageInfo damage = new DamageInfo(CharacterMainControl.Main);
                damage.damageValue = enemy.Health.MaxHealth * 10f;
                damage.ignoreArmor = true;
                damage.toDamageReceiver = enemy.mainDamageReceiver;
                damage.damagePoint = enemy.transform.position;
                enemy.Health.SetInvincible(false);
                enemy.Health.Hurt(damage);
                if (enemy.Health.IsDead) return true;
                reason = "mode_d_enemy_did_not_die";
                return false;
            }
            catch (Exception e)
            {
                reason = "mode_d_kill_exception:" + e.GetType().Name;
                DevLog("[Validation] [ERROR] Mode D 伤害链失败: " + e);
                return false;
            }
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
