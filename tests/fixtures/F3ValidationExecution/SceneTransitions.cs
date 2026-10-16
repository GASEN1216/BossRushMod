using System;
using System.Collections;
using System.Threading.Tasks;
using BossRush;

// 原生投票/场景任务与宿主对象是边界替身；LoadScene 和最终 Ready 判据逐字来自生产代码。
internal enum UniTaskStatus { Pending, Succeeded, Faulted }
internal struct UniTask
{
    internal Task Inner;
    internal UniTaskStatus Status { get { return !Inner.IsCompleted ? UniTaskStatus.Pending : Inner.IsFaulted ? UniTaskStatus.Faulted : UniTaskStatus.Succeeded; } }
    internal System.Runtime.CompilerServices.TaskAwaiter GetAwaiter() { return Inner.GetAwaiter(); }
    internal void Forget() { }
}
internal struct MultiSceneLocation { }
internal sealed class MultiSceneCore
{
    internal static MultiSceneCore Instance;
    internal bool IsLoading;
    internal SceneInfo SceneInfo;
    internal readonly SceneGameObject gameObject = new SceneGameObject();
}
internal sealed class SceneInfo { internal string ID; }
internal sealed class SceneLoader
{
    internal static readonly SceneLoader Instance = new SceneLoader();
    internal static bool IsSceneLoading;
    internal static string LoadingComment = "native_vote";
    internal static Task NextTask;
    internal UniTask LoadBaseScene(object location, bool click) { return new UniTask { Inner = NextTask }; }
    internal UniTask LoadScene(string id, object point, bool click, bool a, bool b, bool c, MultiSceneLocation location, bool d, bool e)
    { return new UniTask { Inner = NextTask }; }
}
internal sealed class CharacterMainControl
{
    internal static CharacterMainControl Main;
    internal readonly SceneGameObject gameObject = new SceneGameObject();
    internal ItemStatsSystem.Item CharacterItem;
    internal readonly SceneHealth Health = new SceneHealth();
    internal Teams Team = Teams.wolf;
    internal object Controller, mainDamageReceiver = new object();
    internal readonly SceneTransform transform = new SceneTransform();
    internal int PositionCalls;
    internal void SetPosition(object value) { PositionCalls++; transform.position = value; }
    internal T GetComponent<T>() where T : class { return Controller as T; }
}
internal sealed class SceneTransform { internal object position = new object(); }
internal sealed class SceneGameObject { internal bool activeInHierarchy; internal SceneManager.Scene scene; }
internal sealed class SceneHealth
{
    internal bool IsDead, Invincible, ReviveOnce;
    internal float CurrentHealth = 100, MaxHealth = 100;
    internal int HurtCalls;
    internal DamageInfo LastDamage;
    internal bool Hurt(DamageInfo damage)
    {
        HurtCalls++; LastDamage = damage;
        if (Invincible) return false;
        if (ReviveOnce) { ReviveOnce = false; CurrentHealth = 1; return true; }
        CurrentHealth = 0; IsDead = true; return true;
    }
}
internal sealed class SceneCamera { internal bool isActiveAndEnabled; }
internal sealed class LevelManager
{
    internal static LevelManager Instance;
    internal static bool AfterInit;
    internal SceneCamera GameCamera;
    internal CharacterMainControl MainCharacter;
}
internal sealed class SceneHost { internal string GetArenaSceneName() { return "Arena"; } }

internal static class SceneTransitions
{
    private static ProductionSceneCase Fresh()
    {
        ProductionSceneCase.ClosureClicks = 0;
        Time.realtimeSinceStartupAsDouble = 0;
        SceneManager.Handle = 7; SceneManager.Name = "Arena";
        SceneLoader.IsSceneLoading = false; SceneLoader.NextTask = Task.CompletedTask;
        CharacterMainControl.Main = new CharacterMainControl { CharacterItem = new ItemStatsSystem.Item() };
        CharacterMainControl.Main.gameObject.activeInHierarchy = true;
        LevelManager.Instance = new LevelManager { GameCamera = new SceneCamera { isActiveAndEnabled = true }, MainCharacter = CharacterMainControl.Main };
        LevelManager.AfterInit = true;
        MultiSceneCore.Instance = new MultiSceneCore { SceneInfo = new SceneInfo { ID = "ArenaCore" } };
        MultiSceneCore.Instance.gameObject.scene = new SceneManager.Scene { handle = 7, isLoaded = true };
        BossRushCoop.ResetSceneReady(); ProductionSceneCase.Dedicated = true;
        return new ProductionSceneCase();
    }
    private static void Drain(IEnumerator iterator)
    {
        using (var stack = new ValidationCoroutineStack(iterator))
            while (stack.MoveNext()) Time.realtimeSinceStartupAsDouble += 0.25;
    }
    internal static void Run(Action<bool, string> check)
    {
        Fresh(); BossRushCoop.IsConnected = true;
        ProductionSceneCase.MarkActualReady();
        check(!ProductionSceneCase.ActualGroupReady(), "scene: one genuinely ready peer cannot pass three-person barrier");
        BossRushCoop.ReadyPeer = "guest1"; ProductionSceneCase.MarkActualReady();
        BossRushCoop.ReadyPeer = "guest2"; LevelManager.AfterInit = false; ProductionSceneCase.MarkActualReady();
        check(!ProductionSceneCase.ActualGroupReady(), "scene: late guest without AfterInit does not publish ready");
        LevelManager.AfterInit = true; CharacterMainControl.Main.CharacterItem = null; ProductionSceneCase.MarkActualReady();
        check(!ProductionSceneCase.ActualGroupReady(), "scene: save-dependent character item must really exist");
        CharacterMainControl.Main.CharacterItem = new ItemStatsSystem.Item();
        LevelManager.Instance.MainCharacter = null; ProductionSceneCase.MarkActualReady();
        check(!ProductionSceneCase.ActualGroupReady(), "scene: LevelManager save target must be the actual Main body");
        LevelManager.Instance.MainCharacter = CharacterMainControl.Main;
        SceneLoader.IsSceneLoading = true; ProductionSceneCase.MarkActualReady();
        check(!ProductionSceneCase.ActualGroupReady(), "scene: native pending load cannot acknowledge ready");
        SceneLoader.IsSceneLoading = false; ProductionSceneCase.Dedicated = false;
        check(!ProductionSceneCase.ActualGroupReady(), "scene: normal player slot cannot participate in Dev validation barrier");
        ProductionSceneCase.Dedicated = true; ProductionSceneCase.MarkActualReady();
        check(ProductionSceneCase.ActualGroupReady(), "scene: all three actual initialized peers release the production barrier");
        ProductionSceneCase.Dedicated = false;
        check(!ProductionSceneCase.ActualGroupReady(), "scene: cached ready cannot admit a newly selected non-test slot");
        ProductionSceneCase.Dedicated = true;
        BossRushCoop.SceneReady.Clear(); BossRushCoop.ReadyPeer = "host";
        check(!ProductionSceneCase.ActualGroupReady(), "scene: new generation discards old same-map ready");
        MultiSceneCore.Instance.SceneInfo.ID = "OtherCore";
        check(!ProductionSceneCase.ActualGroupReady(), "scene: another official Core cannot satisfy old scene key");
        MultiSceneCore.Instance.SceneInfo.ID = "Base"; SceneManager.Name = "Base_SceneV2_Sub_01";
        check(ProductionSceneCase.ActualBase(), "scene: live Base Core permits original-item recovery in official boat subscene");
        MultiSceneCore.Instance.SceneInfo.ID = "Raid"; SceneManager.Name = "Base_SceneV2";
        check(!ProductionSceneCase.ActualBase(), "scene: Base-looking active name cannot grant recovery in raid Core");
        MultiSceneCore.Instance.SceneInfo.ID = "Base"; MultiSceneCore.Instance.IsLoading = true;
        check(!ProductionSceneCase.ActualBase(), "scene: loading Base cannot grant recovery");
        MultiSceneCore.Instance.IsLoading = false;
        MultiSceneCore.Instance.gameObject.scene = new SceneManager.Scene { handle = 7, isLoaded = false };
        check(!ProductionSceneCase.ActualBase(), "scene: unloaded Base Core cannot grant recovery");

        var multiplayer = Fresh(); BossRushCoop.IsConnected = true;
        using (var stack = new ValidationCoroutineStack(multiplayer.Run()))
        {
            check(stack.MoveNext(), "scene: three-player transition starts with original official entry");
            SceneManager.Handle = 8;
            check(stack.MoveNext() && multiplayer.Results.Count == 0, "scene: host target is ready but slow guests must not be declared PASS");
            BossRushCoop.ReadyPeer = "guest1"; ProductionSceneCase.MarkActualReady();
            BossRushCoop.ReadyPeer = "host";
            check(stack.MoveNext(), "scene: second peer arrival still waits for third");
            BossRushCoop.ReadyPeer = "guest2"; ProductionSceneCase.MarkActualReady();
            BossRushCoop.ReadyPeer = "host";
            check(!stack.MoveNext() && multiplayer._operationSucceeded, "scene: actual LoadScene coroutine only passes after all three ready facts");
        }

        var unchanged = Fresh(); Drain(unchanged.Run());
        check(unchanged.Results.Count == 1 && unchanged.Results[0] == "FAIL"
            && unchanged._operationReason == "scene_instance_did_not_change" && ProductionSceneCase.ClosureClicks == 0,
            "scene: original co-op CompletedTask vote with ready old map cannot pass same-map reload");

        var reloaded = Fresh();
        using (var stack = new ValidationCoroutineStack(reloaded.Run()))
        {
            check(stack.MoveNext() && reloaded.Results.Count == 0, "scene: completed native vote waits for the real transition");
            Time.realtimeSinceStartupAsDouble = 0.5; SceneLoader.IsSceneLoading = true;
            check(stack.MoveNext(), "scene: loading the same map cannot pass before its instance changes");
            SceneManager.Handle = 8; SceneLoader.IsSceneLoading = false; LevelManager.AfterInit = false;
            check(stack.MoveNext(), "scene: changed handle still waits for actual initialization");
            LevelManager.AfterInit = true; reloaded._sceneClicksFed = 2;
            check(!stack.MoveNext() && reloaded._operationSucceeded && reloaded._lastSceneClicksFed == 2
                && reloaded.Metrics[0].Contains("previous_handle=7,current_handle=8"),
                "scene: new same-map instance passes only after init and includes delayed continue clicks");
        }
        var wrong = Fresh();
        using (var stack = new ValidationCoroutineStack(wrong.Run()))
        {
            check(stack.MoveNext(), "scene: wrong-target probe waits initially");
            SceneManager.Handle = 9; SceneManager.Name = "Wrong";
            while (stack.MoveNext()) Time.realtimeSinceStartupAsDouble += 0.25;
        }
        check(wrong.Results[0] == "FAIL" && wrong._operationReason == "scene_loaded_but_runtime_not_ready",
            "scene: a new unrelated scene cannot pass target readiness");

        var pending = Fresh(); var completion = new TaskCompletionSource<bool>(); SceneLoader.NextTask = completion.Task;
        using (var stack = new ValidationCoroutineStack(pending.Run(true)))
        {
            check(stack.MoveNext(), "scene: base return waits for native task");
            SceneManager.Handle = 10; SceneManager.Name = "Base";
            check(stack.MoveNext() && pending.Results.Count == 0, "scene: ready base cannot bypass unfinished native task");
            completion.SetResult(true);
            check(!stack.MoveNext() && pending._operationSucceeded && ProductionSceneCase.ClosureClicks == 2,
                "scene: complete native task and ready new base pass together after polling actual Closure continue helper");
        }
    }
}
