// ============================================================================
// NewWeaponEquipState.cs - 五把新武器的热路径判据（装备状态缓存 + 命中归因）
// ============================================================================
// 本文件两个类型都是「给 Health.OnHurt 回调用的判据」，放一处便于一起读：
//   NewWeaponEquipState —— 主角当前手持 / 佩戴了哪件新武器（带缓存）
//   NewWeaponAttribution —— 这次命中算不算「打在敌人身上」（无状态谓词）
// ============================================================================
// 为什么需要本文件：
//   毒蛇匕首 / 召唤法杖判「手持」、能量盾 / 雷电戒指判「佩戴」，此前各写一份：
//     ViperDaggerRuntime.IsHoldingViperDagger      —— GetMeleeWeapon + CurrentHoldItemAgent
//     SummonStaffManager.IsHoldingSummonStaff      —— 与上面逐字相同
//     EnergyShieldRuntime.IsEquippingEnergyShield  —— 遍历 CharacterItem.Slots 找 "Totem*"
//     ThunderRingRuntime.IsEquippingThunderRing    —— 同上，另加一份自己的单帧缓存
//   四份实现三种写法，其中图腾那两份是**每次受击都遍历一遍全部槽位**：既是重复代码，
//   也违反 AGENTS §4.12「装备专属的扫描只在实际手持/穿戴时启动」。
//
// 做法：
//   槽位与手持只在官方发出结构事件时置脏，查询时惰性重扫一次，之后全是 O(1) 读字段。
//   订阅的三个事件与 AffixRuntimeService 完全一致（那条链已在生产里跑过）：
//     CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent
//     CharacterMainControl.OnMainCharacterSlotContentChangedEvent
//     LevelManager.OnAfterLevelInitialized
//   另外每次查询都比一次 CharacterMainControl.Main（LevelManager.Instance.MainCharacter，
//   纯字段读）：主角实例换了就强制重扫。这样即使某条事件时序没赶上，过图 / 复活也不会读到陈旧值。
//
// 硬约束：
//   1. 命名静态方法订阅 + 私有 bool 幂等，退订路径在 NewWeaponRuntime.Cleanup（AGENTS §4.6）。
//   2. 只认主角自己的手持与图腾槽。NPC 手持同款武器、仓库/背包里的闲置装备一律不算。
//   3. 本类零 Harmony、零反射、零 Unity 对象持有（只存 TypeID 与一个用于比对的角色引用）。
//   4. 扩新武器只要往 TrackedTotemTypeIds 里加一个 TypeID，或直接用 IsHolding(typeId)。
// ============================================================================

using System;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    /// <summary>五把新武器的手持 / 佩戴状态缓存。查询 O(1)，重扫只在结构事件后发生一次。</summary>
    internal static class NewWeaponEquipState
    {
        /// <summary>官方图腾槽的 Key 前缀（Totem1 / Totem2 …）。</summary>
        private const string TotemSlotKeyPrefix = "Totem";

        /// <summary>需要追踪佩戴状态的图腾类 TypeID。新增图腾类新武器时往这里加一个即可。</summary>
        private static readonly int[] TrackedTotemTypeIds =
        {
            NewWeaponIds.EnergyShieldTypeId,
            NewWeaponIds.ThunderRingTypeId
        };

        private static readonly bool[] totemEquipped = new bool[TrackedTotemTypeIds.Length];

        private static bool isSubscribed;
        private static bool isDirty = true;
        private static CharacterMainControl cachedPlayer;
        private static int heldWeaponTypeId;

        // ====================================================================
        // 生命周期
        // ====================================================================

        /// <summary>订阅结构事件。幂等。</summary>
        internal static void Subscribe()
        {
            if (isSubscribed) return;

            try
            {
                CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += OnHoldItemChanged;
                CharacterMainControl.OnMainCharacterSlotContentChangedEvent += OnSlotContentChanged;
                LevelManager.OnAfterLevelInitialized += OnAfterLevelInitialized;
                isSubscribed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeaponEquipState] [WARNING] 结构事件订阅失败: " + e.Message);
            }
        }

        /// <summary>退订结构事件并清缓存。幂等。</summary>
        internal static void Unsubscribe()
        {
            if (!isSubscribed)
            {
                ResetStaticCaches();
                return;
            }

            try
            {
                CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= OnHoldItemChanged;
                CharacterMainControl.OnMainCharacterSlotContentChangedEvent -= OnSlotContentChanged;
                LevelManager.OnAfterLevelInitialized -= OnAfterLevelInitialized;
            }
            catch (Exception e)
            {
                // 约定要求解绑失败留警告，禁止空 catch
                ModBehaviour.DevLog("[NewWeaponEquipState] [WARNING] 结构事件退订失败: " + e.Message);
            }

            isSubscribed = false;
            ResetStaticCaches();
        }

        /// <summary>清缓存并置脏。切图 / 宿主重建时调用。</summary>
        internal static void ResetStaticCaches()
        {
            isDirty = true;
            cachedPlayer = null;
            heldWeaponTypeId = 0;
            for (int i = 0; i < totemEquipped.Length; i++)
            {
                totemEquipped[i] = false;
            }
        }

        /// <summary>手工置脏。装备被代码直接改写（非官方槽位事件）时调用。</summary>
        internal static void MarkDirty()
        {
            isDirty = true;
        }

        // ====================================================================
        // 查询（热路径，全部 O(1)）
        // ====================================================================

        /// <summary>主角当前手持的物品是否是指定 TypeID。</summary>
        internal static bool IsHolding(int typeId)
        {
            if (typeId == 0) return false;
            EnsureFresh();
            return heldWeaponTypeId == typeId;
        }

        /// <summary>主角的图腾槽里是否装着指定 TypeID。仅支持 TrackedTotemTypeIds 里登记过的 id。</summary>
        internal static bool IsTotemEquipped(int typeId)
        {
            int index = IndexOfTrackedTotem(typeId);
            if (index < 0) return false;
            EnsureFresh();
            return totemEquipped[index];
        }

        /// <summary>主角当前手持物品的 TypeID；空手为 0。Dev 诊断用。</summary>
        internal static int HeldWeaponTypeId
        {
            get
            {
                EnsureFresh();
                return heldWeaponTypeId;
            }
        }

        // ====================================================================
        // 内部
        // ====================================================================

        private static int IndexOfTrackedTotem(int typeId)
        {
            for (int i = 0; i < TrackedTotemTypeIds.Length; i++)
            {
                if (TrackedTotemTypeIds[i] == typeId) return i;
            }
            return -1;
        }

        /// <summary>
        /// 缓存过期时重扫一次。主角实例变化也算过期——过图后官方会重建主角与 CharacterItem，
        /// 只靠事件的话有可能读到上一条命的装备。
        /// </summary>
        private static void EnsureFresh()
        {
            CharacterMainControl player = null;
            try { player = CharacterMainControl.Main; }
            catch { player = null; }

            if (!isDirty && ReferenceEquals(player, cachedPlayer))
            {
                return;
            }

            Refresh(player);
        }

        private static void Refresh(CharacterMainControl player)
        {
            isDirty = false;
            cachedPlayer = player;
            heldWeaponTypeId = 0;
            for (int i = 0; i < totemEquipped.Length; i++)
            {
                totemEquipped[i] = false;
            }

            if (player == null) return;

            try
            {
                // GetMeleeWeapon() 返回的就是 agentHolder.CurrentHoldMeleeWeapon（官方源码
                // CharacterMainControl.cs:1112），与 CurrentHoldItemAgent 同属「当前手上这件」；
                // 两条都查是为了兼容近战代理与通用代理挂法不同的情况。
                ItemAgent_MeleeWeapon melee = player.GetMeleeWeapon();
                if (melee != null && melee.Item != null)
                {
                    heldWeaponTypeId = melee.Item.TypeID;
                }

                if (heldWeaponTypeId == 0)
                {
                    DuckovItemAgent holdAgent = player.CurrentHoldItemAgent;
                    if (holdAgent != null && holdAgent.Item != null)
                    {
                        heldWeaponTypeId = holdAgent.Item.TypeID;
                    }
                }
            }
            catch (Exception)
            {
                // 角色尚未就绪：按空手处理，下次查询会因为 player 引用变化再扫一次
            }

            try
            {
                Item characterItem = player.CharacterItem;
                if (characterItem == null || characterItem.Slots == null) return;

                foreach (Slot slot in characterItem.Slots)
                {
                    if (slot == null || slot.Content == null) continue;
                    string key = slot.Key;
                    if (key == null || !key.StartsWith(TotemSlotKeyPrefix, StringComparison.Ordinal)) continue;

                    int index = IndexOfTrackedTotem(slot.Content.TypeID);
                    if (index >= 0) totemEquipped[index] = true;
                }
            }
            catch (Exception)
            {
                // 槽位集合不可用时按「都没戴」处理，不影响玩法结算（只会少触发）
            }
        }

        // ---- 事件 handler（命名方法，禁 lambda）----

        private static void OnHoldItemChanged(CharacterMainControl character, DuckovItemAgent agent)
        {
            isDirty = true;
        }

        private static void OnSlotContentChanged(CharacterMainControl character, Slot slot)
        {
            isDirty = true;
        }

        private static void OnAfterLevelInitialized()
        {
            isDirty = true;
        }
    }

    /// <summary>
    /// 新武器的命中归因谓词。无状态、零分配，供毒蛇匕首与雷电戒指共用。
    ///
    /// 为什么必须有：`Health.OnHurt` 是全局多播链，链上会出现玩家自己的召唤物，
    /// 以及本 Mod 自己派发的 buff/效果伤害（荆棘反弹、殉爆、毒爆发、套装反震）。
    /// 不过滤的话，「打中敌人才触发」的机制会被自己的 DoT 和误伤白白消耗掉。
    /// 口径与 <c>AffixRuntimeService.IsPlayerHitOnEnemy</c> 逐条一致。
    ///
    /// 刻意**不**排除 <c>Teams.middle</c>：可破坏木箱 / 木桶走的是官方 <c>SimpleHealth</c>
    /// （见官方 Breakable.cs），根本不进这条 <c>Health.OnHurt</c> 链；而「中立档」在海岛更新后
    /// 会出现在部分 Boss 预设上（见 ModBehaviour 的敌对性安全网注释），排掉它反而可能
    /// 让这些 Boss 身上的机制静默失效。
    /// </summary>
    internal static class NewWeaponAttribution
    {
        /// <summary>
        /// 受击目标是否算「敌人」：活着、不是主角、也不是玩家阵营（含召唤物与友军）。
        /// </summary>
        internal static bool IsHostileVictim(Health victim)
        {
            if (victim == null || victim.IsDead) return false;
            if (victim.IsMainCharacterHealth) return false;
            return victim.team != Teams.player;
        }

        /// <summary>
        /// 这次伤害是否是「主角用武器直接打出去的一击」。
        /// 自建伤害（isFromBuffOrEffect）一律不算，否则毒爆发 / 荆棘 / 殉爆会互相触发。
        /// </summary>
        internal static bool IsPlayerDirectHit(Health victim, ref DamageInfo info, CharacterMainControl player)
        {
            if (player == null) return false;
            if (info.isFromBuffOrEffect) return false;
            if (info.fromCharacter == null || !ReferenceEquals(info.fromCharacter, player)) return false;
            return IsHostileVictim(victim);
        }
    }
}
