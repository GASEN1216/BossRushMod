using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// BossRush 地图配置类（统一管理所有地图相关配置）
    /// 从 ModBehaviour 嵌套类提升为顶层类，字段与行为完全不变
    /// </summary>
    public class BossRushMapConfig
    {
        public string sceneName;           // 运行时场景名称（如 Level_DemoChallenge_1）
        public string sceneID;             // 加载用场景ID（如 Level_DemoChallenge_Main）
        public string displayNameCN;       // 显示名称（中文）
        public string displayNameEN;       // 显示名称（英文）
        public Vector3[] spawnPoints;      // Boss 刷新点
        public Vector3? customSpawnPos;    // 玩家自定义传送位置（null 表示使用默认）
        public Vector3? defaultSignPos;    // 默认路牌位置（null 表示使用玩家位置偏移）
        public int beaconIndex;            // 信标索引（用于地图选择UI）
        public string previewImageName;    // 预览图文件名（可选，用于地图选择UI）
        public Vector3 mapNorth;           // 地图北方向量（用于方位播报，与小地图朝向一致）
        public Vector3[] modeESpawnPoints;  // Mode E 专用刷怪点（null 表示使用原地图 spawner 位置兜底）
        public Vector3? modeEPlayerSpawnPos; // Mode E 独狼玩家落点（null 表示使用远离Boss的安全位置兜底）
        
        /// <summary>
        /// 获取本地化的显示名称
        /// </summary>
        public string displayName { get { return L10n.T(displayNameCN, displayNameEN); } }
        
        public BossRushMapConfig(string name, string id, string displayCN, string displayEN, Vector3[] spawns, Vector3? customPos = null, Vector3? signPos = null, int beacon = 0, string preview = null, Vector3? north = null, Vector3[] modeESpawns = null, Vector3? modeEPlayerPos = null)
        {
            sceneName = name;
            sceneID = id;
            displayNameCN = displayCN;
            displayNameEN = displayEN;
            spawnPoints = spawns;
            customSpawnPos = customPos;
            defaultSignPos = signPos;
            beaconIndex = beacon;
            previewImageName = preview;
            // 默认使用 DEMO 竞技场的北方向量
            mapNorth = north.HasValue ? north.Value : new Vector3(-0.959f, 0f, 0.284f);
            modeESpawnPoints = modeESpawns;
            modeEPlayerSpawnPos = modeEPlayerPos;
        }
    }
}
