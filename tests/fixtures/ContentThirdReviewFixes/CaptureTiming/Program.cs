using System;
using System.Collections;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string label) { checks++; if (!value) throw new Exception(label); }
    private static F3GameplayValidationRunner Reset()
    {
        FrameState.Current = 1; FrameState.Overlay = false;
        ScreenCapture.Calls = 0; ScreenCapture.Throw = false; ScreenCapture.Texture = null;
        F3AutotestJudges.Encoding = "png"; Time.realtimeSinceStartup = 0;
        return new F3GameplayValidationRunner();
    }
    private static void Main()
    {
        foreach (string encoding in new[] { "png", "jpg", "jpg_half", "skipped" })
        {
            var runner = Reset(); F3AutotestJudges.Encoding = encoding;
            var record = new F3AutotestStepRecord(); IEnumerator run = runner.Begin(record);
            Check(run.MoveNext() && run.Current is WaitForEndOfFrame,"capture first waits for rendered frame");
            Check(runner.TextCollections == 0 && runner.WorldCollections == 0 && runner.RowCollections == 0,"probes collected only after frame-end layout and camera");
            FrameState.Current = 9;
            Check(!run.MoveNext(),"no yield between frame-end probes and pixels");
            Check(runner.Errors.Count == 0 && runner._autotest.LastShot.TextFrame == 9 && runner._autotest.LastShot.WorldFrame == 9 && runner._autotest.LastShot.RowLuminance[0] == 9,"glyph camera ring and row probes match captured pixel frame");
            Check(ScreenCapture.Calls == 1 && ScreenCapture.Texture.Destroyed,"texture captured once and disposed");
            Check(runner.TextCollections == 1 && runner.WorldCollections == 1 && runner.RowCollections == 1,"all probe families collected exactly once");
            Check(record.Shots[0].Encoding == encoding,"encoding decision remains unchanged");
            Check(encoding == "skipped" ? runner._autotest.Info.ShotsSkipped == 1 : runner._autotest.Info.ShotCount == 1 && runner._autotest.ShotBytes == 1,"shot budget/count records correct boundary");
            if (encoding == "jpg_half") Check(runner.Half.Destroyed,"half-size temporary texture disposed");
        }
        {
            var runner = Reset(); var record = new F3AutotestStepRecord(); IEnumerator run = runner.Begin(record);
            runner._autotest.LastShot = new F3GameplayValidationRunner.AutotestShotAnalysis { TextFrame = 7 };
            run.MoveNext(); FrameState.Overlay = true;
            Check(!run.MoveNext() && record.Shots[0].Encoding == "skipped","overlay appearing before frame-end blocks capture");
            Check(ScreenCapture.Calls == 0 && runner.TextCollections == 0 && runner.WorldCollections == 0 && runner.RowCollections == 0,"blocked frame has no probes or pixels");
            Check(runner._autotest.LastShot == null,"blocked frame cannot reuse previous frame analysis");
        }
        {
            var runner = Reset(); var record = new F3AutotestStepRecord(); IEnumerator run = runner.Begin(record);
            runner._autotest.LastShot = new F3GameplayValidationRunner.AutotestShotAnalysis { TextFrame = 7 };
            run.MoveNext(); ScreenCapture.Throw = true;
            Check(!run.MoveNext() && record.Shots[0].Encoding == "failed" && record.Shots[0].Metrics.Contains("capture_failed:InvalidOperationException"),"capture failure remains observable");
            Check(runner._autotest.LastShot == null,"capture failure cannot reuse previous frame analysis");
        }
        Console.WriteLine("CaptureTiming: " + checks + " assertions PASS");
    }
}
