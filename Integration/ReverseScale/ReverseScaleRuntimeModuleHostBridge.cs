using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>保留原生命周期、本地化与公开配置器入口，转发到逆鳞运行时模块。</summary>
    public partial class ModBehaviour
    {
        private void InitializeReverseScaleSystem()
        {
            reverseScaleRuntime.InitializeReverseScaleSystem();
        }

        private void SetupReverseScaleForScene(Scene scene)
        {
            reverseScaleRuntime.SetupReverseScaleForScene(scene);
        }

        private void CleanupReverseScaleSystem()
        {
            reverseScaleRuntime.CleanupReverseScaleSystem();
        }

        internal void InjectReverseScaleLocalizationFromRuntimeModule()
        {
            reverseScaleRuntime.InjectReverseScaleLocalization();
        }

        internal static WaitForSeconds ReverseScaleSharedWait05sForRuntime
        {
            get { return sharedWait05s; }
        }
        public static bool TryConfigureReverseScale(ItemStatsSystem.Item item, string baseName)
        {
            return ReverseScaleRuntimeModule.TryConfigureReverseScale(item, baseName);
        }

    }
}
