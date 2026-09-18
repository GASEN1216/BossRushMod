using System;

namespace BossRush
{
    /// <summary>
    /// 头目 / 岛主的招式类型：一种一个控制器，不拿同一套编排换参数（防换皮）。
    /// 断风游猎是同一家族的三个变体（<see cref="SkyIslandBossProfile.Variant"/>）：共用一个控制器，冲步按变体递进（追、伏、守各不相同）。
    /// </summary>
    internal enum SkyIslandBossKind
    {
        /// <summary>残星匠首：G 残星工坊 · 星穹工台的岛主。</summary>
        Foreman = 0,
        /// <summary>瞭台观星手：S4 残星瞭台 · 高台的头目。</summary>
        Stargazer = 1,
        /// <summary>悬根猎首：D 悬根林 · 大根树下的岛主（R2）。</summary>
        RootHunter = 2,
        /// <summary>截信人：S2 倒挂邮亭的头目（R2）。</summary>
        Waylayer = 3,
        /// <summary>穗镰：C 青穗梯田 · 水车渠与谷仓之间的岛主（R3）。</summary>
        Sickle = 4,
        /// <summary>听雨人：S3 听雨洞的头目（R3）。</summary>
        Listener = 5,
        /// <summary>蚋笛翁：S1 蛙鸣池的头目，只在夜里出来（R3）。</summary>
        Piper = 6,
        /// <summary>镜中客：F 镜水寺的头目，只在夜里出来（R4）。</summary>
        Mirror = 7,
        /// <summary>断风游猎：K1 / K2 / K3 中继平台上的三位头目，与岛上其余敌人不是一伙（R4）。</summary>
        Windhunter = 8
    }

    /// <summary>Boss 身上的一件专属装备：穿在哪个槽、是哪件、死后被抽中的权重。</summary>
    internal sealed class SkyIslandBossGearPiece
    {
        /// <summary>官方槽位 key（头盔是官方拼写 "Helmat"；另有 "Armor" / "Backpack" / "FaceMask" / "Headset"）。</summary>
        internal string Slot;
        internal int TypeId;
        /// <summary>整数权重，便于执行回归按种子精确计频。</summary>
        internal int Weight;
    }

    /// <summary>一件专属装备的数值：物品配置器与 Boss 配装共用这一份（价值与中英名仍只在 <see cref="SkyIslandItemRules"/>）。</summary>
    internal sealed class SkyIslandBossGearSpec
    {
        internal int TypeId;
        internal string Slot;
        /// <summary>穿上时的官方 stat（对任何穿戴者都生效：Boss 穿着就真的挡子弹）。</summary>
        internal string StatKey;
        internal float StatValue;
        /// <summary>
        /// 耐久上限；0 表示不用耐久（背包）。官方只在 Raid 图上磨头盔与护甲（`Health.Hurt`）；
        /// 面罩与耳机的耐久由招式控制器照同一口径在 Boss 被暴击时扣（<see cref="SkyIslandBossProps.WearSoftPiece"/>），归零即「破甲」。
        /// </summary>
        internal float Durability;
        internal int Quality;
        /// <summary>装备 bundle 里的 {名}_{类型} 基名（EquipmentFactory 命名规则，发布后不改）。</summary>
        internal string ModelBaseName;
        internal string IconName;
        /// <summary>`DisplayNameRaw` 本地化键（`BossRush_SkyIsland_*`，发布后不改）；描述键是它加 `_Desc`。</summary>
        internal string LocKey;
    }

    /// <summary>
    /// 一位头目 / 岛主的档案。
    /// 五栏设计说明（小环境 / 核心招式 / 克制 / 装备联动 / 串联）写在档案表每一项上方的 `// 【五栏】` 注释里——
    /// 它们只给维护者与守卫看，不编进运行时：`SkyIslandBossEcologyGuard` 解析核对每栏非空、核心招式两两不同
    /// （同一条生成管线，但不许只换参数）。中英名走 <see cref="SkyIslandBossRules.Name"/>。
    /// </summary>
    internal sealed class SkyIslandBossProfile
    {
        internal string EncounterId;
        internal int Index;
        internal SkyIslandBossKind Kind;
        internal SkyIslandEnemyTier Tier;
        internal string Id;
        /// <summary>官方击杀计数键（`Count/Kills/&lt;key&gt;` 进正式存档）：发布后冻结。</summary>
        internal string NameKey;
        /// <summary>首杀记进剧情手记（`discoveredNotes`）的 id。旗标 16 位已满，走见闻数组。</summary>
        internal string NoteId;
        /// <summary>DuckNpcs.json 的捏脸蓝图 id；空表示沿用底模 preset 的脸。</summary>
        internal string FaceId;
        internal float Health, Damage, Reaction, Scale;
        internal SkyIslandBossGearPiece[] Gear;
        /// <summary>「这一趟没掉专属装备」的权重：岛主为 0（必出一件），头目大于 0。</summary>
        internal int NoDropWeight;
        /// <summary>
        /// 只在夜里出来（蚋笛翁、镜中客）：白天这一组照常只刷随从，带队位留着，玩家夜里走近时再单独补刷（遭遇 owner 判，
        /// 口径是 <see cref="SkyIslandNight"/>）。不在 Forge 里跳过——自动组每个位置每趟只刷一次，跳过就把这一位烧成白板拾荒者。
        /// </summary>
        internal bool NightOnly;
        /// <summary>整组换成另一阵营（断风游猎）：与玩家、也与岛上其余敌人敌对，官方 AI 见了拾荒者会先打起来（便宜版两派）。</summary>
        internal bool RivalFaction;
        /// <summary>同一家族里的第几位（断风游猎：1 追、2 伏、3 守）；其余为 0。</summary>
        internal int Variant;
    }

    /// <summary>
    /// COMPAT：天空岛头目 / 岛主的纯规则——档案表、专属装备数值、掉落抽样、相位、逃圈速度、有效血量、各招式的几何与套装效果。
    ///
    /// 【掉落口径（owner 2026-09-14 拍板）】每次都穿全套；死后按权重只留其中一件，占尸体箱一格（龙王式），
    /// 其余物品照官方尸体箱原样掉。天空岛敌人本来就 `dropBoxOnDead = true`，所以抽样只在官方建箱前的
    /// `BeforeCharacterSpawnLootOnDead` 里做一次（<see cref="SkyIslandBossLoot"/>），不经过 BossRush 奖励箱、不需要 defer 协议。
    ///
    /// 【R2–R4（owner 2026-09-15 批准）】悬根猎首、截信人、穗镰、听雨人、蚋笛翁、镜中客、断风三游猎；截信人真偷岛上耗材、
    /// 夜限定的两位夜里才刷、断风游猎整组换阵营。全部挂在已有自动组的带队位上，id / marker / 人数不变。
    ///
    /// 纯逻辑、无 Unity 依赖：隔离回归直接执行（tests/fixtures/SkyIslandStory）。
    /// </summary>
    internal static class SkyIslandBossRules
    {
        /// <summary>装备 bundle 名（Assets/Equipment/ 下，compile_official.bat 部署段要登记）。</summary>
        internal const string GearBundle = "skyisland_boss_gear";

        internal const string ForemanNote = "Lord_Foreman";
        internal const string StargazerNote = "Chief_Stargazer";
        internal const string RootHunterNote = "Lord_RootHunter";
        internal const string WaylayerNote = "Chief_Waylayer";
        internal const string SickleNote = "Lord_Sickle";
        internal const string ListenerNote = "Chief_Listener";
        internal const string PiperNote = "Chief_Piper";
        internal const string MirrorNote = "Chief_Mirror";
        internal const string WindhunterChaserNote = "Chief_WindhunterChaser";
        internal const string WindhunterStalkerNote = "Chief_WindhunterStalker";
        internal const string WindhunterWardenNote = "Chief_WindhunterWarden";

        /// <summary>有效血量排序用的参考穿甲：官方护甲公式 2/(clamp(护甲-穿甲)+2)。</summary>
        internal const double ReferencePierce = 2.0;

        /// <summary>玩家逃出一个预警圈所需的最低速度（米/秒）。与噬风同一条判据：必须 ≤ 5.5（正常跑动可达）。</summary>
        internal const float MaxEscapeSpeed = 5.5f;

        /// <summary>套装效果要穿几件（任意几件同套即可）。</summary>
        internal const int SetBonusPieces = 2;

        /// <summary>戴着静听耳罩时，所有头目 / 岛主的预警时长乘这么多（它们蓄力的动静你先听见）。</summary>
        internal const float EarmuffsTelegraphFactor = 1.25f;

        // ---- 残星匠首 ----
        /// <summary>血线跨过即立一轮供能桩。三档，不与噬风的四档混用。</summary>
        internal static readonly float[] ForemanPhaseThresholds = { 0.70f, 0.40f, 0.15f };
        internal const int PylonCount = 2;
        internal const float PylonHealth = 45f;
        /// <summary>桩的寿命：到时自灭，避免桩落在够不着的地方把战斗卡死。</summary>
        internal const float PylonLifetime = 25f;
        internal const float PylonMinDistance = 5.5f;
        internal const float PylonMaxDistance = 8.5f;
        /// <summary>桩存活期间加在匠首头甲与身甲上的护甲值；星炉背甲被打穿后只剩一半。</summary>
        internal const float ShieldArmor = 6f;
        internal const float ShieldArmorBroken = 3f;
        internal const float StarfireRadius = 2.6f;
        internal const float StarfireTelegraph = 1.25f;
        internal const float StarfireDamage = 24f;
        /// <summary>两侧星焰圈离中心圈的横向距离：封左右走位，但中间留得出缝。</summary>
        internal const float StarfireSpread = 3.6f;
        internal const float StarfireInterval = 6.5f;
        internal const float StarfireRange = 26f;
        internal const int CastsBeforeOverheat = 2;
        internal const float OverheatSeconds = 4f;
        /// <summary>过热期间物理伤害系数加这么多（ElementFactor_Physics，官方口径）。</summary>
        internal const float OverheatDamageTaken = 0.25f;

        // ---- 瞭台观星手 ----
        internal const float MarkRange = 42f;
        /// <summary>贴到这么近它就不再标记，交给官方 AI 常规射击：给「冲上去」这条解法。</summary>
        internal const float MarkCloseRange = 8f;
        /// <summary>标记圈先跟着玩家瞄准，再原地锁定：逃圈只算锁定那一段。</summary>
        internal const float MarkAimSeconds = 0.8f;
        internal const float MarkLockSeconds = 0.6f;
        internal const float FlareRadius = 2.2f;
        internal const float FlareDamage = 20f;
        internal const int FlareShots = 2;
        internal const float FlareShotGap = 0.4f;
        internal const float MarkCooldown = 6.5f;

        // ---- 悬根猎首 ----
        /// <summary>血线跨过即换一次根洞伏击。</summary>
        internal static readonly float[] RootHunterAmbushThresholds = { 0.75f, 0.45f };
        internal const int RootHollowCount = 3;
        /// <summary>开战时在自己周围挑三处根洞的距离带（米）：够远才叫换位，又不至于跳出这场战斗。</summary>
        internal const float RootHollowMinDistance = 8f;
        internal const float RootHollowMaxDistance = 12f;
        internal const float AmbushRadius = 2.5f;
        internal const float AmbushTelegraph = 1.0f;
        /// <summary>根须面罩被暴击打穿之后，它钻出来之前根洞多亮这么久。</summary>
        internal const float AmbushTelegraphMaskBroken = 2.0f;
        internal const float AmbushDamage = 16f;
        /// <summary>钻出根洞之后愣一下（官方 AI 暂停）：给「看准它钻出来的地方」一个反打窗口。</summary>
        internal const float AmbushStagger = 1.5f;
        internal const float SnareInterval = 9f;
        /// <summary>玩家离它这么近才拉绊索（米）。</summary>
        internal const float SnareRange = 20f;
        /// <summary>两根根桩各离绊索中点这么远：绊索横在你和它之间，长约 8 米。</summary>
        internal const float SnareHalfLength = 4f;
        internal const float SnareStakeHealth = 30f;
        internal const float SnareLifetime = 12f;
        /// <summary>立好之后过这么久绊索才绷紧（线由暗变亮），不许立在脚下当场绊人。</summary>
        internal const float SnareArmSeconds = 0.8f;
        /// <summary>离绊索这么近（米）也算碰上。</summary>
        internal const float SnareTouchWidth = 0.6f;
        internal const float SnareSlowSeconds = 2.5f;
        /// <summary>被绊住的减速（官方 WalkSpeed / RunSpeed 的 PercentageAdd）；藤编甲被打空后只剩一半。</summary>
        internal const float SnareSlow = -0.45f;
        internal const float SnareSlowBroken = -0.225f;
        internal const float SnareDamage = 6f;

        // ---- 截信人 ----
        internal const float SnatchRange = 2.5f;
        /// <summary>伸手预警结束时还在这个距离内就被抢：比起手距离宽一点，退得不够远照样被抢。</summary>
        internal const float SnatchReach = 2.9f;
        internal const float SnatchTelegraph = 0.8f;
        internal const float SnatchCooldown = 7f;
        /// <summary>一场最多抢这么多次：被抢两次还追不上，就是这场该输。</summary>
        internal const int SnatchMax = 2;
        internal const float FleeSeconds = 6f;
        internal const float FleeSpeedBonus = 0.30f;
        /// <summary>血线低于这个比例它把抢来的东西丢在脚下，不再劫包（旧邮包是它的胆）。</summary>
        internal const float WaylayerDropBelow = 0.40f;
        /// <summary>逃点：倒挂邮亭的两处作者标记，挑离玩家远的那个。</summary>
        internal const string WaylayerFleeA = "POI_S2";
        internal const string WaylayerFleeB = "Search_S2";

        // ---- 穗镰 ----
        /// <summary>血线跨过即开一次闸。</summary>
        internal static readonly float[] SicklePhaseThresholds = { 0.80f, 0.50f, 0.25f };
        internal const int MudPatches = 3;
        /// <summary>青穗斗笠被打穿之后，开闸只冲出一块泥。</summary>
        internal const int MudPatchesHatBroken = 1;
        internal const float MudRadius = 3.5f;
        /// <summary>泥从圈里漫开的时间：漫开之前踩进去不减速。</summary>
        internal const float MudTelegraph = 1.2f;
        internal const float MudSeconds = 12f;
        internal const float MudSlow = -0.40f;
        /// <summary>两侧泥块离中间那块的距离：围着你铺，中间那块压在你脚下。</summary>
        internal const float MudSpread = 5.5f;
        internal const float SweepRange = 3f;
        internal const float SweepRadius = 2.2f;
        internal const float SweepTelegraph = 0.9f;
        /// <summary>蓑衣甲被打空之后镰扫慢下来。</summary>
        internal const float SweepTelegraphBroken = 1.6f;
        internal const float SweepDamage = 18f;
        internal const float SweepCooldown = 4f;
        /// <summary>第一次开闸时「谷仓叫人」：唤来这一组还活着的帮手（先清谷仓那边就没人来）。</summary>
        internal const string SickleHelpers = "C_02";
        /// <summary>谷仓门口（作者布局 C_FarmBarn 朝梯田那一侧，世界坐标，与 SkyIslandMosquitoRules 的水域同一口径）。</summary>
        internal const float BarnDoorX = -137f;
        internal const float BarnDoorZ = -104f;
        internal const float BarnDoorY = 10.5f;

        // ---- 听雨人 ----
        /// <summary>听雨洞洞岩的中心（作者布局 S3_RainCave，世界坐标）。</summary>
        internal const float CaveCenterX = 305.5f;
        internal const float CaveCenterZ = -75f;
        /// <summary>玩家在洞岩这么近之内开的枪才算：落石只落在洞口一带，把它引出洞区计数就不涨。</summary>
        internal const float CaveRange = 26f;
        internal const int ShotsPerRockfall = 8;
        /// <summary>玩家自己戴着静听耳罩（自己的枪声压得低）：要开这么多枪才落一次石。</summary>
        internal const int ShotsPerRockfallEarmuffs = 16;
        internal const int RockfallStones = 2;
        internal const float RockfallRadius = 2.4f;
        internal const float RockfallTelegraph = 1.4f;
        internal const float RockfallDamage = 22f;
        internal const float RockfallSpread = 2.8f;
        internal const float RockfallCooldown = 4f;

        // ---- 蚋笛翁 ----
        internal const float FluteInterval = 9f;
        /// <summary>吹笛要站定这么久（官方 AI 暂停）：这一下里打掉它 <see cref="FluteInterruptDamage"/> 点血，笛声就断了。</summary>
        internal const float FluteChannel = 1.0f;
        internal const float FluteInterruptDamage = 8f;
        internal const float FluteRange = 30f;
        internal const float FluteRingRadius = 1.6f;
        internal const int FluteGnatsMin = 2;
        internal const int FluteGnatsMax = 3;
        /// <summary>笛声压过灭蚊灯、把全场云蚋往你身上引的时长。</summary>
        internal const float FluteLureSeconds = 8f;

        // ---- 镜中客 ----
        /// <summary>镜池中心（作者布局 F_MirrorPool，世界坐标）。</summary>
        internal const float PoolCenterX = 175f;
        internal const float PoolCenterZ = -62.333f;
        /// <summary>玩家在镜池这么近之内它才换位：把它拉离镜池就不再翻到你背后。</summary>
        internal const float PoolRange = 45f;
        internal const float SwapInterval = 8f;
        internal const float SwapTelegraph = 1.1f;
        internal const float SwapRadius = 1.8f;
        internal const float SwapDamage = 12f;
        /// <summary>翻到你身后的距离带（米）。下限比圈半径大：圈不会直接亮在脚下，但边退边打的人一步就退进去。</summary>
        internal const float SwapMinDistance = 4f;
        internal const float SwapMaxDistance = 18f;
        internal const float DecoyHealth = 20f;
        internal const float DecoySeconds = 6f;
        /// <summary>倒影被打碎之后它失衡这么久（官方 AI 暂停）。</summary>
        internal const float DecoyStagger = 3f;

        // ---- 断风游猎 ----
        internal const float LungeTelegraph = 0.9f;
        /// <summary>守（K3）的冲锋线亮得最久：三位里最好读的一位，先打它练熟冲步。</summary>
        internal const float LungeTelegraphWarden = 1.2f;
        /// <summary>它那件断风装备打空之后冲步预警翻倍（追 1.8 秒，守 2.4 秒）。</summary>
        internal const float LungeTelegraphBroken = 1.8f;
        internal const float LungeRadius = 1.8f;
        internal const float LungeDamage = 14f;
        /// <summary>冲步停在玩家面前（追的第二步停在身后）这么远。</summary>
        internal const float LungeStandOff = 2.5f;
        internal const float LungeMinRange = 6f;
        internal const float LungeMaxRange = 28f;
        internal const float LungeStagger = 1.2f;
        internal const float LungeCooldown = 7f;
        /// <summary>
        /// 被贴身这么久（秒）之后拉开距离：官方 AI 会一路走到脸上，进了 <see cref="LungeMinRange"/> 以内冲步就再也起不来，
        /// 三位游猎会退化成普通拾荒者（2026-09-16 第八轮 F3：守停在 7.5 m 一路收窄，17 秒零冲步）。
        /// 游猎不跟人贴身肉搏：撤开重新拉线，招牌动作就回来了。
        /// </summary>
        internal const float CloseQuartersSeconds = 2.5f;
        /// <summary>拉开距离时退到离玩家这么远（米）：落在冲步区间里侧一点，撤完马上又能拉线。</summary>
        internal const float DisengageRange = LungeMinRange + 3f;

        /// <summary>
        /// 该不该拉开距离：贴身计时到点、不在冲步中、冷却也到了才撤。<paramref name="closeSeconds"/> 是连续贴身的秒数
        /// （离开 <see cref="LungeMinRange"/> 就清零），<paramref name="cooldownReady"/> 是 nextLungeAt 已过。
        /// </summary>
        internal static bool ShouldDisengage(bool lunging, float closeSeconds, bool cooldownReady)
        {
            return !lunging && cooldownReady && closeSeconds >= CloseQuartersSeconds;
        }

        /// <summary>伏：冲完这么久退回起手点补枪。</summary>
        internal const float StalkerRetreatDelay = 0.6f;
        /// <summary>伏：血线低于这个比例它的行囊散了，不再后撤。</summary>
        internal const float StalkerPackBreakBelow = 0.5f;
        /// <summary>断风套（任两件）在桥与中继平台上的移动加成。</summary>
        internal const float WindbreakBridgeSpeed = 0.12f;
        internal const int WindhunterChaser = 1;
        internal const int WindhunterStalker = 2;
        internal const int WindhunterWarden = 3;

        private static readonly SkyIslandBossGearSpec[] gearSpecs =
        {
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandStarbrassVisorHelm, Slot = "Helmat", StatKey = "HeadArmor", StatValue = 3f,
                Durability = 70f, Quality = 5, ModelBaseName = "StarbrassVisor_Helmet", IconName = "sky_island_starbrass_visor_helm",
                LocKey = "BossRush_SkyIsland_StarbrassVisorHelm"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandStarfurnaceHarness, Slot = "Armor", StatKey = "BodyArmor", StatValue = 3f,
                Durability = 100f, Quality = 5, ModelBaseName = "StarfurnaceHarness_Armor", IconName = "sky_island_starfurnace_harness",
                LocKey = "BossRush_SkyIsland_StarfurnaceHarness"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandStarfurnacePack, Slot = "Backpack", StatKey = "InventoryCapacity", StatValue = 6f,
                Durability = 0f, Quality = 5, ModelBaseName = "StarfurnacePack_Backpack", IconName = "sky_island_starfurnace_pack",
                LocKey = "BossRush_SkyIsland_StarfurnacePack"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandStargazerLensHelm, Slot = "Helmat", StatKey = "HeadArmor", StatValue = 2f,
                Durability = 50f, Quality = 5, ModelBaseName = "StargazerLens_Helmet", IconName = "sky_island_stargazer_lens_helm",
                LocKey = "BossRush_SkyIsland_StargazerLensHelm"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandRootweaveMask, Slot = "FaceMask", StatKey = "HeadArmor", StatValue = 1f,
                Durability = 40f, Quality = 5, ModelBaseName = "RootweaveMask_FaceMask", IconName = "sky_island_rootweave_mask",
                LocKey = "BossRush_SkyIsland_RootweaveMask"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandVinewovenCuirass, Slot = "Armor", StatKey = "BodyArmor", StatValue = 3f,
                Durability = 100f, Quality = 5, ModelBaseName = "VinewovenCuirass_Armor", IconName = "sky_island_vinewoven_cuirass",
                LocKey = "BossRush_SkyIsland_VinewovenCuirass"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandHangrootQuiver, Slot = "Backpack", StatKey = "InventoryCapacity", StatValue = 6f,
                Durability = 0f, Quality = 5, ModelBaseName = "HangrootQuiver_Backpack", IconName = "sky_island_hangroot_quiver",
                LocKey = "BossRush_SkyIsland_HangrootQuiver"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandOldMailbag, Slot = "Backpack", StatKey = "InventoryCapacity", StatValue = 4f,
                Durability = 0f, Quality = 4, ModelBaseName = "OldMailbag_Backpack", IconName = "sky_island_old_mailbag",
                LocKey = "BossRush_SkyIsland_OldMailbag"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandGreenearStrawHat, Slot = "Helmat", StatKey = "HeadArmor", StatValue = 2f,
                Durability = 60f, Quality = 5, ModelBaseName = "GreenearStrawHat_Helmet", IconName = "sky_island_greenear_straw_hat",
                LocKey = "BossRush_SkyIsland_GreenearStrawHat"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandStrawRaincoat, Slot = "Armor", StatKey = "BodyArmor", StatValue = 3f,
                Durability = 90f, Quality = 5, ModelBaseName = "StrawRaincoat_Armor", IconName = "sky_island_straw_raincoat",
                LocKey = "BossRush_SkyIsland_StrawRaincoat"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandGrainSack, Slot = "Backpack", StatKey = "InventoryCapacity", StatValue = 7f,
                Durability = 0f, Quality = 5, ModelBaseName = "GrainSack_Backpack", IconName = "sky_island_grain_sack",
                LocKey = "BossRush_SkyIsland_GrainSack"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandRainhushEarmuffs, Slot = "Headset", StatKey = "HearingAbility", StatValue = 0.5f,
                Durability = 40f, Quality = 4, ModelBaseName = "RainhushEarmuffs_Headset", IconName = "sky_island_rainhush_earmuffs",
                LocKey = "BossRush_SkyIsland_RainhushEarmuffs"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandMossgauzeMask, Slot = "FaceMask", StatKey = "HeadArmor", StatValue = 1f,
                Durability = 40f, Quality = 4, ModelBaseName = "MossgauzeMask_FaceMask", IconName = "sky_island_mossgauze_mask",
                LocKey = "BossRush_SkyIsland_MossgauzeMask"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandMirrorgrainPlate, Slot = "Armor", StatKey = "BodyArmor", StatValue = 2f,
                Durability = 80f, Quality = 5, ModelBaseName = "MirrorgrainPlate_Armor", IconName = "sky_island_mirrorgrain_plate",
                LocKey = "BossRush_SkyIsland_MirrorgrainPlate"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandWindbreakHood, Slot = "Helmat", StatKey = "HeadArmor", StatValue = 2f,
                Durability = 50f, Quality = 4, ModelBaseName = "WindbreakHood_Helmet", IconName = "sky_island_windbreak_hood",
                LocKey = "BossRush_SkyIsland_WindbreakHood"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandWindbreakMantle, Slot = "Armor", StatKey = "BodyArmor", StatValue = 2f,
                Durability = 70f, Quality = 4, ModelBaseName = "WindbreakMantle_Armor", IconName = "sky_island_windbreak_mantle",
                LocKey = "BossRush_SkyIsland_WindbreakMantle"
            },
            new SkyIslandBossGearSpec
            {
                TypeId = BossRushItemIds.SkyIslandWindbreakPack, Slot = "Backpack", StatKey = "InventoryCapacity", StatValue = 5f,
                Durability = 0f, Quality = 4, ModelBaseName = "WindbreakPack_Backpack", IconName = "sky_island_windbreak_pack",
                LocKey = "BossRush_SkyIsland_WindbreakPack"
            }
        };

        private static readonly SkyIslandBossProfile[] profiles =
        {
            // 【五栏】小环境：G 残星工坊 · 星穹工台，星灯与工台之间的开阔地
            // 【五栏】核心招式：立星炉供能桩给自己加护甲，星焰落点封左右走位，放完两招星炉过热
            // 【五栏】克制：先打掉供能桩（25 秒自灭）；抓过热那 4 秒输出；星焰圈 1.25 秒预警，从两圈之间的缝走
            // 【五栏】装备联动：星铜护目盔被爆头打穿，星焰只落 1 处；星炉背甲耐久打空，供能桩给的护甲减半
            // 【五栏】串联：G 组带队，修星灯本来就要清 G 组；首杀记手记 Lord_Foreman；星工装备任意两件在渡口工台做配方少耗 1 片残铜片
            new SkyIslandBossProfile
            {
                EncounterId = "G", Index = 0, Kind = SkyIslandBossKind.Foreman, Tier = SkyIslandEnemyTier.Lord, Id = "Foreman",
                NameKey = "BossRush_SkyIsland_Lord_Foreman", NoteId = ForemanNote, FaceId = "skyboss_foreman",
                Health = 7.0f, Damage = 1.65f, Reaction = 1.55f, Scale = 1.12f, NoDropWeight = 0,
                Gear = new[]
                {
                    Piece("Helmat", BossRushItemIds.SkyIslandStarbrassVisorHelm, 35),
                    Piece("Armor", BossRushItemIds.SkyIslandStarfurnaceHarness, 35),
                    Piece("Backpack", BossRushItemIds.SkyIslandStarfurnacePack, 30)
                }
            },
            // 【五栏】小环境：S4 残星瞭台 · 群岛最高的观星台
            // 【五栏】核心招式：有视线时远程标记玩家，锁定后在标记处落两发星火
            // 【五栏】克制：躲进掩体断开视线就打断标记；贴到 8 米内它不再标记；锁定后 0.6 秒内出圈
            // 【五栏】装备联动：观星镜盔被爆头打穿，它再也标记不了
            // 【五栏】串联：S4 组带队，校准观星镜本来就要清 S4 组；首杀记手记 Chief_Stargazer；玩家戴上观星镜盔站定 2 秒会标出 40 米内的敌人
            new SkyIslandBossProfile
            {
                EncounterId = "S4", Index = 0, Kind = SkyIslandBossKind.Stargazer, Tier = SkyIslandEnemyTier.Chief, Id = "Stargazer",
                NameKey = "BossRush_SkyIsland_Chief_Stargazer", NoteId = StargazerNote, FaceId = "skyboss_stargazer",
                Health = 3.4f, Damage = 1.45f, Reaction = 1.38f, Scale = 1.0f, NoDropWeight = 70,
                Gear = new[] { Piece("Helmat", BossRushItemIds.SkyIslandStargazerLensHelm, 30) }
            },
            // 【五栏】小环境：D 悬根林 · 风标底下的大根树林地，树根间到处是能钻人的根洞
            // 【五栏】核心招式：在你和它之间立两根根桩拉一道绊索，跨过去就被绊住减速；血线 75% / 45% 时从三处根洞之一亮圈钻出来伏击
            // 【五栏】克制：先打掉任意一根根桩（30 血）绊索就断；看见根洞亮圈就离开那处；它钻出来愣 1.5 秒，趁机输出
            // 【五栏】装备联动：根须面罩被暴击打穿，钻出来之前根洞亮得更久（1 秒变 2 秒）；藤编甲耐久打空，绊索的减速只剩一半
            // 【五栏】串联：D 组带队，修风标本来就要清 D 与 D_02；首杀记手记 Lord_RootHunter；悬根猎装任穿两件翻搜刮箱，岛上特产（便当、药膏、罗盘）的机会翻倍
            new SkyIslandBossProfile
            {
                EncounterId = "D", Index = 0, Kind = SkyIslandBossKind.RootHunter, Tier = SkyIslandEnemyTier.Lord, Id = "RootHunter",
                NameKey = "BossRush_SkyIsland_Lord_RootHunter", NoteId = RootHunterNote, FaceId = "skyboss_roothunter",
                Health = 6.2f, Damage = 1.6f, Reaction = 1.5f, Scale = 1.08f, NoDropWeight = 0,
                Gear = new[]
                {
                    Piece("FaceMask", BossRushItemIds.SkyIslandRootweaveMask, 35),
                    Piece("Armor", BossRushItemIds.SkyIslandVinewovenCuirass, 35),
                    Piece("Backpack", BossRushItemIds.SkyIslandHangrootQuiver, 30)
                }
            },
            // 【五栏】小环境：S2 倒挂邮亭 · 倒挂的邮亭与两处出口，截下来的信件堆了一地
            // 【五栏】核心招式：贴身时伸手（脚下亮圈 0.8 秒）抢走你背包里一件岛上耗材，随即闪到邮亭离你远的那一头，跑速暴涨 6 秒
            // 【五栏】克制：别让它贴身，伸手亮圈时后撤；被抢了就追到逃点打，东西就在它身上，倒下时照样进尸体箱
            // 【五栏】装备联动：旧邮包是它的胆，血线打到四成以下它把抢来的东西丢在脚下、不再劫包
            // 【五栏】串联：S2 组带队，拼回旧信的谜题在 S2；首杀记手记 Chief_Waylayer；玩家背着旧邮包上岛，信鸽这一趟多送一封
            new SkyIslandBossProfile
            {
                EncounterId = "S2", Index = 0, Kind = SkyIslandBossKind.Waylayer, Tier = SkyIslandEnemyTier.Chief, Id = "Waylayer",
                NameKey = "BossRush_SkyIsland_Chief_Waylayer", NoteId = WaylayerNote, FaceId = "skyboss_waylayer",
                Health = 3.2f, Damage = 1.4f, Reaction = 1.4f, Scale = 1.0f, NoDropWeight = 70,
                Gear = new[] { Piece("Backpack", BossRushItemIds.SkyIslandOldMailbag, 30) }
            },
            // 【五栏】小环境：C 青穗梯田 · 水车渠与谷仓之间，一层层的田埂和闸口
            // 【五栏】核心招式：血线 80% / 50% / 25% 时开闸放水，在你周围冲出三块烂泥，站进去移动变慢；第一次开闸还把谷仓那边的帮手喊过来；贴身时抡镰扫一圈
            // 【五栏】克制：别站在泥里打；先清掉谷仓那一组（C_02）它就喊不来人；保持 3 米外，镰扫亮圈就退
            // 【五栏】装备联动：青穗斗笠被爆头打穿，开闸只冲出一块泥；蓑衣甲耐久打空，镰扫的预警慢下来（0.9 秒变 1.6 秒）
            // 【五栏】串联：C 组带队，晴禾的菜畦与灶台都在 C；首杀记手记 Lord_Sickle；蓑衣农装任穿两件去割青穗草，一次多割一份
            new SkyIslandBossProfile
            {
                EncounterId = "C", Index = 0, Kind = SkyIslandBossKind.Sickle, Tier = SkyIslandEnemyTier.Lord, Id = "Sickle",
                NameKey = "BossRush_SkyIsland_Lord_Sickle", NoteId = SickleNote, FaceId = "skyboss_sickle",
                Health = 6.6f, Damage = 1.62f, Reaction = 1.52f, Scale = 1.14f, NoDropWeight = 0,
                Gear = new[]
                {
                    Piece("Helmat", BossRushItemIds.SkyIslandGreenearStrawHat, 35),
                    Piece("Armor", BossRushItemIds.SkyIslandStrawRaincoat, 35),
                    Piece("Backpack", BossRushItemIds.SkyIslandGrainSack, 30)
                }
            },
            // 【五栏】小环境：S3 听雨洞 · 洞岩前的湿地，洞顶的石头被雨水泡得松动
            // 【五栏】核心招式：数你在洞口一带开的枪，每满 8 枪就循着枪声让洞顶在你身边落两块石头
            // 【五栏】克制：在洞口少开枪、换近战或蒲扇；把它引出洞区再打计数就不涨；落石圈 1.4 秒预警，看见就挪开
            // 【五栏】装备联动：静听耳罩被暴击打穿，它听不见枪声，再也引不下石头
            // 【五栏】串联：S3 组带队，航路图谜题在 S3；首杀记手记 Chief_Listener；玩家戴着静听耳罩，听雨人要 16 枪才引一次石，所有头目与岛主的预警也亮得更久
            new SkyIslandBossProfile
            {
                EncounterId = "S3", Index = 0, Kind = SkyIslandBossKind.Listener, Tier = SkyIslandEnemyTier.Chief, Id = "Listener",
                NameKey = "BossRush_SkyIsland_Chief_Listener", NoteId = ListenerNote, FaceId = "skyboss_listener",
                Health = 3.6f, Damage = 1.42f, Reaction = 1.36f, Scale = 1.0f, NoDropWeight = 70,
                Gear = new[] { Piece("Headset", BossRushItemIds.SkyIslandRainhushEarmuffs, 30) }
            },
            // 【五栏】小环境：S1 蛙鸣池 · 夜里的池边，云蚋贴着水面打转
            // 【五栏】核心招式：站定吹笛（1 秒），把附近的云蚋招到你身边、8 秒内所有云蚋都往你身上扑（压过灭蚊灯）
            // 【五栏】克制：现成的七种驱蚋手段都管用；吹笛那一秒打掉它 8 点血笛声就断；白天去找不到它
            // 【五栏】装备联动：苔纱面罩被暴击打穿，它吹不响笛子
            // 【五栏】串联：S1 组带队（只在夜里），蛙卵放生与种植记录在 S1；首杀记手记 Chief_Piper；玩家戴着苔纱面罩，身边的云蚋认不出你在瞄它、躲不开枪口
            new SkyIslandBossProfile
            {
                EncounterId = "S1", Index = 0, Kind = SkyIslandBossKind.Piper, Tier = SkyIslandEnemyTier.Chief, Id = "Piper",
                NameKey = "BossRush_SkyIsland_Chief_Piper", NoteId = PiperNote, FaceId = "skyboss_piper",
                Health = 3.0f, Damage = 1.38f, Reaction = 1.34f, Scale = 1.0f, NoDropWeight = 70, NightOnly = true,
                Gear = new[] { Piece("FaceMask", BossRushItemIds.SkyIslandMossgauzeMask, 30) }
            },
            // 【五栏】小环境：F 镜水寺 · 夜里的镜池与大殿之间，池面把人影照得一清二楚
            // 【五栏】核心招式：在你背后与它对称的位置亮圈，1.1 秒后翻过去，原地留一个会吸准星的半透明倒影
            // 【五栏】克制：看见身后亮圈就转身、挪出圈；先打碎倒影它会失衡 3 秒；把它拉离镜池它就不再翻身
            // 【五栏】装备联动：镜纹甲耐久打空，它照样翻身，但再也留不下倒影
            // 【五栏】串联：F 组带队（只在夜里），折翎守着镜水寺；首杀记手记 Chief_Mirror；玩家穿着镜纹甲去见折翎，不带旧信与航路图也能和解
            new SkyIslandBossProfile
            {
                EncounterId = "F", Index = 0, Kind = SkyIslandBossKind.Mirror, Tier = SkyIslandEnemyTier.Chief, Id = "Mirror",
                NameKey = "BossRush_SkyIsland_Chief_Mirror", NoteId = MirrorNote, FaceId = "skyboss_mirror",
                Health = 3.6f, Damage = 1.48f, Reaction = 1.42f, Scale = 1.04f, NoDropWeight = 70, NightOnly = true,
                Gear = new[] { Piece("Armor", BossRushItemIds.SkyIslandMirrorgrainPlate, 30) }
            },
            // 【五栏】小环境：K1 西北回程捷径的中继平台，桥面窄、两头通风
            // 【五栏】核心招式：地上画一条冲锋线，连冲两步——第一步停在你面前震一下，第二步追到你身后再震一下
            // 【五栏】克制：冲锋线亮起就横着让开，两步都要让；每冲完一轮它硬直 1.2 秒；它和拾荒者不是一伙，引过去让它们先打
            // 【五栏】装备联动：断风披甲耐久打空，冲锋线要亮两倍久
            // 【五栏】串联：K1_Relay 组带队（整组另一阵营），风铃集留言板上有它们的传闻；首杀记手记 Chief_WindhunterChaser；断风套任穿两件，走桥与中继平台更快
            new SkyIslandBossProfile
            {
                EncounterId = "K1_Relay", Index = 0, Kind = SkyIslandBossKind.Windhunter, Tier = SkyIslandEnemyTier.Chief, Id = "WindhunterChaser",
                NameKey = "BossRush_SkyIsland_Chief_WindhunterChaser", NoteId = WindhunterChaserNote, FaceId = "skyboss_windhunter_chaser",
                Health = 3.0f, Damage = 1.44f, Reaction = 1.42f, Scale = 1.0f, NoDropWeight = 60, RivalFaction = true, Variant = WindhunterChaser,
                Gear = new[] { Piece("Armor", BossRushItemIds.SkyIslandWindbreakMantle, 40) }
            },
            // 【五栏】小环境：K2 东侧回程捷径的中继平台，平台边缘有掩体
            // 【五栏】核心招式：冲一步停在你面前震一下，随即闪回起手点、躲在平台边缘补枪
            // 【五栏】克制：冲锋线亮起就横着让开；它闪回之后别追到平台边缘去对枪，等它下一次冲过来的硬直；它和拾荒者不是一伙
            // 【五栏】装备联动：血线打到一半它的断风行囊散开，不再闪回
            // 【五栏】串联：K2_Relay 组带队（整组另一阵营），风铃集留言板上有它们的传闻；首杀记手记 Chief_WindhunterStalker；断风套任穿两件，走桥与中继平台更快
            new SkyIslandBossProfile
            {
                EncounterId = "K2_Relay", Index = 0, Kind = SkyIslandBossKind.Windhunter, Tier = SkyIslandEnemyTier.Chief, Id = "WindhunterStalker",
                NameKey = "BossRush_SkyIsland_Chief_WindhunterStalker", NoteId = WindhunterStalkerNote, FaceId = "skyboss_windhunter_stalker",
                Health = 3.0f, Damage = 1.44f, Reaction = 1.42f, Scale = 1.0f, NoDropWeight = 60, RivalFaction = true, Variant = WindhunterStalker,
                Gear = new[] { Piece("Backpack", BossRushItemIds.SkyIslandWindbreakPack, 40) }
            },
            // 【五栏】小环境：K3 中央回程捷径的中继平台，离风铃集最近
            // 【五栏】核心招式：只冲一步、落地震一下，冲之前冲锋线最长，是三位里最好读的一位
            // 【五栏】克制：冲锋线亮起就横着让开，冲完的硬直里输出；先打它练熟冲步，再去 K1 与 K2；它和拾荒者不是一伙
            // 【五栏】装备联动：断风兜帽被爆头打穿，冲锋线要亮两倍久
            // 【五栏】串联：K3_Relay 组带队（整组另一阵营），风铃集留言板上有它们的传闻；首杀记手记 Chief_WindhunterWarden；断风套任穿两件，走桥与中继平台更快
            new SkyIslandBossProfile
            {
                EncounterId = "K3_Relay", Index = 0, Kind = SkyIslandBossKind.Windhunter, Tier = SkyIslandEnemyTier.Chief, Id = "WindhunterWarden",
                NameKey = "BossRush_SkyIsland_Chief_WindhunterWarden", NoteId = WindhunterWardenNote, FaceId = "skyboss_windhunter_warden",
                Health = 3.0f, Damage = 1.44f, Reaction = 1.42f, Scale = 1.0f, NoDropWeight = 60, RivalFaction = true, Variant = WindhunterWarden,
                Gear = new[] { Piece("Helmat", BossRushItemIds.SkyIslandWindbreakHood, 40) }
            }
        };

        internal static SkyIslandBossProfile[] Profiles { get { return profiles; } }
        internal static SkyIslandBossGearSpec[] GearSpecs { get { return gearSpecs; } }

        /// <summary>全部专属装备 TypeID（按 TypeID 递增）。</summary>
        internal static readonly int[] AllGearTypeIds =
        {
            BossRushItemIds.SkyIslandStarbrassVisorHelm, BossRushItemIds.SkyIslandStarfurnaceHarness,
            BossRushItemIds.SkyIslandStarfurnacePack, BossRushItemIds.SkyIslandStargazerLensHelm,
            BossRushItemIds.SkyIslandRootweaveMask, BossRushItemIds.SkyIslandVinewovenCuirass,
            BossRushItemIds.SkyIslandHangrootQuiver, BossRushItemIds.SkyIslandOldMailbag,
            BossRushItemIds.SkyIslandGreenearStrawHat, BossRushItemIds.SkyIslandStrawRaincoat,
            BossRushItemIds.SkyIslandGrainSack, BossRushItemIds.SkyIslandRainhushEarmuffs,
            BossRushItemIds.SkyIslandMossgauzeMask, BossRushItemIds.SkyIslandMirrorgrainPlate,
            BossRushItemIds.SkyIslandWindbreakHood, BossRushItemIds.SkyIslandWindbreakMantle,
            BossRushItemIds.SkyIslandWindbreakPack
        };

        /// <summary>悬根猎装（悬根猎首的三件）。</summary>
        internal static readonly int[] RootweaveSet =
        {
            BossRushItemIds.SkyIslandRootweaveMask, BossRushItemIds.SkyIslandVinewovenCuirass, BossRushItemIds.SkyIslandHangrootQuiver
        };

        /// <summary>蓑衣农装（穗镰的三件）。</summary>
        internal static readonly int[] SickleSet =
        {
            BossRushItemIds.SkyIslandGreenearStrawHat, BossRushItemIds.SkyIslandStrawRaincoat, BossRushItemIds.SkyIslandGrainSack
        };

        /// <summary>断风套（三位断风游猎各穿一件）。</summary>
        internal static readonly int[] WindbreakSet =
        {
            BossRushItemIds.SkyIslandWindbreakHood, BossRushItemIds.SkyIslandWindbreakMantle, BossRushItemIds.SkyIslandWindbreakPack
        };

        /// <summary>这一组第 <paramref name="index"/> 名是不是头目 / 岛主。不是返回 null（走普通档次装饰）。</summary>
        internal static SkyIslandBossProfile Find(string encounterId, int index)
        {
            if (string.IsNullOrEmpty(encounterId)) return null;
            for (int i = 0; i < profiles.Length; i++)
                if (profiles[i].Index == index && string.Equals(profiles[i].EncounterId, encounterId, StringComparison.Ordinal))
                    return profiles[i];
            return null;
        }

        /// <summary>按档案 id 取（F3 步骤表与演练用；大小写不敏感）。</summary>
        internal static SkyIslandBossProfile FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < profiles.Length; i++)
                if (string.Equals(profiles[i].Id, id, StringComparison.OrdinalIgnoreCase)) return profiles[i];
            return null;
        }

        /// <summary>这一组的带队是不是「只在夜里出来」的头目。</summary>
        internal static bool LeadIsNightOnly(string encounterId)
        {
            SkyIslandBossProfile profile = Find(encounterId, 0);
            return profile != null && profile.NightOnly;
        }

        /// <summary>这一组是不是整组换阵营（断风游猎）。</summary>
        internal static bool IsRivalFaction(string encounterId)
        {
            SkyIslandBossProfile profile = Find(encounterId, 0);
            return profile != null && profile.RivalFaction;
        }

        internal static SkyIslandBossGearSpec GearSpec(int typeId)
        {
            for (int i = 0; i < gearSpecs.Length; i++)
                if (gearSpecs[i].TypeId == typeId) return gearSpecs[i];
            return null;
        }

        /// <summary>首杀手记 id 是否登记过（剧情存档只收登记过的 id）。</summary>
        internal static bool IsBossNote(string id)
        {
            for (int i = 0; i < profiles.Length; i++)
                if (string.Equals(profiles[i].NoteId, id, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static SkyIslandBossProfile FindByNote(string id)
        {
            for (int i = 0; i < profiles.Length; i++)
                if (string.Equals(profiles[i].NoteId, id, StringComparison.Ordinal)) return profiles[i];
            return null;
        }

        /// <summary>中英名对照表的中文那一半（取用走 <see cref="Name"/>）。改名要连 Wiki 与首杀字幕一起改。</summary>
        internal static string NameCn(SkyIslandBossKind kind)
        {
            switch (kind)
            {
                case SkyIslandBossKind.Foreman: return "残星匠首";
                case SkyIslandBossKind.Stargazer: return "瞭台观星手";
                case SkyIslandBossKind.RootHunter: return "悬根猎首";
                case SkyIslandBossKind.Waylayer: return "截信人";
                case SkyIslandBossKind.Sickle: return "穗镰";
                case SkyIslandBossKind.Listener: return "听雨人";
                case SkyIslandBossKind.Piper: return "蚋笛翁";
                case SkyIslandBossKind.Mirror: return "镜中客";
                case SkyIslandBossKind.Windhunter: return "断风游猎";
                default: return "头目";
            }
        }

        internal static string NameEn(SkyIslandBossKind kind)
        {
            switch (kind)
            {
                case SkyIslandBossKind.Foreman: return "Starforge Foreman";
                case SkyIslandBossKind.Stargazer: return "Overlook Stargazer";
                case SkyIslandBossKind.RootHunter: return "Hanging-Root Huntmaster";
                case SkyIslandBossKind.Waylayer: return "The Waylayer";
                case SkyIslandBossKind.Sickle: return "Grain Sickle";
                case SkyIslandBossKind.Listener: return "Rain Listener";
                case SkyIslandBossKind.Piper: return "Gnat Piper";
                case SkyIslandBossKind.Mirror: return "Mirror Guest";
                // 与精英档次「断风游猎 / Galebreaker Ranger」同名：三位头目是这群游猎的领头人，名字后缀区分追、伏、守。
                case SkyIslandBossKind.Windhunter: return "Galebreaker Ranger";
                default: return "Chief";
            }
        }

        /// <summary>同一家族里的变体后缀（中文那一半）：断风游猎 · 追 / 伏 / 守。</summary>
        internal static string VariantCn(int variant)
        {
            switch (variant)
            {
                case WindhunterChaser: return " · 追";
                case WindhunterStalker: return " · 伏";
                case WindhunterWarden: return " · 守";
                default: return string.Empty;
            }
        }

        internal static string VariantEn(int variant)
        {
            switch (variant)
            {
                case WindhunterChaser: return " (Chaser)";
                case WindhunterStalker: return " (Stalker)";
                case WindhunterWarden: return " (Warden)";
                default: return string.Empty;
            }
        }

        /// <summary>按当前语言取名（玩家能在游戏里切语言，取用时解析）。</summary>
        internal static string Name(SkyIslandBossProfile profile)
        {
            return profile == null ? string.Empty
                : L10n.T(NameCn(profile.Kind) + VariantCn(profile.Variant), NameEn(profile.Kind) + VariantEn(profile.Variant));
        }

        /// <summary>
        /// 死后留哪一件：返回 <see cref="SkyIslandBossProfile.Gear"/> 的下标，-1 表示这一趟不掉专属装备。
        /// 权重顺序是「各件依次，最后是不掉」；<paramref name="roll"/> 取 [0, 1)，越界夹回。
        /// </summary>
        internal static int RollDrop(SkyIslandBossProfile profile, double roll)
        {
            if (profile == null || profile.Gear == null) return -1;
            int total = Math.Max(0, profile.NoDropWeight);
            for (int i = 0; i < profile.Gear.Length; i++) total += Math.Max(0, profile.Gear[i].Weight);
            if (total <= 0) return -1;
            if (double.IsNaN(roll) || roll < 0.0) roll = 0.0;
            double pick = Math.Min(roll, 0.999999999) * total;
            int accumulated = 0;
            for (int i = 0; i < profile.Gear.Length; i++)
            {
                accumulated += Math.Max(0, profile.Gear[i].Weight);
                if (pick < accumulated) return i;
            }
            return -1;
        }

        /// <summary>这一件的掉率（0..1）。</summary>
        internal static double DropChance(SkyIslandBossProfile profile, int index)
        {
            if (profile == null || profile.Gear == null || index < 0 || index >= profile.Gear.Length) return 0.0;
            int total = Math.Max(0, profile.NoDropWeight);
            for (int i = 0; i < profile.Gear.Length; i++) total += Math.Max(0, profile.Gear[i].Weight);
            return total <= 0 ? 0.0 : Math.Max(0, profile.Gear[index].Weight) / (double)total;
        }

        /// <summary>给定血量比例应处的相位序号（0 = 还没跨过第一档）。</summary>
        internal static int PhaseFor(float fraction, float[] thresholds)
        {
            if (thresholds == null) return 0;
            int result = 0;
            for (int i = 0; i < thresholds.Length; i++) if (fraction <= thresholds[i]) result = i + 1;
            return result;
        }

        /// <summary>逃出半径 <paramref name="radius"/> 的圈、预警 <paramref name="telegraph"/> 秒时的最低速度。</summary>
        internal static float EscapeSpeed(float radius, float telegraph)
        {
            return telegraph <= 0f ? float.PositiveInfinity : radius / telegraph;
        }

        /// <summary>全部会伤人或减速的预警圈（逃圈判据逐圈核；F3 SKY_BOSS_PROFILES 与执行回归共用这一张，下标对应 <see cref="TelegraphRingEscapeSpeed"/>）。</summary>
        internal static readonly string[] TelegraphRingNames = { "starfire", "flare", "ambush", "snatch", "mud", "sweep", "rockfall", "swap", "lunge" };

        /// <summary>第 <paramref name="index"/> 个预警圈的逃圈速度（半径 ÷ 最短预警秒数，不戴静听耳罩）。</summary>
        internal static float TelegraphRingEscapeSpeed(int index)
        {
            switch (index)
            {
                case 0: return EscapeSpeed(StarfireRadius, StarfireTelegraph);
                case 1: return EscapeSpeed(FlareRadius, MarkLockSeconds);
                case 2: return EscapeSpeed(AmbushRadius, AmbushTelegraph);
                case 3: return EscapeSpeed(SnatchRange, SnatchTelegraph);
                case 4: return EscapeSpeed(MudRadius, MudTelegraph);
                case 5: return EscapeSpeed(SweepRadius, SweepTelegraph);
                case 6: return EscapeSpeed(RockfallRadius, RockfallTelegraph);
                case 7: return EscapeSpeed(SwapRadius, SwapTelegraph);
                case 8: return EscapeSpeed(LungeRadius, LungeTelegraph);
                default: return float.PositiveInfinity;
            }
        }

        /// <summary>预警时长：戴着静听耳罩的玩家早一点听见，所有头目 / 岛主的圈都亮得更久（只会更长，逃圈判据按不戴的算）。</summary>
        internal static float TelegraphSeconds(float baseSeconds, bool earmuffsWorn)
        {
            return earmuffsWorn ? baseSeconds * EarmuffsTelegraphFactor : baseSeconds;
        }

        /// <summary>官方护甲减伤系数（`Health.Hurt`）：2 / (clamp(护甲 - 穿甲, 0, 999) + 2)，没有护甲时为 1。</summary>
        internal static double ArmorFactor(double armor, double pierce)
        {
            if (armor <= 0.0) return 1.0;
            return 2.0 / (Math.Max(0.0, Math.Min(999.0, armor - pierce)) + 2.0);
        }

        /// <summary>按参考穿甲折算的有效血量倍率：血量倍率 ÷ 身甲减伤系数。档次排序用它，而不是只看血量倍率。</summary>
        internal static double EffectiveHealth(SkyIslandBossProfile profile)
        {
            if (profile == null) return 0.0;
            double bodyArmor = 0.0;
            if (profile.Gear != null)
                for (int i = 0; i < profile.Gear.Length; i++)
                {
                    SkyIslandBossGearSpec spec = GearSpec(profile.Gear[i].TypeId);
                    if (spec != null && spec.StatKey == "BodyArmor") bodyArmor += spec.StatValue;
                }
            return profile.Health / ArmorFactor(bodyArmor, ReferencePierce);
        }

        /// <summary>这份存档是否已经打倒过这位头目 / 岛主（首杀手记在不在）。只管首杀字幕与手记进度，不影响刷新与掉落。</summary>
        internal static bool Defeated(SkyIslandStoryData data, SkyIslandBossProfile profile)
        {
            return data != null && profile != null && data.discoveredNotes != null &&
                Array.IndexOf(data.discoveredNotes, profile.NoteId) >= 0;
        }

        internal static int DefeatedCount(SkyIslandStoryData data)
        {
            int count = 0;
            for (int i = 0; i < profiles.Length; i++) if (Defeated(data, profiles[i])) count++;
            return count;
        }

        /// <summary>手记「旅程进度」一行：「 · 岛主与头目 3/11」。</summary>
        internal static string ProgressLine(SkyIslandStoryData data)
        {
            return L10n.T(" · 岛主与头目 ", " · island lords and chiefs ") + DefeatedCount(data) + "/" + profiles.Length;
        }

        /// <summary>首杀那一刻的字幕：它是谁、身上那套装备以后怎么拿。</summary>
        internal static string FirstKillCaption(SkyIslandBossProfile profile)
        {
            if (profile == null) return string.Empty;
            switch (profile.Kind)
            {
                case SkyIslandBossKind.Foreman:
                    return L10n.T("第一次打倒残星匠首：它每趟都会回到工坊，每次都穿着全套星工装备，倒下时只留下其中一件。",
                        "The Starforge Foreman is down for the first time. It returns to the workshop every raid in its full Starworks set and leaves one piece behind each time it falls.");
                case SkyIslandBossKind.Stargazer:
                    return L10n.T("第一次打倒瞭台观星手：它每趟都会回到瞭台，倒下时有三成机会留下观星镜盔。",
                        "The overlook stargazer is down for the first time. It returns to the overlook every raid and leaves its lens helm behind three times in ten.");
                case SkyIslandBossKind.RootHunter:
                    return L10n.T("第一次打倒悬根猎首：它每趟都会回到悬根林，每次都穿着全套悬根猎装，倒下时只留下其中一件。",
                        "The Hanging-Root Huntmaster is down for the first time. It returns to the Hanging Root Wood every raid in its full rootweave hunting gear and leaves one piece behind each time it falls.");
                case SkyIslandBossKind.Waylayer:
                    return L10n.T("首次击败截信人。它每趟都会回倒挂邮亭，赃物都在尸体上。有三成机会掉落旧邮包。",
                        "First Waylayer defeat. It returns to the Upturned Post Hut each raid. Recover stolen items from its body. Old Mailbag drop chance: 30%.");
                case SkyIslandBossKind.Sickle:
                    return L10n.T("第一次打倒穗镰：它每趟都会回到青穗梯田，每次都穿着全套蓑衣农装，倒下时只留下其中一件。",
                        "Grain Sickle is down for the first time. It returns to the Green Terraces every raid in its full straw-cloak farm gear and leaves one piece behind each time it falls.");
                case SkyIslandBossKind.Listener:
                    return L10n.T("第一次打倒听雨人：它每趟都会回到听雨洞，倒下时有三成机会留下静听耳罩。",
                        "The Rain Listener is down for the first time. It returns to the Rainlisten Grotto every raid and leaves its rainhush earmuffs behind three times in ten.");
                case SkyIslandBossKind.Piper:
                    return L10n.T("第一次打倒蚋笛翁：它只在夜里回到蛙鸣池，倒下时有三成机会留下苔纱面罩。",
                        "The Gnat Piper is down for the first time. It returns to Frogsong Pool only at night and leaves its mossgauze mask behind three times in ten.");
                case SkyIslandBossKind.Mirror:
                    return L10n.T("第一次打倒镜中客：它只在夜里回到镜水寺，倒下时有三成机会留下镜纹甲。",
                        "The Mirror Guest is down for the first time. It returns to Mirrorwater Temple only at night and leaves its mirrorgrain plate behind three times in ten.");
                case SkyIslandBossKind.Windhunter:
                    return string.Format(L10n.T("首次击败{0}。三位断风游猎各守一座回程中继平台，各穿一件断风装备。每位有四成机会掉落所穿的那件。",
                        "First defeat of {0}. Each Galebreaker holds a return relay platform and wears one set piece. Each has a 40% chance to drop that piece."), Name(profile));
                default:
                    return L10n.T("头目倒下了。", "The chief is down.");
            }
        }

        /// <summary>主角身上穿着几件星工装备（头盔 / 护甲 / 背包三槽各传当前 TypeID，没穿传 0）。</summary>
        internal static int StarworksPiecesWorn(int helmTypeId, int armorTypeId, int packTypeId)
        {
            int count = 0;
            if (helmTypeId == BossRushItemIds.SkyIslandStarbrassVisorHelm) count++;
            if (armorTypeId == BossRushItemIds.SkyIslandStarfurnaceHarness) count++;
            if (packTypeId == BossRushItemIds.SkyIslandStarfurnacePack) count++;
            return count;
        }

        /// <summary>星工两件套：渡口工台配方里的残铜片少耗 1 片（至少还要 1 片）。其余材料与其他台子不变。</summary>
        internal static int BrassScrapCost(int baseCost, int starworksPiecesWorn)
        {
            if (baseCost <= 1 || starworksPiecesWorn < 2) return baseCost;
            return baseCost - 1;
        }

        /// <summary>主角五个装备槽里有几件属于 <paramref name="set"/>（各槽传当前 TypeID，没穿传 0；不用 params，采样节拍上不分配）。</summary>
        internal static int SetPiecesWorn(int[] set, int helm, int armor, int pack, int mask, int headset)
        {
            if (set == null) return 0;
            int count = 0;
            if (Array.IndexOf(set, helm) >= 0) count++;
            if (Array.IndexOf(set, armor) >= 0) count++;
            if (Array.IndexOf(set, pack) >= 0) count++;
            if (Array.IndexOf(set, mask) >= 0) count++;
            if (Array.IndexOf(set, headset) >= 0) count++;
            return count;
        }

        /// <summary>
        /// 悬根猎装（任两件）：搜刮箱里出岛上特产的机会翻倍。做法是把特产那一次抽样的数压一半、门槛不变——
        /// 不多抽随机数，箱子里原有的件数与品质一件不变（<see cref="SkyIslandItemRules.IslandExtraFor"/>）。
        /// </summary>
        internal static double HunterIslandExtraRoll(double roll, int rootweavePiecesWorn)
        {
            return rootweavePiecesWorn >= SetBonusPieces ? roll * 0.5 : roll;
        }

        /// <summary>蓑衣农装（任两件）：割一次青穗草多出的份数。</summary>
        internal static int GrassBonus(int sicklePiecesWorn)
        {
            return sicklePiecesWorn >= SetBonusPieces ? 1 : 0;
        }

        /// <summary>断风套（任两件）：在桥与中继平台上的移动加成（官方 WalkSpeed / RunSpeed 的 PercentageAdd）；不在桥上为 0。</summary>
        internal static float BridgeSpeedBonus(int windbreakPiecesWorn, bool onBridge)
        {
            return onBridge && windbreakPiecesWorn >= SetBonusPieces ? WindbreakBridgeSpeed : 0f;
        }

        /// <summary>听雨人要数满几枪才落一次石：玩家自己戴着静听耳罩时翻倍。</summary>
        internal static int ShotsPerRockfallFor(bool playerEarmuffsWorn)
        {
            return playerEarmuffsWorn ? ShotsPerRockfallEarmuffs : ShotsPerRockfall;
        }

        /// <summary>这个位置算不算听雨洞的洞口一带（落石只落在这里）。</summary>
        internal static bool InCave(float x, float z)
        {
            float dx = x - CaveCenterX, dz = z - CaveCenterZ;
            return dx * dx + dz * dz <= CaveRange * CaveRange;
        }

        /// <summary>这个位置离镜池够不够近（镜中客只在镜池一带翻身）。</summary>
        internal static bool NearMirrorPool(float x, float z)
        {
            float dx = x - PoolCenterX, dz = z - PoolCenterZ;
            return dx * dx + dz * dz <= PoolRange * PoolRange;
        }

        /// <summary>
        /// 倒影换位的落点：把 Boss 相对玩家的位置翻到玩家另一侧，距离夹在 [<see cref="SwapMinDistance"/>, <see cref="SwapMaxDistance"/>]。
        /// 两者几乎重合时没有方向，返回 false。
        /// </summary>
        internal static bool MirrorAcross(float playerX, float playerZ, float bossX, float bossZ, out float x, out float z)
        {
            x = playerX;
            z = playerZ;
            float dx = playerX - bossX, dz = playerZ - bossZ;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (distance < 0.01f) return false;
            float clamped = Math.Max(SwapMinDistance, Math.Min(SwapMaxDistance, distance));
            x = playerX + dx / distance * clamped;
            z = playerZ + dz / distance * clamped;
            return true;
        }

        /// <summary>
        /// 断风冲步的落点：沿 Boss 指向玩家的方向，停在玩家面前 <see cref="LungeStandOff"/> 米；<paramref name="behind"/> 为 true 时停在玩家身后同样远（追的第二步）。
        /// 两者几乎重合时落在原地。
        /// </summary>
        internal static void LungeLanding(float bossX, float bossZ, float playerX, float playerZ, bool behind, out float x, out float z)
        {
            float dx = playerX - bossX, dz = playerZ - bossZ;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (distance < 0.01f) { x = bossX; z = bossZ; return; }
            float along = behind ? distance + LungeStandOff : Math.Max(0f, distance - LungeStandOff);
            x = bossX + dx / distance * along;
            z = bossZ + dz / distance * along;
        }

        /// <summary>
        /// 断风冲步的预警时长（不含静听耳罩）：守最长；它那件断风装备打空之后翻倍。伏穿的是行囊（背包没有耐久），
        /// 它的装备联动是半血后不再闪回，不走这里，<paramref name="pieceBroken"/> 恒传 false。
        /// </summary>
        internal static float LungeTelegraphFor(int variant, bool pieceBroken)
        {
            float baseSeconds = variant == WindhunterWarden ? LungeTelegraphWarden : LungeTelegraph;
            return pieceBroken ? baseSeconds * 2f : baseSeconds;
        }

        /// <summary>两个逃点里离玩家远的那个：返回 0 表示 A、1 表示 B（一样远取 A）。</summary>
        internal static int FartherPoint(float playerX, float playerZ, float ax, float az, float bx, float bz)
        {
            float da = (ax - playerX) * (ax - playerX) + (az - playerZ) * (az - playerZ);
            float db = (bx - playerX) * (bx - playerX) + (bz - playerZ) * (bz - playerZ);
            return db > da ? 1 : 0;
        }

        /// <summary>
        /// 截信人挑哪件下手：只偷岛上的耗材与工具（<see cref="SkyIslandItemRules.StealableTypeIds"/>，不碰官方物品），
        /// 分数是价值，越值钱越先偷；不能偷返回 -1。
        /// </summary>
        internal static int StealScore(int typeId)
        {
            return Array.IndexOf(SkyIslandItemRules.StealableTypeIds, typeId) >= 0 ? Math.Max(1, SkyIslandItemRules.ValueOf(typeId)) : -1;
        }

        /// <summary>悬根猎首的绊索：被绊住的减速（藤编甲被打空之后只剩一半）。</summary>
        internal static float SnareSlowFor(bool cuirassBroken)
        {
            return cuirassBroken ? SnareSlowBroken : SnareSlow;
        }

        /// <summary>
        /// 玩家这一步有没有碰上绊索 AB：这一步的位移线段与绊索相交，或者停在离绊索 <see cref="SnareTouchWidth"/> 米之内。
        /// 只看水平面。
        /// </summary>
        internal static bool SnareTripped(float ax, float az, float bx, float bz, float fromX, float fromZ, float toX, float toZ)
        {
            if (DistanceToSegment(toX, toZ, ax, az, bx, bz) <= SnareTouchWidth) return true;
            return SegmentsCross(ax, az, bx, bz, fromX, fromZ, toX, toZ);
        }

        /// <summary>点 P 到线段 AB 的水平距离。</summary>
        internal static float DistanceToSegment(float px, float pz, float ax, float az, float bx, float bz)
        {
            float abx = bx - ax, abz = bz - az;
            float lengthSqr = abx * abx + abz * abz;
            float t = lengthSqr <= 1e-6f ? 0f : ((px - ax) * abx + (pz - az) * abz) / lengthSqr;
            t = Math.Max(0f, Math.Min(1f, t));
            float cx = ax + abx * t - px, cz = az + abz * t - pz;
            return (float)Math.Sqrt(cx * cx + cz * cz);
        }

        private static bool SegmentsCross(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz)
        {
            float d1 = Cross(ax, az, bx, bz, cx, cz), d2 = Cross(ax, az, bx, bz, dx, dz);
            float d3 = Cross(cx, cz, dx, dz, ax, az), d4 = Cross(cx, cz, dx, dz, bx, bz);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross(float ax, float az, float bx, float bz, float px, float pz)
        {
            return (bx - ax) * (pz - az) - (bz - az) * (px - ax);
        }

        /// <summary>
        /// 居民说起头目 / 岛主：每位居民管自己那几位，按顺序说**第一位还没打倒的**「在哪、怎么躲」；都打倒过就说那几套装备拿去做什么。
        /// 拼接口径与各自台词一致：浮舟、晴禾、眠苔那句以换行开头，苇白那句以换行结尾。
        ///
        /// 【2026-09-17 改写】信息一个不少，说法换成各人自己的口气：浮舟话少说船说料、苇白像在派活、
        /// 晴禾拿农活打比方、眠苔只关心伤口与夜里的东西。原来那版是攻略腔（「先拆供能桩，等炉子过热再打它」），
        /// 放在谁嘴里都一样。**每条仍是两屏**（官方对话一句一屏），关键词
        /// 「星工装备 / Starworks outfit」「悬根林的风标底下蹲着个猎首 / huntmaster crouches」
        /// 「断风游猎 / Galebreaker Ranger holds」「观星手 / stargazer up on」原样保留——
        /// 全自动验收按屏序截图并断言这些词（Assets/Data/SkyIslandAutotest.json）。
        /// </summary>
        internal static string ResidentLine(string npcId, SkyIslandStoryData data)
        {
            switch (npcId)
            {
                case "sky_fuzhou":
                    if (!Defeated(data, Find("G", 0)))
                        return L10n.T("\n星灯那边的匠首，穿着全套星工装备。先拆它的供能桩，炉子过热那几秒才轮到你。",
                            "\nThe Foreman by the star lamp wears the full Starworks outfit. Break its pylons first — your turn comes when the furnace overheats.");
                    if (!Defeated(data, Find("S2", 0)))
                        return L10n.T("\n倒挂邮亭那个专抢便当和药膏。别让它近身，真丢了就打倒它，东西还在它身上。",
                            "\nThe one at the Upturned Post Hut takes bentos and salves. Don't let it close, and if it takes something, put it down — your things are on its body.");
                    return L10n.T("\n星工装备穿两件，我这工台就少收你一片残铜。背上它那只旧邮包，信鸽一趟多来一封。",
                            "\nTwo Starworks pieces and my bench takes one less brass scrap. Carry its old mailbag and the pigeon brings one extra letter.");
                case "sky_weibai":
                    if (!Defeated(data, Find("S4", 0)))
                        return L10n.T("残星瞭台上那个观星手，眼睛毒得很。脚下一亮圈，要么钻掩体，要么冲到它脸上。\n",
                            "The stargazer up on Starfall Overlook has a mean eye. If a ring lights at your feet, find cover or get right in its face.\n");
                    if (!Defeated(data, Find("D", 0)))
                        return L10n.T("悬根林的风标底下蹲着个猎首。它的绊索桩砍一根就断，地上根洞一亮赶紧让开。\n",
                            "A huntmaster crouches under the beacon in Hanging Root Wood. Cut one tripwire stake and the line drops; when a root hollow lights, move.\n");
                    if (!Defeated(data, Find("K1_Relay", 0)) || !Defeated(data, Find("K2_Relay", 0)) || !Defeated(data, Find("K3_Relay", 0)))
                        return L10n.T("三座回程中继，一座一个断风游猎，连拾荒者也打。冲锋前地上先亮一条线，横着让。\n",
                            "A Galebreaker Ranger holds each return relay, and they fight the scavengers too. A line lights before the charge — step sideways.\n");
                    return L10n.T("戴观星镜盔站着别动，远处的人影会给你标出来。悬根套两件好翻箱，断风套两件走桥快。\n",
                            "Stand still in the lens helm and far figures get marked for you. Two Rootweave pieces turn up more local goods; two Galebreaker pieces make the bridges quicker.\n");
                case "sky_qinghe":
                    if (!Defeated(data, Find("C", 0)))
                        return L10n.T("\n梯田上拿镰刀那个，一急就开闸放水，还朝谷仓喊人。先把谷仓清了，泥里别站。",
                            "\nThe one with the sickle opens the sluice when pressed and shouts for the barn. Clear the barn first, and keep out of the mud.");
                    if (!Defeated(data, Find("S3", 0)))
                        return L10n.T("\n听雨洞里少开枪，响得多了顶上会掉石头。真要打，把它引到洞外面去。",
                            "\nDon't fire much in Rainlisten Grotto; too many shots bring the ceiling down. If you must fight, draw it outside.");
                    return L10n.T("\n穗镰那身穿两件，割青穗草一次多一把。它那副耳罩你戴着，出招前能多喘一口。",
                            "\nTwo Grain Sickle pieces give an extra handful of greenear each cut. Wear the earmuffs and you get a breath more before a boss swings.");
                case "sky_miantai":
                    if (!Defeated(data, Find("S1", 0)))
                        return L10n.T("\n蚋笛翁只在夜里的蛙鸣池边。笛子一响云蚋全冲你来，趁它吹的时候打，曲子就断。",
                            "\nThe Gnat Piper only shows at Frogsong Pool at night. The flute pulls every gnat onto you; hit it while it plays and the tune breaks.");
                    if (!Defeated(data, Find("F", 0)))
                        return L10n.T("\n镜中客也是夜里，在镜池边，会换到你背后。它留下的倒影打碎，它就站不稳。",
                            "\nThe Mirror Guest is a night thing too, and it swaps behind you at the pool. Break the reflection it leaves and it can't keep its feet.");
                    return L10n.T("\n苔纱面罩戴上，云蚋认不出你在瞄它。穿镜纹甲去见折翎，没有旧信和航路图他也肯谈。",
                            "\nIn the mossgauze mask, gnats can't read your aim and stop dodging. In the Mirrorgrain Plate, Zheling will talk without the letter or chart.");
                default:
                    return string.Empty;
            }
        }

        private static SkyIslandBossGearPiece Piece(string slot, int typeId, int weight)
        {
            return new SkyIslandBossGearPiece { Slot = slot, TypeId = typeId, Weight = weight };
        }
    }
}
