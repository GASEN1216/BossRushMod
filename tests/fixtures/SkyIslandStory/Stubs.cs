using System;
using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>故障恢复节流用的时钟替身；测试显式推进，不依赖真实帧。</summary>
    public static class Time { public static float unscaledTime; }
    public static class Debug { public static void LogWarning(string value) { } }

    /// <summary>最小 Unity 对象模型：只为验证「待保存 owner 独立于 Mod 宿主」的销毁语义。</summary>
    public class Object
    {
        internal bool Destroyed;
        internal static readonly List<Object> Live = new List<Object>();
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value)
        {
            if (value == null || value.Destroyed) return;
            GameObject go = value as GameObject;
            if (go != null) foreach (Component part in go.Parts.ToArray()) Destroy(part);
            value.Destroyed = true;
            Live.Remove(value);
            MonoBehaviour behaviour = value as MonoBehaviour;
            if (behaviour != null) behaviour.Raise("OnDestroy");
        }
        public static T[] FindObjectsOfType<T>(bool includeInactive) where T : Object
        {
            var found = new List<T>();
            foreach (Object candidate in Live.ToArray()) { T typed = candidate as T; if (typed != null) found.Add(typed); }
            return found.ToArray();
        }
    }
    public class Component : Object { public GameObject gameObject; }
    public class MonoBehaviour : Component
    {
        internal void Raise(string method)
        {
            System.Reflection.MethodInfo target = GetType().GetMethod(method, System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (target != null) target.Invoke(this, null);
        }
        /// <summary>推进所有存活的 MonoBehaviour 一帧，等价于 Unity 调用各自的 Update。</summary>
        internal static void PumpAll()
        {
            foreach (Object candidate in Live.ToArray())
            {
                MonoBehaviour behaviour = candidate as MonoBehaviour;
                if (behaviour != null && !behaviour.Destroyed) behaviour.Raise("Update");
            }
        }
    }
    public class GameObject : Object
    {
        public string name;
        internal readonly List<Component> Parts = new List<Component>();
        public GameObject(string value) { name = value; Live.Add(this); }
        public T AddComponent<T>() where T : Component, new()
        { T part = new T { gameObject = this }; Parts.Add(part); Live.Add(part); return part; }
    }
}
namespace BossRush
{
    internal static class ModBehaviour
    {
        internal static void DevLog(string value) { }
        internal static string GetModPath() { return Environment.CurrentDirectory; }
        internal static void CriticalLog(string key, string value) { }
    }
    internal static class BossRushSaveFileThrottle { internal static bool TryBeginSaveFile(bool bypass) { return true; } }
}
internal sealed class LevelManager
{
    internal static LevelManager Instance = new LevelManager();
    internal bool IsBaseLevel = true;
}
namespace Saves
{
    internal static class SavesSystem
    {
        internal static event Action OnCollectSaveData, OnSetFile, OnSaveDeleted;
        internal static int CurrentSlot;
        internal static bool IsSaving, FailPhysical, FailKeyWrite;
        internal static int PhysicalWrites;
        private static readonly Dictionary<int, Dictionary<string, string>> slots = new Dictionary<int, Dictionary<string, string>>();
        private static Dictionary<string, string> Data
        {
            get { if (!slots.ContainsKey(CurrentSlot)) slots[CurrentSlot] = new Dictionary<string, string>(); return slots[CurrentSlot]; }
        }
        internal static bool KeyExisits(string key) { return Data.ContainsKey(key); }
        internal static T Load<T>(string key) { return (T)(object)Data[key]; }
        internal static void Save<T>(string key, T value)
        {
            // 键写入异常是 CR-2026-09-08-003 的起点：它会让共享 store 进入单向 StoreFaulted。
            if (FailKeyWrite && key != "SaveTime") throw new InvalidOperationException("key write unavailable");
            Data[key] = (string)(object)value;
        }
        /// <summary>
        /// 按当前游戏 DLL 的真实语义建模（U4 替身偏差修正）：
        /// 官方签名是 <c>SaveFile(bool writeSaveTime = true)</c>，参数只决定是否写 SaveTime 键，
        /// **不触发** OnCollectSaveData；方法体 saving=true -> ES3.StoreCachedFile -> saving=false 没有
        /// try/finally，所以物理写异常之后 IsSaving 会一直停在 true。旧替身把参数当 collect，
        /// 并在失败后自动复位，掩盖了这条真实状态。
        /// </summary>
        internal static void SaveFile(bool writeSaveTime = true)
        {
            if (writeSaveTime) Data["SaveTime"] = DateTime.UtcNow.ToBinary().ToString();
            IsSaving = true;
            if (FailPhysical) throw new InvalidOperationException("disk unavailable");
            PhysicalWrites++;
            IsSaving = false;
        }
        /// <summary>模拟官方在异常之后重新走完一次完整保存，把卡住的 saving 状态放开。</summary>
        internal static void ClearStuckSaving() { IsSaving = false; }
        internal static void Switch(int slot) { CurrentSlot = slot; if (OnSetFile != null) OnSetFile(); }
        internal static void DeleteCurrent() { Data.Clear(); if (OnSaveDeleted != null) OnSaveDeleted(); }
        internal static int Subscribers { get { return (OnSetFile == null ? 0 : OnSetFile.GetInvocationList().Length); } }
    }
}
