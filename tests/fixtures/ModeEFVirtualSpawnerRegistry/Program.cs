using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
        {
            bool an=ReferenceEquals(a,null)||a.Destroyed, bn=ReferenceEquals(b,null)||b.Destroyed;
            return an||bn ? an==bn : ReferenceEquals(a,b);
        }
        public static bool operator !=(Object a,Object b) { return !(a==b); }
        public override bool Equals(object o) { return ReferenceEquals(this,o); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void DontDestroyOnLoad(GameObject go) { go.Persistent=true; }
        public static void Destroy(GameObject go)
        {
            if(go==null)return;
            var root=go.Components.OfType<BossRush.CharacterSpawnerRoot>().FirstOrDefault();
            if(root!=null) BossRush.Program.DestroyCounts.Add(root.CreatedCharacters.Count);
            go.Destroyed=true;
            foreach(var c in go.Components)c.Destroyed=true;
        }
    }
    public class Component:Object { public GameObject gameObject; }
    public class GameObject:Object
    {
        public bool Persistent;
        public string name;
        public readonly List<Component> Components=new List<Component>();
        public GameObject(string name) { this.name=name; }
        public T AddComponent<T>() where T:Component,new()
        {
            var c=new T{gameObject=this}; Components.Add(c);
            var root=c as BossRush.CharacterSpawnerRoot;
            if(root!=null)BossRush.Program.Roots.Add(root);
            return c;
        }
    }
}
namespace BossRush
{
    using UnityEngine;
    public sealed class CharacterMainControl:Component { }
    public sealed class CharacterSpawnerRoot:Component
    {
        // Same shape as the official type: lower-case private field, public getter.
        private readonly List<CharacterMainControl> createdCharacters=new List<CharacterMainControl>();
        public List<CharacterMainControl> CreatedCharacters { get { return createdCharacters; } }
        public bool enabled=true;
        public void AddCreatedCharacter(CharacterMainControl character) { createdCharacters.Add(character); }
    }
    internal static class ModBehaviour { internal static void DevLog(string text) { } }
    internal static class Program
    {
        internal static readonly List<CharacterSpawnerRoot> Roots=new List<CharacterSpawnerRoot>();
        internal static readonly List<int> DestroyCounts=new List<int>();
        private static CharacterMainControl Character(string name)
        { return new GameObject(name).AddComponent<CharacterMainControl>(); }
        private static void Check(bool value,string label)
        { if(!value)throw new Exception(label); Console.WriteLine("PASS "+label); }
        private static void Main()
        {
            var registry=new ModeEFVirtualSpawnerRegistry(); var other=new ModeEFVirtualSpawnerRegistry();
            registry.CleanupModeEVirtualSpawnerRoot(); registry.RegisterModeEEnemyToSpawnerRoot(null);
            Check(Roots.Count==0,"empty cleanup and null registration allocate no virtual root");
            var a=Character("a"); var b=Character("b");
            registry.RegisterModeEEnemyToSpawnerRoot(a); var first=Roots.Single();
            Check(!first.enabled && first.gameObject.Persistent && first.gameObject.name=="ModeE_VirtualSpawnerRoot",
                  "virtual registry keeps the official spawn loop disabled and its original identity");
            first.CreatedCharacters.Add(null); first.CreatedCharacters.Add(a);
            registry.RegisterModeEEnemyToSpawnerRoot(a);
            Check(first.CreatedCharacters.Count==1 && ReferenceEquals(first.CreatedCharacters[0],a),
                  "re-registration removes duplicates and null entries before adding once");
            registry.RegisterModeEEnemyToSpawnerRoot(b); registry.UnregisterModeEEnemyFromSpawnerRoot(a);
            Check(first.CreatedCharacters.SequenceEqual(new[]{b}),"unregister removes the exact character from official list");
            registry.ClearRegisteredEnemies();
            Check(first.CreatedCharacters.SequenceEqual(new[]{b}),"early bookkeeping clear does not prematurely clear official list");
            other.RegisterModeEEnemyToSpawnerRoot(a); var second=Roots.Last();
            registry.CleanupModeEVirtualSpawnerRoot();
            Check(DestroyCounts.SequenceEqual(new[]{0}) && first==null && second!=null
                  && second.CreatedCharacters.SequenceEqual(new[]{a}),"cleanup clears official list before destruction and isolates owners");
            registry.CleanupModeEVirtualSpawnerRoot(); Check(DestroyCounts.Count==1,"repeated cleanup does not destroy twice");
            registry.RegisterModeEEnemyToSpawnerRoot(b);
            Check(Roots.Count==3 && Roots.Last().CreatedCharacters.SequenceEqual(new[]{b}),"registry can create a fresh root after cleanup");
            var dead=Character("dead"); UnityEngine.Object.Destroy(dead.gameObject);
            registry.RegisterModeEEnemyToSpawnerRoot(dead);
            Check(Roots.Last().CreatedCharacters.Count==1,"destroyed character is rejected using Unity fake null");
            UnityEngine.Object.Destroy(Roots.Last().gameObject);
            registry.RegisterModeEEnemyToSpawnerRoot(a);
            Check(Roots.Count==4 && Roots.Last().CreatedCharacters.SequenceEqual(new[]{a}),"externally destroyed root is replaced on next registration");
        }
    }
}
