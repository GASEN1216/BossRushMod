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
        Console.WriteLine("PASS: " + assertions + " assertions; production coroutine stack and case wrapper, no Unity smoke");
    }
}
