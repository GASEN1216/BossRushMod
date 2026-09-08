using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// COMPAT / WIRE+：独立 Scene AssetBundle 的所有权。保留基地的 LevelManager 服务，
    /// 自建场景只承载环境资源；不伪造 buildIndex，不写入官方场景/存档登记。
    /// </summary>
    internal sealed class StoneOutpostSceneLease
    {
        internal const string ScenePath = SceneRuntimeGate.StoneOutpostResourceScenePath;
        internal const string BundleRelativePath = "Assets/arenas/stone_outpost";
        private AssetBundle bundle;
        private AsyncOperation loading;
        private Scene scene;
        private string loadedPath;
        private bool released;
        private bool unloading;
        private Action onReleased;
        internal bool Loaded { get { return scene.IsValid() && scene.isLoaded; } }
        internal bool LoadResolved { get; private set; }
        internal string LoadError { get; private set; }
        internal GameObject Root { get; private set; }

        internal static bool IsResourceScene(Scene candidate)
        {
            return string.Equals(candidate.path, ScenePath, StringComparison.OrdinalIgnoreCase);
        }

        internal AsyncOperation BeginLoad(string modDirectory)
        {
            string path = Path.Combine(modDirectory, BundleRelativePath);
            if (!File.Exists(path)) throw new FileNotFoundException("缺少石堡前哨场景资源包", path);
            if (SceneManager.GetSceneByPath(ScenePath).isLoaded)
                throw new InvalidOperationException("前哨场景仍在加载或回收，请稍后再试");
            bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) throw new InvalidOperationException("前哨场景包加载失败");
            string[] scenes = bundle.GetAllScenePaths();
            if (scenes.Length != 1 || !string.Equals(scenes[0], ScenePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("前哨场景包路径不匹配");
            loadedPath = scenes[0];
            loading = SceneManager.LoadSceneAsync(loadedPath, LoadSceneMode.Additive);
            if (loading == null) throw new InvalidOperationException("前哨场景加载未启动");
            loading.completed += OnLoaded;
            return loading;
        }

        private void OnLoaded(AsyncOperation operation)
        {
            operation.completed -= OnLoaded;
            loading = null;
            try
            {
                // Bundle 路径会被规范化成小写；按实际已加载 Scene.path 精确忽略大小写匹配。
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene candidate = SceneManager.GetSceneAt(i);
                    if (IsResourceScene(candidate) && candidate.isLoaded) { scene = candidate; break; }
                }
                if (!Loaded) throw new InvalidOperationException("加载回调中找不到已加载的前哨场景：" + loadedPath);
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (root.name == "StoneOutpost") { Root = root; break; }
                if (Root == null) throw new InvalidOperationException("前哨场景缺少 StoneOutpost 根对象");
                Debug.Log("[StoneOutpost] SCENE_LOADED path=" + scene.path + " handle=" + scene.handle);
            }
            catch (Exception e)
            {
                LoadError = e.Message;
                Debug.LogWarning("[StoneOutpost] SCENE_RESOLVE_FAILED " + LoadError);
            }
            finally { LoadResolved = true; }
            if (released) { Unload(); return; }
        }

        internal void Release(Action completed)
        {
            if (completed != null) onReleased += completed;
            released = true;
            // Unity 场景加载不能取消；completed 回调持有租约，迟到加载也必须卸载。
            if (loading == null) Unload();
        }

        private void Unload()
        {
            if (unloading) return;
            unloading = true;
            if (Root != null) Root.SetActive(false);
            if (Loaded)
            {
                AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
                if (operation != null) { operation.completed += OnUnloaded; return; }
            }
            FinishRelease();
        }

        private void OnUnloaded(AsyncOperation operation)
        {
            operation.completed -= OnUnloaded;
            FinishRelease();
        }

        private void FinishRelease()
        {
            if (bundle != null) bundle.Unload(true);
            bundle = null;
            Root = null;
            Debug.Log("[StoneOutpost] SCENE_UNLOADED");
            Action callback = onReleased;
            onReleased = null;
            if (callback != null) callback();
        }
    }
}
