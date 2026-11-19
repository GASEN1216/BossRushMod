using System;
using System.Collections;
using System.IO;
using BossRush;
using UnityEngine;
static class Program
{
    static void Check(bool value,string why) { if(!value) throw new Exception(why); }
    static void Dispose(IEnumerator e) { ((IDisposable)e).Dispose(); }
    sealed class Flow : IDisposable
    {
        readonly System.Collections.Generic.Stack<IEnumerator> stack = new System.Collections.Generic.Stack<IEnumerator>();
        internal Flow(IEnumerator root) { stack.Push(root); }
        internal bool Step()
        {
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); Program.Dispose(current); continue; }
                if (current.Current is IEnumerator) { stack.Push((IEnumerator)current.Current); continue; }
                return true;
            }
            return false;
        }
        public void Dispose() { while (stack.Count > 0) Program.Dispose(stack.Pop()); }
    }
    static void FactoryTransitions(string path)
    {
        var owner = new ModBehaviour();
        int consumed = 0;
        var old = NewRequest();
        var oldRequest = AssetBundle.Next;
        UnityEngine.SceneManagement.SceneManager.ActiveHandle = 1;
        using (var flow = new Flow(FactoryResourceLoading.RunSpecial(owner, "fixture", () => { ResourceBundleLoader.LoadFromFile(path); consumed++; })))
        {
            Check(flow.Step(), "factory starts async");
            UnityEngine.SceneManagement.SceneManager.ActiveHandle = 2;
            Check(flow.Step(), "factory waits for abandoned native request before retry");
            Check(consumed == 0 && old.Unloads == 0, "scene change never registers stale content");
            oldRequest.Finish();
            Check(old.Unloads == 1, "scene change drains first lease");
            var fresh = new AssetBundle { name = "retry" };
            AssetBundle.Next = new AssetBundleCreateRequest { Result = fresh };
            Check(flow.Step(), "retry starts after drain"); AssetBundle.Next.Finish();
            Check(flow.Step(), "retry awaits prefab assets"); fresh.Assets.Finish();
            Check(!flow.Step() && consumed == 1 && fresh.Unloads == 0, "retry transfers exactly once");
            fresh.Unload(true);
        }
        old = NewRequest(); oldRequest = AssetBundle.Next;
        using (var flow = new Flow(FactoryResourceLoading.RunSpecial(owner, "fixture", () => consumed++)))
        {
            flow.Step(); owner.Destroyed = true;
            Check(!flow.Step(), "destroyed factory owner stops");
            oldRequest.Finish(); Check(old.Unloads == 1 && consumed == 1, "destroyed owner releases late result");
        }
        // 2026-09-22 实机：基地建筑每次进基地重跑装配，消费方还持有 bundle 时不得再发原生加载。
        owner = new ModBehaviour(); NewRequest(); consumed = 0;
        int loadsBefore = AssetBundle.Loads;
        using (var flow = new Flow(FactoryResourceLoading.RunSpecial(owner, "fixture", () => consumed++, () => true)))
        {
            Check(!flow.Step() && consumed == 1 && AssetBundle.Loads == loadsBefore, "already-held bundle hands over without native work");
        }
        using (var flow = new Flow(FactoryResourceLoading.RunSpecial(owner, "fixture", () => consumed++, () => false)))
        {
            Check(flow.Step() && AssetBundle.Loads == loadsBefore + 1, "released bundle loads again");
        }
        // 2026-11-19：换场景作废一次尝试后，重试前重新判 alreadyLoaded——等场景期间消费方已自己持有包时不再发原生加载。
        AssetBundle.Next.Finish(); // drain the request the previous case left behind
        owner = new ModBehaviour(); old = NewRequest(); oldRequest = AssetBundle.Next; consumed = 0;
        bool held = false;
        UnityEngine.SceneManagement.SceneManager.ActiveHandle = 3;
        int loads = AssetBundle.Loads;
        using (var flow = new Flow(FactoryResourceLoading.RunSpecial(owner, "fixture", () => consumed++, () => held)))
        {
            Check(flow.Step() && AssetBundle.Loads == loads + 1, "not-yet-held bundle starts one async load");
            UnityEngine.SceneManagement.SceneManager.ActiveHandle = 4;
            held = true;
            Check(!flow.Step() && consumed == 1 && AssetBundle.Loads == loads + 1, "retry re-checks the holder and hands over without a second native load");
            Check(old.Unloads == 0, "abandoned first request is still draining");
            oldRequest.Finish();
            Check(old.Unloads == 1 && consumed == 1, "abandoned first request still releases its late result exactly once");
        }
    }
    // 2026-11-19：找已加载包的唯一入口。名字最后一段 OrdinalIgnoreCase 全等；子串、已销毁、无名都不算，枚举抛错返回 null。
    static void FindLoadedMatching()
    {
        var plain = new AssetBundle { name = "codex_portraits" };
        var nested = new AssetBundle { name = "assets/ui\\Modeh_Presentation" };
        var longer = new AssetBundle { name = "codex_portraits_v2" };
        var gone = new AssetBundle { name = "achievement_icons", Destroyed = true };
        var nameless = new AssetBundle { name = null };
        AssetBundle.Loaded.Clear();
        AssetBundle.Loaded.AddRange(new[] { nameless, gone, longer, plain, nested });
        Check(ResourceBundleLoader.FindLoaded("codex_portraits") == plain, "exact file name is found");
        Check(ResourceBundleLoader.FindLoaded("CODEX_PORTRAITS") == plain, "file name comparison ignores case");
        Check(ResourceBundleLoader.FindLoaded("modeh_presentation") == nested, "only the last / or \\ segment of the bundle name is compared");
        Check(ResourceBundleLoader.FindLoaded("codex") == null && ResourceBundleLoader.FindLoaded("portraits_v2") == null, "a substring of a loaded name is not a match");
        Check(ResourceBundleLoader.FindLoaded("achievement_icons") == null, "destroyed bundle is not returned");
        Check(ResourceBundleLoader.FindLoaded(null) == null && ResourceBundleLoader.FindLoaded("") == null, "empty file name finds nothing");
        Check(ResourceBundleLoader.IsNamed(longer, "codex_portraits_v2") && !ResourceBundleLoader.IsNamed(longer, "codex_portraits")
            && !ResourceBundleLoader.IsNamed(null, "codex_portraits") && !ResourceBundleLoader.IsNamed(nameless, ""), "shared name rule");
        AssetBundle.FailEnumeration = true;
        Check(ResourceBundleLoader.FindLoaded("codex_portraits") == null, "enumeration failure returns null instead of throwing");
        AssetBundle.FailEnumeration = false;
        AssetBundle.Loaded.Clear();
    }
    static AssetBundle NewRequest()
    {
        ResourceBundleLoader.ResetStaticCaches();
        Time.realtimeSinceStartupAsDouble=0;
        var b=new AssetBundle {name="fixture"};
        AssetBundle.Next=new AssetBundleCreateRequest {Result=b};
        return b;
    }
    static void Main(string[] args)
    {
        ModBehaviour.Root=args[0]; Directory.CreateDirectory(args[0]);
        BuildingModels.Run();
        string path=Path.Combine(args[0],"fixture"); File.WriteAllText(path,"fixture");
        int consumed=0;
        var b=NewRequest();
        var e=ResourceBundleLoader.Prepare(path,false,()=>false,()=>{Check(ResourceBundleLoader.LoadFromFile(path)==b,"transfer same bundle");consumed++;});
        Check(e.MoveNext(),"request waits"); Check(AssetBundle.Next.ForcedReads==0,"no blocking accessor while pending");
        AssetBundle.Next.Finish(); Check(!e.MoveNext(),"complete"); Dispose(e);
        Check(consumed==1 && b.Unloads==0,"owner transfer preserves bundle");
        b.Unload(true);

        // 2026-09-22 实机：Prepare 用 "Assets/x"、消费方用 "Assets\x" 找同一个 bundle，键不一致就会同步二次加载。
        b=NewRequest();
        string aliasPath=Path.Combine(args[0],".","fixture").Replace('\\','/');
        e=ResourceBundleLoader.Prepare(aliasPath,false,()=>false,()=>{int loads=AssetBundle.Loads;
            Check(ResourceBundleLoader.LoadFromFile(path)==b && AssetBundle.Loads==loads,"separator/dot alias resolves to the pending lease without a second native load");consumed++;});
        Check(e.MoveNext(),"alias request waits"); AssetBundle.Next.Finish(); Check(!e.MoveNext(),"alias complete"); Dispose(e);
        Check(consumed==2 && b.Unloads==0,"alias transfer preserves bundle");
        b.Unload(true);

        foreach(string mode in new[]{"cancel","scene","dispose","destroy","timeout"})
        {
            b=NewRequest(); bool cancel=false;
            e=ResourceBundleLoader.Prepare(path,false,()=>cancel,()=>{throw new Exception("cancelled consumer ran");},1);
            Check(e.MoveNext(),"start "+mode);
            if(mode=="dispose") Dispose(e);
            else if(mode=="destroy") { ResourceBundleLoader.ResetStaticCaches(); Check(!e.MoveNext(),"destroy ends wait"); }
            else { cancel=mode!="timeout"; if(mode=="timeout") Time.realtimeSinceStartupAsDouble=2; Check(!e.MoveNext(),"abort ends wait"); }
            Check(b.Unloads==0 && AssetBundle.Next.ForcedReads==0,"pending cancellation never synchronously waits");
            int loadCount = AssetBundle.Loads;
            Check(ResourceBundleLoader.LoadFromFile(path) == null && AssetBundle.Loads == loadCount,
                "sync query while cancellation drains does not start duplicate native work");
            AssetBundle.Next.Finish(); Dispose(e);
            Check(b.Unloads==1 && b.UnloadedObjects,"late native result released exactly once "+mode);
        }
        b=NewRequest(); AssetBundle.Next.Result=null;
        int fallbacks = 0;
        e=ResourceBundleLoader.Prepare(path,false,()=>false,()=>fallbacks++);
        e.MoveNext();AssetBundle.Next.Finish();Check(!e.MoveNext(),"failed load completes");Dispose(e);
        var records=ResourceBundleLoader.Snapshot();Check(records[records.Length-1].failed,"failure observable");
        Check(fallbacks == 1, "failed native request preserves consumer fallback");

        foreach(bool stopWhileAssets in new[]{false,true})
        {
            b=NewRequest(); e=ResourceBundleLoader.Prepare(path,true,()=>false,()=>ResourceBundleLoader.LoadFromFile(path));
            e.MoveNext();AssetBundle.Next.Finish();Check(e.MoveNext(),"asset request waits");
            Dispose(e);Check(b.Unloads==0,"do not unload a bundle while asset request is live");
            b.Assets.Finish();Check(b.Unloads==1,"disposed asset request late cleanup");
        }
        b=NewRequest(); AssetBundle.Sync=b;
        e=ResourceBundleLoader.Prepare(path,true,()=>false,()=>{});
        e.MoveNext();AssetBundle.Next.Finish();e.MoveNext();b.Assets.Finish();Check(!e.MoveNext(),"unclaimed prepared bundle completes");Dispose(e);
        var prepared=(System.Collections.IDictionary)typeof(ResourceBundleLoader).GetField("preparedAssets",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).GetValue(null);
        Check(b.Unloads==1 && prepared.Count==0,"unclaimed bundle releases its prepared asset array as well as native objects");
        b=NewRequest(); AssetBundle.Sync=b;
        string iconPath=Path.Combine(args[0],ProductionIconCache.BundleRelativePath);Directory.CreateDirectory(Path.GetDirectoryName(iconPath));File.WriteAllText(iconPath,"fixture");
        var icon=new Sprite{name="original_name",texture=new Texture2D()};b.Sprites["assets/items/icon.png"]=icon;
        ProductionIconCache.ResetStaticCaches();
        Check(ProductionIconCache.Get("Assets\\Items\\icon.png")==icon,"legacy path alias resolves");
        Check(ProductionIconCache.Get("assets/items/icon.png")==icon,"cached sprite reused");
        Check(ProductionIconCache.Contains("Assets\\Items\\icon.png")&&!ProductionIconCache.Contains("assets/items/none.png")&&!ProductionIconCache.Contains("../icon.png"),"read-only directory probe");
        Check(!ProductionIconCache.AllowRawFallback,"release build with bundle forbids PNG fallback");
        Check(ProductionIconCache.Get("../icon.png")==null,"path traversal rejected");
        ProductionIconCache.ResetStaticCaches();ProductionIconCache.ResetStaticCaches();
        Check(b.Unloads==1&&icon.Destroyed&&icon.texture.Destroyed,"idempotent bundle release");
        AssetBundle.Sync=null;Check(ProductionIconCache.AllowRawFallback,"missing bundle preserves fallback");
        var m=ResourcePerformanceMetrics.Summarize(new double[]{5,1,3,2,4},5,"ms","test");
        Check(m.p50==3&&m.p95==5&&m.mean==3,"nearest-rank quantiles");
        Check(ResourcePerformanceMetrics.Summarize(new double[]{double.NaN},1,"ms","test").status=="unavailable","NaN unavailable");
        Check(ResourcePerformanceMetrics.Summarize(new double[0],0,"ms","GPU").status=="unavailable","no GPU data not zero");
        string reason;Check(ResourcePerformanceMetrics.IsComplete(10,600,false,true,false,out reason),"10s complete");
        foreach(bool[] flags in new[]{new[]{true,true,false},new[]{false,false,false},new[]{false,true,true}})
            Check(!ResourcePerformanceMetrics.IsComplete(10,600,flags[0],flags[1],flags[2],out reason),"interrupted windows fail");
        Check(!ResourcePerformanceMetrics.IsComplete(9.999,600,false,true,false,out reason),"short window fails");
        FactoryTransitions(path);
        FindLoadedMatching();
        Console.WriteLine("ResourceProduction PASS: native late completion, disposal, timeout, failure, aliases/ownership, loaded-bundle lookup, metric judges");
    }
}
