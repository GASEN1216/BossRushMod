using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    // 官方烟雾的计时等待没有销毁 token，切图销毁后仍可能申请帧末协程。
    // CLR 引用仍在而 Unity 对象已失效时，将这次等待结束为取消。
    [HarmonyPatch(typeof(UniTask), "WaitForEndOfFrame", new Type[] { typeof(MonoBehaviour) })]
    internal static class FowSmokeDestroyedRunnerPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(MonoBehaviour coroutineRunner, ref UniTask __result)
        {
            if (coroutineRunner is FowSmoke && coroutineRunner == null)
            {
                __result = UniTask.FromCanceled(new CancellationToken(true));
                return false;
            }

            return true;
        }
    }
}
