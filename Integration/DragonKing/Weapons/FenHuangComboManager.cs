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

            DamageReceiver receiver = TryGetReceiver(health);
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

            if (state.Step == 1)
            {
                ApplyKnockback(receiver.transform, attacker, FenHuangHalberdConfig.Combo2KnockbackDistance);
            }
            else if (state.Step == 2)
            {
                ApplyBurnBuff(receiver, attacker);
            }
        }

        private static DamageReceiver TryGetReceiver(Health health)
        {
            DamageReceiver receiver = health.GetComponent<DamageReceiver>();
            if (receiver != null)
            {
                return receiver;
            }

            receiver = health.GetComponentInParent<DamageReceiver>();
            if (receiver != null)
            {
                return receiver;
            }

            return health.GetComponentInChildren<DamageReceiver>();
        }

        internal static float GetComboConfiguredDamage(int step)
        {
            switch (step)
            {
                case 0:
                    return FenHuangHalberdConfig.Combo1Damage;
                case 1:
                    return FenHuangHalberdConfig.Combo2Damage;
                case 2:
                    return FenHuangHalberdConfig.Combo3Damage;
                default:
                    return FenHuangHalberdConfig.Combo1Damage;
            }
        }

        private static void ApplyKnockback(Transform target, CharacterMainControl holder, float distance)
        {
            try
            {
                Vector3 knockDir = target.position - holder.transform.position;
                knockDir.y = 0f;
                knockDir.Normalize();

                CharacterMainControl targetChar = target.GetComponentInParent<CharacterMainControl>();
                if (targetChar != null)
                {
                    targetChar.SetForceMoveVelocity(knockDir * distance * 5f);
                    if (FenHuangComboManager.Instance != null)
                    {
                        FenHuangComboManager.Instance.StartCoroutine(
                            StopKnockbackCoroutine(targetChar, 0.2f)
                        );
                    }
                }
            }
            catch
            {
            }
        }

        private static System.Collections.IEnumerator StopKnockbackCoroutine(CharacterMainControl target, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (target != null)
            {
                target.SetForceMoveVelocity(Vector3.zero);
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
            ApplyBuffLifetime(combo3BurnBuff, FenHuangHalberdConfig.Combo3BurnDuration);
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
        [HarmonyPostfix]
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

                int effectStep = combo.ComboStep;
                combo.RegisterAttack(character, effectStep);
                combo.AdvanceCombo();
                SpawnSwingEffect(character, effectStep);
            }
            catch
            {
            }
        }

        private static void SpawnSwingEffect(CharacterMainControl character, int step)
        {
            try
            {
                Transform socket = null;
                if (character.characterModel != null)
                {
                    socket = character.characterModel.MeleeWeaponSocket;
                    if (socket == null)
                    {
                        socket = character.characterModel.RightHandSocket;
                    }
                }

                Vector3 forward = character.CurrentAimDirection;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = character.transform.forward;
                    forward.y = 0f;
                }
                forward.Normalize();

                Vector3 spawnPos = socket != null ? socket.position : character.transform.position + Vector3.up * 1.1f;
                spawnPos += forward * 0.6f;

                Quaternion rotation = Quaternion.LookRotation(forward);
                GameObject fx = new GameObject("FenHuang_SwingFX");
                fx.transform.position = spawnPos;
                fx.transform.rotation = rotation;

                FenHuangSwingFx swingFx = fx.AddComponent<FenHuangSwingFx>();
                swingFx.Initialize(step);
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
        private static readonly Dictionary<int, float> originalDamageValues = new Dictionary<int, float>();
        private static readonly int damageStatHash = "Damage".GetHashCode();

        [HarmonyPrefix]
        public static void Prefix(ItemAgent_MeleeWeapon __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                Item item = __instance.Item;
                if (item == null || item.TypeID != FenHuangHalberdIds.WeaponTypeId)
                {
                    return;
                }

                CharacterMainControl holder = __instance.Holder;
                FenHuangComboManager combo = FenHuangComboManager.Instance;
                if (holder == null || combo == null)
                {
                    return;
                }

                int step;
                if (!combo.TryGetActiveAttackStep(holder, out step))
                {
                    return;
                }

                Stat damageStat = item.GetStat(damageStatHash);
                if (damageStat == null)
                {
                    return;
                }

                int meleeId = __instance.GetInstanceID();
                if (!originalDamageValues.ContainsKey(meleeId))
                {
                    originalDamageValues[meleeId] = damageStat.BaseValue;
                }

                damageStat.BaseValue = FenHuangComboManager.GetComboConfiguredDamage(step);
            }
            catch
            {
            }
        }

        [HarmonyPostfix]
        public static void Postfix(ItemAgent_MeleeWeapon __instance)
        {
            RestoreDamage(__instance);
        }

        private static void RestoreDamage(ItemAgent_MeleeWeapon melee)
        {
            if (melee == null)
            {
                return;
            }

            int meleeId = melee.GetInstanceID();
            float originalDamage;
            if (!originalDamageValues.TryGetValue(meleeId, out originalDamage))
            {
                return;
            }

            originalDamageValues.Remove(meleeId);

            try
            {
                Item item = melee.Item;
                if (item == null)
                {
                    return;
                }

                Stat damageStat = item.GetStat(damageStatHash);
                if (damageStat == null)
                {
                    return;
                }

                damageStat.BaseValue = originalDamage;
            }
            catch
            {
            }
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

                // 如果返回结果已经是 ItemAgent_MeleeWeapon，则无需重复注入
                if (__result is ItemAgent_MeleeWeapon) return;

                // 首次进入时缓存反射字段
                if (!_fieldsCached)
                {
                    _meleeRefField = typeof(ItemAgentHolder).GetField("_meleeRef",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _currentUsingSocketCacheField = typeof(ItemAgentHolder).GetField("_currentUsingSocketCache",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    // ItemAgent.item 是 private 字段，需要通过反射读取/写入
                    _itemAgentItemField = typeof(ItemStatsSystem.ItemAgent).GetField("item",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    _fieldsCached = true;

                    ModBehaviour.DevLog($"[FenHuangHalberd] 反射字段缓存完成: _meleeRef={_meleeRefField != null}, usingSocket={_currentUsingSocketCacheField != null}, itemField={_itemAgentItemField != null}");
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
                meleeComp.handheldSocket = HandheldSocketTypes.meleeWeapon;
                meleeComp.handAnimationType = HandheldAnimationType.meleeWeapon;

                // 绑定 Holder，确保角色控制和归属关系正确
                meleeComp.SetHolder(__result.Holder);

                // 同步返回代理的挂点、动画类型和实际父节点，确保 CurrentHoldItemAgent 表现正确
                __result.handheldSocket = HandheldSocketTypes.meleeWeapon;
                __result.handAnimationType = HandheldAnimationType.meleeWeapon;

                Transform meleeSocket = null;
                if (__instance.characterController != null && __instance.characterController.characterModel != null)
                {
                    meleeSocket = __instance.characterController.characterModel.MeleeWeaponSocket;
                    if (meleeSocket == null)
                    {
                        meleeSocket = __instance.characterController.characterModel.RightHandSocket;
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

                ModBehaviour.DevLog("[FenHuangHalberd] Injected ItemAgent_MeleeWeapon into handheld agent.");

                // 禁用运动模糊，使武器移动时不会模糊
                try
                {
                    Renderer[] renderers = agentGo.GetComponentsInChildren<Renderer>(true);
                    foreach (Renderer r in renderers)
                    {
                        if (r != null)
                        {
                            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
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
        private float elapsed;
        private float duration = 0.2f;
        private Light pointLight;
        private Material coreMaterial;
        private Transform coreRoot;

        public void Initialize(int step)
        {
            duration = 0.2f;
            Color color = GetStepColor(step);

            if (coreRoot == null)
            {
                coreRoot = new GameObject("SwingCore").transform;
                coreRoot.SetParent(transform, false);

                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(coreRoot, false);
                sphere.transform.localPosition = Vector3.zero;
                sphere.transform.localScale = new Vector3(1.4f, 0.35f, 1.9f);
                UnityEngine.Object.Destroy(sphere.GetComponent<Collider>());

                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    shader = Shader.Find("Unlit/Color");
                }
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                coreMaterial = new Material(shader);
                Renderer renderer = sphere.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = coreMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                pointLight = gameObject.AddComponent<Light>();
                pointLight.type = LightType.Point;
                pointLight.range = 2.4f;
                pointLight.shadows = LightShadows.None;
            }

            coreMaterial.color = color;
            pointLight.color = color;
            pointLight.intensity = 4.5f;
            elapsed = 0f;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float scale = Mathf.Lerp(0.7f, 1.7f, t);

            if (coreRoot != null)
            {
                coreRoot.localScale = new Vector3(scale, 1f, scale);
            }

            if (coreMaterial != null)
            {
                Color color = coreMaterial.color;
                color.a = Mathf.Lerp(0.85f, 0f, t);
                coreMaterial.color = color;
            }

            if (pointLight != null)
            {
                pointLight.intensity = Mathf.Lerp(4.5f, 0f, t);
            }
        }

        private void OnDestroy()
        {
            if (coreMaterial != null)
            {
                UnityEngine.Object.Destroy(coreMaterial);
            }
        }

        private static Color GetStepColor(int step)
        {
            switch (step)
            {
                case 1:
                    return new Color(1f, 0.6f, 0.18f, 0.85f);
                case 2:
                    return new Color(1f, 0.25f, 0.08f, 0.9f);
                default:
                    return new Color(1f, 0.45f, 0.12f, 0.85f);
            }
        }
    }
}
