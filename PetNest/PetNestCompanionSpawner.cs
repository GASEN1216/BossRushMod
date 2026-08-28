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

        /// <summary>幼体视觉缩放基准档。</summary>
        internal const float DefaultModelScale = 0.4f;

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

                AICharacterController ai = handle.Character.GetComponentInChildren<AICharacterController>();
                if (ai != null)
                {
                    // 原版 spawner 路径会把 forceTracePlayerDistance 写成 9999f，
                    // 清零必须发生在创建返回之后。
                    ai.forceTracePlayerDistance = 0f;
                    ai.searchedEnemy = null;
                    ai.noticed = false;
                }

                PetNestCompanionAgent agent = handle.Character.GetComponent<PetNestCompanionAgent>();
                if (agent == null)
                {
                    agent = handle.Character.gameObject.AddComponent<PetNestCompanionAgent>();
                }
                handle.Agent = agent;

                handle.Character.gameObject.SetActive(true);
                if (handle.Health != null)
                {
                    handle.Health.SetInvincible(false);
                }

                ApplyModelScale(handle);
                agent.Bind(handle.Character, master);

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
        /// 把崽的出身天赋与战痕应用到幼体身上。
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
                        PetNestTalentEntry t = pet.talents[i];
                        if (t == null || string.IsNullOrEmpty(t.statKey)) continue;
                        if (string.Equals(t.statKey, PetNestPetProxyBridge.PetCapacityStatKey,
                                StringComparison.Ordinal))
                        {
                            continue;
                        }
                        ApplyOneModifier(characterItem, t.statKey, t.value, t.percentage);
                    }
                }

                ApplyScarModifiers(characterItem, pet);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 应用崽属性失败: " + e.Message);
            }
        }

        /// <summary>同一 stat 上的战痕合并成一条并按封顶钳制，避免逐条叠加突破上限。</summary>
        private static void ApplyScarModifiers(Item characterItem, PetNestPetRecord pet)
        {
            if (pet.scars == null || pet.scars.Count == 0) return;

            Dictionary<string, float> byStat = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < pet.scars.Count; i++)
            {
                PetNestScarRecord s = pet.scars[i];
                if (s == null || string.IsNullOrEmpty(s.statKey)) continue;
                float sum;
                byStat.TryGetValue(s.statKey, out sum);
                byStat[s.statKey] = sum + s.percent;
            }

            foreach (KeyValuePair<string, float> pair in byStat)
            {
                float clamped = Mathf.Max(pair.Value, PetNestTuning.ScarModifierCapPercent);
                ApplyOneModifier(characterItem, pair.Key, clamped, true);
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
