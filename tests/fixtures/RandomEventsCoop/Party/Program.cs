using System;using System.Collections.Generic;using UnityEngine;using BossRush;
namespace UnityEngine {
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public float sqrMagnitude {get{return x*x+y*y+z*z;}}public static Vector3 zero {get{return new Vector3();}}public static Vector3 forward {get{return new Vector3(0,0,1);}}public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}public static Vector3 operator *(Vector3 a,float b){return new Vector3(a.x*b,a.y*b,a.z*b);}public static float Distance(Vector3 a,Vector3 b){return (float)Math.Sqrt((a-b).sqrMagnitude);}}
 public sealed class Transform {public Vector3 position;}
 public static class Mathf {public static float Max(float a,float b){return Math.Max(a,b);}}
 public static class Random {internal static readonly System.Random Generator=new System.Random(194);public static int Range(int a,int b){return Generator.Next(a,b);}}
}
public sealed class Health {public bool IsDead;}
public sealed class CharacterMainControl {public static CharacterMainControl Main;public Health Health=new Health();public Transform transform=new Transform();}
namespace BossRush {
 internal sealed class BossRushCoopPlayer {internal bool IsAlive=true;internal CharacterMainControl Character;}
 internal static class BossRushCoop {internal static bool IsConnected=true,IsHost=true;internal static readonly List<BossRushCoopPlayer> Players=new List<BossRushCoopPlayer>();
 internal static CharacterMainControl GetCombatTarget(Vector3 at){CharacterMainControl best=null;float distance=float.MaxValue;foreach(var member in Players){var player=member.Character;if(!member.IsAlive||player==null||player.Health==null||player.Health.IsDead)continue;float d=(player.transform.position-at).sqrMagnitude;if(d<distance){best=player;distance=d;}}return best;}}
 internal sealed class ModBehaviour {internal static void DevLog(string text){}}
 internal static class RandomEventsTuning {internal const string LogPrefix="test";}
 internal partial class RandomEventEffectsBridge {internal Vector3[] Points=new Vector3[0];private Vector3[] GetRandomEventSpawnPointsSafe(){return Points;}}
 internal static partial class RandomEventEffectHelpers {}
 internal static class SpawnPositionHelper {
  internal const float DefaultLiftOffset=.5f;internal static bool Around;internal static Vector3 AroundValue;
  internal static Vector3 SnapToGround(Vector3 value){return value;}internal static bool TryFindAroundPlayer(Vector3 at,int count,float distance,out Vector3 value,float lift,float minimum){value=AroundValue;return Around;}
  internal static Vector3 FindNearestSafeSpawnPoint(Vector3[] points,Vector3 at,float minimum){Vector3 best=Vector3.zero;float distance=float.MaxValue;foreach(var p in points){float d=(p-at).sqrMagnitude;if(d>=minimum*minimum&&d<distance){best=p;distance=d;}}return best;}
 }
}
internal static class Program {
 static int checks;static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
 static int Main(){
  var host=new CharacterMainControl();host.transform.position=new Vector3(0,0,0);CharacterMainControl.Main=host;
  var guest=new CharacterMainControl();guest.transform.position=new Vector3(1000,0,0);
  var third=new CharacterMainControl();third.transform.position=new Vector3(2000,0,0);
  BossRushCoop.Players.Add(new BossRushCoopPlayer{Character=host});BossRushCoop.Players.Add(new BossRushCoopPlayer{Character=guest});BossRushCoop.Players.Add(new BossRushCoopPlayer{Character=third});
  var seen=new HashSet<CharacterMainControl>();for(int i=0;i<300;i++)seen.Add(RandomEventEffectHelpers.ResolveAlivePlayer());Check(seen.Count==3,"actual reservoir can select all alive participants independent of origin");
  host.Health.IsDead=true;BossRushCoop.Players[0].IsAlive=false;for(int i=0;i<20;i++)Check(RandomEventEffectHelpers.ResolveAlivePlayer()!=host,"dead host never becomes random event anchor");
  var owner=new RandomEventEffectsBridge {Points=new[]{new Vector3(10,0,0),new Vector3(1010,0,0),new Vector3(2010,0,0),new Vector3(1100,0,0)}};
  for(int i=0;i<30;i++){Vector3 pos=owner.GetRandomEventSafePointAwayFromPlayer(50);Check(Vector3.Distance(pos,guest.transform.position)>=50&&Vector3.Distance(pos,third.transform.position)>=50,"actual intrusion preset rejects proximity to each guest");}
  owner.Points=new[]{new Vector3(1000,0,0)};SpawnPositionHelper.Around=true;SpawnPositionHelper.AroundValue=third.transform.position;
  Vector3 fallback=owner.GetRandomEventSafePointAwayFromPlayer(50);Check(Vector3.Distance(fallback,guest.transform.position)>=50&&Vector3.Distance(fallback,third.transform.position)>=50,"unsafe nav fallback is replaced by party envelope fallback");
  BossRushCoop.IsConnected=false;host.Health.IsDead=false;host.transform.position=new Vector3(1,0,0);Check(RandomEventEffectHelpers.ResolveAlivePlayer()==host,"offline anchor remains actual Main");owner.Points=new[]{new Vector3(51,0,0),new Vector3(1000,0,0)};Vector3 solo=owner.GetRandomEventSafePointAwayFromPlayer(50);Check(solo.x==51,"single player preserves existing safe spawn helper");
  Console.WriteLine("PASS RandomEventPartySafety: "+checks+" actual selection and intrusion safe-point checks");return 0;
 }
}
