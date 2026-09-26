using System;
using BossRush;
using Duckov.UI;
using UnityEngine;

internal static class Program
{
    private static int count;
    private static void Check(bool condition,string message){count++;if(!condition)throw new InvalidOperationException(message);}
    private static void MustThrow(Action action,string message)
    {
        bool threw=false;try{action();}catch(InvalidOperationException){threw=true;}Check(threw,message);
    }
    private static GameplayUIManager Manager(){return new GameObject().AddComponent<GameplayUIManager>();}
    private static ModBehaviour Host()
    {
        ModBehaviour host=new ModBehaviour();host.BindProductionForFixture();return host;
    }
    private static int Main()
    {
        Probe.Clear();GameplayUIManager.Current=null;DailyReportView.Instance=null;
        ModBehaviour host=Host();
        Check(ReferenceEquals(host.Module,host.Registered)&&Probe.ConfigReads==0&&Probe.Creates==0,"Registration must keep one module without eagerly reading the UI gate");
        host.Module.OnAwake(host);
        Probe.Clear();host.OpenDailyReportUI();
        Check(Probe.ConfigReads==1&&Probe.ManagerReads==0&&Probe.Creates==0,"Disabled host UI must read the live gate once and avoid all view work");
        host.Enabled=true;Probe.Clear();host.OpenDailyReportUI();
        Check(Probe.ConfigReads==1&&Probe.ManagerReads==1&&Probe.Creates==0,"Missing GameplayUIManager must leave UI creation retryable");
        GameplayUIManager.Current=Manager();Probe.Clear();host.OpenDailyReportUI();
        DailyReportView first=DailyReportView.Instance;
        Check(Probe.ConfigReads==1&&Probe.ManagerReads==2&&Probe.Creates==1&&Probe.Opens==1&&first!=null,"First UI open must retain both manager reads and create then refresh exactly once");
        host.OpenDailyReportUI();
        Check(Probe.ConfigReads==2&&Probe.ManagerReads==2&&Probe.Creates==1&&Probe.Opens==2,"Repeated UI open must reuse the owner-held view");
        host.Enabled=false;host.OpenDailyReportUI();
        Check(Probe.Opens==2&&Probe.Creates==1,"Disabling an existing view must block future opens without recreating it");
        host.Enabled=true;
        UnityEngine.Object.Destroy(GameplayUIManager.Current.gameObject);
        Check(first==null,"Scene destruction must destroy the view through its UI parent tree");
        GameplayUIManager.Current=Manager();host.OpenDailyReportUI();
        Check(Probe.Creates==2&&Probe.Opens==3&&DailyReportView.Instance!=null,"Destroyed owner-held views must be recreated after scene replacement");
        DailyReportView replacement=DailyReportView.Instance;
        Probe.Trace.Clear();Probe.ThrowFlush=true;Probe.ThrowClose=true;
        host.Module.OnDestroy();
        Check(string.Join(",",Probe.Trace)=="sync,flush,save-unsubscribe,stats-unsubscribe,stats-reset,view-close,mailbox,service-reset,save-reset,layout-reset,background-reset",
            "Real module cleanup must preserve save, subscriptions, view, mailbox and cache order even when flush or Close fails");
        Check(replacement==null&&ReferenceEquals(DailyReportView.Instance,null)&&host.Module.Owner==null,
            "Real view cleanup must destroy its canvas and detach the module owner");
        Probe.Clear();host.Module.OnDestroy();
        Check(!Probe.Trace.Contains("view-close")&&!Probe.Trace.Contains("mailbox"),"Repeated module teardown must not reuse a destroyed view or detached host");

        Probe.Clear();ModBehaviour retry=Host();retry.Enabled=true;retry.Module.OnAwake(retry);Probe.Clear();
        Probe.CreateReturnsNull=true;retry.OpenDailyReportUI();
        Check(Probe.Creates==1&&Probe.Opens==0,"Null view creation must not call RefreshAndOpen");
        Probe.CreateReturnsNull=false;retry.OpenDailyReportUI();
        Check(Probe.Creates==2&&Probe.Opens==1,"Null view creation must retry on the next public request");
        UnityEngine.Object.Destroy(DailyReportView.Instance.gameObject);
        Probe.ThrowCreate=true;MustThrow(retry.OpenDailyReportUI,"View creation exceptions must retain their original propagation boundary");
        Probe.ThrowCreate=false;retry.OpenDailyReportUI();Probe.ThrowOpen=true;
        MustThrow(retry.OpenDailyReportUI,"View refresh exceptions must retain their original propagation boundary");
        Probe.ThrowOpen=false;retry.Module.OnDestroy();
        Console.WriteLine("DailyReportHostUI PASS: "+count+" assertions");return 0;
    }
}
