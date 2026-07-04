// ============================================================================
// MutatorDefinitions.cs - 每局变异词条池定义
// ============================================================================
// 模块说明：
//   定义所有可用的变异词条及其应用/清理逻辑。
//   词条分三类：敌人强化、玩家增益、环境规则（含死亡触发）。
//   每条词条只改一个维度，效果一句话说清。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;

namespace BossRush
{
    /// <summary>
    /// 词条分类
    /// </summary>
    public enum MutatorCategory
    {
        EnemyBuff,        // 敌人强化
        PlayerBoon,       // 玩家增益（幸运词条）
        EnvironmentRule   // 环境规则
    }

    /// <summary>
    /// 词条运行时上下文（存储应用过程中的引用，用于清理）
    /// </summary>
    public class MutatorContext
    {
        public CharacterMainControl Player;

        /// <summary>玩家身上的 (Stat, Modifier) 记录（退局清理）</summary>
        public List<(Stat stat, Modifier modifier)> PlayerAppliedModifiers
            = new List<(Stat, Modifier)>();

        /// <summary>已应用到敌人的 (Stat, Modifier) 记录（退局清理仍存活的敌人）</summary>
        public List<(Stat stat, Modifier modifier)> EnemyAppliedModifiers
            = new List<(Stat, Modifier)>();

        /// <summary>敌人生成时调用的回调列表（EnemyBuff 类词条注册到这里）</summary>
        public List<Action<CharacterMainControl>> EnemySpawnCallbacks
            = new List<Action<CharacterMainControl>>();

        /// <summary>
        /// 敌人被玩家击杀时调用的回调列表（嗜血/殉爆 等死亡触发词条注册到这里）。
        /// 参数为死亡敌人的角色（可能为 null）与其死亡位置。
        /// 由 MutatorManager 统一订阅 Health.OnDead 静态事件并转发，退局时统一退订。
        /// </summary>
        public List<Action<CharacterMainControl, UnityEngine.Vector3>> EnemyKilledCallbacks
            = new List<Action<CharacterMainControl, UnityEngine.Vector3>>();
    }

    /// <summary>
    /// 单条变异词条定义
    /// </summary>
    public class MutatorDefinition
    {
        public string Id;
        public string NameCn;          // 中文名
        public string NameEn;          // 英文名
        public string DescCn;          // 中文描述
        public string DescEn;          // 英文描述
        public MutatorCategory Category;
        public Func<string, bool> IsAllowedForMode; // null = 全模式可抽
        public Action<MutatorContext> OnApply;   // 应用回调
        public Action<MutatorContext> OnRemove;  // 清理回调

        /// <summary>检查该词条是否允许在指定模式中抽取</summary>
        public bool AllowsMode(string modeTag)
        {
            return IsAllowedForMode == null || IsAllowedForMode(modeTag);
        }

        /// <summary>获取本地化显示名</summary>
        public string GetDisplayName()
        {
            return L10n.T(NameCn, NameEn);
        }

        /// <summary>获取本地化描述</summary>
        public string GetDescription()
        {
            return L10n.T(DescCn, DescEn);
        }
    }

    /// <summary>
    /// 词条池：定义所有可用的变异词条（28条）
    /// </summary>
    public static class MutatorPool
    {
        // 统一的 Modifier source 标识（用于识别来源）
        private static readonly object MutatorSource = new object();

        public static readonly MutatorDefinition[] All = new MutatorDefinition[]
        {
            // ═══════════════════════════════════════════
            // 敌人强化类（9条）
            // ═══════════════════════════════════════════

            new MutatorDefinition
            {
                Id = "enemy_speed_up",
                NameCn = "疾风骤雨",
                NameEn = "Swift Storm",
                DescCn = "敌人移动速度 +30%",
                DescEn = "Enemy move speed +30%",
                Category = MutatorCategory.EnemyBuff,
                OnApply = ctx =>
                {
                    // 注册敌人生成回调，给每个新生成的敌人加移速
                    ctx.EnemySpawnCallbacks.Add(enemy =>
                    {
                        Item enemyItem = enemy.CharacterItem;
                        if (enemyItem == null) return;
                        Stat walk = enemyItem.GetStat("WalkSpeed");
                        Stat run = enemyItem.GetStat("RunSpeed");
                        if (walk != null)
                        {
                            var mod = new Modifier(ModifierType.PercentageAdd, 0.3f, MutatorSource);
                            walk.AddModifier(mod);
                            ctx.EnemyAppliedModifiers.Add((walk, mod));
                        }
                        if (run != null)
                        {
                            var mod = new Modifier(ModifierType.PercentageAdd, 0.3f, MutatorSource);
                            run.AddModifier(mod);
                            ctx.EnemyAppliedModifiers.Add((run, mod));
                        }
                    });
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 EnemyAppliedModifiers */ }
            },

            new MutatorDefinition
            {
                Id = "enemy_health_up",
                NameCn = "铁壁铜墙",
                NameEn = "Iron Fortress",
                DescCn = "敌人血量 +50%",
                DescEn = "Enemy HP +50%",
                Category = MutatorCategory.EnemyBuff,
                OnApply = ctx =>
                {
                    ctx.EnemySpawnCallbacks.Add(enemy =>
                    {
                        Item enemyItem = enemy.CharacterItem;
                        if (enemyItem == null) return;
                        Stat hpStat = enemyItem.GetStat("MaxHealth");
                        if (hpStat == null) return;
                        // 用 PercentageAdd 而不是 Add(BaseValue * 0.5f)：
                        // 1) 与 BossRush 全局 Boss 数值倍率走加法叠加（不是乘性叠乘），避免 Boss 血量爆炸
                        // 2) 与其它 Modifier 行为一致（rougeLite 标准做法）
                        var mod = new Modifier(ModifierType.PercentageAdd, 0.5f, MutatorSource);
                        hpStat.AddModifier(mod);
                        ctx.EnemyAppliedModifiers.Add((hpStat, mod));
                        // 同步当前血量，避免血条出现"少 50%"显示空缺：
                        // 直接按旧 BaseValue 的 50% 回血，与上面的 PercentageAdd 0.5 对应
                        if (enemy.Health != null)
                        {
                            float healAmount = hpStat.BaseValue * 0.5f;
                            enemy.Health.AddHealth(healAmount);
                        }
                    });
                },
                OnRemove = ctx => { }
            },

            new MutatorDefinition
            {
                Id = "enemy_attack_fast",
                NameCn = "弹雨倾盆",
                NameEn = "Bullet Rain",
                DescCn = "敌人射速 +25%",
                DescEn = "Enemy fire rate +25%",
                Category = MutatorCategory.EnemyBuff,
                OnApply = ctx =>
                {
                    ctx.EnemySpawnCallbacks.Add(enemy =>
                    {
                        Item enemyItem = enemy.CharacterItem;
                        if (enemyItem == null) return;
                        Stat shootStat = enemyItem.GetStat("GunShootSpeedMultiplier");
                        if (shootStat == null) return;
                        var mod = new Modifier(ModifierType.PercentageAdd, 0.25f, MutatorSource);
                        shootStat.AddModifier(mod);
                        ctx.EnemyAppliedModifiers.Add((shootStat, mod));
                    });
                },
                OnRemove = ctx => { }
            },

            new MutatorDefinition
            {
                Id = "enemy_giant",
                NameCn = "巨灵兵",
                NameEn = "Giants",
                DescCn = "敌人体型变大，血量 +40%",
                DescEn = "Enemies grow huge, HP +40%",
                Category = MutatorCategory.EnemyBuff,
                OnApply = ctx =>
                {
                    ctx.EnemySpawnCallbacks.Add(enemy =>
                    {
                        // 体型放大（纯视觉 + 碰撞体，敌人退场即随对象销毁，无需清理）
                        if (enemy.transform != null)
                        {
                            enemy.transform.localScale = enemy.transform.localScale * 1.4f;
                        }

                        Item enemyItem = enemy.CharacterItem;
                        if (enemyItem == null) return;
                        Stat hpStat = enemyItem.GetStat("MaxHealth");
                        if (hpStat == null) return;
                        var mod = new Modifier(ModifierType.PercentageAdd, 0.4f, MutatorSource);
                        hpStat.AddModifier(mod);
                        ctx.EnemyAppliedModifiers.Add((hpStat, mod));
                        // 同步当前血量，避免血条出现空缺（与 enemy_health_up 同一做法）
                        if (enemy.Health != null)
                        {
                            enemy.Health.AddHealth(hpStat.BaseValue * 0.4f);
                        }
                    });
                },
                OnRemove = ctx => { }
            },

            new MutatorDefinition
            {
                Id = "enemy_swarm",
                NameCn = "群鼠战术",
                NameEn = "Ratswarm",
                DescCn = "敌人体型缩小，移动速度 +45%",
                DescEn = "Enemies shrink, move speed +45%",
                Category = MutatorCategory.EnemyBuff,
                OnApply = ctx =>
                {
                    ctx.EnemySpawnCallbacks.Add(enemy =>
                    {
                        if (enemy.transform != null)
                        {
                            enemy.transform.localScale = enemy.transform.localScale * 0.6f;
                        }

                        Item enemyItem = enemy.CharacterItem;
                        if (enemyItem == null) return;
                        Stat walk = enemyItem.GetStat("WalkSpeed");
                        Stat run = enemyItem.GetStat("RunSpeed");
                        if (walk != null)
                        {
                            var mod = new Modifier(ModifierType.PercentageAdd, 0.45f, MutatorSource);
                            walk.AddModifier(mod);
                            ctx.EnemyAppliedModifiers.Add((walk, mod));
                        }
                        if (run != null)
                        {
                            var mod = new Modifier(ModifierType.PercentageAdd, 0.45f, MutatorSource);
                            run.AddModifier(mod);
                            ctx.EnemyAppliedModifiers.Add((run, mod));
                        }
                    });
                },
                OnRemove = ctx => { }
            },

            new MutatorDefinition
            {
                Id = "enemy_bloodhound",
                NameCn = "嗜血猎犬",
                NameEn = "Bloodhounds",
                DescCn = "敌人永久锁定你，视野拉满",
                DescEn = "Enemies lock onto you, infinite sight",
                Category = MutatorCategory.EnemyBuff,
                OnApply = ctx =>
                {
                    ctx.EnemySpawnCallbacks.Add(enemy =>
                    {
                        // AI 字段直接改，组件随敌人对象销毁，无需清理
                        AICharacterController ai = enemy.GetComponentInChildren<AICharacterController>();
                        if (ai == null) return;
                        ai.forceTracePlayerDistance = 99999f; // 永不脱战
                        ai.noticed = true;                    // 立刻进入警觉
                    });
                },
                OnRemove = ctx => { }
            },

            new MutatorDefinition
            {
                Id = "enemy_vicious",
                NameCn = "穷凶极恶",
                NameEn = "Vicious",
                DescCn = "敌人造成的伤害 +30%",
                DescEn = "Enemies deal +30% damage",
                Category = MutatorCategory.EnemyBuff,
                OnApply = ctx =>
                {
                    ctx.EnemySpawnCallbacks.Add(enemy =>
                    {
                        Item enemyItem = enemy.CharacterItem;
                        if (enemyItem == null) return;
                        // 伤害倍率走加法（与 glass_cannon / ModeFBounty 同款）：基础 1.0 → +0.3 即 +30%
                        Stat gunDmg = enemyItem.GetStat("GunDamageMultiplier");
                        if (gunDmg != null)
                        {
                            var mod = new Modifier(ModifierType.Add, 0.3f, MutatorSource);
                            gunDmg.AddModifier(mod);
                            ctx.EnemyAppliedModifiers.Add((gunDmg, mod));
                        }
                        Stat meleeDmg = enemyItem.GetStat("MeleeDamageMultiplier");
                        if (meleeDmg != null)
                        {
                            var mod = new Modifier(ModifierType.Add, 0.3f, MutatorSource);
                            meleeDmg.AddModifier(mod);
                            ctx.EnemyAppliedModifiers.Add((meleeDmg, mod));
                        }
                    });
                },
                OnRemove = ctx => { }
            },

            new MutatorDefinition
            {
                Id = "enemy_sharpshooter",
                NameCn = "神枪敌军",
                NameEn = "Enemy Marksman",
                DescCn = "敌人枪械散射 -25%",
                DescEn = "Enemy gun scatter -25%",
                Category = MutatorCategory.EnemyBuff,
                OnApply = ctx =>
                {
                    ctx.EnemySpawnCallbacks.Add(enemy =>
                    {
                        Item enemyItem = enemy.CharacterItem;
                        if (enemyItem == null) return;
                        // 直接压低敌人 GunScatterMultiplier，让敌人枪线更紧、更准
                        Stat scatterStat = enemyItem.GetStat("GunScatterMultiplier");
                        if (scatterStat == null) return;
                        var mod = new Modifier(ModifierType.PercentageAdd, -0.25f, MutatorSource);
                        scatterStat.AddModifier(mod);
                        ctx.EnemyAppliedModifiers.Add((scatterStat, mod));
                    });
                },
                OnRemove = ctx => { }
            },

            new MutatorDefinition
            {
                Id = "frenzy",
                NameCn = "血腥狂欢",
                NameEn = "Frenzy",
                DescCn = "敌人移速和射速各 +20%",
                DescEn = "Enemy move speed and fire rate +20%",
                Category = MutatorCategory.EnemyBuff,
                OnApply = ctx =>
                {
                    ctx.EnemySpawnCallbacks.Add(enemy =>
                    {
                        Item enemyItem = enemy.CharacterItem;
                        if (enemyItem == null) return;

                        Stat walk = enemyItem.GetStat("WalkSpeed");
                        if (walk != null)
                        {
                            var mod = new Modifier(ModifierType.PercentageAdd, 0.2f, MutatorSource);
                            walk.AddModifier(mod);
                            ctx.EnemyAppliedModifiers.Add((walk, mod));
                        }

                        Stat run = enemyItem.GetStat("RunSpeed");
                        if (run != null)
                        {
                            var mod = new Modifier(ModifierType.PercentageAdd, 0.2f, MutatorSource);
                            run.AddModifier(mod);
                            ctx.EnemyAppliedModifiers.Add((run, mod));
                        }

                        Stat shootStat = enemyItem.GetStat("GunShootSpeedMultiplier");
                        if (shootStat != null)
                        {
                            var mod = new Modifier(ModifierType.PercentageAdd, 0.2f, MutatorSource);
                            shootStat.AddModifier(mod);
                            ctx.EnemyAppliedModifiers.Add((shootStat, mod));
                        }
                    });
                },
                OnRemove = ctx => { }
            },

            // ═══════════════════════════════════════════
            // 玩家增益类（幸运词条，11条）
            // ═══════════════════════════════════════════

            new MutatorDefinition
            {
                Id = "player_swift",
                NameCn = "闪电步伐",
                NameEn = "Fleet Footed",
                DescCn = "玩家移动速度 +35%",
                DescEn = "Player move speed +35%",
                Category = MutatorCategory.PlayerBoon,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;
                    Stat walk = playerItem.GetStat("WalkSpeed");
                    Stat run = playerItem.GetStat("RunSpeed");
                    if (walk != null)
                    {
                        var mod = new Modifier(ModifierType.PercentageAdd, 0.35f, MutatorSource);
                        walk.AddModifier(mod);
                        ctx.PlayerAppliedModifiers.Add((walk, mod));
                    }
                    if (run != null)
                    {
                        var mod = new Modifier(ModifierType.PercentageAdd, 0.35f, MutatorSource);
                        run.AddModifier(mod);
                        ctx.PlayerAppliedModifiers.Add((run, mod));
                    }
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 PlayerAppliedModifiers */ }
            },

            new MutatorDefinition
            {
                Id = "player_sharpshooter",
                NameCn = "百步穿杨",
                NameEn = "Sharpshooter",
                DescCn = "玩家枪械暴击率 +30%",
                DescEn = "Player gun crit rate +30%",
                Category = MutatorCategory.PlayerBoon,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;
                    Stat critStat = playerItem.GetStat("GunCritRateGain");
                    if (critStat == null) return;
                    // GunCritRateGain 走加法（与 ZombieMode 暴击专注同款）
                    var mod = new Modifier(ModifierType.Add, 0.3f, MutatorSource);
                    critStat.AddModifier(mod);
                    ctx.PlayerAppliedModifiers.Add((critStat, mod));
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 PlayerAppliedModifiers */ }
            },

            new MutatorDefinition
            {
                Id = "player_rapid_fire",
                NameCn = "疾速扳机",
                NameEn = "Trigger Discipline",
                DescCn = "玩家射速 +25%",
                DescEn = "Player fire rate +25%",
                Category = MutatorCategory.PlayerBoon,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;
                    Stat shootStat = playerItem.GetStat("GunShootSpeedMultiplier");
                    if (shootStat == null) return;
                    var mod = new Modifier(ModifierType.PercentageAdd, 0.25f, MutatorSource);
                    shootStat.AddModifier(mod);
                    ctx.PlayerAppliedModifiers.Add((shootStat, mod));
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 PlayerAppliedModifiers */ }
            },

            new MutatorDefinition
            {
                Id = "player_steady_aim",
                NameCn = "稳如磐石",
                NameEn = "Steady Aim",
                DescCn = "玩家枪械散射 -30%",
                DescEn = "Player gun scatter -30%",
                Category = MutatorCategory.PlayerBoon,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;
                    Stat scatterStat = playerItem.GetStat("GunScatterMultiplier");
                    if (scatterStat == null) return;
                    var mod = new Modifier(ModifierType.PercentageAdd, -0.3f, MutatorSource);
                    scatterStat.AddModifier(mod);
                    ctx.PlayerAppliedModifiers.Add((scatterStat, mod));
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 PlayerAppliedModifiers */ }
            },

            new MutatorDefinition
            {
                Id = "player_fast_reload",
                NameCn = "快速换弹",
                NameEn = "Fast Hands",
                DescCn = "换弹速度 +40%",
                DescEn = "Reload speed +40%",
                Category = MutatorCategory.PlayerBoon,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;
                    Stat reloadStat = playerItem.GetStat("ReloadSpeedGain");
                    if (reloadStat == null) return;
                    var mod = new Modifier(ModifierType.Add, 0.4f, MutatorSource);
                    reloadStat.AddModifier(mod);
                    ctx.PlayerAppliedModifiers.Add((reloadStat, mod));
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 PlayerAppliedModifiers */ }
            },

            new MutatorDefinition
            {
                Id = "player_crit_damage",
                NameCn = "致命一击",
                NameEn = "Lethal Strike",
                DescCn = "玩家枪械暴伤 +50%",
                DescEn = "Player gun crit damage +50%",
                Category = MutatorCategory.PlayerBoon,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;
                    // GunCritDamageGain 会进入 (1f + gain) 乘区，+0.5 即 +50% 暴伤
                    Stat critDamageStat = playerItem.GetStat("GunCritDamageGain");
                    if (critDamageStat == null) return;
                    var mod = new Modifier(ModifierType.Add, 0.5f, MutatorSource);
                    critDamageStat.AddModifier(mod);
                    ctx.PlayerAppliedModifiers.Add((critDamageStat, mod));
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 PlayerAppliedModifiers */ }
            },

            new MutatorDefinition
            {
                Id = "player_long_shot",
                NameCn = "千里狙",
                NameEn = "Long Shot",
                DescCn = "玩家枪械射程 +30%",
                DescEn = "Player gun range +30%",
                Category = MutatorCategory.PlayerBoon,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;
                    Stat distanceStat = playerItem.GetStat("GunDistanceMultiplier");
                    if (distanceStat == null) return;
                    var mod = new Modifier(ModifierType.PercentageAdd, 0.3f, MutatorSource);
                    distanceStat.AddModifier(mod);
                    ctx.PlayerAppliedModifiers.Add((distanceStat, mod));
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 PlayerAppliedModifiers */ }
            },

            new MutatorDefinition
            {
                Id = "player_melee_master",
                NameCn = "近战宗师",
                NameEn = "Melee Master",
                DescCn = "玩家近战伤害 +40%",
                DescEn = "Player melee damage +40%",
                Category = MutatorCategory.PlayerBoon,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;
                    Stat meleeDmgStat = playerItem.GetStat("MeleeDamageMultiplier");
                    if (meleeDmgStat == null) return;
                    var mod = new Modifier(ModifierType.Add, 0.4f, MutatorSource);
                    meleeDmgStat.AddModifier(mod);
                    ctx.PlayerAppliedModifiers.Add((meleeDmgStat, mod));
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 PlayerAppliedModifiers */ }
            },

            new MutatorDefinition
            {
                Id = "player_tank",
                NameCn = "铜皮铁骨",
                NameEn = "Tank",
                DescCn = "玩家血量 +30%",
                DescEn = "Player max HP +30%",
                Category = MutatorCategory.PlayerBoon,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;
                    Stat maxHealthStat = playerItem.GetStat("MaxHealth");
                    if (maxHealthStat == null) return;

                    float oldMaxHealth = ctx.Player != null && ctx.Player.Health != null
                        ? ctx.Player.Health.MaxHealth
                        : -1f;

                    var mod = new Modifier(ModifierType.PercentageAdd, 0.3f, MutatorSource);
                    maxHealthStat.AddModifier(mod);
                    ctx.PlayerAppliedModifiers.Add((maxHealthStat, mod));

                    SyncCurrentHealthWithRaisedMax(ctx.Player, oldMaxHealth);
                },
                OnRemove = ctx => { ClampCurrentHealthToMax(ctx != null ? ctx.Player : null); }
            },

            new MutatorDefinition
            {
                Id = "player_swift_heal",
                NameCn = "妙手回春",
                NameEn = "Field Medic",
                DescCn = "治疗效果 +50%",
                DescEn = "Healing +50%",
                Category = MutatorCategory.PlayerBoon,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;
                    Stat healStat = playerItem.GetStat("HealGain");
                    if (healStat == null) return;
                    var mod = new Modifier(ModifierType.Add, 0.5f, MutatorSource);
                    healStat.AddModifier(mod);
                    ctx.PlayerAppliedModifiers.Add((healStat, mod));
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 PlayerAppliedModifiers */ }
            },

            new MutatorDefinition
            {
                Id = "player_lucky_crit",
                NameCn = "幸运星",
                NameEn = "Lucky Star",
                DescCn = "玩家枪械与近战暴击率各 +20%",
                DescEn = "Player gun and melee crit rate +20%",
                Category = MutatorCategory.PlayerBoon,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;

                    Stat gunCritStat = playerItem.GetStat("GunCritRateGain");
                    if (gunCritStat != null)
                    {
                        var mod = new Modifier(ModifierType.Add, 0.2f, MutatorSource);
                        gunCritStat.AddModifier(mod);
                        ctx.PlayerAppliedModifiers.Add((gunCritStat, mod));
                    }

                    Stat meleeCritStat = playerItem.GetStat("MeleeCritRateGain");
                    if (meleeCritStat != null)
                    {
                        var mod = new Modifier(ModifierType.Add, 0.2f, MutatorSource);
                        meleeCritStat.AddModifier(mod);
                        ctx.PlayerAppliedModifiers.Add((meleeCritStat, mod));
                    }
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 PlayerAppliedModifiers */ }
            },

            // ═══════════════════════════════════════════
            // 环境规则类（5条）
            // ═══════════════════════════════════════════

            new MutatorDefinition
            {
                Id = "bleed_accelerate",
                NameCn = "血流如注",
                NameEn = "Hemorrhage",
                DescCn = "流血速度 x1.5",
                DescEn = "Bleed rate x1.5",
                Category = MutatorCategory.EnvironmentRule,
                IsAllowedForMode = modeTag => string.Equals(modeTag, "ModeF", StringComparison.OrdinalIgnoreCase),
                OnApply = ctx => { MutatorManager.BleedRateMultiplier = 1.5f; },
                OnRemove = ctx => { MutatorManager.BleedRateMultiplier = 1.0f; }
            },

            new MutatorDefinition
            {
                Id = "heal_reduced",
                NameCn = "伤口难愈",
                NameEn = "Festering Wounds",
                DescCn = "治疗效果 -40%",
                DescEn = "Healing -40%",
                Category = MutatorCategory.EnvironmentRule,
                OnApply = ctx =>
                {
                    // 注意：用 player.CharacterItem 属性，不是 GetComponent<Item>()
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;
                    Stat healStat = playerItem.GetStat("HealGain");
                    if (healStat == null) return;
                    var mod = new Modifier(ModifierType.Add, -0.4f, MutatorSource);
                    healStat.AddModifier(mod);
                    ctx.PlayerAppliedModifiers.Add((healStat, mod));
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 PlayerAppliedModifiers */ }
            },

            new MutatorDefinition
            {
                Id = "boss_regen",
                NameCn = "不死之躯",
                NameEn = "Undying",
                DescCn = "Boss 每 10 秒回复 5% 血量",
                DescEn = "Boss regenerates 5% HP every 10s",
                Category = MutatorCategory.EnvironmentRule,
                OnApply = ctx => { MutatorManager.BossRegenEnabled = true; },
                OnRemove = ctx => { MutatorManager.BossRegenEnabled = false; }
            },

            new MutatorDefinition
            {
                Id = "glass_cannon",
                NameCn = "玻璃大炮",
                NameEn = "Glass Cannon",
                DescCn = "玩家伤害 +50%，但护甲归零",
                DescEn = "Player damage +50%, but armor = 0",
                Category = MutatorCategory.EnvironmentRule,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;

                    Stat dmgStat = playerItem.GetStat("GunDamageMultiplier");
                    if (dmgStat != null)
                    {
                        var mod = new Modifier(ModifierType.Add, 0.5f, MutatorSource);
                        dmgStat.AddModifier(mod);
                        ctx.PlayerAppliedModifiers.Add((dmgStat, mod));
                    }

                    Stat meleeDmgStat = playerItem.GetStat("MeleeDamageMultiplier");
                    if (meleeDmgStat != null)
                    {
                        var mod = new Modifier(ModifierType.Add, 0.5f, MutatorSource);
                        meleeDmgStat.AddModifier(mod);
                        ctx.PlayerAppliedModifiers.Add((meleeDmgStat, mod));
                    }

                    Stat armorStat = playerItem.GetStat("BodyArmor");
                    if (armorStat != null)
                    {
                        // 将护甲压成 0：用 PercentageMultiply -1f，即使期间有其它 Modifier 加值也会被乘 0
                        // 不能用 Add(-armorStat.Value) 因为那只抵消当时的总值，后续新增的 Modifier 会让护甲变正
                        var mod = new Modifier(ModifierType.PercentageMultiply, -1f, MutatorSource);
                        armorStat.AddModifier(mod);
                        ctx.PlayerAppliedModifiers.Add((armorStat, mod));
                    }
                },
                OnRemove = ctx => { /* 由 MutatorManager.RemoveAll 集中清理 PlayerAppliedModifiers */ }
            },

            new MutatorDefinition
            {
                Id = "glass_speed",
                NameCn = "疾影",
                NameEn = "Blitz",
                DescCn = "玩家移动速度 +40%，但血量 -20%",
                DescEn = "Player move speed +40%, but max HP -20%",
                Category = MutatorCategory.EnvironmentRule,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem == null) return;

                    Stat walk = playerItem.GetStat("WalkSpeed");
                    if (walk != null)
                    {
                        var mod = new Modifier(ModifierType.PercentageAdd, 0.4f, MutatorSource);
                        walk.AddModifier(mod);
                        ctx.PlayerAppliedModifiers.Add((walk, mod));
                    }

                    Stat run = playerItem.GetStat("RunSpeed");
                    if (run != null)
                    {
                        var mod = new Modifier(ModifierType.PercentageAdd, 0.4f, MutatorSource);
                        run.AddModifier(mod);
                        ctx.PlayerAppliedModifiers.Add((run, mod));
                    }

                    Stat maxHealthStat = playerItem.GetStat("MaxHealth");
                    if (maxHealthStat != null)
                    {
                        var mod = new Modifier(ModifierType.PercentageAdd, -0.2f, MutatorSource);
                        maxHealthStat.AddModifier(mod);
                        ctx.PlayerAppliedModifiers.Add((maxHealthStat, mod));
                        ClampCurrentHealthToMax(ctx.Player);
                    }
                },
                OnRemove = ctx => { /* 移除后最大生命只会上升，无需额外裁血 */ }
            },

            // ═══════════════════════════════════════════
            // 死亡触发类（刺激词条，3条）
            // ═══════════════════════════════════════════

            new MutatorDefinition
            {
                Id = "lifesteal_on_kill",
                NameCn = "嗜血",
                NameEn = "Lifesteal",
                DescCn = "击杀敌人回复 8% 最大生命",
                DescEn = "Killing an enemy heals 8% max HP",
                Category = MutatorCategory.EnvironmentRule,
                OnApply = ctx =>
                {
                    // 注册到死亡回调，由 MutatorManager 统一订阅 Health.OnDead（玩家击杀才触发）
                    ctx.EnemyKilledCallbacks.Add((dead, pos) =>
                    {
                        CharacterMainControl player = ctx.Player;
                        if (player == null || player.Health == null) return;
                        if (player.Health.IsDead) return;
                        float heal = player.Health.MaxHealth * 0.08f;
                        if (heal > 0f) player.AddHealth(heal);
                    });
                },
                OnRemove = ctx => { /* Health.OnDead 由 MutatorManager.RemoveAll 统一退订 */ }
            },

            new MutatorDefinition
            {
                Id = "blood_pact",
                NameCn = "血之契约",
                NameEn = "Blood Pact",
                DescCn = "玩家直接击杀回复 16% 最大生命，但治疗效果 -30%",
                DescEn = "Direct player kills heal 16% max HP, but healing -30%",
                Category = MutatorCategory.EnvironmentRule,
                OnApply = ctx =>
                {
                    Item playerItem = ctx.Player.CharacterItem;
                    if (playerItem != null)
                    {
                        Stat healStat = playerItem.GetStat("HealGain");
                        if (healStat != null)
                        {
                            var mod = new Modifier(ModifierType.Add, -0.3f, MutatorSource);
                            healStat.AddModifier(mod);
                            ctx.PlayerAppliedModifiers.Add((healStat, mod));
                        }
                    }

                    // 只走 MutatorManager 已经筛过的"玩家直接归因击杀"回调；
                    // 回血直接调用 Health.AddHealth，故意不再吃一层 HealGain。
                    ctx.EnemyKilledCallbacks.Add((dead, pos) =>
                    {
                        CharacterMainControl player = ctx.Player;
                        if (player == null || player.Health == null) return;
                        if (player.Health.IsDead) return;
                        float heal = player.Health.MaxHealth * 0.16f;
                        if (heal > 0f) player.Health.AddHealth(heal);
                    });
                },
                OnRemove = ctx => { /* Health.OnDead 由 MutatorManager.RemoveAll 统一退订 */ }
            },

            new MutatorDefinition
            {
                Id = "explode_on_death",
                NameCn = "天降殉爆",
                NameEn = "Volatile Remains",
                DescCn = "敌人死亡时爆炸（小心被波及！）",
                DescEn = "Enemies explode on death (mind the blast!)",
                Category = MutatorCategory.EnvironmentRule,
                OnApply = ctx =>
                {
                    ctx.EnemyKilledCallbacks.Add((dead, pos) =>
                    {
                        CharacterMainControl player = ctx.Player;
                        if (player == null) return;
                        if (LevelManager.Instance == null || LevelManager.Instance.ExplosionManager == null) return;

                        // 爆炸来源记为玩家，但不豁免任何人——会波及玩家自身（风险与收益并存）
                        DamageInfo dmg = new DamageInfo(player);
                        dmg.damageValue = 40f;
                        dmg.damagePoint = pos;
                        dmg.isExplosion = true;
                        dmg.AddElementFactor(ElementTypes.fire, 1.0f);

                        LevelManager.Instance.ExplosionManager.CreateExplosion(
                            pos,
                            3.0f,
                            dmg,
                            // 原版 ExplosionManager 只为 normal / flash 生成爆炸特效；
                            // fire 没有对应特效预制体会导致“看不见的爆炸”，故用 normal 保证可见反馈。
                            ExplosionFxTypes.normal,
                            0.35f,
                            true); // canHurtSelf=true：连玩家一起炸
                    });
                },
                OnRemove = ctx => { /* Health.OnDead 由 MutatorManager.RemoveAll 统一退订 */ }
            }
        };

        private static void SyncCurrentHealthWithRaisedMax(CharacterMainControl character, float oldMaxHealth)
        {
            if (character == null || character.Health == null) return;
            if (oldMaxHealth <= 0f) return;

            float newMaxHealth = character.Health.MaxHealth;
            if (newMaxHealth <= oldMaxHealth) return;

            float targetHealth = character.Health.CurrentHealth + (newMaxHealth - oldMaxHealth);
            character.Health.SetHealth(targetHealth);
        }

        private static void ClampCurrentHealthToMax(CharacterMainControl character)
        {
            if (character == null || character.Health == null) return;
            character.Health.SetHealth(character.Health.CurrentHealth);
        }
    }
}
