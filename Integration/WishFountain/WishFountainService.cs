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
    public static partial class WishFountainService
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

        /// <summary>弹幕读取成功结果的短 TTL，避免每次开面板都重新鉴权拉表</summary>
        private const float DANMAKU_FETCH_TTL_SECONDS = 45f;

        private static float lastDanmakuFetchAttemptRealtime = -999f;
        private static bool lastDanmakuFetchSucceeded = false;
        private static string lastDanmakuFetchFailureReason = null;
        private static bool danmakuFetchInProgress = false;
        private static List<string> cachedDanmakuFetchSnapshot = null;
        private static Coroutine danmakuFetchBackgroundCoroutine = null;
        private static Action<List<string>> danmakuFetchSuccessWaiters = null;
        private static Action<string> danmakuFetchFailureWaiters = null;

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
        private static bool wishRewardSaveFileEventSubscribed = false;
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

        /// <summary>反射成员缓存：TagsData 上的 Field/Property 查找结果（null 表示已查找但不存在）</summary>
        private static readonly Dictionary<string, MemberInfo> wishRewardTagMemberCache =
            new Dictionary<string, MemberInfo>(StringComparer.OrdinalIgnoreCase);

        internal static void EnsureRuntime()
        {
            EnsureSaveFileEventSubscription();
        }

        internal static void ShutdownRuntime()
        {
            try
            {
                if (wishRewardSaveFileEventSubscribed)
                {
                    SavesSystem.OnSetFile -= OnWishRewardSaveFileChanged;
                    wishRewardSaveFileEventSubscribed = false;
                }
            }
            catch (Exception e)
            {
                wishRewardSaveFileEventSubscribed = false;
                ModBehaviour.DevLog("[WishFountain] [WARNING] 许愿奖励存档事件解绑失败: " + e.Message);
            }
        }

        public static void ResetStaticCaches()
        {
            IsSending = false;
            lastSendTime = -999f;
            cachedFeishuConfig = null;
            configLoaded = false;
            cachedModVersion = null;
            InvalidateCachedTenantAccessToken();
            ResetDanmakuFetchState();
            cachedWishRewardNextAvailableTicks = 0L;
            wishRewardCooldownLoaded = false;
            lastWishRewardPoolInitAttemptRealtime = -999f;
            wishRewardPoolWarmupInProgress = false;
            wishRewardTagMemberCache.Clear();
            ResetWishRewardPoolCaches();
        }

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
            public readonly HashSet<string> tagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class WishRewardPoolSelection
        {
            public string poolMode = "legacy_no_match";
            public string fallbackReason = string.Empty;
            public readonly HashSet<int> filteredTypeIds = new HashSet<int>();
            public readonly HashSet<string> matchedItemTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public bool HasFilteredPool
            {
                get { return filteredTypeIds.Count > 0; }
            }
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

        private static void CollectWishRewardTagNames(Item item, HashSet<string> destination)
        {
            if (item == null || item.Tags == null || destination == null)
            {
                return;
            }

            foreach (Duckov.Utilities.Tag tag in item.Tags)
            {
                if (tag == null || string.IsNullOrEmpty(tag.name))
                {
                    continue;
                }

                destination.Add(tag.name);
            }
        }

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
            EnsureSaveFileEventSubscription();
        }

        private static void EnsureSaveFileEventSubscription()
        {
            if (wishRewardSaveFileEventSubscribed)
            {
                return;
            }

            try
            {
                SavesSystem.OnSetFile -= OnWishRewardSaveFileChanged;
                SavesSystem.OnSetFile += OnWishRewardSaveFileChanged;
                wishRewardSaveFileEventSubscribed = true;
            }
            catch (Exception e)
            {
                wishRewardSaveFileEventSubscribed = false;
                ModBehaviour.DevLog("[WishFountain] [WARNING] 许愿奖励存档事件订阅失败: " + e.Message);
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
    }
}
