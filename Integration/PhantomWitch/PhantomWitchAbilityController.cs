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
using System.Reflection;
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
    public partial class PhantomWitchAbilityController : MonoBehaviour
    {
        private const float PlayerRefUpdateInterval = 0.1f;

        private static readonly Collider[] damageHitBuffer = new Collider[32];
        private static readonly WaitForSeconds wait05s = new WaitForSeconds(0.5f);
        private static readonly WaitForSeconds wait1s = new WaitForSeconds(1.0f);
        private static readonly WaitForSeconds waitBlinkHide = new WaitForSeconds(PhantomWitchConfig.BlinkHideDuration);
        private static readonly WaitForSeconds waitBlinkRecovery = new WaitForSeconds(PhantomWitchConfig.BlinkRecovery);
        private static readonly WaitForSeconds waitScytheSweepWindup = new WaitForSeconds(PhantomWitchConfig.ScytheSweepWindup);
        private static readonly WaitForSeconds waitScytheSweepRecovery = new WaitForSeconds(PhantomWitchConfig.ScytheSweepRecovery);
        private static readonly WaitForSeconds waitSummonWindup = new WaitForSeconds(PhantomWitchConfig.SummonWindup);
        private static readonly WaitForSeconds waitSummonRecovery = new WaitForSeconds(PhantomWitchConfig.SummonRecovery);
        private static readonly WaitForSeconds waitPhase1PackageInterval = new WaitForSeconds(PhantomWitchConfig.Phase1PackageInterval);
        private static readonly WaitForSeconds waitPhase2PackageInterval = new WaitForSeconds(PhantomWitchConfig.Phase2PackageInterval);
        private static readonly WaitForSeconds waitPhase3PackageInterval = new WaitForSeconds(PhantomWitchConfig.Phase3PackageInterval);
        private static FieldInfo attackAnimationHoldAgentField;
        private static bool attackAnimationHoldAgentFieldCached;

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
                if (inst != null && (inst.IsModeEActive || inst.IsModeFActive))
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
                            if (inst.IsModeFPreparationPhase && target == CharacterMainControl.Main)
                            {
                                playerCharacter = null;
                                return;
                            }

                            playerCharacter = target;
                            return;
                        }
                    }

                    if (inst.IsModeEActive || inst.IsModeFPreparationPhase)
                    {
                        playerCharacter = null;
                        return;
                    }
                }

                playerCharacter = CharacterMainControl.Main;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitch] [WARNING] UpdatePlayerReference异常: " + e.Message);
                var inst = ModBehaviour.Instance;
                playerCharacter = (inst != null && (inst.IsModeEActive || inst.IsModeFPreparationPhase))
                    ? null
                    : CharacterMainControl.Main;
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

    }
}
