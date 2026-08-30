// ============================================================================
// AffixRuntimeService.cs - 词缀锻造运行时行为面（生命周期 / 订阅 / 归因分发）
// ============================================================================
// 模块职责：
//   把"装备上的 AFX_ KV"翻译成"当前生效的词缀列表（context）"，并按 context
//   动态订阅/退订官方静态事件，最后把事件按归因规则分发给各条词缀效果。
//   具体效果实现在同名 partial 文件 AffixRuntimeService_Effects.cs。
//
// 硬约束（AGENTS 4.6 / 4.12 + docs/架构说明/事件订阅生命周期约定.md）：
//   1. 只订这 6 个既有静态事件，零新增 Harmony patch、零新增反射绑定策略：
//        结构事件（EnsureRuntime 时一次性挂上）
//          CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent
//          CharacterMainControl.OnMainCharacterSlotContentChangedEvent
//          LevelManager.OnAfterLevelInitialized
//        战斗事件（按 context 动态增删）
//          Health.OnHurt / Health.OnDead / ItemAgent_Gun.OnMainCharacterShootEvent
//      全部用【命名静态方法】订阅，禁止 lambda，每组配一个私有 bool 幂等守卫。
//   2. context 只由主角自身的 4 个位置重建（手持 + 护甲 + 头盔 + 面罩）。
//      NPC 手持同款武器、仓库/背包里的闲置装备一律不进 context —— 因此
//      **context 为空时战斗订阅必须立即退订**：订阅数与激活词缀严格同生共死。
//   3. 每帧热路径（OnGlobalHurt / OnGlobalDead / OnMainCharacterShoot / TickDrain）
//      零日志、零分配、零 GetComponent；异常整体自吞，避免截断同一静态事件上
//      其它订阅者（图鉴 / 日报 / Mutators / DragonSet / ThunderSet 都在同一条多播链上）。
//   4. 常驻型词缀（鹰目 / 狂血 / 玻璃炮）用运行时 Modifier 挂在角色 CharacterItem 的
//      Stat 上，**绝不**走 EquipmentHelper.AddModifierToItem（那会写进装备的持久
//      ModifierDescription 并进存档、被重铸系统误判为物品自带属性）。幅度永不进存档。
//   5. 死契的持续流失只能直接写 Health.CurrentHealth 并 clamp 最低 1 点血，
//      **绝不能调 Health.Hurt()** —— Hurt 的致死分支里 OnDead 先于 OnHurt 触发，
//      会误触发全 Mod 的击杀链路并把玩家打死（逆鳞时期踩过的坑）。
//
// 已知语义边界：
//   DamageInfo.fromWeaponItemID 是 TypeID 而不是实例 ID。背包里另有一把同型号武器
//   不受影响（context 只收手持那把），但"两把同 TypeID 武器分别附不同词缀"在归因上
//   无法区分。这是官方 API 的天花板，不是缺陷。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 当前激活的一条词缀（context 条目）。只在内存里存在，不进存档。
    /// </summary>
    internal sealed class ActiveAffix
    {
        /// <summary>词缀定义（数值查表口）。</summary>
        internal AffixDefinition Def;
        /// <summary>强度档 1..3。</summary>
        internal int Tier;
        /// <summary>承载该词缀的装备 TypeID，武器归因用。</summary>
        internal int SourceTypeId;
        /// <summary>true = 来自手持武器（需要武器归因）；false = 来自穿戴部位。</summary>
        internal bool FromHeldWeapon;
        /// <summary>下一次允许触发的 Time.time 门槛。</summary>
        internal float NextProcTime;
    }

    /// <summary>
    /// 词缀运行时服务。静态单例形态，生命周期三段式见文件头。
    /// </summary>
    public static partial class AffixRuntimeService
    {
        /// <summary>日志前缀。只在非热路径使用。</summary>
        internal const string LogPrefix = "[AffixForge]";

        /// <summary>
        /// 开火闸的有效窗口（秒）。开火事件每次射击只发一次，霰弹的多个弹丸会
        /// 在窗口内连发多次 OnHurt；窗口外的命中视为近战/环境伤害，退回时间 CD。
        /// </summary>
        private const float ShotGateWindowSeconds = 0.25f;

        /// <summary>死契流失后允许保留的最低血量，绝不触碰死亡分支。</summary>
        private const float DrainFloorHealth = 1f;

        // ---- 订阅守卫（约定：私有 bool + 命名方法，禁 lambda）----
        private static bool _structuralSubscribed;
        private static bool _combatSubscribed;
        private static bool _shootSubscribed;

        /// <summary>分发重入守卫，挡住殉爆 / 荆棘 / 灌能的连环递归。</summary>
        private static bool _dispatching;

        /// <summary>重建重入守卫（重建过程中触发的槽位事件不再递归重建）。</summary>
        private static bool _rebuildScheduled;

        private static readonly List<ActiveAffix> _active = new List<ActiveAffix>();
        private static readonly List<ZombieModeAttributeModifierRecord> _persistentModifiers
            = new List<ZombieModeAttributeModifierRecord>();
        private static readonly List<AffixSlotView> _slotScratch = new List<AffixSlotView>();

        /// <summary>Modifier 归属标识，移除时按此对象定位。</summary>
        private static readonly object ModifierSource = new object();

        private static bool _shotGateOpen;
        private static float _lastShotTime = -999f;
        private static bool _tickerActive;

        // ====================================================================
        // 生命周期三段式
        // ====================================================================

        /// <summary>
        /// 幂等注册。开关关闭时直接进入休眠（等价 ShutdownRuntime）。
        /// </summary>
        public static void EnsureRuntime()
        {
            try
            {
                if (!IsEnabled)
                {
                    ShutdownRuntime();
                    return;
                }

                SubscribeStructural();
                RebuildContext();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [ERROR] EnsureRuntime 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 幂等解绑。退全部订阅、清 context、撤销全部常驻 modifier、销毁 ticker。
        /// 不清 AFX_ KV（开关热切回 true 后立即恢复）。
        /// </summary>
        public static void ShutdownRuntime()
        {
            UnsubscribeStructural();

            _active.Clear();
            RemoveAllPersistentModifiers();

            // _active 已空 → 这两个 Sync 会把战斗订阅与 ticker 一起收掉
            SyncCombatSubscriptions();
            SyncTicker();

            _dispatching = false;
            _shotGateOpen = false;
            _lastShotTime = -999f;
        }

        /// <summary>
        /// 清缓存。内部先 ShutdownRuntime，再清复用容器与下游缓存。
        /// </summary>
        public static void ResetStaticCaches()
        {
            try
            {
                ShutdownRuntime();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] ResetStaticCaches 关停阶段异常: " + e.Message);
            }

            _slotScratch.Clear();
            _persistentModifiers.Clear();
            _rebuildScheduled = false;
            _tickerActive = false;

            try { AffixBuffFactory.ResetStaticCaches(); } catch { }
            try { AffixDefinitions.ResetStaticCaches(); } catch { }
        }

        /// <summary>配置开关。缺配置一律视为关闭，且永不抛。</summary>
        public static bool IsEnabled
        {
            get
            {
                try
                {
                    return ModBehaviour.Instance != null && ModBehaviour.Instance.IsAffixForgeConfiguredEnabled();
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>当前激活词缀条数。门控自检 / F3 调试用。</summary>
        public static int ActiveAffixCount { get { return _active.Count; } }

        /// <summary>战斗事件是否处于订阅态。门控自检 / guard 用。</summary>
        internal static bool IsCombatSubscribed { get { return _combatSubscribed; } }

        /// <summary>开火事件是否处于订阅态。门控自检用。</summary>
        internal static bool IsShootSubscribed { get { return _shootSubscribed; } }

        /// <summary>
        /// 锻造完成后由 AffixForgeSystem 调用。被改的物品可能正在手持/穿戴，
        /// 必须立即重建 context，否则新词缀要等下一次换装才生效。
        /// </summary>
        public static void NotifyItemAffixesChanged(Item item)
        {
            try
            {
                if (!IsEnabled)
                {
                    ShutdownRuntime();
                    return;
                }

                SubscribeStructural();
                RebuildContext();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [ERROR] NotifyItemAffixesChanged 失败: " + e.Message);
            }
        }

        // ====================================================================
        // 结构事件订阅（context 重建源）
        // ====================================================================

        private static void SubscribeStructural()
        {
            if (_structuralSubscribed)
            {
                return;
            }

            try
            {
                CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += OnMainCharacterHoldItemChanged;
                CharacterMainControl.OnMainCharacterSlotContentChangedEvent += OnMainCharacterSlotChanged;
                LevelManager.OnAfterLevelInitialized += OnAfterLevelInitializedRebuild;
                _structuralSubscribed = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [ERROR] 结构事件订阅失败: " + e.Message);
            }
        }

        private static void UnsubscribeStructural()
        {
            if (!_structuralSubscribed)
            {
                return;
            }

            try
            {
                CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= OnMainCharacterHoldItemChanged;
                CharacterMainControl.OnMainCharacterSlotContentChangedEvent -= OnMainCharacterSlotChanged;
                LevelManager.OnAfterLevelInitialized -= OnAfterLevelInitializedRebuild;
            }
            catch (Exception e)
            {
                // 约定要求解绑失败必须留警告，禁止空 catch
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 结构事件退订失败: " + e.Message);
            }

            _structuralSubscribed = false;
        }

        /// <summary>手持物品变化。官方只在 IsMainCharacter 时才发此事件。</summary>
        private static void OnMainCharacterHoldItemChanged(CharacterMainControl character, DuckovItemAgent agent)
        {
            try
            {
                if (character == null || !character.IsMainCharacter)
                {
                    return;
                }

                RebuildContext();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 手持变化重建失败: " + e.Message);
            }
        }

        /// <summary>
        /// 槽位内容变化。必须只认三个穿戴槽，否则弹药/背包每次增删都会触发全量重建。
        /// "Helmat" 是官方拼写，不要"修正"。
        /// </summary>
        private static void OnMainCharacterSlotChanged(CharacterMainControl character, Slot slot)
        {
            try
            {
                if (slot == null)
                {
                    return;
                }

                string key = slot.Key;
                if (key != "Armor" && key != "Helmat" && key != "FaceMask")
                {
                    return;
                }

                RebuildContext();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 槽位变化重建失败: " + e.Message);
            }
        }

        /// <summary>读档 / 切图完成后的全量重扫兜底。</summary>
        private static void OnAfterLevelInitializedRebuild()
        {
            try
            {
                RemoveAllPersistentModifiers();
                RebuildContext();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 关卡初始化后重建失败: " + e.Message);
            }
        }

        // ====================================================================
        // context 重建
        // ====================================================================

        /// <summary>
        /// 全量重扫主角的 4 个装备位。先全撤常驻 modifier 再重挂，避免叠加泄漏。
        /// </summary>
        private static void RebuildContext()
        {
            if (_rebuildScheduled)
            {
                return;
            }

            _rebuildScheduled = true;
            try
            {
                _active.Clear();
                RemoveAllPersistentModifiers();

                if (!IsEnabled)
                {
                    SyncCombatSubscriptions();
                    SyncTicker();
                    return;
                }

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null)
                {
                    SyncCombatSubscriptions();
                    SyncTicker();
                    return;
                }

                Item held = null;
                try
                {
                    DuckovItemAgent agent = main.CurrentHoldItemAgent;
                    held = agent != null ? agent.Item : null;
                }
                catch { }

                CollectFromItem(held, true);
                CollectFromItem(SafeGetSlotItem(main, 0), false);
                CollectFromItem(SafeGetSlotItem(main, 1), false);
                CollectFromItem(SafeGetSlotItem(main, 2), false);

                ApplyPersistentModifiers(main);
                SyncCombatSubscriptions();
                SyncTicker();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [ERROR] RebuildContext 失败: " + e.Message);
            }
            finally
            {
                _rebuildScheduled = false;
            }
        }

        /// <summary>0=护甲 1=头盔 2=面罩。官方 getter 在角色未就绪时可能抛，统一包住。</summary>
        private static Item SafeGetSlotItem(CharacterMainControl main, int which)
        {
            try
            {
                if (which == 0) return main.GetArmorItem();
                if (which == 1) return main.GetHelmatItem();
                return main.GetFaceMaskItem();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把一件装备上的全部有效词缀收进 context。</summary>
        private static void CollectFromItem(Item item, bool held)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                if (!AffixItemData.HasAffixData(item))
                {
                    return;
                }

                // 先清一次：不依赖被调方是否清 buffer，避免 4 个装备位互相串槽
                _slotScratch.Clear();
                AffixItemData.ReadAllSlots(item, _slotScratch);
                if (_slotScratch.Count == 0)
                {
                    return;
                }

                int typeId = item.TypeID;
                for (int i = 0; i < _slotScratch.Count; i++)
                {
                    AffixSlotView view = _slotScratch[i];
                    if (view.IsEmpty)
                    {
                        continue;
                    }

                    // 未知 id fail-open：KV 原样保留，只是不进 context
                    AffixDefinition def = AffixDefinitions.Find(view.AffixId);
                    if (def == null)
                    {
                        continue;
                    }

                    ActiveAffix active = new ActiveAffix();
                    active.Def = def;
                    active.Tier = view.Tier;
                    active.SourceTypeId = typeId;
                    active.FromHeldWeapon = held;
                    active.NextProcTime = 0f;
                    _active.Add(active);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 收集词缀失败: " + e.Message);
            }
        }

        private static bool AnyActiveWithHook(AffixHookMask mask)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                ActiveAffix active = _active[i];
                if (active != null && active.Def != null && (active.Def.Hooks & mask) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyActiveConsumingShotGate()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                ActiveAffix active = _active[i];
                if (active != null && active.Def != null && active.Def.ConsumesShotGate)
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================
        // 战斗事件的动态订阅（与激活词缀同生共死）
        // ====================================================================

        /// <summary>
        /// Health.OnHurt 与 Health.OnDead 用同一个 bool 管理：两者要么都要要么都不要，
        /// 细分收益为零而泄漏风险翻倍。
        /// </summary>
        private static void SyncCombatSubscriptions()
        {
            bool needCombat = AnyActiveWithHook(
                AffixHookMask.OnPlayerHitEnemy | AffixHookMask.OnPlayerHurt | AffixHookMask.OnPlayerKill);
            bool needShoot = needCombat && AnyActiveConsumingShotGate();

            try
            {
                if (needCombat && !_combatSubscribed)
                {
                    Health.OnHurt += OnGlobalHurt;
                    Health.OnDead += OnGlobalDead;
                    _combatSubscribed = true;
                }
                else if (!needCombat && _combatSubscribed)
                {
                    Health.OnHurt -= OnGlobalHurt;
                    Health.OnDead -= OnGlobalDead;
                    _combatSubscribed = false;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 战斗事件订阅同步失败: " + e.Message);
            }

            try
            {
                if (needShoot && !_shootSubscribed)
                {
                    ItemAgent_Gun.OnMainCharacterShootEvent += OnMainCharacterShoot;
                    _shootSubscribed = true;
                }
                else if (!needShoot && _shootSubscribed)
                {
                    ItemAgent_Gun.OnMainCharacterShootEvent -= OnMainCharacterShoot;
                    _shootSubscribed = false;
                    _shotGateOpen = false;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 开火事件订阅同步失败: " + e.Message);
            }
        }

        /// <summary>只有 Tick 类词缀激活时才存在 ticker 对象。</summary>
        private static void SyncTicker()
        {
            bool needTick = AnyActiveWithHook(AffixHookMask.Tick);

            try
            {
                if (needTick && !_tickerActive)
                {
                    AffixRuntimeTicker.EnsureInstance();
                    _tickerActive = true;
                }
                else if (!needTick && _tickerActive)
                {
                    AffixRuntimeTicker.DestroyInstance();
                    _tickerActive = false;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] Ticker 同步失败: " + e.Message);
            }
        }

        // ====================================================================
        // 热路径：三个战斗事件 handler（零日志、零分配）
        // ====================================================================

        /// <summary>
        /// 开火闸。官方每次射击只发一次此事件（不是每弹丸），用它挡住霰弹多弹丸滥用。
        /// </summary>
        private static void OnMainCharacterShoot(ItemAgent_Gun gun)
        {
            _shotGateOpen = true;
            _lastShotTime = Time.time;
        }

        /// <summary>
        /// 受伤分发。注意官方致死路径是 OnDead 先于 OnHurt，且中间已 SetActive(false)，
        /// 所以命中类词缀必须用 !IsDead 排掉致命一击的那次 OnHurt，
        /// 否则一次击杀会同时触发"命中"和"击杀"两套效果。
        /// </summary>
        private static void OnGlobalHurt(Health health, DamageInfo info)
        {
            try
            {
                if (_dispatching || _active.Count == 0 || health == null)
                {
                    return;
                }

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null)
                {
                    return;
                }

                bool victimIsPlayer = health.IsMainCharacterHealth;
                bool gateWasOpen = _shotGateOpen;
                bool gateConsumed = false;

                _dispatching = true;
                try
                {
                    for (int i = 0; i < _active.Count; i++)
                    {
                        ActiveAffix active = _active[i];
                        if (active == null || active.Def == null)
                        {
                            continue;
                        }

                        AffixHookMask hooks = active.Def.Hooks;

                        if (victimIsPlayer)
                        {
                            if ((hooks & AffixHookMask.OnPlayerHurt) == 0)
                            {
                                continue;
                            }

                            if (info.isFromBuffOrEffect || !CanProc(active))
                            {
                                continue;
                            }

                            CommitProc(active);
                            DispatchPlayerHurt(active, ref info, main);
                            continue;
                        }

                        if ((hooks & AffixHookMask.OnPlayerHitEnemy) == 0)
                        {
                            continue;
                        }

                        if (!IsPlayerHitOnEnemy(health, ref info, active, main))
                        {
                            continue;
                        }

                        bool needsGate = active.Def.ConsumesShotGate
                                         && active.FromHeldWeapon
                                         && (Time.time - _lastShotTime) <= ShotGateWindowSeconds;
                        if (needsGate && !gateWasOpen)
                        {
                            continue;
                        }

                        if (!CanProc(active))
                        {
                            continue;
                        }

                        if (needsGate)
                        {
                            gateConsumed = true;
                        }

                        CommitProc(active);
                        DispatchPlayerHitEnemy(active, health, ref info, main);
                    }
                }
                finally
                {
                    _dispatching = false;
                    if (gateConsumed)
                    {
                        _shotGateOpen = false;
                    }
                }
            }
            catch
            {
                // 热路径不写日志：本 handler 与图鉴/日报/Mutators 共享同一条多播链，
                // 抛出去会截断后续订阅者，写日志则会在连射时刷爆输出。
            }
        }

        /// <summary>击杀分发。带重入守卫，挡住殉爆连锁递归。</summary>
        private static void OnGlobalDead(Health health, DamageInfo info)
        {
            try
            {
                if (_dispatching || _active.Count == 0 || health == null)
                {
                    return;
                }

                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null)
                {
                    return;
                }

                // 自建伤害（荆棘 / 殉爆 / 灌能）一律不算击杀，防连环
                if (info.isFromBuffOrEffect)
                {
                    return;
                }

                if (info.fromCharacter == null || !ReferenceEquals(info.fromCharacter, main))
                {
                    return;
                }

                CharacterMainControl dead = health.TryGetCharacter();
                if (dead != null && ReferenceEquals(dead, main))
                {
                    return;
                }

                Vector3 position = dead != null && dead.transform != null
                    ? dead.transform.position
                    : info.damagePoint;

                _dispatching = true;
                try
                {
                    for (int i = 0; i < _active.Count; i++)
                    {
                        ActiveAffix active = _active[i];
                        if (active == null || active.Def == null)
                        {
                            continue;
                        }

                        if ((active.Def.Hooks & AffixHookMask.OnPlayerKill) == 0)
                        {
                            continue;
                        }

                        if (active.FromHeldWeapon && info.fromWeaponItemID != active.SourceTypeId)
                        {
                            continue;
                        }

                        if (!CanProc(active))
                        {
                            continue;
                        }

                        CommitProc(active);
                        DispatchPlayerKill(active, position, main);
                    }
                }
                finally
                {
                    _dispatching = false;
                }
            }
            catch
            {
                // 同 OnGlobalHurt：热路径静默
            }
        }

        // ====================================================================
        // 归因谓词与 CD（全部 O(1) 零分配）
        // ====================================================================

        /// <summary>主角对敌人造成的直接伤害。</summary>
        private static bool IsPlayerHitOnEnemy(Health victim, ref DamageInfo info, ActiveAffix active, CharacterMainControl main)
        {
            if (info.fromCharacter == null || !ReferenceEquals(info.fromCharacter, main))
            {
                return false;
            }

            // 自建伤害不再二次触发，防循环
            if (info.isFromBuffOrEffect)
            {
                return false;
            }

            // 致命一击那次 OnHurt 归"击杀"，不归"命中"
            if (victim.IsMainCharacterHealth || victim.IsDead)
            {
                return false;
            }

            if (victim.team == Teams.player)
            {
                return false;
            }

            if (active.FromHeldWeapon && info.fromWeaponItemID != active.SourceTypeId)
            {
                return false;
            }

            return true;
        }

        private static bool CanProc(ActiveAffix active)
        {
            return active.Def.ProcCooldownSeconds <= 0f || Time.time >= active.NextProcTime;
        }

        private static void CommitProc(ActiveAffix active)
        {
            if (active.Def.ProcCooldownSeconds > 0f)
            {
                active.NextProcTime = Time.time + active.Def.ProcCooldownSeconds;
            }
        }
    }
}
