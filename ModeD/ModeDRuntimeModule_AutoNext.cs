using System;
using System.Collections;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ModeDRuntimeModule
    {
        internal void ScheduleAutoNextWave(float delay)
        {
            // 取消旧协程
            if (modeDAutoNextWaveCoroutine != null)
            {
                owner.StopCoroutine(modeDAutoNextWaveCoroutine);
                modeDAutoNextWaveCoroutine = null;
            }

            // 捕获当前波次作为令牌
            int tokenWave = modeDWaveIndex;
            modeDAutoNextWaveCoroutine = owner.StartCoroutine(ModeDAutoNextWave(delay, tokenWave));
        }

        /// <summary>
        /// 自动下一波协程（带波次令牌验证，防止跨波误触发）
        /// </summary>
        private System.Collections.IEnumerator ModeDAutoNextWave(float delay, int tokenWave)
        {
            Func<bool> isAutoNextCurrent = ModeDRuntimeModule.CaptureValidity(owner, false);
            if (delay <= 0f)
            {
                // 立即开波前二次校验
                if (isAutoNextCurrent() && modeDActive && modeDWaveIndex == tokenWave && modeDWaveCompletePending)
                {
                    ModeDStartNextWave();
                }
                modeDAutoNextWaveCoroutine = null;
                yield break;
            }

            float timer = delay;
            while (timer > 0f)
            {
                // 每帧校验：模式仍激活 + 波次未变 + 仍在等待状态
                try
                {
                    if (!isAutoNextCurrent()) yield break;
                    if (!modeDActive || modeDWaveIndex != tokenWave || !modeDWaveCompletePending)
                    {
                        ModBehaviour.DevLog("[ModeD] 自动下一波协程已取消（模式/波次状态变化）");
                        modeDAutoNextWaveCoroutine = null;
                        yield break;
                    }
                }
                catch {}

                // P1-3 修复：使用 unscaledDeltaTime 避免时间缩放（慢动作/暂停）影响自动开波
                timer -= Time.unscaledDeltaTime;
                yield return null;
            }

            // 到时间后二次校验再开波
            if (!isAutoNextCurrent()) yield break;
            if (isAutoNextCurrent() && modeDActive && modeDWaveIndex == tokenWave && modeDWaveCompletePending)
            {
                ModBehaviour.DevLog("[ModeD] 自动下一波协程触发开波");
                ModeDStartNextWave();
            }
            else
            {
                ModBehaviour.DevLog("[ModeD] 自动下一波协程跳过（状态已变化）");
            }
            modeDAutoNextWaveCoroutine = null;
        }
    }
}
