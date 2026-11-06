using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ItemStatsSystem;

namespace UnityEngine
{
    public partial class Object { internal static Object Session;public static T FindObjectOfType<T>() where T:Object{return Session as T;}public static void Destroy(Object obj,float delay) { if(delay!=25f) throw new Exception("unexpected marker lifetime"); } }
    public partial class Component { public T GetComponent<T>() where T : Component { return gameObject.components.OfType<T>().FirstOrDefault(); } }
    public partial class GameObject { public bool activeInHierarchy { get { return activeSelf; } } }
    public partial struct Vector3
    {
        public static Vector3 zero {get{return new Vector3();}} public static Vector3 up {get{return new Vector3(0,1,0);}}
        public float magnitude {get{return (float)Math.Sqrt(sqrMagnitude);}}
        public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator *(Vector3 a,float s){return new Vector3(a.x*s,a.y*s,a.z*s);}
        public static Vector3 one {get{return new Vector3(1,1,1);}}
    }
    public class MonoBehaviour : Component { public bool enabled=true; }
    public partial class Transform { public Vector3 localScale; }
    public class LineRenderer : MonoBehaviour { }
    internal enum QueryTriggerInteraction { Ignore }
    internal static class Physics { internal static bool Wall; internal static bool Linecast(Vector3 a,Vector3 b,int mask,QueryTriggerInteraction t){return Wall;} }
    internal static class Mathf { internal static float Clamp01(float t){return Math.Max(0,Math.Min(1,t));} }
}
namespace Duckov.Utilities {internal static class GameplayDataSettings {internal static class Layers {internal const int wallLayerMask=2;}}}
namespace Cysharp.Threading.Tasks { internal static class TaskShim { internal static void Forget(this int v){} } }
namespace Duckov.UI.DialogueBubbles { internal static class DialogueBubblesManager { internal static int Show(string s,Transform t,float f,bool a,bool b,float c,float d){return 1;} } }
namespace Duckov.UI { internal static class NotificationText {internal static void Push(string s){} } }
namespace Duckov.MiniMaps { internal sealed class SimplePointOfInterest : Component {internal Color Color; internal bool IsArea; internal float AreaRadius;internal static SimplePointOfInterest Last; internal void Setup(object o,string key){if(key!=BossRush.TidegateLeadUsage.PinKey)throw new Exception("map key");Last=this;} } }
namespace ItemStatsSystem
{
    public class Item { public int TypeID; }
    public abstract class UsageBehavior : MonoBehaviour
    { public struct DisplaySettingsData {public bool display;public string description;} public abstract DisplaySettingsData DisplaySettings{get;} public abstract bool CanBeUsed(Item item,object user); protected abstract void OnUse(Item item,object user); public void Use(Item i,object c){if(CanBeUsed(i,c))OnUse(i,c);} }
    internal static class ItemAssetsCollection {public static Item GetPrefab(int id){return new Item{TypeID=id};} public static Item InstantiateSync(int id){return GetPrefab(id);}}
}
namespace BossRush
{
    internal sealed class Health {public bool IsDead;}
    internal sealed class CharacterModel : Component {}
    internal static class TidegateInventory {public static bool CarryingLead;public static bool Carries(CharacterMainControl p,int id){return CarryingLead&&p==CharacterMainControl.Main&&id==BossRushItemIds.TidegateSoundingLead;}}
    internal sealed class CharacterItem { public readonly List<Item> Inventory=new List<Item>();}
    internal sealed class CharacterMainControl : Component
    {public static CharacterMainControl Main; public readonly Health Health=new Health();public readonly object mainDamageReceiver=new object();public readonly CharacterItem CharacterItem=new CharacterItem();public CharacterModel characterModel;
        public event Action<DamageInfo> BeforeCharacterSpawnLootOnDead;public void Die(){BeforeCharacterSpawnLootOnDead?.Invoke(new DamageInfo());}
        public int Subscribers {get{return BeforeCharacterSpawnLootOnDead?.GetInvocationList().Length??0;}}}
    internal struct DamageInfo {}
    internal class AISpecialAttachmentBase {}
    internal sealed class CharacterRandomPreset {public string nameKey;public bool showName,showHealthBar,isBoss;public float health,forceTracePlayerDistance,patrolRange,damageMultiplier,shootDelay;public List<AISpecialAttachmentBase> specialAttachmentBases;}
    internal static class TidegateText {public const string MapBossKey="BossRush_TidegateMap_Boss";}
    internal sealed class AICharacterController {public object searchedEnemy;public float traceTargetChance,combatMoveRange;public Vector3 patrolPosition; public int Moves;public void MoveToPos(Vector3 at){Moves++;patrolPosition=at;}}
    internal sealed class TidegateWorld {public bool Ground=true; public bool TryGroundAt(Vector3 pos,out float y){y=0;return Ground;}}
    internal sealed class TidegateTide {public bool Deep;public bool IsDeepAt(Vector3 p){return Deep;}}
    internal static class SkyIslandBossForge
    {public static bool Material=true;public static LineRenderer Ring;public static int Hits;public static Vector3 Last;public static float LastRadius;
        public static LineRenderer CreateGroundRing(Transform parent,Vector3 at){if(!Material)throw new Exception("missing material");var go=new GameObject("ring");go.transform.position=at;go.transform.SetParent(parent,false);return Ring=go.AddComponent<LineRenderer>();}
        public static void SetRing(LineRenderer r,float range,float t,Color c){LastRadius=range;}
        public static void ReleaseRing(LineRenderer r){if(r!=null)r.gameObject.SetActive(false);}
        public static void Detonate(CharacterMainControl c,Vector3 at,float radius,float force,bool blast,float shake,Color col){Hits++;Last=at;LastRadius=radius;}
    }
    internal static class L10n {public static string T(string zh,string en){return en;}}
    internal static class BossRushItemIds {public const int TidegateSoundingLead=500116;}
    internal static class InteractableLootboxInventoryHelper {public static Action During;public static bool TryAddExtraItem(List<Item> inv,Item item){inv.Add(item);During?.Invoke();return true;}}
    internal static class SkyIslandInventoryTransaction {public static void DestroyUnowned(Item i){} }
    internal sealed partial class TidegateSession : MonoBehaviour
    {internal bool valid=true;internal GameObject root;internal TidegateTide tide;internal CharacterMainControl player;internal TidegateLoot loot;internal string Message;private bool IsSessionValid(){return valid;}private void Status(string s,bool error){Message=s;}}
    internal sealed partial class TidegateLoot
    {internal sealed class Point{public string Kind;public bool Opened,Failed; public Transform Marker;} internal readonly List<Point> points=new List<Point>(); }
}
