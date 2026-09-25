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
using ItemStatsSystem.Items;
using Duckov.Utilities;
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
        /// <summary>炫彩 / 异色特效（普通崽为 null，零对象零成本）。</summary>
        internal PetNestAuraEffect Aura;
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
        /// 避免同一个体型基准在两处各写一遍、改一处漏一处。
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
                // 战斗强度归一，避免克隆继承 Boss 侧的 combat factor；
                // 血脉差异保留在 damageMultiplier / moveSpeedFactor 等字段。
                clone.aiCombatFactor = 1f;

                // —— 伤害归一必须写在这里 ——
                // 官方只在 CreateCharacterAsync 内部消费这三个字段一次
                // （SetCharacterStat("GunDamageMultiplier"/"MeleeDamageMultiplier"/
                // "GunCritRateGain", ...) 写进角色 Item 的 BaseValue），创建返回之后再改
                // preset 已经没有任何读者。写在这里才真的能把幼体输出压到目标占比。
                float sourceGunDamage = clone.damageMultiplier;
                float sourceMeleeDamage = clone.setMeleeDamageMultiplier
                    ? clone.meleeDamageMultiplier : sourceGunDamage;
                // 自定义 Boss 的 runtime preset 不在官方 preset 池里，幼体解析时使用
                // Cname_Boss_Red / Cname_Ghost 底模；这里回读各 Boss Config 的既有倍率，
                // 避免三只自定义 Boss 因底模不同而丢失强弱差异。
                if (string.Equals(clone.nameKey, DragonDescendantConfig.BOSS_NAME_KEY, StringComparison.Ordinal))
                {
                    sourceGunDamage = sourceMeleeDamage = DragonDescendantConfig.DamageMultiplier;
                    clone.health = DragonDescendantConfig.BaseHealth;
                }
                else if (string.Equals(clone.nameKey, DragonKingConfig.BossNameKey, StringComparison.Ordinal))
                {
                    sourceGunDamage = sourceMeleeDamage = DragonKingConfig.DamageMultiplier;
                    clone.health = DragonKingConfig.BaseHealth;
                }
                else if (string.Equals(clone.nameKey, PhantomWitchConfig.BossNameKey, StringComparison.Ordinal))
                {
                    sourceGunDamage = sourceMeleeDamage = PhantomWitchConfig.DamageMultiplier;
                    clone.health = PhantomWitchConfig.BaseHealth;
                }
                clone.damageMultiplier = PetNestGrowth.BaseDamageMultiplier(sourceGunDamage);
                clone.setMeleeDamageMultiplier = true;
                clone.meleeDamageMultiplier = PetNestGrowth.BaseDamageMultiplier(sourceMeleeDamage);
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
                character.gameObject.SetActive(false);
                // 三个自定义 Boss 血脉沿用 Boss 本人的专属装备；先在 staging 阶段装好，
                // 激活后玩家看到的第一帧就是完整造型。其它血脉保留官方 preset 的随机装备。
                await EquipCustomBossGearAsync(character, lineageKey);
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
        /// 给自定义 Boss 血脉的崽穿 Boss 专属装备。装备缺失时保留已生成角色，
        /// 不能因为某个可选 bundle 尚未加载而阻断随从入场。
        /// </summary>
        private static async UniTask EquipCustomBossGearAsync(CharacterMainControl character, string lineageKey)
        {
            if (character == null || string.IsNullOrEmpty(lineageKey)) return;

            int[] typeIds = ResolveCustomBossGear(lineageKey);
            if (typeIds == null || typeIds.Length == 0) return;

            string[] slots = string.Equals(lineageKey, PhantomWitchConfig.BossNameKey, StringComparison.Ordinal)
                ? new[] { "MeleeWeapon" }
                : typeIds.Length == 4
                    ? new[] { "Helmat", "Armor", "MeleeWeapon", "PrimaryWeapon" }
                    : new[] { "Helmat", "Armor", "PrimaryWeapon" };
            for (int i = 0; i < typeIds.Length; i++)
            {
                await EquipCustomBossSlotAsync(character, slots[i], typeIds[i]);
            }
        }

        /// <summary>复用官方 Slot.Plug 的替换语义：新物品可用后才卸旧物品，失败保留原装。</summary>
        private static async UniTask EquipCustomBossSlotAsync(CharacterMainControl character, string slotKey, int typeId)
        {
            Item item = null;
            bool equipped = false;
            try
            {
                Item prefab = ItemAssetsCollection.GetPrefab(typeId);
                // 官方 GetPrefab 缺资源时可能给 FallbackItem；非空不等于找到了目标装备。
                if (prefab == null || prefab.TypeID != typeId) return;
                item = await ItemAssetsCollection.InstantiateAsync(typeId);
                if (item == null || item.TypeID != typeId || character == null || character.CharacterItem == null) return;
                if (!PrepareCustomGunMagazine(item)) return;
                Slot slot = character.CharacterItem.Slots.GetSlot(slotKey);
                if (slot == null) return;
                Item replaced;
                slot.Plug(item, out replaced);
                equipped = slot.Content == item;
                if (!equipped) return;
                if (replaced != null && replaced != item) replaced.DestroyTree();
                ItemSetting_Gun gun = item.GetComponent<ItemSetting_Gun>();
                if (gun != null) StoreCustomAmmo(character.CharacterItem.Inventory, gun.TargetBulletID, 300);
                if (slotKey == "PrimaryWeapon" || typeId == PhantomWitchConfig.ReservedScytheTypeId)
                    character.ChangeHoldItem(item);
                ModBehaviour.DevLog("[PetNest] 专属装备已穿戴: " + slotKey + "=" + typeId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] [WARNING] 专属装备穿戴失败: " + typeId + " / " + e.Message);
            }
            finally
            {
                if (!equipped && item != null) item.DestroyTree();
            }
        }

        /// <summary>新枪在挂到角色前装满弹匣；没有可用子弹就保留原武器，避免拿空枪出战。</summary>
        private static bool PrepareCustomGunMagazine(Item item)
        {
            ItemSetting_Gun gun = item.GetComponent<ItemSetting_Gun>();
            if (gun == null) return true;
            if (item.Inventory == null || gun.Capacity <= 0) return false;
            if (gun.GetBulletCount() > 0) return true;
            Item bullet = ItemAssetsCollection.GetPrefab(gun.TargetBulletID);
            if (bullet == null || bullet.TypeID != gun.TargetBulletID || !gun.IsValidBullet(bullet))
            {
                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new[] { GameplayDataSettings.Tags.Bullet };
                filter.minQuality = 1;
                filter.maxQuality = 6;
                filter.caliber = item.Constants.GetString("Caliber", null);
                int[] candidates = ItemAssetsCollection.Search(filter);
                bullet = null;
                if (candidates != null)
                {
                    for (int i = candidates.Length - 1; i >= 0; i--)
                    {
                        Item candidate = ItemAssetsCollection.GetPrefab(candidates[i]);
                        if (candidate == null || candidate.TypeID != candidates[i] || !gun.IsValidBullet(candidate)) continue;
                        bullet = candidate;
                        break;
                    }
                }
            }
            if (bullet == null) return false;
            gun.SetTargetBulletType(bullet.TypeID);
            // 此时新枪未生成手持 agent；_bulletCountCache 的初值仍为 -1，第一次读从弹匣计算。
            if (!StoreCustomAmmo(item.Inventory, bullet.TypeID, gun.Capacity)) return false;
            return gun.BulletCount > 0;
        }

        private static bool StoreCustomAmmo(Inventory inventory, int typeId, int count)
        {
            if (inventory == null || typeId <= 0) return false;
            // 每只崽只在 staging 装一次，最多 16 组，不跑每帧补弹或扫描。
            for (int i = 0; i < 16 && count > 0; i++)
            {
                Item ammo = ItemAssetsCollection.InstantiateSync(typeId);
                if (ammo == null) return false;
                int stack = Math.Min(count, ammo.MaxStackCount);
                if (ammo.TypeID != typeId || stack <= 0)
                {
                    ammo.DestroyTree();
                    return false;
                }
                ammo.StackCount = stack;
                if (!inventory.AddAndMerge(ammo, 0))
                {
                    ammo.DestroyTree();
                    return false;
                }
                count -= stack;
            }
            return count <= 0;
        }

        private static int[] ResolveCustomBossGear(string lineageKey)
        {
            if (string.Equals(lineageKey, DragonDescendantConfig.BOSS_NAME_KEY, StringComparison.Ordinal))
            {
                return new int[]
                {
                    DragonDescendantConfig.DRAGON_HELM_TYPE_ID,
                    DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID,
                    DragonDescendantConfig.DRAGON_BREATH_TYPE_ID
                };
            }
            if (string.Equals(lineageKey, DragonKingConfig.BossNameKey, StringComparison.Ordinal))
            {
                return new int[]
                {
                    DragonKingConfig.DRAGON_KING_HELM_TYPE_ID,
                    DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID,
                    DragonKingConfig.FEN_HUANG_HALBERD_TYPE_ID,
                    DragonKingBossGunConfig.WeaponTypeId
                };
            }
            if (string.Equals(lineageKey, PhantomWitchConfig.BossNameKey, StringComparison.Ordinal))
            {
                return new int[] { PhantomWitchConfig.ReservedScytheTypeId };
            }
            return null;
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

                handle.ModelScale = PetNestGrowth.ModelScale(handle.ModelScale, pet != null ? pet.level : 1);
                ApplyModelScale(handle);
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

        #region 炫彩 / 异色特效

        /// <summary>
        /// 给带炫彩或异色的崽挂上身上的特效。owner 2026-09-20：
        /// 「不同炫彩弄不同的粒子特效，异色则是最豪华的最好看的」；2026-09-22 实测：
        /// 「异色弄的特效要帅不要廉价」。配方与材质口径见 PetNestAuraEffect / PetNestAuraRecipes：
        /// 炫彩第一色决定主元素（赤=龙息火焰……银=镜屑），第二色是一圈反向环绕的点缀；
        /// 异色是金色符文环 + 金星 + 星芒 + 光冠 + 呼吸点光，带炫彩时再叠降强度的元素层。
        /// 普通崽：**一个对象都不创建**，零每帧成本（AGENTS 4.12）。
        /// </summary>
        private static void AttachChromaAura(PetNestCompanionHandle handle, PetNestPetRecord pet)
        {
            if (handle == null || handle.Character == null || pet == null) return;
            if (handle.Aura != null) return;

            bool chroma = PetNestChroma.HasChroma(pet);
            if (!pet.shiny && !chroma) return;

            try
            {
                PetNestChromaColor a = chroma ? PetNestChroma.Find(pet.chromaA) : null;
                PetNestChromaColor b = chroma ? PetNestChroma.Find(pet.chromaB) : null;
                // 特效根挂在角色根下：角色被任何路径销毁时特效一起走，不会留孤儿
                handle.Aura = PetNestAuraEffect.Attach(handle.Character, pet.shiny, a, b);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 崽特效创建失败: " + e.Message);
                handle.Aura = null;
            }
        }

        /// <summary>回收特效。幂等；角色已销毁时特效也已随之销毁（Unity 判空为假），这里只丢引用。</summary>
        private static void DetachChromaAura(PetNestCompanionHandle handle)
        {
            if (handle == null) return;
            PetNestAuraEffect aura = handle.Aura;
            handle.Aura = null;
            if (aura == null) return;
            try
            {
                aura.Dispose();
            }
            catch (Exception)
            {
                // 特效回收失败不阻断角色回收：它是角色的子节点，随后会随角色一起销毁
            }
        }

        #endregion

        /// <summary>
        /// 伤害归一：把幼体的输出压到「锦上添花不改天换地」的区间。
        ///
        /// 克隆自 Boss 的随从会原样继承 Boss 的武器与伤害倍率，不归一会直接抢镜。
        /// 基准倍率上限见 PetNestTuning.CompanionDpsShareTarget；实际 DPS 取决于所持武器与 AI。
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

        /// <summary>经验事务成功后刷新在场崽；不额外回血，模型以血脉基准绝对赋值，避免反复乘大。</summary>
        internal static void RefreshProgression(PetNestCompanionHandle handle, PetNestPetRecord pet)
        {
            if (handle == null || handle.CleanedUp || handle.Character == null || pet == null) return;
            PetNestLineageInfo lineage;
            if (!PetNestLineageCatalog.TryGet(pet.lineageKey, out lineage) || lineage == null) return;
            float healthRatio = handle.Health != null && handle.Health.MaxHealth > 0f
                ? Mathf.Clamp01(handle.Health.CurrentHealth / handle.Health.MaxHealth) : 0f;
            ApplyPetModifiers(handle.Character, pet);
            if (handle.Health != null) handle.Health.SetHealth(handle.Health.MaxHealth * healthRatio);
            handle.ModelScale = PetNestGrowth.ModelScale(lineage.ModelScale, pet.level);
            ApplyModelScale(handle);
            // 层数不变，只在真正升级时重建一次，让元素大小与新体型保持比例。
            DetachChromaAura(handle);
            AttachChromaAura(handle, pet);
        }

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
            int levels = PetNestGrowth.GrowthLevels(pet.level);
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
