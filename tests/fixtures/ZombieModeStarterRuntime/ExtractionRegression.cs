using System;
using System.Linq;
using BossRush;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public class UnityEvent
    {
        private event UnityAction listeners;
        public void AddListener(UnityAction listener) { listeners += listener; }
        public void Invoke() { listeners?.Invoke(); }
    }
    public class UnityEvent<T>
    {
        private event Action<T> listeners;
        public void AddListener(Action<T> listener) { listeners += listener; }
        public void Invoke(T value) { listeners?.Invoke(value); }
    }
}

public sealed class CountDownArea : Component
{
    public UnityEvent onCountDownSucceed;
    public UnityEvent<CountDownArea> onCountDownStarted,onCountDownStopped;
}
public static class EvacuationCountdownUI
{
    public static void Request(CountDownArea area) { Probe.Trace.Add("countdown-request"); }
    public static void Release(CountDownArea area) { Probe.Trace.Add("countdown-release"); }
}
public sealed class CharacterMainControl : Component { public static CharacterMainControl Main; }
namespace Duckov.UI
{
    public static class NotificationText
    {
        public static void Push(string text)
        {
            if(Probe.ThrowPayoutNotification && text=="BossRush_ZombieMode_Notify_ExtractionCash")
                throw new InvalidOperationException("notification unavailable after payout");
        }
    }
}
namespace BossRush
{
    public sealed class ZombieModeExtractionController : Component { public void Initialize(int runId) { } }
    internal static partial class ModeExtractionPointFactory
    {
        // 仅替代 Unity 预制体创建；事件装配和调用链全部来自生产方法。
        internal static ModeExtractionPointResult CreateExtractionPoint(ModeExtractionPointRequest request)
        {
            var go=new GameObject(request.ObjectName);
            var area=go.AddComponent<CountDownArea>();
            ConfigureEvents(area,request,true,true);
            return new ModeExtractionPointResult { GameObject=go,CountDownArea=area };
        }
    }
}

internal static class ExtractionRegression
{
    private static void Check(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
    private static ZombieModeRuntimeModule New(out ZombieModeRunState state)
    {
        Probe.Trace.Clear();Probe.Money=100;Probe.PayoutAvailable=true;Probe.ThrowAfterPayout=false;
        Probe.ThrowEconomy=false;Probe.ThrowPayoutNotification=false;
        state=new ZombieModeRunState { RunId=8,PurificationPoints=400,ExtractionChanneling=true,LifecyclePhase=ZombieModeLifecyclePhase.Active };
        var runtime=new ZombieModeRuntimeModule(new ModBehaviour(),state);
        runtime.EnsureZombieModeExtractionArea(8);
        return runtime;
    }
    internal static void Run()
    {
        // 官方自动成功事件先到时，失败必须留在当前地图并能在同一个点重试。
        ZombieModeRunState state; var runtime=New(out state);var area=state.ActiveExtractionArea;
        Probe.PayoutAvailable=false;
        area.onCountDownSucceed.Invoke();
        Check(state.PurificationPoints==400 && Probe.Money==100 && state.RunId==8,"failed payout preserves both points and the current run");
        Check(!Probe.Trace.Contains("load-base") && !Probe.Trace.Contains("notify-extraction") && Probe.Trace.Contains("retry-ui"),"failed factory acceptance never notifies evacuation or leaves the map");
        Probe.PayoutAvailable=true;area.onCountDownSucceed.Invoke();
        Check(Probe.Money==500 && state.PurificationPoints==0 && area==null,"retry pays once and performs the actual run cleanup");
        Check(Probe.Trace.Count(x=>x=="load-base")==1 && Probe.Trace.Count(x=>x=="notify-extraction")==1,"acceptance survives run reset and destroyed area, and reentry dispatches return once");
        area.onCountDownSucceed.Invoke();
        Check(Probe.Money==500 && Probe.Trace.Count(x=>x=="load-base")==1,"late duplicated success cannot pay or load again");

        // 模式协程先到也走同一工厂，重入不能触发第二次结算。
        runtime=New(out state);Check(runtime.CompleteExtraction(8),"manual countdown completion succeeds");
        Check(Probe.Money==500 && Probe.Trace.Count(x=>x=="load-base")==1,"manual completion pays and returns exactly once");

        foreach(bool postPayment in new[] { false,true })
        {
            runtime=New(out state);area=state.ActiveExtractionArea;
            Probe.ThrowEconomy=!postPayment;Probe.ThrowAfterPayout=postPayment;
            area.onCountDownSucceed.Invoke();
            Check(postPayment ? Probe.Money==500 && Probe.Trace.Count(x=>x=="load-base")==1 : Probe.Money==100 && state.PurificationPoints==400 && !Probe.Trace.Contains("load-base"),"economy exception distinguishes before commit from OnMoneyChanged after commit");
            Probe.ThrowEconomy=false;Probe.ThrowAfterPayout=false;
            area.onCountDownSucceed.Invoke();
            Check(Probe.Money==500,"retry never duplicates a committed wallet credit");
        }

        runtime=New(out state);Probe.ThrowPayoutNotification=true;area=state.ActiveExtractionArea;
        area.onCountDownSucceed.Invoke();area.onCountDownSucceed.Invoke();
        Check(Probe.Money==500 && Probe.Trace.Count(x=>x=="load-base")==1,"notification failure cannot retain spendable points or block successful extraction");

        Probe.Trace.Clear();int legacy=0;
        var legacyArea=ModeExtractionPointFactory.CreateExtractionPoint(new ModeExtractionPointRequest {
            OnSucceed=()=>legacy++,OnFallbackNotify=p=>Probe.Trace.Add("legacy-notify"),OnFallbackLoadBase=()=>Probe.Trace.Add("legacy-load")
        }).CountDownArea;
        legacyArea.onCountDownSucceed.Invoke();legacyArea.onCountDownSucceed.Invoke();
        Check(legacy==1 && Probe.Trace.Count(x=>x=="legacy-load")==1,"consumers without acceptance callback retain existing once-only behavior");

        Probe.Trace.Clear();int attempts=0;
        var throwingArea=ModeExtractionPointFactory.CreateExtractionPoint(new ModeExtractionPointRequest {
            TrySucceed=()=> { if(++attempts==1)throw new InvalidOperationException("receipt failure");return true; },
            OnFallbackLoadBase=()=>Probe.Trace.Add("recovered-load")
        }).CountDownArea;
        bool threw=false;
        try { throwingArea.onCountDownSucceed.Invoke(); } catch(InvalidOperationException) { threw=true; }
        Check(threw && Probe.Trace.Count==0,"receipt exception must not dispatch fallback actions");
        throwingArea.onCountDownSucceed.Invoke();throwingArea.onCountDownSucceed.Invoke();
        Check(attempts==2 && Probe.Trace.Count(x=>x=="recovered-load")==1,"receipt exception releases the gate so retry can succeed exactly once");
        Console.WriteLine("PASS ZombieMode extraction acceptance: rollback, retry, wallet commit exceptions, notification failure, recursive success, legacy factory");
    }
}
