using System;
using System.Collections.Generic;
using BossRush;

internal static class Program
{
    private static int count;

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL " + name);
        count++;
    }

    private static SkyIslandCaptionQueue.Admission Admit(SkyIslandCaptionQueue queue, string text, bool warning,
        string showing = null, bool showingWarning = false)
    {
        bool preempt;
        return queue.Admit(text, warning, showing, showingWarning, out preempt);
    }

    private static string Order(SkyIslandCaptionQueue queue)
    {
        var parts = new List<string>();
        for (int i = 0; i < queue.Count; i++) parts.Add((queue[i].Warning ? "!" : "") + queue[i].Text);
        return string.Join(",", parts.ToArray());
    }

    private static void Main()
    {
        // 1. 普通字幕先来先播。
        var q = new SkyIslandCaptionQueue(3);
        Admit(q, "A", false); Admit(q, "B", false); Admit(q, "C", false);
        Check(Order(q) == "A,B,C", "normal captions keep arrival order");

        // 2. 去重：队里已有同一句不再排；与正在播的同一句要求刷新停留。
        q = new SkyIslandCaptionQueue(3);
        Admit(q, "A", false);
        Check(Admit(q, "A", false) == SkyIslandCaptionQueue.Admission.Dropped && q.Count == 1, "duplicate in queue is dropped");
        Check(Admit(q, "S", false, "S", false) == SkyIslandCaptionQueue.Admission.RefreshShowing && q.Count == 1,
            "duplicate of the showing caption refreshes it instead of queueing");
        Check(Admit(q, null, true) == SkyIslandCaptionQueue.Admission.Dropped && Admit(q, "", false) == SkyIslandCaptionQueue.Admission.Dropped,
            "empty captions never enter the queue");

        // 3. 警示插在全部普通字幕之前、已有警示之后。
        q = new SkyIslandCaptionQueue(4);
        Admit(q, "A", false); Admit(q, "B", false); Admit(q, "W1", true); Admit(q, "W2", true);
        Check(Order(q) == "!W1,!W2,A,B", "warnings jump ahead of normals but keep their own order");

        // 4. 队满先丢最旧的普通字幕，警示照样进队。
        q = new SkyIslandCaptionQueue(3);
        Admit(q, "A", false); Admit(q, "B", false); Admit(q, "C", false);
        Check(Admit(q, "W", true) == SkyIslandCaptionQueue.Admission.Queued && Order(q) == "!W,B,C",
            "a full queue evicts its oldest normal caption for a warning");
        Check(Admit(q, "D", false) == SkyIslandCaptionQueue.Admission.Queued && Order(q) == "!W,C,D",
            "a full queue evicts its oldest normal caption for a newer normal caption");

        // 5. 全是警示：普通字幕直接丢；新警示挤掉最旧的警示。
        q = new SkyIslandCaptionQueue(3);
        Admit(q, "W1", true); Admit(q, "W2", true); Admit(q, "W3", true);
        Check(Admit(q, "N", false) == SkyIslandCaptionQueue.Admission.Dropped && Order(q) == "!W1,!W2,!W3",
            "a normal caption never evicts a warning");
        Check(Admit(q, "W4", true) == SkyIslandCaptionQueue.Admission.Queued && Order(q) == "!W2,!W3,!W4",
            "a warning evicts the oldest warning only when nothing else can go");

        // 6. 打断：只有「正在播普通字幕 + 新来警示」才打断。
        q = new SkyIslandCaptionQueue(3);
        bool preempt;
        q.Admit("W", true, "normal showing", false, out preempt);
        Check(preempt, "warning preempts a showing normal caption");
        q.Admit("W2", true, "warning showing", true, out preempt);
        Check(!preempt, "warning never preempts another warning");
        q.Admit("N", false, "normal showing", false, out preempt);
        Check(!preempt, "normal caption never preempts");
        q.Admit("W3", true, null, false, out preempt);
        Check(!preempt, "nothing to preempt when nothing is showing");

        // 7. 升级：队里同一句的普通字幕以警示身份再来时插队。
        q = new SkyIslandCaptionQueue(3);
        Admit(q, "A", false); Admit(q, "S", false);
        Check(Admit(q, "S", true) == SkyIslandCaptionQueue.Admission.Queued && Order(q) == "!S,A", "normal duplicate upgrades to warning");
        Check(Admit(q, "S", true) == SkyIslandCaptionQueue.Admission.Dropped && Order(q) == "!S,A", "warning duplicate is dropped");

        // 8. 出队顺序即播放顺序。
        SkyIslandCaptionQueue.Entry entry;
        Check(q.TryDequeue(out entry) && entry.Text == "S" && entry.Warning, "dequeue returns the head warning");
        Check(q.TryDequeue(out entry) && entry.Text == "A" && !entry.Warning, "then the normal caption");
        Check(!q.TryDequeue(out entry), "empty queue yields nothing");

        // 9. 随机序列上的不变式：容量不超、警示永远在普通字幕前面、不含重复、
        //    有普通字幕在队时警示绝不被丢。固定种子，跨机器结果一致。
        var random = new Random(20260910);
        for (int round = 0; round < 200; round++)
        {
            int limit = 1 + random.Next(4);
            q = new SkyIslandCaptionQueue(limit);
            for (int step = 0; step < 60; step++)
            {
                if (random.Next(4) == 0) { q.TryDequeue(out entry); continue; }
                bool warning = random.Next(3) == 0;
                string text = "T" + random.Next(6);
                bool hadNormal = false, alreadyQueued = false;
                for (int i = 0; i < q.Count; i++)
                {
                    if (!q[i].Warning) hadNormal = true;
                    if (q[i].Text == text) alreadyQueued = true;
                }
                SkyIslandCaptionQueue.Admission result = Admit(q, text, warning);
                if (warning && !alreadyQueued && hadNormal)
                    Check(result == SkyIslandCaptionQueue.Admission.Queued, "warning admitted whenever a normal caption can make room");
                Check(q.Count <= limit, "queue never exceeds its limit");
                bool seenNormal = false;
                var seen = new HashSet<string>();
                for (int i = 0; i < q.Count; i++)
                {
                    if (!q[i].Warning) seenNormal = true;
                    else Check(!seenNormal, "no warning sits behind a normal caption");
                    Check(seen.Add(q[i].Text), "queue holds no duplicate text");
                }
            }
        }

        Console.WriteLine("SkyIslandHudPolicy production-linked PASS assertions=" + count);
    }
}
