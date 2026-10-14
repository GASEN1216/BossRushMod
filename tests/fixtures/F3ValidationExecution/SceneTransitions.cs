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
internal sealed class SceneGameObject { internal bool activeInHierarchy; }
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
        LevelManager.Instance = new LevelManager { GameCamera = new SceneCamera { isActiveAndEnabled = true } };
        LevelManager.AfterInit = true;
        return new ProductionSceneCase();
    }
    private static void Drain(IEnumerator iterator)
    {
        using (var stack = new ValidationCoroutineStack(iterator))
            while (stack.MoveNext()) Time.realtimeSinceStartupAsDouble += 0.25;
    }
    internal static void Run(Action<bool, string> check)
    {
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
