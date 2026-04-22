// ============================================================================
// PhantomWitchAbilityController.cs - 幽灵女巫Boss能力控制器
// ============================================================================
// 模块说明：
//   管理幽灵女巫Boss的AI状态机、战术包轮播和三阶段切换。
//   当前重构方向：
//   - 保留传送、领域和召怪底层
//   - 将顶层改为 package scheduler
//   - 引入 Phase3 / Boss-only curse realm / dual-minion roles
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
        private static readonly WaitForSeconds waitPhase1PackageInterval = new WaitForSeconds(PhantomWitchConfig.Phase1PackageInterval);
        private static readonly WaitForSeconds waitPhase2PackageInterval = new WaitForSeconds(PhantomWitchConfig.Phase2PackageInterval);
        private static readonly WaitForSeconds waitPhase3PackageInterval = new WaitForSeconds(PhantomWitchConfig.Phase3PackageInterval);
        private static readonly WaitForSeconds waitDoublePressureFollowup = new WaitForSeconds(PhantomWitchConfig.DoublePressureFollowupDelay);

        public PhantomWitchPhase CurrentPhase { get; private set; } = PhantomWitchPhase.Phase1;

        private int currentPackageIndex = 0;
        private PhantomWitchPhase pendingTransitionTargetPhase = PhantomWitchPhase.Phase1;
        private CharacterMainControl bossCharacter;
        private Health bossHealth;
        private CharacterMainControl playerCharacter;
        private BossAIController aiController;
        private Coroutine attackLoopCoroutine;
        private Coroutine currentAttackCoroutine;
        private PhantomWitchAmbientPresence ambientPresence;
        private Vector3 spawnAnchorPosition;
        private float lastPlayerRefUpdateTime = -999f;
        private bool assetReferenceReleased = false;
        private PhantomWitchBossCurseRealmRuntime activeBossCurseRealm;
        private readonly List<MinionEntry> liveMinions = new List<MinionEntry>(2);
        private PhantomWitchStealthMode currentStealthMode = PhantomWitchStealthMode.Visible;
        private readonly List<Renderer> stealthCachedRenderers = new List<Renderer>(16);
        private readonly List<float> stealthCachedAlphas = new List<float>(16);
        private readonly List<MaterialPropertyBlock> stealthCachedBlocks = new List<MaterialPropertyBlock>(16);
        private GameObject activeSemiStealthEffect;
        private float telemetryTrueStealthSec;
        private float telemetrySemiStealthSec;
        private float telemetryVisibleSec;
        private readonly float[] phaseTrueStealthSeconds = new float[3];
        private readonly float[] phaseSemiStealthSeconds = new float[3];
        private readonly float[] phaseVisibleSeconds = new float[3];
        private readonly int[] phasePackageCounts = new int[3];
        private readonly float[] phasePackageDurationSeconds = new float[3];
        private float stealthModeEnteredAt;
        private bool alphaSupportChecked;
        private bool alphaSupported;
        private float currentPackageStartedAt;
        private bool currentPackageHadAttackLanded;
        private PhantomWitchAttackPackageType currentTelemetryPackageType = PhantomWitchAttackPackageType.FlankPressure;
        private int realmWarningCount;
        private int realmCommitCount;
        private int realmForcedClearOnTransitionCount;
        private int realmMisfireCount;
        private int minionTotalSpawned;
        private int minionMaxConcurrent;
        private int minionRosterDesyncCount;
        private bool minionFirstClearedLogged;
        private bool bossFirstPhase3ExitLogged;
        private bool sawSustainMinion;
        private bool sawHarassMinion;
        private int stealthTimeoutCount;
        private int wraithFallbackCount;
        private float phase3NextMotionSnapshotTime = -1f;
        private float phase3TeleportDistance;
        private int phase3TeleportCount;
        private float nextMinionCensusTime = -1f;
        private float nextHarassMinionPressureTime = -1f;
        private float weaponTransformPosDriftMax = 0f;
        private float weaponTransformRotDriftMax = 0f;

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
        private readonly HashSet<PhantomWitchMinionRole> pendingMinionRoles = new HashSet<PhantomWitchMinionRole>();

        private static CharacterRandomPreset cachedSharedMinionPreset = null;
        private static bool sharedMinionPresetSearched = false;

        private sealed class MinionEntry
        {
            public CharacterMainControl character;
            public PhantomWitchMinionRole role;
            public float spawnTimeGameSec;
        }

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
                + ", packageIndex=" + currentPackageIndex
                + ", aiPaused=" + (aiController != null && aiController.IsPaused);
        }

        private void LogSkillState(string skillName, string stage, CharacterMainControl target = null)
        {
            ModBehaviour.DevLog(
                "[PhantomWitch] [" + skillName + "] " + stage
                + " | boss=" + DescribeBossState()
                + " | target=" + DescribeCharacter(target));
        }

        private PhantomWitchAttackPackageType[] CurrentPackageSequence
        {
            get
            {
                switch (CurrentPhase)
                {
                    case PhantomWitchPhase.Phase2:
                        return PhantomWitchConfig.Phase2Packages;
                    case PhantomWitchPhase.Phase3:
                        return PhantomWitchConfig.Phase3Packages;
                    default:
                        return PhantomWitchConfig.Phase1Packages;
                }
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
            ambientPresence = bossCharacter.GetComponent<PhantomWitchAmbientPresence>();
            if (ambientPresence == null)
            {
                ambientPresence = bossCharacter.gameObject.AddComponent<PhantomWitchAmbientPresence>();
            }
            ambientPresence.Initialize(bossCharacter.transform);
            ambientPresence.SetDetailLevel(PhantomWitchFxRuntime.CurrentDetailLevel);
            ambientPresence.SetPhase(CurrentPhase);
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

                PhantomWitchAttackPackageType[] sequence = CurrentPackageSequence;
                if (sequence == null || sequence.Length == 0)
                {
                    attackLoopLastTickTime = Time.time;
                    yield return wait05s;
                    continue;
                }

                PhantomWitchAttackPackageType packageType = sequence[currentPackageIndex % sequence.Length];
                currentTelemetryPackageType = packageType;
                currentPackageStartedAt = Time.time;
                currentPackageHadAttackLanded = false;
                IncrementPhasePackageCount(CurrentPhase);
                EmitTelemetry("package_start", "packageType=" + packageType + ",phase=" + CurrentPhase);
                LogSkillState(packageType.ToString(), "start", target);
                attackLoopLastTickTime = Time.time;
                currentAttackCoroutine = StartCoroutine(ExecuteAttackPackage(packageType));
                yield return currentAttackCoroutine;
                currentAttackCoroutine = null;
                LogSkillState(packageType.ToString(), "end", target);
                float packageDurationMs = Mathf.Max(0f, (Time.time - currentPackageStartedAt) * 1000f);
                int phaseIndex = GetPhaseTelemetryIndex(CurrentPhase);
                if (phaseIndex >= 0)
                {
                    phasePackageDurationSeconds[phaseIndex] += Mathf.Max(0f, Time.time - currentPackageStartedAt);
                }
                EmitTelemetry("package_end",
                    "packageType=" + packageType
                    + ",phase=" + CurrentPhase
                    + ",durationMs=" + packageDurationMs.ToString("0")
                    + ",hadAttackLanded=" + currentPackageHadAttackLanded);
                EmitTelemetry("weapon_transform_guard",
                    "weaponLocalPosDrift=0,weaponLocalRotDrift=0,packageType=" + packageType);
                attackLoopLastTickTime = Time.time;

                // 每个技能结束都兜底恢复 AI；若协程内部 ResumeAI 已经跑过，再调一次也幂等。
                if (aiController != null && aiController.IsPaused && CurrentPhase != PhantomWitchPhase.Dead)
                {
                    ModBehaviour.DevLog("[PhantomWitch] [AttackLoop] 技能结束但 AI 仍处于暂停，补发 ResumeAI");
                    ResumeAI(target);
                }

                currentPackageIndex = (currentPackageIndex + 1) % sequence.Length;
                yield return GetCurrentPackageIntervalYield();
                attackLoopLastTickTime = Time.time;
            }

            ModBehaviour.DevLog("[PhantomWitch] 攻击循环结束");
        }

        private WaitForSeconds GetCurrentPackageIntervalYield()
        {
            switch (CurrentPhase)
            {
                case PhantomWitchPhase.Phase2:
                    return waitPhase2PackageInterval;
                case PhantomWitchPhase.Phase3:
                    return waitPhase3PackageInterval;
                default:
                    return waitPhase1PackageInterval;
            }
        }

        private IEnumerator ExecuteAttackPackage(PhantomWitchAttackPackageType packageType)
        {
            switch (packageType)
            {
                case PhantomWitchAttackPackageType.FlankPressure:
                    yield return ExecuteFlankPressurePackage();
                    yield break;
                case PhantomWitchAttackPackageType.MidrangeRequiem:
                    yield return ExecuteMidrangeRequiemPackage();
                    yield break;
                case PhantomWitchAttackPackageType.WraithTrailObserve:
                    yield return ExecuteWraithTrailObservePackage();
                    yield break;
                case PhantomWitchAttackPackageType.MidrangeDouble:
                    yield return ExecuteMidrangeDoublePackage();
                    yield break;
                case PhantomWitchAttackPackageType.CurseTrap:
                    yield return ExecuteCurseTrapPackage();
                    yield break;
                case PhantomWitchAttackPackageType.ShortDriftPressure:
                    yield return ExecuteShortDriftPressurePackage();
                    yield break;
                case PhantomWitchAttackPackageType.LastStandSummon:
                    yield return ExecuteLastStandSummonPackage();
                    yield break;
                case PhantomWitchAttackPackageType.MinionRetreat:
                    yield return ExecuteMinionRetreatPackage();
                    yield break;
                default:
                    yield return wait05s;
                    yield break;
            }
        }

        private IEnumerator ExecuteFlankPressurePackage()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);

            if (target != null)
            {
                yield return ExecuteTrackedTeleportStrike(target);
            }
            else
            {
                yield return ExecuteImmediateScytheSweep(null);
            }
            if (CanContinueAttacking())
            {
                yield return waitDoublePressureFollowup;
                yield return ExecuteHeavyScytheSlash();
            }
        }

        private IEnumerator ExecuteMidrangeRequiemPackage()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            PauseAI();
            SetStealthMode(PhantomWitchStealthMode.SemiStealthWindup);
            FaceTarget(target);

            TrackEffect(PhantomWitchAssetManager.CreateChannelChargeEffect(
                bossCharacter.transform.position,
                1.2f,
                PhantomWitchConfig.RequiemArcWindup,
                false));
            yield return new WaitForSeconds(PhantomWitchConfig.RequiemArcWindup);

            if (!CanContinueAttacking())
            {
                SetStealthMode(PhantomWitchStealthMode.Visible);
                ResumeAI(target);
                yield break;
            }

            SetStealthMode(PhantomWitchStealthMode.Visible);
            Vector3 forward = ResolveAttackForward(target);
            Vector3 origin = bossCharacter.transform.position + forward * 1.0f;
            TrackEffect(PhantomWitchAssetManager.CreateScytheSweepEffect(
                origin,
                forward,
                PhantomWitchConfig.RequiemArcRange,
                28f));
            DealConeDamage(
                PhantomWitchConfig.RequiemArcRange,
                28f,
                PhantomWitchConfig.RequiemArcDamage,
                true,
                1.0f,
                target);
            ResumeAI(target);
        }

        private IEnumerator ExecuteWraithTrailObservePackage()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            if (target == null)
            {
                wraithFallbackCount++;
                EmitTelemetry("wraith_fallback_to_sweep", "reason=missing_target");
                yield return ExecuteScytheSweep();
                yield break;
            }

            PauseAI();
            SetStealthMode(PhantomWitchStealthMode.SemiStealthWindup);
            FaceTarget(target);
            float windupDuration = Mathf.Max(PhantomWitchConfig.WraithWindupDuration, PhantomWitchConfig.WraithWindupMinGate);
            Vector3 lockedForward = ResolveAttackForward(target);

            GameObject windupOutline = PhantomWitchAssetManager.CreateWraithWindupOutlineEffect(
                bossCharacter.transform.position + lockedForward * 0.9f,
                lockedForward,
                PhantomWitchConfig.WraithWindupOutlineRadius,
                windupDuration);
            if (windupOutline == null)
            {
                wraithFallbackCount++;
                EmitTelemetry("wraith_fallback_to_sweep", "reason=missing_windup_vfx");
                SetStealthMode(PhantomWitchStealthMode.Visible);
                ResumeAI(target);
                yield return ExecuteScytheSweep();
                yield break;
            }

            TrackEffect(windupOutline);

            TrackEffect(PhantomWitchAssetManager.CreateChannelChargeEffect(
                bossCharacter.transform.position,
                1.0f,
                windupDuration,
                true));
            yield return RunBodySinkWindup(windupDuration, PhantomWitchConfig.WraithBodySinkDepth);

            if (!CanContinueAttacking())
            {
                SetStealthMode(PhantomWitchStealthMode.Visible);
                ResumeAI(target);
                yield break;
            }

            SetStealthMode(PhantomWitchStealthMode.Visible);
            Vector3 origin = bossCharacter.transform.position + lockedForward * 1.15f;
            TrackEffect(PhantomWitchAssetManager.CreateHeavySlashEffect(origin, lockedForward, 3.2f));
            DealConeDamage(3.2f, 48f, PhantomWitchConfig.WraithTrailDamage, true, 1.15f, target);
            yield return new WaitForSeconds(PhantomWitchConfig.WraithTrailDelay);

            if (CanContinueAttacking())
            {
                TrackEffect(PhantomWitchAssetManager.CreateScytheSweepEffect(origin, lockedForward, 3.6f, 54f));
                DealConeDamage(3.6f, 54f, PhantomWitchConfig.WraithTrailDamage, false, 1.0f, target);
            }

            ResumeAI(target);
        }

        private IEnumerator ExecuteMidrangeDoublePackage()
        {
            yield return ExecuteMidrangeRequiemPackage();
            if (CanContinueAttacking())
            {
                yield return ExecuteWraithTrailObservePackage();
            }
        }

        private IEnumerator ExecuteCurseTrapPackage()
        {
            float radiusScale = CurrentPhase == PhantomWitchPhase.Phase3
                ? PhantomWitchConfig.CurseRealmPhase3RadiusScale
                : 1f;
            float durationScale = CurrentPhase == PhantomWitchPhase.Phase3
                ? PhantomWitchConfig.CurseRealmPhase3DurationScale
                : 1f;

            yield return ExecuteTelegraphedCurseRealm(radiusScale, durationScale);

            if (CanContinueAttacking())
            {
                if (CurrentPhase == PhantomWitchPhase.Phase3)
                {
                    yield return ExecuteShortDriftPressurePackage();
                }
                else
                {
                    yield return ExecuteFlankPressurePackage();
                }
            }
        }

        private IEnumerator ExecuteShortDriftPressurePackage()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);

            if (target != null)
            {
                yield return ExecuteTrackedTeleportStrike(target);
            }
            else
            {
                yield return ExecuteImmediateScytheSweep(null);
            }
            if (CanContinueAttacking())
            {
                yield return waitDoublePressureFollowup;
                yield return ExecuteHeavyScytheSlash();
            }
        }

        private IEnumerator ExecuteLastStandSummonPackage()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);

            if (CountLiveMinions() >= PhantomWitchConfig.MaxMinions)
            {
                yield return ExecuteMinionRetreatPackage();
                yield break;
            }

            PauseAI();
            SetStealthMode(PhantomWitchStealthMode.SemiStealthWindup);
            TrackEffect(PhantomWitchAssetManager.CreateSummonCircleEffect(bossCharacter.transform.position));
            TrackEffect(PhantomWitchAssetManager.CreateChannelChargeEffect(
                bossCharacter.transform.position,
                PhantomWitchConfig.SummonCircleRadius * 0.55f,
                PhantomWitchConfig.SummonWindup,
                false));
            yield return waitSummonWindup;
            yield return SpawnMinionPair();
            SetStealthMode(PhantomWitchStealthMode.Visible);
            ResumeAI(target);
        }

        private IEnumerator ExecuteMinionRetreatPackage()
        {
            CharacterMainControl retreatAnchor = GetPreferredRetreatMinion();
            CharacterMainControl target;
            TryResolveCombatTarget(out target);

            if (retreatAnchor != null)
            {
                PauseAI();
                SetStealthMode(PhantomWitchStealthMode.TrueStealthTransition);
                Vector3 retreatPos = SampleNavMeshOrFallback(
                    retreatAnchor.transform.position + (bossCharacter.transform.position - retreatAnchor.transform.position).normalized * 1.8f,
                    bossCharacter.transform.position);
                yield return TeleportTo(retreatPos);
                SetStealthMode(PhantomWitchStealthMode.Visible);
                ResumeAI(target);
            }

            yield return ExecuteWraithTrailObservePackage();
        }

        private void SetStealthMode(PhantomWitchStealthMode mode)
        {
            SetStealthMode(mode, "package");
        }

        private void SetStealthMode(PhantomWitchStealthMode mode, string reason)
        {
            if (currentStealthMode == mode)
            {
                return;
            }

            PhantomWitchStealthMode previousMode = currentStealthMode;
            if (previousMode != PhantomWitchStealthMode.Visible)
            {
                EmitTelemetry("stealth_exit", "mode=" + previousMode + ",nextMode=" + mode + ",reason=" + reason);
            }

            RestoreStealthVisuals();
            currentStealthMode = mode;
            stealthModeEnteredAt = Time.time;

            if (bossCharacter == null || bossCharacter.gameObject == null)
            {
                EmitTelemetry("stealth_enter", "mode=" + mode + ",prevMode=" + previousMode + ",reason=no_boss");
                return;
            }

            CacheStealthRenderers();
            switch (mode)
            {
                case PhantomWitchStealthMode.TrueStealthTransition:
                    for (int i = 0; i < stealthCachedRenderers.Count; i++)
                    {
                        if (stealthCachedRenderers[i] != null)
                        {
                            stealthCachedRenderers[i].enabled = false;
                        }
                    }
                    break;
                case PhantomWitchStealthMode.SemiStealthWindup:
                    if (!alphaSupported)
                    {
                        currentStealthMode = PhantomWitchStealthMode.Visible;
                        mode = PhantomWitchStealthMode.Visible;
                        EmitTelemetry("stealth_downgrade", "mode=SemiStealthWindup,reason=no_alpha_support");
                        break;
                    }

                    for (int i = 0; i < stealthCachedRenderers.Count; i++)
                    {
                        MaterialPropertyBlock block = i < stealthCachedBlocks.Count ? stealthCachedBlocks[i] : null;
                        SetRendererAlpha(stealthCachedRenderers[i], block, 0.33f);
                    }

                    if (bossCharacter != null && activeSemiStealthEffect == null)
                    {
                        activeSemiStealthEffect = PhantomWitchAssetManager.CreateSemiStealthWindupEffect(bossCharacter.transform);
                        if (activeSemiStealthEffect != null)
                        {
                            TrackEffect(activeSemiStealthEffect);
                        }
                    }
                    break;
                case PhantomWitchStealthMode.Visible:
                    break;
            }

            EmitTelemetry("stealth_enter", "mode=" + mode + ",prevMode=" + previousMode + ",reason=" + reason);
        }

        private void CacheStealthRenderers()
        {
            stealthCachedRenderers.Clear();
            stealthCachedAlphas.Clear();
            stealthCachedBlocks.Clear();

            if (bossCharacter == null || bossCharacter.gameObject == null)
            {
                return;
            }

            Renderer[] renderers = bossCharacter.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                stealthCachedRenderers.Add(renderer);
                stealthCachedBlocks.Add(new MaterialPropertyBlock());
                stealthCachedAlphas.Add(GetRendererAlpha(renderer));
            }

            if (!alphaSupportChecked)
            {
                alphaSupported = PhantomWitchPerformancePolicy.SupportsAlphaModulation(stealthCachedRenderers);
                alphaSupportChecked = true;
            }
        }

        private void RestoreStealthVisuals()
        {
            if (activeSemiStealthEffect != null)
            {
                UnityEngine.Object.Destroy(activeSemiStealthEffect);
                activeSemiStealthEffect = null;
            }

            for (int i = 0; i < stealthCachedRenderers.Count; i++)
            {
                Renderer renderer = stealthCachedRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = true;
                float alpha = i < stealthCachedAlphas.Count ? stealthCachedAlphas[i] : 1f;
                MaterialPropertyBlock block = i < stealthCachedBlocks.Count ? stealthCachedBlocks[i] : null;
                SetRendererAlpha(renderer, block, alpha);
            }
        }

        private float GetRendererAlpha(Renderer renderer)
        {
            if (renderer == null)
            {
                return 1f;
            }

            return PhantomWitchFxRenderUtil.GetRendererColor(renderer, null).a;
        }

        private void SetRendererAlpha(Renderer renderer, MaterialPropertyBlock block, float alpha)
        {
            if (renderer == null)
            {
                return;
            }

            Color color = PhantomWitchFxRenderUtil.GetRendererColor(renderer, block);
            color.a = alpha;
            PhantomWitchFxRenderUtil.SetRendererColor(renderer, block, color);
        }

        private IEnumerator RunBodySinkWindup(float duration, float depth)
        {
            if (bossCharacter == null)
            {
                yield return new WaitForSeconds(duration);
                yield break;
            }

            Transform bossTf = bossCharacter.transform;
            Vector3 basePosition = bossTf.position;
            float safeDuration = Mathf.Max(duration, 0.01f);
            float safeDepth = Mathf.Max(0f, depth);
            float elapsed = 0f;

            while (elapsed < safeDuration)
            {
                if (bossCharacter == null)
                {
                    yield break;
                }

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / safeDuration);
                float sink = Mathf.Sin(t * Mathf.PI) * safeDepth;
                Vector3 nextPosition = basePosition;
                nextPosition.y = basePosition.y - sink;
                bossTf.position = nextPosition;
                yield return null;
            }

            if (bossCharacter != null)
            {
                bossTf.position = basePosition;
            }
        }

        private IEnumerator ExecuteBlink()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            LogSkillState("Blink", "before PauseAI", target);

            if (target != null)
            {
                yield return ExecuteTrackedTeleportStrike(target);
            }
            else
            {
                PauseAI();
                Vector3 targetPos = ResolveTeleportPosition(target,
                    PhantomWitchConfig.BlinkMinDistance,
                    PhantomWitchConfig.BlinkMaxDistance);
                ModBehaviour.DevLog("[PhantomWitch] [Blink] resolved teleport targetPos=" + targetPos);
                yield return TeleportTo(targetPos);
                FaceTarget(target);
            }
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
            TrackEffect(PhantomWitchAssetManager.CreateChannelChargeEffect(
                bossCharacter.transform.position,
                PhantomWitchConfig.CurseAuraRadius * 0.45f,
                PhantomWitchConfig.CurseAuraWindup,
                false));
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
            TrackEffect(PhantomWitchAssetManager.CreateChannelChargeEffect(
                bossCharacter.transform.position + bossCharacter.transform.forward * 0.4f,
                0.85f,
                PhantomWitchConfig.ScytheSweepWindup,
                false));
            yield return waitScytheSweepWindup;

            if (!CanContinueAttacking())
            {
                LogSkillState("ScytheSweep", "CanContinueAttacking=false, before ResumeAI", target);
                ResumeAI(target);
                yield break;
            }

            yield return ExecuteImmediateScytheSweep(target);

            yield return waitScytheSweepRecovery;
            LogSkillState("ScytheSweep", "before ResumeAI", target);
            ResumeAI(target);
        }

        private IEnumerator ExecuteImmediateScytheSweep(CharacterMainControl target)
        {
            if (!CanContinueAttacking() || bossCharacter == null)
            {
                yield break;
            }

            FaceTarget(target);
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
            yield break;
        }

        private IEnumerator ExecuteTrackedTeleportStrike(CharacterMainControl target)
        {
            if (target == null)
            {
                yield break;
            }

            PauseAI();
            SetStealthMode(PhantomWitchStealthMode.SemiStealthWindup);

            GameObject markerEffect = null;
            float telegraphStartedAt = Time.time;
            float markerDuration = PhantomWitchConfig.BlinkTrackedMarkerFxDuration;

            while (Time.time - telegraphStartedAt < PhantomWitchConfig.BlinkTrackedTelegraphDuration)
            {
                if (!CanContinueAttacking() || target == null || target.Health == null || target.Health.IsDead)
                {
                    SetStealthMode(PhantomWitchStealthMode.Visible);
                    yield break;
                }

                Vector3 currentTargetPosition = target.transform.position;
                Vector3 trackedTeleportPos = ResolveTrackedTeleportStrikePosition(target);
                if (markerEffect == null)
                {
                    markerEffect = PhantomWitchAssetManager.CreateTrackedTeleportMarkerEffect(trackedTeleportPos, markerDuration);
                    if (markerEffect != null)
                    {
                        TrackEffect(markerEffect);
                    }
                }
                else
                {
                    markerEffect.transform.position = trackedTeleportPos;
                }

                if (currentTargetPosition.y != trackedTeleportPos.y && markerEffect != null)
                {
                    markerEffect.transform.position = new Vector3(
                        markerEffect.transform.position.x,
                        currentTargetPosition.y,
                        markerEffect.transform.position.z);
                }

                yield return null;
            }

            if (!CanContinueAttacking() || target == null || target.Health == null || target.Health.IsDead)
            {
                SetStealthMode(PhantomWitchStealthMode.Visible);
                yield break;
            }

            Vector3 teleportPos = ResolveTrackedTeleportStrikePosition(target);
            if (markerEffect != null)
            {
                markerEffect.transform.position = teleportPos;
            }

            TrackEffect(PhantomWitchAssetManager.CreateTrackedTeleportFlashEffect(teleportPos));
            yield return TeleportTo(teleportPos);
            FaceTarget(target);
            SetStealthMode(PhantomWitchStealthMode.Visible);
            yield return ExecuteImmediateScytheSweep(target);
        }

        private IEnumerator ExecuteHeavyScytheSlash()
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            LogSkillState("HeavyScytheSlash", "before PauseAI", target);

            PauseAI();
            if (target != null && GetFlatDistance(bossCharacter.transform.position, target.transform.position) >
                PhantomWitchConfig.HeavyScytheSlashRadius + 0.45f)
            {
                Vector3 targetPos = ResolveTeleportPosition(
                    target,
                    PhantomWitchConfig.HeavySlashBlinkMinDistance,
                    PhantomWitchConfig.HeavySlashBlinkMaxDistance);
                ModBehaviour.DevLog("[PhantomWitch] [HeavyScytheSlash] resolved teleport targetPos=" + targetPos);

                yield return TeleportTo(targetPos);
            }

            FaceTarget(target);

            TrackEffect(PhantomWitchAssetManager.CreateChannelChargeEffect(
                bossCharacter.transform.position,
                PhantomWitchConfig.HeavyScytheSlashRadius * 0.45f,
                PhantomWitchConfig.HeavyScytheSlashWindup,
                true));
            yield return waitHeavySlashWindup;

            if (!CanContinueAttacking())
            {
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

            yield return waitHeavySlashRecovery;
            LogSkillState("HeavyScytheSlash", "before ResumeAI", target);
            ResumeAI(target);
        }

        private IEnumerator ExecuteCurseRealm()
        {
            yield return ExecuteTelegraphedCurseRealm(1f, 1f);
        }

        private IEnumerator ExecuteSummonMinions()
        {
            yield return SpawnMinionPair();
        }

        private async UniTask SpawnMinion(int index, int totalCount, PhantomWitchMinionRole role)
        {
            if (bossCharacter == null)
            {
                pendingMinionRoles.Remove(role);
                return;
            }

            try
            {
                Vector3 lateralOffset = role == PhantomWitchMinionRole.Sustain
                    ? -bossCharacter.transform.right
                    : bossCharacter.transform.right;
                if (lateralOffset.sqrMagnitude < 0.001f)
                {
                    lateralOffset = index == 0 ? Vector3.left : Vector3.right;
                }

                Vector3 spawnPos = bossCharacter.transform.position +
                    lateralOffset.normalized * PhantomWitchConfig.MinionSpawnDistance;

                spawnPos = SampleNavMeshOrFallback(spawnPos, bossCharacter.transform.position);
                TrackEffect(PhantomWitchAssetManager.CreateMinionSpawnEffect(spawnPos));

                CharacterRandomPreset minionPreset = GetCachedMinionPreset();
                if (minionPreset == null)
                {
                    ModBehaviour.DevLog("[PhantomWitch] [WARNING] 未找到合适的随从预设");
                    pendingMinionRoles.Remove(role);
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
                    pendingMinionRoles.Remove(role);
                    return;
                }

                if (bossCharacter == null || CurrentPhase == PhantomWitchPhase.Dead)
                {
                    CleanupSpawnedMinion(minion);
                    return;
                }

                FinalizeSpawnedMinion(minion, index);
                AssignMinionRole(minion, role);
                ModBehaviour.DevLog("[PhantomWitch] 随从 " + index + " 已召唤");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [ERROR] 生成随从失败: " + e.Message);
            }
            finally
            {
                pendingMinionRoles.Remove(role);
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
                    liveMinions.RemoveAll(delegate(MinionEntry entry)
                    {
                        return entry == null || entry.character == null || entry.character == deadChar;
                    });
                    EmitTelemetry("minion_death", "liveCount=" + CountLiveMinions() + ",name=" + deadChar.name);
                    if (CurrentPhase == PhantomWitchPhase.Phase3 && !minionFirstClearedLogged)
                    {
                        minionFirstClearedLogged = true;
                        EmitTelemetry("minion_first_cleared",
                            "minionCleared=true,playerHPRatio=" + GetPlayerHealthRatio().ToString("0.00"));
                    }
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
            TickStealthTelemetry();
            TickPhase3MotionSnapshot();
            TickMinionCensusTelemetry();

            if (CurrentPhase != PhantomWitchPhase.Phase3 || bossHealth == null || bossHealth.IsDead)
            {
                return;
            }

            TickMinionHealBonus();
            TickHarassMinionPressure();
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
            float healPerSecond = 0f;
            for (int i = liveMinions.Count - 1; i >= 0; i--)
            {
                MinionEntry entry = liveMinions[i];
                CharacterMainControl minion = entry != null ? entry.character : null;
                if (minion == null || minion.Health == null || minion.Health.IsDead)
                {
                    liveMinions.RemoveAt(i);
                    continue;
                }

                if (entry.role == PhantomWitchMinionRole.Sustain)
                {
                    healPerSecond += PhantomWitchConfig.SustainMinionHealRate;
                    if (GetFlatDistance(minion.transform.position, bossCharacter.transform.position) <= PhantomWitchConfig.SustainProximityRadius)
                    {
                        healPerSecond += PhantomWitchConfig.SustainMinionHealRate * (PhantomWitchConfig.SustainProximityBonusMultiplier - 1f);
                    }
                }
            }

            if (healPerSecond > 0f)
            {
                bossHealth.AddHealth(healPerSecond * Time.deltaTime);
            }
        }

        private void TickStealthTelemetry()
        {
            if (CurrentPhase == PhantomWitchPhase.Transitioning || CurrentPhase == PhantomWitchPhase.Dead)
            {
                return;
            }

            int phaseIndex = GetPhaseTelemetryIndex(CurrentPhase);
            switch (currentStealthMode)
            {
                case PhantomWitchStealthMode.TrueStealthTransition:
                    telemetryTrueStealthSec += Time.deltaTime;
                    if (phaseIndex >= 0)
                    {
                        phaseTrueStealthSeconds[phaseIndex] += Time.deltaTime;
                    }
                    break;
                case PhantomWitchStealthMode.SemiStealthWindup:
                    telemetrySemiStealthSec += Time.deltaTime;
                    if (phaseIndex >= 0)
                    {
                        phaseSemiStealthSeconds[phaseIndex] += Time.deltaTime;
                    }
                    break;
                default:
                    telemetryVisibleSec += Time.deltaTime;
                    if (phaseIndex >= 0)
                    {
                        phaseVisibleSeconds[phaseIndex] += Time.deltaTime;
                    }
                    break;
            }

            if (currentStealthMode == PhantomWitchStealthMode.TrueStealthTransition &&
                Time.time - stealthModeEnteredAt >= PhantomWitchConfig.TrueStealthMaxDuration)
            {
                stealthTimeoutCount++;
                EmitTelemetry("stealth_timeout", "packageType=" + currentTelemetryPackageType + ",phase=" + CurrentPhase);
                SetStealthMode(PhantomWitchStealthMode.Visible, "timeout");
            }
        }

        private void TickPhase3MotionSnapshot()
        {
            if (CurrentPhase != PhantomWitchPhase.Phase3)
            {
                return;
            }

            if (phase3NextMotionSnapshotTime < 0f)
            {
                phase3NextMotionSnapshotTime = Time.time + 5f;
                return;
            }

            if (Time.time < phase3NextMotionSnapshotTime)
            {
                return;
            }

            float avgTeleportDistance = phase3TeleportCount > 0 ? phase3TeleportDistance / phase3TeleportCount : 0f;
            float phaseDuration = GetPhaseDurationSeconds(PhantomWitchPhase.Phase3);
            float teleportsPerMin = phaseDuration > 0.01f ? (phase3TeleportCount / phaseDuration) * 60f : 0f;
            float avgPackageInterval = phasePackageCounts[2] > 0 ? phasePackageDurationSeconds[2] / phasePackageCounts[2] : 0f;

            EmitTelemetry("phase3_motion_snapshot",
                "avgTeleportDistance=" + avgTeleportDistance.ToString("0.00")
                + ",teleportsPerMin=" + teleportsPerMin.ToString("0.00")
                + ",avgPackageInterval=" + avgPackageInterval.ToString("0.00"));
            phase3NextMotionSnapshotTime = Time.time + 5f;
        }

        private void TickMinionCensusTelemetry()
        {
            if (CurrentPhase == PhantomWitchPhase.Dead || CurrentPhase == PhantomWitchPhase.Transitioning)
            {
                return;
            }

            if (nextMinionCensusTime < 0f)
            {
                nextMinionCensusTime = Time.time + PhantomWitchConfig.MinionCensusInterval;
                return;
            }

            if (Time.time < nextMinionCensusTime)
            {
                return;
            }

            nextMinionCensusTime = Time.time + PhantomWitchConfig.MinionCensusInterval;
            EmitTelemetry("minion_census",
                "liveCount=" + CountLiveMinions()
                + ",roles=[" + DescribeCurrentLiveRoles() + "]"
                + ",phase=" + CurrentPhase);
        }

        private void TickHarassMinionPressure()
        {
            if (Time.time < nextHarassMinionPressureTime)
            {
                return;
            }

            CharacterMainControl target;
            if (!TryResolveCombatTarget(out target) || target == null || target.Health == null || target.Health.IsDead)
            {
                return;
            }

            Buff curseBuff = PhantomWitchAssetManager.GetCurseBuff();
            if (curseBuff == null)
            {
                return;
            }

            for (int i = liveMinions.Count - 1; i >= 0; i--)
            {
                MinionEntry entry = liveMinions[i];
                CharacterMainControl minion = entry != null ? entry.character : null;
                if (minion == null || minion.Health == null || minion.Health.IsDead)
                {
                    liveMinions.RemoveAt(i);
                    continue;
                }

                if (entry.role != PhantomWitchMinionRole.Harass)
                {
                    continue;
                }

                if (GetFlatDistance(minion.transform.position, target.transform.position) > PhantomWitchConfig.HarassMinionPressureRadius)
                {
                    continue;
                }

                try
                {
                    target.AddBuff(curseBuff, bossCharacter, GetCurrentWeaponTypeId());
                    PhantomWitchCurseSweatVfx.TryAttach(target.gameObject);
                    EmitTelemetry("minion_harass_pulse",
                        "player=" + DescribeCharacter(target)
                        + ",radius=" + PhantomWitchConfig.HarassMinionPressureRadius.ToString("0.0"));
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[PhantomWitch] [WARNING] Harass minion pulse failed: " + e.Message);
                }

                nextHarassMinionPressureTime = Time.time + PhantomWitchConfig.HarassMinionPressureInterval;
                return;
            }
        }

        private void IncrementPhasePackageCount(PhantomWitchPhase phase)
        {
            int phaseIndex = GetPhaseTelemetryIndex(phase);
            if (phaseIndex < 0)
            {
                return;
            }

            phasePackageCounts[phaseIndex]++;
        }

        private int GetPhaseTelemetryIndex(PhantomWitchPhase phase)
        {
            switch (phase)
            {
                case PhantomWitchPhase.Phase1: return 0;
                case PhantomWitchPhase.Phase2: return 1;
                case PhantomWitchPhase.Phase3: return 2;
                default: return -1;
            }
        }

        private float GetPhaseDurationSeconds(PhantomWitchPhase phase)
        {
            int phaseIndex = GetPhaseTelemetryIndex(phase);
            if (phaseIndex < 0)
            {
                return 0f;
            }

            return phaseTrueStealthSeconds[phaseIndex] +
                   phaseSemiStealthSeconds[phaseIndex] +
                   phaseVisibleSeconds[phaseIndex];
        }

        private void EmitStealthRatioSnapshot(PhantomWitchPhase phase)
        {
            int phaseIndex = GetPhaseTelemetryIndex(phase);
            if (phaseIndex < 0)
            {
                return;
            }

            float total = GetPhaseDurationSeconds(phase);
            float ratio = total > 0.01f
                ? (phaseTrueStealthSeconds[phaseIndex] + phaseSemiStealthSeconds[phaseIndex]) / total
                : 0f;

            EmitTelemetry("stealth_ratio_snapshot",
                "phase=" + phase
                + ",trueStealthSec=" + phaseTrueStealthSeconds[phaseIndex].ToString("0.00")
                + ",semiStealthSec=" + phaseSemiStealthSeconds[phaseIndex].ToString("0.00")
                + ",visibleSec=" + phaseVisibleSeconds[phaseIndex].ToString("0.00")
                + ",ratio=" + ratio.ToString("0.00"));
        }

        private int CountLiveMinions()
        {
            int count = 0;
            for (int i = liveMinions.Count - 1; i >= 0; i--)
            {
                MinionEntry entry = liveMinions[i];
                CharacterMainControl minion = entry != null ? entry.character : null;
                if (minion == null || minion.Health == null || minion.Health.IsDead)
                {
                    liveMinions.RemoveAt(i);
                    continue;
                }

                count++;
            }

            return count;
        }

        private int CountOccupiedMinionSlots()
        {
            return CountLiveMinions() + pendingMinionRoles.Count;
        }

        private bool HasRoleAlive(PhantomWitchMinionRole role)
        {
            for (int i = liveMinions.Count - 1; i >= 0; i--)
            {
                MinionEntry entry = liveMinions[i];
                if (entry == null || entry.character == null || entry.character.Health == null || entry.character.Health.IsDead)
                {
                    liveMinions.RemoveAt(i);
                    continue;
                }

                if (entry.role == role)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasRoleReservedOrAlive(PhantomWitchMinionRole role)
        {
            return pendingMinionRoles.Contains(role) || HasRoleAlive(role);
        }

        private IEnumerator SpawnMinionPair()
        {
            int count = CountOccupiedMinionSlots();
            if (count >= PhantomWitchConfig.MaxMinions)
            {
                EmitTelemetry("minion_census", "liveCount=" + count + ",reason=skip_spawn");
                yield break;
            }

            List<PhantomWitchMinionRole> missingRoles = new List<PhantomWitchMinionRole>(2);
            if (!HasRoleReservedOrAlive(PhantomWitchMinionRole.Sustain))
            {
                missingRoles.Add(PhantomWitchMinionRole.Sustain);
            }

            if (!HasRoleReservedOrAlive(PhantomWitchMinionRole.Harass))
            {
                missingRoles.Add(PhantomWitchMinionRole.Harass);
            }

            if (missingRoles.Count == 0)
            {
                yield break;
            }

            if (count == 1 && missingRoles.Count != 1)
            {
                minionRosterDesyncCount++;
                EmitTelemetry("minion_roster_desync", "liveCount=" + count + ",rolesSeen=" + DescribeCurrentLiveRoles());
            }

            int totalCount = Mathf.Min(PhantomWitchConfig.PairFillSpawnsPerPackage, missingRoles.Count);
            for (int i = 0; i < totalCount; i++)
            {
                if (!pendingMinionRoles.Add(missingRoles[i]))
                {
                    continue;
                }

                SpawnMinion(i, totalCount, missingRoles[i]).Forget();
                if (i < totalCount - 1)
                {
                    yield return new WaitForSeconds(PhantomWitchConfig.MinionPairFrameGap);
                }
            }

            EmitTelemetry("minion_spawn", "liveCount=" + CountLiveMinions() + ",requested=" + totalCount);
            yield return waitSummonRecovery;
        }

        private void AssignMinionRole(CharacterMainControl minion, PhantomWitchMinionRole role)
        {
            if (minion == null)
            {
                return;
            }

            liveMinions.RemoveAll(delegate(MinionEntry entry)
            {
                return entry == null || entry.character == null || entry.character == minion;
            });
            liveMinions.Add(new MinionEntry
            {
                character = minion,
                role = role,
                spawnTimeGameSec = Time.time
            });
            minionTotalSpawned++;
            minionMaxConcurrent = Mathf.Max(minionMaxConcurrent, CountLiveMinions());
            if (role == PhantomWitchMinionRole.Sustain)
            {
                sawSustainMinion = true;
            }
            else if (role == PhantomWitchMinionRole.Harass)
            {
                sawHarassMinion = true;
            }
            EmitTelemetry("minion_spawn", "liveCount=" + CountLiveMinions() + ",role=" + role + ",name=" + minion.name);
        }

        private CharacterMainControl GetPreferredRetreatMinion()
        {
            for (int i = 0; i < liveMinions.Count; i++)
            {
                MinionEntry entry = liveMinions[i];
                if (entry != null && entry.character != null && entry.character.Health != null && !entry.character.Health.IsDead &&
                    entry.role == PhantomWitchMinionRole.Sustain)
                {
                    return entry.character;
                }
            }

            for (int i = 0; i < liveMinions.Count; i++)
            {
                MinionEntry entry = liveMinions[i];
                if (entry != null && entry.character != null && entry.character.Health != null && !entry.character.Health.IsDead)
                {
                    return entry.character;
                }
            }

            return null;
        }

        private void CheckPhaseTransition()
        {
            if (bossHealth == null || bossHealth.MaxHealth <= 0f || CurrentPhase == PhantomWitchPhase.Transitioning || CurrentPhase == PhantomWitchPhase.Dead)
            {
                return;
            }

            float healthPercent = bossHealth.CurrentHealth / bossHealth.MaxHealth;
            if (CurrentPhase == PhantomWitchPhase.Phase1 && healthPercent <= PhantomWitchConfig.Phase2HealthThreshold)
            {
                BeginPhaseTransition(PhantomWitchPhase.Phase2);
            }
            else if (CurrentPhase == PhantomWitchPhase.Phase2 && healthPercent <= PhantomWitchConfig.Phase3HealthThreshold)
            {
                BeginPhaseTransition(PhantomWitchPhase.Phase3);
            }
        }

        private void BeginPhaseTransition(PhantomWitchPhase nextPhase)
        {
            if (CurrentPhase == PhantomWitchPhase.Transitioning || CurrentPhase == nextPhase || CurrentPhase == PhantomWitchPhase.Dead)
            {
                return;
            }

            pendingTransitionTargetPhase = nextPhase;
            StartCoroutine(RunPhaseTransition());
        }

        private IEnumerator RunPhaseTransition()
        {
            ModBehaviour.DevLog("[PhantomWitch] 触发阶段转换 -> " + pendingTransitionTargetPhase + " | boss=" + DescribeBossState());
            EmitStealthRatioSnapshot(CurrentPhase);
            CurrentPhase = PhantomWitchPhase.Transitioning;

            if (currentAttackCoroutine != null)
            {
                ModBehaviour.DevLog("[PhantomWitch] [Phase] 停止当前 package 协程以进入新阶段");
                StopCoroutine(currentAttackCoroutine);
                currentAttackCoroutine = null;
            }

            LogSkillState("PhaseTransition", "before PauseAI", null);
            PauseAI();
            if (PhantomWitchConfig.PhaseTransitionClearsActiveRealm)
            {
                ClearActiveBossCurseRealm("phase_transition -> " + pendingTransitionTargetPhase);
            }

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
                    pendingTransitionTargetPhase == PhantomWitchPhase.Phase3
                        ? L10n.T(PhantomWitchConfig.Phase3MessageCN, PhantomWitchConfig.Phase3MessageEN)
                        : L10n.T(PhantomWitchConfig.Phase2MessageCN, PhantomWitchConfig.Phase2MessageEN));
            }

            yield return new WaitForSeconds(1.6f);

            CurrentPhase = pendingTransitionTargetPhase;
            nextMinionCensusTime = Time.time + PhantomWitchConfig.MinionCensusInterval;
            if (CurrentPhase == PhantomWitchPhase.Phase3)
            {
                phase3NextMotionSnapshotTime = Time.time + 5f;
                nextHarassMinionPressureTime = Time.time + PhantomWitchConfig.HarassMinionPressureInterval;
            }
            if (ambientPresence != null)
            {
                if (CurrentPhase == PhantomWitchPhase.Phase2)
                {
                    ambientPresence.SetPhase(PhantomWitchPhase.Phase2);
                }
                else if (CurrentPhase == PhantomWitchPhase.Phase3)
                {
                    ambientPresence.SetPhase(PhantomWitchPhase.Phase3);
                }
                else
                {
                    ambientPresence.SetPhase(CurrentPhase);
                }
            }
            currentPackageIndex = 0;
            LogSkillState("PhaseTransition", "before ResumeAI", target);
            ResumeAI(target);

            if (attackLoopCoroutine != null)
            {
                StopCoroutine(attackLoopCoroutine);
            }

            attackLoopCoroutine = StartCoroutine(AttackLoop());
            ModBehaviour.DevLog("[PhantomWitch] 阶段转换完成 -> " + CurrentPhase);
        }

        private IEnumerator ExecuteTelegraphedCurseRealm(float radiusScale, float durationScale)
        {
            CharacterMainControl target;
            TryResolveCombatTarget(out target);
            PauseAI();
            SetStealthMode(PhantomWitchStealthMode.SemiStealthWindup);

            Vector3 groundPoint = target != null
                ? target.transform.position
                : bossCharacter.transform.position + bossCharacter.transform.forward * 2f;
            groundPoint = SampleNavMeshOrFallback(groundPoint, bossCharacter.transform.position);

            float scaledRadius = PhantomWitchConfig.BossCurseRealmRadius * radiusScale;
            float warningStartedAt = Time.time;
            GameObject warningCircle = CreateCurseRealmWarningCircle(
                groundPoint,
                scaledRadius,
                PhantomWitchConfig.CurseRealmWarningDuration);
            if (warningCircle != null)
            {
                TrackEffect(warningCircle);
            }
            realmWarningCount++;
            EmitTelemetry("realm_warning_spawn", "origin=" + groundPoint + ",warningMs=" + (PhantomWitchConfig.CurseRealmWarningDuration * 1000f).ToString("0"));

            yield return new WaitForSeconds(PhantomWitchConfig.CurseRealmWarningDuration);

            if (!CanContinueAttacking())
            {
                SetStealthMode(PhantomWitchStealthMode.Visible);
                ResumeAI(target);
                yield break;
            }

            ClearActiveBossCurseRealm("refresh_before_commit");

            float radius = scaledRadius;
            float duration = PhantomWitchConfig.BossCurseRealmDuration * durationScale;
            try
            {
                GameObject host = new GameObject("PhantomWitch_BossCurseRealm");
                host.transform.position = groundPoint;
                PhantomWitchBossCurseRealmRuntime runtime = host.AddComponent<PhantomWitchBossCurseRealmRuntime>();
                runtime.Initialize(groundPoint, radius, duration, CurrentPhase);
                runtime.BindController(this);
                activeBossCurseRealm = runtime;
                TrackEffect(host);
                realmCommitCount++;
            }
            catch (Exception e)
            {
                realmMisfireCount++;
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] Boss curse realm commit failed: " + e.Message);
                EmitTelemetry("realm_clear", "reason=commit_failed");
                SetStealthMode(PhantomWitchStealthMode.Visible);
                ResumeAI(target);
                yield break;
            }
            SetStealthMode(PhantomWitchStealthMode.Visible);
            FaceTarget(target);
            EmitTelemetry("realm_commit",
                "origin=" + groundPoint
                + ",radius=" + radius.ToString("0.00")
                + ",phase=" + CurrentPhase
                + ",commitMs=" + Mathf.Max(0f, (Time.time - warningStartedAt) * 1000f).ToString("0"));
            ResumeAI(target);
        }

        private GameObject CreateCurseRealmWarningCircle(Vector3 groundPoint, float radius, float duration)
        {
            try
            {
                return PhantomWitchAssetManager.CreateCurseRealmWarningCircle(
                    groundPoint,
                    radius,
                    Mathf.Max(duration, 0.01f));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] CreateCurseRealmWarningCircle失败: " + e.Message);
                return null;
            }
        }

        private void ClearActiveBossCurseRealm(string reason)
        {
            if (activeBossCurseRealm == null)
            {
                return;
            }

            PhantomWitchBossCurseRealmRuntime runtime = activeBossCurseRealm;
            activeBossCurseRealm = null;
            if (reason.StartsWith("phase_transition", StringComparison.Ordinal))
            {
                realmForcedClearOnTransitionCount++;
            }
            runtime.ForceTerminate(reason);
            EmitTelemetry("realm_clear", "reason=" + reason);
        }

        public void OnBossDeath()
        {
            PhantomWitchPhase phaseAtDeath = CurrentPhase;
            CurrentPhase = PhantomWitchPhase.Dead;
            EmitStealthRatioSnapshot(phaseAtDeath);
            if (phaseAtDeath == PhantomWitchPhase.Phase3 && !bossFirstPhase3ExitLogged)
            {
                bossFirstPhase3ExitLogged = true;
                EmitTelemetry("boss_first_phase3_exit",
                    "minionCleared=" + minionFirstClearedLogged + ",playerHPRatio=" + GetPlayerHealthRatio().ToString("0.00"));
            }

            Health.OnDead -= OnAnyEntityDeath;
            StopAllCoroutines();
            ClearActiveBossCurseRealm("boss_death");
            RestoreVisibleState();
            CleanupMinions();
            CleanupAllEffects();
            ReleaseAssetReferenceIfNeeded();
            PrintTelemetrySummary();

            ModBehaviour.DevLog("[PhantomWitch] Boss死亡清理完成");

            // 控制器在独立 GO 上，需显式销毁
            Destroy(gameObject);
        }

        public void OnPlayerDeath()
        {
            PhantomWitchPhase phaseAtStop = CurrentPhase;
            CurrentPhase = PhantomWitchPhase.Dead;
            EmitStealthRatioSnapshot(phaseAtStop);
            Health.OnDead -= OnAnyEntityDeath;

            ModBehaviour.DevLog(
                "[PhantomWitch] 检测到玩家死亡，停止所有攻击"
                + " | cachedTarget=" + DescribeCharacter(playerCharacter)
                + " | boss=" + DescribeBossState());

            StopAllCoroutines();
            ClearActiveBossCurseRealm("player_death");
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
                if (ambientPresence != null)
                {
                    ambientPresence.Pause();
                }
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
                if (ambientPresence != null)
                {
                    ambientPresence.SetDetailLevel(PhantomWitchFxRuntime.CurrentDetailLevel);
                    ambientPresence.Resume();
                }
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
            PhantomWitchStealthMode preTeleportStealthMode = currentStealthMode;
            bool enteredTrueStealth = currentStealthMode != PhantomWitchStealthMode.TrueStealthTransition;
            if (enteredTrueStealth)
            {
                SetStealthMode(PhantomWitchStealthMode.TrueStealthTransition, "teleport");
            }
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
            if (enteredTrueStealth && currentStealthMode == PhantomWitchStealthMode.TrueStealthTransition)
            {
                SetStealthMode(preTeleportStealthMode, "teleport_complete");
            }
            if (CurrentPhase == PhantomWitchPhase.Phase3)
            {
                phase3TeleportDistance += Mathf.Sqrt(teleportDistanceSq);
                phase3TeleportCount++;
            }
            ModBehaviour.DevLog("[PhantomWitch] [Teleport] end | boss=" + DescribeBossState());
        }

        private void RestoreVisibleState()
        {
            SetStealthMode(PhantomWitchStealthMode.Visible);

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

        private Vector3 ResolveTrackedTeleportStrikePosition(CharacterMainControl target)
        {
            Vector3 fallback = bossCharacter != null
                ? bossCharacter.transform.position
                : spawnAnchorPosition;
            if (target == null)
            {
                return fallback;
            }

            Vector3 currentTargetPos = target.transform.position;
            Vector3 offsetDirection = fallback - currentTargetPos;
            offsetDirection.y = 0f;
            if (offsetDirection.sqrMagnitude < 0.0001f)
            {
                offsetDirection = -target.transform.forward;
                offsetDirection.y = 0f;
            }

            if (offsetDirection.sqrMagnitude < 0.0001f)
            {
                offsetDirection = Vector3.back;
            }

            Vector3 candidate = currentTargetPos + offsetDirection.normalized * PhantomWitchConfig.BlinkTrackedOffsetDistance;
            candidate.y = currentTargetPos.y;
            return SampleNavMeshOrFallback(candidate, currentTargetPos);
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
                        if (targetCharacter != null)
                        {
                            targetCharacter.AddBuff(curseBuff, bossCharacter, GetCurrentWeaponTypeId());
                        }
                        else
                        {
                            receiver.AddBuff(curseBuff, bossCharacter);
                        }
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
                currentPackageHadAttackLanded = true;
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

        internal int GetCurrentWeaponTypeIdForRealmRuntime()
        {
            return GetCurrentWeaponTypeId();
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
            liveMinions.Clear();
            pendingMinionRoles.Clear();
            ModBehaviour.DevLog("[PhantomWitch] 所有随从已清理");
        }

        private void CleanupSpawnedMinion(CharacterMainControl minion)
        {
            if (minion == null)
            {
                return;
            }

            summonedMinions.Remove(minion);
            liveMinions.RemoveAll(delegate(MinionEntry entry)
            {
                return entry == null || entry.character == null || entry.character == minion;
            });

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

        internal CharacterMainControl GetBossCharacterForRealmRuntime()
        {
            return bossCharacter;
        }

        internal void NotifyBossCurseRealmRuntimeEnded(PhantomWitchBossCurseRealmRuntime runtime, string reason)
        {
            if (runtime == null)
            {
                return;
            }

            if (activeBossCurseRealm == runtime)
            {
                activeBossCurseRealm = null;
            }

            EmitTelemetry("realm_clear", "reason=" + reason + ",phase=" + CurrentPhase);
        }

        private void EmitTelemetry(string eventName, string payloadKv)
        {
            if (!PhantomWitchConfig.TelemetryEnabled)
            {
                return;
            }

            ModBehaviour.DevLog("[PhantomWitchTelemetry] " + eventName + " | " + payloadKv);
        }

        private void PrintTelemetrySummary()
        {
            if (!PhantomWitchConfig.TelemetryEnabled)
            {
                return;
            }

            ModBehaviour.DevLog("[PhantomWitchTelemetry] === Phantom Witch Summary ===");
            string verdict = "PASS";
            for (int phaseIndex = 0; phaseIndex < 3; phaseIndex++)
            {
                float total = phaseTrueStealthSeconds[phaseIndex] + phaseSemiStealthSeconds[phaseIndex] + phaseVisibleSeconds[phaseIndex];
                float stealthRatio = total > 0.01f
                    ? (phaseTrueStealthSeconds[phaseIndex] + phaseSemiStealthSeconds[phaseIndex]) / total
                    : 0f;
                float target = phaseIndex == 0
                    ? PhantomWitchConfig.Phase1StealthRatioTarget
                    : (phaseIndex == 1 ? PhantomWitchConfig.Phase2StealthRatioTarget : PhantomWitchConfig.Phase3StealthRatioTarget);
                bool pass = Mathf.Abs(stealthRatio - target) <= PhantomWitchConfig.StealthRatioTolerance;
                if (!pass && verdict == "PASS")
                {
                    verdict = "WARN";
                }

                ModBehaviour.DevLog("[PhantomWitchTelemetry] P" + (phaseIndex + 1)
                    + " duration: " + total.ToString("0.0") + "s"
                    + " | stealth ratio: " + stealthRatio.ToString("0.00")
                    + " (target " + target.ToString("0.00") + " ±" + PhantomWitchConfig.StealthRatioTolerance.ToString("0.00") + ") "
                    + (pass ? "PASS" : "WARN"));
            }

            if (weaponTransformPosDriftMax > 0f || weaponTransformRotDriftMax > 0f || realmMisfireCount > 0 || minionRosterDesyncCount > 0)
            {
                verdict = "FAIL";
            }
            else if ((stealthTimeoutCount > 0 || wraithFallbackCount > 0) && verdict == "PASS")
            {
                verdict = "WARN";
            }

            ModBehaviour.DevLog("[PhantomWitchTelemetry] Packages fired: P1=" + phasePackageCounts[0] + " P2=" + phasePackageCounts[1] + " P3=" + phasePackageCounts[2]);
            ModBehaviour.DevLog("[PhantomWitchTelemetry] Realms: warnings=" + realmWarningCount
                + " commits=" + realmCommitCount
                + " forced_clears_on_transition=" + realmForcedClearOnTransitionCount
                + " misfires=" + realmMisfireCount);
            ModBehaviour.DevLog("[PhantomWitchTelemetry] Minions: max concurrent=" + minionMaxConcurrent
                + " totalSpawned=" + minionTotalSpawned
                + " rolesSeen=[" + DescribeLiveRoles() + "] desync=" + minionRosterDesyncCount);
            ModBehaviour.DevLog("[PhantomWitchTelemetry] Weapon transform drift: posMax=" + weaponTransformPosDriftMax.ToString("0.0000")
                + " rotMax=" + weaponTransformRotDriftMax.ToString("0.0000")
                + " " + ((weaponTransformPosDriftMax <= 0f && weaponTransformRotDriftMax <= 0f) ? "PASS" : "FAIL"));
            ModBehaviour.DevLog("[PhantomWitchTelemetry] Stealth timeouts=" + stealthTimeoutCount + " Wraith fallbacks=" + wraithFallbackCount);
            ModBehaviour.DevLog("[PhantomWitchTelemetry] Verdict: " + verdict);
        }

        private string DescribeLiveRoles()
        {
            List<string> roles = new List<string>(2);
            if (sawSustainMinion)
            {
                roles.Add(PhantomWitchMinionRole.Sustain.ToString());
            }
            if (sawHarassMinion)
            {
                roles.Add(PhantomWitchMinionRole.Harass.ToString());
            }

            return string.Join(",", roles.ToArray());
        }

        private string DescribeCurrentLiveRoles()
        {
            List<string> roles = new List<string>(2);
            for (int i = liveMinions.Count - 1; i >= 0; i--)
            {
                MinionEntry entry = liveMinions[i];
                if (entry == null || entry.character == null || entry.character.Health == null || entry.character.Health.IsDead)
                {
                    liveMinions.RemoveAt(i);
                    continue;
                }

                string roleName = entry.role.ToString();
                if (!roles.Contains(roleName))
                {
                    roles.Add(roleName);
                }
            }

            return string.Join(",", roles.ToArray());
        }

        private float GetPlayerHealthRatio()
        {
            if (playerCharacter == null || playerCharacter.Health == null || playerCharacter.Health.MaxHealth <= 0f)
            {
                return 0f;
            }

            return playerCharacter.Health.CurrentHealth / playerCharacter.Health.MaxHealth;
        }

        private void OnDisable()
        {
            RestoreStealthVisuals();
            stealthCachedRenderers.Clear();
            stealthCachedAlphas.Clear();
            stealthCachedBlocks.Clear();
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
            ClearActiveBossCurseRealm("controller_destroy");
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
