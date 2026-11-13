using System;

namespace BossRush
{
    /// <summary>
    /// 末班编组站的官方场景身份与场景包约定。场景包由作者工程 `LastShiftBuilder.BuildAndExit` 生成
    /// （`ArtSource/LastShiftYard/README.md`）；服务根 `LastShiftLevel` 在包里未激活，世界根 `LastShiftYardWorld` 常开。
    /// </summary>
    internal static class LastShiftRaidScene
    {
        internal const string SceneId = "BossRush_LastShiftYard";
        internal const string ScenePath = "Assets/LastShiftYard/LastShiftYardRaid.unity";
        internal const string SceneName = "LastShiftYardRaid";
        /// <summary>作者工程里 `LastShiftYardRaid.unity.meta` 的 GUID，桥接用它登记 Eflatun 路径表。</summary>
        private const string SceneGuid = "65770131696b8244fa687f9924eaedef";
        internal const string BundleRelativePath = "Assets/arenas/lastshift_raid";
        internal const string ServicesRootName = "LastShiftLevel";
        internal const string WorldRootName = "LastShiftYardWorld";
        internal const string LogTag = "[LastShift]";

        internal static readonly ModRaidSceneIdentity Identity =
            new ModRaidSceneIdentity(SceneId, ScenePath, SceneGuid, "BossRush_LastShiftYard_SceneName", InjectLocalization, LogTag);

        internal static string DisplayName { get { return L10n.T("末班编组站", "Last Shift Yard"); } }

        internal static void InjectLocalization()
        {
            LocalizationHelper.InjectLocalization("BossRush_LastShiftYard_SceneName", DisplayName);
            LocalizationHelper.InjectLocalization(LastShiftEntryInteractable.NameKey, L10n.T("剪票上末班车", "Board the last train"));
            // 「没人来领的末班票」三条线索与收尾的官方笔记标题、正文（随语言刷新）
            LastShiftYuXiang.InjectLocalization();
            LastShiftNoteBridge.InjectNoteKeys();
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
                RequiredWorldPaths = new[] { "Markers/Exits", "Markers/Loot", "Markers/Spawns", "Markers/Interact", "Collision/COL_Ground", "Navigation" },
                // 天气副本从仓库区（户外出击图）克隆，本来就是户外 Volume，不用像基地出发那样换掉地堡室内的。
                AdjustTimeOfDay = null,
                // 官方爆炸遮挡射线写死在 y = 0.5：司机室楼面 5.2 m、跨线桥 5.4 m，不抬就整片爆炸穿墙。补丁按场景句柄门控（同潮闸镇）。
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
