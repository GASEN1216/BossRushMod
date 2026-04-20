// ============================================================================
// PhantomWitchAbilityController.cs - 幽灵女巫Boss能力控制器
// ============================================================================
// 模块说明：
//   管理幽灵女巫Boss的AI状态机、攻击序列和阶段转换。
//   新版近战核心：
//   - Blink：闪现调整站位
//   - CurseAura：近距离诅咒范围技
//   - ScytheSweep：镰刀横扫
//   - HeavyScytheSlash：二阶段重斩
//   - SummonMinions：召唤亡灵随从
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Buffs;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 幽灵女巫 Boss 能力控制器
    /// </summary>
    public class PhantomWitchAbilityController : MonoBehaviour
    {
        private const float PlayerRefUpdateInterval = 0.1f;

        private static readonly Collider[] damageHitBuffer = new Collider[32];
        private static readonly WaitForSeconds wait05s = new WaitForSeconds(0.5f);
        private static readonly WaitForSeconds wait1s = new WaitForSeconds(1.0f);
        private static readonly WaitForSeconds waitBlinkHide = new WaitForSeconds(PhantomWitchConfig.BlinkHideDuration);
        private static readonly WaitForSeconds waitBlinkRecovery = new WaitForSeconds(PhantomWitchConfig.BlinkRecovery);
        private static readonly WaitForSeconds waitCurseAuraWindup = new WaitForSeconds(PhantomWitchConfig.CurseAuraWindup);
        private static readonly WaitForSeconds waitCurseAuraRecovery = new WaitForSeconds(PhantomWitchConfig.CurseAuraRecovery);
        private static readonly WaitForSeconds waitScytheSweepWindup = new WaitForSeconds(PhantomWitchConfig.ScytheSweepWindup);
        private static readonly WaitForSeconds waitScytheSweepRecovery = new WaitForSeconds(PhantomWitchConfig.ScytheSweepRecovery);
        private static readonly WaitForSeconds waitHeavySlashWindup = new WaitForSeconds(PhantomWitchConfig.HeavyScytheSlashWindup);
        private static readonly WaitForSeconds waitHeavySlashRecovery = new WaitForSeconds(PhantomWitchConfig.HeavyScytheSlashRecovery);
        private static readonly WaitForSeconds waitSummonWindup = new WaitForSeconds(PhantomWitchConfig.SummonWindup);
        private static readonly WaitForSeconds waitSummonRecovery = new WaitForSeconds(PhantomWitchConfig.SummonRecovery);
        private static readonly WaitForSeconds waitCurseRealmWindup = new WaitForSeconds(PhantomWitchConfig.BossCurseRealmWindup);
        private static readonly WaitForSeconds waitCurseRealmRecovery = new WaitForSeconds(PhantomWitchConfig.BossCurseRealmRecovery);
        private static readonly WaitForSeconds waitPhase1Interval = new WaitForSeconds(PhantomWitchConfig.Phase1AttackInterval);
        private static readonly WaitForSeconds waitPhase2Interval = new WaitForSeconds(PhantomWitchConfig.Phase2AttackInterval);

        public PhantomWitchPhase CurrentPhase { get; private set; } = PhantomWitchPhase.Phase1;

        private int currentAttackIndex = 0;
        private bool phase2Triggered = false;
        private CharacterMainControl bossCharacter;
        private Health bossHealth;
        private CharacterMainControl playerCharacter;
        private BossAIController aiController;
        private Coroutine attackLoopCoroutine;
        private Coroutine currentAttackCoroutine;
        private Vector3 spawnAnchorPosition;
        private float lastPlayerRefUpdateTime = -999f;
        private bool assetReferenceReleased = false;

        // ========== 重斩蓄力武器偏移 ==========
        private Vector3 cachedWeaponLocalPos;
        private Quaternion cachedWeaponLocalRot;
        private bool weaponOffsetActive = false;

        // ========== AI 看门狗（防止技能协程被 Unity 静默中断后 Boss 卡死在原地）==========
        /// <summary>
        /// AttackLoop 最近一次推进的时间。用于检测"AI 被暂停且攻击协程不再推进"的异常状态。
        /// </summary>
        private float attackLoopLastTickTime;
        /// <summary>
        /// 连续触发看门狗的时间戳，避免每帧都刷日志。
        /// </summary>
        private float lastWatchdogRecoverTime = -999f;
        private const float WatchdogStallThreshold = 3f;
        private const float WatchdogRecoverCooldown = 1.5f;

        private readonly List<GameObject> activeEffects = new List<GameObject>(24);
        private readonly List<CharacterMainControl> summonedMinions = new List<CharacterMainControl>(4);
        private readonly List<int> processedReceiverIds = new List<int>(16);

        private static CharacterRandomPreset cachedSharedMinionPreset = null;
        private static bool sharedMinionPresetSearched = false;

        private string DescribeCharacter(CharacterMainControl character)
        {
            if (character == null)
            {
                return "null";
            }

            string name = "unknown";
            bool active = false;
            bool hasHealth = false;
            bool isDead = false;
            float currentHealth = -1f;
            float maxHealth = -1f;
            Vector3 position = Vector3.zero;

            try
            {
                if (character.gameObject != null)
                {
                    name = character.gameObject.name;
                    active = character.gameObject.activeInHierarchy;
                }
            }
            catch
            {
            }

            try
            {
                position = character.transform.position;
            }
            catch
            {
            }

            try
            {
                if (character.Health != null)
                {
                    hasHealth = true;
                    isDead = character.Health.IsDead;
                    currentHealth = character.Health.CurrentHealth;
                    maxHealth = character.Health.MaxHealth;
                }
            }
            catch
            {
            }

            return string.Format(
                "{0}(active={1}, health={2}, dead={3}, hp={4:0.##}/{5:0.##}, pos={6})",
                name,
                active,
                hasHealth,
                isDead,
                currentHealth,
                maxHealth,
                position);
        }

        private string DescribeBossState()
        {
            return DescribeCharacter(bossCharacter)
                + ", phase=" + CurrentPhase
                + ", attackIndex=" + currentAttackIndex
                + ", aiPaused=" + (aiController != null && aiController.IsPaused);
        }

        private void LogSkillState(string skillName, string stage, CharacterMainControl target = null)
        {
            ModBehaviour.DevLog(
                "[PhantomWitch] [" + skillName + "] " + stage
                + " | boss=" + DescribeBossState()
                + " | target=" + DescribeCharacter(target));
        }

        private PhantomWitchAttackType[] CurrentSequence
        {
            get
            {
                return CurrentPhase == PhantomWitchPhase.Phase2
                    ? PhantomWitchConfig.Phase2Sequence
                    : PhantomWitchConfig.Phase1Sequence;
            }
        }

        public void Initialize(CharacterMainControl character, Vector3 anchorPosition)
        {
            bossCharacter = character;
            spawnAnchorPosition = anchorPosition;

            if (bossCharacter == null)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] Initialize: bossCharacter is null");
                return;
            }

            bossHealth = bossCharacter.Health;
            aiController = new BossAIController(bossCharacter, "PhantomWitch");
            aiController.EnsureInitialized();

            NormalizeGhostElementFactor();
            UpdatePlayerReference(true);
            Health.OnDead += OnAnyEntityDeath;

            attackLoopLastTickTime = Time.time;
            attackLoopCoroutine = StartCoroutine(AttackLoop());
            ModBehaviour.DevLog("[PhantomWitch] 能力控制器初始化完成");
        }

        /// <summary>
        /// 将幽灵女巫的幽灵属性受伤系数设为 1，避免噬魂挽歌的 ghost 元素触发原版幽灵弱点导致伤害翻倍。
        /// 仅影响本 Boss 实例，不影响其他幽灵敌人。
        /// </summary>
        private void NormalizeGhostElementFactor()
        {
            try
            {
                if (bossCharacter == null || bossCharacter.CharacterItem == null) return;
                Stat ghostStat = bossCharacter.CharacterItem.GetStat("ElementFactor_Ghost");
                if (ghostStat != null && ghostStat.BaseValue > 1f)
                {
                    ModBehaviour.DevLog("[PhantomWitch] ElementFactor_Ghost 原值=" + ghostStat.BaseValue + " → 1.0");
                    ghostStat.BaseValue = 1f;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] NormalizeGhostElementFactor 失败: " + e.Message);
            }
        }

        private void UpdatePlayerReference(bool force = false)
        {
            if (!force && Time.time - lastPlayerRefUpdateTime < PlayerRefUpdateInterval)
            {
                return;
            }

            lastPlayerRefUpdateTime = Time.time;

            try
            {
                var inst = ModBehaviour.Instance;
                if (inst != null && inst.IsModeEActive)
                {
                    AICharacterController ai = aiController != null ? aiController.GetAI() : null;
                    if (ai != null && ai.searchedEnemy != null)
                    {
                        CharacterMainControl target = null;
                        try
                        {
                            if (ai.searchedEnemy.health != null)
                            {
                                target = ai.searchedEnemy.health.TryGetCharacter();
                            }
                        }
                        catch
                        {
                        }

                        if (target == null)
                        {
                            target = ai.searchedEnemy.GetComponentInParent<CharacterMainControl>();
                        }

                        if (IsValidCombatTarget(target))
                        {
                            playerCharacter = target;
                            return;
                        }
                    }

                    playerCharacter = null;
                    return;
                }

                playerCharacter = CharacterMainControl.Main;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] UpdatePlayerReference异常: " + e.Message);
                playerCharacter = CharacterMainControl.Main;
            }
        }

        private bool IsValidCombatTarget(CharacterMainControl target)
        {
            return target != null &&
                   target.Health != null &&
                   !target.Health.IsDead &&
                   target.gameObject != null &&
                   target.gameObject.activeInHierarchy;
        }

        private bool TryResolveCombatTarget(out CharacterMainControl target)
        {
            UpdatePlayerReference();
            if (IsValidCombatTarget(playerCharacter))
            {
                target = playerCharacter;
                return true;
            }

            ModBehaviour.DevLog(
                "[PhantomWitch] [Target] 无有效目标，cachedTarget=" + DescribeCharacter(playerCharacter)
                + " | boss=" + DescribeBossState());
            target = null;
            return false;
        }

        private IEnumerator AttackLoop()
        {
            yield return wait1s;
            attackLoopLastTickTime = Time.time;

            ModBehaviour.DevLog("[PhantomWitch] 攻击循环开始");

            while (CurrentPhase != PhantomWitchPhase.Dead && bossCharacter != null)
            {
                if (CurrentPhase == PhantomWitchPhase.Transitioning)
                {
                    attackLoopLastTickTime = Time.time;
                    yield return wait05s;
                    continue;
                }

                CharacterMainControl target;
                bool hasTarget = TryResolveCombatTarget(out target);
                if (!hasTarget)
                {
                    var inst = ModBehaviour.Instance;
                    if (inst != null && inst.IsModeEActive)
                    {
                        attackLoopLastTickTime = Time.time;
                        yield return wait05s;
                        continue;
                    }

                    ModBehaviour.DevLog("[PhantomWitch] [AttackLoop] 普通 BossRush 下目标丢失，触发 OnPlayerDeath");
                    OnPlayerDeath();
                    yield break;
                }

                CheckPhaseTransition();
                if (CurrentPhase == PhantomWitchPhase.Transitioning || CurrentPhase == PhantomWitchPhase.Dead)
                {
                    yield break;
                }

                PhantomWitchAttackType[] sequence = CurrentSequence;
                if (sequence == null || sequence.Length == 0)
                {
                    attackLoopLastTickTime = Time.time;
                    yield return wait05s;
                    continue;
                }

                PhantomWitchAttackType attackType = sequence[currentAttackIndex % sequence.Length];
                LogSkillState(attackType.ToString(), "start", target);
                attackLoopLastTickTime = Time.time;
                currentAttackCoroutine = StartCoroutine(ExecuteAttack(attackType));
                yield return currentAttackCoroutine;
                currentAttackCoroutine = null;
                LogSkillState(attackType.ToString(), "end", target);
                attackLoopLastTickTime = Time.time;

                // 每个技能结束都兜底恢复 AI；若协程内部 ResumeAI 已经跑过，再调一次也幂等。
                if (aiController != null && aiController.IsPaused && CurrentPhase != PhantomWitchPhase.Dead)
                {
                    ModBehaviour.DevLog("[PhantomWitch] [AttackLoop] 技能结束但 AI 仍处于暂停，补发 ResumeAI");
                    ResumeAI(target);
                }

                currentAttackIndex = (currentAttackIndex + 1) % sequence.Length;
                yield return CurrentPhase == PhantomWitchPhase.Phase2 ? waitPhase2Interval : waitPhase1Interval;
                attackLoopLastTickTime = Time.time;
            }

            ModBehaviour.DevLog("[PhantomWitch] 攻击循环结束");
        }

        private IEnumerator ExecuteAttack(PhantomWitchAttackType attackType)
        {
            switch (attackType)
            {
                case PhantomWitchAttackType.Blink:
                    yield return ExecuteBlink();
                    break;
                case PhantomWitchAttackType.CurseAura:
                    yield return ExecuteCurseAura();
                    break;
                case PhantomWitchAttackType.ScytheSweep:
                    yield return ExecuteScytheSweep();
                    break;
                case PhantomWitchAttackType.HeavyScytheSlash:
                    yield return ExecuteHeavyScytheSlash();
                    break;
                case PhantomWitchAttackType.SummonMinions:
                    yield return ExecuteSummonMinions();
                    break;
                case PhantomWitchAttackType.CurseRealm:
                    yield return ExecuteCurseRealm();
                    break;
                default:
                    yield return wait05s;
                    break;
            }
        }

        private IEnumerator ExecuteBlink()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            LogSkillState("Blink", "before PauseAI", target);

            PauseAI();

            // 直接传送到玩家脚下（NavMesh 采样保证合法性）
            Vector3 targetPos;
            if (target != null)
            {
                Vector3 playerFeet = target.transform.position;
                targetPos = SampleNavMeshOrFallback(playerFeet,
                    bossCharacter != null ? bossCharacter.transform.position : playerFeet);
            }
            else
            {
                targetPos = ResolveTeleportPosition(target,
                    PhantomWitchConfig.BlinkMinDistance,
                    PhantomWitchConfig.BlinkMaxDistance);
            }
            ModBehaviour.DevLog("[PhantomWitch] [Blink] resolved teleport targetPos=" + targetPos);

            yield return TeleportTo(targetPos);
            FaceTarget(target);

            yield return waitBlinkRecovery;
            LogSkillState("Blink", "before ResumeAI", target);
            ResumeAI(target);
        }

        private IEnumerator ExecuteCurseAura()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            LogSkillState("CurseAura", "before PauseAI", target);

            PauseAI();
            FaceTarget(target);

            if (target != null && GetFlatDistance(bossCharacter.transform.position, target.transform.position) >
                PhantomWitchConfig.CurseAuraRadius + 0.75f)
            {
                Vector3 targetPos = ResolveTeleportPosition(target, 1.6f, 2.8f);
                ModBehaviour.DevLog("[PhantomWitch] [CurseAura] target out of range, teleporting to " + targetPos);
                yield return TeleportTo(targetPos);
                FaceTarget(target);
            }

            TrackEffect(PhantomWitchAssetManager.CreateCurseAuraEffect(
                bossCharacter.transform.position,
                PhantomWitchConfig.CurseAuraRadius));
            yield return waitCurseAuraWindup;

            if (!CanContinueAttacking())
            {
                LogSkillState("CurseAura", "CanContinueAttacking=false, before ResumeAI", target);
                ResumeAI(target);
                yield break;
            }

            DealConeDamage(
                PhantomWitchConfig.CurseAuraRadius,
                180f,
                PhantomWitchConfig.CurseAuraDamage,
                true,
                0.2f,
                target);

            yield return waitCurseAuraRecovery;
            LogSkillState("CurseAura", "before ResumeAI", target);
            ResumeAI(target);
        }

        private IEnumerator ExecuteScytheSweep()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            LogSkillState("ScytheSweep", "before PauseAI", target);

            PauseAI();

            if (target != null && GetFlatDistance(bossCharacter.transform.position, target.transform.position) >
                PhantomWitchConfig.ScytheSweepRadius + 0.6f)
            {
                Vector3 targetPos = ResolveTeleportPosition(target, 1.4f, 2.8f);
                ModBehaviour.DevLog("[PhantomWitch] [ScytheSweep] target out of range, teleporting to " + targetPos);
                yield return TeleportTo(targetPos);
            }

            FaceTarget(target);
            yield return waitScytheSweepWindup;

            if (!CanContinueAttacking())
            {
                LogSkillState("ScytheSweep", "CanContinueAttacking=false, before ResumeAI", target);
                ResumeAI(target);
                yield break;
            }

            Vector3 sweepForward = ResolveAttackForward(target);
            Vector3 sweepOrigin = bossCharacter.transform.position +
                sweepForward * PhantomWitchConfig.ScytheSweepForwardOffset;
            TrackEffect(PhantomWitchAssetManager.CreateScytheSweepEffect(
                sweepOrigin,
                sweepForward,
                PhantomWitchConfig.ScytheSweepRadius,
                PhantomWitchConfig.ScytheSweepHalfAngle));

            DealConeDamage(
                PhantomWitchConfig.ScytheSweepRadius,
                PhantomWitchConfig.ScytheSweepHalfAngle,
                PhantomWitchConfig.ScytheSweepDamage,
                false,
                PhantomWitchConfig.ScytheSweepForwardOffset,
                target);

            yield return waitScytheSweepRecovery;
            LogSkillState("ScytheSweep", "before ResumeAI", target);
            ResumeAI(target);
        }

        private IEnumerator ExecuteHeavyScytheSlash()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            LogSkillState("HeavyScytheSlash", "before PauseAI", target);

            PauseAI();

            Vector3 targetPos = ResolveTeleportPosition(
                target,
                PhantomWitchConfig.HeavySlashBlinkMinDistance,
                PhantomWitchConfig.HeavySlashBlinkMaxDistance);
            ModBehaviour.DevLog("[PhantomWitch] [HeavyScytheSlash] resolved teleport targetPos=" + targetPos);

            yield return TeleportTo(targetPos);
            FaceTarget(target);

            RaiseWeaponForHeavySlash();
            yield return waitHeavySlashWindup;

            if (!CanContinueAttacking())
            {
                RestoreWeaponPosition();
                LogSkillState("HeavyScytheSlash", "CanContinueAttacking=false, before ResumeAI", target);
                ResumeAI(target);
                yield break;
            }

            Vector3 heavyForward = ResolveAttackForward(target);
            Vector3 heavyOrigin = bossCharacter.transform.position +
                heavyForward * PhantomWitchConfig.HeavyScytheSlashForwardOffset;
            TrackEffect(PhantomWitchAssetManager.CreateHeavySlashEffect(
                heavyOrigin,
                heavyForward,
                PhantomWitchConfig.HeavyScytheSlashRadius));

            DealConeDamage(
                PhantomWitchConfig.HeavyScytheSlashRadius,
                PhantomWitchConfig.HeavyScytheSlashHalfAngle,
                PhantomWitchConfig.HeavyScytheSlashDamage,
                true,
                PhantomWitchConfig.HeavyScytheSlashForwardOffset,
                target);

            RestoreWeaponPosition();
            yield return waitHeavySlashRecovery;
            LogSkillState("HeavyScytheSlash", "before ResumeAI", target);
            ResumeAI(target);
        }

        private IEnumerator ExecuteCurseRealm()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            LogSkillState("CurseRealm", "before PauseAI", target);

            PauseAI();

            if (target != null && GetFlatDistance(bossCharacter.transform.position, target.transform.position) >
                PhantomWitchConfig.BossCurseRealmRadius + 0.75f)
            {
                Vector3 telePos = ResolveTeleportPosition(
                    target,
                    PhantomWitchConfig.BossCurseRealmTeleportMinDist,
                    PhantomWitchConfig.BossCurseRealmTeleportMaxDist);
                ModBehaviour.DevLog("[PhantomWitch] [CurseRealm] target out of range, teleporting to " + telePos);
                yield return TeleportTo(telePos);
            }

            FaceTarget(target);
            yield return waitCurseRealmWindup;

            if (!CanContinueAttacking())
            {
                LogSkillState("CurseRealm", "CanContinueAttacking=false, before ResumeAI", target);
                ResumeAI(target);
                yield break;
            }

            // 在目标位置生成诅咒领域（复用玩家右键技能的 Runtime，使用 Boss 专属参数）
            Vector3 realmOrigin = target != null
                ? target.transform.position
                : bossCharacter.transform.position + bossCharacter.transform.forward * 2f;
            try
            {
                GameObject host = new GameObject("PhantomWitch_BossCurseRealm");
                host.transform.position = realmOrigin;
                PhantomWitchCurseRealmRuntime runtime = host.AddComponent<PhantomWitchCurseRealmRuntime>();
                runtime.Initialize(
                    realmOrigin,
                    bossCharacter,
                    PhantomWitchConfig.BossCurseRealmRadius,
                    PhantomWitchConfig.BossCurseRealmDuration,
                    PhantomWitchConfig.BossCurseRealmDamagePerTick,
                    PhantomWitchConfig.BossCurseRealmDamageInterval);
                TrackEffect(host);
                ModBehaviour.DevLog("[PhantomWitch] [CurseRealm] 诅咒领域已生成 @ " + realmOrigin);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [CurseRealm] 生成失败: " + e.Message);
            }

            yield return waitCurseRealmRecovery;
            LogSkillState("CurseRealm", "before ResumeAI", target);
            ResumeAI(target);
        }

        private IEnumerator ExecuteSummonMinions()
        {
            if (bossCharacter == null)
            {
                yield break;
            }

            LogSkillState("SummonMinions", "before PauseAI", playerCharacter);
            PauseAI();

            summonedMinions.RemoveAll(delegate(CharacterMainControl minion)
            {
                return minion == null || minion.Health == null || minion.Health.IsDead;
            });

            int toSpawn = Mathf.Min(
                PhantomWitchConfig.SpawnPerSummon,
                PhantomWitchConfig.MaxMinions - summonedMinions.Count);
            if (toSpawn <= 0)
            {
                ModBehaviour.DevLog("[PhantomWitch] [SummonMinions] no slots available, summonedMinions=" + summonedMinions.Count);
                yield return wait05s;
                LogSkillState("SummonMinions", "no slots, before ResumeAI", playerCharacter);
                ResumeAI(playerCharacter);
                yield break;
            }

            TrackEffect(PhantomWitchAssetManager.CreateSummonCircleEffect(
                bossCharacter.transform.position));
            yield return waitSummonWindup;

            for (int i = 0; i < toSpawn; i++)
            {
                if (bossCharacter == null || CurrentPhase == PhantomWitchPhase.Dead)
                {
                    break;
                }

                SpawnMinion(i, toSpawn).Forget();

                // 每只之间让出一帧，避免二阶段同帧创建多个随从导致低端机卡顿尖峰。
                if (i < toSpawn - 1)
                {
                    yield return null;
                }
            }

            yield return waitSummonRecovery;
            LogSkillState("SummonMinions", "before ResumeAI", playerCharacter);
            ResumeAI(playerCharacter);
        }

        private async UniTask SpawnMinion(int index, int totalCount)
        {
            if (bossCharacter == null)
            {
                return;
            }

            try
            {
                float angleStep = 360f / Mathf.Max(totalCount, 1);
                Vector3 spawnPos = bossCharacter.transform.position +
                    Quaternion.Euler(0f, index * angleStep, 0f) * Vector3.forward * PhantomWitchConfig.MinionSpawnDistance;

                spawnPos = SampleNavMeshOrFallback(spawnPos, bossCharacter.transform.position);
                TrackEffect(PhantomWitchAssetManager.CreateMinionSpawnEffect(spawnPos));

                CharacterRandomPreset minionPreset = GetCachedMinionPreset();
                if (minionPreset == null)
                {
                    ModBehaviour.DevLog("[PhantomWitch] [WARNING] 未找到合适的随从预设");
                    return;
                }

                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                CharacterMainControl minion = await minionPreset.CreateCharacterAsync(
                    spawnPos,
                    Vector3.forward,
                    relatedScene,
                    null,
                    false);

                if (minion == null)
                {
                    return;
                }

                if (bossCharacter == null || CurrentPhase == PhantomWitchPhase.Dead)
                {
                    CleanupSpawnedMinion(minion);
                    return;
                }

                FinalizeSpawnedMinion(minion, index);
                ModBehaviour.DevLog("[PhantomWitch] 随从 " + index + " 已召唤");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] 生成随从失败: " + e.Message);
            }
        }

        private void ApplyMinionHealth(CharacterMainControl minion)
        {
            if (minion == null || minion.Health == null)
            {
                return;
            }

            Stat healthStat = minion.CharacterItem != null
                ? minion.CharacterItem.GetStat("MaxHealth")
                : null;
            if (healthStat != null)
            {
                healthStat.BaseValue = PhantomWitchConfig.MinionHealth;
            }

            minion.Health.SetHealth(PhantomWitchConfig.MinionHealth);
            minion.Health.showHealthBar = true;
            minion.Health.RequestHealthBar();
        }

        private void ConfigureMinionAI(CharacterMainControl minion)
        {
            AICharacterController aiCtrl = minion != null ? minion.GetComponentInChildren<AICharacterController>() : null;
            if (aiCtrl == null)
            {
                return;
            }

            var inst = ModBehaviour.Instance;
            bool isModeE = inst != null && inst.IsModeEActive;
            aiCtrl.forceTracePlayerDistance = isModeE ? 0f : PhantomWitchConfig.MinionForceTraceDistance;

            CharacterMainControl target;
            if (!isModeE && TryResolveCombatTarget(out target) &&
                target.mainDamageReceiver != null)
            {
                aiCtrl.searchedEnemy = target.mainDamageReceiver;
                aiCtrl.noticed = true;
            }
        }

        private void FinalizeSpawnedMinion(CharacterMainControl minion, int index)
        {
            if (minion == null || bossCharacter == null)
            {
                return;
            }

            minion.SetTeam(bossCharacter.Team);
            minion.dropBoxOnDead = false;
            minion.gameObject.SetActive(true);
            ApplyMinionHealth(minion);
            ConfigureMinionAI(minion);

            minion.gameObject.name = "PhantomWitch_Minion_" + index;
            summonedMinions.Add(minion);
        }

        private void OnAnyEntityDeath(Health deadHealth, DamageInfo info)
        {
            if (CurrentPhase == PhantomWitchPhase.Dead || deadHealth == null)
            {
                return;
            }

            try
            {
                CharacterMainControl deadChar = deadHealth.TryGetCharacter();
                if (deadChar != null && summonedMinions.Contains(deadChar))
                {
                    summonedMinions.Remove(deadChar);
                    ModBehaviour.DevLog("[PhantomWitch] 随从被击杀，剩余: " + summonedMinions.Count);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] OnAnyEntityDeath处理失败: " + e.Message);
            }
        }

        private void Update()
        {
            // 控制器在独立 GO 上，若 bossCharacter 被外部销毁则自行清理
            // Destroy 延迟到帧末执行，届时 Unity 自动调用 OnDestroy 完成资源释放
            if (bossCharacter == null)
            {
                Destroy(gameObject);
                return;
            }

            TickAttackLoopWatchdog();

            if (CurrentPhase != PhantomWitchPhase.Phase2 || bossHealth == null || bossHealth.IsDead)
            {
                return;
            }

            TickMinionHealBonus();
        }

        /// <summary>
        /// 看门狗：如果 AttackLoop 长时间没推进（协程被 Unity 静默中断、
        /// 或 ResumeAI 因 yield 等待未执行），强制恢复 AI 并重启循环，
        /// 避免出现"Boss 第一个技能后原地不动"的死局。
        /// </summary>
        private void TickAttackLoopWatchdog()
        {
            if (bossCharacter == null || bossHealth == null || bossHealth.IsDead)
            {
                return;
            }
            if (CurrentPhase == PhantomWitchPhase.Dead || CurrentPhase == PhantomWitchPhase.Transitioning)
            {
                return;
            }
            if (aiController == null)
            {
                return;
            }

            bool stalled = (Time.time - attackLoopLastTickTime) > WatchdogStallThreshold;
            if (!stalled)
            {
                return;
            }

            if (Time.time - lastWatchdogRecoverTime < WatchdogRecoverCooldown)
            {
                return;
            }
            lastWatchdogRecoverTime = Time.time;

            ModBehaviour.DevLog(
                "[PhantomWitch] [Watchdog] AttackLoop 停滞 " + (Time.time - attackLoopLastTickTime).ToString("0.0")
                + "s，强制恢复 AI 并重启攻击循环");

            if (currentAttackCoroutine != null)
            {
                try { StopCoroutine(currentAttackCoroutine); } catch { }
                currentAttackCoroutine = null;
            }
            if (attackLoopCoroutine != null)
            {
                try { StopCoroutine(attackLoopCoroutine); } catch { }
                attackLoopCoroutine = null;
            }

            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            RestoreVisibleState();
            ResumeAI(target);

            attackLoopLastTickTime = Time.time;
            attackLoopCoroutine = StartCoroutine(AttackLoop());
        }

        private void TickMinionHealBonus()
        {
            int aliveMinions = 0;
            for (int i = summonedMinions.Count - 1; i >= 0; i--)
            {
                CharacterMainControl minion = summonedMinions[i];
                if (minion != null && minion.Health != null && !minion.Health.IsDead)
                {
                    aliveMinions++;
                }
                else
                {
                    summonedMinions.RemoveAt(i);
                }
            }

            if (aliveMinions > 0)
            {
                bossHealth.AddHealth(PhantomWitchConfig.MinionHealPerSecond * aliveMinions * Time.deltaTime);
            }
        }

        private void CheckPhaseTransition()
        {
            if (phase2Triggered || bossHealth == null || bossHealth.MaxHealth <= 0f)
            {
                return;
            }

            float healthPercent = bossHealth.CurrentHealth / bossHealth.MaxHealth;
            if (healthPercent <= PhantomWitchConfig.Phase2HealthThreshold)
            {
                ModBehaviour.DevLog(
                    "[PhantomWitch] [Phase] 达到二阶段阈值"
                    + " | hp=" + bossHealth.CurrentHealth + "/" + bossHealth.MaxHealth
                    + " | percent=" + healthPercent);
                phase2Triggered = true;
                StartCoroutine(TransitionToPhase2());
            }
        }

        private IEnumerator TransitionToPhase2()
        {
            ModBehaviour.DevLog("[PhantomWitch] 触发二阶段转换 | boss=" + DescribeBossState());

            CurrentPhase = PhantomWitchPhase.Transitioning;

            if (currentAttackCoroutine != null)
            {
                ModBehaviour.DevLog("[PhantomWitch] [Phase] 停止当前攻击协程以进入二阶段");
                StopCoroutine(currentAttackCoroutine);
                currentAttackCoroutine = null;
            }

            LogSkillState("PhaseTransition", "before PauseAI", null);
            PauseAI();

            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            Vector3 targetPos = ResolveTeleportPosition(target, 5f, 8f);
            ModBehaviour.DevLog("[PhantomWitch] [Phase] transition teleport targetPos=" + targetPos);

            yield return TeleportTo(targetPos);
            FaceTarget(target);
            TrackEffect(PhantomWitchAssetManager.CreatePhaseTransitionEffect(
                bossCharacter.transform.position));

            if (ModBehaviour.Instance != null)
            {
                ModBehaviour.Instance.ShowMessage(
                    L10n.T(PhantomWitchConfig.Phase2MessageCN, PhantomWitchConfig.Phase2MessageEN));
            }

            yield return wait1s;

            CurrentPhase = PhantomWitchPhase.Phase2;
            currentAttackIndex = 0;
            LogSkillState("PhaseTransition", "before ResumeAI", target);
            ResumeAI(target);

            if (attackLoopCoroutine != null)
            {
                StopCoroutine(attackLoopCoroutine);
            }

            attackLoopCoroutine = StartCoroutine(AttackLoop());
            ModBehaviour.DevLog("[PhantomWitch] 二阶段转换完成");
        }

        public void OnBossDeath()
        {
            CurrentPhase = PhantomWitchPhase.Dead;

            Health.OnDead -= OnAnyEntityDeath;
            StopAllCoroutines();
            RestoreVisibleState();
            CleanupMinions();
            CleanupAllEffects();
            ReleaseAssetReferenceIfNeeded();

            ModBehaviour.DevLog("[PhantomWitch] Boss死亡清理完成");

            // 控制器在独立 GO 上，需显式销毁
            Destroy(gameObject);
        }

        public void OnPlayerDeath()
        {
            CurrentPhase = PhantomWitchPhase.Dead;
            Health.OnDead -= OnAnyEntityDeath;

            ModBehaviour.DevLog(
                "[PhantomWitch] 检测到玩家死亡，停止所有攻击"
                + " | cachedTarget=" + DescribeCharacter(playerCharacter)
                + " | boss=" + DescribeBossState());

            StopAllCoroutines();
            RestoreVisibleState();
            CleanupMinions();
            CleanupAllEffects();

            try
            {
                if (aiController != null)
                {
                    aiController.Resume();
                }
            }
            catch
            {
            }

            ModBehaviour.DevLog("[PhantomWitch] 玩家死亡清理完成");

            // 控制器在独立 GO 上，需显式销毁
            Destroy(gameObject);
        }

        public void ReleaseAssetReferenceIfNeeded()
        {
            if (assetReferenceReleased)
            {
                return;
            }

            assetReferenceReleased = true;
            ModBehaviour.ReleasePhantomWitchInstance();
        }

        private bool CanContinueAttacking()
        {
            return bossCharacter != null &&
                   bossHealth != null &&
                   !bossHealth.IsDead &&
                   CurrentPhase != PhantomWitchPhase.Dead;
        }

        private void PauseAI()
        {
            try
            {
                ModBehaviour.DevLog("[PhantomWitch] [AI] PauseAI request | boss=" + DescribeBossState());
                if (aiController != null)
                {
                    aiController.Pause();
                }
                ModBehaviour.DevLog("[PhantomWitch] [AI] PauseAI done | boss=" + DescribeBossState());
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] PauseAI失败: " + e.Message);
            }
        }

        private void ResumeAI(CharacterMainControl target)
        {
            try
            {
                ModBehaviour.DevLog(
                    "[PhantomWitch] [AI] ResumeAI request | target=" + DescribeCharacter(target)
                    + " | boss=" + DescribeBossState());
                if (aiController != null)
                {
                    aiController.Resume(target);
                }
                ModBehaviour.DevLog(
                    "[PhantomWitch] [AI] ResumeAI done | target=" + DescribeCharacter(target)
                    + " | boss=" + DescribeBossState());
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] ResumeAI失败: " + e.Message);
            }
        }

        private IEnumerator TeleportTo(Vector3 targetPos)
        {
            if (bossCharacter == null || bossHealth == null)
            {
                ModBehaviour.DevLog("[PhantomWitch] [Teleport] skipped because bossCharacter/bossHealth is null");
                yield break;
            }

            // 若目标位置就在原地（ResolveTeleportPosition 找不到 NavMesh 时会回退），
            // 跳过 Hide/Wait/Show，避免出现"原地隐身 0.22s 却像卡住"的观感，
            // 同时规避罕见的 Unity 协程在 Hide/Show 期间被静默中断的死局。
            float teleportDistanceSq = (targetPos - bossCharacter.transform.position).sqrMagnitude;
            if (teleportDistanceSq < 0.16f)
            {
                ModBehaviour.DevLog("[PhantomWitch] [Teleport] targetPos 与当前位置几乎一致，跳过 Hide/Show");
                yield break;
            }

            ModBehaviour.DevLog(
                "[PhantomWitch] [Teleport] begin from " + bossCharacter.transform.position
                + " to " + targetPos
                + " | boss=" + DescribeBossState());
            TrackEffect(PhantomWitchAssetManager.CreateTeleportEffect(
                bossCharacter.transform.position,
                false));
            bossHealth.SetInvincible(true);
            ModBehaviour.DevLog("[PhantomWitch] [Teleport] invincible=true");
            bossCharacter.Hide();
            ModBehaviour.DevLog("[PhantomWitch] [Teleport] hide called");

            yield return waitBlinkHide;

            if (bossCharacter == null || bossHealth == null)
            {
                ModBehaviour.DevLog("[PhantomWitch] [Teleport] aborted after hide because bossCharacter/bossHealth is null");
                yield break;
            }

            bossCharacter.SetPosition(targetPos);
            ModBehaviour.DevLog("[PhantomWitch] [Teleport] SetPosition done, currentPos=" + bossCharacter.transform.position);
            bossCharacter.Show();
            ModBehaviour.DevLog("[PhantomWitch] [Teleport] show called");
            bossHealth.SetInvincible(false);
            ModBehaviour.DevLog("[PhantomWitch] [Teleport] invincible=false");
            TrackEffect(PhantomWitchAssetManager.CreateTeleportEffect(targetPos, true));
            ModBehaviour.DevLog("[PhantomWitch] [Teleport] end | boss=" + DescribeBossState());
        }

        private void RestoreVisibleState()
        {
            RestoreWeaponPosition();

            try
            {
                if (bossCharacter != null)
                {
                    bossCharacter.Show();
                }
            }
            catch
            {
            }

            try
            {
                if (bossHealth != null)
                {
                    bossHealth.SetInvincible(false);
                }
            }
            catch
            {
            }
        }

        private void RaiseWeaponForHeavySlash()
        {
            try
            {
                Transform weaponTf = GetWeaponTransform();
                if (weaponTf == null) return;

                cachedWeaponLocalPos = weaponTf.localPosition;
                cachedWeaponLocalRot = weaponTf.localRotation;
                weaponOffsetActive = true;

                weaponTf.localPosition = cachedWeaponLocalPos + Vector3.up * 0.8f;
                weaponTf.localRotation = cachedWeaponLocalRot * Quaternion.Euler(-45f, 0f, 0f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] RaiseWeaponForHeavySlash失败: " + e.Message);
            }
        }

        private void RestoreWeaponPosition()
        {
            if (!weaponOffsetActive) return;
            weaponOffsetActive = false;

            try
            {
                Transform weaponTf = GetWeaponTransform();
                if (weaponTf == null) return;

                weaponTf.localPosition = cachedWeaponLocalPos;
                weaponTf.localRotation = cachedWeaponLocalRot;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] RestoreWeaponPosition失败: " + e.Message);
            }
        }

        private Transform GetWeaponTransform()
        {
            if (bossCharacter == null) return null;

            try
            {
                Slot meleeSlot = bossCharacter.MeleeWeaponSlot();
                if (meleeSlot != null && meleeSlot.Content != null)
                {
                    return meleeSlot.Content.transform;
                }
            }
            catch
            {
            }

            return null;
        }

        private Vector3 ResolveTeleportPosition(
            CharacterMainControl target,
            float minDistance,
            float maxDistance)
        {
            Vector3 bossPos = bossCharacter != null
                ? bossCharacter.transform.position
                : spawnAnchorPosition;

            // 保留一个"足够远"的保底点：如果 NavMesh 采样失败，宁可直接跳到该位置
            // 也不要停在原地——原地传送会让 Boss 表现得像卡住。
            Vector3 finalFallback = bossPos;

            if (target != null)
            {
                for (int i = 0; i < 8; i++)
                {
                    float angle = UnityEngine.Random.Range(100f, 260f);
                    float distance = UnityEngine.Random.Range(minDistance, maxDistance);
                    Quaternion rotation = Quaternion.Euler(0f, angle, 0f);
                    Vector3 candidate = target.transform.position + rotation * target.transform.forward * distance;
                    candidate.y = target.transform.position.y;

                    Vector3 sampled = SampleNavMeshOrFallback(candidate, bossPos);
                    if ((sampled - bossPos).sqrMagnitude > 0.25f)
                    {
                        return sampled;
                    }
                    // 记下候选里最有意义的"远点"，NavMesh 全军覆没时可直接使用
                    if ((candidate - bossPos).sqrMagnitude > (finalFallback - bossPos).sqrMagnitude)
                    {
                        finalFallback = candidate;
                    }
                }

                Vector3 behindTarget = target.transform.position - target.transform.forward * Mathf.Max(minDistance, PhantomWitchConfig.BlinkFallbackDistance);
                behindTarget.y = target.transform.position.y;
                Vector3 sampledBehind = SampleNavMeshOrFallback(behindTarget, bossPos);
                if ((sampledBehind - bossPos).sqrMagnitude > 0.25f)
                {
                    return sampledBehind;
                }

                // NavMesh 整个范围都没命中 → 用目标位置 + 偏移硬跳，避免原地传送死局
                Vector3 toTarget = target.transform.position - bossPos;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.01f)
                {
                    Vector3 approach = bossPos + toTarget.normalized * Mathf.Min(toTarget.magnitude, maxDistance * 0.6f);
                    approach.y = bossPos.y;
                    return approach;
                }

                return finalFallback;
            }

            Vector3 anchor = spawnAnchorPosition;
            if (anchor == Vector3.zero && bossCharacter != null)
            {
                anchor = bossCharacter.transform.position;
            }

            for (int i = 0; i < 6; i++)
            {
                Vector2 circle = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(minDistance, maxDistance);
                Vector3 candidate = anchor + new Vector3(circle.x, 0f, circle.y);
                Vector3 sampled = SampleNavMeshOrFallback(candidate, bossPos);
                if ((sampled - bossPos).sqrMagnitude > 0.25f)
                {
                    return sampled;
                }
            }

            return SampleNavMeshOrFallback(anchor, bossPos);
        }

        private Vector3 SampleNavMeshOrFallback(Vector3 candidate, Vector3 fallback)
        {
            UnityEngine.AI.NavMeshHit navHit;
            if (UnityEngine.AI.NavMesh.SamplePosition(
                candidate,
                out navHit,
                PhantomWitchConfig.NavMeshSampleRadius,
                UnityEngine.AI.NavMesh.AllAreas))
            {
                return navHit.position;
            }

            if (UnityEngine.AI.NavMesh.SamplePosition(
                fallback,
                out navHit,
                PhantomWitchConfig.NavMeshFallbackRadius,
                UnityEngine.AI.NavMesh.AllAreas))
            {
                return navHit.position;
            }

            return fallback;
        }

        private void FaceTarget(CharacterMainControl target)
        {
            if (bossCharacter == null || target == null)
            {
                return;
            }

            Vector3 dir = target.transform.position - bossCharacter.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
            {
                return;
            }

            bossCharacter.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private int DealConeDamage(
            float radius,
            float halfAngle,
            float damage,
            bool applyCurse,
            float forwardOffset,
            CharacterMainControl target)
        {
            if (bossCharacter == null)
            {
                return 0;
            }

            Vector3 forward = ResolveAttackForward(target);
            Vector3 origin = bossCharacter.transform.position + forward * Mathf.Max(0f, forwardOffset);
            int hitCount = Physics.OverlapSphereNonAlloc(
                origin,
                radius,
                damageHitBuffer,
                FenHuangHalberdRuntime.DamageReceiverLayerMask);

            Buff curseBuff = applyCurse ? PhantomWitchAssetManager.GetCurseBuff() : null;
            processedReceiverIds.Clear();

            int dealtCount = 0;
            for (int i = 0; i < hitCount; i++)
            {
                Collider col = damageHitBuffer[i];
                if (col == null)
                {
                    continue;
                }

                DamageReceiver receiver = FenHuangHalberdRuntime.TryGetDamageReceiver(col);
                if (receiver == null || receiver.health == null || receiver.health.IsDead)
                {
                    continue;
                }

                int receiverId = receiver.GetInstanceID();
                if (processedReceiverIds.Contains(receiverId))
                {
                    continue;
                }
                processedReceiverIds.Add(receiverId);

                if (!IsEnemyReceiver(receiver))
                {
                    continue;
                }

                CharacterMainControl targetCharacter = receiver.health.TryGetCharacter();
                if (targetCharacter == bossCharacter || (targetCharacter != null && targetCharacter.Dashing))
                {
                    continue;
                }

                Vector3 toTarget = receiver.transform.position - origin;
                toTarget.y = 0f;
                float sqrDistance = toTarget.sqrMagnitude;
                if (sqrDistance > radius * radius)
                {
                    continue;
                }

                if (halfAngle < 179f && sqrDistance > 0.0001f)
                {
                    float angle = Vector3.Angle(forward, toTarget.normalized);
                    if (angle > halfAngle)
                    {
                        continue;
                    }
                }

                DamageInfo damageInfo = new DamageInfo(bossCharacter);
                damageInfo.damageType = DamageTypes.normal;
                damageInfo.damageValue = damage;
                damageInfo.damagePoint = receiver.transform.position;
                damageInfo.damageNormal = -forward;
                damageInfo.fromWeaponItemID = GetCurrentWeaponTypeId();
                damageInfo.crit = -1;
                damageInfo.AddElementFactor(ElementTypes.ghost, 1f);

                receiver.Hurt(damageInfo);

                if (applyCurse && curseBuff != null)
                {
                    try
                    {
                        receiver.AddBuff(curseBuff, bossCharacter);
                        GameObject vfxTarget = (targetCharacter != null) ? targetCharacter.gameObject : receiver.gameObject;
                        PhantomWitchCurseSweatVfx.TryAttach(vfxTarget);
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[PhantomWitch] [WARNING] 施加诅咒失败: " + e.Message);
                    }
                }

                dealtCount++;
            }

            if (dealtCount > 0)
            {
                TrackEffect(PhantomWitchAssetManager.CreateDamageHitEffect(origin));
            }

            return dealtCount;
        }

        private Vector3 ResolveAttackForward(CharacterMainControl target)
        {
            Vector3 forward = bossCharacter != null ? bossCharacter.transform.forward : Vector3.forward;
            forward.y = 0f;

            if (target != null && bossCharacter != null)
            {
                Vector3 dir = target.transform.position - bossCharacter.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                {
                    forward = dir.normalized;
                }
            }

            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }

            return forward.normalized;
        }

        private bool IsEnemyReceiver(DamageReceiver receiver)
        {
            if (receiver == null || bossCharacter == null)
            {
                return false;
            }

            return Team.IsEnemy(bossCharacter.Team, receiver.Team);
        }

        private int GetCurrentWeaponTypeId()
        {
            try
            {
                Slot meleeSlot = bossCharacter != null ? bossCharacter.MeleeWeaponSlot() : null;
                if (meleeSlot != null && meleeSlot.Content != null)
                {
                    return meleeSlot.Content.TypeID;
                }
            }
            catch
            {
            }

            return PhantomWitchConfig.PlaceholderScytheTypeId;
        }

        private float GetFlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private void CleanupMinions()
        {
            for (int i = 0; i < summonedMinions.Count; i++)
            {
                CharacterMainControl minion = summonedMinions[i];
                if (minion != null)
                {
                    try
                    {
                        if (minion.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(minion.gameObject);
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[PhantomWitch] [WARNING] 清理随从失败: " + e.Message);
                    }
                }
            }

            summonedMinions.Clear();
            ModBehaviour.DevLog("[PhantomWitch] 所有随从已清理");
        }

        private void CleanupSpawnedMinion(CharacterMainControl minion)
        {
            if (minion == null)
            {
                return;
            }

            summonedMinions.Remove(minion);

            try
            {
                if (minion.gameObject != null)
                {
                    UnityEngine.Object.Destroy(minion.gameObject);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] 清理待接管随从失败: " + e.Message);
            }
        }

        private void CleanupAllEffects()
        {
            PruneDestroyedEffects();

            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                GameObject effect = activeEffects[i];
                if (effect != null)
                {
                    UnityEngine.Object.Destroy(effect);
                }
            }

            activeEffects.Clear();
        }

        private CharacterRandomPreset GetCachedMinionPreset()
        {
            if (sharedMinionPresetSearched)
            {
                return cachedSharedMinionPreset;
            }

            sharedMinionPresetSearched = true;

            try
            {
                CharacterRandomPreset[] presets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();

                for (int i = 0; i < presets.Length; i++)
                {
                    CharacterRandomPreset preset = presets[i];
                    if (preset == null) continue;
                    if (preset.nameKey == PhantomWitchConfig.MinionPresetNameKey)
                    {
                        cachedSharedMinionPreset = preset;
                        ModBehaviour.DevLog("[PhantomWitch] 缓存随从预设: " + preset.name);
                        return cachedSharedMinionPreset;
                    }
                }

                for (int i = 0; i < presets.Length; i++)
                {
                    CharacterRandomPreset preset = presets[i];
                    if (preset == null) continue;
                    if (preset.nameKey == PhantomWitchConfig.FallbackPresetNameKey)
                    {
                        cachedSharedMinionPreset = preset;
                        ModBehaviour.DevLog("[PhantomWitch] 缓存随从回退预设: " + preset.name);
                        return cachedSharedMinionPreset;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] 查找随从预设失败: " + e.Message);
            }

            return null;
        }

        private void PruneDestroyedEffects()
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                if (activeEffects[i] == null)
                {
                    activeEffects.RemoveAt(i);
                }
            }
        }

        private void TrackEffect(GameObject effect)
        {
            try
            {
                PruneDestroyedEffects();
                if (effect != null)
                {
                    activeEffects.Add(effect);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] 生成特效失败: " + e.Message);
            }
        }

        private void OnDestroy()
        {
            // OnBossDeath / OnPlayerDeath 已完成清理并调了 Destroy(gameObject)，
            // Unity 再次回调 OnDestroy 时跳过重复操作。
            if (CurrentPhase == PhantomWitchPhase.Dead)
            {
                ModBehaviour.DevLog("[PhantomWitch] 组件销毁（已由 OnBossDeath/OnPlayerDeath 清理）");
                return;
            }

            Health.OnDead -= OnAnyEntityDeath;
            StopAllCoroutines();
            RestoreVisibleState();
            CleanupMinions();
            CleanupAllEffects();
            ReleaseAssetReferenceIfNeeded();

            if (aiController != null)
            {
                aiController.Cleanup();
                aiController = null;
            }

            bossCharacter = null;
            bossHealth = null;
            playerCharacter = null;

            ModBehaviour.DevLog("[PhantomWitch] 组件销毁，资源清理完成");
        }

        // 当前静态字段（damageHitBuffer、WaitForSeconds 实例）为 AppDomain 级常驻缓存，
        // 不随场景切换清理。保留此方法作为未来扩展入口，与 Boss 模块 ClearXxxStaticCache 调用约定一致。
        public static void ClearStaticCache()
        {
            cachedSharedMinionPreset = null;
            sharedMinionPresetSearched = false;
        }
    }
}
