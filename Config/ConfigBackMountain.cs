// 竞技场后山保持默认开放；章节设施仍由战役进度解锁。
// 旧旁路字段保留以兼容配置文件，正式构建不读取、不注册该调试选项。
namespace BossRush
{
    public partial class ModBehaviour
    {
        private partial class BossRushConfig
        {
            public bool backMountainEnabled = true;
            public bool backMountainUnlockAll = false;
        }

        internal bool IsBackMountainConfiguredEnabled()
        {
            return config != null && config.backMountainEnabled;
        }

        internal bool IsBackMountainUnlockAllConfigured()
        {
#if BOSSRUSH_DEV
            return config != null && config.backMountainUnlockAll;
#else
            return false;
#endif
        }
    }
}
