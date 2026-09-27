using System.Collections;
using UnityEngine;

namespace BossRush
{
    /// <summary>Integration-owned scene scheduling and delayed common NPC spawn flow.</summary>
    internal sealed partial class IntegrationRuntimeModule
    {
        internal void ScheduleWishRewardPoolWarmup()
        {
            _owner.StartCoroutine(WishFountainService.WarmupWishRewardPoolAfterDelay());
        }

        internal void ScheduleDelayedSpawnCommonNPCsInNormalMode(string sceneName)
        {
            _owner.StartCoroutine(DelayedSpawnCommonNPCsInNormalMode(sceneName));
        }

        private IEnumerator DelayedSpawnCommonNPCsInNormalMode(string sceneName)
        {
            // Wait until the scene and its level have initialized before spawning shared NPCs.
            const float maxWait = 10f;
            const float interval = 0.2f;
            float elapsed = 0f;

            while (elapsed < maxWait)
            {
                bool mainExists = ReadMainExistsWithWarning("DelayedSpawnCommonNPCsInNormalMode");
                bool levelInited = ReadLevelInitedWithWarning("DelayedSpawnCommonNPCsInNormalMode");

                if (mainExists && levelInited)
                {
                    break;
                }

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            // Give scene physics colliders time to finish loading.
            yield return new WaitForSeconds(0.5f);

            // Cancel if the player moved to a different scene while this coroutine was waiting.
            string currentScene = ReadActiveSceneNameWithWarning("DelayedSpawnCommonNPCsInNormalMode");

            if (currentScene != sceneName)
            {
                ModBehaviour.DevLog("[NPCSpawn] 场景已切换，取消普通模式公共NPC生成");
                yield break;
            }

            // BossRush may have started while scene initialization was pending.
            if (_owner.ShouldSuppressBaseNpcSpawnForCurrentMode())
            {
                ModBehaviour.DevLog("[NPCSpawn] 已进入 BossRush 模式，跳过普通模式公共NPC生成");
                yield break;
            }

            _owner.SpawnCommonNPCsForIntegrationRuntimeModule("普通模式场景初始化完成");
            _owner.ScheduleRestoreFollowingSpouse(sceneName, "普通模式场景初始化完成");
        }
    }
}
