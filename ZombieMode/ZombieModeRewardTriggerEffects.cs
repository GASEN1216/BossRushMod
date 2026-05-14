// ============================================================================
// ZombieModeRewardTriggerEffects.cs - 丧尸模式触发与支援弹体效果
// ============================================================================

using System.Collections;
using Duckov.Buffs;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.Events;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private void HandleZombieModeOptionHealthHurt(
            int runId,
            Health health,
            DamageInfo damageInfo,
            CharacterMainControl victim,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (!IsZombieModeRunValid(runId) ||
                health == null ||
                victim == null ||
                marker == null ||
                marker.RunId != zombieModeRunState.RunId ||
                marker.DeathSettled ||
                marker.RemovedFromRuntime ||
                damageInfo.fromCharacter != CharacterMainControl.Main ||
                damageInfo.isFromBuffOrEffect ||
                !IsZombieModeKnownEnemy(victim))
            {
                return;
            }

            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (options.TriggerLifestealChancePercent > 0)
            {
                CharacterMainControl player = CharacterMainControl.Main;
                int chance = Mathf.Min(ZombieModeLifestealChanceCapPercent, options.TriggerLifestealChancePercent);
                if (player != null &&
                    player.Health != null &&
                    UnityEngine.Random.value <= chance / 100f)
                {
                    player.Health.AddHealth(Mathf.Max(1, options.TriggerLifestealHealAmount));
                }
            }

            if (options.TriggerCritBurstStacks > 0 && damageInfo.crit > 0)
            {
                float now = GetZombieModeRuntimeNow();
                if (now - options.LastCritBurstTriggerTime >= 0.15f)
                {
                    int stacks = Mathf.Min(3, options.TriggerCritBurstStacks);
                    options.LastCritBurstTriggerTime = now;
                    CreateZombieModeOptionExplosion(
                        runId,
                        victim.transform.position,
                        1.5f + 0.25f * (stacks - 1),
                        Mathf.Max(1f, damageInfo.finalDamage * (0.30f + 0.10f * (stacks - 1))));
                }
            }

            if (options.ProjectileStasisStacks > 0 && IsZombieModePlayerProjectileDamage(damageInfo))
            {
                int stacks = Mathf.Min(2, options.ProjectileStasisStacks);
                TryApplyZombieModeEnemyStasis(victim, marker, 0.65f + 0.15f * (stacks - 1), 1.0f + 0.4f * (stacks - 1));
            }

            if (IsZombieModePlayerProjectileDamage(damageInfo))
            {
                float now = GetZombieModeRuntimeNow();
                if (now - options.LastTrajectorySupportTriggerTime >= 0.08f)
                {
                    bool spawnedSupportProjectile = false;
                    if (options.ProjectileRicochetStacks > 0)
                    {
                        CharacterMainControl nearest = TryFindZombieModeNearestEnemyTarget(runId, victim, 9f);
                        if (nearest != null)
                        {
                            Vector3 direction = (nearest.transform.position - victim.transform.position).normalized;
                            spawnedSupportProjectile |= TrySpawnZombieModePlayerSupportProjectile(victim.transform.position + Vector3.up * 0.2f, direction, 0.40f, 0.65f);
                        }
                    }

                    if (options.ProjectileForkStacks > 0)
                    {
                        Vector3 baseDirection = (victim.transform.position - CharacterMainControl.Main.transform.position);
                        baseDirection.y = 0f;
                        if (baseDirection.sqrMagnitude <= 0.001f)
                        {
                            baseDirection = CharacterMainControl.Main.transform.forward;
                        }
                        baseDirection.Normalize();
                        spawnedSupportProjectile |= TrySpawnZombieModePlayerSupportProjectile(victim.transform.position + Vector3.up * 0.2f, Quaternion.Euler(0f, -20f, 0f) * baseDirection, 0.35f, 0.55f);
                        spawnedSupportProjectile |= TrySpawnZombieModePlayerSupportProjectile(victim.transform.position + Vector3.up * 0.2f, Quaternion.Euler(0f, 20f, 0f) * baseDirection, 0.35f, 0.55f);
                    }

                    if (options.ProjectileReturnStacks > 0)
                    {
                        Vector3 returnDirection = (CharacterMainControl.Main.transform.position - victim.transform.position);
                        returnDirection.y = 0f;
                        if (returnDirection.sqrMagnitude > 0.001f)
                        {
                            spawnedSupportProjectile |= TrySpawnZombieModePlayerSupportProjectile(victim.transform.position + Vector3.up * 0.2f, returnDirection.normalized, 0.45f, 0.70f);
                        }
                    }

                    if (spawnedSupportProjectile)
                    {
                        options.LastTrajectorySupportTriggerTime = now;
                    }
                }
            }
        }

        private void HandleZombieModeOptionHealthDead(
            int runId,
            Health health,
            DamageInfo damageInfo,
            CharacterMainControl victim,
            ZombieModeEnemyRuntimeMarker marker)
        {
            if (!IsZombieModeRunValid(runId) ||
                health == null ||
                victim == null ||
                marker == null ||
                marker.RunId != zombieModeRunState.RunId ||
                damageInfo.fromCharacter != CharacterMainControl.Main ||
                damageInfo.isFromBuffOrEffect)
            {
                return;
            }

            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (options.TriggerPurificationSiphonStacks > 0)
            {
                int stacks = Mathf.Min(2, options.TriggerPurificationSiphonStacks);
                int bonus = Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(1, marker.PurificationPointValue) * 0.20f * stacks));
                SpawnZombieModeDeathStars(runId, victim.transform.position, bonus, 1);
            }

            if (options.TriggerSecondWindStacks > 0)
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null && player.Health != null)
                {
                    player.Health.AddHealth(Mathf.Min(10f, 2f * Mathf.Min(5, options.TriggerSecondWindStacks)));
                }
            }

            if (options.TriggerDoomPulseStacks > 0)
            {
                int stacks = Mathf.Min(3, options.TriggerDoomPulseStacks);
                options.DoomPulseKillCounter++;
                int interval = Mathf.Max(20, 40 - 6 * (stacks - 1));
                if (options.DoomPulseKillCounter >= interval)
                {
                    options.DoomPulseKillCounter = 0;
                    TriggerZombieModeDoomPulse(runId, stacks);
                }
            }
        }

        private void TriggerZombieModeDoomPulse(int runId, int stacks)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (!IsZombieModeRunValid(runId) || player == null)
            {
                return;
            }

            Vector3 center = player.transform.position;
            Vector3 forward = player.transform.forward;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Vector3.forward;
            }

            float radius = 2.75f;
            float damage = 30f + 10f * (stacks - 1);
            float offsetDistance = 1.5f + 0.25f * (stacks - 1);
            for (int i = 0; i < 3; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 120f * i, 0f) * forward * offsetDistance;
                CreateZombieModeOptionExplosion(runId, center + offset, radius, damage);
            }
        }

        private void CreateZombieModeOptionExplosion(int runId, Vector3 position, float radius, float damage)
        {
            if (!IsZombieModeRunValid(runId) || CharacterMainControl.Main == null)
            {
                return;
            }

            if (LevelManager.Instance == null || LevelManager.Instance.ExplosionManager == null)
            {
                float now = GetZombieModeRuntimeNow();
                if (now - zombieModeOptionExplosionSkipLogTime >= 5f)
                {
                    zombieModeOptionExplosionSkipLogTime = now;
                    DevLog("[ZombieMode] option explosion skipped: ExplosionManager unavailable");
                }
                return;
            }

            try
            {
                DamageInfo info = new DamageInfo(CharacterMainControl.Main);
                info.damageValue = damage;
                info.damagePoint = position;
                Vector3 normal = CharacterMainControl.Main.transform.position - position;
                info.damageNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
                info.isExplosion = true;
                info.isFromBuffOrEffect = true;
                LevelManager.Instance.ExplosionManager.CreateExplosion(
                    position,
                    radius,
                    info,
                    ExplosionFxTypes.normal,
                    0.35f,
                    false);
            }
            catch (System.Exception e)
            {
                DevLog("[ZombieMode] option explosion failed: " + e.Message);
            }
        }

        private void StartZombieModeAmmoRainIfNeeded(int runId)
        {
            ZombieModeOptionRuntimeState options = zombieModeRunState.OptionRuntime;
            if (!IsZombieModeRunValid(runId) || options.BattlefieldAmmoRainStacks <= 0 || options.AmmoRainCoroutineStarted)
            {
                return;
            }

            options.AmmoRainCoroutineStarted = true;
            StartZombieModeCoroutine(ZombieModeAmmoRainCoroutine(runId), runId);
        }

        private IEnumerator ZombieModeAmmoRainCoroutine(int runId)
        {
            int stacks = Mathf.Min(2, zombieModeRunState.OptionRuntime.BattlefieldAmmoRainStacks);
            float nextGrantDelay = stacks >= 2 ? 35f : 45f;
            float countdownRemaining = nextGrantDelay;
            float previousNow = GetZombieModeRuntimeNow();
            while (IsZombieModeRunValid(runId) && zombieModeRunState.OptionRuntime.BattlefieldAmmoRainStacks > 0)
            {
                float now = GetZombieModeRuntimeNow();
                float deltaTime = Mathf.Max(0f, now - previousNow);
                previousNow = now;
                // 暂停帧丢弃 deltaTime；previousNow 必须先更新，避免恢复时一次性补扣。
                if (IsZombieModeRuntimePaused())
                {
                    yield return null;
                    continue;
                }

                ZombieModeCombatPhase phase = zombieModeRunState.CombatPhase;
                bool allowedPhase = phase == ZombieModeCombatPhase.InitialPreparation ||
                                    phase == ZombieModeCombatPhase.Preparation ||
                                    phase == ZombieModeCombatPhase.ExtractionOpportunity ||
                                    phase == ZombieModeCombatPhase.Combat;
                if (!allowedPhase)
                {
                    yield return null;
                    continue;
                }

                stacks = Mathf.Min(2, zombieModeRunState.OptionRuntime.BattlefieldAmmoRainStacks);
                nextGrantDelay = stacks >= 2 ? 35f : 45f;
                countdownRemaining = Mathf.Min(countdownRemaining, nextGrantDelay);
                int amount = stacks >= 2 ? 90 : 60;
                countdownRemaining -= deltaTime;
                if (countdownRemaining <= 0f)
                {
                    GrantZombieModeAmmoRainSupply(amount);
                    countdownRemaining = nextGrantDelay;
                }

                yield return null;
            }
        }

        private void GrantZombieModeAmmoRainSupply(int amount)
        {
            string caliber = !string.IsNullOrEmpty(zombieModeRunState.StarterAmmoCaliber)
                ? zombieModeRunState.StarterAmmoCaliber
                : string.Empty;
            if (!string.IsNullOrEmpty(caliber) && TryGiveZombieModeStarterAmmo(caliber, amount))
            {
                return;
            }

            if (!TryGiveRandomItemByTags(ZombieModeRewardTagAmmo, 1, 4))
            {
                TryGiveRandomItemByTags(ZombieModeRewardTagBullet, 1, 4);
            }
        }

        private void TryApplyZombieModeEnemyStasis(CharacterMainControl enemy, ZombieModeEnemyRuntimeMarker marker, float slowPercent, float duration)
        {
            if (enemy == null || marker == null || marker.IsBoss || enemy.CharacterItem == null)
            {
                return;
            }

            ZombieModeEnemyStasisRuntime runtime = enemy.gameObject.GetComponent<ZombieModeEnemyStasisRuntime>();
            if (runtime == null)
            {
                runtime = enemy.gameObject.AddComponent<ZombieModeEnemyStasisRuntime>();
            }

            runtime.Apply(marker.RunId, slowPercent, duration);
        }

        private CharacterMainControl TryFindZombieModeNearestEnemyTarget(int runId, CharacterMainControl exclude, float radius)
        {
            CharacterMainControl result = null;
            float bestSqr = radius * radius;
            int count = CollectZombieModeRuntimeEnemyMarkers(runId, zombieModeEnemyMarkerScratch, false);
            if (count <= 0)
            {
                return null;
            }

            CharacterMainControl main = CharacterMainControl.Main;
            Vector3 origin = exclude != null ? exclude.transform.position : (main != null ? main.transform.position : Vector3.zero);
            for (int i = 0; i < zombieModeEnemyMarkerScratch.Count; i++)
            {
                ZombieModeEnemyRuntimeMarker marker = zombieModeEnemyMarkerScratch[i];
                if (marker == null || marker.RunId != runId || marker.DeathSettled || marker.RemovedFromRuntime)
                {
                    continue;
                }

                CharacterMainControl enemy = marker.Owner != null ? marker.Owner : marker.GetComponent<CharacterMainControl>();
                if (enemy == null || enemy == exclude)
                {
                    continue;
                }

                float sqr = (enemy.transform.position - origin).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    result = enemy;
                }
            }

            zombieModeEnemyMarkerScratch.Clear();
            return result;
        }

        private bool TrySpawnZombieModePlayerSupportProjectile(Vector3 origin, Vector3 direction, float damageFactor, float distanceFactor)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || LevelManager.Instance == null || LevelManager.Instance.BulletPool == null)
            {
                return false;
            }

            ItemAgent_Gun gun = player.GetGun();
            if (gun == null || gun.GunItemSetting == null)
            {
                return false;
            }

            Projectile bulletPrefab = gun.GunItemSetting.bulletPfb;
            if (bulletPrefab == null && GameplayDataSettings.Prefabs != null)
            {
                bulletPrefab = GameplayDataSettings.Prefabs.DefaultBullet;
            }

            if (bulletPrefab == null)
            {
                return false;
            }

            Projectile bullet = LevelManager.Instance.BulletPool.GetABullet(bulletPrefab);
            if (bullet == null)
            {
                return false;
            }

            direction = direction.sqrMagnitude > 0.001f ? direction.normalized : player.transform.forward;
            bullet.transform.position = origin;
            bullet.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

            ProjectileContext ctx = default(ProjectileContext);
            ctx.direction = direction;
            ctx.speed = gun.BulletSpeed;
            ctx.distance = gun.BulletDistance * distanceFactor;
            ctx.halfDamageDistance = ctx.distance * 0.5f;
            ctx.damage = Mathf.Max(1f, gun.Damage * damageFactor);
            ctx.penetrate = 0;
            ctx.critRate = 0f;
            ctx.critDamageFactor = gun.CritDamageFactor;
            ctx.armorPiercing = gun.ArmorPiercing;
            ctx.armorBreak = gun.ArmorBreak;
            ctx.fromCharacter = player;
            ctx.realFromCharacter = player;
            ctx.team = player.Team;
            ctx.fromWeaponItemID = 0;
            ctx.firstFrameCheck = false;
            bullet.Init(ctx);
            return true;
        }
    }
}
