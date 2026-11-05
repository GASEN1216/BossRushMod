using System;

namespace BossRush
{
    /// <summary>
    /// 潮闸镇的官方场景身份与场景包约定。场景包由作者工程 `TidegateBuilder.BuildAndExit` 生成
    /// （`ArtSource/Tidegate/README.md`）；服务根 `TidegateLevel` 在包里未激活，世界根 `TidegateWorld` 常开。
    /// </summary>
    internal static class TidegateRaidScene
    {
        internal const string SceneId = "BossRush_Tidegate";
        internal const string ScenePath = "Assets/Tidegate/TidegateRaid.unity";
        internal const string SceneName = "TidegateRaid";
        /// <summary>作者工程里 `TidegateRaid.unity.meta` 的 GUID，桥接用它登记 Eflatun 路径表。</summary>
        private const string SceneGuid = "1355ea33e86768f498ecd7e7cddcd279";
        internal const string BundleRelativePath = "Assets/arenas/tidegate_raid";
        internal const string ServicesRootName = "TidegateLevel";
        internal const string WorldRootName = "TidegateWorld";
        internal const string LogTag = "[Tidegate]";

        internal static readonly ModRaidSceneIdentity Identity =
            new ModRaidSceneIdentity(SceneId, ScenePath, SceneGuid, "BossRush_Tidegate_SceneName", InjectLocalization, LogTag);

        internal static string DisplayName { get { return L10n.T("潮闸镇", "Tidegate"); } }

        internal static void InjectLocalization()
        {
            LocalizationHelper.InjectLocalization("BossRush_Tidegate_SceneName", DisplayName);
            LocalizationHelper.InjectLocalization(TidegateRaftInteractable.NameKey, L10n.T("乘闸伯的竹筏去潮闸镇", "Take Old Zha's raft to Tidegate"));
            LocalizationHelper.InjectLocalization(TidegateWatchInteractable.NameKey, L10n.T("把潮汐怀表交给闸伯", "Give Old Zha the tide pocket watch"));
            TidegateNoteBridge.InjectNoteKeys();
            TidegateText.InjectMapLabels();
        }

        internal static bool IsScene(UnityEngine.SceneManagement.Scene scene) { return ModRaidSceneBridge.IsScene(Identity, scene); }

        internal static bool IsActiveScene
        {
            get { return IsScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); }
        }

        internal static ModRaidProfile CreateProfile()
        {
            return new ModRaidProfile
            {
                Identity = Identity,
                BundleRelativePath = BundleRelativePath,
                ServicesRootName = ServicesRootName,
                WorldRootName = WorldRootName,
                SpawnMarkerPath = "Markers/PlayerSpawn",
                RequiredWorldPaths = new[] { "Markers/Exits", "Markers/Loot", "Markers/Spawns", "Collision", "Visual/VIS_Water" },
                // 潮闸镇是户外海边：基地克隆来的天气副本里，白天 / 黎明的地堡室内 Volume（远景雾纯黑）换成官方户外的。
                AdjustTimeOfDay = delegate (TimeOfDayConfig clone) { SkyIslandOutdoorDaylight.Apply(clone); },
                // 官方爆炸遮挡射线写死在 y=0.5，镇区地面在 0.8 m 上下、灯塔岬更高：不抬就整片爆炸穿墙。补丁按场景句柄门控。
                SceneLoaded = SkyIslandExplosionObstaclePatch.Arm,
                SceneUnloaded = delegate (UnityEngine.SceneManagement.Scene scene) { SkyIslandExplosionObstaclePatch.Disarm(); },
                DisplayName = delegate { return DisplayName; },
            };
        }

        internal static bool IsBundleDeployed()
        {
            return ModRaidLease.IsBundleDeployed(new ModRaidProfile { BundleRelativePath = BundleRelativePath });
        }
    }
}
