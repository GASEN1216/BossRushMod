// ============================================================================
// FenHuangComboManager.cs - 焚皇断界戟三段连招管理器
// ============================================================================
// 模块说明：
//   1. MonoBehaviour 组件：管理三段连招状态（ComboStep / 超时重置）
//   2. Harmony Postfix Patch：在 CA_Attack.OnStart 后推进连招并生成挥击特效
//   3. Harmony Postfix Patch：在 ItemAgent_MeleeWeapon.CheckAndDealDamage 后
//      根据 ComboStep 施加不同效果（击退 / 灼烧 / 叠印记）
//
//   性能注意：
//   - 两个 Patch 都先检查武器 TypeID；非焚皇断界戟时立即返回，额外开销极低
//   - 不会影响其他近战武器
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Buffs;
using UnityEngine;
using HarmonyLib;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 焚皇断界戟三段连招状态管理器 (MonoBehaviour)
    /// </summary>
    public class FenHuangComboManager : MonoBehaviour
    {
        private sealed class ComboAttackState
        {
            public int Step;
            public float ExpireTime;
            public readonly HashSet<int> ProcessedTargetIds = new HashSet<int>();
        }
        // ========== 单例 ==========

        /// <summary>
        /// 全局实例（挂在常驻 GameObject 上）
        /// </summary>
        public static FenHuangComboManager Instance { get; private set; }

        // ========== 连招状态 ==========

        /// <summary>
        /// 当前连招段数（0=横扫，1=上挑，2=重劈）
        /// </summary>
        public int ComboStep { get; private set; }

        /// <summary>
        /// 上次攻击时间
        /// </summary>
        private float lastAttackTime = -999f;

        private readonly Dictionary<int, ComboAttackState> activeComboAttacks = new Dictionary<int, ComboAttackState>();
        private readonly List<int> expiredAttackers = new List<int>();

        private struct PendingHitEffect
        {
            public DamageReceiver receiver;
            public CharacterMainControl attacker;
            public int step;
            public int enqueueFrame;
        }

        private readonly Queue<PendingHitEffect> pendingHitEffects = new Queue<PendingHitEffect>();
        private bool hasPendingEffects;
        private const int MaxEffectsPerFrame = 2;

        private static Buff combo3BurnBuff;
        private static FieldInfo buffTotalLifeTimeField;
        private static FieldInfo buffLimitedLifeTimeField;
        private static PropertyInfo buffTotalLifeTimeProperty;
        private static PropertyInfo buffLimitedLifeTimeProperty;

        // ========== 生命周期 ==========

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            Health.OnHurt += OnFenHuangHalberdHurt;
        }

        void Update()
        {
            if (!ModBehaviour.CanRunGameplayRuntimeNow(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
            {
                return;
            }

            // 连招超时重置
            if (ComboStep > 0 && Time.time - lastAttackTime > FenHuangHalberdConfig.ComboWindowTime)
            {
                ComboStep = 0;
            }

            if (hasPendingEffects)
            {
                ProcessPendingHitEffects();
            }

            // 定期清理过期的龙焰印记
            CleanupExpiredAttacks();
            DragonFlameMarkTracker.CleanupExpired();
        }

        void OnDestroy()
        {
            Health.OnHurt -= OnFenHuangHalberdHurt;

            if (Instance == this)
            {
                Instance = null;
            }

            activeComboAttacks.Clear();
            pendingHitEffects.Clear();
            hasPendingEffects = false;
            DragonFlameMarkTracker.ClearAll();

            if (combo3BurnBuff != null)
            {
                Destroy(combo3BurnBuff);
                combo3BurnBuff = null;
            }
        }

        // ========== 连招推进 ==========

        /// <summary>
        /// 推进到下一段连招（由 Harmony Patch 在 CA_Attack.OnStart 后调用）
        /// </summary>
        public void AdvanceCombo()
        {
            lastAttackTime = Time.time;
            // combo 在 OnStart 触发时，先记录当前段数用于本次特效和附加效果
            // 然后再推进到下一段，形成 0 -> 1 -> 2 -> 0 的循环
            ComboStep = (ComboStep + 1) % 3;
        }

        /// <summary>
        /// 获取当前攻击段数（在 AdvanceCombo 之前读取，用于判定本次攻击生效的连招段）
        /// </summary>
        public void RegisterAttack(CharacterMainControl holder, int step)
        {
            if (holder == null)
            {
                return;
            }

            lastAttackTime = Time.time;

            int holderId = holder.GetInstanceID();
            ComboAttackState state;
            if (!activeComboAttacks.TryGetValue(holderId, out state))
            {
                state = new ComboAttackState();
                activeComboAttacks[holderId] = state;
            }

            state.Step = step;
            state.ExpireTime = Time.time + FenHuangHalberdConfig.ComboHitConfirmWindow;
            state.ProcessedTargetIds.Clear();
        }

        public bool TryGetActiveAttackStep(CharacterMainControl holder, out int step)
        {
            step = 0;
            if (holder == null)
            {
                return false;
            }

            ComboAttackState state;
            int holderId = holder.GetInstanceID();
            if (!activeComboAttacks.TryGetValue(holderId, out state))
            {
                return false;
            }

            if (Time.time >= state.ExpireTime)
            {
                activeComboAttacks.Remove(holderId);
                return false;
            }

            step = state.Step;
            return true;
        }

        public int GetCurrentAttackStep()
        {
            return ComboStep;
        }

        /// <summary>
        /// 重置连招状态
        /// </summary>
        public void ResetCombo()
        {
            ComboStep = 0;
            lastAttackTime = -999f;
            activeComboAttacks.Clear();
            pendingHitEffects.Clear();
            hasPendingEffects = false;
        }

        // ========== 场景切换清理 ==========

        /// <summary>
        /// 场景切换时调用，清空连招与印记状态
        /// </summary>
        public void OnSceneChanged()
        {
            ResetCombo();
            DragonFlameMarkTracker.ClearAll();
        }

        private void CleanupExpiredAttacks()
        {
            if (activeComboAttacks.Count == 0)
            {
                return;
            }

            expiredAttackers.Clear();

            foreach (var kvp in activeComboAttacks)
            {
                if (Time.time >= kvp.Value.ExpireTime)
                {
                    expiredAttackers.Add(kvp.Key);
                }
            }

            for (int i = 0; i < expiredAttackers.Count; i++)
            {
                activeComboAttacks.Remove(expiredAttackers[i]);
            }
        }

        private void ProcessPendingHitEffects()
        {
            int processed = 0;
            while (processed < MaxEffectsPerFrame && pendingHitEffects.Count > 0)
            {
                PendingHitEffect effect = pendingHitEffects.Peek();
                if (effect.enqueueFrame >= Time.frameCount)
                {
                    break;
                }

                pendingHitEffects.Dequeue();

                if (effect.receiver == null || effect.receiver.health == null || effect.receiver.health.IsDead)
                {
                    continue;
                }

                ApplyFireDamage(effect.receiver, effect.attacker);
                ApplyBurnBuff(effect.receiver, effect.attacker);

                if (effect.step == 1)
                {
                    ApplyLaunch(effect.receiver.transform, effect.attacker, FenHuangHalberdConfig.Combo2LaunchHeight);
                }
                else if (effect.step == 2)
                {
                    ApplyPull(effect.receiver.transform, effect.attacker, FenHuangHalberdConfig.Combo3PullDistance);
                }

                processed++;
            }

            hasPendingEffects = pendingHitEffects.Count > 0;
        }

        private void OnFenHuangHalberdHurt(Health health, DamageInfo damageInfo)
        {
            if (health == null)
            {
                return;
            }

            if (damageInfo.fromWeaponItemID != FenHuangHalberdIds.WeaponTypeId || damageInfo.isFromBuffOrEffect)
            {
                return;
            }

            CharacterMainControl attacker = damageInfo.fromCharacter;
            if (attacker == null)
            {
                return;
            }

            ComboAttackState state;
            int attackerId = attacker.GetInstanceID();
            if (!activeComboAttacks.TryGetValue(attackerId, out state))
            {
                return;
            }

            if (Time.time >= state.ExpireTime)
            {
                activeComboAttacks.Remove(attackerId);
                return;
            }

            DamageReceiver receiver = FenHuangHalberdRuntime.TryGetDamageReceiver(health);
            if (receiver == null)
            {
                return;
            }

            int receiverId = receiver.GetInstanceID();
            if (state.ProcessedTargetIds.Contains(receiverId))
            {
                return;
            }

            CharacterMainControl targetCharacter = health.TryGetCharacter();
            if (targetCharacter != null)
            {
                if (targetCharacter == attacker)
                {
                    return;
                }

                if (targetCharacter.Team == attacker.Team && attacker.Team != Teams.all)
                {
                    return;
                }
            }

            if (health.IsDead)
            {
                return;
            }

            state.ProcessedTargetIds.Add(receiverId);

            DragonFlameMarkTracker.AddMark(receiver);

            // 将附加效果（火伤/灼烧/击飞/拉扯）入队，延迟到后续帧批量处理
            pendingHitEffects.Enqueue(new PendingHitEffect
            {
                receiver = receiver,
                attacker = attacker,
                step = state.Step,
                enqueueFrame = Time.frameCount
            });
            hasPendingEffects = true;
        }

        internal static float GetComboConfiguredDamage(int step, float baseDamage)
        {
            switch (step)
            {
                case 0:
                    return baseDamage;
                case 1:
                    return baseDamage * 1.3f; // 1.3倍伤害
                case 2:
                    return baseDamage * 1.8f; // 1.8倍伤害并且有火
                default:
                    return baseDamage;
            }
        }

        internal static float GetComboConfiguredRange(int step, float currentRange)
        {
            return Mathf.Max(0.05f, currentRange + GetComboRangeOffset(step));
        }

        internal static float GetComboBaseRange(int step)
        {
            switch (step)
            {
                case 0:
                    return FenHuangHalberdConfig.Combo1Range;
                case 1:
                    return FenHuangHalberdConfig.Combo2Range;
                case 2:
                    return FenHuangHalberdConfig.Combo3Range;
                default:
                    return FenHuangHalberdConfig.BaseAttackRange;
            }
        }

        internal static float GetComboRangeOffset(int step)
        {
            switch (step)
            {
                case 0:
                    return FenHuangHalberdConfig.Combo1Range - FenHuangHalberdConfig.BaseAttackRange;
                case 1:
                    return FenHuangHalberdConfig.Combo2Range - FenHuangHalberdConfig.BaseAttackRange;
                case 2:
                    return FenHuangHalberdConfig.Combo3Range - FenHuangHalberdConfig.BaseAttackRange;
                default:
                    return 0f;
            }
        }

        private static void ApplyPull(Transform target, CharacterMainControl holder, float distance)
        {
            try
            {
                CharacterMainControl targetChar = target.GetComponentInParent<CharacterMainControl>();
                if (targetChar == null)
                {
                    return;
                }

                // 龙系 Boss 有独立的转阶段/锁血状态机，直接位移它们容易把状态机打坏。
                if (ShouldSuppressBossCrowdControl(targetChar))
                {
                    return;
                }

                // 方向：从目标指向玩家（拉扯）
                Vector3 pullDir = holder.transform.position - target.position;
                pullDir.y = 0f;
                pullDir.Normalize();

                if (FenHuangComboManager.Instance != null)
                {
                    FenHuangComboManager.Instance.StartCoroutine(
                        PullCoroutine(targetChar, distance, pullDir)
                    );
                }
            }
            catch
            {
            }
        }

        private static System.Collections.IEnumerator PullCoroutine(
            CharacterMainControl target, float distance, Vector3 direction)
        {
            if (target == null) yield break;

            float duration = 0.2f; // 快速拉扯
            float elapsed = 0f;
            Vector3 startPos = target.transform.position;

            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // 使用缓动函数，一开始快，后面慢（Ease Out）
                float easeT = 1f - Mathf.Pow(1f - t, 3f);
                float currentDist = distance * easeT;

                Vector3 newPos = startPos + direction * currentDist;

                try
                {
                    target.SetPosition(newPos);
                }
                catch
                {
                    target.transform.position = newPos;
                }

                yield return null;
            }

            if (target != null)
            {
                target.SetForceMoveVelocity(Vector3.zero);
            }
        }

        private static void ApplyLaunch(Transform target, CharacterMainControl holder, float height)
        {
            try
            {
                CharacterMainControl targetChar = target.GetComponentInParent<CharacterMainControl>();
                if (targetChar == null)
                {
                    return;
                }

                // 龙系 Boss 有独立的转阶段/锁血状态机，直接位移它们容易把状态机打坏。
                if (ShouldSuppressBossCrowdControl(targetChar))
                {
                    return;
                }

                // 尝试暂停敌人的地面约束，防止移动系统每帧把它拉回地面
                TryPauseEnemyGroundConstraint(targetChar, 0.8f);

                if (FenHuangComboManager.Instance != null)
                {
                    Vector3 knockDir = target.position - holder.transform.position;
                    knockDir.y = 0f;
                    knockDir.Normalize();

                    FenHuangComboManager.Instance.StartCoroutine(
                        LaunchCoroutine(targetChar, height, knockDir)
                    );
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 通过直接操纵位置实现挑飞效果（抛物线轨迹）
        /// </summary>
        private static System.Collections.IEnumerator LaunchCoroutine(
            CharacterMainControl target, float height, Vector3 horizontalDir)
        {
            if (target == null) yield break;

            float duration = 0.6f; // 挑飞总时长
            float elapsed = 0f;
            Vector3 startPos = target.transform.position;
            float horizontalDist = 1.0f; // 向前推一小段距离

            while (elapsed < duration && target != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // 抛物线：y = 4h * t * (1-t)，在 t=0.5 时达到最高点 h
                float yOffset = 4f * height * t * (1f - t);
                float xOffset = horizontalDist * t;

                Vector3 newPos = startPos + Vector3.up * yOffset + horizontalDir * xOffset;

                // 直接设置目标位置
                try
                {
                    target.SetPosition(newPos);
                }
                catch
                {
                    target.transform.position = newPos;
                }

                yield return null;
            }

            // 落地后清零速度
            if (target != null)
            {
                target.SetForceMoveVelocity(Vector3.zero);
            }
        }

        private static bool ShouldSuppressBossCrowdControl(CharacterMainControl targetChar)
        {
            if (targetChar == null)
            {
                return false;
            }

            try
            {
                if (targetChar.gameObject != null)
                {
                    string objectName = targetChar.gameObject.name;
                    if (!string.IsNullOrEmpty(objectName) &&
                        (objectName.Contains("DragonDescendant") || objectName.Contains("DragonKing")))
                    {
                        return true;
                    }
                }

                var preset = targetChar.characterPreset;
                if (preset != null)
                {
                    string nameKey = preset.nameKey;
                    if (nameKey == DragonDescendantConfig.BOSS_NAME_KEY ||
                        nameKey == DragonKingConfig.BossNameKey)
                    {
                        return true;
                    }
                }

                if (targetChar.GetComponent<DragonDescendantAbilityController>() != null ||
                    targetChar.GetComponent<DragonKingAbilityController>() != null)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        /// <summary>
        /// 通过反射暂停敌人的地面约束（缓存 MethodInfo 避免重复反射）
        /// </summary>
        private static MethodInfo cachedPauseGroundConstraintMethod;
        private static bool pauseMethodCached;

        private static void TryPauseEnemyGroundConstraint(CharacterMainControl character, float duration)
        {
            try
            {
                if (character == null || character.movementControl == null)
                {
                    return;
                }

                var movementControl = character.movementControl;

                if (!pauseMethodCached)
                {
                    cachedPauseGroundConstraintMethod = movementControl.GetType().GetMethod(
                        "PauseGroundConstraint",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    pauseMethodCached = true;
                }

                if (cachedPauseGroundConstraintMethod != null)
                {
                    cachedPauseGroundConstraintMethod.Invoke(movementControl, new object[] { duration });
                }
            }
            catch
            {
            }
        }

        private static void ApplyFireDamage(DamageReceiver receiver, CharacterMainControl fromCharacter)
        {
            try
            {
                if (receiver.health == null || receiver.health.IsDead)
                {
                    return;
                }

                DamageInfo fireDmg = new DamageInfo(fromCharacter);
                fireDmg.damageValue = FenHuangHalberdConfig.ComboFireDamageBonus;
                fireDmg.damageType = DamageTypes.normal;
                fireDmg.damagePoint = receiver.transform.position;
                fireDmg.isFromBuffOrEffect = true;
                fireDmg.AddElementFactor(ElementTypes.fire, 1f);
                receiver.health.Hurt(fireDmg);
            }
            catch
            {
            }
        }

        private static void ApplyBurnBuff(DamageReceiver receiver, CharacterMainControl fromCharacter)
        {
            try
            {
                Buff burnBuff = GetCombo3BurnBuff();
                if (burnBuff != null)
                {
                    receiver.AddBuff(burnBuff, fromCharacter);
                }
            }
            catch
            {
            }
        }

        private static Buff GetCombo3BurnBuff()
        {
            if (combo3BurnBuff != null)
            {
                return combo3BurnBuff;
            }

            Buff baseBurn = Duckov.Utilities.GameplayDataSettings.Buffs.Burn;
            if (baseBurn == null)
            {
                return null;
            }

            combo3BurnBuff = UnityEngine.Object.Instantiate(baseBurn);
            ApplyBuffLifetime(combo3BurnBuff, FenHuangHalberdConfig.ComboBurnDuration);
            return combo3BurnBuff;
        }

        private static void ApplyBuffLifetime(Buff buff, float duration)
        {
            if (buff == null)
            {
                return;
            }

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            if (buffTotalLifeTimeField == null)
            {
                buffTotalLifeTimeField = typeof(Buff).GetField("totalLifeTime", flags);
            }

            if (buffLimitedLifeTimeField == null)
            {
                buffLimitedLifeTimeField = typeof(Buff).GetField("limitedLifeTime", flags);
            }

            if (buffTotalLifeTimeProperty == null)
            {
                buffTotalLifeTimeProperty = typeof(Buff).GetProperty("TotalLifeTime", flags);
            }

            if (buffLimitedLifeTimeProperty == null)
            {
                buffLimitedLifeTimeProperty = typeof(Buff).GetProperty("LimitedLifeTime", flags);
            }

            try
            {
                if (buffTotalLifeTimeField != null)
                {
                    buffTotalLifeTimeField.SetValue(buff, duration);
                }
                else if (buffTotalLifeTimeProperty != null && buffTotalLifeTimeProperty.CanWrite)
                {
                    buffTotalLifeTimeProperty.SetValue(buff, duration, null);
                }
            }
            catch
            {
            }

            try
            {
                if (buffLimitedLifeTimeField != null)
                {
                    buffLimitedLifeTimeField.SetValue(buff, true);
                }
                else if (buffLimitedLifeTimeProperty != null && buffLimitedLifeTimeProperty.CanWrite)
                {
                    buffLimitedLifeTimeProperty.SetValue(buff, true, null);
                }
            }
            catch
            {
            }
        }
    }
}
