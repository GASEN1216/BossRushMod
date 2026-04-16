// ============================================================================
// WishFountainService.cs - 布满了灰尘的星愿许愿台网络服务与内容校验
// ============================================================================
// 模块说明：
//   提供布满了灰尘的星愿许愿台的核心服务：
//   - 文本标准化（全角转半角、空白折叠、换行统一等）
//   - 内容校验（长度、有效字符、重复、链接、敏感词过滤）
//   - 加密配置文件读取（AES-256-CBC）
//   - 通过飞书应用鉴权 + 多维表格 API 写入心愿记录
//   - 复用缓存的 tenant_access_token，并在鉴权失效时自动重试一次
//
// 防污染策略：
//   第一步：StandardizeText — 清洗原始输入
//   第二步：ValidateWishText — 8 条规则逐一校验
//   第三步：发送前限流（全局 30 秒冷却）
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using ItemStatsSystem;
using Saves;

namespace BossRush
{
    /// <summary>
    /// 布满了灰尘的星愿许愿台网络服务与内容校验
    /// </summary>
    public static class WishFountainService
    {
        // ============================================================================
        // 配置常量
        // ============================================================================

        /// <summary>请求超时（秒）</summary>
        private const int REQUEST_TIMEOUT = 15;

        /// <summary>飞书租户 token 刷新预留秒数</summary>
        private const int TOKEN_REFRESH_BUFFER_SECONDS = 60;

        /// <summary>最小字符数</summary>
        internal const int MIN_CHARS = 20;

        /// <summary>最大字符数</summary>
        internal const int MAX_CHARS = 10000;

        /// <summary>全局发送冷却时间（秒）</summary>
        private const float SEND_COOLDOWN = 30f;

        /// <summary>加密配置文件名</summary>
        private const string CONFIG_FILE_NAME = "wish_config.dat";

        /// <summary>加密配置文件头标识</summary>
        private const string CONFIG_HEADER = "WISH_CFG_V2_FEISHU_APP";

        /// <summary>未配置占位标记</summary>
        private const string UNCONFIGURED_CONFIG_VALUE = "UNCONFIGURED";

        /// <summary>飞书 tenant token 接口</summary>
        private const string FEISHU_TENANT_TOKEN_URL = "https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal";

        /// <summary>加密盐值</summary>
        private static readonly byte[] AES_SALT = {
            0x53, 0x74, 0x61, 0x72, 0x57, 0x69, 0x73, 0x68,
            0x46, 0x6F, 0x75, 0x6E, 0x74, 0x61, 0x69, 0x6E
        };

        /// <summary>PBKDF2 迭代次数</summary>
        private const int PBKDF2_ITERATIONS = 10000;

        // ============================================================================
        // 状态字段
        // ============================================================================

        /// <summary>是否正在发送</summary>
        internal static bool IsSending = false;

        /// <summary>上次发送时间（realtimeSinceStartup）</summary>
        private static float lastSendTime = -999f;

        /// <summary>缓存的飞书应用配置</summary>
        private static FeishuAppConfig cachedFeishuConfig = null;

        /// <summary>是否已尝试加载配置</summary>
        private static bool configLoaded = false;

        /// <summary>缓存的 Mod 版本号</summary>
        private static string cachedModVersion = null;

        /// <summary>缓存的 tenant access token</summary>
        private static string cachedTenantAccessToken = null;

        /// <summary>tenant access token 到期时间（UTC）</summary>
        private static DateTime cachedTenantAccessTokenExpiryUtc = DateTime.MinValue;

        /// <summary>许愿奖励冷却（小时）</summary>
        private const int WISH_REWARD_COOLDOWN_HOURS = 4;

        /// <summary>许愿奖励存档键</summary>
        private const string WISH_REWARD_NEXT_AVAILABLE_SAVE_KEY = "BossRush_WishReward_NextAvailableTicks";

        /// <summary>基础品质权重（Q1~Q8）</summary>
        private const int WISH_REWARD_MIN_QUALITY = 4;

        private static readonly float[] WishRewardBaseQualityWeights =
        {
            0f, 0f, 0f, 13f, 9f, 5f, 3f, 2f
        };

        private const float WISH_REWARD_QUALITY_BIAS_CAP = 1.6f;
        private const float WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER = 2f;
        private const float WISH_REWARD_ITEM_BIAS_CAP_Q1_TO_Q4 = 1.9f;
        private const float WISH_REWARD_ITEM_BIAS_CAP_Q5_TO_Q6 = 1.7f;
        private const float WISH_REWARD_ITEM_BIAS_CAP_Q7 = 1.5f;
        private const float WISH_REWARD_ITEM_BIAS_CAP_Q8 = 1.2f;
        private const float WISH_REWARD_EXACT_ITEM_QUALITY_BIAS = 1.25f;
        private const float WISH_REWARD_POOL_INIT_RETRY_SECONDS = 8f;
        private const int WISH_REWARD_POOL_BUILD_YIELD_INTERVAL = 24;

        private static long cachedWishRewardNextAvailableTicks = 0L;
        private static bool wishRewardCooldownLoaded = false;
        private static bool wishRewardPoolInitialized = false;
        private static float lastWishRewardPoolInitAttemptRealtime = -999f;
        private static bool wishRewardPoolWarmupInProgress = false;
        private static readonly Dictionary<int, WishRewardCandidate> wishRewardCandidatesByTypeId =
            new Dictionary<int, WishRewardCandidate>();
        private static readonly Dictionary<int, List<int>> wishRewardQualityBuckets =
            new Dictionary<int, List<int>>();
        private static readonly Dictionary<string, WishRewardCategoryDefinition> wishRewardCategoriesById =
            new Dictionary<string, WishRewardCategoryDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<int>> wishRewardCategoryCandidateIds =
            new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, WishRewardItemDefinition> wishRewardCustomItemsByTypeId =
            new Dictionary<int, WishRewardItemDefinition>();

        private sealed class WishRewardCategoryDefinition
        {
            public string categoryId;
            public string[] zhAliases;
            public string[] enAliases;
            public int[] preferredQualities;
            public float qualityBiasMultiplier;
            public float itemBiasMultiplier;
        }

        private sealed class WishRewardItemDefinition
        {
            public int typeId;
            public string categoryId;
            public string displayNameCN;
            public string displayNameEN;
            public string[] zhAliases;
            public string[] enAliases;
            public float itemBiasMultiplier;
            public bool enabledInWishRewardPool;
        }

        private sealed class WishRewardCandidate
        {
            public int typeId;
            public int quality;
            public string displayName;
            public readonly HashSet<string> categoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class WishRewardMatchResult
        {
            public readonly HashSet<string> matchedCategoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<int> matchedItemTypeIds = new HashSet<int>();

            public bool HasAnyMatch
            {
                get { return matchedCategoryIds.Count > 0 || matchedItemTypeIds.Count > 0; }
            }
        }

        private sealed class WishRewardPoolBuildContext
        {
            public readonly Dictionary<int, WishRewardCandidate> candidatesByTypeId =
                new Dictionary<int, WishRewardCandidate>();
            public readonly Dictionary<int, List<int>> qualityBuckets =
                new Dictionary<int, List<int>>();
            public readonly Dictionary<string, WishRewardCategoryDefinition> categoriesById =
                new Dictionary<string, WishRewardCategoryDefinition>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, HashSet<int>> categoryCandidateIds =
                new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<int, WishRewardItemDefinition> customItemsByTypeId =
                new Dictionary<int, WishRewardItemDefinition>();
        }

        private static readonly WishRewardCategoryDefinition[] WishRewardCategories =
        {
            CreateWishRewardCategory(
                "weapon",
                1.2f,
                WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER,
                new int[] { 4, 5, 6, 7, 8 },
                new string[] { "武器", "战斗装备", "输出" },
                new string[] { "weapon", "weapons", "combat gear" }),
            CreateWishRewardCategory(
                "gun",
                1.35f,
                WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER,
                new int[] { 4, 5, 6, 7, 8 },
                new string[] { "枪", "枪械", "手枪", "步枪", "冲锋枪", "狙", "狙击", "霰弹", "炮", "铳", "火力" },
                new string[] { "gun", "guns", "rifle", "smg", "sniper", "shotgun", "pistol", "cannon" }),
            CreateWishRewardCategory(
                "melee",
                1.3f,
                WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER,
                new int[] { 4, 5, 6, 7, 8 },
                new string[] { "近战", "刀", "剑", "大剑", "戟", "斩击" },
                new string[] { "melee", "sword", "blade", "halberd" }),
            CreateWishRewardCategory(
                "armor",
                1.25f,
                WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER,
                new int[] { 3, 4, 5, 6, 7 },
                new string[] { "护甲", "防具", "盔甲", "护具", "甲" },
                new string[] { "armor", "armour", "protective gear" }),
            CreateWishRewardCategory(
                "helmet",
                1.25f,
                WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER,
                new int[] { 3, 4, 5, 6, 7 },
                new string[] { "头盔", "头甲", "盔", "冠" },
                new string[] { "helmet", "helm", "crown" }),
            CreateWishRewardCategory(
                "totem",
                1.35f,
                WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER,
                new int[] { 5, 6, 7, 8 },
                new string[] { "图腾", "圣物", "遗物", "护符", "鳞片" },
                new string[] { "totem", "relic", "artifact", "scale" }),
            CreateWishRewardCategory(
                "gift",
                1.15f,
                WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER,
                new int[] { 2, 3, 4, 5, 6 },
                new string[] { "礼物", "礼品", "纪念", "蛋糕", "钻石", "戒指", "涂鸦", "勋章" },
                new string[] { "gift", "present", "cake", "diamond", "ring", "drawing", "medal" }),
            CreateWishRewardCategory(
                "healing",
                1.2f,
                WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER,
                new int[] { 2, 3, 4, 5 },
                new string[] { "药", "治疗", "回血", "恢复", "喷剂", "滴剂", "护身符", "平安" },
                new string[] { "heal", "healing", "recovery", "drops", "spray", "charm" }),
            CreateWishRewardCategory(
                "flag",
                1.18f,
                WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER,
                new int[] { 3, 4, 5 },
                new string[] { "营旗", "旗", "战旗", "阵营", "派系" },
                new string[] { "flag", "banner", "faction" }),
            CreateWishRewardCategory(
                "summon",
                1.25f,
                WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER,
                new int[] { 4, 5, 6 },
                new string[] { "召唤", "刷怪", "引怪", "boss", "首领", "烟雾弹", "引爆器", "响哨", "烽火" },
                new string[] { "summon", "spawn", "boss", "smoke", "detonator", "whistle", "beacon" }),
            CreateWishRewardCategory(
                "fortification",
                1.2f,
                WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER,
                new int[] { 3, 4, 5, 6 },
                new string[] { "工事", "掩体", "路障", "铁丝网", "防线", "维修", "修理" },
                new string[] { "fortification", "cover", "roadblock", "barbed wire", "repair", "defense" }),
            CreateWishRewardCategory(
                "travel",
                1.1f,
                WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER,
                new int[] { 1, 2, 3, 4 },
                new string[] { "船票", "门票", "快递", "日志", "百科", "扫箱", "令牌", "通行" },
                new string[] { "ticket", "courier", "delivery", "journal", "encyclopedia", "sweep", "token" })
        };

        private static readonly WishRewardItemDefinition[] WishRewardCustomItems =
        {
            CreateWishRewardItem(BossRushItemIds.BossRushTicket, "travel", "BossRush 船票", "Boss Rush Ticket", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "船票", "门票", "bossrush船票", "bossrush票" },
                new string[] { "boss rush ticket", "bossrush ticket", "ticket" }),
            CreateWishRewardItem(BossRushItemIds.BirthdayCake, "gift", "生日蛋糕", "Birthday Cake", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "蛋糕", "生日蛋糕" },
                new string[] { "birthday cake", "cake" }),
            CreateWishRewardItem(DragonDescendantConfig.DRAGON_HELM_TYPE_ID, "helmet", "赤龙首", "Dragon Helm", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "赤龙首", "龙盔", "龙头盔" },
                new string[] { "dragon helm", "dragon helmet" }),
            CreateWishRewardItem(DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID, "armor", "焰鳞甲", "Dragon Armor", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "焰鳞甲", "龙甲", "龙护甲" },
                new string[] { "dragon armor", "dragon armour" }),
            CreateWishRewardItem(DragonBreathConfig.WEAPON_TYPE_ID, "gun", "龙息", "Dragon Breath", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "龙息", "龙息枪", "龙枪" },
                new string[] { "dragon breath" }),
            CreateWishRewardItem(BossRushItemIds.AdventureJournal, "travel", "冒险家日志", "Adventure Journal", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "冒险家日志", "日志", "百科", "图鉴" },
                new string[] { "adventure journal", "journal", "encyclopedia", "wiki book" }),
            CreateWishRewardItem(AwenCourierTokenConfig.TYPE_ID, "travel", "阿稳快递牌", "Awen Courier Token", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "阿稳快递牌", "快递牌", "快递令牌", "寄存牌" },
                new string[] { "awen courier token", "courier token", "delivery token" }),
            CreateWishRewardItem(FlightConfig.TotemTypeIdBase, "totem", "腾云驾雾 I", "Cloud Soar I", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "腾云驾雾", "飞行图腾", "飞天图腾", "飞行" },
                new string[] { "cloud soar", "flight totem", "flying totem" }),
            CreateWishRewardItem(DragonKingConfig.DRAGON_KING_HELM_TYPE_ID, "helmet", "龙王之冕", "Dragon King Helm", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "龙王之冕", "龙王头盔", "龙王冠" },
                new string[] { "dragon king helm", "dragon king helmet" }),
            CreateWishRewardItem(DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID, "armor", "龙王鳞铠", "Dragon King Armor", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "龙王鳞铠", "龙王甲", "龙王护甲" },
                new string[] { "dragon king armor", "dragon king armour" }),
            CreateWishRewardItem(ReverseScaleConfig.TotemTypeId, "totem", "逆鳞", "Reverse Scale", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "逆鳞", "逆鳞图腾" },
                new string[] { "reverse scale" }),
            CreateWishRewardItem(ColdQuenchFluidConfig.TYPE_ID, "gift", "冷淬液", "Cold Quench Fluid", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "冷淬液", "冷萃液", "冷却液", "重铸液" },
                new string[] { "cold quench fluid", "quench fluid" }),
            CreateWishRewardItem(BrickStoneConfig.TYPE_ID, "gift", "砖石", "Brick Stone", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "砖石", "假钻石" },
                new string[] { "brick stone", "fake diamond" }),
            CreateWishRewardItem(DingdangDrawingConfig.TYPE_ID, "gift", "叮当涂鸦", "Dingdang's Doodle", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "叮当涂鸦", "涂鸦", "叮当画" },
                new string[] { "dingdang doodle", "doodle" }),
            CreateWishRewardItem(DiamondConfig.TYPE_ID, "gift", "钻石", "Diamond", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "钻石", "真钻石" },
                new string[] { "diamond" }),
            CreateWishRewardItem(AchievementMedalConfig.TYPE_ID, "gift", "成就勋章", "Achievement Medal", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "成就勋章", "勋章", "奖章" },
                new string[] { "achievement medal", "medal" }),
            CreateWishRewardItem(WildHornConfig.TYPE_ID, "gift", "荒野号角", "Wild Horn", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "荒野号角", "号角", "狼号角", "坐骑号角" },
                new string[] { "wild horn", "horn" }),
            CreateWishRewardItem(FactionFlagConfig.RANDOM_FLAG_TYPE_ID, "flag", FactionFlagConfig.RANDOM_FLAG_NAME_CN, FactionFlagConfig.RANDOM_FLAG_NAME_EN, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "随机营旗", "随机旗" },
                new string[] { "random faction flag", "random flag" }),
            CreateWishRewardItem(FactionFlagConfig.SCAV_FLAG_TYPE_ID, "flag", FactionFlagConfig.SCAV_FLAG_NAME_CN, FactionFlagConfig.SCAV_FLAG_NAME_EN, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "拾荒者营旗", "拾荒旗", "scav旗" },
                new string[] { "scav faction flag", "scav flag" }),
            CreateWishRewardItem(FactionFlagConfig.USEC_FLAG_TYPE_ID, "flag", FactionFlagConfig.USEC_FLAG_NAME_CN, FactionFlagConfig.USEC_FLAG_NAME_EN, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "usec营旗", "usec旗" },
                new string[] { "usec faction flag", "usec flag" }),
            CreateWishRewardItem(FactionFlagConfig.BEAR_FLAG_TYPE_ID, "flag", FactionFlagConfig.BEAR_FLAG_NAME_CN, FactionFlagConfig.BEAR_FLAG_NAME_EN, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "bear营旗", "bear旗" },
                new string[] { "bear faction flag", "bear flag" }),
            CreateWishRewardItem(FactionFlagConfig.LAB_FLAG_TYPE_ID, "flag", FactionFlagConfig.LAB_FLAG_NAME_CN, FactionFlagConfig.LAB_FLAG_NAME_EN, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "实验室营旗", "实验室旗", "lab旗" },
                new string[] { "lab faction flag", "lab flag" }),
            CreateWishRewardItem(FactionFlagConfig.WOLF_FLAG_TYPE_ID, "flag", FactionFlagConfig.WOLF_FLAG_NAME_CN, FactionFlagConfig.WOLF_FLAG_NAME_EN, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "狼群营旗", "狼旗" },
                new string[] { "wolf faction flag", "wolf flag" }),
            CreateWishRewardItem(FactionFlagConfig.PLAYER_FLAG_TYPE_ID, "flag", FactionFlagConfig.PLAYER_FLAG_NAME_CN, FactionFlagConfig.PLAYER_FLAG_NAME_EN, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "爷的营旗", "独狼旗", "玩家旗" },
                new string[] { "lone wolf flag", "player flag" }),
            CreateWishRewardItem(RespawnItemConfig.TAUNT_SMOKE_TYPE_ID, "summon", RespawnItemConfig.TAUNT_SMOKE_NAME_CN, RespawnItemConfig.TAUNT_SMOKE_NAME_EN, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "挑衅烟雾弹", "烟雾弹", "挑衅弹" },
                new string[] { "taunt smoke", "smoke" }),
            CreateWishRewardItem(RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID, "summon", RespawnItemConfig.CHAOS_DETONATOR_NAME_CN, RespawnItemConfig.CHAOS_DETONATOR_NAME_EN, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "混沌引爆器", "引爆器", "混沌装置" },
                new string[] { "chaos detonator", "detonator" }),
            CreateWishRewardItem(DiamondRingConfig.TYPE_ID, "gift", "钻石戒指", "Diamond Ring", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "钻石戒指", "戒指", "婚戒" },
                new string[] { "diamond ring", "ring", "wedding ring" }),
            CreateWishRewardItem(CalmingDropsConfig.TYPE_ID, "healing", "安神滴剂", "Calming Drops", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "安神滴剂", "滴剂", "药滴", "安神药" },
                new string[] { "calming drops", "drops" }),
            CreateWishRewardItem(PeaceCharmConfig.TYPE_ID, "healing", "平安护身符", "Peace Charm", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "平安护身符", "护身符", "护符" },
                new string[] { "peace charm", "charm" }),
            CreateWishRewardItem(RespawnItemConfig.BOSSCALL_WHISTLE_TYPE_ID, "summon", RespawnItemConfig.BOSSCALL_WHISTLE_NAME_CN, RespawnItemConfig.BOSSCALL_WHISTLE_NAME_EN, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "猎王响哨", "响哨", "boss哨", "首领哨" },
                new string[] { "bosscall whistle", "boss whistle", "whistle" }),
            CreateWishRewardItem(RespawnItemConfig.ALL_KINGS_BANNER_TYPE_ID, "summon", RespawnItemConfig.ALL_KINGS_BANNER_NAME_CN, RespawnItemConfig.ALL_KINGS_BANNER_NAME_EN, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "血狩烽火", "烽火", "血猎烽火", "引战旗" },
                new string[] { "bloodhunt beacon", "beacon" }),
            CreateWishRewardItem(FenHuangHalberdIds.WeaponTypeId, "melee", "焚皇断界戟", "Inferno Emperor's Realm-Breaking Halberd", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "焚皇断界戟", "断界戟", "焚皇戟" },
                new string[] { "realm-breaking halberd", "fenhuang halberd", "halberd" }),
            CreateWishRewardItem(DragonKingBossGunConfig.WeaponTypeId, "gun", DragonKingBossGunConfig.WeaponNameCN, DragonKingBossGunConfig.WeaponNameEN, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "焚天龙铳", "龙皇铳", "龙铳" },
                new string[] { "skyburn dragon cannon", "dragon king gun", "dragon cannon" }),
            CreateWishRewardItem(BloodhuntTransponderConfig.TYPE_ID, "travel", "血猎收发器", "Bloodhunt Transponder", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "血猎收发器", "收发器", "血猎器" },
                new string[] { "bloodhunt transponder", "transponder" }),
            CreateWishRewardItem(FoldableCoverPackConfig.TYPE_ID, "fortification", "折叠掩体包", "Foldable Cover Pack", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "折叠掩体包", "掩体包", "折叠掩体" },
                new string[] { "foldable cover pack", "cover pack" }),
            CreateWishRewardItem(ReinforcedRoadblockPackConfig.TYPE_ID, "fortification", "加固路障包", "Reinforced Roadblock Pack", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "加固路障包", "路障包", "加固路障" },
                new string[] { "reinforced roadblock pack", "roadblock pack" }),
            CreateWishRewardItem(BarbedWirePackConfig.TYPE_ID, "fortification", "阻滞铁丝网包", "Barbed Wire Pack", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "阻滞铁丝网包", "铁丝网包", "铁丝网" },
                new string[] { "barbed wire pack", "barbed wire" }),
            CreateWishRewardItem(EmergencyRepairSprayConfig.TYPE_ID, "healing", "应急维修喷剂", "Emergency Repair Spray", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "应急维修喷剂", "维修喷剂", "修理喷剂" },
                new string[] { "emergency repair spray", "repair spray" }),
            CreateWishRewardItem(FrostmourneIds.WeaponTypeId, "melee", "霜之哀伤", "Frostmourne", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "霜之哀伤", "霜伤", "冰剑" },
                new string[] { "frostmourne" }),
            CreateWishRewardItem(AwenLootSweepTokenConfig.TYPE_ID, "travel", "阿稳扫箱令", "Awen Loot Sweep Token", WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER, true,
                new string[] { "阿稳扫箱令", "扫箱令", "扫箱牌" },
                new string[] { "awen loot sweep token", "loot sweep token", "sweep token" })
        };

        [Serializable]
        private sealed class FeishuAppConfig
        {
            public string appId;
            public string appSecret;
            public string appToken;
            public string tableId;

            public bool IsValid()
            {
                return !string.IsNullOrEmpty(appId)
                    && !string.IsNullOrEmpty(appSecret)
                    && !string.IsNullOrEmpty(appToken)
                    && !string.IsNullOrEmpty(tableId);
            }
        }

        static WishFountainService()
        {
            try
            {
                SavesSystem.OnSetFile += OnWishRewardSaveFileChanged;
            }
            catch
            {
            }
        }

        private static WishRewardCategoryDefinition CreateWishRewardCategory(
            string categoryId,
            float qualityBiasMultiplier,
            float itemBiasMultiplier,
            int[] preferredQualities,
            string[] zhAliases,
            string[] enAliases)
        {
            return new WishRewardCategoryDefinition
            {
                categoryId = categoryId,
                qualityBiasMultiplier = qualityBiasMultiplier,
                itemBiasMultiplier = Mathf.Clamp(itemBiasMultiplier, 1f, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER),
                preferredQualities = preferredQualities ?? new int[0],
                zhAliases = zhAliases ?? new string[0],
                enAliases = enAliases ?? new string[0]
            };
        }

        private static WishRewardItemDefinition CreateWishRewardItem(
            int typeId,
            string categoryId,
            string displayNameCN,
            string displayNameEN,
            float itemBiasMultiplier,
            bool enabledInWishRewardPool,
            string[] zhAliases,
            string[] enAliases)
        {
            return new WishRewardItemDefinition
            {
                typeId = typeId,
                categoryId = categoryId,
                displayNameCN = displayNameCN ?? string.Empty,
                displayNameEN = displayNameEN ?? string.Empty,
                itemBiasMultiplier = Mathf.Clamp(itemBiasMultiplier, 1f, WISH_REWARD_MAX_HARDCODED_ITEM_BIAS_MULTIPLIER),
                enabledInWishRewardPool = enabledInWishRewardPool,
                zhAliases = zhAliases ?? new string[0],
                enAliases = enAliases ?? new string[0]
            };
        }

        // ============================================================================
        // 敏感词黑名单（完整版）
        // ============================================================================

        private static readonly string[] Blacklist = {
            // === 辱骂类（中文）===
            "傻逼", "操你", "去死", "废物", "智障", "脑残", "煞笔", "沙比", "傻比",
            "妈逼", "你妈", "他妈", "尼玛", "你马", "草泥马", "操你妈", "日你",
            "滚蛋", "贱人", "婊子", "狗逼", "狗日", "王八蛋", "混蛋", "杂种",
            "畜生", "白痴", "弱智", "低能", "蠢货", "蠢猪", "猪头",
            "死全家", "全家死", "断子绝孙", "不得好死", "妈的", "他妈的",
            "我操", "卧槽", "牛逼", "妈了个逼", "逼你妈", "屌你", "屁眼",
            "放屁", "狗屎", "吃屎", "人渣", "垃圾人", "下贱", "恶心", "变态",
            "滚出去",

            // === 辱骂类（英文）===
            "fuck", "shit", "bitch", "asshole", "bastard", "cunt", "dick",
            "motherfucker", "nigger", "nigga", "faggot", "retard",
            "slut", "whore", "wanker", "twat", "cock",
            "son of a bitch", "stfu", "gtfo", "kys",

            // === 色情/低俗类 ===
            "做爱", "性交", "约炮", "一夜情", "裸聊", "援交", "卖淫", "嫖娼",
            "小姐服务", "色情", "黄片", "成人视频", "三级片",
            "口交", "肛交", "自慰", "手淫", "高潮", "淫荡", "骚货", "发骚", "露骨",
            "porn", "hentai", "nsfw", "onlyfans",

            // === 广告/引流类 ===
            "代练", "代打", "外挂", "开挂", "辅助软件", "作弊器", "刷钱", "刷币",
            "优惠券", "免费领", "加微信", "私聊", "低价出售", "打折促销",
            "扫码", "二维码", "关注公众号", "抖音号", "快手号", "B站关注",
            "代购", "淘宝", "拼多多", "京东优惠", "点击链接", "注册领取",
            "赌博", "彩票", "下注", "押注", "赢钱", "翻倍", "稳赚",
        };

        /// <summary>联系方式/引流关键词</summary>
        private static readonly string[] ContactPatterns = {
            "微信", "wx:", "加我", "联系",
            "手机", "电话", "tel:", "加群", "私信我",
            "联系方式", "留言",
        };

        /// <summary>需要按完整词匹配的英文关键词</summary>
        private static readonly string[] BlacklistWordPatterns = {
            "damn", "penis", "pussy", "idiot", "moron",
            "bollocks", "piss", "arse", "bloody hell",
            "av", "sex", "nude", "naked", "xxx",
        };

        /// <summary>需要按完整词匹配的联系方式关键词</summary>
        private static readonly string[] ContactWordPatterns = {
            "qq", "wechat", "telegram", "discord", "line",
        };

        // ============================================================================
        // 加密配置文件
        // ============================================================================

        /// <summary>
        /// 加载并解密飞书应用配置（从 Assets/config/wish_config.dat）
        /// </summary>
        public static void LoadFeishuAppConfig()
        {
            if (configLoaded) return;
            configLoaded = true;

            try
            {
                string modDir = GetModDirectory();
                if (string.IsNullOrEmpty(modDir))
                {
                    ModBehaviour.DevLog("[WishFountain] 无法获取 Mod 目录");
                    return;
                }

                string configPath = Path.Combine(modDir, "Assets", "config", CONFIG_FILE_NAME);
                if (!File.Exists(configPath))
                {
                    ModBehaviour.DevLog("[WishFountain] 配置文件不存在: " + configPath);
                    return;
                }

                string[] lines = File.ReadAllLines(configPath);
                if (lines.Length < 2 || lines[0].Trim() != CONFIG_HEADER)
                {
                    ModBehaviour.DevLog("[WishFountain] 配置文件格式无效");
                    return;
                }

                string encryptedBase64 = lines[1].Trim();
                if (string.IsNullOrEmpty(encryptedBase64)
                    || string.Equals(encryptedBase64, UNCONFIGURED_CONFIG_VALUE, StringComparison.Ordinal))
                {
                    ModBehaviour.DevLog("[WishFountain] 飞书应用配置仍为占位状态，请填写真实加密配置");
                    return;
                }

                string decryptedConfig = DecryptString(encryptedBase64);
                if (string.IsNullOrEmpty(decryptedConfig))
                {
                    ModBehaviour.DevLog("[WishFountain] 飞书应用配置解密失败");
                    return;
                }

                cachedFeishuConfig = ParseFeishuAppConfig(decryptedConfig);

                if (cachedFeishuConfig != null && cachedFeishuConfig.IsValid())
                {
                    ModBehaviour.DevLog("[WishFountain] 飞书应用配置加载成功");
                }
                else
                {
                    cachedFeishuConfig = null;
                    ModBehaviour.DevLog("[WishFountain] 飞书应用配置格式无效");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 加载飞书应用配置异常: " + e.Message);
            }
        }

        /// <summary>
        /// 获取当前飞书应用配置（从缓存或配置文件）
        /// </summary>
        private static FeishuAppConfig GetFeishuAppConfig()
        {
            if (!configLoaded) LoadFeishuAppConfig();
            return cachedFeishuConfig;
        }

        /// <summary>
        /// 获取 Mod 目录路径
        /// </summary>
        private static string GetModDirectory()
        {
            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                return Path.GetDirectoryName(assemblyLocation);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取当前 Mod 版本号
        /// </summary>
        private static string GetModVersion()
        {
            if (!string.IsNullOrEmpty(cachedModVersion))
            {
                return cachedModVersion;
            }

            try
            {
                Assembly assembly = typeof(ModBehaviour).Assembly;

                object[] informationalAttrs = assembly.GetCustomAttributes(
                    typeof(AssemblyInformationalVersionAttribute), false);
                if (informationalAttrs != null && informationalAttrs.Length > 0)
                {
                    string informational = ((AssemblyInformationalVersionAttribute)informationalAttrs[0]).InformationalVersion;
                    if (!string.IsNullOrEmpty(informational))
                    {
                        cachedModVersion = NormalizeVersionText(informational);
                        return cachedModVersion;
                    }
                }

                object[] fileVersionAttrs = assembly.GetCustomAttributes(
                    typeof(AssemblyFileVersionAttribute), false);
                if (fileVersionAttrs != null && fileVersionAttrs.Length > 0)
                {
                    string fileVersion = ((AssemblyFileVersionAttribute)fileVersionAttrs[0]).Version;
                    if (!string.IsNullOrEmpty(fileVersion))
                    {
                        cachedModVersion = NormalizeVersionText(fileVersion);
                        return cachedModVersion;
                    }
                }

                Version version = assembly.GetName().Version;
                if (version != null && version.ToString() != "0.0.0.0")
                {
                    cachedModVersion = NormalizeVersionText(version.ToString());
                    return cachedModVersion;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 获取 Mod 版本失败: " + e.Message);
            }

            string latestWikiVersion = GetLatestWikiChangelogVersion();
            if (!string.IsNullOrEmpty(latestWikiVersion))
            {
                cachedModVersion = latestWikiVersion;
                return cachedModVersion;
            }

            cachedModVersion = "unknown";
            return cachedModVersion;
        }

        private static string NormalizeVersionText(string rawVersion)
        {
            if (string.IsNullOrEmpty(rawVersion))
            {
                return null;
            }

            Match match = Regex.Match(rawVersion, @"\d+\.\d+\.\d+");
            if (match.Success)
            {
                return "v" + match.Value;
            }

            return rawVersion.Trim();
        }

        private static string GetLatestWikiChangelogVersion()
        {
            try
            {
                string modDir = GetModDirectory();
                if (string.IsNullOrEmpty(modDir))
                {
                    return null;
                }

                string wikiContentDir = Path.Combine(modDir, "WikiContent");
                if (!Directory.Exists(wikiContentDir))
                {
                    return null;
                }

                string[] changelogFiles = Directory.GetFiles(
                    wikiContentDir,
                    "changelog__v*.md",
                    SearchOption.AllDirectories);

                Version latestVersion = null;
                for (int i = 0; i < changelogFiles.Length; i++)
                {
                    Version version;
                    if (!TryParseVersionFromWikiChangelogFileName(Path.GetFileName(changelogFiles[i]), out version))
                    {
                        continue;
                    }

                    if (latestVersion == null || version.CompareTo(latestVersion) > 0)
                    {
                        latestVersion = version;
                    }
                }

                if (latestVersion != null)
                {
                    return "v" + latestVersion.ToString(3);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 从 WikiContent 回退版本号失败: " + e.Message);
            }

            return null;
        }

        private static bool TryParseVersionFromWikiChangelogFileName(string fileName, out Version version)
        {
            version = null;
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            Match match = Regex.Match(fileName, @"changelog__v(\d+)_(\d+)_(\d+)\.md", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            int major;
            int minor;
            int patch;
            if (!int.TryParse(match.Groups[1].Value, out major)
                || !int.TryParse(match.Groups[2].Value, out minor)
                || !int.TryParse(match.Groups[3].Value, out patch))
            {
                return false;
            }

            version = new Version(major, minor, patch);
            return true;
        }

        /// <summary>
        /// 派生 AES 密钥（PBKDF2）
        /// </summary>
        private static byte[] DeriveKey()
        {
            // 使用 assembly GUID + 固定盐值派生密钥
            string assemblyGuid = "";
            try
            {
                var guidAttrs = typeof(ModBehaviour).Assembly.GetCustomAttributes(
                    typeof(System.Runtime.InteropServices.GuidAttribute), false);
                if (guidAttrs != null && guidAttrs.Length > 0)
                {
                    assemblyGuid = ((System.Runtime.InteropServices.GuidAttribute)guidAttrs[0]).Value;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(assemblyGuid))
            {
                assemblyGuid = "BossRush-StarWish-DefaultKey-2026";
            }

            byte[] passwordBytes = Encoding.UTF8.GetBytes(assemblyGuid);

            using (var deriveBytes = new Rfc2898DeriveBytes(passwordBytes, AES_SALT, PBKDF2_ITERATIONS))
            {
                return deriveBytes.GetBytes(32); // 256-bit key
            }
        }

        /// <summary>
        /// AES 解密字符串
        /// </summary>
        private static string DecryptString(string encryptedBase64)
        {
            try
            {
                byte[] encryptedData = Convert.FromBase64String(encryptedBase64);
                byte[] key = DeriveKey();

                // 前 16 字节是 IV
                if (encryptedData.Length < 17) return null;

                byte[] iv = new byte[16];
                Array.Copy(encryptedData, 0, iv, 0, 16);

                byte[] cipherText = new byte[encryptedData.Length - 16];
                Array.Copy(encryptedData, 16, cipherText, 0, cipherText.Length);

                using (var aes = new RijndaelManaged())
                {
                    aes.KeySize = 256;
                    aes.BlockSize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key;
                    aes.IV = iv;

                    using (var decryptor = aes.CreateDecryptor())
                    {
                        byte[] decrypted = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
                        return Encoding.UTF8.GetString(decrypted);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 解密失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// AES 加密字符串（开发工具）
        /// </summary>
        private static string EncryptConfigString(string plainText)
        {
            try
            {
                byte[] key = DeriveKey();

                using (var aes = new RijndaelManaged())
                {
                    aes.KeySize = 256;
                    aes.BlockSize = 128;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = key;
                    aes.GenerateIV();

                    using (var encryptor = aes.CreateEncryptor())
                    {
                        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                        byte[] cipherText = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                        // IV + CipherText → Base64
                        byte[] result = new byte[aes.IV.Length + cipherText.Length];
                        Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
                        Array.Copy(cipherText, 0, result, aes.IV.Length, cipherText.Length);

                        return Convert.ToBase64String(result);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 加密失败: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 生成飞书应用配置的加密字符串，供写入 wish_config.dat
        /// </summary>
        public static string EncryptFeishuAppConfig(string appId, string appSecret, string appToken, string tableId)
        {
            StringBuilder sb = SimpleJsonHelper.GetBuilder();
            sb.Append('{');
            SimpleJsonHelper.AppendString(sb, "app_id", appId);
            SimpleJsonHelper.AppendString(sb, "app_secret", appSecret);
            SimpleJsonHelper.AppendString(sb, "app_token", appToken);
            SimpleJsonHelper.AppendString(sb, "table_id", tableId, false);
            sb.Append('}');
            return EncryptConfigString(sb.ToString());
        }

        private static FeishuAppConfig ParseFeishuAppConfig(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            FeishuAppConfig config = new FeishuAppConfig();
            config.appId = SimpleJsonHelper.ExtractString(json, "app_id");
            config.appSecret = SimpleJsonHelper.ExtractString(json, "app_secret");
            config.appToken = SimpleJsonHelper.ExtractString(json, "app_token");
            config.tableId = SimpleJsonHelper.ExtractString(json, "table_id");

            if (!config.IsValid())
            {
                return null;
            }

            return config;
        }

        // ============================================================================
        // 第一步：文本标准化
        // ============================================================================

        /// <summary>
        /// 对原始输入文本进行标准化清洗
        /// </summary>
        /// <param name="raw">原始输入文本</param>
        /// <returns>标准化后的文本</returns>
        public static string StandardizeText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            StringBuilder sb = new StringBuilder(raw.Length);

            // 1. 全角转半角（保留中文标点）
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                // 全角ASCII字符范围 0xFF01~0xFF5E → 半角 0x0021~0x007E
                if (c >= (char)0xFF01 && c <= (char)0xFF5E)
                {
                    // 保留中文常用全角标点
                    if (IsChinesePunctuation(c))
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        sb.Append((char)(c - 0xFEE0)); // 全角→半角
                    }
                }
                else if (c == '\u3000') // 全角空格
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(c);
                }
            }

            string text = sb.ToString();

            // 2. \r\n 和 \r 统一为 \n
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");

            // 3. 连续空白折叠（同一行内的多个空格/制表符→单个空格）
            sb.Length = 0;
            bool prevIsSpace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n')
                {
                    sb.Append(c);
                    prevIsSpace = false;
                }
                else if (c == ' ' || c == '\t')
                {
                    if (!prevIsSpace) sb.Append(' ');
                    prevIsSpace = true;
                }
                else
                {
                    sb.Append(c);
                    prevIsSpace = false;
                }
            }
            text = sb.ToString();

            // 4. 连续空行折叠：超过2个\n → 2个\n
            while (text.IndexOf("\n\n\n") >= 0)
            {
                text = text.Replace("\n\n\n", "\n\n");
            }

            // 5. 去除首尾空行和空白
            text = text.Trim();
            while (text.Length > 0 && text[0] == '\n')
            {
                text = text.Substring(1);
            }
            while (text.Length > 0 && text[text.Length - 1] == '\n')
            {
                text = text.Substring(0, text.Length - 1);
            }
            text = text.Trim();

            return text;
        }

        /// <summary>
        /// 检查字符是否为中文常用全角标点（需要保留，不转半角）
        /// </summary>
        private static bool IsChinesePunctuation(char c)
        {
            return c == '，' || c == '。' || c == '；' || c == '：'
                || c == '？' || c == '！' || c == '\u201C' || c == '\u201D'
                || c == '\u2018' || c == '\u2019' || c == '【' || c == '】'
                || c == '（' || c == '）' || c == '—' || c == '…' || c == '、';
        }

        // ============================================================================
        // 第二步：内容校验
        // ============================================================================

        /// <summary>
        /// 校验标准化后的心愿文本
        /// </summary>
        public static bool ValidateWishText(string text, out string errorMsg)
        {
            errorMsg = "";

            // === 规则 1：空白检查 ===
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                errorMsg = L10n.T("请输入有意义的内容哦~", "Please enter meaningful content~");
                return false;
            }

            // === 规则 2：长度下限 ===
            if (text.Length < MIN_CHARS)
            {
                errorMsg = L10n.T(
                    "⚠ 最少输入 " + MIN_CHARS + " 个字符哦",
                    "⚠ At least " + MIN_CHARS + " characters required");
                return false;
            }

            // === 规则 4：有效文字数 ≥ 5 ===
            int effectiveChars = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c) || (c >= 0x4E00 && c <= 0x9FFF))
                {
                    effectiveChars++;
                }
            }
            if (effectiveChars < 5)
            {
                errorMsg = L10n.T("请输入有意义的内容哦~", "Please enter meaningful content~");
                return false;
            }

            // === 规则 5：同字符占比 > 70% ===
            if (!CheckCharDiversity(text, out errorMsg)) return false;

            // === 规则 6：重复行检测 ===
            if (!CheckDuplicateLines(text, out errorMsg)) return false;

            // === 规则 7：链接/联系方式/引流检测 ===
            if (!CheckNoContactInfo(text, out errorMsg)) return false;

            // === 规则 8：敏感词/辱骂/广告 ===
            if (!CheckNoProfanity(text, out errorMsg)) return false;

            return true;
        }

        /// <summary>规则 5：检查字符多样性</summary>
        private static bool CheckCharDiversity(string text, out string errorMsg)
        {
            errorMsg = "";
            Dictionary<char, int> charCount = new Dictionary<char, int>();
            int maxCount = 0;
            int contentLen = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ' || c == '\n') continue;
                contentLen++;

                int count;
                charCount.TryGetValue(c, out count);
                count++;
                charCount[c] = count;
                if (count > maxCount) maxCount = count;
            }

            if (contentLen > 0 && (float)maxCount / contentLen > 0.7f)
            {
                errorMsg = L10n.T("请输入有意义的内容哦~", "Please enter meaningful content~");
                return false;
            }

            return true;
        }

        /// <summary>规则 6：检查重复行</summary>
        private static bool CheckDuplicateLines(string text, out string errorMsg)
        {
            errorMsg = "";
            string[] lines = text.Split('\n');

            HashSet<string> uniqueLines = new HashSet<string>();
            int nonEmptyLines = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string trimLine = lines[i].Trim();
                if (trimLine.Length > 0)
                {
                    nonEmptyLines++;
                    uniqueLines.Add(trimLine);
                }
            }

            if (nonEmptyLines <= 1)
            {
                return true;
            }

            if (nonEmptyLines > 0 && (float)uniqueLines.Count / nonEmptyLines < 0.3f)
            {
                errorMsg = L10n.T("请输入有意义的内容哦~", "Please enter meaningful content~");
                return false;
            }

            return true;
        }

        /// <summary>规则 7：检查链接、联系方式、引流</summary>
        private static bool CheckNoContactInfo(string text, out string errorMsg)
        {
            errorMsg = "";
            string lower = text.ToLowerInvariant();

            // URL 检测
            if (lower.IndexOf("http://") >= 0 || lower.IndexOf("https://") >= 0 || lower.IndexOf("www.") >= 0)
            {
                errorMsg = L10n.T("心愿里不能包含链接或联系方式哦~",
                                  "Wishes cannot contain links or contact info~");
                return false;
            }

            // 二维码引导词
            if (lower.IndexOf("扫码") >= 0 || lower.IndexOf("二维码") >= 0 || lower.IndexOf("qr code") >= 0)
            {
                errorMsg = L10n.T("心愿里不能包含链接或联系方式哦~",
                                  "Wishes cannot contain links or contact info~");
                return false;
            }

            // 联系方式关键词
            for (int i = 0; i < ContactPatterns.Length; i++)
            {
                if (lower.IndexOf(ContactPatterns[i].ToLowerInvariant()) >= 0)
                {
                    errorMsg = L10n.T("心愿里不能包含链接或联系方式哦~",
                                      "Wishes cannot contain links or contact info~");
                    return false;
                }
            }

            for (int i = 0; i < ContactWordPatterns.Length; i++)
            {
                if (ContainsWholeLatinTerm(lower, ContactWordPatterns[i]))
                {
                    errorMsg = L10n.T("心愿里不能包含链接或联系方式哦~",
                                      "Wishes cannot contain links or contact info~");
                    return false;
                }
            }

            // 手机号格式：1开头的11位连续数字
            for (int i = 0; i < text.Length - 10; i++)
            {
                if (text[i] == '1' && char.IsDigit(text[i]))
                {
                    bool allDigit = true;
                    for (int j = 1; j < 11 && i + j < text.Length; j++)
                    {
                        if (!char.IsDigit(text[i + j]))
                        {
                            allDigit = false;
                            break;
                        }
                    }
                    if (allDigit
                        && (i == 0 || !char.IsDigit(text[i - 1]))
                        && (i + 11 >= text.Length || !char.IsDigit(text[i + 11])))
                    {
                        errorMsg = L10n.T("心愿里不能包含链接或联系方式哦~",
                                          "Wishes cannot contain links or contact info~");
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>规则 8：检查敏感词/辱骂/广告</summary>
        private static bool CheckNoProfanity(string text, out string errorMsg)
        {
            errorMsg = "";
            string lower = text.ToLowerInvariant();

            for (int i = 0; i < Blacklist.Length; i++)
            {
                if (lower.IndexOf(Blacklist[i].ToLowerInvariant()) >= 0)
                {
                    errorMsg = L10n.T("内容包含不当用语，请修改后重试",
                                      "Content contains inappropriate language, please revise");
                    return false;
                }
            }

            for (int i = 0; i < BlacklistWordPatterns.Length; i++)
            {
                if (ContainsWholeLatinTerm(lower, BlacklistWordPatterns[i]))
                {
                    errorMsg = L10n.T("内容包含不当用语，请修改后重试",
                                      "Content contains inappropriate language, please revise");
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsWholeLatinTerm(string lowerText, string term)
        {
            return Regex.IsMatch(
                lowerText,
                @"(?<![a-z0-9])" + Regex.Escape(term) + @"(?![a-z0-9])",
                RegexOptions.CultureInvariant);
        }

        // ============================================================================
        // 许愿奖励
        // ============================================================================

        private static void OnWishRewardSaveFileChanged()
        {
            wishRewardCooldownLoaded = false;
            cachedWishRewardNextAvailableTicks = 0L;
        }

        private static void EnsureWishRewardCooldownLoaded()
        {
            if (wishRewardCooldownLoaded)
            {
                return;
            }

            wishRewardCooldownLoaded = true;
            cachedWishRewardNextAvailableTicks = 0L;

            try
            {
                if (SavesSystem.KeyExisits(WISH_REWARD_NEXT_AVAILABLE_SAVE_KEY))
                {
                    cachedWishRewardNextAvailableTicks = SavesSystem.Load<long>(WISH_REWARD_NEXT_AVAILABLE_SAVE_KEY);
                }
            }
            catch (Exception e)
            {
                cachedWishRewardNextAvailableTicks = 0L;
                ModBehaviour.DevLog("[WishFountain] [WARNING] 读取许愿奖励冷却失败: " + e.Message);
            }
        }

        private static void SaveWishRewardNextAvailableUtc(DateTime nextAvailableUtc)
        {
            cachedWishRewardNextAvailableTicks = nextAvailableUtc.Ticks;
            wishRewardCooldownLoaded = true;

            try
            {
                SavesSystem.Save<long>(WISH_REWARD_NEXT_AVAILABLE_SAVE_KEY, cachedWishRewardNextAvailableTicks);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 保存许愿奖励冷却失败: " + e.Message);
            }
        }

        public static bool IsWishRewardReady()
        {
            EnsureWishRewardCooldownLoaded();
            return DateTime.UtcNow.Ticks >= cachedWishRewardNextAvailableTicks;
        }

        public static int GetWishRewardCooldownRemainingSeconds()
        {
            EnsureWishRewardCooldownLoaded();

            long remainingTicks = cachedWishRewardNextAvailableTicks - DateTime.UtcNow.Ticks;
            if (remainingTicks <= 0L)
            {
                return 0;
            }

            return Mathf.CeilToInt((float)TimeSpan.FromTicks(remainingTicks).TotalSeconds);
        }

        internal static void ClearWishRewardCooldownForDevMode()
        {
            cachedWishRewardNextAvailableTicks = 0L;
            wishRewardCooldownLoaded = true;

            try
            {
                SavesSystem.Save<long>(WISH_REWARD_NEXT_AVAILABLE_SAVE_KEY, 0L);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 清除许愿奖励冷却失败: " + e.Message);
            }
        }

        private static string FormatWishRewardCooldownForBubble(int remainingSeconds)
        {
            TimeSpan remain = TimeSpan.FromSeconds(Mathf.Max(0, remainingSeconds));
            StringBuilder sb = new StringBuilder(32);

            if (remain.Hours > 0)
            {
                sb.Append(remain.Hours).Append("h");
            }

            if (remain.Minutes > 0)
            {
                sb.Append(remain.Minutes).Append("m");
            }

            sb.Append(remain.Seconds).Append("s");
            return sb.ToString();
        }

        private static void ShowWishRewardBubble(string text, float duration = 2.4f)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                return;
            }

            try
            {
                Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(
                    text,
                    player.transform,
                    2.5f,
                    false,
                    false,
                    -1f,
                    duration);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 显示许愿奖励气泡失败: " + e.Message);
            }
        }

        private static void ShowWishRewardCooldownBubble()
        {
            int remaining = GetWishRewardCooldownRemainingSeconds();
            string formatted = FormatWishRewardCooldownForBubble(remaining);
            ShowWishRewardBubble(L10n.T(
                "许愿抽奖冷却：" + formatted,
                "Wish Gacha Cooldown: " + formatted));
        }

        private static void ShowWishRewardResultBubble(string rewardDisplayName)
        {
            if (string.IsNullOrEmpty(rewardDisplayName))
            {
                rewardDisplayName = L10n.T("未知奖励", "Unknown reward");
            }

            ShowWishRewardBubble(L10n.T(
                "我许到了一件：" + rewardDisplayName,
                "I wished for: " + rewardDisplayName), 2.8f);
        }

        private static void ShowWishRewardFailureBubble()
        {
            ShowWishRewardBubble(L10n.T(
                "星愿奖励发放失败，请稍后再试",
                "Wish reward delivery failed. Please try again later"), 2.8f);
        }

        internal static void ShowWishCloseReminderBubble()
        {
            if (IsWishRewardReady())
            {
                ShowWishRewardBubble(L10n.T(
                    "你这家伙快去许愿领奖励！！",
                    "Hey you, go make a wish and claim your reward!!"), 2.8f);
                return;
            }

            ShowWishRewardCooldownBubble();
        }

        private static bool HasSpecialWishRewardTag(Item item)
        {
            if (item == null || item.Tags == null)
            {
                return false;
            }

            Duckov.Utilities.Tag specialTag = null;
            try { specialTag = Duckov.Utilities.GameplayDataSettings.Tags.Special; } catch { }
            return specialTag != null && item.Tags.Contains(specialTag);
        }

        private static bool IsWishRewardExplicitlyAllowedCustomItem(
            Dictionary<int, WishRewardItemDefinition> customItemsByTypeId,
            int typeId)
        {
            WishRewardItemDefinition definition;
            return customItemsByTypeId != null
                && customItemsByTypeId.TryGetValue(typeId, out definition)
                && definition != null
                && definition.enabledInWishRewardPool;
        }

        private static void ResetWishRewardPoolCaches()
        {
            wishRewardCandidatesByTypeId.Clear();
            wishRewardQualityBuckets.Clear();
            wishRewardCategoriesById.Clear();
            wishRewardCategoryCandidateIds.Clear();
            wishRewardCustomItemsByTypeId.Clear();
            wishRewardPoolInitialized = false;
        }

        private static bool CanAttemptWishRewardPoolInitialization()
        {
            return Time.realtimeSinceStartup - lastWishRewardPoolInitAttemptRealtime >= WISH_REWARD_POOL_INIT_RETRY_SECONDS;
        }

        private static WishRewardPoolBuildContext CreateWishRewardPoolBuildContext()
        {
            WishRewardPoolBuildContext context = new WishRewardPoolBuildContext();

            for (int i = 0; i < WishRewardCategories.Length; i++)
            {
                WishRewardCategoryDefinition category = WishRewardCategories[i];
                if (category == null || string.IsNullOrEmpty(category.categoryId))
                {
                    continue;
                }

                context.categoriesById[category.categoryId] = category;
                context.categoryCandidateIds[category.categoryId] = new HashSet<int>();
            }

            for (int i = 0; i < WishRewardCustomItems.Length; i++)
            {
                WishRewardItemDefinition itemDefinition = WishRewardCustomItems[i];
                if (itemDefinition == null || itemDefinition.typeId <= 0)
                {
                    continue;
                }

                context.customItemsByTypeId[itemDefinition.typeId] = itemDefinition;
            }

            return context;
        }

        private static void CommitWishRewardPoolBuild(WishRewardPoolBuildContext context)
        {
            ResetWishRewardPoolCaches();
            if (context == null)
            {
                return;
            }

            foreach (KeyValuePair<int, WishRewardCandidate> kvp in context.candidatesByTypeId)
            {
                wishRewardCandidatesByTypeId[kvp.Key] = kvp.Value;
            }

            foreach (KeyValuePair<int, List<int>> kvp in context.qualityBuckets)
            {
                wishRewardQualityBuckets[kvp.Key] = kvp.Value;
            }

            foreach (KeyValuePair<string, WishRewardCategoryDefinition> kvp in context.categoriesById)
            {
                wishRewardCategoriesById[kvp.Key] = kvp.Value;
            }

            foreach (KeyValuePair<string, HashSet<int>> kvp in context.categoryCandidateIds)
            {
                wishRewardCategoryCandidateIds[kvp.Key] = kvp.Value;
            }

            foreach (KeyValuePair<int, WishRewardItemDefinition> kvp in context.customItemsByTypeId)
            {
                wishRewardCustomItemsByTypeId[kvp.Key] = kvp.Value;
            }

            wishRewardPoolInitialized = wishRewardCandidatesByTypeId.Count > 0;
        }

        private static void AddWishRewardCategoryCandidate(WishRewardPoolBuildContext context, string categoryId, int typeId)
        {
            if (context == null || string.IsNullOrEmpty(categoryId) || typeId <= 0)
            {
                return;
            }

            HashSet<int> set;
            if (!context.categoryCandidateIds.TryGetValue(categoryId, out set))
            {
                set = new HashSet<int>();
                context.categoryCandidateIds[categoryId] = set;
            }

            set.Add(typeId);

            WishRewardCandidate candidate;
            if (context.candidatesByTypeId.TryGetValue(typeId, out candidate))
            {
                candidate.categoryIds.Add(categoryId);
            }
        }

        private static void AddWishRewardQualityBucket(WishRewardPoolBuildContext context, int quality, int typeId)
        {
            if (context == null)
            {
                return;
            }

            List<int> bucket;
            if (!context.qualityBuckets.TryGetValue(quality, out bucket))
            {
                bucket = new List<int>();
                context.qualityBuckets[quality] = bucket;
            }

            if (!bucket.Contains(typeId))
            {
                bucket.Add(typeId);
            }
        }

        private static void EnsureWishRewardPoolInitialized()
        {
            if (wishRewardPoolInitialized)
            {
                return;
            }

            if (!CanAttemptWishRewardPoolInitialization())
            {
                return;
            }

            TryBuildWishRewardPoolSynchronously();
        }

        private static bool TryBuildWishRewardPoolSynchronously()
        {
            lastWishRewardPoolInitAttemptRealtime = Time.realtimeSinceStartup;

            try
            {
                WishRewardPoolBuildContext context = CreateWishRewardPoolBuildContext();
                BuildWishRewardPoolSynchronously(context);
                CommitWishRewardPoolBuild(context);
                return wishRewardPoolInitialized;
            }
            catch (Exception e)
            {
                ResetWishRewardPoolCaches();
                ModBehaviour.DevLog("[WishFountain] [WARNING] 同步构建许愿奖励池失败: " + e.Message);
                return false;
            }
        }

        private static IEnumerable<int> EnumerateWishRewardBasePoolCandidateIds()
        {
            Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
            if (tagsData == null || tagsData.AllTags == null || tagsData.AllTags.Count == 0)
            {
                yield break;
            }

            List<Duckov.Utilities.Tag> excludeTags = BuildWishRewardExcludeTags(tagsData);
            HashSet<int> yieldedIds = new HashSet<int>();

            for (int i = 0; i < tagsData.AllTags.Count; i++)
            {
                Duckov.Utilities.Tag requireTag = tagsData.AllTags[i];
                if (requireTag == null || excludeTags.Contains(requireTag))
                {
                    continue;
                }

                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { requireTag };
                filter.excludeTags = excludeTags.ToArray();
                filter.minQuality = WISH_REWARD_MIN_QUALITY;
                filter.maxQuality = 8;

                int[] ids = ItemAssetsCollection.Search(filter);
                if (ids == null)
                {
                    continue;
                }

                for (int j = 0; j < ids.Length; j++)
                {
                    int id = ids[j];
                    if (id <= 0 || LootBlacklistRegistry.Contains(id))
                    {
                        continue;
                    }

                    if (yieldedIds.Add(id))
                    {
                        yield return id;
                    }
                }
            }
        }

        private static void BuildWishRewardPoolSynchronously(WishRewardPoolBuildContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            BuildWishRewardBasePool(context);
            BuildWishRewardCategoryMemberships(context);
            AddWishRewardCustomOverrides(context);

            if (context.candidatesByTypeId.Count <= 0)
            {
                throw new InvalidOperationException("Wish reward pool build produced no candidates.");
            }
        }

        private static IEnumerator BuildWishRewardPoolIncrementally(WishRewardPoolBuildContext context)
        {
            if (context == null)
            {
                yield break;
            }

            int processed = 0;
            foreach (int typeId in EnumerateWishRewardBasePoolCandidateIds())
            {
                TryRegisterWishRewardCandidate(context, typeId, false);
                processed++;

                if (processed % WISH_REWARD_POOL_BUILD_YIELD_INTERVAL == 0)
                {
                    yield return null;
                }
            }

            BuildWishRewardCategoryMemberships(context);
            yield return null;
            AddWishRewardCustomOverrides(context);

            if (context.candidatesByTypeId.Count <= 0)
            {
                throw new InvalidOperationException("Wish reward pool build produced no candidates.");
            }
        }

        private static bool TryAdvanceWishRewardPoolBuildEnumerator(
            IEnumerator enumerator,
            out object current,
            out Exception error)
        {
            current = null;
            error = null;

            if (enumerator == null)
            {
                return false;
            }

            try
            {
                if (!enumerator.MoveNext())
                {
                    return false;
                }

                current = enumerator.Current;
                return true;
            }
            catch (Exception e)
            {
                error = e;
                return false;
            }
        }

        internal static IEnumerator WarmupWishRewardPoolAfterDelay()
        {
            if (wishRewardPoolInitialized || wishRewardPoolWarmupInProgress)
            {
                yield break;
            }

            wishRewardPoolWarmupInProgress = true;
            try
            {
                yield return null;
                yield return null;

                WishRewardPoolBuildContext context = null;
                Exception warmupError = null;

                try
                {
                    context = CreateWishRewardPoolBuildContext();
                }
                catch (Exception e)
                {
                    warmupError = e;
                }

                if (warmupError == null && context != null)
                {
                    IEnumerator incrementalBuilder = BuildWishRewardPoolIncrementally(context);
                    while (warmupError == null)
                    {
                        object currentYield;
                        Exception stepError;
                        if (!TryAdvanceWishRewardPoolBuildEnumerator(incrementalBuilder, out currentYield, out stepError))
                        {
                            warmupError = stepError;
                            break;
                        }

                        yield return currentYield;
                    }
                }

                if (warmupError == null && context != null && !wishRewardPoolInitialized)
                {
                    try
                    {
                        CommitWishRewardPoolBuild(context);
                    }
                    catch (Exception e)
                    {
                        warmupError = e;
                    }
                }

                if (warmupError != null)
                {
                    if (!wishRewardPoolInitialized)
                    {
                        ResetWishRewardPoolCaches();
                    }

                    ModBehaviour.DevLog("[WishFountain] [WARNING] 预热构建许愿奖励池失败: " + warmupError.Message);
                }
            }
            finally
            {
                wishRewardPoolWarmupInProgress = false;
            }
        }

        private static void BuildWishRewardBasePool(WishRewardPoolBuildContext context)
        {
            foreach (int id in EnumerateWishRewardBasePoolCandidateIds())
            {
                TryRegisterWishRewardCandidate(context, id, false);
            }
        }

        private static void AddWishRewardCustomOverrides(WishRewardPoolBuildContext context)
        {
            if (context == null)
            {
                return;
            }

            for (int i = 0; i < WishRewardCustomItems.Length; i++)
            {
                WishRewardItemDefinition definition = WishRewardCustomItems[i];
                if (definition == null || definition.typeId <= 0 || !definition.enabledInWishRewardPool)
                {
                    continue;
                }

                TryRegisterWishRewardCandidate(context, definition.typeId, true);
                AddWishRewardCategoryCandidate(context, definition.categoryId, definition.typeId);
            }
        }

        private static void BuildWishRewardCategoryMemberships(WishRewardPoolBuildContext context)
        {
            if (context == null)
            {
                return;
            }

            Duckov.Utilities.GameplayDataSettings.TagsData tagsData = Duckov.Utilities.GameplayDataSettings.Tags;
            if (tagsData != null)
            {
                RegisterWishRewardTagCategory(context, tagsData, "gun", new string[] { "Gun" });
                RegisterWishRewardTagCategory(context, tagsData, "weapon", new string[] { "Gun", "Weapon", "MeleeWeapon" });
                RegisterWishRewardTagCategory(context, tagsData, "helmet", new string[] { "Helmat", "Helmet" });
                RegisterWishRewardTagCategory(context, tagsData, "armor", new string[] { "Armor" });
                RegisterWishRewardTagCategory(context, tagsData, "travel", new string[] { "Backpack" });
                RegisterWishRewardTagCategory(context, tagsData, "gift", new string[] { "Food", "Special" });
                RegisterWishRewardTagCategory(context, tagsData, "healing", new string[] { "Food", "Medical", "Special" });
            }

            foreach (KeyValuePair<int, WishRewardCandidate> kvp in context.candidatesByTypeId)
            {
                int typeId = kvp.Key;
                WishRewardCandidate candidate = kvp.Value;
                if (candidate == null)
                {
                    continue;
                }

                WishRewardItemDefinition customItem;
                if (context.customItemsByTypeId.TryGetValue(typeId, out customItem))
                {
                    AddWishRewardCategoryCandidate(context, customItem.categoryId, typeId);
                }

                string normalizedName = NormalizeWishRewardText(candidate.displayName);
                if (string.IsNullOrEmpty(normalizedName))
                {
                    continue;
                }

                foreach (KeyValuePair<string, WishRewardCategoryDefinition> categoryKvp in context.categoriesById)
                {
                    WishRewardCategoryDefinition category = categoryKvp.Value;
                    if (category == null)
                    {
                        continue;
                    }

                    if (MatchesAnyWishRewardAlias(normalizedName, category.zhAliases, category.enAliases))
                    {
                        AddWishRewardCategoryCandidate(context, category.categoryId, typeId);
                    }
                }
            }
        }

        private static void RegisterWishRewardTagCategory(
            WishRewardPoolBuildContext context,
            Duckov.Utilities.GameplayDataSettings.TagsData tagsData,
            string categoryId,
            string[] memberNames)
        {
            if (context == null || tagsData == null || string.IsNullOrEmpty(categoryId) || memberNames == null)
            {
                return;
            }

            List<Duckov.Utilities.Tag> excludeTags = BuildWishRewardExcludeTags(tagsData);

            for (int i = 0; i < memberNames.Length; i++)
            {
                Duckov.Utilities.Tag requiredTag = TryGetWishRewardTagByMemberName(tagsData, memberNames[i]);
                if (requiredTag == null)
                {
                    continue;
                }

                ItemFilter filter = default(ItemFilter);
                filter.requireTags = new Duckov.Utilities.Tag[] { requiredTag };
                filter.excludeTags = excludeTags.ToArray();
                filter.minQuality = WISH_REWARD_MIN_QUALITY;
                filter.maxQuality = 8;

                int[] ids = ItemAssetsCollection.Search(filter);
                if (ids == null)
                {
                    continue;
                }

                for (int j = 0; j < ids.Length; j++)
                {
                    int typeId = ids[j];
                    if (!context.candidatesByTypeId.ContainsKey(typeId))
                    {
                        continue;
                    }

                    AddWishRewardCategoryCandidate(context, categoryId, typeId);
                }
            }
        }

        private static Duckov.Utilities.Tag TryGetWishRewardTagByMemberName(
            Duckov.Utilities.GameplayDataSettings.TagsData tagsData,
            string memberName)
        {
            if (tagsData == null || string.IsNullOrEmpty(memberName))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            try
            {
                FieldInfo field = tagsData.GetType().GetField(memberName, flags);
                if (field != null && typeof(Duckov.Utilities.Tag).IsAssignableFrom(field.FieldType))
                {
                    return field.GetValue(tagsData) as Duckov.Utilities.Tag;
                }
            }
            catch
            {
            }

            try
            {
                PropertyInfo property = tagsData.GetType().GetProperty(memberName, flags);
                if (property != null && typeof(Duckov.Utilities.Tag).IsAssignableFrom(property.PropertyType))
                {
                    return property.GetValue(tagsData, null) as Duckov.Utilities.Tag;
                }
            }
            catch
            {
            }

            return null;
        }

        private static void AddWishRewardExcludeTag(List<Duckov.Utilities.Tag> excludeTags, Duckov.Utilities.Tag tag)
        {
            if (excludeTags == null || tag == null || excludeTags.Contains(tag))
            {
                return;
            }

            excludeTags.Add(tag);
        }

        private static Duckov.Utilities.Tag TryFindWishRewardQuestTag(Duckov.Utilities.GameplayDataSettings.TagsData tagsData)
        {
            if (tagsData == null)
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            try
            {
                FieldInfo questField = tagsData.GetType().GetField("Quest", flags);
                if (questField != null && typeof(Duckov.Utilities.Tag).IsAssignableFrom(questField.FieldType))
                {
                    return questField.GetValue(tagsData) as Duckov.Utilities.Tag;
                }
            }
            catch
            {
            }

            try
            {
                PropertyInfo questProperty = tagsData.GetType().GetProperty("Quest", flags);
                if (questProperty != null && typeof(Duckov.Utilities.Tag).IsAssignableFrom(questProperty.PropertyType))
                {
                    return questProperty.GetValue(tagsData, null) as Duckov.Utilities.Tag;
                }
            }
            catch
            {
            }

            try
            {
                if (tagsData.AllTags != null)
                {
                    for (int i = 0; i < tagsData.AllTags.Count; i++)
                    {
                        Duckov.Utilities.Tag tag = tagsData.AllTags[i];
                        if (tag != null && string.Equals(tag.name, "Quest", StringComparison.OrdinalIgnoreCase))
                        {
                            return tag;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static List<Duckov.Utilities.Tag> BuildWishRewardExcludeTags(Duckov.Utilities.GameplayDataSettings.TagsData tagsData)
        {
            List<Duckov.Utilities.Tag> excludeTags = new List<Duckov.Utilities.Tag>();
            if (tagsData == null)
            {
                return excludeTags;
            }

            AddWishRewardExcludeTag(excludeTags, tagsData.Character);
            AddWishRewardExcludeTag(excludeTags, tagsData.DestroyOnLootBox);
            AddWishRewardExcludeTag(excludeTags, tagsData.DontDropOnDeadInSlot);
            AddWishRewardExcludeTag(excludeTags, tagsData.LockInDemoTag);
            AddWishRewardExcludeTag(excludeTags, TryFindWishRewardQuestTag(tagsData));
            return excludeTags;
        }

        private static bool TryRegisterWishRewardCandidate(
            WishRewardPoolBuildContext context,
            int typeId,
            bool allowBlacklistedOverride)
        {
            if (context == null || typeId <= 0)
            {
                return false;
            }

            if (!allowBlacklistedOverride && LootBlacklistRegistry.Contains(typeId))
            {
                return false;
            }

            if (context.candidatesByTypeId.ContainsKey(typeId))
            {
                return true;
            }

            Item temp = null;
            try
            {
                temp = ItemAssetsCollection.InstantiateSync(typeId);
                if (temp == null)
                {
                    return false;
                }

                int quality = 0;
                try { quality = temp.Quality; } catch { quality = 0; }
                if (quality < WISH_REWARD_MIN_QUALITY || quality > 8)
                {
                    return false;
                }

                if (HasSpecialWishRewardTag(temp)
                    && !IsWishRewardExplicitlyAllowedCustomItem(context.customItemsByTypeId, typeId))
                {
                    return false;
                }

                string displayName = GetWishRewardDisplayNameFromItem(context.customItemsByTypeId, typeId, temp);
                if (string.IsNullOrEmpty(displayName))
                {
                    displayName = "Item " + typeId;
                }

                WishRewardCandidate candidate = new WishRewardCandidate
                {
                    typeId = typeId,
                    quality = quality,
                    displayName = displayName
                };

                context.candidatesByTypeId[typeId] = candidate;
                AddWishRewardQualityBucket(context, quality, typeId);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 注册许愿奖励候选失败 typeId=" + typeId + ": " + e.Message);
                return false;
            }
            finally
            {
                try
                {
                    if (temp != null)
                    {
                        temp.DestroyTree();
                    }
                }
                catch
                {
                }
            }
        }

        private static string GetWishRewardDisplayNameFromItem(
            Dictionary<int, WishRewardItemDefinition> customItemsByTypeId,
            int typeId,
            Item item)
        {
            WishRewardItemDefinition customItem;
            if (customItemsByTypeId != null && customItemsByTypeId.TryGetValue(typeId, out customItem))
            {
                return L10n.T(customItem.displayNameCN, customItem.displayNameEN);
            }

            if (item != null)
            {
                try
                {
                    if (!string.IsNullOrEmpty(item.DisplayName))
                    {
                        return item.DisplayName;
                    }
                }
                catch
                {
                }

                try
                {
                    if (!string.IsNullOrEmpty(item.name))
                    {
                        return item.name;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static string GetWishRewardDisplayNameFromItem(int typeId, Item item)
        {
            return GetWishRewardDisplayNameFromItem(wishRewardCustomItemsByTypeId, typeId, item);
        }

        private static string NormalizeWishRewardText(string text)
        {
            text = StandardizeText(text);
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(text.Length + 8);
            bool previousWasSpace = true;

            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToLowerInvariant(text[i]);
                if (char.IsLetterOrDigit(c) || c > 127)
                {
                    sb.Append(c);
                    previousWasSpace = false;
                }
                else
                {
                    if (!previousWasSpace)
                    {
                        sb.Append(' ');
                        previousWasSpace = true;
                    }
                }
            }

            return sb.ToString().Trim();
        }

        private static bool IsWishRewardLatinAlias(string alias)
        {
            if (string.IsNullOrEmpty(alias))
            {
                return false;
            }

            for (int i = 0; i < alias.Length; i++)
            {
                char c = alias[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == ' ')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool ShouldUseWishRewardChineseAlias(string normalizedAlias)
        {
            if (string.IsNullOrEmpty(normalizedAlias))
            {
                return false;
            }

            if (normalizedAlias.Length < 2)
            {
                return false;
            }

            return true;
        }

        private static bool MatchesAnyWishRewardAlias(string normalizedText, string[] zhAliases, string[] enAliases)
        {
            if (string.IsNullOrEmpty(normalizedText))
            {
                return false;
            }

            if (zhAliases != null)
            {
                for (int i = 0; i < zhAliases.Length; i++)
                {
                    string normalizedAlias = NormalizeWishRewardText(zhAliases[i]);
                    if (!ShouldUseWishRewardChineseAlias(normalizedAlias))
                    {
                        continue;
                    }

                    if (normalizedText.Contains(normalizedAlias))
                    {
                        return true;
                    }
                }
            }

            if (enAliases != null)
            {
                string paddedText = " " + normalizedText + " ";
                for (int i = 0; i < enAliases.Length; i++)
                {
                    string alias = NormalizeWishRewardText(enAliases[i]);
                    if (string.IsNullOrEmpty(alias) || !IsWishRewardLatinAlias(alias))
                    {
                        continue;
                    }

                    if (paddedText.Contains(" " + alias + " "))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static WishRewardMatchResult MatchWishRewardKeywords(string standardizedWishText)
        {
            WishRewardMatchResult result = new WishRewardMatchResult();
            string normalizedText = NormalizeWishRewardText(standardizedWishText);
            if (string.IsNullOrEmpty(normalizedText))
            {
                return result;
            }

            foreach (KeyValuePair<string, WishRewardCategoryDefinition> kvp in wishRewardCategoriesById)
            {
                WishRewardCategoryDefinition category = kvp.Value;
                if (category == null)
                {
                    continue;
                }

                if (MatchesAnyWishRewardAlias(normalizedText, category.zhAliases, category.enAliases))
                {
                    result.matchedCategoryIds.Add(category.categoryId);
                }
            }

            foreach (KeyValuePair<int, WishRewardItemDefinition> kvp in wishRewardCustomItemsByTypeId)
            {
                WishRewardItemDefinition itemDefinition = kvp.Value;
                if (itemDefinition == null)
                {
                    continue;
                }

                if (MatchesAnyWishRewardAlias(normalizedText, itemDefinition.zhAliases, itemDefinition.enAliases) ||
                    MatchesAnyWishRewardAlias(normalizedText,
                        new string[] { itemDefinition.displayNameCN },
                        new string[] { itemDefinition.displayNameEN }))
                {
                    result.matchedItemTypeIds.Add(itemDefinition.typeId);
                    if (!string.IsNullOrEmpty(itemDefinition.categoryId))
                    {
                        result.matchedCategoryIds.Add(itemDefinition.categoryId);
                    }
                }
            }

            return result;
        }

        private static string TruncateWishRewardLogValue(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "(empty)";
            }

            if (maxLength <= 0 || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength) + "...";
        }

        private static string FormatWishRewardCategoryMatchesForLog(WishRewardMatchResult match)
        {
            if (match == null || match.matchedCategoryIds.Count <= 0)
            {
                return "(none)";
            }

            List<string> categoryIds = new List<string>(match.matchedCategoryIds);
            categoryIds.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", categoryIds.ToArray());
        }

        private static string FormatWishRewardItemMatchesForLog(WishRewardMatchResult match)
        {
            if (match == null || match.matchedItemTypeIds.Count <= 0)
            {
                return "(none)";
            }

            List<string> itemEntries = new List<string>();
            foreach (int typeId in match.matchedItemTypeIds)
            {
                string displayName = null;

                WishRewardItemDefinition itemDefinition;
                if (wishRewardCustomItemsByTypeId.TryGetValue(typeId, out itemDefinition) && itemDefinition != null)
                {
                    displayName = L10n.T(itemDefinition.displayNameCN, itemDefinition.displayNameEN);
                }

                if (string.IsNullOrEmpty(displayName))
                {
                    WishRewardCandidate candidate;
                    if (wishRewardCandidatesByTypeId.TryGetValue(typeId, out candidate) && candidate != null)
                    {
                        displayName = candidate.displayName;
                    }
                }

                if (string.IsNullOrEmpty(displayName))
                {
                    displayName = "Item " + typeId;
                }

                itemEntries.Add(typeId + ":" + displayName);
            }

            itemEntries.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", itemEntries.ToArray());
        }

        private static void LogWishRewardRoll(
            string wishText,
            WishRewardMatchResult match,
            int rolledQuality,
            int selectedTypeId,
            string rewardDisplayName)
        {
            try
            {
                string normalizedWishText = TruncateWishRewardLogValue(NormalizeWishRewardText(wishText), 120);
                string matchedCategories = FormatWishRewardCategoryMatchesForLog(match);
                string matchedItems = FormatWishRewardItemMatchesForLog(match);
                string selectedReward = string.IsNullOrEmpty(rewardDisplayName)
                    ? "(none)"
                    : TruncateWishRewardLogValue(rewardDisplayName, 80);

                ModBehaviour.DevLog(
                    "[WishFountain] reward roll normalizedWishText=\"" + normalizedWishText +
                    "\" matchedCategories=" + matchedCategories +
                    " matchedItems=" + matchedItems +
                    " rolledQuality=Q" + rolledQuality +
                    " selectedTypeId=" + selectedTypeId +
                    " selectedReward=" + selectedReward);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 记录许愿奖励抽取日志失败: " + e.Message);
            }
        }

        private static float GetWishRewardMaxItemBiasForQuality(int quality)
        {
            if (quality <= 4)
            {
                return WISH_REWARD_ITEM_BIAS_CAP_Q1_TO_Q4;
            }

            if (quality <= 6)
            {
                return WISH_REWARD_ITEM_BIAS_CAP_Q5_TO_Q6;
            }

            if (quality == 7)
            {
                return WISH_REWARD_ITEM_BIAS_CAP_Q7;
            }

            return WISH_REWARD_ITEM_BIAS_CAP_Q8;
        }

        private static int RollWishRewardQuality(WishRewardMatchResult match)
        {
            float[] weights = new float[WishRewardBaseQualityWeights.Length];
            float[] multipliers = new float[WishRewardBaseQualityWeights.Length];

            for (int i = 0; i < WishRewardBaseQualityWeights.Length; i++)
            {
                weights[i] = WishRewardBaseQualityWeights[i];
                multipliers[i] = 1f;
            }

            foreach (string categoryId in match.matchedCategoryIds)
            {
                WishRewardCategoryDefinition category;
                if (!wishRewardCategoriesById.TryGetValue(categoryId, out category) || category == null)
                {
                    continue;
                }

                for (int i = 0; i < category.preferredQualities.Length; i++)
                {
                    int quality = category.preferredQualities[i];
                    if (quality < 1 || quality > weights.Length)
                    {
                        continue;
                    }

                    multipliers[quality - 1] = Mathf.Min(
                        WISH_REWARD_QUALITY_BIAS_CAP,
                        multipliers[quality - 1] * Mathf.Max(1f, category.qualityBiasMultiplier));
                }
            }

            foreach (int typeId in match.matchedItemTypeIds)
            {
                WishRewardCandidate candidate;
                if (!wishRewardCandidatesByTypeId.TryGetValue(typeId, out candidate) || candidate == null)
                {
                    continue;
                }

                int qualityIndex = candidate.quality - 1;
                if (qualityIndex < 0 || qualityIndex >= multipliers.Length)
                {
                    continue;
                }

                multipliers[qualityIndex] = Mathf.Min(
                    WISH_REWARD_QUALITY_BIAS_CAP,
                    multipliers[qualityIndex] * WISH_REWARD_EXACT_ITEM_QUALITY_BIAS);
            }

            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] *= multipliers[i];
            }

            return RollWishRewardWeightedIndex(weights) + 1;
        }

        private static int RollWishRewardWeightedIndex(float[] weights)
        {
            if (weights == null || weights.Length == 0)
            {
                return -1;
            }

            float totalWeight = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] > 0f)
                {
                    totalWeight += weights[i];
                }
            }

            if (totalWeight <= 0f)
            {
                return 0;
            }

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            float cursor = 0f;

            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f)
                {
                    continue;
                }

                cursor += weights[i];
                if (roll <= cursor)
                {
                    return i;
                }
            }

            return weights.Length - 1;
        }

        private static int RollWishRewardItemInQuality(int rolledQuality, WishRewardMatchResult match, out string rewardDisplayName)
        {
            rewardDisplayName = null;

            List<int> bucket;
            if (!wishRewardQualityBuckets.TryGetValue(rolledQuality, out bucket) || bucket == null || bucket.Count <= 0)
            {
                return -1;
            }

            float[] weights = new float[bucket.Count];
            for (int i = 0; i < bucket.Count; i++)
            {
                int typeId = bucket[i];
                WishRewardCandidate candidate;
                if (!wishRewardCandidatesByTypeId.TryGetValue(typeId, out candidate) || candidate == null)
                {
                    weights[i] = 0f;
                    continue;
                }

                float weight = 1f;
                float itemBiasCap = GetWishRewardMaxItemBiasForQuality(candidate.quality);

                foreach (string categoryId in match.matchedCategoryIds)
                {
                    if (!candidate.categoryIds.Contains(categoryId))
                    {
                        continue;
                    }

                    WishRewardCategoryDefinition category;
                    if (!wishRewardCategoriesById.TryGetValue(categoryId, out category) || category == null)
                    {
                        continue;
                    }

                    weight = Mathf.Min(
                        itemBiasCap,
                        weight * Mathf.Max(1f, category.itemBiasMultiplier));
                }

                if (match.matchedItemTypeIds.Contains(typeId))
                {
                    WishRewardItemDefinition itemDefinition;
                    if (wishRewardCustomItemsByTypeId.TryGetValue(typeId, out itemDefinition) && itemDefinition != null)
                    {
                        weight = Mathf.Min(
                            itemBiasCap,
                            weight * Mathf.Max(1f, itemDefinition.itemBiasMultiplier));
                    }
                }

                weights[i] = weight;
            }

            int selectedIndex = RollWishRewardWeightedIndex(weights);
            if (selectedIndex < 0 || selectedIndex >= bucket.Count)
            {
                return -1;
            }

            int selectedTypeId = bucket[selectedIndex];
            WishRewardCandidate selectedCandidate;
            if (wishRewardCandidatesByTypeId.TryGetValue(selectedTypeId, out selectedCandidate) && selectedCandidate != null)
            {
                rewardDisplayName = selectedCandidate.displayName;
            }

            return selectedTypeId;
        }

        private static int RollWishRewardTypeId(string wishText, out string rewardDisplayName)
        {
            rewardDisplayName = null;

            EnsureWishRewardPoolInitialized();
            if (!wishRewardPoolInitialized)
            {
                return -1;
            }

            WishRewardMatchResult match = MatchWishRewardKeywords(wishText);
            int rolledQuality = RollWishRewardQuality(match);
            int selectedTypeId = RollWishRewardItemInQuality(rolledQuality, match, out rewardDisplayName);
            LogWishRewardRoll(wishText, match, rolledQuality, selectedTypeId, rewardDisplayName);
            return selectedTypeId;
        }

        private static int PickWishRewardAnimationCandidate(
            List<int> primaryPool,
            List<int> secondaryPool,
            List<int> tertiaryPool,
            List<int> existingSequence,
            int winningTypeId)
        {
            List<int>[] pools = new List<int>[] { primaryPool, secondaryPool, tertiaryPool };
            int lastTypeId = existingSequence.Count > 0 ? existingSequence[existingSequence.Count - 1] : -1;

            for (int poolIndex = 0; poolIndex < pools.Length; poolIndex++)
            {
                List<int> pool = pools[poolIndex];
                if (pool == null || pool.Count <= 0)
                {
                    continue;
                }

                int fallbackTypeId = -1;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    int candidateTypeId = pool[UnityEngine.Random.Range(0, pool.Count)];
                    if (candidateTypeId <= 0 || candidateTypeId == winningTypeId)
                    {
                        continue;
                    }

                    if (fallbackTypeId <= 0)
                    {
                        fallbackTypeId = candidateTypeId;
                    }

                    if (candidateTypeId != lastTypeId)
                    {
                        return candidateTypeId;
                    }
                }

                if (fallbackTypeId > 0)
                {
                    return fallbackTypeId;
                }
            }

            return winningTypeId;
        }

        private static List<int> BuildWishRewardAnimationSequence(int rewardTypeId, out int outWinnerIndex)
        {
            const int sequenceLength = 45;
            const int winnerIndex = 32;
            outWinnerIndex = winnerIndex;

            EnsureWishRewardPoolInitialized();

            List<int> lowQuality = new List<int>();
            List<int> midQuality = new List<int>();
            List<int> highQuality = new List<int>();

            foreach (KeyValuePair<int, WishRewardCandidate> kvp in wishRewardCandidatesByTypeId)
            {
                WishRewardCandidate candidate = kvp.Value;
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.typeId == rewardTypeId)
                {
                    continue;
                }

                if (candidate.quality <= 3)
                {
                    lowQuality.Add(candidate.typeId);
                }
                else if (candidate.quality <= 5)
                {
                    midQuality.Add(candidate.typeId);
                }
                else
                {
                    highQuality.Add(candidate.typeId);
                }
            }

            List<int> sequence = new List<int>(sequenceLength);
            for (int i = 0; i < sequenceLength; i++)
            {
                if (i == winnerIndex)
                {
                    sequence.Add(rewardTypeId);
                    continue;
                }

                int pickedTypeId;
                if (i % 7 == 0)
                {
                    pickedTypeId = PickWishRewardAnimationCandidate(highQuality, midQuality, lowQuality, sequence, rewardTypeId);
                }
                else if (i % 3 == 0)
                {
                    pickedTypeId = PickWishRewardAnimationCandidate(midQuality, lowQuality, highQuality, sequence, rewardTypeId);
                }
                else
                {
                    pickedTypeId = PickWishRewardAnimationCandidate(lowQuality, midQuality, highQuality, sequence, rewardTypeId);
                }

                sequence.Add(pickedTypeId);
            }

            return sequence;
        }

        private static bool TryGiveWishRewardItem(int typeId)
        {
            Item item = null;
            try
            {
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null)
                {
                    return false;
                }

                bool sent = false;
                try
                {
                    sent = ItemUtilities.SendToPlayerCharacterInventory(item, false);
                }
                catch
                {
                }

                if (!sent)
                {
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (player != null)
                    {
                        item.Drop(player.transform.position + Vector3.up * 0.3f, true, UnityEngine.Random.insideUnitSphere.normalized, 20f);
                        sent = true;
                    }
                }

                if (!sent)
                {
                    try { item.DestroyTree(); } catch { }
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] [WARNING] 发放许愿奖励失败: " + e.Message);
                try
                {
                    if (item != null)
                    {
                        item.DestroyTree();
                    }
                }
                catch
                {
                }
                return false;
            }
        }

        private static void OnWishRewardAnimationFinished(int rewardTypeId, string rewardDisplayName)
        {
            if (!TryGiveWishRewardItem(rewardTypeId))
            {
                ShowWishRewardFailureBubble();
                return;
            }

            SaveWishRewardNextAvailableUtc(DateTime.UtcNow.AddHours(WISH_REWARD_COOLDOWN_HOURS));
            ShowWishRewardResultBubble(rewardDisplayName);
        }

        internal static void TryStartWishRewardAnimationAfterSuccessfulSend(string wishText)
        {
            if (!IsWishRewardReady())
            {
                ShowWishRewardCooldownBubble();
                return;
            }

            string rewardDisplayName;
            int rewardTypeId = RollWishRewardTypeId(wishText, out rewardDisplayName);
            if (rewardTypeId <= 0 || string.IsNullOrEmpty(rewardDisplayName))
            {
                ShowWishRewardFailureBubble();
                return;
            }

            int animWinnerIndex;
            List<int> animSequence = BuildWishRewardAnimationSequence(rewardTypeId, out animWinnerIndex);
            WishFountainRewardAnimationView.PlayRuntime(
                rewardTypeId,
                rewardDisplayName,
                animSequence,
                animWinnerIndex,
                OnWishRewardAnimationFinished);
        }

        // ============================================================================
        // 发送服务
        // ============================================================================

        /// <summary>检查是否在冷却中</summary>
        public static bool IsInCooldown()
        {
            return Time.realtimeSinceStartup - lastSendTime < SEND_COOLDOWN;
        }

        /// <summary>获取剩余冷却秒数</summary>
        public static int GetCooldownRemaining()
        {
            float remaining = SEND_COOLDOWN - (Time.realtimeSinceStartup - lastSendTime);
            return remaining > 0 ? Mathf.CeilToInt(remaining) : 0;
        }

        /// <summary>
        /// 获取 tenant access token
        /// </summary>
        private static IEnumerator GetTenantAccessToken(FeishuAppConfig config, Action<string> onSuccess, Action<string> onFailure)
        {
            if (config == null || !config.IsValid())
            {
                if (onFailure != null)
                {
                    onFailure("invalid_config");
                }
                yield break;
            }

            if (!string.IsNullOrEmpty(cachedTenantAccessToken)
                && DateTime.UtcNow < cachedTenantAccessTokenExpiryUtc)
            {
                if (onSuccess != null)
                {
                    onSuccess(cachedTenantAccessToken);
                }
                yield break;
            }

            StringBuilder authBuilder = SimpleJsonHelper.GetBuilder();
            authBuilder.Append('{');
            SimpleJsonHelper.AppendString(authBuilder, "app_id", config.appId);
            SimpleJsonHelper.AppendString(authBuilder, "app_secret", config.appSecret, false);
            authBuilder.Append('}');

            byte[] authBody = Encoding.UTF8.GetBytes(authBuilder.ToString());

            using (UnityWebRequest authRequest = new UnityWebRequest(FEISHU_TENANT_TOKEN_URL, "POST"))
            {
                authRequest.uploadHandler = new UploadHandlerRaw(authBody);
                authRequest.downloadHandler = new DownloadHandlerBuffer();
                authRequest.SetRequestHeader("Content-Type", "application/json");
                authRequest.timeout = REQUEST_TIMEOUT;

                yield return authRequest.SendWebRequest();

                if (HasRequestTransportError(authRequest))
                {
                    if (onFailure != null)
                    {
                        onFailure(authRequest.error ?? "auth_transport_error");
                    }
                    yield break;
                }

                string authResponse = authRequest.downloadHandler != null ? authRequest.downloadHandler.text : "";
                int code = SimpleJsonHelper.ExtractInt(authResponse, "code");
                if (code != 0)
                {
                    if (onFailure != null)
                    {
                        onFailure(GetFeishuErrorMessage(authResponse, "auth_api_error"));
                    }
                    yield break;
                }

                string tenantAccessToken = SimpleJsonHelper.ExtractString(authResponse, "tenant_access_token");
                int expireSeconds = SimpleJsonHelper.ExtractInt(authResponse, "expire");
                if (string.IsNullOrEmpty(tenantAccessToken))
                {
                    if (onFailure != null)
                    {
                        onFailure("missing_tenant_access_token");
                    }
                    yield break;
                }

                cachedTenantAccessToken = tenantAccessToken;
                int cacheSeconds = expireSeconds > TOKEN_REFRESH_BUFFER_SECONDS
                    ? expireSeconds - TOKEN_REFRESH_BUFFER_SECONDS
                    : Mathf.Max(30, expireSeconds);
                cachedTenantAccessTokenExpiryUtc = DateTime.UtcNow.AddSeconds(cacheSeconds);

                if (onSuccess != null)
                {
                    onSuccess(tenantAccessToken);
                }
            }
        }

        private static string BuildFeishuRecordJson(string playerName, string wishText, string sceneName, string language, string modVersion)
        {
            StringBuilder sb = SimpleJsonHelper.GetBuilder();
            sb.Append("{\"fields\":{");
            SimpleJsonHelper.AppendString(sb, "时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            SimpleJsonHelper.AppendString(sb, "版本", modVersion);
            SimpleJsonHelper.AppendString(sb, "玩家名", playerName);
            SimpleJsonHelper.AppendString(sb, "心愿内容", wishText);
            SimpleJsonHelper.AppendString(sb, "场景", sceneName);
            SimpleJsonHelper.AppendString(sb, "语言", language, false);
            sb.Append("}}");
            return sb.ToString();
        }

        private static string GetFeishuRecordsUrl(FeishuAppConfig config)
        {
            return "https://open.feishu.cn/open-apis/bitable/v1/apps/"
                + config.appToken + "/tables/" + config.tableId + "/records";
        }

        private static string GetFeishuErrorMessage(string responseText, string fallback)
        {
            string msg = SimpleJsonHelper.ExtractString(responseText, "msg");
            if (!string.IsNullOrEmpty(msg))
            {
                return msg;
            }

            string message = SimpleJsonHelper.ExtractString(responseText, "message");
            if (!string.IsNullOrEmpty(message))
            {
                return message;
            }

            return fallback;
        }

        private static void InvalidateCachedTenantAccessToken()
        {
            cachedTenantAccessToken = null;
            cachedTenantAccessTokenExpiryUtc = DateTime.MinValue;
        }

        private static bool ShouldRetryWithFreshTenantToken(UnityWebRequest request, string responseText)
        {
            long responseCode = 0;
            try
            {
                responseCode = request.responseCode;
            }
            catch { }

            if (responseCode == 401 || responseCode == 403)
            {
                return true;
            }

            string lower = string.IsNullOrEmpty(responseText) ? "" : responseText.ToLowerInvariant();
            return lower.IndexOf("tenant_access_token", StringComparison.Ordinal) >= 0
                || lower.IndexOf("access token", StringComparison.Ordinal) >= 0
                || lower.IndexOf("token expired", StringComparison.Ordinal) >= 0
                || lower.IndexOf("unauthorized", StringComparison.Ordinal) >= 0
                || lower.IndexOf("99991661", StringComparison.Ordinal) >= 0
                || lower.IndexOf("99991663", StringComparison.Ordinal) >= 0;
        }

        private static bool HasRequestTransportError(UnityWebRequest request)
        {
            return request.result != UnityWebRequest.Result.Success;
        }

        /// <summary>
        /// 发送心愿数据到飞书多维表格（协程）
        /// </summary>
        public static IEnumerator SendWish(string wishText, bool isAnonymous,
            Action onSuccess, Action<string> onFailure)
        {
            if (IsSending)
            {
                if (onFailure != null)
                {
                    onFailure(L10n.T("正在发送中，请稍候…", "Sending in progress, please wait…"));
                }
                yield break;
            }

            if (IsInCooldown())
            {
                if (onFailure != null)
                {
                    int remaining = GetCooldownRemaining();
                    onFailure(L10n.T(
                        "请 " + remaining + " 秒后再试",
                        "Please wait " + remaining + " seconds"));
                }
                yield break;
            }

            FeishuAppConfig config = GetFeishuAppConfig();
            if (config == null || !config.IsValid())
            {
                if (onFailure != null)
                {
                    onFailure(L10n.T("星空暂时联系不上，请稍后再试",
                                     "The stars are unreachable, please try again later"));
                }
                ModBehaviour.DevLog("[WishFountain] 飞书应用配置未准备好");
                yield break;
            }

            IsSending = true;

            try
            {
                // 截断超长文本
                if (wishText.Length > MAX_CHARS)
                {
                    wishText = wishText.Substring(0, MAX_CHARS);
                }

                // 获取玩家名
                string playerName;
                if (isAnonymous)
                {
                    playerName = L10n.T("匿名玩家", "Anonymous");
                }
                else
                {
                    playerName = ModBehaviour.TryGetSteamPersonaName();
                    if (string.IsNullOrEmpty(playerName))
                    {
                        playerName = L10n.T("匿名玩家", "Anonymous");
                    }
                }

                // 获取当前场景
                string sceneName = "";
                try
                {
                    sceneName = SceneManager.GetActiveScene().name;
                }
                catch { }

                // 获取语言
                string language = L10n.IsChinese ? "zh-CN" : "en";
                string modVersion = GetModVersion();

                string jsonBody = BuildFeishuRecordJson(playerName, wishText, sceneName, language, modVersion);
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                string recordsUrl = GetFeishuRecordsUrl(config);

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    string tenantAccessToken = null;
                    string authError = null;
                    yield return GetTenantAccessToken(
                        config,
                        token => tenantAccessToken = token,
                        error => authError = error);

                    if (string.IsNullOrEmpty(tenantAccessToken))
                    {
                        ModBehaviour.DevLog("[WishFountain] 飞书鉴权失败: " + (authError ?? "unknown"));
                        if (onFailure != null)
                        {
                            onFailure(L10n.T("星空暂时联系不上，请稍后再试",
                                             "The stars are unreachable, please try again later"));
                        }
                        yield break;
                    }

                    using (UnityWebRequest request = new UnityWebRequest(recordsUrl, "POST"))
                    {
                        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Authorization", "Bearer " + tenantAccessToken);
                        request.SetRequestHeader("Content-Type", "application/json");
                        request.timeout = REQUEST_TIMEOUT;

                        yield return request.SendWebRequest();

                        string responseText = request.downloadHandler != null ? request.downloadHandler.text : "";
                        if (HasRequestTransportError(request))
                        {
                            if (attempt == 0 && ShouldRetryWithFreshTenantToken(request, responseText))
                            {
                                ModBehaviour.DevLog("[WishFountain] 记录写入疑似 token 失效，清理缓存后重试一次");
                                InvalidateCachedTenantAccessToken();
                                continue;
                            }

                            string error = request.error ?? "Unknown error";
                            if (!string.IsNullOrEmpty(responseText))
                            {
                                error = error + " | " + responseText;
                            }
                            ModBehaviour.DevLog("[WishFountain] 写入飞书失败: " + error);
                            if (onFailure != null)
                            {
                                onFailure(L10n.T("星空暂时联系不上，请稍后再试",
                                                 "The stars are unreachable, please try again later"));
                            }
                            yield break;
                        }

                        int code = SimpleJsonHelper.ExtractInt(responseText, "code");
                        if (code != 0)
                        {
                            if (attempt == 0 && ShouldRetryWithFreshTenantToken(request, responseText))
                            {
                                ModBehaviour.DevLog("[WishFountain] 飞书 API 指示 token 无效，清理缓存后重试一次");
                                InvalidateCachedTenantAccessToken();
                                continue;
                            }

                            ModBehaviour.DevLog("[WishFountain] 飞书 API 返回错误: " + GetFeishuErrorMessage(responseText, "bitable_api_error"));
                            if (onFailure != null)
                            {
                                onFailure(L10n.T("星空暂时联系不上，请稍后再试",
                                                 "The stars are unreachable, please try again later"));
                            }
                            yield break;
                        }

                        lastSendTime = Time.realtimeSinceStartup;
                        ModBehaviour.DevLog("[WishFountain] 心愿发送成功");
                        if (onSuccess != null)
                        {
                            onSuccess();
                        }
                        yield break;
                    }
                }

                if (onFailure != null)
                {
                    onFailure(L10n.T("星空暂时联系不上，请稍后再试",
                                     "The stars are unreachable, please try again later"));
                }
            }
            finally
            {
                IsSending = false;
            }
        }
    }
}
