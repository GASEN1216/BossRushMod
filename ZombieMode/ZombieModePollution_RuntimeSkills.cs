// ============================================================================
// ZombieModePollution_RuntimeSkills.cs - special and elite runtime skill handling
// ============================================================================

using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void HandleZombieModeSpecialDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            if (marker == null || marker.SpecialKind != ZombieModeSpecialKind.Exploder || character == null)
            {
                return;
            }

            DealZombieModeAreaDamageToPlayer(
                runId,
                character,
                character.transform.position,
                ZombieModeTuning.ExploderDeathRadius,
                ZombieModeTuning.ExploderDeathDamage);
        }

        private void HandleZombieModeEliteDeathEffects(int runId, ZombieModeEnemyRuntimeMarker marker, CharacterMainControl character)
        {
            if (marker == null || character == null)
            {
                return;
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Burst))
            {
                DealZombieModeAreaDamageToPlayer(
                    runId,
                    character,
                    character.transform.position,
                    ZombieModeTuning.BurstAffixDeathRadius,
                    ZombieModeTuning.BurstAffixDeathDamage);
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Splitting))
            {
                int count = ZombieModeTuning.SplittingAffixSpawnCount;
                for (int i = 0; i < count; i++)
                {
                    Vector3 offset = Quaternion.Euler(0f, 360f * i / count, 0f) * Vector3.forward * 1.5f;
                    QueueZombieModeSmallSplitSpawn(runId, character.transform.position + offset);
                }
            }
        }

        private async Cysharp.Threading.Tasks.UniTask SpawnZombieModeSmallSplitAsync(int runId, Vector3 position)
        {
            CharacterMainControl zombie = await TrySpawnZombieModeNormalZombieAsync(
                runId,
                position,
                ZombieModeEnemyKind.Normal,
                true,
                () => zombieModeRunState.CombatPhase == ZombieModeCombatPhase.Combat);
            if (zombie != null)
            {
                zombie.transform.localScale = zombie.transform.localScale * 0.6f;
            }
        }

        private void EnsureZombieModeThreatRuntime(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker)
        {
            if (enemy == null || marker == null || marker.IsBoss ||
                (marker.EnemyKind != ZombieModeEnemyKind.Special && marker.EnemyKind != ZombieModeEnemyKind.Elite))
            {
                return;
            }

            ZombieModeThreatRuntime runtime = enemy.gameObject.GetComponent<ZombieModeThreatRuntime>();
            if (marker.EnemyKind == ZombieModeEnemyKind.Special &&
                marker.SpecialKind == ZombieModeSpecialKind.OfficialExploder)
            {
                if (runtime != null)
                {
                    runtime.enabled = false;
                    Destroy(runtime);
                }
                return;
            }

            if (runtime == null)
            {
                runtime = enemy.gameObject.AddComponent<ZombieModeThreatRuntime>();
            }

            float cooldown = marker.EnemyKind == ZombieModeEnemyKind.Elite
                ? ZombieModeTuning.EliteSkillCooldownSeconds
                : GetZombieModeSpecialCooldown(marker.SpecialKind);
            runtime.Initialize(marker.RunId, cooldown);

            if (marker.EnemyKind == ZombieModeEnemyKind.Elite &&
                marker.EliteAffixes.Contains(ZombieModeEliteAffix.Commander))
            {
                ZombieModeCommanderAuraRuntime commanderAura = enemy.gameObject.GetComponent<ZombieModeCommanderAuraRuntime>();
                if (commanderAura == null)
                {
                    commanderAura = enemy.gameObject.AddComponent<ZombieModeCommanderAuraRuntime>();
                }

                commanderAura.Initialize(
                    marker.RunId,
                    ZombieModeTuning.CommanderAffixAuraRadius,
                    ZombieModeTuning.CommanderAuraTickIntervalSeconds);
            }
        }

        private float GetZombieModeSpecialCooldown(ZombieModeSpecialKind kind)
        {
            switch (kind)
            {
                case ZombieModeSpecialKind.Sprinter:
                    return ZombieModeTuning.SprinterCooldownSeconds;
                case ZombieModeSpecialKind.Exploder:
                case ZombieModeSpecialKind.OfficialExploder:
                    return ZombieModeTuning.ExploderCooldownSeconds;
                case ZombieModeSpecialKind.Plague:
                    return ZombieModeTuning.PoisonCooldownSeconds;
                case ZombieModeSpecialKind.Summoner:
                    return ZombieModeTuning.SummonerCooldownSeconds;
                case ZombieModeSpecialKind.Harasser:
                    return ZombieModeTuning.HarasserCooldownSeconds;
                default:
                    return ZombieModeTuning.ExploderCooldownSeconds;
            }
        }

        internal void TryExecuteZombieModeEnemyRuntimeSkill(ZombieModeEnemyRuntimeMarker marker)
        {
            if (marker == null ||
                !IsZombieModeRunValid(marker.RunId) ||
                zombieModeRunState.CombatPhase != ZombieModeCombatPhase.Combat ||
                ZombieModePhaseGuards.ShouldPauseModePressure(zombieModeRunState.CombatPhase) ||
                marker.RemovedFromRuntime ||
                marker.DeathSettled)
            {
                return;
            }

            CharacterMainControl character = marker.Owner;
            if (character == null)
            {
                character = marker.GetComponent<CharacterMainControl>();
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (character == null || player == null)
            {
                return;
            }

            if (marker.EnemyKind == ZombieModeEnemyKind.Special)
            {
                TryExecuteZombieModeSpecialSkill(marker.RunId, character, marker, player);
            }
            else if (marker.EnemyKind == ZombieModeEnemyKind.Elite)
            {
                TryExecuteZombieModeEliteSkill(marker.RunId, character, marker, player);
            }
        }

        private void TryExecuteZombieModeSpecialSkill(
            int runId,
            CharacterMainControl character,
            ZombieModeEnemyRuntimeMarker marker,
            CharacterMainControl player)
        {
            switch (marker.SpecialKind)
            {
                case ZombieModeSpecialKind.Sprinter:
                    character.PopText(L10n.T("BossRush_ZombieMode_Special_Sprinter"));
                    Vector3 dashTarget = Vector3.MoveTowards(
                        character.transform.position,
                        player.transform.position,
                        ZombieModeTuning.SprinterDashDistance);
                    dashTarget.y = character.transform.position.y;
                    character.transform.position = dashTarget;
                    break;
                case ZombieModeSpecialKind.Exploder:
                    StartZombieModeTelegraphedAreaDamage(
                        runId,
                        character,
                        character.transform.position,
                        ZombieModeTuning.ExploderDeathRadius,
                        ZombieModeTuning.ExploderDeathDamage,
                        ZombieModeTuning.ExploderDetonationDelaySeconds,
                        L10n.T("BossRush_ZombieMode_Special_Exploder"));
                    break;
                case ZombieModeSpecialKind.OfficialExploder:
                    // 官方自爆型直接复用原版技能；这里不再叠自定义爆炸。
                    break;
                case ZombieModeSpecialKind.Plague:
                    StartZombieModeTelegraphedAreaDamage(
                        runId,
                        character,
                        character.transform.position,
                        ZombieModeTuning.PlagueCloudRadius,
                        ZombieModeTuning.PlagueCloudDamagePerSecond * ZombieModeTuning.PlagueCloudDurationSeconds,
                        ZombieModeTuning.ThreatTelegraphDelaySeconds,
                        L10n.T("BossRush_ZombieMode_Special_Plague"));
                    break;
                case ZombieModeSpecialKind.Summoner:
                    character.PopText(L10n.T("BossRush_ZombieMode_Special_Summoner"));
                    for (int i = 0; i < ZombieModeTuning.SummonerSpawnCount; i++)
                    {
                        Vector3 offset = Quaternion.Euler(0f, 360f * i / ZombieModeTuning.SummonerSpawnCount, 0f) * Vector3.forward * 1.5f;
                        QueueZombieModeSmallSplitSpawn(runId, character.transform.position + offset);
                    }
                    break;
                case ZombieModeSpecialKind.Harasser:
                    StartZombieModeTelegraphedPlayerSlow(
                        runId,
                        character,
                        player.transform.position,
                        ZombieModeTuning.HarasserSlowRadius,
                        ZombieModeTuning.HarasserSlowPercent,
                        ZombieModeTuning.HarasserSlowDurationSeconds,
                        ZombieModeTuning.ThreatTelegraphDelaySeconds,
                        L10n.T("BossRush_ZombieMode_Special_Harasser"));
                    break;
            }
        }

        private void TryExecuteZombieModeEliteSkill(
            int runId,
            CharacterMainControl character,
            ZombieModeEnemyRuntimeMarker marker,
            CharacterMainControl player)
        {
            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Commander))
            {
                character.PopText(L10n.T("BossRush_ZombieMode_Affix_Commander"));
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.ToxicAura) ||
                marker.EliteAffixes.Contains(ZombieModeEliteAffix.Plague))
            {
                StartZombieModeTelegraphedAreaDamage(
                    runId,
                    character,
                    character.transform.position,
                    5.5f,
                    26f,
                    ZombieModeTuning.ThreatTelegraphDelaySeconds,
                    L10n.T("BossRush_ZombieMode_Affix_ToxicAura"));
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Shielded))
            {
                character.PopText(L10n.T("BossRush_ZombieMode_Affix_Shielded"));
                if (character.Health != null && character.Health.CurrentHealth > 0f)
                {
                    float shieldAmount = Mathf.Max(1f, character.Health.MaxHealth * ZombieModeTuning.ShieldedAffixShieldPercent);
                    ZombieModeShieldedAffixRuntime shield = marker.ShieldedAffix;
                    if (shield == null)
                    {
                        shield = character.gameObject.AddComponent<ZombieModeShieldedAffixRuntime>();
                    }
                    shield.ActivateShield(marker.RunId, shieldAmount, ZombieModeTuning.ShieldedAffixDurationSeconds);
                    marker.ShieldedAffix = shield;
                }
            }

            if (marker.EliteAffixes.Contains(ZombieModeEliteAffix.Adaptive))
            {
                character.PopText(L10n.T("BossRush_ZombieMode_Affix_Adaptive"));
            }
        }


        // Commander Aura tick scratch HashSet：复用避免每次 0.5 秒 tick 都 new。
        // 仅在 trackedTargets != null 时使用；调用方负责调用前 Clear()。审查 §3.6。
        private readonly HashSet<int> commanderAuraTargetsScratch = new HashSet<int>();
        private readonly List<int> commanderAuraStaleTargetsScratch = new List<int>(8);

        internal void RefreshZombieModeCommanderAuraTargets(
            int runId,
            CharacterMainControl commander,
            float radius,
            Dictionary<int, ZombieModeCommanderAuraTargetRuntime> trackedTargets)
        {
            if (!IsZombieModeRunValid(runId) || commander == null)
            {
                return;
            }

            GameObject commanderObject = commander.gameObject;
            if (commanderObject == null)
            {
                return;
            }

            float radiusSqr = radius * radius;
            Vector3 commanderPosition = commander.transform.position;
            int sourceId = commanderObject.GetInstanceID();
            HashSet<int> currentTargets = null;
            if (trackedTargets != null)
            {
                commanderAuraTargetsScratch.Clear();
                currentTargets = commanderAuraTargetsScratch;
            }
            for (int i = 0; i < zombieModeRunState.RunOnlyObjects.Count; i++)
            {
                ZombieModeRunOnlyRecord record = zombieModeRunState.RunOnlyObjects[i];
                if (record == null ||
                    record.Kind != ZombieModeRunOnlyObjectKind.Enemy)
                {
                    continue;
                }

                GameObject recordObject = record.GameObject;
                if (recordObject == null ||
                    recordObject == commanderObject)
                {
                    continue;
                }

                ZombieModeEnemyRuntimeMarker target = record.Target as ZombieModeEnemyRuntimeMarker;
                if (target == null)
                {
                    target = recordObject.GetComponent<ZombieModeEnemyRuntimeMarker>();
                    if (target != null)
                    {
                        record.Target = target;
                    }
                }

                if (target == null ||
                    target.RunId != runId ||
                    target.IsBoss ||
                    target.EnemyKind != ZombieModeEnemyKind.Normal ||
                    target.RemovedFromRuntime ||
                    target.DeathSettled)
                {
                    continue;
                }

                Vector3 delta = target.transform.position - commanderPosition;
                delta.y = 0f;
                if (delta.sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                CharacterMainControl targetCharacter = target.Owner;
                if (targetCharacter == null)
                {
                    targetCharacter = target.GetComponent<CharacterMainControl>();
                }

                if (targetCharacter == null ||
                    targetCharacter.Health == null ||
                    targetCharacter.Health.CurrentHealth <= 0f)
                {
                    continue;
                }

                GameObject targetObject = targetCharacter.gameObject;
                int targetId = targetObject.GetInstanceID();
                if (currentTargets != null)
                {
                    currentTargets.Add(targetId);
                }

                ZombieModeCommanderAuraTargetRuntime targetRuntime = target.CommanderAuraTargetRuntime;
                if (targetRuntime == null)
                {
                    targetRuntime = targetObject.GetComponent<ZombieModeCommanderAuraTargetRuntime>();
                    if (targetRuntime == null)
                    {
                        targetRuntime = targetObject.AddComponent<ZombieModeCommanderAuraTargetRuntime>();
                    }
                    target.CommanderAuraTargetRuntime = targetRuntime;
                }

                targetRuntime.ApplySource(runId, sourceId);
                if (trackedTargets != null)
                {
                    trackedTargets[targetId] = targetRuntime;
                }
            }

            if (trackedTargets == null)
            {
                return;
            }

            commanderAuraStaleTargetsScratch.Clear();
            foreach (KeyValuePair<int, ZombieModeCommanderAuraTargetRuntime> entry in trackedTargets)
            {
                if (currentTargets.Contains(entry.Key))
                {
                    continue;
                }

                if (entry.Value != null)
                {
                    entry.Value.RemoveSource(sourceId);
                }

                commanderAuraStaleTargetsScratch.Add(entry.Key);
            }

            if (commanderAuraStaleTargetsScratch.Count == 0)
            {
                return;
            }

            for (int i = 0; i < commanderAuraStaleTargetsScratch.Count; i++)
            {
                trackedTargets.Remove(commanderAuraStaleTargetsScratch[i]);
            }

            commanderAuraStaleTargetsScratch.Clear();
        }

        private void StartZombieModeTelegraphedAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage,
            float delay,
            string label)
        {
            if (!IsZombieModeRunValid(runId) || ZombieModePhaseGuards.ShouldPauseModePressure(zombieModeRunState.CombatPhase))
            {
                return;
            }

            if (source != null && !string.IsNullOrEmpty(label))
            {
                source.PopText(label);
            }

            // 共享 disk mesh 替代 CreatePrimitive(Cylinder)（审查 §3.3）。
            GameObject telegraph = CreateZombieModeFlatZoneVisual(
                "ZombieMode_Telegraph",
                origin + Vector3.up * 0.03f,
                radius,
                0.02f,
                new Color(1f, 0.16f, 0.08f, 0.35f));

            ZombieModeTelegraphedAreaDamageRuntime runtime = telegraph.AddComponent<ZombieModeTelegraphedAreaDamageRuntime>();
            runtime.Initialize(runId, source, origin, radius, damage, delay);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, telegraph, runtime, null);
        }

        private void StartZombieModeTelegraphedPlayerSlow(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float slowPercent,
            float slowDuration,
            float delay,
            string label)
        {
            if (!IsZombieModeRunValid(runId) || ZombieModePhaseGuards.ShouldPauseModePressure(zombieModeRunState.CombatPhase))
            {
                return;
            }

            if (source != null && !string.IsNullOrEmpty(label))
            {
                source.PopText(label);
            }

            GameObject telegraph = CreateZombieModeFlatZoneVisual(
                "ZombieMode_SlowTelegraph",
                origin + Vector3.up * 0.03f,
                radius,
                0.02f,
                new Color(0.12f, 0.75f, 1f, 0.35f));

            ZombieModeTelegraphedPlayerSlowRuntime runtime = telegraph.AddComponent<ZombieModeTelegraphedPlayerSlowRuntime>();
            runtime.Initialize(runId, origin, radius, slowPercent, slowDuration, delay);
            RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Projectile, telegraph, runtime, null);
        }

        public void TryExecuteZombieModeTelegraphedAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage)
        {
            if (IsZombieModeRunValid(runId) &&
                !ZombieModePhaseGuards.ShouldPauseModePressure(zombieModeRunState.CombatPhase))
            {
                // 起手 telegraph 完成时走 ExplosionManager 路径（审查 §4.2）：
                // 玩家与丧尸都按 team 命中，自动尊重墙体阻挡 / VFX / 屏幕震动；
                // source 为空时 helper 内部回退为 player-only 实现保持原行为。
                DealZombieModeExplosionAreaDamage(runId, source, origin, radius, damage);
            }
        }

        public void TryApplyZombieModePlayerSlowInArea(int runId, Vector3 origin, float radius, float percent, float duration)
        {
            if (!IsZombieModeRunValid(runId) || ZombieModePhaseGuards.ShouldPauseModePressure(zombieModeRunState.CombatPhase))
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }

            Vector3 delta = player.transform.position - origin;
            if (delta.sqrMagnitude > radius * radius)
            {
                return;
            }

            TryApplyZombieModePlayerSlow(runId, percent, duration);
        }

        private void DealZombieModeAreaDamageToPlayer(int runId, Vector3 origin, float radius, float damage)
        {
            DealZombieModeAreaDamageToPlayer(runId, null, origin, radius, damage);
        }

        private void DealZombieModeAreaDamageToPlayer(int runId, CharacterMainControl source, Vector3 origin, float radius, float damage)
        {
            if (!IsZombieModeRunValid(runId) || IsZombieModeRuntimePaused())
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.mainDamageReceiver == null)
            {
                return;
            }

            Vector3 delta = player.transform.position - origin;
            if (delta.sqrMagnitude > radius * radius)
            {
                return;
            }

            CharacterMainControl damageSource = source != null ? source : player;
            DamageInfo damageInfo = new DamageInfo(damageSource);
            damageInfo.damageType = DamageTypes.normal;
            damageInfo.damageValue = damage;
            damageInfo.damagePoint = player.transform.position;
            damageInfo.damageNormal = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.up;
            damageInfo.isFromBuffOrEffect = source == null;

            DamageReceiver receiver = player.mainDamageReceiver;
            receiver.Hurt(damageInfo);
        }

        // ====================================================================
        // ExplosionManager.CreateExplosion 接入（审查 §4.2）
        // ====================================================================
        // DealZombieModeAreaDamageToPlayer 是 mod 自实现的"只对主角伤害"路径，
        // 跳过墙体 raycast，无 VFX，无屏幕震动。当 telegraph 起手或 Boss 死亡爆炸
        // 想要原生效果（屏幕震动 / 标准爆炸 VFX / 障碍物挡墙）时，调用此方法走源码。
        //
        // CreateExplosion 按 team 命中所有敌对单位 — Boss source.Team 为 wolf/scav，
        // wolf 不与 scav 互伤、不与自己互伤；玩家是 player，会被命中。
        // 兜底：源码 API 不可用时退回 player-only 实现。
        // ====================================================================
        public void DealZombieModeExplosionAreaDamage(
            int runId,
            CharacterMainControl source,
            Vector3 origin,
            float radius,
            float damage,
            bool canHurtSelf = false)
        {
            if (!IsZombieModeRunValid(runId) || IsZombieModeRuntimePaused())
            {
                return;
            }

            try
            {
                if (source != null &&
                    LevelManager.Instance != null &&
                    LevelManager.Instance.ExplosionManager != null)
                {
                    DamageInfo dmgInfo = new DamageInfo(source);
                    dmgInfo.damageValue = damage;
                    dmgInfo.isExplosion = true;

                    LevelManager.Instance.ExplosionManager.CreateExplosion(
                        origin,
                        radius,
                        dmgInfo,
                        ExplosionFxTypes.normal,
                        0.5f,
                        canHurtSelf);
                    return;
                }
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] ExplosionManager 调用失败，回退 player-only 路径: " + e.Message);
            }

            // 兜底：源码 API 不可用 / source 为空时仍走 player-only 实现，
            // 与原行为一致避免技能完全失效。
            DealZombieModeAreaDamageToPlayer(runId, source, origin, radius, damage);
        }
    }
}
