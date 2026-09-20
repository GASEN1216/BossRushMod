using System;
using BossRush;
using Saves;

// 只抽取生产序章的初始化 / 关闭 / 调度方法；场景查询与任务桥是替身，故事与存档引擎是真实源码。
namespace BossRush
{
    internal sealed partial class SkyIslandPreludeFlow
    {
        internal SkyIslandStoryService story;
        private bool storyReady, routeUnlocked, bundleDeployed, holdsInstrument, instrumentSampled;
        private int openedSlot = -1, jeffAttempts;
        private float nextJeffAttempt, nextTick;
        internal bool ProbeEnsure() { return EnsureStory(); }
        internal void ProbeClose() { CloseStory(); }
        internal bool ProbeReady { get { return storyReady && routeUnlocked; } }
        internal bool ProbeBundle { get { return bundleDeployed; } }
        /// <summary>零号区那具残骸与守卫要不要在场：判据是生产代码逐字抽来的。</summary>
        internal bool ProbeObjective(bool holds) { holdsInstrument = holds; return ShouldRunObjective(); }
    }
    internal static class SkyIslandOfficialQuestStory
    {
        internal static SkyIslandStoryService Published;
        internal static void SetBaseSource(SkyIslandStoryService service) { Published = service; }
    }
    internal static class SkyIslandRaidLease
    {
        internal static bool Deployed;
        internal static int Checks;
        internal static bool IsBundleDeployed() { Checks++; return Deployed; }
    }
    internal static class SceneRuntimeGate
    {
        internal static bool IsBaseHubSceneName(string name) { return name == "Base_SceneV2"; }
    }
}
namespace UnityEngine.SceneManagement
{
    internal struct Scene { internal string name; }
    internal static class SceneManager
    {
        internal static Scene GetActiveScene() { return new Scene { name = "Base_SceneV2" }; }
    }
}
internal static class SkyIslandQuestLifecycleRegression
{
    internal static void Run(Action<bool, string> check)
    {
        SavesSystem.Switch(100172);
        SkyIslandStoryData old = SkyIslandStoryRules.CreateDefault();
        old.visitedRegions = 1;
        SavesSystem.Save(SkyIslandStoryRules.StorageKey, SkyIslandStoryCodec.Encode(old));
        var flow = new SkyIslandPreludeFlow();
        // 注入一个确实发生过键写失败的门面。失败期间不能向任务桥发布，也不能下一帧跳过迁移。
        flow.story = new SkyIslandStoryService(); flow.story.Open();
        string message;
        check(flow.story.TryApply(SkyIslandStoryAction.FindOldLetter, out message), "initialization probe accepts a pending story fact");
        SavesSystem.FailKeyWrite = true;
        flow.story.Tick(true);
        check(!flow.story.CanWrite && !flow.ProbeEnsure(), "faulted story is not ready for route compatibility");
        SkyIslandStoryService retained = flow.story;
        check(!flow.ProbeEnsure() && ReferenceEquals(flow.story, retained) && SkyIslandOfficialQuestStory.Published == null,
            "a second failed initialization keeps the same owner and publishes nothing");
        SavesSystem.FailKeyWrite = false;
        UnityEngine.Time.unscaledTime += 5f;
        // 第一拍让现有恢复器接续，下一拍提交迁移。
        bool ready = flow.ProbeEnsure();
        if (!ready) ready = flow.ProbeEnsure();
        check(ready && flow.ProbeReady && flow.story.Current.SkyIslandRouteUnlocked &&
            ReferenceEquals(SkyIslandOfficialQuestStory.Published, flow.story), "write recovery completes compatibility before publishing the route");
        check(flow.story.Current.Has(SkyIslandStoryFlag.OldLetter), "initialization recovery preserves the pending story fact");
        flow.ProbeClose();
        check(!flow.ProbeReady && SkyIslandOfficialQuestStory.Published == null, "closing clears readiness and the published story");
        SavesSystem.Switch(100173);
        check(flow.ProbeEnsure() && !flow.ProbeReady, "a new empty slot cannot inherit the previous route");
        flow.ProbeClose();

        SkyIslandRaidLease.Checks = 0; SkyIslandRaidLease.Deployed = false;
        flow.Schedule(); check(!flow.ProbeBundle && SkyIslandRaidLease.Checks == 1, "schedule observes a missing bundle");
        SkyIslandRaidLease.Deployed = true;
        flow.Schedule(); check(flow.ProbeBundle && SkyIslandRaidLease.Checks == 2, "next level schedule observes a restored bundle");
        for (int i = 0; i < 100; i++) check(flow.ProbeBundle, "bundle state is stable between level schedules");
        check(SkyIslandRaidLease.Checks == 2, "reading cached bundle availability never touches the filesystem");

        // 零号区目标的在场判据：看「手上有没有航向仪」，不看剧情位。
        // 拿到就撤场，丢了（阵亡、卖掉）下一趟照旧刷，玩家不会被卡在「目标已完成但交付物没了」。
        SavesSystem.Switch(100174);
        flow.story = new SkyIslandStoryService(); flow.story.Open();
        check(!flow.ProbeObjective(false), "an untaken quest does not place the Ground Zero objective");
        string accepted;
        check(flow.story.TryApply(SkyIslandStoryAction.AcceptPrelude, out accepted), "the prelude can be accepted");
        check(flow.ProbeObjective(false), "after accepting, the objective is placed while the instrument is missing");
        check(!flow.ProbeObjective(true), "carrying the instrument takes the objective down");
        string recovered;
        check(flow.story.TryApply(SkyIslandStoryAction.RecoverPreludeInstrument, out recovered), "recovering the instrument is recorded");
        check(flow.ProbeObjective(false), "losing the instrument after recovery brings the objective back");
        string unlocked;
        check(flow.story.TryApply(SkyIslandStoryAction.UnlockRoute, out unlocked), "the route can be unlocked");
        check(!flow.ProbeObjective(false) && !flow.ProbeObjective(true), "a delivered prelude never places the objective again");
        flow.ProbeClose();
    }
}
