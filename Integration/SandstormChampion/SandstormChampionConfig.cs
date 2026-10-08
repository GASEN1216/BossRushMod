// ============================================================================
// SandstormChampionConfig.cs - 冠军之影（沙暴形态）数值单点
// ============================================================================
// 征程第六章终章 Boss。原版专家 / 大师 / 传奇猪鲨机制的 3D 地面适配：
//   一阶段：五连冲 → 21 颗可打破追踪泡 → 五连冲 → 双追踪沙圈 / 小沙暴；
//   二阶段（<50% 血）：三连冲 → 绕圈留 31 泡 → 三连冲 → 追踪种子 / 大沙暴；
//   三阶段（<15% 血）：隐去沙身只留眼，换侧后一、二、三连冲循环，不再新吐泡 / 放柱。
//   海面边界适配为报名点竞技半径；越界激怒，回场恢复。持棍小弟是用户追加扩展。
// 外形照沙尘精：没有身子，是一股下窄上宽的沙卷风，顶上一团沙云和两点琥珀色的眼。
// 归位依据 AGENTS.md §4.8 第 2 层。所有伤害常量会乘「近战伤害 Stat 当前值 / 刷出时基线」，
// 征程倍率与全局倍率都跟着放大（BossSkillDamageRules）。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    internal static class SandstormChampionConfig
    {
        internal const string LogPrefix = "[SandstormChampion] ";
        internal const string PresetInstanceName = "SandstormChampion_Preset";
        internal const string GameObjectName = "BossRush_SandstormChampion";

        /// <summary>
        /// 底模预设（只借它的血条、碰撞、移动与受伤；渲染全部藏起来，AI 全程暂停由本模块驾驶）。
        /// 按顺序找，第一个找到的用。
        /// </summary>
        internal static readonly string[] BasePresetNames = { "Cname_Boss_Red", "Cname_Ghost", "Cname_Boss_Shot" };

        // ========== 数值 ==========
        internal const float BaseHealth = 5000f;
        internal const float Phase2HealthRatio = 0.5f;
        internal const float Phase3HealthRatio = 0.15f;

        // ========== 移动 ==========
        internal const float HoverSpeed = 13f;
        internal const float HoverDistance = 12f;
        internal const float HoverSeconds = 0.32f;
        internal const float ContactRadius = 1.7f;
        internal const float RepathSeconds = 0.6f;
        internal const float DamageGraceSeconds = 0.22f;
        internal const float MinionSummonWindup = 0.75f;
        internal const float ArenaRadius = 42f;
        internal const float ArenaReturnRadius = 38f;
        internal const float EnrageDelay = 1.2f;

        // ========== 连续冲锋 ==========
        internal const float DashTelegraphP1 = 0.46f;
        internal const float DashTelegraphP2 = 0.32f;
        internal const float DashTelegraphP3 = 0.22f;
        internal const float DashSpeedP1 = 38f;
        internal const float DashSpeedP2 = 48f;
        internal const float DashSpeedP3 = 58f;
        internal const float DashOvershoot = 9f;
        internal const float DashMaxLength = 32f;
        internal const float DashRecover = 0.1f;
        internal const float DashDamage = 24f;
        internal const float DashDamageP2 = 32f;
        internal const float DashDamageP3 = 27f;

        // ========== 沙珠 ==========
        internal const float OrbSpeed = 11f;
        internal const float OrbTurnDegrees = 80f;
        internal const float OrbLifetime = 6f;
        internal const float OrbHomingSeconds = OrbLifetime;
        internal const float OrbHitRadius = 0.6f;
        internal const float OrbDamage = 10f;
        // 31 泡 + 最多四柱各 12 鲨仍有余量；不能把固定 21 / 31 发吞进旧 28 发上限。
        internal const int MaxLiveOrbs = 88;
        internal const float BubbleBelchSeconds = 1.9f;
        internal const float SpiralBubbleSeconds = 2.1f;
        internal const float SpiralOrbitRadius = 8.5f;
        internal const float SpiralOrbitSpeed = 32f;
        internal const float SharkSpeed = 23f;
        internal const float SharkDamage = 14f;
        internal const float SharkHealth = 24f;

        // ========== 沙卷柱 ==========
        internal const float TornadoRadius = 3.4f;
        internal const float TornadoHeight = 12f;
        internal const float TornadoLifetime = 9f;
        internal const float TornadoTickSeconds = 0.5f;
        internal const float TornadoTickDamage = 6f;
        internal const float CycloneRadius = 5.6f;
        internal const float CycloneHeight = 24f;
        internal const float CycloneLifetime = 14f;
        internal const int MaxLiveTornadoes = 4;
        internal const float TornadoTelegraphSeconds = 0.9f;
        internal const float TornadoVolleySeconds = 1.1f;
        internal const float CycloneVolleySeconds = 0.85f;
        internal const float CycloneSeedSpeed = 17f;
        internal const float CycloneSeedLifetime = 3f;
        internal const int MaxLiveSeeds = 4;
        internal const float TwinSeedSpeed = 7.5f;
        internal const float SeedContactRadius = 1.35f;
        internal const float MinionWalkSpeed = 6.2f;
        internal const float MinionRepathSeconds = 0.16f;
        internal const float MinionFirstAttackDelay = 0.2f;
        internal const float MinionAttackCooldown = 0.68f;

        // ========== 阶段转换 ==========
        internal const float TransitionSeconds = 1f;
        internal const float NovaRadius = 6.5f;

        // ========== 配色 ==========
        internal static readonly Color Sand = new Color(0.86f, 0.68f, 0.42f, 0.55f);
        internal static readonly Color SandDark = new Color(0.55f, 0.4f, 0.24f, 0.6f);
        internal static readonly Color SandLight = new Color(1f, 0.88f, 0.62f, 0.5f);
        internal static readonly Color Ember = new Color(1f, 0.62f, 0.2f, 1f);
        internal static readonly Color EyeColor = new Color(1f, 0.82f, 0.35f, 1f);
        internal static readonly Color RageTint = new Color(0.95f, 0.38f, 0.22f, 0.6f);
        internal static readonly Color WarningColor = new Color(1f, 0.55f, 0.18f, 0.55f);
    }
}
