using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityEngine
{
    internal class WaitForEndOfFrame { }
    internal struct Rect { internal int Frame; }
    internal struct Color32 { internal byte r; }
    internal class Object
    { public static void Destroy(Object value) { if (value != null) ((Texture2D)value).Destroyed = true; } }
    internal class Texture2D : Object
    {
        internal int width = 1, height = 1;
        internal bool Destroyed;
        internal readonly byte Frame;
        internal Texture2D(byte frame) { Frame = frame; }
        internal Color32[] GetPixels32() { return new[] { new Color32 { r = Frame } }; }
    }
    internal static class Time { internal static float realtimeSinceStartup; }
    internal static class ScreenCapture
    {
        internal static int Calls;
        internal static bool Throw;
        internal static Texture2D Texture;
        internal static Texture2D CaptureScreenshotAsTexture()
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("capture failed");
            return Texture = new Texture2D((byte)BossRush.FrameState.Current);
        }
    }
    internal static class ImageConversion
    {
        internal static byte[] EncodeToPNG(Texture2D value) { return new[] { value.Frame }; }
        internal static byte[] EncodeToJPG(Texture2D value,int quality) { return new[] { value.Frame }; }
    }
}
namespace BossRush
{
    internal static class FrameState { internal static int Current; internal static bool Overlay; }
    internal class F3AutotestStepRecord
    { internal string Id = "frame"; internal readonly List<F3AutotestShot> Shots = new List<F3AutotestShot>(); }
    internal class F3AutotestShot { internal string Name, Kind, Encoding, Metrics, File; internal long Bytes; }
    internal class Info { internal int ShotsSkipped, ShotCount, ShotsDegraded; }
    internal class CaptureState
    {
        internal readonly Info Info = new Info();
        internal long ShotBytes;
        internal string ShotsDir = AppContext.BaseDirectory;
        internal F3GameplayValidationRunner.AutotestShotAnalysis LastShot;
    }
    internal class AutotestMeasure { internal string Result, Reason, Metrics; }
    internal static class F3AutotestJudges
    {
        internal const long ShotBudgetBytes = 10000;
        internal static string Encoding = "png";
        internal static string ChooseEncoding(long bytes,bool ui,long budget) { return Encoding; }
        internal static string JudgeTextOverflow(List<string> a,List<string> b,List<string> c,int count,out string metrics,out string reason)
        { metrics = "inspected=" + count; reason = null; return "PASS"; }
    }
    internal partial class F3GameplayValidationRunner
    {
        internal struct AutotestTextProbe { internal int Frame; }
        internal struct AutotestWorldProbe { internal int Frame; }
        internal class AutotestShotAnalysis
        {
            internal int Width, Height, TextFrame, WorldFrame;
            internal readonly Dictionary<string,AutotestMeasure> Contrast = new Dictionary<string,AutotestMeasure>();
            internal readonly Dictionary<string,AutotestMeasure> Visibility = new Dictionary<string,AutotestMeasure>();
            internal readonly List<double> RowLuminance = new List<double>();
            internal AutotestMeasure Overflow;
        }
        internal readonly CaptureState _autotest = new CaptureState();
        internal int TextCollections, WorldCollections, RowCollections;
        internal readonly List<string> Errors = new List<string>();
        internal Texture2D Half;
        private static bool EnsureAutotestOverlaysHidden(out string blocking) { blocking = FrameState.Overlay ? "overlay" : null; return blocking == null; }
        private int CollectAutotestTextProbes(string f,List<string> p,List<AutotestTextProbe> text,List<string> a,List<string> b,List<string> c)
        { TextCollections++; text.Add(new AutotestTextProbe { Frame = FrameState.Current }); return 1; }
        private List<AutotestWorldProbe> CollectAutotestWorldProbes(List<string> p)
        { WorldCollections++; return new List<AutotestWorldProbe> { new AutotestWorldProbe { Frame = FrameState.Current } }; }
        private List<Rect> CollectAutotestRowRects()
        { RowCollections++; return new List<Rect> { new Rect { Frame = FrameState.Current } }; }
        private void AnalyzeAutotestText(Color32[] p,AutotestShotAnalysis a,List<AutotestTextProbe> probes,List<string> paths)
        { a.TextFrame = probes[0].Frame; if (a.TextFrame != p[0].r) Errors.Add("stale TMP glyph bounds"); }
        private void AnalyzeAutotestWorld(Color32[] p,AutotestShotAnalysis a,List<AutotestWorldProbe> probes,List<string> paths)
        { a.WorldFrame = probes[0].Frame; if (a.WorldFrame != p[0].r) Errors.Add("stale camera/ring coordinates"); }
        private static Rect AutotestRowStrip(Rect row) { return row; }
        private double MeanAutotestLuminance(Color32[] p,int w,int h,Rect row)
        { if (row.Frame != p[0].r) Errors.Add("stale row coordinates"); return row.Frame; }
        private static long AutotestStepElapsedMs(F3AutotestStepRecord r) { return 1; }
        private Texture2D DownsampleAutotestShot(Color32[] p,int w,int h) { return Half = new Texture2D(p[0].r); }
        private static string AutotestFileName(string s) { return s; }
        internal IEnumerator Begin(F3AutotestStepRecord r) { return CaptureAutotestShot(r,"shot","ui",null,new List<string>(),new List<string>()); }
    }
}
