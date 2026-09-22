using System;
namespace UnityEngine {public static class Time{public static float unscaledTime;}public static class Mathf{public static float Max(float a,float b){return Math.Max(a,b);}}}
public static class CameraMode {public static bool Active;}
namespace BossRush {
 public static class ZombieModeUIHelper {public static bool IsModalInputPaused;}
 public class RunState {public int RunId=1;}
 public partial class ModBehaviour {
  private RunState zombieModeRunState=new RunState();
  private float zombieModeRuntimePausedDuration;
  private float zombieModeRuntimePauseStartTime=-1;
  private int zombieModeRuntimePauseRunId;
  public bool MenuPaused;
  private bool IsZombieModeGamePaused(){return MenuPaused;}
  public void TickClock(float time){UnityEngine.Time.unscaledTime=time;RefreshZombieModeRuntimePauseClock();}
 }
 public static class Program {
  static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("PASS "+s);}
  static void Main(){
   var owner=new ModBehaviour();owner.TickClock(10);Check(owner.GetZombieModeRuntimeNow()==10,"normal runtime clock advances");
   CameraMode.Active=true;owner.TickClock(10);owner.TickClock(30);
   Check(owner.IsZombieModeRuntimePaused()&&owner.GetZombieModeRuntimeNow()==10,"photo mode freezes runtime deadline clock");
   CameraMode.Active=false;owner.TickClock(30);owner.TickClock(35);
   Check(!owner.IsZombieModeRuntimePaused()&&owner.GetZombieModeRuntimeNow()==15,"leaving photo mode resumes without charging paused duration");
   ZombieModeUIHelper.IsModalInputPaused=true;Check(owner.IsZombieModeRuntimePaused(),"reward-modal pause remains active");
   ZombieModeUIHelper.IsModalInputPaused=false;owner.MenuPaused=true;Check(owner.IsZombieModeRuntimePaused(),"pause-menu behavior preserved");
  }
 }
}
