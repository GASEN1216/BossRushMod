using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace UnityEngine {
 public struct Vector3 { }
 public static class Time { public static float realtimeSinceStartup; }
}
namespace BossRush {
 public enum Teams { scav, wolf }
 public class CharacterRandomPreset { }
 public class Health { public int GetInstanceID(){return 1;} }
 public class ModeHSpawnHandle { public Health Health=new Health(); public int Recycles; }
 public class ModeHSpawnDiagnostics { }
 public class ModeHCommandCertificationProbe { public void Dispose() { } }
 public static class ModeHSpawnBridge {
  public static TaskCompletionSource<ModeHSpawnHandle> Pending;
  public static Task<ModeHSpawnHandle> CreateIsolatedAsync(CharacterRandomPreset p,string k,Teams t,UnityEngine.Vector3 v,ModeHSpawnDiagnostics d){Pending=new TaskCompletionSource<ModeHSpawnHandle>();return Pending.Task;}
  public static void Recycle(ModeHSpawnHandle h){if(h!=null)h.Recycles++;}
 }
 public partial class CertificationOwner {
  private bool _cancelled, _running;
  private int _diagnosticGeneration;
  private ModeHSpawnHandle _activeScavHandle, _activeWolfHandle;
  private ModeHCommandCertificationProbe _commandProbe;
  private ModeHSpawnDiagnostics _diagnostics=new ModeHSpawnDiagnostics();
  private HashSet<int> _diagnosticHealthIds=new HashSet<int>();
  private void UnbindDiagnosticSink(){}
  public Task<ModeHSpawnHandle> Begin(Teams t){_cancelled=false;return CreateOwnedDiagnosticAsync(new CharacterRandomPreset(),"boss",t,new UnityEngine.Vector3(),_diagnosticGeneration,15);}
  public void NewGeneration(){_diagnosticGeneration++;_cancelled=false;}
 }
 public static class Program {
  static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("PASS "+s);}
  public static async Task Main(){
   foreach(var team in new []{Teams.scav,Teams.wolf}) {
    UnityEngine.Time.realtimeSinceStartup=0;
    var owner=new CertificationOwner();var pending=owner.Begin(team);var source=ModeHSpawnBridge.Pending;
    owner.Cancel();var handle=new ModeHSpawnHandle();source.SetResult(handle);
    Check(await pending==null && handle.Recycles==1,"late success after cancel recycled "+team);
    owner=new CertificationOwner();pending=owner.Begin(team);source=ModeHSpawnBridge.Pending;
    UnityEngine.Time.realtimeSinceStartup=16;handle=new ModeHSpawnHandle();source.SetResult(handle);
    Check(await pending==null && handle.Recycles==1,"timeout late success recycled "+team);
    UnityEngine.Time.realtimeSinceStartup=0;
    owner=new CertificationOwner();pending=owner.Begin(team);source=ModeHSpawnBridge.Pending;
    owner.NewGeneration();handle=new ModeHSpawnHandle();source.SetResult(handle);
    Check(await pending==null && handle.Recycles==1,"old generation late success recycled "+team);
    owner=new CertificationOwner();pending=owner.Begin(team);source=ModeHSpawnBridge.Pending;
    handle=new ModeHSpawnHandle();source.SetResult(handle);
    Check(await pending==handle && handle.Recycles==0,"current result owned "+team);
    owner.Cancel();owner.Cancel();Check(handle.Recycles==1,"cancel before coroutine consumes result exactly once "+team);
    owner=new CertificationOwner();pending=owner.Begin(team);source=ModeHSpawnBridge.Pending;
    owner.Cancel();source.SetException(new InvalidOperationException("late create failure"));
    Check(await pending==null,"cancelled owner consumes late factory fault "+team);
    owner=new CertificationOwner();pending=owner.Begin(team);source=ModeHSpawnBridge.Pending;
    source.SetException(new InvalidOperationException("current create failure"));
    bool propagated=false;try{await pending;}catch(InvalidOperationException){propagated=true;}
    Check(propagated,"current factory fault remains observable "+team);
   }
  }
 }
}
