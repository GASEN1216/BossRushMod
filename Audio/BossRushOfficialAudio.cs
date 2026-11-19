// ============================================================================
// BossRushOfficialAudio.cs - 官方音频入口的唯一反射绑定
// ============================================================================
// 为什么走反射：官方 AudioManager.Post / PostCustomSFX 返回 FMOD 的 EventInstance?，
//   AudioObject.StopAll 的参数是 FMOD 的 STOP_MODE。编译清单不引用 FMOD 程序集，直接调用就编不过，
//   所以全 Mod 调官方音频都经这里反射（2026-11-19 统一：此前 17 处各自 GetMethod，
//   PlaySoundEffect 的委托一直绑不上官方的非 void 返回值，每次都退回重新 GetMethod）。
//
// 约定：
//   - 首次用到时解析一次并长期持有。MethodInfo 与 STOP_MODE 装箱值是官方程序集的元数据，
//     不随场景、存档或 Mod 重新启用变化，所以没有 ResetStaticCaches；
//   - 官方返回值原样按 object 交出：EventInstance? 有值时装箱成 EventInstance，没有时为 null；
//   - 一律不抛。入口缺失返回 null / false；官方方法抛出时 Try* 经 error 交出那次异常
//     （就是 MethodInfo.Invoke 抛出的那个对象，Message 与调用方从前自己 Invoke 时相同），
//     调用方据此保持原有的告警文案与降级时机（例如云蚋嗡声出错后不再重试）；
//   - 只管「调官方」。文件在不在、挂在哪个发声体上、循环与否、什么时候停，留在各调用点。
// ============================================================================

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BossRush
{
    internal static class BossRushOfficialAudio
    {
        private static bool resolved;
        private static MethodInfo postCustomSfx;   // AudioManager.PostCustomSFX(string, GameObject, bool)
        private static MethodInfo postAt;          // AudioManager.Post(string, GameObject)
        private static MethodInfo post2D;          // AudioManager.Post(string)：UI 音效，不挂发声体
        private static MethodInfo stopAll;         // AudioObject.StopAll(STOP_MODE)，官方是 internal
        private static object stopImmediate;       // STOP_MODE.IMMEDIATE（= 1）

        // UI 音效在悬停 / 点击时调，参数数组复用一个（只在主线程用；Invoke 不留存参数数组）。
        private static readonly object[] post2DArgs = new object[1];

        // FMOD EventInstance 的方法按句柄类型解析一次（过场音频压音量 / 停止、星阙法杖调音高）。
        private static Type eventType;
        private static MethodInfo eventSetVolume, eventSetPitch, eventStop;
        private static object eventStopAllowFadeOut;  // STOP_MODE.ALLOWFADEOUT（= 0）

        /// <summary>官方 PostCustomSFX(string, GameObject, bool) 是否可用。</summary>
        internal static bool HasPostCustomSfx { get { Resolve(); return postCustomSfx != null; } }

        /// <summary>官方 Post(string, GameObject) 是否可用。</summary>
        internal static bool HasPost { get { Resolve(); return postAt != null; } }

        /// <summary>官方 AudioObject.StopAll 是否可用。</summary>
        internal static bool HasStopAll { get { Resolve(); return stopAll != null; } }

        /// <summary>播一个 Mod 自带的音频文件；返回官方事件实例（装箱）或 null，不抛。</summary>
        internal static object PostCustomSfx(string filePath, GameObject target, bool loop)
        {
            object instance;
            Exception error;
            TryPostCustomSfx(filePath, target, loop, out instance, out error);
            return instance;
        }

        /// <summary>
        /// 播一个 Mod 自带的音频文件。返回 true 表示官方方法正常返回（instance 可能为 null，例如官方 AudioManager
        /// 还没实例）；false 时 error 为 null 表示入口缺失，非 null 表示官方方法抛出的那次异常。
        /// </summary>
        internal static bool TryPostCustomSfx(string filePath, GameObject target, bool loop, out object instance, out Exception error)
        {
            Resolve();
            return Invoke(postCustomSfx, null, new object[] { filePath, target, loop }, out instance, out error);
        }

        /// <summary>在发声体上播官方 FMOD 事件；返回官方事件实例（装箱）或 null，不抛。</summary>
        internal static object Post(string eventName, GameObject target)
        {
            object instance;
            Exception error;
            TryPost(eventName, target, out instance, out error);
            return instance;
        }

        /// <summary>在发声体上播官方 FMOD 事件；返回值约定同 <see cref="TryPostCustomSfx"/>。</summary>
        internal static bool TryPost(string eventName, GameObject target, out object instance, out Exception error)
        {
            Resolve();
            return Invoke(postAt, null, new object[] { eventName, target }, out instance, out error);
        }

        /// <summary>播官方 UI 事件（不挂发声体，对应官方 Post(string)）；不抛，失败静默。</summary>
        internal static void Post(string eventName)
        {
            Resolve();
            if (post2D == null) return;
            post2DArgs[0] = eventName;
            try
            {
                post2D.Invoke(null, post2DArgs);
            }
            catch (Exception)
            {
                // UI 音效不是关键路径：没有 AudioManager 实例或 FMOD 未就绪时静默跳过
            }
            finally
            {
                post2DArgs[0] = null;
            }
        }

        /// <summary>
        /// 立即停掉官方 AudioObject 手里的全部事件（STOP_MODE.IMMEDIATE）。用 ReferenceEquals 判空：
        /// 发声体已被 Unity 销毁时，StopAll 仍只遍历托管的 events 列表调 FMOD，照样停得掉。
        /// 返回值约定同 <see cref="TryPostCustomSfx"/>；audio 为 null 时什么也不做，返回 false 且 error 为 null。
        /// </summary>
        internal static bool TryStopAll(Duckov.AudioObject audio, out Exception error)
        {
            Resolve();
            error = null;
            if (ReferenceEquals(audio, null) || stopAll == null) return false;
            object ignored;
            return Invoke(stopAll, audio, new[] { stopImmediate }, out ignored, out error);
        }

        /// <summary>调整一次官方事件的音量（EventInstance.setVolume）。返回值约定同 <see cref="TryPostCustomSfx"/>。</summary>
        internal static bool TrySetEventVolume(object eventInstance, float volume, out Exception error)
        {
            object ignored;
            if (!ResolveEvent(eventInstance, out error)) return false;
            return Invoke(eventSetVolume, eventInstance, new object[] { volume }, out ignored, out error);
        }

        /// <summary>调整一次官方事件的音高（EventInstance.setPitch）。返回值约定同 <see cref="TryPostCustomSfx"/>。</summary>
        internal static bool TrySetEventPitch(object eventInstance, float pitch, out Exception error)
        {
            object ignored;
            if (!ResolveEvent(eventInstance, out error)) return false;
            return Invoke(eventSetPitch, eventInstance, new object[] { pitch }, out ignored, out error);
        }

        /// <summary>按事件自带的淡出停掉一次官方事件（EventInstance.stop(STOP_MODE.ALLOWFADEOUT)）。</summary>
        internal static bool TryStopEvent(object eventInstance, out Exception error)
        {
            object ignored;
            if (!ResolveEvent(eventInstance, out error)) return false;
            return Invoke(eventStop, eventInstance, new[] { eventStopAllowFadeOut }, out ignored, out error);
        }

        private static bool Invoke(MethodInfo method, object target, object[] args, out object result, out Exception error)
        {
            result = null;
            error = null;
            if (method == null) return false;
            try
            {
                result = method.Invoke(target, args);
                return true;
            }
            catch (Exception e)
            {
                error = e;
                return false;
            }
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            try
            {
                ResolveOfficialMembers();
            }
            catch (Exception e)
            {
                // 官方类型缺失（TypeLoadException）或签名变了：整体视为入口缺失，各调用点按原样静默降级。
                postCustomSfx = postAt = post2D = stopAll = null;
                stopImmediate = null;
                ModBehaviour.DevLog("[BossRushOfficialAudio] [WARNING] 官方音频入口解析失败: " + e.Message);
            }
        }

        // 单独成方法且禁止内联：官方类型在 JIT 编译这里时才加载，加载失败的异常落在 Resolve 的 try 里。
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ResolveOfficialMembers()
        {
            Type manager = typeof(Duckov.AudioManager);
            postCustomSfx = manager.GetMethod("PostCustomSFX", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(GameObject), typeof(bool) }, null);
            postAt = manager.GetMethod("Post", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(GameObject) }, null);
            post2D = manager.GetMethod("Post", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string) }, null);
            MethodInfo stop = typeof(Duckov.AudioObject).GetMethod("StopAll", BindingFlags.NonPublic | BindingFlags.Instance);
            if (stop != null)
            {
                stopImmediate = Enum.ToObject(stop.GetParameters()[0].ParameterType, 1);
                stopAll = stop;
            }
        }

        /// <summary>
        /// 按句柄类型解析 setVolume / setPitch / stop(STOP_MODE)。同一类型只解析一次（与原过场音频的解析口径相同：
        /// 先记下类型再逐个查，查的过程抛出时本次返回 false）。句柄为 null 时返回 false 且 error 为 null。
        /// </summary>
        private static bool ResolveEvent(object eventInstance, out Exception error)
        {
            error = null;
            if (eventInstance == null) return false;
            Type type = eventInstance.GetType();
            if (type == eventType) return true;
            try
            {
                eventType = type;
                eventSetVolume = type.GetMethod("setVolume", new[] { typeof(float) });
                eventSetPitch = type.GetMethod("setPitch", new[] { typeof(float) });
                eventStop = null;
                eventStopAllowFadeOut = null;
                MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    ParameterInfo[] parameters = methods[i].GetParameters();
                    if (methods[i].Name == "stop" && parameters.Length == 1 && parameters[0].ParameterType.IsEnum)
                    {
                        eventStop = methods[i];
                        eventStopAllowFadeOut = Enum.ToObject(parameters[0].ParameterType, 0);
                        break;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                error = e;
                return false;
            }
        }
    }
}
