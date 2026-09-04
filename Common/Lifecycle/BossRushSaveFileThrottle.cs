// ============================================================================
// BossRushSaveFileThrottle.cs - 跨子系统的「每帧至多一次物理落盘」闸
// ============================================================================
// 为什么需要它：
//   Mod 里有五个各自独立的落盘协调器（Mode H / 图鉴 / 征程 / 日报 / 遗种巢），
//   每个都持有自己的 SavesSystem.SaveFile 调用点，各自只保证「本协调器每批至多一次」。
//   它们的重试驱动都挂在宿主 OnUpdate 上、并且都以「回到基地」为放行条件，
//   于是**回基地的第一帧**极易出现多个协调器在同一帧连续调 SaveFile。
//
//   官方 SavesSystem.SaveFile 每次都会 CreateBackup() 复制整份存档文件、
//   再 ES3.StoreCachedFile 同步写整档（每 5 分钟还额外 CreateIndexedBackup），
//   全部在主线程。四五次叠在同一帧上就是一次肉眼可见的卡顿。
//
// 语义：
//   - 一帧只放行第一个申请者；其余申请者被拒，各自沿用**已有的** deferred + Tick
//     重试链在后续帧补写（它们本来就为 IsSaving 准备了这条路，语义完全一致）。
//   - 强制路径（宿主销毁、切槽最后机会）必须传 force:true 绕过，否则会丢数据。
//   - no-throw：判不出帧号就一律放行，宁可多写一次也不阻塞落盘。
//
// 这不是第二套落盘机制：它只回答「这一帧轮不轮得到你」，不碰 pending、不碰 store。
// ============================================================================

using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>跨子系统的每帧物理落盘闸。静态、无状态依赖、no-throw。</summary>
    internal static class BossRushSaveFileThrottle
    {
        private static readonly object _lock = new object();

        /// <summary>上一次放行物理落盘的帧号。-1 表示本会话尚未落过盘。</summary>
        private static int _lastSaveFrame = -1;

        /// <summary>
        /// 申请在本帧做一次物理落盘。
        /// </summary>
        /// <param name="force">
        /// 强制路径（宿主销毁 / 切槽最后机会）传 true：这些时点没有「下一帧」，
        /// 被闸拦下等于永久丢数据。
        /// </param>
        /// <returns>true = 本帧可以调 SavesSystem.SaveFile。</returns>
        internal static bool TryBeginSaveFile(bool force)
        {
            try
            {
                int frame = Time.frameCount;
                lock (_lock)
                {
                    if (!force && _lastSaveFrame == frame) return false;
                    _lastSaveFrame = frame;
                    return true;
                }
            }
            catch (Exception)
            {
                // 拿不到帧号（非主线程 / 引擎未就绪）：放行。
                // 漏放行只会退回到改动前的行为，拦错则可能丢存档。
                return true;
            }
        }

        /// <summary>清空静态状态（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            lock (_lock)
            {
                _lastSaveFrame = -1;
            }
        }
    }
}
