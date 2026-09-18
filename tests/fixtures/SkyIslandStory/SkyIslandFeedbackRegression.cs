using System;
using System.Collections.Generic;
using BossRush;
using UnityEngine;

namespace BossRush
{
    // 只替换 Unity 物体的创建和表现资源；重建决策、旗标、顺序与文案均逐字运行生产方法。
    internal static class BossRushUIColors
    { internal const int Success = 1, WarningText = 2, Accent = 3; }

    internal sealed partial class SkyIslandFeedbackHarness
    {
        private sealed class StoryView { internal SkyIslandStoryData Current; }
        private readonly StoryView story = new StoryView();
        private readonly List<GameObject> feedback = new List<GameObject>();
        internal int Builds;
        internal GameObject[] Objects { get { return feedback.ToArray(); } }
        internal void Refresh(SkyIslandStoryData data) { story.Current = data; RebuildFeedback(); }
        private void Beacon(string marker, string title, int color, Func<string> body = null, Func<object> choices = null)
        { Builds++; feedback.Add(new GameObject(marker + " " + title)); }
        private string ZhelingBadgeText() { return "badge"; }
        private object CrewChoices() { return null; }
    }
}

internal static class SkyIslandFeedbackRegression
{
    internal static void Run(Action<bool, string> check)
    {
        // 这张期望表独立于生产掩码：新增世界事实漏进掩码时，交叉组合会转红。
        var visible = new[] { SkyIslandStoryFlag.WindBeacon, SkyIslandStoryFlag.StarLamp,
            SkyIslandStoryFlag.Telescope, SkyIslandStoryFlag.PlantingDelivered, SkyIslandStoryFlag.StormSlain,
            SkyIslandStoryFlag.ZhelingDefeated, SkyIslandStoryFlag.Ending };
        var questOnly = SkyIslandStoryFlag.BeaconQuestAccepted | SkyIslandStoryFlag.BeaconQuestDelivered
            | SkyIslandStoryFlag.BellCourtQuestAccepted | SkyIslandStoryFlag.BellCourtQuestDelivered
            | SkyIslandStoryFlag.HomecomingQuestAccepted | SkyIslandStoryFlag.HomecomingQuestDelivered;
        for (int bits = 0; bits < (1 << visible.Length); bits++)
        {
            L10n.IsChinese = true;
            var data = new SkyIslandStoryData();
            int expected = 0;
            for (int i = 0; i < visible.Length; i++)
                if ((bits & (1 << i)) != 0)
                { data.flags |= (int)visible[i]; expected += visible[i] == SkyIslandStoryFlag.Ending ? 2 : 1; }
            var view = new SkyIslandFeedbackHarness();
            view.Refresh(data);
            check(view.Builds == expected, "all existing world feedback is preserved: " + bits);
            var original = view.Objects;
            data.flags |= (int)questOnly;
            for (int i = 0; i < 60; i++) view.Refresh(data);
            check(view.Builds == expected, "quest hand-ins and steady frames do not recreate world objects: " + bits);
            for (int i = 0; i < original.Length; i++)
                check(ReferenceEquals(original[i], view.Objects[i]) && !original[i].Destroyed, "existing visual object retained");
            L10n.IsChinese = false;
            view.Refresh(data); view.Refresh(data);
            check(view.Builds == expected * 2, "language change refreshes world text exactly once: " + bits);
            foreach (var item in original) check(item.Destroyed, "language rebuild releases replaced objects");
            // 移除所有可见事实也必须清理（Dev 快照还原 / 状态回退）。
            data.flags = 0;
            view.Refresh(data);
            check(view.Objects.Length == 0, "clearing world facts clears their objects");
        }
        L10n.IsChinese = true;
        var changed = new SkyIslandStoryData { flags = (int)SkyIslandStoryFlag.WindBeacon };
        var simultaneous = new SkyIslandFeedbackHarness();
        simultaneous.Refresh(changed);
        changed.flags |= (int)SkyIslandStoryFlag.StarLamp;
        L10n.IsChinese = false;
        simultaneous.Refresh(changed); simultaneous.Refresh(changed);
        check(simultaneous.Builds == 3, "simultaneous language and story update creates the two beacons once");
        L10n.IsChinese = true;
    }
}
