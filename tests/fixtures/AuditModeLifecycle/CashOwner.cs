using System;
using System.Threading.Tasks;
namespace UnityEngine {
 public class Object {public bool Destroyed;public static bool operator ==(Object a,Object b){bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed;return an||bn?an==bn:ReferenceEquals(a,b);}public static bool operator !=(Object a,Object b){return !(a==b);}public override bool Equals(object o){return this==o as Object;}public override int GetHashCode(){return base.GetHashCode();}}
 public struct Vector3 {public float y;public float sqrMagnitude{get{return 1;}}public Vector3 normalized{get{return this;}}public static Vector3 forward,up;public void Normalize(){}public static Vector3 operator +(Vector3 a,Vector3 b){return a;}public static Vector3 operator *(Vector3 a,float b){return a;}}
 public static class Mathf {public static int Max(int a,int b){return Math.Max(a,b);}public static float Max(float a,float b){return Math.Max(a,b);}}
 public static class Random {public static Vector3 insideUnitSphere;public static float Range(float a,float b){return a;}}
}
namespace UnityEngine.SceneManagement {public struct Scene{public int buildIndex;}public static class SceneManager{public static int Scene=1;public static Scene GetActiveScene(){return new Scene{buildIndex=Scene};}}}
namespace ItemStatsSystem {
 public class Item {public int MaxStackCount=9999999,StackCount;public bool Dropped,Destroyed;public void Drop(UnityEngine.Vector3 p,bool a,UnityEngine.Vector3 d,float f){Dropped=true;}public void DestroyTree(){Destroyed=true;}}
 public static class ItemAssetsCollection {public static TaskCompletionSource<Item> Pending;public static int Requests;public static Task<Item> InstantiateAsync(int id){Requests++;Pending=new TaskCompletionSource<Item>();return Pending.Task;}}
}
namespace BossRush {
 public static class RandomEventsTuning {public const int CashItemTypeID=451;public const string LogPrefix="";}
 public static class SpawnPositionHelper {public static UnityEngine.Vector3 SnapToGround(UnityEngine.Vector3 p){return p;}}
 public partial class ModBehaviour : UnityEngine.Object {
  public bool Valid=true;public int Completed,Spawned;
  internal static void DevLog(string s){}
  public Task Begin(int piles){return new RandomEventsRuntimeModule(this).Begin(piles,(r,s)=>{Completed++;Spawned=s;},()=>Valid);}
 }
 internal sealed partial class RandomEventsRuntimeModule {
  private readonly ModBehaviour _owner;
  internal RandomEventsRuntimeModule(ModBehaviour owner){_owner=owner;}
  internal Task Begin(int piles,Action<int,int> complete,Func<bool> valid){return SpawnRandomEventCashPilesAsync(new UnityEngine.Vector3(),100000,piles,5,complete,valid);}
 }
 public static class Program {
  static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("PASS "+s);}
  public static async Task Main(){
   foreach(bool fault in new[]{false,true}){
    ItemStatsSystem.ItemAssetsCollection.Requests=0;var owner=new ModBehaviour();var task=owner.Begin(3);var pending=ItemStatsSystem.ItemAssetsCollection.Pending;
    owner.Valid=false;var late=new ItemStatsSystem.Item();
    if(fault)pending.SetException(new InvalidOperationException());else pending.SetResult(late);
    await task;Check(owner.Completed==0&&ItemStatsSystem.ItemAssetsCollection.Requests==1,"cancelled rain does not schedule more piles or report completion "+fault);
    if(!fault)Check(late.Destroyed&&!late.Dropped,"late uncommitted cash destroyed");
   }
   var destroyed=new ModBehaviour();var destroyedTask=destroyed.Begin(2);destroyed.Destroyed=true;var orphan=new ItemStatsSystem.Item();ItemStatsSystem.ItemAssetsCollection.Pending.SetResult(orphan);await destroyedTask;
   Check(orphan.Destroyed&&!orphan.Dropped&&destroyed.Completed==0,"destroyed Unity owner cancels late cash and completion");
   var active=new ModBehaviour();var firstTask=active.Begin(1);var landed=new ItemStatsSystem.Item();ItemStatsSystem.ItemAssetsCollection.Pending.SetResult(landed);await firstTask;
   Check(landed.Dropped&&!landed.Destroyed&&active.Completed==1&&active.Spawned==1,"active rain produces one pile and one completion");
   active.Valid=false;Check(landed.Dropped&&!landed.Destroyed,"cleanup retains previously landed cash");
  }
 }
}
