using UnityEngine.SceneManagement;

namespace BossRush
{
    public partial class ModBehaviour
    {
        // 原场景初始化调用点保留，地图对象业务由唯一 IntegrationRuntimeModule 执行。
        private void SpawnBossRushMapObjects()
        {
            bossRushIntegrationRuntime.SpawnBossRushMapObjects();
        }

        // ModBehaviour 的既有公开流程继续取得原方法签名和协程时序。
        private System.Collections.IEnumerator WaitForLevelInitializedThenSetup_Integration(Scene scene)
        {
            return bossRushIntegrationRuntime.WaitForLevelInitializedThenSetup_Integration(scene);
        }

        // 模块等待完成后，在宿主上启动原有场景 setup 协程。
        internal void StartBossRushDemoChallengeSetupForScene(Scene scene)
        {
            StartCoroutine(SetupBossRushInDemoChallenge(scene));
        }

        // 外部装备内容注册器使用的兼容入口；订阅 owner 与 delegate 都在 IntegrationRuntimeModule。
        private void UnsubscribeDragonBreathEffectEvent()
        {
            bossRushIntegrationRuntime.UnsubscribeDragonBreathEffectEvent();
        }
    }
}
