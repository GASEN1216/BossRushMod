using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

// 原生碰撞夹具只替换游戏宿主边界。真实 Encounter、Visual 和官方轻量伤害代码单独复制。
public static class Random
{
    public static float Range(float a, float b) { return .5f; }
    public static float value { get { return UnityEngine.Random.value; } }
}
public enum Teams { wolf, player, all }
public struct DamageInfo
{
    public CharacterMainControl fromCharacter;
    public DamageReceiver toDamageReceiver;
    public float damageValue, critRate, critDamageFactor;
    public int crit;
    public bool isExplosion, isFromBuffOrEffect, ignoreDifficulty;
    public Vector3 damagePoint, damageNormal;
    public DamageInfo(CharacterMainControl player) { this = default(DamageInfo); fromCharacter = player; critDamageFactor = 1; crit = -1; }
}
public sealed class Health : MonoBehaviour
{
    public bool IsDead, IsMainCharacterHealth;
    public Teams team;
    public readonly UnityEvent<DamageInfo> OnDeadEvent = new UnityEvent<DamageInfo>();
    public bool Hurt(DamageInfo info) { return true; }
    public CharacterMainControl TryGetCharacter() { return GetComponent<CharacterMainControl>(); }
}
public sealed class LevelManager { public static LevelManager Instance = new LevelManager(); public CharacterMainControl ControllingCharacter; public CharacterMainControl MainCharacter { get { return CharacterMainControl.Main; } } }
public enum ExplosionFxTypes { normal, flash }
public static class Team { public static bool IsEnemy(Teams from, Teams to) { return from == Teams.all || to == Teams.all || from != to; } }
public static class CameraShaker { public enum CameraShakeTypes { explosion } public static void Shake(Vector3 direction, CameraShakeTypes type) { } }
public static class HardwareSyncingManager { public static void SetEvent(string name) { } }
public static class SceneLoader { public static bool IsSceneLoading; }
public class InteractableBase : MonoBehaviour { }
namespace ItemStatsSystem { public sealed class Item { public int TypeID; public string DisplayName = "Food"; } }
namespace Duckov.Buffs { public class Buff { } }
namespace Duckov.Economy { public static class EconomyManager { public const int CashItemID = 451; } }
namespace Duckov.Utilities { public static class GameplayDataSettings { public static class Layers { public static int wallLayerMask, groundLayerMask; public static int damageReceiverLayerMask = 1 << 8; } } }
namespace BossRush.Utils
{
    public static class NPCInteractionGroupHelper
    {
        public static List<InteractableBase> PrepareGroupedInteractionOwner(InteractableBase owner, string label) { return new List<InteractableBase>(); }
        public static T AddSubInteractable<T>(Transform parent, string name, List<InteractableBase> group, Action<T> bind) where T : InteractableBase
        { var root = new GameObject(name); root.transform.SetParent(parent, false); T child = root.AddComponent<T>(); bind(child); group.Add(child); return child; }
    }
}
namespace BossRush
{
    internal abstract class BossRushRuntimeModuleBase
    {
        public abstract string ModuleName { get; }
        public virtual void OnAwake(ModBehaviour host) { }
        public virtual void OnSceneLoaded(SceneRuntimeContext context) { }
        public virtual void OnUpdate(float deltaTime, float unscaledDeltaTime) { }
        public virtual void OnDestroy() { }
    }
    internal sealed class SceneRuntimeContext { internal LoadSceneMode LoadMode; }
    internal sealed class GreedyMimicInteractable : InteractableBase { internal void Bind(GreedyMimicEncounter encounter, int action) { } internal void RefreshName() { } }
    internal static class FieldAdventureSupport
    {
        internal static readonly List<string> Notices = new List<string>();
        internal static bool TryGetRaid(ModBehaviour owner, out CharacterMainControl player, out Scene scene)
        { player = CharacterMainControl.Main; scene = SceneManager.GetActiveScene(); return player != null && player.Health != null && !player.Health.IsDead; }
        internal static void Notify(string cn, string en) { Notices.Add(en); }
        internal static bool TryFindGround(Vector3 origin, float min, float max, out Vector3 position) { position = origin; return true; }
        internal static bool TryFindGroundBands(Vector3 origin, float[] bands, out Vector3 position) { position = origin; return true; }
    }
    internal static class L10n { internal static string T(string cn, string en) { return en; } }
    internal static class GreedyMimicInventory
    {
        internal static ItemStatsSystem.Item Find(CharacterMainControl player, GreedyMimicFood food, out int slot) { slot = 0; return new ItemStatsSystem.Item { TypeID = 100 + (int)food }; }
        internal static bool Consume(ItemStatsSystem.Item item, CharacterMainControl player, int slot, GreedyMimicFood food, Action growth) { growth(); return true; }
        internal static void Reward(int id, int count, Vector3 position) { }
    }
    internal static class BossRushConfirmDialog
    {
        internal sealed class Options { public string Title, Target, Body, Warning, ConfirmLabel, CancelLabel; public GameObject Anchor; public Action OnConfirm; }
        internal static Options Latest;
        internal static void Show(Options options) { Latest = options; }
    }
}
