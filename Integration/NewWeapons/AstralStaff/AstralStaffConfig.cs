// ============================================================================
// AstralStaffConfig.cs - 星阙（500104）数值与文案单点
// ============================================================================
// 星阙是冠军之影（征程第六章终章 Boss）的专属掉落：一根没有模型的光棍，
// 物品、手持光棍、挥击与重击表现全部由代码生成；图标走生图管线（production_icons），缺包时回落程序化图。
//
// 玩法（参考《黑神话：悟空》的棍势豆）：
//   - 左键：官方近战轻击。每次挥击命中至少一个敌人记一段，两段攒一豆（最多三豆）；
//     三段连招的收尾段命中多记一段。
//   - 按住右键：蓄势，每 ChargeSecondsPerBean 秒再长一豆；松开按当前豆数出重击，豆数清零。
//   - 一豆「流星横扫」、二豆「双龙回旋」、三豆「星陨天崩」，三档手感各不相同。
// 归位依据 AGENTS.md §4.8 第 2 层：玩法强耦合常量放模块自己的 Config。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    internal static class AstralStaffConfig
    {
        internal const int TypeId = BossRushItemIds.AstralStaff;
        internal const string BaseName = "AstralStaff";
        internal const string PrefabName = "AstralStaff_Runtime";
        internal const string DisplayNameKey = "BossRush_AstralStaff";
        internal const string LogPrefix = "[AstralStaff]";

        // ========== 文案 ==========
        internal const string DisplayNameCN = "星阙";
        internal const string DisplayNameEN = "Astral Staff";
        internal const string DescriptionCN =
            "冠军之影散成一地沙之后，沙里立着的就是它。握上去没有分量，只有一道光。\n"
            + "<color=#FFD36B>【棍势】</color>左键轻击，打中两下攒一豆（连招第三下多算一下），最多三豆；按住右键蓄势也会长豆。松开右键放重击。\n"
            + "<color=#FFE9A8>【一豆 · 流星横扫】</color>前方大半圈横扫，击退。\n"
            + "<color=#9FF3FF>【二豆 · 双龙回旋】</color>原地两圈回旋，两段伤害，把周围的敌人卷近。\n"
            + "<color=#FFFFFF>【三豆 · 星陨天崩】</color>向前一跃，砸下一根巨大的光棍，前方一条全中，落点震开一圈。\n"
            + "<color=#BBBBBB>来源：鸭王征程第六章 冠军之影 必掉</color>";
        internal const string DescriptionEN =
            "When the Shadow of the Champion fell apart into sand, this was left standing in it. It weighs nothing. It is just light.\n"
            + "<color=#FFD36B>[Focus]</color> Left-click light attacks; every 2 hits that land build one Focus point (the third combo hit counts double), up to 3. Hold right-click to charge more; release to unleash.\n"
            + "<color=#FFE9A8>[1 Focus · Meteor Sweep]</color> A wide sweep in front that knocks enemies back.\n"
            + "<color=#9FF3FF>[2 Focus · Twin Dragon Spin]</color> Two full spins, two hits, drags nearby enemies in.\n"
            + "<color=#FFFFFF>[3 Focus · Starfall]</color> Leap forward and slam down a giant staff of light: hits everything in a line, then a shockwave at the landing point.\n"
            + "<color=#BBBBBB>Source: guaranteed drop from the Shadow of the Champion (Duck King's Campaign, Chapter 6)</color>";

        // ========== 物品属性 ==========
        internal const int Quality = 6;
        internal const int Value = 78000;
        internal const float MaxDurability = 100f;

        // ========== 轻击面板（官方近战 Stat） ==========
        internal const float Damage = 42f;
        internal const float AttackSpeed = 1.45f;
        internal const float AttackRange = 2.3f;
        internal const float CritRate = 0.08f;
        internal const float CritDamageFactor = 1.6f;
        internal const float ArmorPiercing = 6f;
        // 2026-10-08 实测反馈「耐力一下子就没了都攒不到豆」：轻击 7 → 5，重击同步下调。
        internal const float StaminaCost = 5f;
        internal const float DealDamageTime = 0.1f;
        internal const float BleedChance = 0f;
        internal const float MoveSpeedMultiplier = 1.05f;
        internal const float BlockBullet = 0f;

        // ========== 棍势豆 ==========
        internal const int MaxBeans = 3;
        /// <summary>轻击命中几次攒一豆（三段连招的收尾段命中记两段）。</summary>
        internal const int LightHitsPerBean = 2;
        /// <summary>按住右键多久开始长豆；已有豆时点按右键可直接重击。</summary>
        internal const float ChargeStartDelay = 0.18f;
        /// <summary>蓄势中每长一豆需要的秒数。</summary>
        internal const float ChargeSecondsPerBean = 0.45f;
        // ========== 一豆 · 流星横扫 ==========
        internal const float SweepWindup = 0.1f;
        internal const float SweepRadius = 4.2f;
        internal const float SweepHalfAngle = 115f;
        internal const float SweepDamageMultiplier = 2.2f;
        internal const float SweepKnockback = 9f;
        internal const float SweepStamina = 4f;
        internal const float SweepRecovery = 0.25f;

        // ========== 二豆 · 双龙回旋 ==========
        internal const float SpinFirstHit = 0.16f;
        internal const float SpinSecondHit = 0.4f;
        internal const float SpinRadius = 4.6f;
        internal const float SpinDamageMultiplier = 1.7f;
        internal const float SpinPullSpeed = 7f;
        internal const float SpinStamina = 7f;
        internal const float SpinRecovery = 0.3f;

        // ========== 三豆 · 星陨天崩 ==========
        internal const float StarfallLeapTime = 0.34f;
        internal const float StarfallLeapDistance = 3.2f;
        internal const float StarfallImpactTime = 0.56f;
        internal const float StarfallLineLength = 7f;
        internal const float StarfallLineHalfWidth = 1.25f;
        internal const float StarfallBlastRadius = 3.2f;
        internal const float StarfallBlastOffset = 5.2f;
        internal const float StarfallDamageMultiplier = 5.2f;
        internal const float StarfallKnockback = 13f;
        internal const float StarfallStamina = 11f;
        internal const float StarfallRecovery = 0.45f;

        // ========== 打击感 ==========
        // 顿帧是官方子弹时间（0.05 倍速）的未缩放秒数；层级：轻击 &lt; 连招收尾 ≈ 横扫 &lt; 击杀 &lt; 星陨。
        internal const float HitStopLight = 0.055f;
        internal const float HitStopLightFinisher = 0.09f;
        internal const float HitStopSweep = 0.09f;
        internal const float HitStopSpin = 0.07f;
        internal const float HitStopStarfall = 0.15f;
        internal const float HitStopKill = 0.1f;
        internal const float KnockbackSeconds = 0.14f;
        /// <summary>三段轻击的收尾段把目标往前顶一下（米/秒，Boss 照例只吃三成）。</summary>
        internal const float LightFinisherKnockback = 6f;
        /// <summary>普通轻击的受击顿退（米/秒）：很小，只让敌人「吃到」这一下，不把连招推出射程。</summary>
        internal const float LightKnockback = 1.8f;
        // 相机冲量（官方近战命中自带 0.05）：收尾段补一记，重击按档递增，星陨走爆炸通道。
        internal const float ShakeLight = 0.035f;
        internal const float ShakeLightFinisher = 0.08f;
        internal const float ShakeSweep = 0.12f;
        internal const float ShakeSpin = 0.1f;
        internal const float ShakeStarfall = 0.32f;
        // 群怪时伤害全结算，命中表现只取前六个接触点，避免特效数量随目标数爆炸。
        internal const int MaxHitFxPerAttack = 6;

        // ========== 配色 ==========
        internal static readonly Color CoreWhite = new Color(1f, 0.94f, 0.78f, 1f);
        internal static readonly Color Gold = new Color(0.95f, 0.73f, 0.36f, 1f);
        internal static readonly Color GoldDeep = new Color(0.67f, 0.4f, 0.16f, 1f);
        internal static readonly Color Cyan = new Color(0.42f, 0.7f, 0.77f, 1f);
        internal static readonly Color GoldFade = new Color(0.72f, 0.44f, 0.16f, 0f);
        internal static readonly Color CyanFade = new Color(0.24f, 0.45f, 0.54f, 0f);
    }
}
