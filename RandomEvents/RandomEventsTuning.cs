// ============================================================================
// RandomEventsTuning.cs - 局内随机事件「鸭生无常」数值单点（方案二 步骤 1）
// ============================================================================
// 职责：
//   - 随机事件子系统的**全部**可调数值、权重、时长、字符串常量的唯一落点。
//     调度器、事件目录、桥接层、HUD 一律引用本类，禁止在别处写魔法数字。
//
// 硬约束：
//   - 无状态、无逻辑：只有 const / static readonly，禁止方法与可变静态字段
//     （唯一数组 MaxEventsPerRunByFrequency 是只读查表，禁止运行期改写元素）；
//   - 权重与 RandomEventId 一一对应，权重 <= 0 表示不入池；
//   - 时长单位一律「秒」，且都是**受 timeScale 影响**的游戏内秒
//     （与官方 GameClock 同源，由 host 的 deltaTime 驱动）；
//   - 本文件不得引用任何波次状态机符号（tests/RandomEventsWaveIsolationGuard.py 守卫）。
// ============================================================================

namespace BossRush
{
    /// <summary>随机事件数值常量单点。无状态、无逻辑，只有 const / static readonly。</summary>
    internal static class RandomEventsTuning
    {
        #region 标识

        internal const string ModuleName = "RandomEvents";
        internal const string LogPrefix = "[RandomEvents] ";
        internal const string LocalizationPrefix = "BossRush_RandomEvent_";

        #endregion

        #region 调度

        /// <summary>开局冷却：局开始后先静默这么久，避免一进场就被事件打断布置。</summary>
        internal const float OpeningArmDelaySeconds = 90f;

        /// <summary>两次事件之间的冷却下限。</summary>
        internal const float CooldownMinSeconds = 45f;

        /// <summary>两次事件之间的冷却上限。</summary>
        internal const float CooldownMaxSeconds = 75f;

        /// <summary>并发事件数恒为 1（不可调：并发会让播报与清理口径失控）。</summary>
        internal const int MaxConcurrentEvents = 1;

        /// <summary>频率档 1/2/3 对应的单局事件上限（索引 0 未用，仅为让档位直接当下标）。</summary>
        internal static readonly int[] MaxEventsPerRunByFrequency = { 0, 2, 3, 5 };

        /// <summary>频率档缺失或异常时的兜底上限（= 档位 2 的值）。</summary>
        internal const int DefaultMaxEventsPerRun = 3;

        /// <summary>单帧 deltaTime 保险帽（秒），挡加载完成后的首帧尖峰把计时一次性烧穿。</summary>
        internal const float MaxDeltaPerFrame = 1f;

        /// <summary>
        /// run signature 边缘轮询间隔（秒）。
        /// Dormant/Armed 每帧只做一次 float 累加与比较，只有跨过这个间隔才真正去读模式门面，
        /// 满足 AGENTS 4.12「重运行时工作按实际使用状态门控」的每帧成本红线。
        /// 90s 开局冷却远大于该间隔，边缘识别延迟对玩法不可感知。
        /// </summary>
        internal const float RunSignaturePollIntervalSeconds = 0.5f;

        #endregion

        #region 权重（与 RandomEventId 一一对应；<= 0 表示不入池）

        internal const float WeightAirdropSupply = 30f;
        internal const float WeightBloodMoon = 20f;
        internal const float WeightBossIntrusion = 18f;
        internal const float WeightWanderingMerchant = 15f;
        internal const float WeightFeint = 12f;
        internal const float WeightFireworks = 10f;
        internal const float WeightGoldenDuckRain = 12f;
        internal const float WeightDuckParade = 8f;

        #endregion

        #region E1 空投补给

        internal const float AirdropDurationSeconds = 45f;
        internal const float AirdropFallHeight = 28f;
        internal const float AirdropFallSeconds = 2.6f;
        internal const int AirdropItemCount = 4;
        internal const int AirdropQualityMinNormal = 4;
        internal const int AirdropQualityMaxNormal = 7;

        /// <summary>白手起家（ModeD）单独压低品质上限：局内经济曲线更陡。</summary>
        internal const int AirdropQualityMaxModeD = 5;

        internal const int AirdropQualityMaxInfiniteHell = 8;

        /// <summary>落地 AISound 半径：故意引怪来抢，制造「空投是有代价的」。</summary>
        internal const float AirdropLandingSoundRadius = 34f;

        internal const float AirdropMinPlayerDistance = 18f;
        internal const int AirdropLootboxInventoryCapacity = 512;

        #endregion

        #region E2 血月凶兆

        internal const float BloodMoonDurationSeconds = 75f;

        /// <summary>敌人移速加成（PercentageAdd 口径，0.25 = +25%）。</summary>
        internal const float BloodMoonEnemyMoveSpeedBonus = 0.25f;

        /// <summary>敌人伤害加成（PercentageAdd 口径）。</summary>
        internal const float BloodMoonEnemyDamageBonus = 0.20f;

        /// <summary>补挂新怪的节流间隔：禁止每帧枚举角色。</summary>
        internal const float BloodMoonRefreshIntervalSeconds = 2f;

        internal const float BloodMoonVignetteAlphaMin = 0.10f;
        internal const float BloodMoonVignetteAlphaMax = 0.26f;
        internal const float BloodMoonVignetteBreathSeconds = 2.4f;

        /// <summary>献祭：血月期间每击杀一只被挂 buff 的敌人补偿的现金。</summary>
        internal const long BloodMoonCashPerKill = 500L;

        /// <summary>
        /// 官方 stinger 键（不含 "Music/Stinger/" 前缀，AudioManager.PlayStringer 内部会拼）。
        /// </summary>
        internal const string BloodMoonStingerKey = "stg_storm_1";

        #endregion

        #region E3 Boss 乱入

        /// <summary>乱入者的滞留超时：到时未被击杀则「撤退」并自行销毁。</summary>
        internal const float BossIntrusionDurationSeconds = 120f;

        internal const int BossIntrusionCount = 1;
        internal const float BossIntrusionMinPlayerDistance = 30f;

        #endregion

        #region E4 神秘商人路过

        internal const float MerchantDurationSeconds = 60f;

        /// <summary>价格系数：路过商人比常驻商人贵，换取即时性。</summary>
        internal const float MerchantPriceFactor = 1.5f;

        internal const int MerchantRandomHighQualityCount = 1;
        internal const int MerchantHighQualityMin = 6;
        internal const float MerchantSpawnDistance = 6f;

        /// <summary>
        /// 商人 merchantID 稳定常量。
        /// ⚠️ 禁止拼时间戳/随机数：官方 StockShop 会以 "StockShop_&lt;merchantID&gt;" 落一条存档键，
        /// 拼动态后缀会让存档键无界膨胀。
        /// </summary>
        internal const string MerchantIdConstant = "BossRush_RandomEvent_Merchant";

        #endregion

        #region E5 声东击西

        internal const float FeintDurationSeconds = 12f;
        internal const int FeintSoundCount = 6;
        internal const float FeintSoundRadius = 40f;
        internal const float FeintSoundRingRadius = 26f;
        internal const float FeintSoundIntervalSeconds = 1.4f;

        #endregion

        #region E6 鸭王的烟花

        internal const float FireworksDurationSeconds = 14f;
        internal const int FireworksBurstCount = 15;
        internal const float FireworksIntervalSeconds = 0.55f;

        /// <summary>
        /// 半径必须极小：官方 CreateExplosion 会对半径内所有 DamageReceiver 派发一次 Hurt，
        /// 哪怕伤害为 0 也会惊动 AI 并污染「最快击杀」计时。0.05f 让重叠查询命中 0 个接收器。
        /// </summary>
        internal const float FireworksExplosionRadius = 0.05f;

        internal const float FireworksShakeStrength = 0.25f;
        internal const float FireworksRingRadius = 12f;
        internal const float FireworksHeight = 6f;

        #endregion

        #region E7 金鸭雨

        internal const float GoldenDuckRainDurationSeconds = 20f;

        /// <summary>现金物品 TypeID（= 官方 EconomyManager.CashItemID，StackCount 即金额）。</summary>
        internal const int CashItemTypeID = 451;

        internal const long GoldenDuckRainTotalCash = 30000L;
        internal const int GoldenDuckRainPileMin = 10;
        internal const int GoldenDuckRainPileMax = 15;
        internal const float GoldenDuckRainScatterRadius = 5f;

        #endregion

        #region E8 鸭群巡游

        internal const float DuckParadeDurationSeconds = 22f;
        internal const int DuckParadeCountMin = 5;
        internal const int DuckParadeCountMax = 8;
        internal const float DuckParadeSpacing = 1.6f;
        internal const float DuckParadeStartDistance = 22f;

        #endregion

        #region HUD

        /// <summary>事件图标目录（相对 Mod 根）：Assets/ui/random_events/evt_&lt;id&gt;.png。</summary>
        internal const string HudIconDirectory = "ui/random_events";

        internal const float HudBadgeWidth = 260f;
        internal const float HudBadgeHeight = 64f;
        internal const float HudBadgeMarginX = 24f;
        internal const float HudBadgeMarginY = -140f;

        #endregion
    }
}
