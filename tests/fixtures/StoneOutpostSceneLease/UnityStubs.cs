using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class GameObject
    {
        public string name;
        public bool activeSelf = true;
        public GameObject(string name) { this.name = name; }
        public void SetActive(bool value) { activeSelf = value; }
    }
    public class AsyncOperation
    {
        public bool isDone;
        public event Action<AsyncOperation> completed;
        public Action BeforeComplete;
        public void Complete()
        {
            isDone = true;
            if (BeforeComplete != null) BeforeComplete();
            if (completed != null) completed(this);
        }
    }
    public class AssetBundle
    {
        public static AssetBundle Last;
        public bool Unloaded;
        public static AssetBundle LoadFromFile(string path) { return Last = new AssetBundle(); }
        public string[] GetAllScenePaths() { return new[] { "assets/stoneoutpost/stoneoutpost.unity" }; }
        public void Unload(bool all) { Unloaded = all; }
    }
    public static class Debug
    {
        public static void Log(string text) { Console.WriteLine(text); }
        public static void LogWarning(string text) { Console.WriteLine(text); }
    }
}
namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single, Additive }
    public class SceneState
    {
        public string Path;
        public bool Loaded = true;
        public GameObject Root;
    }
    public struct Scene
    {
        internal SceneState State;
        public string path { get { return State == null ? null : State.Path; } }
        public bool isLoaded { get { return State != null && State.Loaded; } }
        public int handle { get { return State == null ? 0 : State.GetHashCode(); } }
        public bool IsValid() { return State != null; }
        public GameObject[] GetRootGameObjects() { return new[] { State.Root }; }
    }
    public static class SceneManager
    {
        private static readonly List<Scene> Scenes = new List<Scene>();
        public static AsyncOperation PendingUnload;
        public static int sceneCount { get { return Scenes.Count; } }
        public static void Reset() { Scenes.Clear(); PendingUnload = null; }
        public static Scene GetSceneAt(int index) { return Scenes[index]; }
        public static void Add(string path, GameObject root) { Scenes.Add(new Scene { State = new SceneState { Path = path, Root = root } }); }
        public static Scene GetSceneByPath(string path) { return Scenes.Find(scene => scene.path == path); }
        public static AsyncOperation LoadSceneAsync(string path, LoadSceneMode mode) { return new AsyncOperation(); }
        public static AsyncOperation UnloadSceneAsync(Scene scene)
        {
            return PendingUnload = new AsyncOperation { BeforeComplete = () => scene.State.Loaded = false };
        }
    }
}
