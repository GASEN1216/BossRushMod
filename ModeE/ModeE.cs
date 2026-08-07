// ============================================================================
// ModeE.cs - 划地为营模式核心逻辑
// ============================================================================
// 模块说明：
//   Mode E（划地为营）是 BossRush 的多阵营沙盒混战模式。
//   玩家裸装携带"营旗"进入竞技场，系统根据营旗类型分配阵营，
//   地图刷怪点平均分配给所有参战阵营，每个阵营在各自领地一次性生成 Boss。
//   同阵营实体互不伤害，不同阵营自动敌对交战。
//   敌人按"出生时死亡基线"计算个人层数：每层生命/伤害 +5%（各阵营独立累计）。
//
// 主要功能：
//   - 入场条件检测（营旗 + 裸装）
//   - 阵营分配（随机/指定）
//   - 阵营气泡显示
//   - 模式启动与结束
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using TMPro;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using Duckov.Economy;
using HarmonyLib;

namespace BossRush
{
    internal enum ModeEShellShopPatchDisposition
    {
        PassOriginal,
        Block,
        HandleModeE
    }

    internal sealed class ModeEShellBalanceChangedEvent
    {
        internal int SessionToken;
        internal long SessionGeneration;
        internal int NewBalance;
    }

    internal sealed class ModeEShellPriceCacheChangedEvent
    {
        internal int SessionToken;
        internal int SceneBuildIndex;
        internal long SessionGeneration;
        internal long MerchantGeneration;
        internal StockShop Shop;
        internal int? ItemTypeID;
    }

    internal sealed class ModeEShellTransactionGateChangedEvent
    {
        internal int SessionToken;
        internal long MerchantGeneration;
        internal long TransactionID;
        internal bool IsBusy;
        internal StockShop Shop;
    }

    /// <summary>
    /// Mode E（划地为营）：多阵营沙盒混战模式
    /// <para>玩家裸装+营旗入场，分配阵营，Boss一次性生成，按个人基线层数动态缩放</para>
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode E 状态变量

        /// <summary>是否处于 Mode E 模式</summary>
        private bool modeEActive = false;

        /// <summary>玩家被分配的阵营</summary>
        private Teams modeEPlayerFaction = Teams.player;

        /// <summary>Mode E 会话序号，防止上一局异步对象晚到污染新局。</summary>
        private int modeESessionSerial = 0;

        /// <summary>当前有效的 Mode E 会话令牌。</summary>
        private int modeESessionToken = 0;

        /// <summary>贝壳经济整局代号。只在整局初始化/失效时递增。</summary>
        private long modeEShellSessionGeneration = 0L;

        /// <summary>贝壳经济商人代号。商人创建、清理或重建时递增。</summary>
        private long modeEShellMerchantGeneration = 0L;

        private int modeEShellSessionToken = 0;
        private int modeEShellSessionScene = -1;
        private bool modeEShellEconomyAvailable = false;
        private bool modeEShellFirstPositiveRewardGranted = false;
        private int modeEShellBalance = 0;
        private int modeEShellItemTypeID = -1;

        // 计数器跨 session 保持单调，旧异步 continuation 不能复用新 owner 身份。
        private long modeEShellNextTransactionID = 0L;
        private long modeEShellNextUiBindingID = 0L;
        private long modeEShellActiveUiBindingID = 0L;
        private StockShop modeEShellActiveUiShop = null;

        private readonly Dictionary<StockShop, long> modeEMerchantShopGenerations
            = new Dictionary<StockShop, long>();
        private readonly HashSet<StockShop> modeEOwnedShopTombstones
            = new HashSet<StockShop>();

        private struct ModeEShellPriceKey : IEquatable<ModeEShellPriceKey>
        {
            internal StockShop Shop;
            internal int ItemTypeID;
            internal long MerchantGeneration;

            public bool Equals(ModeEShellPriceKey other)
            {
                return object.ReferenceEquals(Shop, other.Shop) &&
                       ItemTypeID == other.ItemTypeID &&
                       MerchantGeneration == other.MerchantGeneration;
            }

            public override bool Equals(object obj)
            {
                return obj is ModeEShellPriceKey && Equals((ModeEShellPriceKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Shop != null
                        ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Shop)
                        : 0;
                    hash = (hash * 397) ^ ItemTypeID;
                    hash = (hash * 397) ^ MerchantGeneration.GetHashCode();
                    return hash;
                }
            }
        }

        private sealed class ModeEShellTransactionOwner
        {
            internal int SessionToken;
            internal long MerchantGeneration;
            internal long TransactionID;
            internal StockShop Shop;
            internal bool OwnsBuying;
            internal bool OwnsSelling;
            internal bool IsSellAll;
        }

        private ModeEShellTransactionOwner modeEShellTransactionOwner = null;
        private readonly Dictionary<long, long> modeEShellTransactionUiBindings
            = new Dictionary<long, long>();
        private bool modeEShellOriginalSellBypassArmed = false;
        private long modeEShellOriginalSellBypassTransactionID = 0L;
        private StockShop modeEShellOriginalSellBypassShop = null;
        private readonly Dictionary<long, int> modeEShellDebits = new Dictionary<long, int>();
        private readonly HashSet<long> modeEShellRefundedTransactions = new HashSet<long>();
        private readonly HashSet<long> modeEShellCommittedTransactions = new HashSet<long>();
        private readonly Dictionary<ModeEShellPriceKey, int> modeEShellPriceCache
            = new Dictionary<ModeEShellPriceKey, int>();
        private readonly HashSet<ModeEShellPriceKey> modeEShellPendingPriceKeys
            = new HashSet<ModeEShellPriceKey>();
        private bool modeEShellFatalCleanupPending = false;
        private long modeEShellDeathCallbackSequence = 0L;

        private enum ModeEShellRewardKind
        {
            None,
            StandardBoss,
            PromotedBoss
        }

        private struct ModeEShellRewardSnapshot
        {
            internal bool Claimed;
            internal float BirthMaxHealth;
            internal ModeEShellRewardKind RewardKind;
            internal Teams RegisteredFaction;
            internal int SessionToken;
            internal int SceneBuildIndex;
            internal long SessionGeneration;
            internal CharacterMainControl Killer;
            internal bool PlayerAliveAtSnapshot;
            internal bool HasDeathPosition;
            internal Vector3 DeathPosition;
            internal bool HasPlayerPosition;
            internal Vector3 PlayerPosition;
            internal int Frame;
            internal long CallbackSequence;
        }

        private event Action<ModeEShellBalanceChangedEvent> ModeEShellBalanceChanged;
        private event Action<ModeEShellPriceCacheChangedEvent> ModeEShellPriceCacheChanged;
        private event Action<ModeEShellTransactionGateChangedEvent> ModeEShellTransactionGateChanged;

        // 当前 DLL 的 M0/M1 反射契约；每个 session 都会重新解析并验证补丁安装。
        private MethodInfo modeEShellBuyTarget;
        private MethodInfo modeEShellSellTarget;
        private MethodInfo modeEShellGetItemInstanceDirectTarget;
        private MethodInfo modeEShellItemEntrySetupTarget;
        private MethodInfo modeEShellViewSetupTarget;
        private MethodInfo modeEShellRefreshInteractionButtonTarget;
        private FieldInfo modeEShellBuyingField;
        private FieldInfo modeEShellSellingField;
        private FieldInfo modeEShellCurrentStockField;
        private FieldInfo modeEShellOnStockChangedField;
        private FieldInfo modeEShellOnAfterItemSoldField;
        private FieldInfo modeEShellOnItemPurchasedField;
        private PropertyInfo modeEShellPurchaseNotificationFormatProperty;
        private FieldInfo modeEShellItemEntryPriceTextField;
        private FieldInfo modeEShellViewPriceTextField;
        private FieldInfo modeEShellViewInteractionButtonField;
        private FieldInfo modeEShellViewInteractionButtonImageField;
        private FieldInfo modeEShellViewInteractionTextField;
        private FieldInfo modeEShellViewMerchantNameTextField;

        /// <summary>当前所有存活的 Mode E 敌人（跨阵营）</summary>
        private readonly List<CharacterMainControl> modeEAliveEnemies = new List<CharacterMainControl>();

        /// <summary>Mode E 结束清理快照复用列表，避免结束时按敌人数量分配数组。</summary>
        private readonly List<CharacterMainControl> modeEEndCleanupEnemyScratch = new List<CharacterMainControl>(32);

        /// <summary>Mode E 存活敌人的去重集合，避免重复注册导致列表和扫描路径膨胀。</summary>
        private readonly HashSet<CharacterMainControl> modeEAliveEnemySet = new HashSet<CharacterMainControl>();

        /// <summary>BossRegen 专用缓存，避免变异词条开启时每帧重建存活列表。</summary>
        private readonly List<MonoBehaviour> modeEBossRegenCache = new List<MonoBehaviour>(32);
        private bool modeEBossRegenCacheDirty = true;

        /// <summary>Mode E 存活敌人的阵营缓存，避免清理路径回退到全阵营扫描。</summary>
        private readonly Dictionary<CharacterMainControl, Teams> modeEAliveEnemyFactionMap
            = new Dictionary<CharacterMainControl, Teams>();

        /// <summary>各阵营死亡计数（用于计算每个敌人的个人层数：当前死亡数 - 出生基线）</summary>
        private Dictionary<Teams, int> modeEFactionDeathCount = new Dictionary<Teams, int>();

        /// <summary>
        /// [P4性能优化] 按阵营维护的独立存活敌人列表，避免缩放时全量遍历 modeEAliveEnemies
        /// Key = 阵营, Value = 该阵营的存活敌人列表
        /// </summary>
        private readonly Dictionary<Teams, List<CharacterMainControl>> modeEFactionAliveMap
            = new Dictionary<Teams, List<CharacterMainControl>>();

        /// <summary>Mode E 入场预热线程，尽量把重初始化提前摊到前置等待阶段。</summary>
        private Coroutine modeEStartupWarmupCoroutine = null;

        /// <summary>当前预热对应的场景名，用于避免跨场景误复用协程状态。</summary>
        private string modeEStartupWarmupSceneName = null;
        private const float MODEE_STARTUP_VERIFICATION_TIMEOUT_SECONDS = 8f;
        private HashSet<int> modeEStartupInventorySnapshot = null;
        private int modeEStartupFlagTypeId = -1;
        private bool modeEStartupRecoveryArmed = false;
        private bool modeEStartupFirstBossSpawned = false;
        private bool modeEStartupHasPlayerPosition = false;
        private Vector3 modeEStartupPlayerPosition = Vector3.zero;

        private const float MODEE_PLAYER_NAME_CACHE_INTERVAL = 5f;
        private const float MODEE_HEALTHBAR_LOOKUP_INTERVAL = 1f;
        private const float MODEE_UI_WARNING_LOG_INTERVAL = 5f;
        private static readonly BindingFlags ModeEUiInstanceBindingFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static MethodInfo modeERefreshCharacterIconMethod = null;
        private static MethodInfo modeESteamFriendsGetPersonaNameMethod = null;
        private static MethodInfo modeESteamManagerGetSteamDisplayMethod = null;

        private string modeECachedPlayerName = null;
        private float modeENextPlayerNameRefreshTime = 0f;
        private HealthBar modeECachedPlayerHealthBar = null;
        private float modeENextHealthBarLookupTime = 0f;
        private float modeENextUiWarningLogTime = 0f;
        private readonly Dictionary<int, HealthBar> modeEHealthBarCacheByTargetId = new Dictionary<int, HealthBar>();
        private readonly Dictionary<int, string> modeEHealthBarBaseTextByBarId = new Dictionary<int, string>();
        private readonly Dictionary<int, string> modeEHealthBarDesiredTextByBarId = new Dictionary<int, string>();
        private readonly Dictionary<int, int> modeEHealthBarTargetIdsByBarId = new Dictionary<int, int>();
        private readonly Dictionary<int, int> modeEHealthBarAppliedVersionByBarId = new Dictionary<int, int>();
        private int modeEHealthBarNameVersion = 1;
        private bool? modeELastHealthBarLanguageIsChinese = null;

        #endregion

        #region Mode E 配置

        /// <summary>Mode E 可用阵营池（排除 player/middle/all）</summary>
        private static readonly Teams[] ModeEAvailableFactions = new Teams[]
        {
            Teams.scav,   // 拾荒者
            Teams.usec,   // USEC雇佣兵
            Teams.bear,   // BEAR雇佣兵
            Teams.lab,    // 实验室
            Teams.wolf    // 狼群
        };

        /// <summary>随机营旗 TypeID（引用 FactionFlagConfig 常量）</summary>
        private int modeERandomFlagTypeId = FactionFlagConfig.RANDOM_FLAG_TYPE_ID;

        /// <summary>指定阵营营旗 TypeID → Teams 映射（引用 FactionFlagConfig 常量）</summary>
        private Dictionary<int, Teams> modeEFactionFlagMap = new Dictionary<int, Teams>
        {
            { FactionFlagConfig.SCAV_FLAG_TYPE_ID, Teams.scav },     // 拾荒者营旗
            { FactionFlagConfig.USEC_FLAG_TYPE_ID, Teams.usec },     // USEC营旗
            { FactionFlagConfig.BEAR_FLAG_TYPE_ID, Teams.bear },     // BEAR营旗
            { FactionFlagConfig.LAB_FLAG_TYPE_ID,  Teams.lab },      // 实验室营旗
            { FactionFlagConfig.WOLF_FLAG_TYPE_ID, Teams.wolf },     // 狼群营旗
            { FactionFlagConfig.PLAYER_FLAG_TYPE_ID, Teams.player }  // 爷的营旗（独立阵营，敌对所有Boss）
        };

        #endregion

        #region Mode E 公共属性

        /// <summary>是否处于 Mode E 模式</summary>
        public bool IsModeEActive { get { return modeEActive; } }

        /// <summary>玩家当前所属阵营</summary>
        public Teams ModeEPlayerFaction { get { return modeEPlayerFaction; } }

        internal int CurrentModeESessionToken { get { return modeESessionToken; } }

        /// <summary>当前所有存活的 Mode E 敌人列表（只读访问，供龙王等系统查找攻击目标）</summary>
        public List<CharacterMainControl> ModeEAliveEnemies { get { return modeEAliveEnemies; } }

        #endregion

    }
}
