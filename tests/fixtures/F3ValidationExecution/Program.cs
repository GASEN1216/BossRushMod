using System;
using System.Collections;
using System.Collections.Generic;
using BossRush;

internal static class Program
{
    private static int assertions;
    private static readonly List<string> cleanup = new List<string>();

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception("FAIL: " + message);
    }

    private static IEnumerator Parent(bool fail)
    {
        try { yield return Child(fail); }
        finally { cleanup.Add("parent"); }
    }

    private static IEnumerator Child(bool fail)
    {
        try
        {
            yield return "frame";
            if (fail) throw new InvalidOperationException("nested_failure");
            yield return "last";
        }
        finally { cleanup.Add("child"); }
    }

    private static IEnumerator Empty() { yield break; }
    private static IEnumerator ManyChildren()
    {
        for (int i = 0; i < 400; i++) yield return Empty();
        yield return "done";
    }

    private static IEnumerator Wrap(IEnumerator child)
    {
        try { yield return child; }
        finally { cleanup.Add("wrapper"); }
    }

    private sealed class BrokenEnumerator : IEnumerator, IDisposable
    {
        internal bool ThrowOnCurrent, ThrowOnDispose, Cycle;
        internal int Disposals;
        public object Current
        {
            get
            {
                if (ThrowOnCurrent) throw new InvalidOperationException("current_failed");
                return Cycle ? (object)this : null;
            }
        }
        public bool MoveNext() { return true; }
        public void Reset() { throw new NotSupportedException(); }
        public void Dispose()
        {
            Disposals++;
            if (ThrowOnDispose) throw new InvalidOperationException("dispose_failed");
        }
    }

    private static void Main()
    {
        cleanup.Clear();
        using (var stack = new ValidationCoroutineStack(Parent(false)))
        {
            Check(stack.MoveNext() && (string)stack.Current == "frame", "nested yield reaches caller");
            Check(stack.MoveNext() && (string)stack.Current == "last", "second child yield preserved");
            Check(!stack.MoveNext(), "all iterators finish");
            Check(string.Join(",", cleanup) == "child,parent", "normal exit finalizes leaf before parent");
        }
        Check(cleanup.Count == 2, "normal disposal is idempotent");

        cleanup.Clear();
        var cancelled = new ValidationCoroutineStack(Parent(false));
        Check(cancelled.MoveNext(), "cancellable chain entered");
        cancelled.Dispose();
        cancelled.Dispose();
        Check(string.Join(",", cleanup) == "child,parent", "cancel or timeout unwinds every finally once");
        Check(!cancelled.MoveNext() && cancelled.Current == null, "disposed chain cannot resume");

        cleanup.Clear();
        var failed = new ValidationCoroutineStack(Parent(true));
        Check(failed.MoveNext(), "failing child entered");
        bool caught = false;
        try { failed.MoveNext(); }
        catch (InvalidOperationException e) { caught = e.Message == "nested_failure"; }
        Check(caught, "nested failure reaches runner");
        failed.Dispose();
        Check(string.Join(",", cleanup) == "child,parent", "failure also runs suspended parent finally");

        cleanup.Clear();
        var broken = new BrokenEnumerator { ThrowOnCurrent = true, ThrowOnDispose = true };
        var brokenStack = new ValidationCoroutineStack(Wrap(broken));
        caught = false;
        try { brokenStack.MoveNext(); }
        catch (InvalidOperationException e) { caught = e.Message == "current_failed"; }
        Check(caught, "Current getter failures are observable");
        caught = false;
        try { brokenStack.Dispose(); }
        catch (AggregateException e) { caught = e.InnerExceptions.Count == 1; }
        Check(caught && broken.Disposals == 1, "dispose failures stay observable");
        Check(cleanup.Count == 1 && cleanup[0] == "wrapper", "bad child disposer cannot skip parent cleanup");
        brokenStack.Dispose();
        Check(broken.Disposals == 1, "failed disposer is not retried");

        var cycle = new BrokenEnumerator { Cycle = true };
        using (var stack = new ValidationCoroutineStack(cycle))
        {
            caught = false;
            try { stack.MoveNext(); }
            catch (InvalidOperationException) { caught = true; }
            Check(caught, "self-yield rejected instead of hanging game");
        }
        Check(cycle.Disposals == 1, "cycle owner disposed once");

        using (var stack = new ValidationCoroutineStack(ManyChildren()))
        {
            int frames = 0;
            bool sawDone = false;
            while (stack.MoveNext())
            {
                frames++;
                if ((string)stack.Current == "done") sawDone = true;
                Check(frames < 30, "bounded synchronous work eventually completes");
            }
            Check(frames > 1 && sawDone, "synchronous child sequence spreads work across frames");
        }
        cleanup.Clear();
        var isolated = new ProductionCase();
        isolated.OnReclaim = delegate { Check(cleanup.Contains("parent"), "production wrapper finalizes before reclaim"); };
        using (var stack = new ValidationCoroutineStack(isolated.Run(delegate { return Parent(true); })))
            while (stack.MoveNext()) { }
        Check(isolated.Reclaimed && isolated.Results.Contains("CASE_UNHANDLED:FAIL"), "production wrapper records nested exception and reclaims");

        cleanup.Clear();
        var cancelledCase = new ProductionCase();
        using (var stack = new ValidationCoroutineStack(cancelledCase.Run(delegate { return Parent(false); })))
        {
            Check(stack.MoveNext(), "production wrapper entered before cancellation");
            cancelledCase.Cancelled = true;
            while (stack.MoveNext()) { }
        }
        Check(string.Join(",", cleanup) == "child,parent", "production wrapper cancellation runs parent finally");

        cleanup.Clear();
        var disposeCase = new ProductionCase();
        var disposeError = new BrokenEnumerator { ThrowOnCurrent = true, ThrowOnDispose = true };
        using (var stack = new ValidationCoroutineStack(disposeCase.Run(delegate { return Wrap(disposeError); })))
            while (stack.MoveNext()) { }
        Check(disposeCase.Reclaimed && disposeCase.Results.Contains("CASE_DISPOSE:FAIL"), "bad disposer stays isolated and reclaim proceeds");
        Check(cleanup.Contains("wrapper"), "production error boundary preserves suspended parent cleanup");

        var factoryCase = new ProductionCase();
        using (var stack = new ValidationCoroutineStack(factoryCase.Run(delegate { throw new Exception("factory failed"); })))
            while (stack.MoveNext()) { }
        Check(factoryCase.Reclaimed && factoryCase.Results.Contains("CASE:FAIL"), "factory exception records failure and reclaims");

        SkyIslandShell();
        Console.WriteLine("PASS: " + assertions + " assertions; production coroutine stack, main and Sky Island case wrappers, no Unity smoke");
    }

    // ---- 2026-09-14: the Sky Island shell (RunSkyIslandSync / RunSkyIslandCase / SkyIslandSessionStillValid / RunSyncCase) ----
    private static ProductionSkyIslandCase Island(bool session, bool ready, int handle, int startedHandle)
    {
        var shell = new ProductionSkyIslandCase();
        if (session) shell.Session = new SkyIslandSession { IsReady = ready, ValidationScene = new SceneStub { handle = handle } };
        shell.Begin(startedHandle);
        return shell;
    }

    private static void Drain(IEnumerator routine)
    {
        using (var stack = new ValidationCoroutineStack(routine))
            while (stack.MoveNext()) { }
    }

    private static void SkyIslandShell()
    {
        bool ran = false;
        ProductionSkyIslandCase.SyncValidation never = (out string m, out string r) => { ran = true; m = "x"; r = null; return true; };

        var closed = Island(false, true, 7, 7);
        closed.Sync("SKY_X", never);
        Check(closed.Results.Count == 1 && closed.Results[0] == "SKY_X:SKIP" && closed.Reasons[0] == "island_session_closed" && !ran,
            "island sync case: session gone -> SKIP island_session_closed and the case body never runs");
        var notReady = Island(true, false, 7, 7);
        notReady.Sync("SKY_X", never);
        Check(notReady.Results[0] == "SKY_X:SKIP" && notReady.Reasons[0] == "island_session_not_ready" && !ran,
            "island sync case: returning / dead session -> SKIP island_session_not_ready");
        var changed = Island(true, true, 8, 7);
        changed.Sync("SKY_X", never);
        Check(changed.Results[0] == "SKY_X:SKIP" && changed.Reasons[0] == "island_scene_changed" && !ran,
            "island sync case: another scene instance -> SKIP island_scene_changed");

        var pass = Island(true, true, 7, 7);
        pass.Sync("SKY_PASS", (out string m, out string r) => { m = "metrics"; r = null; return true; });
        Check(pass.Results[0] == "SKY_PASS:PASS" && pass.Metrics[0] == "metrics", "island sync case: judgement true -> PASS with metrics");
        var fail = Island(true, true, 7, 7);
        fail.Sync("SKY_FAIL", (out string m, out string r) => { m = "metrics"; r = "why"; return false; });
        Check(fail.Results[0] == "SKY_FAIL:FAIL" && fail.Reasons[0] == "why", "island sync case: judgement false -> FAIL with reason");
        var skip = Island(true, true, 7, 7);
        skip.Sync("SKY_SKIP", (out string m, out string r) => { throw new SkyIslandSkipCase("daytime", "night=false"); });
        Check(skip.Results[0] == "SKY_SKIP:SKIP" && skip.Reasons[0] == "daytime" && skip.Metrics[0] == "night=false",
            "island sync case: SkyIslandSkipCase -> SKIP (never PASS) and keeps its metrics");
        var threw = Island(true, true, 7, 7);
        threw.Sync("SKY_THROW", (out string m, out string r) => { throw new InvalidOperationException("boom"); });
        Check(threw.Results[0] == "SKY_THROW:FAIL" && threw.Reasons[0].Contains("boom"), "island sync case: unexpected exception -> FAIL");

        bool factoryRan = false;
        var goneCoro = Island(false, true, 7, 7);
        Drain(goneCoro.Coroutine("SKY_CORO", delegate { factoryRan = true; return Parent(false); }));
        Check(goneCoro.Results.Count == 1 && goneCoro.Results[0] == "SKY_CORO:SKIP" && goneCoro.Reasons[0] == "island_session_closed" && !factoryRan,
            "island coroutine case: session gone -> SKIP before the coroutine is even created");
        var cancelled = Island(true, true, 7, 7);
        cancelled.Cancelled = true;
        Drain(cancelled.Coroutine("SKY_CORO", delegate { factoryRan = true; return Parent(false); }));
        Check(cancelled.Results[0] == "SKY_CORO:SKIP" && cancelled.Reasons[0] == "cancelled" && !factoryRan,
            "island coroutine case: abort requested -> SKIP with the abort reason");
        var factoryThrows = Island(true, true, 7, 7);
        Drain(factoryThrows.Coroutine("SKY_CORO", delegate { throw new Exception("factory failed"); }));
        Check(factoryThrows.Results[0] == "SKY_CORO:FAIL" && factoryThrows.Reasons[0].StartsWith("case_factory_threw:"),
            "island coroutine case: factory exception -> FAIL case_factory_threw");
        var factoryNull = Island(true, true, 7, 7);
        Drain(factoryNull.Coroutine("SKY_CORO", delegate { return null; }));
        Check(factoryNull.Results[0] == "SKY_CORO:FAIL" && factoryNull.Reasons[0] == "case_factory_returned_null",
            "island coroutine case: factory returned null -> FAIL");
        cleanup.Clear();
        var nested = Island(true, true, 7, 7);
        Drain(nested.Coroutine("SKY_CORO", delegate { return Parent(true); }));
        Check(nested.Results.Count == 1 && nested.Results[0] == "SKY_CORO_UNHANDLED:FAIL" && cleanup.Contains("parent"),
            "island coroutine case: nested exception -> _UNHANDLED FAIL and suspended finally blocks still run");
        cleanup.Clear();
        var clean = Island(true, true, 7, 7);
        Drain(clean.Coroutine("SKY_CORO", delegate { return Parent(false); }));
        Check(clean.Results.Count == 0 && string.Join(",", cleanup) == "child,parent",
            "island coroutine case: normal completion records nothing from the shell (the case records its own verdict)");
    }
}
