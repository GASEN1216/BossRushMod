using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.Scenes;

namespace BossRush
{
    /// <summary>
    /// 监听并记录所有传送相关的API调用，用于学习官方挑战船票的传送流程
    /// </summary>
    public class TeleportDebugMonitor : MonoBehaviour
    {
        private static TeleportDebugMonitor instance;
        private List<string> logBuffer = new List<string>();
        private bool monitoring = false;

        public static TeleportDebugMonitor Instance
        {
            get { return instance; }
        }

        void Awake()
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            // 订阅场景加载事件
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;

            // 订阅 SceneLoader 事件
            try
            {
                SubscribeToSceneLoaderEvents();
            }
            catch (Exception e)
            {
                Log("订阅 SceneLoader 事件失败: " + e.Message);
            }

            Log("=== 传送调试监听器已启动 ===");
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            instance = null;
        }

        /// <summary>
        /// 开始监听传送流程
        /// </summary>
        public void StartMonitoring()
        {
            monitoring = true;
            logBuffer.Clear();
            Log("========================================");
            Log("=== 开始监听传送流程 ===");
            Log("========================================");
            LogCurrentGameState();
        }

        /// <summary>
        /// 停止监听并输出日志
        /// </summary>
        public void StopMonitoring()
        {
            Log("========================================");
            Log("=== 监听结束 ===");
            Log("========================================");
            monitoring = false;

            // 输出完整日志
            ModBehaviour.DevLog("[TeleportMonitor] 完整传送流程日志:");
            foreach (var log in logBuffer)
            {
                ModBehaviour.DevLog("[TeleportMonitor] " + log);
            }
        }

        private void Log(string message)
        {
            string timestamped = "[" + Time.realtimeSinceStartup.ToString("F3") + "s] " + message;
            logBuffer.Add(timestamped);
            ModBehaviour.DevLog("[TeleportMonitor] " + timestamped);
        }

        private void LogCurrentGameState()
        {
            Log("当前游戏状态:");
            Log("  活动场景: " + SceneManager.GetActiveScene().name);
            Log("  已加载场景数: " + SceneManager.sceneCount);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                Log("    场景[" + i + "]: " + scene.name + " (已加载: " + scene.isLoaded + ")");
            }

            // 查找玩家
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null)
                {
                    Log("  玩家位置: " + main.transform.position);
                    Log("  玩家场景: " + main.gameObject.scene.name);
                }
                else
                {
                    Log("  玩家: 未找到 (CharacterMainControl.Main 为 null)");
                }
            }
            catch (Exception e)
            {
                Log("  获取玩家信息失败: " + e.Message);
            }

            // 查找所有 MultiSceneTeleporter
            try
            {
                MultiSceneTeleporter[] teleporters = FindObjectsOfType<MultiSceneTeleporter>(true);
                Log("  找到 " + teleporters.Length + " 个 MultiSceneTeleporter:");
                foreach (var t in teleporters)
                {
                    if (t != null)
                    {
                        try
                        {
                            MultiSceneLocation loc = t.Target;
                            Log("    - " + t.name + " (场景: " + t.gameObject.scene.name + ")");
                            Log("      目标: SceneID=" + loc.SceneID + ", LocationName=" + loc.LocationName);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception e)
            {
                Log("  查找 MultiSceneTeleporter 失败: " + e.Message);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!monitoring) return;
            Log("事件: SceneLoaded - " + scene.name + " (模式: " + mode + ")");
            LogCurrentGameState();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (!monitoring) return;
            Log("事件: SceneUnloaded - " + scene.name);
        }

        private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            if (!monitoring) return;
            Log("事件: ActiveSceneChanged - " + oldScene.name + " -> " + newScene.name);
        }

        private void SubscribeToSceneLoaderEvents()
        {
            Type sceneLoaderType = Type.GetType("SceneLoader, TeamSoda.Duckov.Core");
            if (sceneLoaderType == null)
            {
                sceneLoaderType = Type.GetType("SceneLoader, Assembly-CSharp");
            }

            if (sceneLoaderType != null)
            {
                // 订阅 onStartedLoadingScene 事件
                EventInfo startEvent = sceneLoaderType.GetEvent("onStartedLoadingScene", BindingFlags.Public | BindingFlags.Static);
                if (startEvent != null)
                {
                    Delegate handler = Delegate.CreateDelegate(startEvent.EventHandlerType, this, "OnSceneLoadingStarted");
                    startEvent.AddEventHandler(null, handler);
                    Log("已订阅 SceneLoader.onStartedLoadingScene");
                }

                // 订阅 onFinishedLoadingScene 事件
                EventInfo finishEvent = sceneLoaderType.GetEvent("onFinishedLoadingScene", BindingFlags.Public | BindingFlags.Static);
                if (finishEvent != null)
                {
                    Delegate handler = Delegate.CreateDelegate(finishEvent.EventHandlerType, this, "OnSceneLoadingFinished");
                    finishEvent.AddEventHandler(null, handler);
                    Log("已订阅 SceneLoader.onFinishedLoadingScene");
                }
            }
        }

        public void OnSceneLoadingStarted(object context)
        {
            if (!monitoring) return;
            Log("事件: SceneLoader.onStartedLoadingScene");
            try
            {
                Type contextType = context.GetType();
                FieldInfo sceneIdField = contextType.GetField("sceneID", BindingFlags.Public | BindingFlags.Instance);
                FieldInfo useLocationField = contextType.GetField("useLocation", BindingFlags.Public | BindingFlags.Instance);
                FieldInfo locationField = contextType.GetField("location", BindingFlags.Public | BindingFlags.Instance);

                if (sceneIdField != null)
                {
                    string sceneId = (string)sceneIdField.GetValue(context);
                    Log("  SceneID: " + sceneId);
                }

                if (useLocationField != null)
                {
                    bool useLocation = (bool)useLocationField.GetValue(context);
                    Log("  UseLocation: " + useLocation);

                    if (useLocation && locationField != null)
                    {
                        object location = locationField.GetValue(context);
                        if (location != null)
                        {
                            Type locType = location.GetType();
                            PropertyInfo sceneIdProp = locType.GetProperty("SceneID");
                            PropertyInfo locationNameProp = locType.GetProperty("LocationName");

                            if (sceneIdProp != null && locationNameProp != null)
                            {
                                string locSceneId = (string)sceneIdProp.GetValue(location);
                                string locName = (string)locationNameProp.GetValue(location);
                                Log("  Location: SceneID=" + locSceneId + ", LocationName=" + locName);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log("  解析 SceneLoadingContext 失败: " + e.Message);
            }
        }

        public void OnSceneLoadingFinished(object context)
        {
            if (!monitoring) return;
            Log("事件: SceneLoader.onFinishedLoadingScene");
        }

        void Update()
        {
            if (!ModBehaviour.DevModeEnabled)
            {
                return;
            }

            // F8：记录玩家当前位置（用于Boss刷新点采集）
            if (Input.GetKeyDown(KeyCode.F8))
            {
                try
                {
                    CharacterMainControl main = null;
                    try
                    {
                        main = CharacterMainControl.Main;
                    }
                    catch { }

                    if (main == null)
                    {
                        Log("按下F8但未找到玩家（CharacterMainControl.Main 为 null）");
                        return;
                    }

                    Vector3 pos = main.transform.position;
                    string sceneName = main.gameObject.scene.name;

                    // 记录到调试日志，方便从 Player.log 中复制
                    string posText = string.Format("场景={0}, 位置=({1:F2}, {2:F2}, {3:F2})", sceneName, pos.x, pos.y, pos.z);
                    Log("[BossRushSpawnPoint] " + posText);

                    // 在屏幕上给出简短提示
                    if (BossRush.ModBehaviour.Instance != null)
                    {
                        BossRush.ModBehaviour.Instance.ShowMessage("已记录刷新点: " + posText);
                    }
                }
                catch (Exception e)
                {
                    Log("F8 记录位置时出错: " + e.Message);
                }
            }
        }
    }
}
