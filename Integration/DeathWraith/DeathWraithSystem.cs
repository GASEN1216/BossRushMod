// ============================================================================
// DeathWraithSystem.cs — 死亡亡魂系统
// ============================================================================
// 模块说明：
//   玩家死亡后记录装备与属性，下次进入同一子场景时在死亡位置生成
//   一个复制了玩家外观和装备的Boss级敌怪（亡魂）。
//
//   亡魂命名与属性根据掉落物品价值与玩家总财产比例分为三档：
//   - ≥50%：强壮的XX的亡魂（移速+90%，攻击+50%）
//   - 10%~50%：均衡的XX的亡魂（移速+50%，攻击+25%）
//   - <10%：弱小的XX的亡魂（移速+20%）
//
//   亡魂死后不掉落任何物品。再次死亡覆盖更新亡魂数据。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using Duckov;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using Saves;

namespace BossRush
{
    /// <summary>
    /// 亡魂数据（持久化存储）
    /// </summary>
    [Serializable]
    public class WraithInfo
    {
        public bool valid;
        public uint raidID;
        public string sceneName;
        public string subSceneID;
        public float posX, posY, posZ;
        // 仅保留给旧存档兼容，主角本体并不是通过 CharacterRandomPreset 创建。
        public string playerPresetName;
        public string playerPresetRuntimeName;
        public string playerName;
        public int droppedItemsValue;
        public long playerTotalWealth;
        public float playerMaxHealth;
        public bool hasPlayerFaceData;
        public CustomFaceSettingData playerFaceData;
        public AudioManager.VoiceType playerVoiceType;
        public AudioManager.FootStepMaterialType playerFootStepMaterialType;
        public bool hasBoundMeleeSnapshot;
        public int boundMeleeTypeId;
        public string boundMeleeDisplayName;
        public ItemTreeData boundMeleeItemTreeData;
        public ItemTreeData itemTreeData;
        public bool killed;
    }

    [Serializable]
    public class WraithBoundMeleeSnapshot
    {
        public bool valid;
        public int typeId;
        public string displayName;
        public ItemTreeData itemTreeData;
    }

    internal sealed class PendingDeathWraithContext_DeathWraith
    {
        public bool valid;
        public uint raidID;
        public string sceneName;
        public string subSceneID;
        public Vector3 worldPosition;
        public string playerPresetName;
        public string playerPresetRuntimeName;
        public string playerName;
        public int droppedItemsValue;
        public long playerTotalWealth;
        public float playerMaxHealth;
        public bool hasPlayerFaceData;
        public CustomFaceSettingData playerFaceData;
        public AudioManager.VoiceType playerVoiceType;
        public AudioManager.FootStepMaterialType playerFootStepMaterialType;
        public bool hasBoundMeleeSnapshot;
        public int boundMeleeTypeId;
        public string boundMeleeDisplayName;
        public ItemTreeData boundMeleeItemTreeData;
    }

    internal sealed class OriginalDeadBodyInfo_DeathWraith
    {
        public bool valid;
        public uint raidID;
        public string subSceneID;
        public Vector3 worldPosition;
        public ItemTreeData itemTreeData;
    }

    internal sealed class DeadBodySpawnContext_DeathWraith
    {
        public uint raidID;
        public string subSceneID;
        public Vector3 worldPosition;
    }

    /// <summary>
    /// 亡魂强度等级
    /// </summary>
    public enum WraithTier
    {
        Weak,       // <10%  弱小的
        Balanced,   // 10%~50% 均衡的
        Strong      // ≥50%  强壮的
    }

    /// <summary>
    /// 死亡亡魂系统（partial class ModBehaviour）
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 亡魂系统 — 常量与字段

        private const string DEATH_WRAITH_LIST_SAVE_KEY = "BossRush_DeathWraith_List";
        private const string DEATH_WRAITH_BOUND_MELEE_SAVE_KEY = "BossRush_DeathWraith_BoundMelee";
        private const string DEATH_WRAITH_NAME_KEY_PREFIX = "BossRush_DeathWraith_Name_";
        private const string DEATH_WRAITH_GUN_AI_PRESET_NAME = "Cname_Boss_Red";
        private const string DEATH_WRAITH_MELEE_AI_PRESET_NAME = "Cname_Wolf";
        private const float DEATH_WRAITH_PENDING_MAX_AGE_SECONDS = 0.5f;
        private const int DEATH_WRAITH_PENDING_MAX_FRAME_DELTA = 1;
        private static readonly FieldInfo LevelManager_CharacterModelField =
            typeof(LevelManager).GetField("characterModel", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CharacterRandomPreset_AiControllerField =
            typeof(CharacterRandomPreset).GetField("aiController", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemAgentHolder_MeleeRefField_DeathWraith =
            typeof(ItemAgentHolder).GetField("_meleeRef", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemAgentHolder_GunRefField_DeathWraith =
            typeof(ItemAgentHolder).GetField("_gunRef", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemAgentHolder_CurrentUsingSocketCacheField_DeathWraith =
            typeof(ItemAgentHolder).GetField("_currentUsingSocketCache", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemAgent_ItemField_DeathWraith =
            typeof(ItemStatsSystem.ItemAgent).GetField("item", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DuckovItemAgent_SocketsListField_DeathWraith =
            typeof(DuckovItemAgent).GetField("socketsList", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemAgentMeleeWeapon_SoundKeyField_DeathWraith =
            typeof(ItemAgent_MeleeWeapon).GetField("soundKey",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo ItemAgentMeleeWeapon_SlashFxDelayTimeField_DeathWraith =
            typeof(ItemAgent_MeleeWeapon).GetField("slashFxDelayTime",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo ItemAgentMeleeWeapon_OnInitializeMethod_DeathWraith =
            typeof(ItemAgent_MeleeWeapon).GetMethod("OnInitialize",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private PendingDeathWraithContext_DeathWraith pendingDeathWraithContext;
        private int pendingDeathWraithPrimedFrame = -1;
        private float pendingDeathWraithPrimedRealtime = -1f;
        private OriginalDeadBodyInfo_DeathWraith pendingOriginalDeadBodyInfo_DeathWraith;
        private int pendingOriginalDeadBodyInfoFrame_DeathWraith = -1;
        private float pendingOriginalDeadBodyInfoRealtime_DeathWraith = -1f;

        // [性能优化] 亡魂记录列表内存缓存：进局加载一次，死亡帧只改内存 + 标脏，
        // 真正的 ES3 序列化延后到游戏的官方存档点（OnCollectSaveData / 场景切换 / 去抖 tick），
        // 避免在死亡那一帧同步反序列化 + 序列化整张含完整物品树的列表（抬回去动画卡顿主因）。
        private List<WraithInfo> _deathWraithListCache;        // 内存中的权威副本；null 表示尚未从存档加载
        private bool _deathWraithListDirty;                    // 是否有未写入 ES3 的改动
        private float _deathWraithListDirtySince = -1f;        // 变脏的时刻（用于去抖）
        // 去抖延迟设得较长：正常流程下游戏在撤离/切场景时触发 OnCollectSaveData 会先把列表刷掉
        // （那一刻游戏本就在序列化整个角色，叠加我们一次列表序列化几乎无感）。
        // 这个 tick 只是「玩家长时间停留、迟迟没碰到官方存档点」时的兜底，
        // 设得长是为了避免它在死亡抬回去动画播放途中突然刷盘、反而制造卡顿。
        private const float DEATH_WRAITH_SAVE_DELAY = 30f;
        private Dictionary<string, CharacterRandomPreset> deathWraithPresetCacheByNameKey;
        private Dictionary<string, CharacterRandomPreset> deathWraithPresetCacheByRuntimeName;
        private readonly Dictionary<uint, CharacterMainControl> activeWraithsByRaidId =
            new Dictionary<uint, CharacterMainControl>();
        private readonly Dictionary<CharacterMainControl, uint> activeWraithRaidIdByCharacter =
            new Dictionary<CharacterMainControl, uint>();
        private readonly HashSet<uint> spawningWraithRaidIds = new HashSet<uint>();
        private readonly List<DeadBodySpawnContext_DeathWraith> pendingDeadBodySpawnContexts =
            new List<DeadBodySpawnContext_DeathWraith>();

        private void HandleDeathWraithConfigChanged_DeathWraith()
        {
            RefreshDeathWraithEventBindings_DeathWraith();

            if (IsDeathWraithSystemEnabled())
            {
                DevLog("[DeathWraith] 系统配置已开启");
                return;
            }

            DevLog("[DeathWraith] 系统配置已关闭，清理当前与已存亡魂状态");
            ClearDeathWraithState_DeathWraith();
            InvalidateStoredDeathWraithRecords_DeathWraith("配置关闭");
        }

        private void RefreshDeathWraithEventBindings_DeathWraith()
        {
            try
            {
                Health.OnHurt -= PrimeDeathWraithData_DeathWraith;
                Health.OnDead -= RecordDeathWraithData_DeathWraith;
                Health.OnDead -= OnWraithDied_DeathWraith;
                SavesSystem.OnCollectSaveData -= OnCollectSaveData_BoundMeleeSnapshot_DeathWraith;
                SavesSystem.OnCollectSaveData -= FlushDeathWraithListIfDirty_DeathWraith;

                if (!IsDeathWraithSystemEnabled())
                {
                    return;
                }

                Health.OnHurt += PrimeDeathWraithData_DeathWraith;
                Health.OnDead += RecordDeathWraithData_DeathWraith;
                Health.OnDead += OnWraithDied_DeathWraith;
                SavesSystem.OnCollectSaveData += OnCollectSaveData_BoundMeleeSnapshot_DeathWraith;
                // 借道游戏官方存档收集点把内存中的亡魂列表落到 ES3 缓存（撤离/切场景/退出时都会触发），
                // 这样死亡帧本身不再做 Load+Save 全表序列化。
                SavesSystem.OnCollectSaveData += FlushDeathWraithListIfDirty_DeathWraith;
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 刷新事件绑定失败: " + e.Message);
            }
        }

        private void OnSetFile_DeathWraith()
        {
            pendingDeadBodySpawnContexts.Clear();
            spawningWraithRaidIds.Clear();
            // 切换存档槽：丢弃内存缓存，下次访问时从新槽位重新加载，避免跨槽串档。
            // 不在此 flush——OnSetFile 发生在切槽时，旧槽的内容应已通过官方存档点写过。
            _deathWraithListCache = null;
            _deathWraithListDirty = false;
            _deathWraithListDirtySince = -1f;
        }

        #endregion

    }
}
