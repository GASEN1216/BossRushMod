// ============================================================================
// PetNestCompanionSpawner.cs - 遗种巢幼体随从生成桥（实施计划 步骤 0 / 步骤 6）
// ============================================================================
// 作用：
//   - 把官方 Boss preset 克隆成「玩家方幼体」：中性化五件套 -> 两段式 staging
//     创建 -> modelRoot 纯视觉缩放 -> 同帧激活。
//   - 幂等 Activate / CleanupOnce handle，形态照 Utilities/ManagedBossSpawnContracts.cs
//     与 ModeH/ModeHSpawnBridge.cs 的既有先例。
//
// 冻结契约：
//   - 只克隆、不修改源 preset：源 preset 是 Resources 里的共享资产；
//   - 中性化五件套（hasSkill / exp / hasSoul / team / dropBoxOnDead）必须在
//     CreateCharacterAsync 调用之前写在 clone 上——await 窗口期间波次统计、掉落、
//     经验、成就只能看到 clone 身份；
//   - group 恒传 null、isLeader 恒传 false：AICharacterController.Update 会在 leader
//     与成员之间双向同步 searchedEnemy，一旦成组目标会互相污染
//     （同 ModeH/ModeHSpawnBridge.cs:44-46）；
//   - 只缩 modelRoot（CharacterMainControl.cs:3284 public Transform），不缩角色根：
//     碰撞体只在 SetCharacterModel 内计算一次，缩根会让碰撞/寻路与视觉永久失配；
//   - 回收顺序固定为「组件退表 -> 销毁角色 -> 销毁 clone preset」。
// ============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 一只在场幼体随从的 runtime handle。角色、Health、clone preset 与组件同寿命：
    /// 原版伤害与死亡路径仍会读 characterPreset，因此 clone 只能在角色销毁之后再销毁。
    /// </summary>
    internal sealed class PetNestCompanionHandle
    {
        /// <summary>血脉 key（官方 preset 的 nameKey，或自定义 Boss 常量）。</summary>
        internal string LineageKey;
        /// <summary>本次使用的 runtime clone preset。</summary>
        internal CharacterRandomPreset ClonePreset;
        /// <summary>生成出的幼体角色。</summary>
        internal CharacterMainControl Character;
        /// <summary>幼体的 Health 组件。</summary>
        internal Health Health;
        /// <summary>跟随维护组件。</summary>
        internal PetNestCompanionAgent Agent;
        /// <summary>视觉缩放档（只作用于 modelRoot）。</summary>
        internal float ModelScale;
        /// <summary>是否已激活进入战场。</summary>
        internal bool Activated;
        /// <summary>CleanupOnce 幂等标记。</summary>
        internal bool CleanedUp;
        /// <summary>炫彩 / 异色光环（普通崽为 null，零对象零成本）。</summary>
        internal PetNestAuraEffect[] Auras;
    }

    /// <summary>
    /// 遗种巢幼体生成桥。全系统唯一调用 CreateCharacterAsync 的地方。
    /// </summary>
    internal static class PetNestCompanionSpawner
    {
        #region 常量

        /// <summary>staging 点相对玩家的偏移：先扔到远处 inactive，再拉回落点激活。</summary>
        internal static readonly Vector3 StagingOffset = new Vector3(0f, -240f, 0f);

        /// <summary>入场落点相对玩家的偏移。</summary>
        internal static readonly Vector3 SpawnOffset = new Vector3(1.2f, 0.5f, 1.2f);

        /// <summary>
        /// 幼体视觉缩放基准档。
        /// 数值单点在 PetNestTuning.DefaultCubModelScale；这里只保留调用面，
        /// 避免同一个 0.4f 在两处各写一遍、改一处漏一处。
        /// </summary>
        internal const float DefaultModelScale = PetNestTuning.DefaultCubModelScale;

        #endregion

        #region 中性化五件套

        /// <summary>
        /// 中性化五件套：把克隆 preset 改造成「玩家方幼体」。
        ///
        /// 五件套本身是契约（guard 逐条断言）：
        /// hasSkill=false / exp=0 / hasSoul=false / team=player / dropBoxOnDead=false。
        /// 其余是同属「不给宿主添乱」的附加安全项。
        /// </summary>
        internal static void NeutralizeClonePreset(CharacterRandomPreset clone)
        {
            if (clone == null) return;

            // —— 中性化五件套 ——
            // 技能白名单化之前，幼体一律不带 Boss 技能（自爆/召唤/范围）
            clone.hasSkill = false;
            // 幼体不给玩家经验：它是随从，不是可击杀目标
            clone.exp = 0;
            // 幼体不掉灵魂方块：避免 SoulCollector 把随从死亡当作战利品来源
            clone.hasSoul = false;
            // 玩家方：这是随从进局的根基，也是清场豁免的判据之一
            clone.team = Teams.player;
            // 幼体不掉落箱：随从倒下是重伤退场，不是战利品事件
            clone.dropBoxOnDead = false;

            // —— 附加安全项 ——
            try
            {
                // 不强制追踪玩家（玩家方本来就不满足 IsEnemy 判定，这里显式清零留证）
                clone.forceTracePlayerDistance = 0f;
                // 随从跟随时可能远离玩家，按距离自动停用会让它假死在原地
                clone.setActiveByPlayerDistance = false;
                // 幼体不掉现金
                clone.hasCashChance = 0f;
                // 非 raid 图也允许判定死亡，与 staging 先例一致
                clone.canDieIfNotRaidMap = true;
                // 战斗强度归一，避免克隆继承 Boss 侧的 combat factor
                clone.aiCombatFactor = 1f;

                // —— 伤害归一必须写在这里 ——
                // 官方只在 CreateCharacterAsync 内部消费这三个字段一次
                // （SetCharacterStat("GunDamageMultiplier"/"MeleeDamageMultiplier"/
                // "GunCritRateGain", ...) 写进角色 Item 的 BaseValue），创建返回之后再改
                // preset 已经没有任何读者。写在这里才真的能把幼体输出压到目标占比。
                clone.damageMultiplier = PetNestTuning.CompanionDpsShareTarget;
                clone.setMeleeDamageMultiplier = true;
                clone.meleeDamageMultiplier = PetNestTuning.CompanionDpsShareTarget;
                clone.gunCritRateGain = 0f;
                // 特殊挂件（炮台/召唤物一类）一律清空，首版幼体只留基础攻击
                clone.specialAttachmentBases = new List<AISpecialAttachmentBase>();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 幼体附加中性化项写入失败: " + e.Message);
            }
        }

        #endregion

        #region 创建

        /// <summary>
        /// 在 staging 点创建一只隔离幼体。返回的 handle 里角色已 inactive + invincible。
        /// 失败返回 null 并给出 failureReasonId。
        /// </summary>
        internal static async UniTask<PetNestCompanionHandle> CreateIsolatedAsync(
            CharacterRandomPreset sourcePreset,
            string lineageKey,
            float modelScale,
            Vector3 stagingPos)
        {
            if (sourcePreset == null)
            {
                ModBehaviour.DevLog("[PetNest] 幼体创建失败: source preset 为空");
                return null;
            }

            PetNestCompanionHandle handle = new PetNestCompanionHandle();
            handle.LineageKey = lineageKey;
            handle.ModelScale = modelScale > 0f ? modelScale : DefaultModelScale;

            CharacterRandomPreset clone = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(sourcePreset);
                clone.name = "PetNest_Companion_" + (string.IsNullOrEmpty(lineageKey) ? "unknown" : lineageKey);
                // 血脉身份戳：自定义血脉的底模是官方角色（Cname_Boss_Red / Cname_Ghost），
                // 不改 nameKey 的话名条与战痕凶手名会显示底模名。官方血脉本就等值，写入无害且幂等。
                if (!string.IsNullOrEmpty(lineageKey)) clone.nameKey = lineageKey;
                NeutralizeClonePreset(clone);
                handle.ClonePreset = clone;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 幼体 clone preset 失败: " + e.Message);
                DestroyClone(clone);
                return null;
            }

            CharacterMainControl character = null;
            try
            {
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                character = await clone.CreateCharacterAsync(
                    stagingPos, Vector3.forward, relatedScene, null, false);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 幼体 CreateCharacterAsync 异常: " + e.Message);
                DestroyClone(clone);
                return null;
            }

            if (character == null)
            {
                ModBehaviour.DevLog("[PetNest] 幼体 CreateCharacterAsync 返回 null");
                DestroyClone(clone);
                return null;
            }

            // 创建返回后的第一个同步步骤：登记引用并立即隔离
            handle.Character = character;
            try { handle.Health = character.Health; }
            catch (Exception) { handle.Health = null; }

            try
            {
                if (handle.Health != null)
                {
                    handle.Health.SetInvincible(true);
                }
                character.gameObject.name = "PetNest_Companion_" + (string.IsNullOrEmpty(lineageKey) ? "unknown" : lineageKey);
                character.gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 幼体隔离失败: " + e.Message);
                CleanupOnce(handle);
                return null;
            }

            ApplyModelScale(handle);
            return handle;
        }

        /// <summary>
        /// 只缩 modelRoot 的纯视觉缩放。角色根与碰撞体不动——碰撞体只在
        /// SetCharacterModel 内算一次，缩根会让碰撞/寻路与视觉永久失配。
        /// </summary>
        internal static void ApplyModelScale(PetNestCompanionHandle handle)
        {
            if (handle == null || handle.Character == null) return;
            try
            {
                Transform modelRoot = handle.Character.modelRoot;
                if (modelRoot == null) return;
                float scale = handle.ModelScale > 0f ? handle.ModelScale : DefaultModelScale;
                modelRoot.localScale = new Vector3(scale, scale, scale);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 幼体 modelRoot 缩放失败: " + e.Message);
            }
        }

        #endregion

        #region 激活

        /// <summary>
        /// 隔离结束后的提交步骤：落到入场点、清干净 AI 目标、挂跟随组件、同帧激活。
        /// 幂等：已激活的 handle 直接返回 true。
        /// </summary>
        internal static bool TryActivate(
            PetNestCompanionHandle handle,
            Vector3 spawnPos,
            CharacterMainControl master,
            ModBehaviour owner,
            PetNestPetRecord pet,
            out string failureReasonId)
        {
            failureReasonId = null;
            if (handle == null || handle.Character == null)
            {
                failureReasonId = "companion_handle_invalid";
                return false;
            }
            if (handle.Activated)
            {
                return true;
            }

            try
            {
                // 阵营取**主人的实际阵营**而不是硬编码 Teams.player：
                // Mode E 会把玩家改到别的阵营，硬编码会让随从在阵营混战里被误伤
                // （同 Integration/NewWeapons/SummonStaff/SummonStaffAction.cs 的取法）。
                Teams companionTeam = master != null ? master.Team : Teams.player;
                handle.Character.SetTeam(companionTeam);
                handle.Character.SetPosition(spawnPos);

                // 复用既有净化入口：移除自爆技能与 BoomCar 特殊挂件。
                // 官方敌方 preset 里确实有会自爆的，孵出来会把主人一起炸了。
                if (owner != null)
                {
                    owner.SanitizeBossRushZombieSpawn(handle.Character, "PetNestCompanion");
                }

                NormalizeCombatOutput(handle.Character);
                // 天赋与战痕必须真的挂上去，否则它们只是面板上的展示文本
                ApplyPetModifiers(handle.Character, pet);
                // MaxHealth 类 Modifier 是在角色创建**之后**挂的，而官方 CurrentHealth
                // 是创建时写死的字段、MaxHealth 则实时从角色 Item 的 stat 算
                // （鸭科夫源码/TeamSoda.Duckov.Core/Health.cs:44-116）。不补这一步，
                // 「结实」天赋与等级成长会让崽一入场就是残血，战痕的 MaxHealth 减益
                // 又会让血条超过 100%。官方 SetHealth 自带 Mathf.Min，两个方向都对。
                TopUpHealthToMax(handle);

                PetNestPersonalityProfile personality = PetNestPersonality.Resolve(pet);

                AICharacterController ai = handle.Character.GetComponentInChildren<AICharacterController>();
                if (ai != null)
                {
                    // 原版 spawner 路径会把 forceTracePlayerDistance 写成 9999f，
                    // 清零必须发生在创建返回之后。
                    ai.forceTracePlayerDistance = 0f;
                    ai.searchedEnemy = null;
                    ai.noticed = false;
                    // 性格落地：只写官方 public 字段，不反射、不另挂行为树
                    ApplyPersonalityToAI(ai, personality);
                }

                PetNestCompanionAgent agent = handle.Character.GetComponent<PetNestCompanionAgent>();
                if (agent == null)
                {
                    agent = handle.Character.gameObject.AddComponent<PetNestCompanionAgent>();
                }
                handle.Agent = agent;
                agent.ApplyPersonality(personality);

                handle.Character.gameObject.SetActive(true);
                if (handle.Health != null)
                {
                    handle.Health.SetInvincible(false);
                }

                ApplyModelScale(handle);
                agent.Bind(handle.Character, master);
                // 炫彩 / 异色光环：只有真的带色的崽才会创建对象（AGENTS 4.12）
                AttachChromaAura(handle, pet);

                handle.Activated = true;
                return true;
            }
            catch (Exception e)
            {
                failureReasonId = "companion_activate_failed:" + e.GetType().Name;
                ModBehaviour.DevLog("[PetNest] 幼体激活失败: " + e.Message);
                return false;
            }
        }

        #endregion

        #region 回收

        /// <summary>
        /// 幂等回收：组件退表 -> 销毁角色 -> 销毁 clone preset。重复调用无副作用。
        /// </summary>
        internal static void CleanupOnce(PetNestCompanionHandle handle)
        {
            if (handle == null || handle.CleanedUp) return;
            handle.CleanedUp = true;

            DetachChromaAura(handle);

            try
            {
                if (handle.Agent != null)
                {
                    UnityEngine.Object.Destroy(handle.Agent);
                }
            }
            catch (Exception)
            {
                // 组件销毁失败不阻断角色回收
            }
            handle.Agent = null;

            try
            {
                if (handle.Character != null && handle.Character.gameObject != null)
                {
                    UnityEngine.Object.Destroy(handle.Character.gameObject);
                }
            }
            catch (Exception)
            {
                // 角色销毁失败不阻断 preset 清理
            }
            handle.Character = null;
            handle.Health = null;

            DestroyClone(handle.ClonePreset);
            handle.ClonePreset = null;
            handle.Activated = false;
        }

        #region 炫彩 / 异色光环

        /// <summary>
        /// 给带炫彩或异色的崽挂上光环。owner 2026-09-20：
        /// 「不同炫彩弄不同的粒子特效，异色则是最豪华的最好看的」。
        ///
        /// 炫彩：两层环形粒子，一层一色、半径与高度错开，转起来是两色交织；
        /// 异色：一层高密度金色 + 一盏跟随点光（最显眼的那一档）。
        /// 普通崽：**一个对象都不创建**，零每帧成本。
        /// </summary>
        private static void AttachChromaAura(PetNestCompanionHandle handle, PetNestPetRecord pet)
        {
            if (handle == null || handle.Character == null || pet == null) return;
            if (handle.Auras != null) return;

            bool chroma = PetNestChroma.HasChroma(pet);
            if (!pet.shiny && !chroma) return;

            try
            {
                Transform follow = handle.Character.transform;
                List<PetNestAuraEffect> auras = new List<PetNestAuraEffect>(3);

                if (chroma)
                {
                    PetNestChromaColor a = PetNestChroma.Find(pet.chromaA);
                    PetNestChromaColor b = PetNestChroma.Find(pet.chromaB);
                    auras.Add(CreateAura(follow, ToColor(a), 4, 0.30f, 0.30f, 0.65f, 6f, 0.8f,
                        new Vector3(0f, 0.05f, 0f)));
                    auras.Add(CreateAura(follow, ToColor(b), 4, 0.44f, 0.26f, 0.5f, 5f, 0.95f,
                        new Vector3(0f, 0.28f, 0f)));
                }

                if (pet.shiny)
                {
                    Color gold = new Color(PetNestChroma.ShinyParticleR,
                        PetNestChroma.ShinyParticleG, PetNestChroma.ShinyParticleB);
                    auras.Add(CreateAura(follow, gold, 6, 0.38f, 0.45f, 0.85f, 11f, 1.1f,
                        new Vector3(0f, 0.15f, 0f)));
                    AttachShinyLight(handle.Character, gold);
                }

                for (int i = auras.Count - 1; i >= 0; i--)
                {
                    if (auras[i] == null) auras.RemoveAt(i);
                }
                handle.Auras = auras.Count > 0 ? auras.ToArray() : null;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 光环创建失败: " + e.Message);
                handle.Auras = null;
            }
        }

        private static PetNestAuraEffect CreateAura(Transform follow, Color tint, int emitters,
            float radius, float alpha, float size, float rate, float lifetime, Vector3 offset)
        {
            PetNestAuraEffect aura = PetNestAuraEffect.Create<PetNestAuraEffect>(
                follow, follow.position + offset);
            if (aura == null) return null;
            aura.Configure(tint, emitters, radius, alpha, size, rate, lifetime, offset);
            // 挂到崽底下：角色销毁时光环随之销毁，不会留孤儿
            aura.transform.SetParent(follow, true);
            return aura;
        }

        /// <summary>异色专属的跟随点光。挂在角色子节点上，随角色销毁。</summary>
        private static void AttachShinyLight(CharacterMainControl character, Color color)
        {
            try
            {
                GameObject lightObj = new GameObject("PetNestShinyLight");
                lightObj.transform.SetParent(character.transform, false);
                lightObj.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                Light light = lightObj.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.intensity = 2.2f;
                light.range = 1.8f;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 异色点光创建失败: " + e.Message);
            }
        }

        private static Color ToColor(PetNestChromaColor color)
        {
            if (color == null) return Color.white;
            return new Color(color.ParticleR, color.ParticleG, color.ParticleB);
        }

        /// <summary>回收光环。幂等；角色已销毁时光环也已随之销毁，这里只丢引用。</summary>
        private static void DetachChromaAura(PetNestCompanionHandle handle)
        {
            if (handle == null || handle.Auras == null) return;
            for (int i = 0; i < handle.Auras.Length; i++)
            {
                try
                {
                    PetNestAuraEffect aura = handle.Auras[i];
                    if (aura != null) aura.StopEffect();
                }
                catch (Exception)
                {
                    // 光环回收失败不阻断角色回收
                }
            }
            handle.Auras = null;
        }

        #endregion

        /// <summary>
        /// 伤害归一：把幼体的输出压到「锦上添花不改天换地」的区间。
        ///
        /// 克隆自 Boss 的随从会原样继承 Boss 的武器与伤害倍率，不归一会直接抢镜。
        /// 目标 DPS 占比见 PetNestTuning.CompanionDpsShareTarget（数值待 owner 审定）。
        /// </summary>
        internal static void NormalizeCombatOutput(CharacterMainControl companion)
        {
            if (companion == null) return;
            try
            {
                Item characterItem = companion.CharacterItem;
                if (characterItem == null) return;

                // 兜底钳制：主归一已经写在 clone preset 上（创建**之前**），这里只防
                // 某条路径漏走 NeutralizeClonePreset。
                // 注意 stat key：角色 Item 上的伤害倍率是 GunDamageMultiplier /
                // MeleeDamageMultiplier；"Damage" 是**武器 Item** 的 stat，在这里取不到。
                ClampDamageStat(characterItem, "GunDamageMultiplier");
                ClampDamageStat(characterItem, "MeleeDamageMultiplier");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 幼体伤害归一失败: " + e.Message);
            }
        }

        /// <summary>把某个伤害倍率 stat 的 BaseValue 钳到目标占比以内（只降不升）。</summary>
        private static void ClampDamageStat(Item characterItem, string statKey)
        {
            try
            {
                Stat stat = characterItem.GetStat(statKey);
                if (stat == null) return;
                if (stat.BaseValue > PetNestTuning.CompanionDpsShareTarget)
                {
                    stat.BaseValue = PetNestTuning.CompanionDpsShareTarget;
                }
            }
            catch (Exception)
            {
                // 该 stat 不存在时静默跳过：主归一已在 preset 侧生效
            }
        }

        #endregion

        #region 天赋与战痕（per-pet Modifier）

        /// <summary>天赋与战痕 Modifier 的 source tag（source-tagged，便于整组摘除）。</summary>
        internal static readonly object CompanionPetModifierSource = new object();

        /// <summary>
        /// 把崽的出身天赋、等级成长、性格与战痕应用到幼体身上。
        ///
        /// 不做这一步，天赋与战痕就只是面板上的展示文本——两只天赋完全不同的崽进局后
        /// 属性一模一样，养成与战痕惩罚在玩法上等于不存在。
        ///
        /// 纪律：
        /// - source-tagged，随角色销毁一起消失，不需要单独摘除；
        /// - PetCapcity 跳过：官方读的是**玩家**的 stat，挂幼体身上完全无效
        ///   （由 PetNestPetProxyBridge.ApplyCapacityBonus 挂到玩家身上）；
        /// - stat 不存在时 AddModifier 返回 false，只记日志不报错。
        /// </summary>
        internal static void ApplyPetModifiers(CharacterMainControl companion, PetNestPetRecord pet)
        {
            if (companion == null || pet == null) return;
            try
            {
                Item characterItem = companion.CharacterItem;
                if (characterItem == null) return;

                // 先摘干净，避免同一角色被重复应用
                characterItem.RemoveAllModifiersFrom(CompanionPetModifierSource);

                if (pet.talents != null)
                {
                    for (int i = 0; i < pet.talents.Count; i++)
                    {
                        ApplyStatRow(characterItem, pet.talents[i]);
                    }
                }

                ApplyLevelModifiers(characterItem, pet);
                ApplyPersonalityModifiers(characterItem, pet);
                ApplyScarModifiers(characterItem, pet);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 应用崽属性失败: " + e.Message);
            }
        }

        /// <summary>
        /// 等级成长。**养成回报的唯一落点**：在此之前 Lv1 与 Lv10 的崽进局后属性完全一样，
        /// 等级只换来每 3 级 +1 格捡漏背包，而那还要先借到官方宠物席位才生效。
        /// 数值在 PetNestTuning（置 0 即回到旧行为），走的是天赋同一条 Modifier 管线。
        /// </summary>
        private static void ApplyLevelModifiers(Item characterItem, PetNestPetRecord pet)
        {
            int levels = pet.level - 1;
            if (levels <= 0) return;

            float health = PetNestTuning.PetLevelMaxHealthBonusPerLevel * levels;
            float damage = PetNestTuning.PetLevelDamageBonusPerLevel * levels;

            ApplyOneModifier(characterItem, "MaxHealth", health, true);
            ApplyOneModifier(characterItem, "GunDamageMultiplier", damage, true);
            ApplyOneModifier(characterItem, "MeleeDamageMultiplier", damage, true);
        }

        /// <summary>
        /// 性格的属性签名。表在 PetNest/PetNestPersonality.cs（唯一一份），
        /// 这里只负责挂上去；AI 侧的索敌与追击旋钮见 ApplyPersonalityToAI。
        /// </summary>
        private static void ApplyPersonalityModifiers(Item characterItem, PetNestPetRecord pet)
        {
            PetNestTalentEntry[] rows = PetNestPersonality.Resolve(pet).Modifiers;
            if (rows == null) return;
            for (int i = 0; i < rows.Length; i++)
            {
                ApplyStatRow(characterItem, rows[i]);
            }
        }

        /// <summary>
        /// 挂一条 statKey/value/percentage 形状的属性行（天赋与性格共用）。
        /// PetCapcity 在这里统一跳过：官方读的是玩家的 stat，挂幼体身上是纯浪费。
        /// </summary>
        private static void ApplyStatRow(Item characterItem, PetNestTalentEntry row)
        {
            if (row == null || string.IsNullOrEmpty(row.statKey)) return;
            if (string.Equals(row.statKey, PetNestPetProxyBridge.PetCapacityStatKey,
                    StringComparison.Ordinal))
            {
                return;
            }
            ApplyOneModifier(characterItem, row.statKey, row.value, row.percentage);
        }

        /// <summary>
        /// 性格的 AI 旋钮。两个都是官方 public 字段，不经反射；
        /// 倍率作用在 preset 给出的原值上，因此不同血脉之间的相对差异保持不变。
        /// </summary>
        private static void ApplyPersonalityToAI(
            AICharacterController ai, PetNestPersonalityProfile personality)
        {
            if (ai == null || personality == null) return;
            try
            {
                if (personality.SightDistanceMultiplier > 0f
                    && !Mathf.Approximately(personality.SightDistanceMultiplier, 1f))
                {
                    ai.sightDistance = ai.sightDistance * personality.SightDistanceMultiplier;
                }
                if (personality.TraceTargetChance > 0f)
                {
                    ai.traceTargetChance = Mathf.Clamp01(personality.TraceTargetChance);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 应用性格 AI 旋钮失败: " + e.Message);
            }
        }

        /// <summary>
        /// 挂完全部 Modifier 之后把血量补到新的上限。
        ///
        /// 官方 Health.MaxHealth 是从角色 Item 的 stat 实时算的，CurrentHealth 则是
        /// 角色创建时写死的字段。我们的 Modifier 全部挂在创建**之后**，不补这一步：
        /// 加血类（结实天赋、等级成长）会让崽一入场就少血，减血类（战痕）会让血条超过 100%。
        /// </summary>
        private static void TopUpHealthToMax(PetNestCompanionHandle handle)
        {
            try
            {
                Health health = handle != null ? handle.Health : null;
                if (health == null) return;
                float max = health.MaxHealth;
                if (max <= 0f) return;
                health.SetHealth(max);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 幼体血量补齐失败: " + e.Message);
            }
        }

        /// <summary>
        /// 同一 stat 上的战痕合并成一条并按封顶钳制，避免逐条叠加突破上限。
        /// 「有哪些 stat 挨过疤」与「封顶后的实际数值」两份口径都在 PetNestDownedHandler，
        /// 这里不再自建第二份聚合（展示层 PetNestUIPages 走的是同两个入口）。
        /// </summary>
        private static void ApplyScarModifiers(Item characterItem, PetNestPetRecord pet)
        {
            List<string> statKeys = new List<string>();
            PetNestDownedHandler.CollectScarStatKeys(pet, statKeys);
            for (int i = 0; i < statKeys.Count; i++)
            {
                // 封顶口径只有一份权威实现，避免两处各写一遍后悄悄跑偏
                float clamped = PetNestDownedHandler.GetEffectiveScarPercent(pet, statKeys[i]);
                ApplyOneModifier(characterItem, statKeys[i], clamped, true);
            }
        }

        private static void ApplyOneModifier(Item characterItem, string statKey, float value, bool percentage)
        {
            try
            {
                if (Mathf.Approximately(value, 0f)) return;
                Modifier modifier = percentage
                    ? new Modifier(ModifierType.PercentageAdd, value, CompanionPetModifierSource)
                    : new Modifier(ModifierType.Add, value, CompanionPetModifierSource);
                if (!characterItem.AddModifier(statKey, modifier))
                {
                    ModBehaviour.DevLog("[PetNest] [WARNING] 幼体没有 stat " + statKey + "，该条未生效");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 应用 Modifier 失败(" + statKey + "): " + e.Message);
            }
        }

        private static void DestroyClone(CharacterRandomPreset clone)
        {
            if (clone == null) return;
            try
            {
                UnityEngine.Object.Destroy(clone);
            }
            catch (Exception)
            {
                // clone 销毁失败只丢弃引用
            }
        }

        #endregion

        #region 血脉 preset 解析

        /// <summary>
        /// 幼体生成专用的源 preset 解析：自定义 Boss 血脉走各自的官方底模，其余走通用解析。
        ///
        /// 为什么不直接改 ResolveSourcePreset：血脉目录也调它，而目录的官方循环靠
        /// "自定义 key 解析不到 → fail-closed 跳过" 把三个自定义 Boss 留给
        /// AddCustomLineages 登记（带元素覆盖、缩放档与显示名）。若通用解析开始认自定义
        /// key，官方循环会先以 IsCustomBoss=false 抢注它们，这些设定全部失效。
        /// </summary>
        internal static CharacterRandomPreset ResolveCompanionSourcePreset(string lineageKey)
        {
            CharacterRandomPreset custom = ResolveCustomLineageBasePreset(lineageKey);
            if (custom != null) return custom;
            return ResolveSourcePreset(lineageKey);
        }

        /// <summary>
        /// 自定义 Boss 血脉的底模解析。
        ///
        /// 三个自定义 Boss 的 runtime preset 是各自 Boss 生成那一刻才 Instantiate 的角色属性，
        /// 不进任何全局注册表，因此 ObjectCache 的一次性快照永远查不到它们——
        /// 孵出来的崽会一直以 lineage_preset_missing 被拦在场外。
        /// 这里改为解析它们共同的官方底模；幼体的数值由 NeutralizeClonePreset 覆盖，
        /// 不需要复现 Boss 的属性/装备构造。血脉身份由 CreateIsolatedAsync 写 nameKey 戳。
        /// </summary>
        private static CharacterRandomPreset ResolveCustomLineageBasePreset(string lineageKey)
        {
            if (string.IsNullOrEmpty(lineageKey)) return null;

            // 龙裔与龙王共用同一官方底模（龙王的 FindDragonKingBasePreset 走问号 preset，同源）
            if (string.Equals(lineageKey, DragonDescendantConfig.BOSS_NAME_KEY, StringComparison.Ordinal)
                || string.Equals(lineageKey, DragonKingConfig.BossNameKey, StringComparison.Ordinal))
            {
                return ResolveSourcePreset(DragonDescendantConfig.BasePresetNameKey);
            }

            if (string.Equals(lineageKey, PhantomWitchConfig.BossNameKey, StringComparison.Ordinal))
            {
                // 镜像 PhantomWitchBoss 的显式回落链：底模缺失时退到红 Boss，仍是常量到常量
                CharacterRandomPreset ghost = ResolveSourcePreset(PhantomWitchConfig.BasePresetNameKey);
                return ghost != null
                    ? ghost
                    : ResolveSourcePreset(PhantomWitchConfig.FallbackPresetNameKey);
            }

            return null;
        }

        /// <summary>
        /// 按官方 preset 的 nameKey 精确解析源 preset。找不到返回 null（fail-closed：
        /// 该血脉不产幼体，不回落到"随便找一个同阵营强敌"）。
        /// </summary>
        internal static CharacterRandomPreset ResolveSourcePreset(string lineageKey)
        {
            if (string.IsNullOrEmpty(lineageKey)) return null;
            try
            {
                CharacterRandomPreset[] all = ObjectCache.GetCharacterPresets();
                if (all == null) return null;
                for (int i = 0; i < all.Length; i++)
                {
                    CharacterRandomPreset p = all[i];
                    if (p == null) continue;
                    if (!string.IsNullOrEmpty(p.nameKey) && p.nameKey == lineageKey)
                    {
                        return p;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 解析血脉 preset 失败: " + e.Message);
            }
            return null;
        }

        #endregion
    }
}
