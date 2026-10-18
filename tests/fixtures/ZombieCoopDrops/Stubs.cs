using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b) { bool an = ReferenceEquals(a, null) || a.Destroyed, bn = ReferenceEquals(b, null) || b.Destroyed; return an || bn ? an == bn : ReferenceEquals(a, b); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    public sealed class GameObject : Object { public readonly Transform transform = new Transform(); }
    public sealed class Transform { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public Vector3 normalized { get { return this; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public static class Random { public static Vector3 insideUnitSphere { get { return new Vector3(1, 0, 0); } } }
    public static class Mathf { public static int Clamp(int value, int min, int max) { return Math.Max(min, Math.Min(max, value)); } }
}

public sealed class CarryAction { public bool Running; public float Weight; public float GetWeight() { return Weight; } }
public sealed class CharacterMainControl : UnityEngine.Object
{
    public static CharacterMainControl Main;
    public readonly Item CharacterItem = new Item();
    public readonly Transform transform = new Transform();
    public CarryAction carryAction;
    public float MaxWeight = 50;
    public bool Dead;
}
namespace ItemStatsSystem
{
    public sealed class Inventory
    {
        internal int Calls; internal bool Accept = true;
        public bool AddAndMerge(Item item, int slot) { Calls++; return Accept; }
    }
    public sealed class Item
    {
        public GameObject gameObject = new GameObject();
        public Transform transform { get { return gameObject.transform; } }
        public string DisplayName = "Recorded supplies";
        public int Quality = 4;
        public float TotalWeight = 2;
        public Inventory Inventory = new Inventory();
        public int GroundDrops, FeetDrops;
        public bool ClaimMarked, MarkedAtGroundDrop;
        public Vector3 DropPosition;
        public CharacterMainControl DropPlayer;
        public float Force;
        public void Drop(Vector3 position, bool createAgent, Vector3 direction, float force) { GroundDrops++; MarkedAtGroundDrop = ClaimMarked; DropPosition = position; Force = force; }
        public void Drop(CharacterMainControl player, bool createAgent) { FeetDrops++; DropPlayer = player; DropPosition = player.transform.position; }
    }
    public static class ItemAssetsCollection
    {
        internal static int Calls;
        internal static Item Last;
        public static Item InstantiateSync(int id) { Calls++; return Last = new Item(); }
    }
}
namespace BossRush
{
    internal static class BossRushCoop
    {
        internal static bool IsConnected, Host;
        internal static bool HasWorldAuthority { get { return !IsConnected || Host; } }
    }
    internal static class L10n { internal static string T(string zh, string en) { return en; } }
    internal sealed class ModBehaviour { internal static void DevLog(string text) { } }
    internal enum ZombieModeRunOnlyObjectKind { Unknown }
    internal sealed class ZombieModeDropCandidate
    {
        internal GameObject GameObject;
        internal int WaveAtSpawn;
        internal float SpawnTime;
        internal bool HighValue, BossDrop;
    }
    internal sealed partial class ZombieModeRuntimeModule
    {
        internal sealed class State { internal int CurrentWave = 7; internal readonly List<ZombieModeDropCandidate> EntityDropCleanupCandidates = new List<ZombieModeDropCandidate>(); }
        internal readonly State runState = new State();
        internal readonly List<GameObject> Registered = new List<GameObject>();
        internal bool Valid = true;
        internal int Pickups, Spotted;
        private bool IsZombieModeRunValid(int run) { return Valid && run == 11; }
        private float GetZombieModeRuntimeNow() { return 20; }
        private void RegisterCoopZombieSharedDrop(int run, Item item) { item.ClaimMarked = true; }
        private void RegisterZombieModeRunOnlyObject(int run, ZombieModeRunOnlyObjectKind kind, GameObject go, object component, Action cleanup) { Registered.Add(go); }
        private void TryShowZombieModeInventoryPickupPopText(CharacterMainControl player, string name, int quality) { Pickups++; }
        private void TryShowZombieModeSpottedPickupPopText(CharacterMainControl player, string name, int quality) { Spotted++; }
        internal GameObject Deliver(Vector3 point, bool high = false, bool boss = false) { return TryDropZombieModeItemNearPosition(11, 123, point, high, boss); }
    }
}
