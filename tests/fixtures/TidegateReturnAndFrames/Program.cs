using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace UnityEngine { internal static class Time { internal static int frameCount = 0; } }
namespace BossRush
{
    internal static class SceneManager { internal struct Scene { internal string name; } internal static Scene Current; internal static Scene GetActiveScene() => Current; }
    internal static class LevelManager { internal static object Instance; internal static bool AfterInit; }
    internal static class CharacterMainControl { internal static object Main; }
    internal sealed class SceneLoader
    {
        internal static SceneLoader Instance;
        internal int Calls;
        internal TaskCompletionSource<bool> Next = new TaskCompletionSource<bool>();
        internal Task LoadBaseScene() { Calls++; return Next.Task; }
    }
    internal static class L10n { internal static string T(string a,string b) { return b; } }
    internal partial class Host
    {
        internal readonly List<string> News = new List<string>(); internal bool Error;
        private bool IsBaseHubSceneName(string n) { return n == "Base" || n == "Base_SceneV2"; }
        private void DevLog(string s) { }
        private void HideF3DebugCheatMenu() { }
        private void ShowMessage(string s) { News.Add(s); }
        private void SetF3DebugCheatStatus(string text, bool error) { Error = error; }
        internal void Return() { TeleportPlayerHomeToBaseScene(); }
    }
    internal static class Program
    {
        private static void Check(bool ok, string failure)
        {
            if (!ok) { Console.Error.WriteLine("FAIL: " + failure); throw new Exception(failure); }
        }
        private static void TestReturn(bool manager, bool init, bool player, string scene, bool expected)
        {
            SceneManager.Current.name = "TidegateRaid"; CharacterMainControl.Main = null; LevelManager.Instance = null;
            var loader = SceneLoader.Instance = new SceneLoader();
            var host = new Host(); host.Return();
            Check(loader.Calls == 1 && host.News.Count == 1 && !host.Error, "must use official base load and await");
            SceneManager.Current.name = scene; LevelManager.Instance = manager ? new object() : null;
            LevelManager.AfterInit = init; CharacterMainControl.Main = player ? new object() : null;
            loader.Next.SetResult(true);
            Check(host.Error == !expected && host.News.Contains("Returned to base") == expected, "false base arrival: " + scene);
        }
        private static void Main()
        {
            TestReturn(true,true,true,"Base_SceneV2",true);
            TestReturn(false,true,true,"Base_SceneV2",false);
            TestReturn(true,false,true,"Base_SceneV2",false);
            TestReturn(true,true,false,"Base_SceneV2",false);
            TestReturn(true,true,true,"TidegateRaid",false);
            using (var w = new TidegateFrameProbe.Window(64))
            {
                Check(w.Metrics().Contains("unavailable"), "empty window claimed a cause");
                // 第 10 帧是一次慢搜刮；下一帧 AI 很贵但下一帧本身是快帧，两个不能串。
                for (int f=1; f<=22; f++)
                {
                    long t = f * Stopwatch.Frequency; w.Start(f,t);
                    t += Stopwatch.Frequency * (f==10 ? 20 : 1) / 1000; w.Mark(f,TidegateTickPart.Loot,t);
                    t += Stopwatch.Frequency * (f==11 ? 18 : 1) / 1000; w.Mark(f,TidegateTickPart.EnemyOwner,t);
                    w.CapturePrevious(f, f==11 ? 55 : 16);
                    w.CapturePrevious(f, f==11 ? 55 : 16); // 同帧重入不会重复记。
                }
                // 21 帧中 p95 是 16；单独造 20 帧，让最慢两帧均为 55ms 并检查分项归属。
                Check(w.Metrics().Contains("owner_samples=21,"),"same-frame duplication or missing sample");
            }
            using (var w = new TidegateFrameProbe.Window(20))
            {
                for (int f=1;f<=21;f++)
                {
                    // 官方 AI 回调可能早于本图的 Update；也可能一帧有多只鸭，计时应相加。
                    w.AddExternal(f,TidegateTickPart.AiUpdate,Stopwatch.Frequency * 3/1000);
                    w.AddExternal(f,TidegateTickPart.AiUpdate,Stopwatch.Frequency * 4/1000);
                    long t=0; w.Start(f,t);
                    t += Stopwatch.Frequency * (f==8 || f==9 ? 20 : 1)/1000; w.Mark(f,TidegateTickPart.Loot,t);
                    t += Stopwatch.Frequency * (f==10 ? 18 : 1)/1000; w.Mark(f,TidegateTickPart.EnemyOwner,t);
                    w.CapturePrevious(f,(f==9 || f==10) ? 55 : 16);
                }
                string m=w.Metrics();
                Check(m.Contains("owner_samples=20,owner_wall_p95_ms=55.00,owner_tail_frames=2"),"bad P95 station");
                Check(m.Contains("tail_loot_mean_ms=20.00") && m.Contains("tail_enemy_owner_mean_ms=1.00"),"wrong owner or one-frame shift");
                Check(m.Contains("tail_ai_update_mean_ms=7.00"), "early official AI samples lost or overwritten");
                Console.WriteLine(m);
            }
            Console.WriteLine("PASS: full base entry, 4 missing-owner / wrong-level failures, previous-frame slow-tail attribution, sample cap, dedup and empty windows.");
        }
    }
}
