using System;
using System.Collections.Generic;
using UnityEngine;

internal static class Probe
{
    internal static readonly List<string> Trace=new List<string>();
    internal static int ConfigReads, ManagerReads, Creates, Opens;
    internal static bool CreateReturnsNull, ThrowCreate, ThrowOpen, ThrowClose, ThrowFlush;
    internal static void Clear()
    {
        Trace.Clear(); ConfigReads=ManagerReads=Creates=Opens=0;
        CreateReturnsNull=ThrowCreate=ThrowOpen=ThrowClose=ThrowFlush=false;
    }
}
namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a,Object b)
        {
            bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;
            return an||bn?an==bn:ReferenceEquals(a,b);
        }
        public static bool operator !=(Object a,Object b){return !(a==b);}
        public override bool Equals(object obj){return ReferenceEquals(this,obj);}
        public override int GetHashCode(){return base.GetHashCode();}
        public static void Destroy(Object value)
        {
            if(value==null)return;
            GameObject go=value as GameObject;
            if(go!=null)
            {
                foreach(Transform child in go.transform.Children)Destroy(child.gameObject);
                foreach(Component component in go.Components)component.Destroyed=true;
                go.transform.Destroyed=true;
            }
            value.Destroyed=true;
        }
    }
    public class Component:Object
    {
        public GameObject gameObject;
        public Transform transform {get{return gameObject.transform;}}
    }
    public class Transform:Object
    {
        public GameObject gameObject;
        public Transform parent;
        internal readonly List<Transform> Children=new List<Transform>();
        public void SetParent(Transform value){parent=value;if(value!=null)value.Children.Add(this);}
    }
    public class GameObject:Object
    {
        internal readonly List<Component> Components=new List<Component>();
        public readonly Transform transform;
        public GameObject(){transform=new Transform{gameObject=this};}
        public T AddComponent<T>() where T:Component,new(){T value=new T{gameObject=this};Components.Add(value);return value;}
    }
}
namespace Duckov.UI
{
    public class GameplayUIManager:Component
    {
        internal static GameplayUIManager Current;
        public static GameplayUIManager Instance {get{Probe.ManagerReads++;return Current;}}
    }
}
public class LevelManager {public static LevelManager Instance;public bool IsBaseLevel;}
namespace Saves {public static class SavesSystem {public static int CurrentSlot;}}
namespace BossRush
{
    public class SceneRuntimeContext { }
    public abstract class BossRushRuntimeModuleBase
    {
        public virtual string ModuleName {get{return "";}}
        public virtual void OnAwake(ModBehaviour owner){}
        public virtual void OnStart(){}
        public virtual void OnSceneLoaded(SceneRuntimeContext context){}
        public virtual void OnUpdate(float deltaTime,float unscaledDeltaTime){}
        public virtual void OnDestroy(){}
    }
    internal sealed class Registry
    {
        internal DailyReportRuntimeModule Registered;
        internal void Register(DailyReportRuntimeModule value){Registered=value;}
    }
    public partial class ModBehaviour:UnityEngine.Object
    {
        private DailyReportRuntimeModule dailyReportRuntime;
        private readonly Registry runtimeModuleHost=new Registry();
        internal bool Enabled;
        internal DailyReportRuntimeModule Module {get{return dailyReportRuntime;}}
        internal DailyReportRuntimeModule Registered {get{return runtimeModuleHost.Registered;}}
        internal bool IsDailyReportConfiguredEnabled(){Probe.ConfigReads++;return Enabled;}
        public void CleanupDailyReportMailbox(){Probe.Trace.Add("mailbox");}
        public void ShowBigBanner(string message){Probe.Trace.Add("banner");}
        public static void DevLog(string message){}
        public static void CriticalLog(string key,string message){}
    }
    internal static class DailyReportTuning {internal const string ModuleName="DailyReport",LogPrefix="[DailyReport]";}
    internal class DailyReportData {internal int DayIndex;}
    internal static class L10n {internal static string T(string cn,string en){return cn;}}
    internal static class DailyReportService
    {
        internal static bool HasPendingIssueBanner;
        internal static DailyReportData Data;
        internal static void SyncCarrySecondsToPersistence(){Probe.Trace.Add("sync");}
        internal static void Tick(float deltaTime){Probe.Trace.Add("tick");}
        internal static void ConsumeIssueBanner(){HasPendingIssueBanner=false;}
        internal static void ResetStaticCaches(){Probe.Trace.Add("service-reset");}
    }
    internal static class DailyReportSaveCoordinator
    {
        internal static void EnsureSubscribed(){Probe.Trace.Add("save-subscribe");}
        internal static void TryFlushOnHostDestroy(){Probe.Trace.Add("flush");if(Probe.ThrowFlush)throw new InvalidOperationException();}
        internal static void ShutdownSubscription(){Probe.Trace.Add("save-unsubscribe");}
        internal static void Tick(){}
        internal static void ResetStaticCaches(){Probe.Trace.Add("save-reset");}
    }
    internal static class DailyReportStatsCollector
    {
        internal static void EnsureSubscribed(){Probe.Trace.Add("stats-subscribe");}
        internal static void ShutdownSubscription(){Probe.Trace.Add("stats-unsubscribe");}
        internal static void ResetStaticCaches(){Probe.Trace.Add("stats-reset");}
    }
    internal static class DailyReportLayoutTable {internal static void ResetStaticCaches(){Probe.Trace.Add("layout-reset");}}
    internal static class DailyReportBackground {internal static void ResetStaticCaches(){Probe.Trace.Add("background-reset");}}
    internal sealed partial class DailyReportView:Component
    {
        internal static DailyReportView Instance;
        internal bool closingForCleanup;
        internal static DailyReportView CreateRuntime(Transform parent)
        {
            Probe.Creates++;
            if(Probe.ThrowCreate)throw new InvalidOperationException("create");
            if(Probe.CreateReturnsNull)return null;
            GameObject canvas=new GameObject();canvas.transform.SetParent(parent);
            DailyReportView view=new GameObject().AddComponent<DailyReportView>();
            view.transform.SetParent(canvas.transform);Instance=view;return view;
        }
        internal void RefreshAndOpen(){Probe.Opens++;if(Probe.ThrowOpen)throw new InvalidOperationException("open");}
        internal void Close(){Probe.Trace.Add("view-close");if(Probe.ThrowClose)throw new InvalidOperationException("close");}
    }
}
