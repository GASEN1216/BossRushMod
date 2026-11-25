// ============================================================================
// ArenaTrialsTuning.cs - 出征令战规机制化（天色 / 天象 / 天灾 / 凶相 / 厄运 / 失明 / 断补 / 血债 / 焚城）的数值与文案
// ============================================================================
// 2026-11-25 owner 拍板（问答结论）：战规照旧固定，每档在出征令上列清单；随机性放在局内。
//   - 轻松 / 标准：只加「天色」（雨、雪、黄昏、夜色轮换），只换画面不改数值，不列为战规；
//   - 困难 6 条：磨刀、急行军、天象无常、天灾、凶相、厄运；
//   - 噩梦 10 条：杀心、急行军、天翻地覆、天灾连环、焚城缩圈、凶煞、失明、断补、血债、厄运（重）；
//   - 撤掉此前借用的变异词条战规（神枪敌军、伤口难愈、Boss 回春、嗜血、血之契约、天降殉爆），盲战并入失明。
// 实现取舍（读码核实，记在这里免得以后有人「顺手」改回去）：
//   - 天色 / 天象全部自绘（跟随玩家的降水粒子 + 屏幕色调层），不调官方 WeatherManager.SetForceWeather：
//     官方「雨」会把火系数 −0.15、电系数 +0.2（Health 元素系数），各天气对应的 Volume 还驱动寒冷 / 炎热与夜视系数，
//     与「天色只换画面」冲突；自绘也就不必跟鸭生无常的血月抢强制天气的 owner；
//   - 永夜 / 暴雨 / 失明 / 近视的「看不远」挂在玩家的 ViewDistance / ViewAngle / SenseRange 上，官方战争迷雾读这三项；
//     几条同时生效时按乘积合成一条修饰，并设下限（不至于整屏全黑）；
//   - 天灾伤害按生命上限的百分比算：玩家吃满额，首领只吃一部分（把首领引进去有用，但不至于靠天灾磨死 Boss）。
// 精确数值只写在出征令悬赏令与 Wiki 上；局内只有天象名 / 诅咒名的角标，不出说明字幕。
// ============================================================================

using UnityEngine;

namespace BossRush
{
    /// <summary>轻松 / 标准的天色（只换画面）。</summary>
    internal enum ArenaTrialSky
    {
        Clear = 0,
        Rain = 1,
        Snow = 2,
        Dusk = 3,
        Night = 4,
    }

    /// <summary>困难 / 噩梦的天象。</summary>
    internal enum ArenaTrialOmen
    {
        None = 0,
        Blizzard = 1,
        Thunder = 2,
        BloodMoon = 3,
        EternalNight = 4,
        Downpour = 5,
    }

    /// <summary>厄运池里的诅咒。</summary>
    internal enum ArenaTrialCurse
    {
        None = 0,
        Butterfingers = 1,
        Leaden = 2,
        Jitters = 3,
        Flinch = 4,
        Nearsighted = 5,
    }

    /// <summary>天灾与临时地形。</summary>
    internal enum ArenaTrialHazard
    {
        Meteor = 0,
        Lightning = 1,
        Miasma = 2,
        Rift = 3,
        InkTide = 4,
        Gale = 5,
        Vortex = 6,
        Frost = 7,
        Fogmire = 8,
        Count = 9,
    }

    /// <summary>首领词缀的四类。</summary>
    internal enum ArenaAffixKind
    {
        Clone = 0,
        Guard = 1,
        Motion = 2,
        Death = 3,
        Count = 4,
    }

    /// <summary>首领词缀：每类两个，按 Kind × 2 + 序号排。</summary>
    internal enum ArenaAffix
    {
        Split = 0,
        Twin = 1,
        Escort = 2,
        Summoner = 3,
        Blink = 4,
        Frenzy = 5,
        Wraith = 6,
        Volatile = 7,
        Count = 8,
    }

    /// <summary>一档战帖在局内要开哪些机制。</summary>
    internal sealed class ArenaTrialProfile
    {
        /// <summary>轻松 / 标准：天色轮换（纯画面）。</summary>
        internal bool Skies;
        /// <summary>变天间隔（波，闭区间）；0 = 没有天象。</summary>
        internal int OmenWavesMin, OmenWavesMax;
        /// <summary>天灾间隔（秒）；0 = 没有天灾。</summary>
        internal float HazardSecondsMin, HazardSecondsMax;
        /// <summary>一阵天灾里叠第二种的几率。</summary>
        internal float HazardDoubleChance;
        /// <summary>每只首领的词缀条数（压轴另算）；0 = 不挂词缀。</summary>
        internal int Affixes, FinaleAffixes;
        /// <summary>几条词缀必须取自不同类别。</summary>
        internal bool AffixDistinctKinds;
        /// <summary>厄运：0 无、1 轻、2 重。</summary>
        internal int CurseLevel;
        internal bool Blind, CutSupply, BloodDebt, FireRing;

        internal bool HasOmens { get { return OmenWavesMax > 0; } }
        internal bool HasHazards { get { return HazardSecondsMax > 0f; } }
        internal bool HasAffixes { get { return Affixes > 0; } }
    }

    internal static class ArenaTrialsTuning
    {
        // ------------------------------------------------------------------
        // 各档开关
        // ------------------------------------------------------------------

        private static readonly ArenaTrialProfile SkiesOnly = new ArenaTrialProfile { Skies = true };

        private static readonly ArenaTrialProfile Hard = new ArenaTrialProfile
        {
            OmenWavesMin = 4, OmenWavesMax = 5,
            HazardSecondsMin = 30f, HazardSecondsMax = 45f, HazardDoubleChance = 0f,
            Affixes = 1, FinaleAffixes = 2, AffixDistinctKinds = true,
            CurseLevel = 1,
        };

        private static readonly ArenaTrialProfile Nightmare = new ArenaTrialProfile
        {
            OmenWavesMin = 2, OmenWavesMax = 3,
            HazardSecondsMin = 15f, HazardSecondsMax = 25f, HazardDoubleChance = 0.25f,
            Affixes = 3, FinaleAffixes = 4, AffixDistinctKinds = true,
            CurseLevel = 2,
            Blind = true, CutSupply = true, BloodDebt = true, FireRing = true,
        };

        internal static ArenaTrialProfile ProfileFor(ArenaExpeditionTier tier)
        {
            switch (tier)
            {
                case ArenaExpeditionTier.Hard: return Hard;
                case ArenaExpeditionTier.Nightmare: return Nightmare;
                default: return SkiesOnly;
            }
        }

        // ------------------------------------------------------------------
        // 天色（轻松 / 标准，只换画面）
        // ------------------------------------------------------------------

        /// <summary>天色每几波换一次（闭区间）。</summary>
        internal const int SkyWavesMin = 3;
        internal const int SkyWavesMax = 4;
        /// <summary>天色 / 天象换场时色调层的淡入淡出（秒）。</summary>
        internal const float SkyBlendSeconds = 2.4f;

        // ------------------------------------------------------------------
        // 天象
        // ------------------------------------------------------------------

        /// <summary>暴雪：双方移速。</summary>
        internal const float BlizzardMoveBonus = -0.15f;
        /// <summary>暴雪：冰封地形出现权重的倍数。</summary>
        internal const float BlizzardFrostWeight = 2f;
        /// <summary>雷暴：落雷出现权重的倍数。</summary>
        internal const float ThunderLightningWeight = 2f;
        /// <summary>血月：首领移速 / 伤害。</summary>
        internal const float BloodMoonBossMoveBonus = 0.25f;
        internal const float BloodMoonBossDamageBonus = 0.20f;
        /// <summary>永夜：视距 / 视角 / 近身感知倍数。</summary>
        internal const float EternalNightViewDistance = 0.45f;
        internal const float EternalNightViewAngle = 0.55f;
        internal const float EternalNightSenseRange = 0.6f;
        /// <summary>暴雨：视距倍数；熔缝、毒雾持续时间倍数。</summary>
        internal const float DownpourViewDistance = 0.75f;
        internal const float DownpourHazardDuration = 0.5f;

        // ------------------------------------------------------------------
        // 厄运（轻 / 重）与噩梦三条常驻诅咒
        // ------------------------------------------------------------------

        /// <summary>手滑：换弹速度（ReloadSpeedGain 是零基数增益，走 Add）。</summary>
        internal static float ButterfingersReload(int level) { return level >= 2 ? -0.45f : -0.25f; }
        /// <summary>沉重：体力恢复。</summary>
        internal static float LeadenStamina(int level) { return level >= 2 ? -0.65f : -0.40f; }
        /// <summary>手抖：散射。</summary>
        internal static float JittersScatter(int level) { return level >= 2 ? 1.0f : 0.5f; }
        /// <summary>怕痛：受击后减速幅度与时长。</summary>
        internal static float FlinchSlow(int level) { return level >= 2 ? -0.40f : -0.25f; }
        internal static float FlinchSeconds(int level) { return level >= 2 ? 1.8f : 1.2f; }
        /// <summary>近视：视距倍数。</summary>
        internal static float NearsightedView(int level) { return level >= 2 ? 0.6f : 0.75f; }

        /// <summary>失明：视距 / 视角倍数；首领血条只剩几格。</summary>
        internal const float BlindViewDistance = 0.8f;
        internal const float BlindViewAngle = 0.75f;
        internal const int BlindHealthBarSteps = 4;

        /// <summary>几条「看不远」叠在一起时的下限（相对原值）。</summary>
        internal const float VisionFloor = 0.3f;

        /// <summary>断补：用药读条倍数、禁药时长与间隔、弹药掉落倍数。</summary>
        internal const float CutSupplyUseTimeMultiplier = 2f;
        internal const float CutSupplyBanSeconds = 8f;
        internal const float CutSupplyBanIntervalMin = 40f;
        internal const float CutSupplyBanIntervalMax = 70f;
        internal const float CutSupplyAmmoDropFactor = 0.5f;

        /// <summary>血债：击杀回血（生命上限的比例）；被首领打中一次压低的上限与封顶。</summary>
        internal const float BloodDebtBossKillHeal = 0.30f;
        internal const float BloodDebtKillHeal = 0.08f;
        internal const float BloodDebtMaxHealthPerHit = 0.03f;
        internal const float BloodDebtMaxHealthCap = 0.30f;

        // ------------------------------------------------------------------
        // 天灾与临时地形
        // ------------------------------------------------------------------

        /// <summary>每一波开打后多久来第一阵（秒）。</summary>
        internal const float HazardFirstDelay = 14f;
        /// <summary>噩梦叠第二种时，第二种晚多久到（一次只说一件事：先看清第一种）。</summary>
        internal const float HazardDoubleDelay = 2.5f;
        /// <summary>首领吃天灾伤害的比例（相对玩家按生命上限算的那份）。</summary>
        internal const float HazardFoeDamageShare = 0.3f;
        /// <summary>落点离玩家 / 首领的随机半径。</summary>
        internal const float HazardScatterRadius = 7f;
        /// <summary>以首领为基准落点的几率（其余以玩家为基准）。</summary>
        internal const float HazardBossAnchorChance = 0.3f;
        /// <summary>范围类天灾的结算间隔（秒）。</summary>
        internal const float HazardTickSeconds = 0.5f;

        internal const int MeteorCountMin = 6, MeteorCountMax = 10;
        internal const float MeteorWarnSeconds = 1.5f, MeteorRadius = 2.4f, MeteorDamage = 0.16f, MeteorSpread = 9f;
        /// <summary>落雷：三道依次锁定玩家（每道提前 1 秒预警），相邻两道相隔 LightningInterval；预判玩家走位 LightningLead 秒。</summary>
        internal const int LightningStrikes = 3;
        internal const float LightningWarnSeconds = 1f, LightningRadius = 1.8f, LightningDamage = 0.2f, LightningInterval = 0.75f,
            LightningLead = 0.3f;
        internal const int MiasmaCloudsMin = 2, MiasmaCloudsMax = 3;
        internal const float MiasmaSeconds = 20f, MiasmaRadius = 3.4f, MiasmaDamagePerSecond = 0.035f;
        internal const float RiftWarnSeconds = 1.6f, RiftSeconds = 15f, RiftLength = 16f, RiftWidth = 1.6f, RiftDamagePerSecond = 0.09f;
        /// <summary>墨潮：从玩家一侧 InkTideTravel 米外推到另一侧；缺口中心离玩家横向不超过 InkTideGapRange。</summary>
        internal const float InkTideWarnSeconds = 2.2f, InkTideSpeed = 11f, InkTideWidth = 34f, InkTideDepth = 2.4f,
            InkTideGap = 4f, InkTideDamage = 0.14f, InkTideKnockback = 9f, InkTideKnockSeconds = 0.35f,
            InkTideTravel = 16f, InkTideGapRange = 7f;
        /// <summary>狂风：先起风 GaleLead 秒（只见风不推），之后每 GaleGustInterval 秒一阵、每阵 GaleGustSeconds，阵间留一点微风。</summary>
        internal const float GaleSeconds = 10f, GaleSpeed = 3.2f, GaleLead = 1.2f, GaleGustSeconds = 0.6f, GaleGustInterval = 1.1f,
            GaleBreeze = 0.18f;
        /// <summary>旋涡：越近拉得越狠（外缘 45%、中心满额），带一点切向的旋。</summary>
        internal const float VortexSeconds = 9f, VortexRadius = 7f, VortexPull = 2.6f, VortexCoreRadius = 1.4f,
            VortexCoreDamagePerSecond = 0.08f, VortexSwirl = 0.35f;
        internal const float FrostSeconds = 14f, FrostRadius = 4.2f, FrostSlow = -0.40f;
        /// <summary>雾泽：雾里的敌人只看得见 FogmireSightDistance 米内；玩家在雾里时视距再乘 FogmirePlayerView。</summary>
        internal const float FogmireSeconds = 16f, FogmireRadius = 5.5f, FogmireSightDistance = 3f, FogmirePlayerView = 0.3f;

        /// <summary>天灾的出现权重（按 ArenaTrialHazard 顺序）。</summary>
        private static readonly float[] HazardWeights = { 1f, 1f, 1f, 1f, 0.8f, 0.8f, 0.8f, 1f, 0.8f };

        internal static float HazardWeight(ArenaTrialHazard hazard, ArenaTrialOmen omen)
        {
            int index = (int)hazard;
            if (index < 0 || index >= HazardWeights.Length) return 0f;
            float weight = HazardWeights[index];
            if (hazard == ArenaTrialHazard.Frost && omen == ArenaTrialOmen.Blizzard) weight *= BlizzardFrostWeight;
            if (hazard == ArenaTrialHazard.Lightning && omen == ArenaTrialOmen.Thunder) weight *= ThunderLightningWeight;
            return weight;
        }

        // ------------------------------------------------------------------
        // 焚城
        // ------------------------------------------------------------------

        internal const float FireRingStartDelay = 30f;
        internal const float FireRingShrinkSeconds = 14f;
        internal const float FireRingRadiusFactor = 0.6f;
        internal const float FireRingFinaleRadiusFactor = 0.4f;
        internal const float FireRingMargin = 10f;
        internal const float FireRingDamagePerSecond = 0.04f;
        internal const float FireRingRecedeSeconds = 4f;
        /// <summary>收紧前多少秒先在地上与 M 图画出落点圈。</summary>
        internal const float FireRingTelegraphSeconds = 6f;
        /// <summary>开波火墙沿圈燃起的时长（燃起期间不烧人）。</summary>
        internal const float FireRingIgniteSeconds = 3.2f;
        /// <summary>圈外灼烧一跳的间隔（每跳掉 FireRingDamagePerSecond × 间隔）。</summary>
        internal const float FireRingTickSeconds = 0.5f;
        /// <summary>开波火圈半径下限：刷新点太少、太挤时也留够腾挪的地方（压轴收紧后仍有约 10 米）。</summary>
        internal const float FireRingMinRadius = 24f;
        /// <summary>圈心贴 NavMesh 的取样半径；重心落不到可走的地面时改用离重心最近的刷新点。</summary>
        internal const float FireRingCenterSnap = 8f;
        /// <summary>收波火墙向外退到当前半径的几倍再熄灭。</summary>
        internal const float FireRingRecedeGrowth = 1.2f;

        /// <summary>焚城的名字（收紧时的墨书横扫与 M 图标签；按当前语言取）。</summary>
        internal static string FireRingName()
        {
            return L10n.T("焚城", "City Ablaze");
        }

        // ------------------------------------------------------------------
        // 首领词缀
        // ------------------------------------------------------------------

        /// <summary>分裂：分身的体型、生命比例。</summary>
        internal const float SplitScale = 0.7f, SplitHealth = 0.35f;
        /// <summary>双生：先倒下的那只被拉起的时限与回血比例；一对最多拉起几次（防止两只轮流倒下永远打不完）。</summary>
        internal const float TwinReviveSeconds = 10f, TwinReviveHealth = 0.5f, TwinHealth = 0.6f;
        internal const int TwinMaxRevives = 2;
        /// <summary>护驾：小兵数量、首领在小兵没清完时的减伤。</summary>
        internal const int EscortCount = 3;
        internal const float EscortDamageReduction = 0.8f, MinionHealthShare = 0.12f, MinionScale = 0.6f;
        /// <summary>护卫 / 召唤物的伤害倍数（它们是首领的影子，带着首领的兵器）。</summary>
        internal const float MinionDamage = 0.5f;
        /// <summary>召唤：间隔、每批几只、同时最多几只。</summary>
        internal const float SummonInterval = 20f;
        internal const int SummonBatch = 2, SummonCap = 6;
        /// <summary>闪现：冒烟前摇、间隔、落在玩家身后多远。</summary>
        internal const float BlinkWindup = 0.5f, BlinkIntervalMin = 7f, BlinkIntervalMax = 11f, BlinkBehind = 2.6f;
        /// <summary>狂暴：触发血线、体型、移速加成。</summary>
        internal const float FrenzyThreshold = 0.5f, FrenzyScale = 1.3f, FrenzyBonus = 0.40f;
        /// <summary>亡魂：追击时长、速度、贴身伤害。</summary>
        internal const float WraithSeconds = 15f, WraithSpeed = 4.2f, WraithTouchRadius = 1.1f, WraithDamagePerSecond = 0.12f;
        /// <summary>殉爆：倒下后几秒炸、半径、伤害。</summary>
        internal const float VolatileDelay = 3f, VolatileRadius = 6f, VolatileDamage = 0.35f;
        /// <summary>分身 / 双生压着收波的上限（秒）：超时把剩下的身体散掉、照常收波，防止身体卡进墙里永远打不到。</summary>
        internal const float AffixHoldMaxSeconds = 150f;

        /// <summary>多掉几件：1–2 条词缀多 1 件，3–4 条多 2 件。</summary>
        internal static int AffixBonusLoot(int affixCount)
        {
            if (affixCount <= 0) return 0;
            return affixCount >= 3 ? 2 : 1;
        }

        internal static ArenaAffixKind KindOf(ArenaAffix affix) { return (ArenaAffixKind)((int)affix / 2); }

        // ------------------------------------------------------------------
        // 文案（取用时解析语言；只用 GBK 字符）
        // ------------------------------------------------------------------

        internal static string OmenName(ArenaTrialOmen omen)
        {
            switch (omen)
            {
                case ArenaTrialOmen.Blizzard: return L10n.T("暴雪", "Blizzard");
                case ArenaTrialOmen.Thunder: return L10n.T("雷暴", "Thunderstorm");
                case ArenaTrialOmen.BloodMoon: return L10n.T("血月", "Blood Moon");
                case ArenaTrialOmen.EternalNight: return L10n.T("永夜", "Endless Night");
                case ArenaTrialOmen.Downpour: return L10n.T("暴雨", "Downpour");
                default: return string.Empty;
            }
        }

        internal static string CurseName(ArenaTrialCurse curse)
        {
            switch (curse)
            {
                case ArenaTrialCurse.Butterfingers: return L10n.T("手滑", "Butterfingers");
                case ArenaTrialCurse.Leaden: return L10n.T("沉重", "Leaden");
                case ArenaTrialCurse.Jitters: return L10n.T("手抖", "Jitters");
                case ArenaTrialCurse.Flinch: return L10n.T("怕痛", "Flinch");
                case ArenaTrialCurse.Nearsighted: return L10n.T("近视", "Nearsighted");
                default: return string.Empty;
            }
        }

        internal static string AffixName(ArenaAffix affix)
        {
            switch (affix)
            {
                case ArenaAffix.Split: return L10n.T("分裂", "Splitting");
                case ArenaAffix.Twin: return L10n.T("双生", "Twinned");
                case ArenaAffix.Escort: return L10n.T("护驾", "Escorted");
                case ArenaAffix.Summoner: return L10n.T("召唤", "Summoner");
                case ArenaAffix.Blink: return L10n.T("闪现", "Blinking");
                case ArenaAffix.Frenzy: return L10n.T("狂暴", "Frenzied");
                case ArenaAffix.Wraith: return L10n.T("亡魂", "Haunting");
                case ArenaAffix.Volatile: return L10n.T("殉爆", "Volatile");
                default: return string.Empty;
            }
        }

        /// <summary>词缀身上光效与血条名缀的主色（每类一色，压低饱和守在原版暖色调里：冷银、古金、墨紫、朱砂）。</summary>
        internal static Color AffixColor(ArenaAffix affix)
        {
            switch (KindOf(affix))
            {
                case ArenaAffixKind.Clone: return new Color(0.66f, 0.76f, 0.88f, 1f);
                case ArenaAffixKind.Guard: return new Color(0.88f, 0.71f, 0.40f, 1f);
                case ArenaAffixKind.Motion: return new Color(0.64f, 0.54f, 0.80f, 1f);
                default: return new Color(0.84f, 0.36f, 0.26f, 1f);
            }
        }

        internal static string FormatPercent(float fraction)
        {
            return Mathf.RoundToInt(Mathf.Abs(fraction) * 100f) + "%";
        }
    }
}
