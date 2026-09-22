using System;
using ItemStatsSystem;
using UnityEngine;
namespace UnityEngine {
 public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} public static Vector3 forward=>new Vector3(0,0,1);public static Vector3 up=>new Vector3(0,1,0);public float sqrMagnitude=>x*x+y*y+z*z;public Vector3 normalized=>this; }
 public struct Quaternion {public static Quaternion LookRotation(Vector3 a,Vector3 b)=>new Quaternion();}
 public class Transform {public Vector3 position;public Quaternion rotation;public Vector3 forward=Vector3.forward;}
 public static class Mathf {public static float Max(float a,float b)=>Math.Max(a,b);}
}
namespace Duckov.Buffs {public class Buff{}}
namespace ItemStatsSystem {public class ItemTypeIDAttribute:Attribute{} public class ItemSetting_Gun{public Projectile bulletPfb;}}
namespace Duckov.Utilities {public static class GameplayDataSettings{public sealed class PrefabsData{public Projectile DefaultBullet;}public static PrefabsData Prefabs;}}
public enum Teams {player,wolf}
public enum ControlMindTypes {none}
// 仅声明生产 builder 访问的宿主字段。官方 ProjectileContext 是无构造器的 struct，
// default 时所有数值字段为 0；尤其不能在替身里把 damageFactorToZombie 预置为 1。
public struct ProjectileContext
{
 public Vector3 direction;
 public float speed;
 public float distance;
 public float halfDamageDistance;
 public float damage;
 public float damageFactorToZombie;
 public int penetrate;
 public float critRate;
 public float critDamageFactor;
 public float armorPiercing;
 public float armorBreak;
 public CharacterMainControl fromCharacter;
 public CharacterMainControl realFromCharacter;
 public Teams team;
 public int fromWeaponItemID;
 public bool firstFrameCheck;
}
public class Projectile {public Transform transform=new Transform();public ProjectileContext context;public void Init(ProjectileContext value){context=value;}}
public class BulletPool {public Projectile Captured;public Projectile GetABullet(Projectile prefab){Captured=new Projectile();return Captured;}}
public class LevelManager {public static LevelManager Instance;public BulletPool BulletPool;}
public class ItemAgent_Gun {public ItemSetting_Gun GunItemSetting=new ItemSetting_Gun();public float BulletSpeed=30,BulletDistance=60,Damage=100,CritDamageFactor=2,ArmorPiercing=10,ArmorBreak=1;public float DamageFactorToZombie=1.5f;}
public class CharacterMainControl{public static CharacterMainControl Main;public Transform transform=new Transform();public Teams Team=Teams.player;public ItemAgent_Gun Gun=new ItemAgent_Gun();public ItemAgent_Gun GetGun()=>Gun;}
namespace BossRush {public partial class ModBehaviour{public bool Spawn(float factor)=>TrySpawnZombieModePlayerSupportProjectile(new Vector3(),Vector3.forward,factor,0.6f);}}
static class Program {
 static int Main(){CharacterMainControl.Main=new CharacterMainControl();CharacterMainControl.Main.Gun.GunItemSetting.bulletPfb=new Projectile();LevelManager.Instance=new LevelManager{BulletPool=new BulletPool()};var mod=new BossRush.ModBehaviour();int fail=0;
 foreach(float factor in new[]{0.40f,0.35f,0.45f}){bool made=mod.Spawn(factor);ProjectileContext ctx=LevelManager.Instance.BulletPool.Captured.context;Console.WriteLine("builder factor="+factor+" spawned="+made+" rawDamage="+ctx.damage+" damageFactorToZombie="+ctx.damageFactorToZombie+" weaponID="+ctx.fromWeaponItemID);if(!made||ctx.damage<=0){Console.WriteLine("FAIL positive base-damage control");fail++;}else Console.WriteLine("PASS positive base-damage control");if(ctx.damageFactorToZombie!=CharacterMainControl.Main.Gun.DamageFactorToZombie || ctx.fromWeaponItemID != 0){Console.WriteLine("FAIL support projectile must retain positive zombie damage factor");fail++;}else Console.WriteLine("PASS zombie damage factor");}
 Console.WriteLine("Note: the production builder is extracted verbatim; ProjectileContext is a minimal host contract stub preserving official zero defaults. Official Projectile/Health damage consumption and Unity physics are outside this probe.");return fail==0?0:1;}
}
