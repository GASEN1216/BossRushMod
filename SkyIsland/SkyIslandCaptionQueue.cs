// ============================================================================
// SkyIslandCaptionQueue.cs - 天空岛中下方字幕的排队策略（纯逻辑，无 Unity 依赖）
// ============================================================================
// 从 SkyIslandHud 拆出来，是为了能在隔离回归里直接执行它（tests/fixtures/SkyIslandHudPolicy）：
// 优先级、去重、队满丢谁这几条规则写错了既不报错也不掉帧，只会在 Boss 战里把机制提示吞掉。
//
// 【为什么要分级】
//   噬风的相位台词是机制提示：说完 1.4 秒（SkyIslandStormBoss.PulseTelegraph）后圈内吃满伤害。
//   旧写法一律按到达顺序排队、队满丢最旧——一条「航路已清理」正在播时，「离开它脚下的那一圈」
//   要等上一条停满 1.6 秒再淡出 0.5 秒，玩家读到时第一波已经炸完；紧跟着再来三条普通字幕，
//   它还会被直接挤出队列。主流做法是警示插队、打断正在播的普通字幕。
//
// 【规则】
//   1. 去重：与正在播的同一句 → 让 HUD 把停留重新拉满；与队里的同一句 → 不再排
//      （警示会把队里同句的普通字幕升级成警示并插队）。
//   2. 排序：警示排在全部普通字幕之前、其它警示之后；同级先来先播。
//   3. 队满：先丢最旧的普通字幕；队里全是警示时，新来的普通字幕直接丢，新来的警示挤掉最旧的警示。
//   4. 打断：正在播的是普通字幕、新来的是警示 → 通知 HUD 让当前这条快速淡出。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>字幕排队策略。只管「谁先播、谁被丢、要不要打断」，不碰任何显示对象。</summary>
    internal sealed class SkyIslandCaptionQueue
    {
        internal struct Entry
        {
            internal string Text;
            internal bool Warning;
        }

        internal enum Admission
        {
            /// <summary>没有进队：空文案、与队里重复，或队满且被规则 3 丢弃。</summary>
            Dropped,
            Queued,
            /// <summary>与正在播的是同一句：调用方把它的停留重新拉满，不再排一遍。</summary>
            RefreshShowing
        }

        private readonly List<Entry> pending;
        private readonly int limit;

        internal SkyIslandCaptionQueue(int limit)
        {
            this.limit = Math.Max(1, limit);
            pending = new List<Entry>(this.limit + 1);
        }

        internal int Count { get { return pending.Count; } }

        internal Entry this[int index] { get { return pending[index]; } }

        /// <param name="showing">正在播的那条文案；没有在播时传 null。</param>
        /// <param name="showingWarning">正在播的那条是不是警示。</param>
        /// <param name="preempt">为 true 时，调用方应让正在播的普通字幕立即转入快速淡出。</param>
        internal Admission Admit(string text, bool warning, string showing, bool showingWarning, out bool preempt)
        {
            preempt = false;
            if (string.IsNullOrEmpty(text)) return Admission.Dropped;
            if (showing != null && string.Equals(showing, text, StringComparison.Ordinal))
                return Admission.RefreshShowing;
            for (int i = 0; i < pending.Count; i++)
            {
                if (!string.Equals(pending[i].Text, text, StringComparison.Ordinal)) continue;
                if (!warning || pending[i].Warning) return Admission.Dropped;
                // 同一句先以普通身份排着、现在作为警示再来：升级并按警示的位置重新插队。
                pending.RemoveAt(i);
                break;
            }
            if (pending.Count >= limit)
            {
                int victim = FirstNormalIndex();
                if (victim < 0)
                {
                    if (!warning) return Admission.Dropped;
                    victim = 0;
                }
                pending.RemoveAt(victim);
            }
            var entry = new Entry { Text = text, Warning = warning };
            if (warning) pending.Insert(WarningInsertIndex(), entry);
            else pending.Add(entry);
            preempt = warning && showing != null && !showingWarning;
            return Admission.Queued;
        }

        internal bool TryDequeue(out Entry entry)
        {
            if (pending.Count == 0)
            {
                entry = default(Entry);
                return false;
            }
            entry = pending[0];
            pending.RemoveAt(0);
            return true;
        }

        internal void Clear() { pending.Clear(); }

        private int FirstNormalIndex()
        {
            for (int i = 0; i < pending.Count; i++)
                if (!pending[i].Warning) return i;
            return -1;
        }

        /// <summary>警示的插入位：已有警示之后、第一条普通字幕之前。</summary>
        private int WarningInsertIndex()
        {
            int index = FirstNormalIndex();
            return index < 0 ? pending.Count : index;
        }
    }
}
