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

    // ========================================================================
    // Harmony Patches
    // ========================================================================

    /// <summary>
    /// Patch CA_Attack.OnStart
    /// 焚皇断界戟攻击开始时推进连招段数并生成挥击特效
    /// 性能：非焚皇断界戟时直接返回，避免影响其他武器
    /// </summary>
    [HarmonyPatch(typeof(CA_Attack), "OnStart")]
    public static class FenHuangComboAttackPatch
    {
        private static FieldInfo _holdAgentField;
        private static bool _holdAgentFieldCached;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        public static void Postfix(CA_Attack __instance, bool __result)
        {
            if (!__result)
            {
                return;
            }

            FenHuangComboManager combo = FenHuangComboManager.Instance;
            if (combo == null)
            {
                return;
            }

            try
            {
                CharacterMainControl character = __instance.characterController;
                if (character == null)
                {
                    return;
                }

                ItemAgent_MeleeWeapon melee = character.GetMeleeWeapon();
                if (melee == null || melee.Item == null)
                {
                    return;
                }

                if (melee.Item.TypeID != FenHuangHalberdIds.WeaponTypeId)
                {
                    return;
                }

                FenHuangHalberdWeaponConfig.EnsureMeleeAttackFx(melee);

                // 强制确保 CharacterAnimationControl 的 holdAgent 指向当前武器代理，
                // 并触发攻击动画。原版的事件链 CA_Attack.OnAttack → CharacterModel →
                // CharacterAnimationControl.OnAttack 依赖 holdAgent 缓存，
                // 但 holdAgent 只在为 null/destroyed 时才在 Update 中更新，
                // 如果 OnAttack 事件在同帧的 Update 之前触发，holdAgent 可能还未指向
                // 新的断界戟代理，导致 handAnimationType 检查失败，攻击动画不播放。
                ForceAttackAnimation(character);

                int effectStep = combo.ComboStep;
                combo.RegisterAttack(character, effectStep);
                combo.AdvanceCombo();
                SpawnSwingEffect(character, effectStep);
                ApplyAnimationTilt(character, effectStep);
            }
            catch
            {
            }
        }

        private static void ForceAttackAnimation(CharacterMainControl character)
        {
            try
            {
                if (character.characterModel == null)
                {
                    return;
                }

                CharacterAnimationControl animControl =
                    character.characterModel.GetComponentInChildren<CharacterAnimationControl>();
                if (animControl == null)
                {
                    return;
                }

                // 缓存反射字段
                if (!_holdAgentFieldCached)
                {
                    _holdAgentField = typeof(CharacterAnimationControl).GetField("holdAgent",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _holdAgentFieldCached = true;
                }

                // 强制将 holdAgent 更新为当前手持代理
                DuckovItemAgent currentAgent = character.CurrentHoldItemAgent;
                if (currentAgent != null && _holdAgentField != null)
                {
                    _holdAgentField.SetValue(animControl, currentAgent);
                }

                // 通过 CharacterModel 的公开方法触发攻击动画
                character.characterModel.ForcePlayAttackAnimation();
            }
            catch
            {
            }
        }

        private static void ApplyAnimationTilt(CharacterMainControl character, int step)
        {
            if (character == null || character.CurrentHoldItemAgent == null) return;
            
            // 每次攻击时附加动画补偿器
            FenHuangAnimationModifier modifier = character.CurrentHoldItemAgent.gameObject.GetComponent<FenHuangAnimationModifier>();
            if (modifier == null)
            {
                modifier = character.CurrentHoldItemAgent.gameObject.AddComponent<FenHuangAnimationModifier>();
            }

            // 不改模型根节点，只改变武器模型本地的空间扭曲
            Transform targetBone = character.CurrentHoldItemAgent.transform;

            if (targetBone == null)
            {
                return;
            }

            // 原始动画是水平挥击 (Yaw轴)
            // step 0: 正常横扫，无需倾斜
            if (step == 0)
            {
                modifier.ApplyTilt(targetBone, Vector3.zero, 0.25f);
            }
            // step 1: 变成垂直下劈 (原先是第三段)
            else if (step == 1)
            {
                modifier.ApplyTilt(targetBone, new Vector3(-80f, 0f, 60f), 0.35f);
            }
            // step 2: 变成右下到左上斜劈 (原先是第二段)
            else if (step == 2)
            {
                modifier.ApplyTilt(targetBone, new Vector3(-35f, 0f, -40f), 0.35f);
            }
        }

        private static void SpawnSwingEffect(CharacterMainControl character, int step)
        {
            try
            {
                float rangeScale = 1f;
                ItemAgent_MeleeWeapon melee = character != null ? character.GetMeleeWeapon() : null;
                if (melee != null)
                {
                    float comboRange = FenHuangComboManager.GetComboConfiguredRange(step, melee.AttackRange);
                    float baseComboRange = FenHuangComboManager.GetComboBaseRange(step);
                    if (baseComboRange > 0.01f)
                    {
                        rangeScale = Mathf.Max(0.2f, comboRange / baseComboRange);
                    }
                }

                // 固定特效生成中心点：人物坐标 + 高度偏移 + 向前少许偏移。
                // 彻底不依赖武器Socket的位置，因为武器会在不同段数被倾斜，Socket会乱跑
                // 保证三段式特效围绕统一个圆心爆发，视觉上才是一个完美的定点连招
                Vector3 forward = character.CurrentAimDirection;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = character.transform.forward;
                    forward.y = 0f;
                }
                forward.Normalize();

                // 统一的特效发生位置：胸口高度，稍微靠前
                Vector3 spawnPos = character.transform.position + Vector3.up * 1.1f + forward * 0.15f;
                // 第二段和第三段高度下降 1.1m（即贴地）
                if (step == 1 || step == 2)
                {
                    spawnPos.y -= 1.1f;
                }

                Quaternion rotation = Quaternion.LookRotation(forward);
                Vector3 currentTilt = Vector3.zero;
                
                // 将特效的角度跟随着武器段数的互换一起互换
                if (step == 1) currentTilt = new Vector3(-80f, 0f, 60f);       // 垂直下劈
                else if (step == 2) currentTilt = new Vector3(-35f, 0f, -40f); // 右下到左上
                
                // 将特效的整体旋转也加上这个倾斜角 (应用在局部空间)
                rotation = rotation * Quaternion.Euler(currentTilt);

                GameObject fx = new GameObject("FenHuang_SwingFX");
                fx.transform.position = spawnPos;
                fx.transform.rotation = rotation;

                FenHuangSwingFx swingFx = fx.AddComponent<FenHuangSwingFx>();
                swingFx.Initialize(step, rangeScale);
                UnityEngine.Object.Destroy(fx, 0.22f);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Patch ItemAgent_MeleeWeapon.CheckAndDealDamage
    /// 焚皇断界戟命中后根据当前连招段数施加附加效果
    /// 性能：非焚皇断界戟时直接返回，不影响其他近战武器
    /// </summary>
    [HarmonyPatch(typeof(ItemAgent_MeleeWeapon), "CheckAndDealDamage")]
    public static class FenHuangComboDamagePatch
    {
        private static readonly Collider[] halberdHitBuffer = new Collider[6];

        [HarmonyPrefix]
        public static bool Prefix(ItemAgent_MeleeWeapon __instance)
        {
            try
            {
                if (__instance == null) return true;
                Item item = __instance.Item;
                if (item == null || item.TypeID != FenHuangHalberdIds.WeaponTypeId) return true;

                CharacterMainControl holder = __instance.Holder;
                FenHuangComboManager combo = FenHuangComboManager.Instance;
                if (holder == null) return true;

                int step = 0;
                if (combo != null)
                {
                    combo.TryGetActiveAttackStep(holder, out step);
                }

                DealHalberdDamage(__instance, item, holder, step);
                return false;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FenHuangHalberd] 自定义近战判定失败，回退原版逻辑: " + e.Message);
                return true;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(ItemAgent_MeleeWeapon __instance)
        {
        }

        private static void DealHalberdDamage(ItemAgent_MeleeWeapon melee, Item item, CharacterMainControl holder, int step)
        {
            float comboDamage = FenHuangComboManager.GetComboConfiguredDamage(step, melee.Damage);
            float comboRange = FenHuangComboManager.GetComboConfiguredRange(step, melee.AttackRange);
            Vector3 aimDirection = GetFlatAimDirection(holder);
            ItemSetting_MeleeWeapon meleeSetting = item.GetComponent<ItemSetting_MeleeWeapon>();

            int hitCount = Physics.OverlapSphereNonAlloc(
                holder.transform.position,
                comboRange + 0.05f,
                halberdHitBuffer,
                Duckov.Utilities.GameplayDataSettings.Layers.damageReceiverLayerMask
            );

            float blockBullet = melee.BlockBullet;
            bool shouldBlockBullet = blockBullet > 0.5f;
            bool bulletBack = blockBullet > 1.5f;
            if (shouldBlockBullet)
            {
                BulletBlocker bulletBlocker = UnityEngine.Object.Instantiate<BulletBlocker>(
                    Duckov.Utilities.GameplayDataSettings.Prefabs.MeleeBulletBlocker,
                    holder.transform.position,
                    Quaternion.LookRotation(aimDirection, Vector3.up)
                );

                SphereCollider sphereCollider = bulletBlocker.GetComponent<SphereCollider>();
                if (sphereCollider != null)
                {
                    sphereCollider.radius = comboRange;
                }

                bulletBlocker.checkDirection = true;
                bulletBlocker.bulletBack = bulletBack;
                bulletBlocker.from = holder;
                bulletBlocker.team = holder.Team;
            }

            float damageReceiverRadius = 0f;
            if (holder.characterModel != null)
            {
                damageReceiverRadius = holder.characterModel.damageReceiverRadius;
            }

            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = halberdHitBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                DamageReceiver receiver = collider.GetComponent<DamageReceiver>();
                if (receiver == null || (holder.Team == receiver.Team && holder.Team != Teams.all))
                {
                    continue;
                }

                Health health = receiver.health;
                if (health != null)
                {
                    CharacterMainControl targetCharacter = health.TryGetCharacter();
                    if (targetCharacter == holder || (targetCharacter != null && targetCharacter.Dashing))
                    {
                        continue;
                    }
                }

                Vector3 hitDirection = collider.transform.position - holder.transform.position;
                hitDirection.y = 0f;
                float hitDistance = hitDirection.magnitude;
                if (hitDistance > 0.0001f)
                {
                    hitDirection /= hitDistance;
                }
                else
                {
                    hitDirection = aimDirection;
                }

                if (Vector3.Angle(hitDirection, aimDirection) >= 90f &&
                    hitDistance >= 0.5f + damageReceiverRadius)
                {
                    continue;
                }

                DamageInfo damageInfo = new DamageInfo(holder);
                damageInfo.damageValue = comboDamage * melee.CharacterDamageMultiplier;
                damageInfo.armorPiercing = melee.ArmorPiercing;
                damageInfo.critDamageFactor = melee.CritDamageFactor * (1f + melee.CharacterCritDamageGain);
                damageInfo.critRate = melee.CritRate * (1f + melee.CharacterCritRateGain);
                damageInfo.crit = -1;
                damageInfo.damageNormal = -holder.modelRoot.right;
                damageInfo.damagePoint = collider.transform.position - hitDirection * 0.2f;
                damageInfo.damagePoint.y = melee.transform.position.y;
                damageInfo.fromWeaponItemID = item.TypeID;
                damageInfo.bleedChance = melee.BleedChance;

                if (meleeSetting != null)
                {
                    damageInfo.isExplosion = meleeSetting.dealExplosionDamage;
                    damageInfo.elementFactors.Add(new ElementFactor(meleeSetting.element, 1f));
                    damageInfo.buff = meleeSetting.buff;
                    damageInfo.buffChance = meleeSetting.buffChance;
                }

                if (LevelManager.Instance != null && LevelManager.Instance.ControllingCharacter == holder)
                {
                    damageInfo.fromCharacter = CharacterMainControl.Main;
                }

                receiver.Hurt(damageInfo);
                receiver.AddBuff(Duckov.Utilities.GameplayDataSettings.Buffs.Pain, holder);

                if (melee.hitFx != null)
                {
                    UnityEngine.Object.Instantiate<GameObject>(
                        melee.hitFx,
                        damageInfo.damagePoint,
                        Quaternion.LookRotation(damageInfo.damageNormal, Vector3.up)
                    );
                }

                if (holder == CharacterMainControl.Main)
                {
                    Vector3 shakeDirection = holder.modelRoot.right;
                    shakeDirection += UnityEngine.Random.insideUnitSphere * 0.3f;
                    shakeDirection.Normalize();
                    CameraShaker.Shake(shakeDirection * 0.05f, CameraShaker.CameraShakeTypes.meleeAttackHit);
                }
            }
        }

        private static Vector3 GetFlatAimDirection(CharacterMainControl holder)
        {
            Vector3 aimDirection = holder.CurrentAimDirection;
            aimDirection.y = 0f;
            if (aimDirection.sqrMagnitude < 0.0001f)
            {
                aimDirection = holder.transform.forward;
                aimDirection.y = 0f;
            }

            if (aimDirection.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return aimDirection.normalized;
        }
    }

    // ========================================================================
    // Harmony Patch: 处理 ChangeHoldItem 生成的手持代理
    // ========================================================================
    // 某些情况下，ChangeHoldItem / CreateHandheldAgent 创建出的代理
    // 不会自动带上 ItemAgent_MeleeWeapon 组件，导致 _meleeRef 为空
    // 这里为焚皇断界戟补齐近战组件、Item 引用和相关挂点配置
    // ========================================================================
    // ========================================================================

    [HarmonyPatch(typeof(ItemAgentHolder), "ChangeHoldItem")]
    public static class FenHuangHoldItemPatch
    {
        // 反射字段缓存
        private static FieldInfo _meleeRefField;
        private static FieldInfo _gunRefField;
        private static FieldInfo _currentUsingSocketCacheField;
        private static FieldInfo _itemAgentItemField; // ItemAgent.item (private)
        private static bool _fieldsCached = false;

        [HarmonyPostfix]
        public static void Postfix(ItemAgentHolder __instance, DuckovItemAgent __result, Item item)
        {
            // 防御性判断：忽略空结果或空 Item
            if (__result == null || item == null) return;

            try
            {
                // 只处理焚皇断界戟
                if (item.TypeID != FenHuangHalberdIds.WeaponTypeId) return;

                ItemAgent_MeleeWeapon existingMelee = __result as ItemAgent_MeleeWeapon;
                if (existingMelee != null)
                {
                    FenHuangHalberdWeaponConfig.EnsureMeleeAttackFx(existingMelee);
                    return;
                }

                // 首次进入时缓存反射字段
                if (!_fieldsCached)
                {
                    _meleeRefField = typeof(ItemAgentHolder).GetField("_meleeRef",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _gunRefField = typeof(ItemAgentHolder).GetField("_gunRef",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _currentUsingSocketCacheField = typeof(ItemAgentHolder).GetField("_currentUsingSocketCache",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    // ItemAgent.item 是 private 字段，需要通过反射读取/写入
                    _itemAgentItemField = typeof(ItemStatsSystem.ItemAgent).GetField("item",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _fieldsCached = true;

                    ModBehaviour.DevLog($"[FenHuangHalberd] 反射字段缓存完成: _meleeRef={_meleeRefField != null}, _gunRef={_gunRefField != null}, usingSocket={_currentUsingSocketCacheField != null}, itemField={_itemAgentItemField != null}");
                }

                // 在返回的 GameObject 上补一个 ItemAgent_MeleeWeapon 组件
                GameObject agentGo = __result.gameObject;
                ItemAgent_MeleeWeapon meleeComp = agentGo.GetComponent<ItemAgent_MeleeWeapon>();
                if (meleeComp == null)
                {
                    meleeComp = agentGo.AddComponent<ItemAgent_MeleeWeapon>();
                }

                // 将原始 Item 引用写入新组件，否则近战代理无法识别对应武器
                // ItemAgent.item 是 private 字段，因此通过反射赋值
                if (_itemAgentItemField != null)
                {
                    _itemAgentItemField.SetValue(meleeComp, item);
                    ModBehaviour.DevLog($"[FenHuangHalberd] 已写入 Item 引用: {item.name} (TypeID={item.TypeID})");
                }
                else
                {
                    ModBehaviour.DevLog("[FenHuangHalberd] [WARNING] 无法找到 ItemAgent.item 字段");
                }

                // 同步近战挂点与动画类型
                meleeComp.handheldSocket = HandheldSocketTypes.normalHandheld;
                meleeComp.handAnimationType = HandheldAnimationType.meleeWeapon;
                FenHuangHalberdWeaponConfig.EnsureMeleeAttackFx(meleeComp);

                // 绑定 Holder，确保角色控制和归属关系正确
                meleeComp.SetHolder(__result.Holder);

                // 同步返回代理的挂点、动画类型和实际父节点，确保 CurrentHoldItemAgent 表现正确
                __result.handheldSocket = HandheldSocketTypes.normalHandheld;
                __result.handAnimationType = HandheldAnimationType.meleeWeapon;

                Transform meleeSocket = null;
                if (__instance.characterController != null && __instance.characterController.characterModel != null)
                {
                    // 原版近战武器挂在 RightHandSocket（受攻击动画骨骼驱动）
                    meleeSocket = __instance.characterController.characterModel.RightHandSocket;
                    if (meleeSocket == null)
                    {
                        meleeSocket = __instance.characterController.characterModel.MeleeWeaponSocket;
                    }
                }

                if (meleeSocket != null)
                {
                    __result.transform.SetParent(meleeSocket, false);
                    __result.transform.localPosition = Vector3.zero;
                    __result.transform.localRotation = Quaternion.identity;
                    _currentUsingSocketCacheField?.SetValue(__instance, meleeSocket);
                    ModBehaviour.DevLog("[FenHuangHalberd] 已同步 CurrentHoldItemAgent 的近战动画与挂点");
                }

                // 修正音效 key，避免播放异常
                try
                {
                    FieldInfo soundKeyField = typeof(ItemAgent_MeleeWeapon).GetField("soundKey",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                    if (soundKeyField != null)
                    {
                        soundKeyField.SetValue(meleeComp, "Default");
                    }
                }
                catch { }

                // 同步 _meleeRef，让 ItemAgentHolder 能正确拿到近战代理
                // 这样 currentHoldItemAgent 在后续逻辑里就能按近战武器处理
                // _meleeRef 是 ItemAgentHolder 内部引用，这里补上刚创建的 meleeComp
                if (_meleeRefField != null)
                {
                    _meleeRefField.SetValue(__instance, meleeComp);
                    ModBehaviour.DevLog("[FenHuangHalberd] 已同步 _meleeRef");
                }

                // 关键修复：清除 _gunRef，阻止枪械 IK 瞄准系统
                // 游戏在 ChangeHoldItem 内部已经通过 `as ItemAgent_Gun` 给 _gunRef 赋值了，
                // 这发生在我们的 Postfix 之前。如果不清除，IK 瞄准系统会让武器始终面向屏幕中心
                if (_gunRefField != null)
                {
                    _gunRefField.SetValue(__instance, null);
                    ModBehaviour.DevLog("[FenHuangHalberd] 已清除 _gunRef，防止枪械 IK 瞄准");
                }

                ModBehaviour.DevLog("[FenHuangHalberd] Injected ItemAgent_MeleeWeapon into handheld agent.");

                // 禁用运动模糊，并修复 Shader（替换 Standard/Lit 为游戏原生 SodaCharacter）
                try
                {
                    Renderer[] renderers = agentGo.GetComponentsInChildren<Renderer>(true);
                    Shader gameShader = Shader.Find("SodaCraft/SodaCharacter");
                    foreach (Renderer r in renderers)
                    {
                        if (r != null)
                        {
                            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                            r.enabled = true;
                            r.forceRenderingOff = false;

                            // 替换非原生 Shader，修复影子/渲染异常
                            if (gameShader != null && r.sharedMaterials != null)
                            {
                                foreach (Material mat in r.sharedMaterials)
                                {
                                    if (mat != null && mat.shader != null)
                                    {
                                        string shaderName = mat.shader.name;
                                        if (shaderName.Contains("Standard") ||
                                            shaderName.Contains("Lit") ||
                                            shaderName.Contains("Universal"))
                                        {
                                            mat.shader = gameShader;
                                            ModBehaviour.DevLog($"[FenHuangHalberd] 已将材质 {mat.name} 的 Shader 从 {shaderName} 替换为 {gameShader.name}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[FenHuangHalberd] 注入手持代理失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
    }
    public class FenHuangSwingFx : MonoBehaviour
    {
        private const float BaseTrailDistance = 1.5f;
        private const float MinTrailDistance = 0.35f;

        private float elapsed;
        private float duration = 0.2f;
        private Transform trailRoot;
        private GameObject blurObj; // 保存运动节点的引用，用于在 Update 中控制粒子
        private ParticleSystem[] injectedParticles;
        
        // Parameters for swing motion
        private float startAngle;
        private float sweepAngle;

        public void Initialize(int step, float rangeScale)
        {
            duration = 0.22f; // Matches Destroy delay
            Color color = GetStepColor(step);
            float clampedRangeScale = Mathf.Max(0.2f, rangeScale);
            float trailDistance = Mathf.Max(MinTrailDistance, BaseTrailDistance * clampedRangeScale);
            float sizeScale = Mathf.Lerp(1f, clampedRangeScale, 0.35f);
            float lightScale = Mathf.Lerp(1f, clampedRangeScale, 0.4f);

            // Determine sweep angles per combo step
            switch (step)
            {
                case 1: // Step 2: Heavy Chop (was Step 3)
                    startAngle = -60f;
                    sweepAngle = FenHuangHalberdConfig.Combo3Angle;
                    break;
                case 2: // Step 3: Uppercut (was Step 2)
                    startAngle = -45f;
                    sweepAngle = FenHuangHalberdConfig.Combo2Angle;
                    break;
                default: // Step 1: Sweep
                    startAngle = -75f;
                    sweepAngle = FenHuangHalberdConfig.Combo1Angle;
                    break;
            }

            if (trailRoot == null)
            {
                trailRoot = new GameObject("SwingTrailPivot").transform;
                trailRoot.SetParent(transform, false);
                trailRoot.localPosition = Vector3.zero;
                
                // Set initial rotation based on start angle
                trailRoot.localRotation = Quaternion.Euler(0f, startAngle, 0f);

                blurObj = new GameObject("TrailNode");
                blurObj.transform.SetParent(trailRoot, false);
                
                // 将拖尾特效节点稍微往后拉，也就是减小它相对角色前方的 Z 轴偏移。
                // 这样特效轨迹就会更加贴合武器的实际攻击判定范围，不会出现“火烧到怪了但没掉血”的情况
                blurObj.transform.localPosition = new Vector3(0f, 0f, trailDistance);

                // =========== 核心复用：使用龙息武器的火焰特效投射到挥击节点 ===========
                // DragonBreathWeaponConfig.TryAddFireEffectsToGraphic 会自动找到火 AK-47，
                // 并将 Smoke, Spark 和 SodaPointLight 复制并作为 blurObj 的子物体
                DragonBreathWeaponConfig.TryAddFireEffectsToGraphic(blurObj);

                // 修改粒子的模拟空间为 World，这样才能挥击出拖尾效果（而不是一坨火跟着转）
                // 并且将颜色染成对应的段数颜色
                injectedParticles = blurObj.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in injectedParticles)
                {
                    var main = ps.main;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    
                    // 强制覆盖颜色，同时保持粒子的亮度
                    main.startColor = new ParticleSystem.MinMaxGradient(color);
                    
                    // 为了让原生枪械的“环境小火苗”变成巨大的“挥击特效拖尾”，必须魔改参数：
                    // 1. 停留时间缩短，符合挥击动作
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
                    
                    // 2. 移除初始速度，让火焰留在挥砍轨迹上，而不是向外喷射
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
                    
                    // 3. 放大粒子体积，但避免过于遮挡视线
                    main.startSizeMultiplier *= 1.8f * sizeScale;

                    // 4. 最重要：原版火焰是靠时间生成的 (rateOverTime)。
                    // 我们挥砍极快（0.2秒），必须改为根据移动距离生成 (rateOverDistance)！
                    // 降低密度，否则太密会遮挡屏幕
                    var em = ps.emission;
                    em.rateOverDistance = new ParticleSystem.MinMaxCurve(15f);
                    // 稍微保留一点时间生成，防止停顿时断火
                    em.rateOverTime = new ParticleSystem.MinMaxCurve(10f);

                    // 重新播放
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                }

                // 尝试抓取刚才注入的 SodaPointLight 改变颜色并适当缩减范围避免过曝
                Light[] injectedLights = blurObj.GetComponentsInChildren<Light>(true);
                foreach (var l in injectedLights)
                {
                    l.color = color;
                    l.range = 3.0f * lightScale;
                    l.intensity = 2.0f;
                }
            }
            
            elapsed = 0f;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Rotate the pivot to move the trail in an arc
            if (trailRoot != null)
            {
                // Ease out cubic
                float easeT = 1f - Mathf.Pow(1f - t, 3f);
                float currentAngle = startAngle + sweepAngle * easeT;
                trailRoot.localRotation = Quaternion.Euler(0f, currentAngle, 0f);
            }

            if (injectedParticles != null && t >= 0.8f) // 动作接近尾声时停止发射
            {
                foreach (var ps in injectedParticles)
                {
                    if (ps != null && ps.isPlaying)
                    {
                        var em = ps.emission;
                        em.enabled = false;
                    }
                }
            }
        }

        private static Color GetStepColor(int step)
        {
            switch (step)
            {
                case 1: 
                    return new Color(1f, 0.25f, 0.08f, 0.9f); // 原本 case 2 的颜色
                case 2:
                    return new Color(1f, 0.6f, 0.18f, 0.85f); // 原本 case 1 的颜色
                default:
                    return new Color(1f, 0.45f, 0.12f, 0.85f);
            }
        }
    }

    // ========================================================================
    // 新增：动态修改连招动画角度的组件
    // ========================================================================
    /// <summary>
    /// 用于动态修改连招动画角度的组件
    /// </summary>
    public class FenHuangAnimationModifier : MonoBehaviour
    {
        private Vector3 targetTiltEuler;
        private float duration;
        private float elapsed;

        private Transform targetBone;
        private Coroutine tiltCoroutine;
        private Quaternion baselineLocalRotation = Quaternion.identity;
        private bool hasBaselineRotation;
        private bool refreshBaselineOnFirstFrame;

        public void ApplyTilt(Transform bone, Vector3 eulerOffset, float dur)
        {
            if (bone == null) return;

            RestoreBaselineRotation();

            targetBone = bone;
            targetTiltEuler = eulerOffset;
            duration = dur;
            elapsed = 0f;
            baselineLocalRotation = bone.localRotation;
            hasBaselineRotation = true;
            refreshBaselineOnFirstFrame = true;

            if (tiltCoroutine != null)
            {
                StopCoroutine(tiltCoroutine);
            }

            tiltCoroutine = StartCoroutine(TiltRoutine());
        }

        private System.Collections.IEnumerator TiltRoutine()
        {
            while (elapsed <= duration)
            {
                yield return new WaitForEndOfFrame();

                if (targetBone == null)
                {
                    tiltCoroutine = null;
                    yield break;
                }

                if (refreshBaselineOnFirstFrame)
                {
                    baselineLocalRotation = targetBone.localRotation;
                    refreshBaselineOnFirstFrame = false;
                }

                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                if (t > 1f) t = 1f;

                if (targetTiltEuler.sqrMagnitude < 0.1f)
                {
                    targetBone.localRotation = baselineLocalRotation;
                    continue;
                }

                float weight = 4f * t * (1f - t);
                Quaternion targetOffset = Quaternion.Euler(targetTiltEuler);
                Quaternion currentOffset = Quaternion.Slerp(Quaternion.identity, targetOffset, weight);

                // Apply the tilt from the captured baseline so repeated attacks cannot accumulate drift.
                targetBone.localRotation = baselineLocalRotation * currentOffset;
            }

            RestoreBaselineRotation();
            tiltCoroutine = null;
        }

        private void OnDisable()
        {
            if (tiltCoroutine != null)
            {
                StopCoroutine(tiltCoroutine);
                tiltCoroutine = null;
            }

            RestoreBaselineRotation();
        }

        private void OnDestroy()
        {
            RestoreBaselineRotation();
        }

        private void RestoreBaselineRotation()
        {
            if (targetBone != null && hasBaselineRotation)
            {
                targetBone.localRotation = baselineLocalRotation;
            }
        }

        /// <summary>
        /// 外部调用：立即停止 tilt 协程并恢复到基线旋转。
        /// 用于右键跳跃前确保武器 pose 干净，防止缓存到被 tilt 扭曲的值。
        /// </summary>
        public void ForceRestore()
        {
            if (tiltCoroutine != null)
            {
                StopCoroutine(tiltCoroutine);
                tiltCoroutine = null;
            }

            RestoreBaselineRotation();
            hasBaselineRotation = false;
        }
    }
}
