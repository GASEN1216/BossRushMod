using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        private static readonly List<Object> Objects = new List<Object>();
        public string name;
        protected Object() { Objects.Add(this); }
        public static T FindObjectOfType<T>() where T : Object { return Objects.OfType<T>().FirstOrDefault(); }
        public static T[] FindObjectsOfType<T>() where T : Object { return Objects.OfType<T>().ToArray(); }
        public static void Destroy(Object obj)
        {
            GameObject go = obj as GameObject;
            if (go != null)
                foreach (Component component in go.Components.ToArray()) Destroy(component);
            Component own = obj as Component;
            if (own != null)
            {
                own.InvokeLifecycle("OnDisable");
                own.InvokeLifecycle("OnDestroy");
                own.gameObject.Components.Remove(own);
            }
            Objects.Remove(obj);
        }
        public static void DontDestroyOnLoad(Object obj) { }
    }
    public struct Vector3 { }
    public sealed class Transform
    {
        public Vector3 position;
        public void SetParent(Transform parent) { }
    }
    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public readonly Transform transform = new Transform();
        public GameObject(string value) { name = value; }
        public T AddComponent<T>() where T : Component
        {
            T component = (T)Activator.CreateInstance(typeof(T));
            component.gameObject = this;
            component.name = name;
            Components.Add(component);
            component.InvokeLifecycle("Awake");
            component.InvokeLifecycle("OnEnable");
            return component;
        }
        public T GetComponent<T>() where T : class { return Components.OfType<T>().FirstOrDefault(); }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>() where T : class { return GetComponent<T>(); }
        internal void InvokeLifecycle(string method)
        {
            MethodInfo target = GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (target != null) target.Invoke(this, null);
        }
    }
    public class MonoBehaviour : Component { }
    public enum KeyCode { Space }
    public static class Input
    {
        public static bool GetKey(KeyCode key) { return false; }
        public static bool GetKeyDown(KeyCode key) { return false; }
    }
    public static class Time { public static float deltaTime = 0.02f; }
}
namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single, Additive }
    public struct Scene { public string name; public string path; }
    public static class SceneManager
    {
        public static event Action<Scene, LoadSceneMode> sceneLoaded;
        public static event Action<Scene> sceneUnloaded;
        public static void Load(Scene scene) { if (sceneLoaded != null) sceneLoaded(scene, LoadSceneMode.Additive); }
        public static void Unload(Scene scene) { if (sceneUnloaded != null) sceneUnloaded(scene); }
    }
}
namespace ItemStatsSystem
{
    public class Item : UnityEngine.Component
    {
        public int TypeID;
        public readonly List<Items.Slot> Slots = new List<Items.Slot>();
        public bool GetBool(string key) { return false; }
    }
}
namespace ItemStatsSystem.Items
{
    public sealed class Slot { public string Key; public Item Content; }
}
public class CharacterMainControl : UnityEngine.MonoBehaviour
{
    public static CharacterMainControl Main;
    public static event Action<CharacterMainControl, ItemStatsSystem.Items.Slot> OnMainCharacterSlotContentChangedEvent;
    public object dashAction = new object();
    public bool StartAction(BossRush.Common.Equipment.EquipmentAbilityAction action) { return true; }
    public void SetSlot(ItemStatsSystem.Items.Slot slot, ItemStatsSystem.Item item)
    {
        slot.Content = item;
        if (OnMainCharacterSlotContentChangedEvent != null) OnMainCharacterSlotContentChangedEvent(this, slot);
    }
}
namespace BossRush.Common.Equipment
{
    // 飞行动作与物理/输入 API 为替身；装备与能力管理器及 Dash 保存/恢复使用生产源码。
    public class EquipmentAbilityAction : UnityEngine.MonoBehaviour
    {
        public CharacterMainControl characterController;
        public bool Running;
        public void ResetState() { Running = false; }
        public void StopAction() { Running = false; }
        public bool IsReady() { return true; }
    }
}
namespace BossRush.Common.Utils
{
    public static class ReflectionCache
    {
        public static Type GetType(string name, string assembly) { return null; }
        public static FieldInfo GetField(Type type, string name, BindingFlags flags) { return type.GetField(name, flags); }
        public static MethodInfo GetMethod(Type type, string name, BindingFlags flags) { return type.GetMethod(name, flags); }
        public static PropertyInfo GetProperty(Type type, string name, BindingFlags flags) { return type.GetProperty(name, flags); }
    }
}
namespace BossRush
{
    public static class ModBehaviour
    {
        public static void DevLog(string message) { }
        public static bool CanRunGameplayRuntimeCached() { return true; }
    }
    public sealed class CA_Flight : Common.Equipment.EquipmentAbilityAction
    {
        public void UpdateAction(float delta) { }
        public bool StartActionByCharacter(CharacterMainControl character) { return true; }
    }
    public static class AchievementTracker { public static void OnUseFlightTotem() { } }
    public static class BossRushAchievementManager { public static void TryUnlock(string key) { } }
}
