using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class IntegrationRuntimeModule
    {
        private sealed class ArenaEntryRequest
        {
            internal readonly Scene Scene;
            internal readonly List<ArenaEntryTask> Tasks = new List<ArenaEntryTask>();
            internal CharacterMainControl Player;
            internal bool PlayerBound, SawActiveScene, Ready, Cancelled;
            internal ArenaEntryRequest(Scene scene) { Scene = scene; }
        }

        private sealed class ArenaEntryTask
        {
            internal readonly Stack<IEnumerator> Stack = new Stack<IEnumerator>();
            internal Coroutine Coroutine;
            internal bool Advancing;
        }

        private ArenaEntryRequest _arenaEntryRequest;
        private bool _arenaEntryEventsSubscribed;

        private void SubscribeArenaEntryEvents()
        {
            if (_arenaEntryEventsSubscribed) return;
            SceneLoader.onStartedLoadingScene += OnArenaEntrySceneLoading;
            SceneManager.sceneUnloaded += OnArenaEntrySceneUnloaded;
            _arenaEntryEventsSubscribed = true;
        }

        private void CleanupArenaEntry()
        {
            CancelArenaEntry(_arenaEntryRequest);
            if (!_arenaEntryEventsSubscribed) return;
            SceneLoader.onStartedLoadingScene -= OnArenaEntrySceneLoading;
            SceneManager.sceneUnloaded -= OnArenaEntrySceneUnloaded;
            _arenaEntryEventsSubscribed = false;
        }

        private void OnArenaEntrySceneLoading(SceneLoadingContext context)
        {
            CancelArenaEntry(_arenaEntryRequest);
        }

        private void OnArenaEntrySceneUnloaded(Scene scene)
        {
            if (_arenaEntryRequest != null && _arenaEntryRequest.Scene == scene)
                CancelArenaEntry(_arenaEntryRequest);
        }

        private void CancelArenaEntry(ArenaEntryRequest request, ArenaEntryTask currentTask = null)
        {
            if (request == null || request.Cancelled) return;
            request.Cancelled = true;
            if (ReferenceEquals(_arenaEntryRequest, request)) _arenaEntryRequest = null;
            ArenaEntryTask[] tasks = request.Tasks.ToArray();
            foreach (ArenaEntryTask task in tasks)
            {
                if (ReferenceEquals(task, currentTask)) continue;
                try { if (!task.Advancing && _owner != null && task.Coroutine != null) _owner.StopCoroutine(task.Coroutine); }
                catch (Exception e) { ModBehaviour.DevLog("[BossRush] 取消竞技场入场任务失败: " + e.Message); }
                // 回调可能在 MoveNext 内触发切图；此时交给该次推进的 finally 释放。
                if (!task.Advancing) DisposeArenaEntryTask(request, task);
            }
        }

        private bool IsArenaEntryCurrent(ArenaEntryRequest request)
        {
            if (_owner == null || request == null || request.Cancelled
                || !ReferenceEquals(_arenaEntryRequest, request)
                || !request.Scene.IsValid() || !request.Scene.isLoaded) return false;
            bool active = SceneManager.GetActiveScene() == request.Scene;
            if (active) request.SawActiveScene = true;
            else if (request.SawActiveScene || request.Ready) return false;

            CharacterMainControl main = CharacterMainControl.Main;
            // sceneLoaded 早于官方新角色初始化。等目标图成为活动图且 loader 完成后再绑定一次。
            if (!request.PlayerBound && active && !SceneLoader.IsSceneLoading && main != null)
            {
                request.Player = main;
                request.PlayerBound = true;
            }
            return !request.PlayerBound || (request.Player != null && request.Player == main
                && request.Player.Health != null && !request.Player.Health.IsDead);
        }

        private IEnumerator BeginArenaEntryWait(Scene scene)
        {
            CancelArenaEntry(_arenaEntryRequest);
            var request = new ArenaEntryRequest(scene);
            _arenaEntryRequest = request;
            return RunArenaEntryTask(request, WaitForArenaEntryReady(request));
        }

        private IEnumerator WaitForArenaEntryReady(ArenaEntryRequest request)
        {
            const float maxWait = 30f;
            const float interval = 0.1f;
            float elapsed = 0f;
            while (elapsed < maxWait)
            {
                bool sceneLoaded = request.Scene.isLoaded;
                bool sceneLoaderDone = ReadSceneLoaderDoneWithWarning("WaitForLevelInitializedThenSetup");
                bool mainExists = ReadMainExistsWithWarning("WaitForLevelInitializedThenSetup");
                bool cameraExists = ReadCameraExistsWithWarning("WaitForLevelInitializedThenSetup");
                bool levelInited = ReadLevelInitedWithWarning("WaitForLevelInitializedThenSetup");
                if (sceneLoaded && sceneLoaderDone && mainExists && cameraExists && levelInited
                    && SceneManager.GetActiveScene() == request.Scene && request.PlayerBound)
                {
                    request.Ready = true;
                    _owner.StartBossRushDemoChallengeSetupForScene(request.Scene);
                    yield break;
                }
                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }
            ModBehaviour.DevLog("[BossRush] 竞技场入场等待超时，取消本次初始化");
            CancelArenaEntry(request);
        }

        internal void StartArenaEntrySetup(Scene scene, IEnumerator setup)
        {
            ArenaEntryRequest request = _arenaEntryRequest;
            if (request == null || request.Scene != scene || !request.Ready || !IsArenaEntryCurrent(request)) return;
            StartArenaEntryTask(request, setup);
        }

        internal void StartArenaEntryContinuation(IEnumerator routine)
        {
            ArenaEntryRequest request = _arenaEntryRequest;
            if (request == null || !request.Ready || !IsArenaEntryCurrent(request)) return;
            StartArenaEntryTask(request, routine);
        }

        private void StartArenaEntryTask(ArenaEntryRequest request, IEnumerator routine)
        {
            var task = new ArenaEntryTask();
            task.Stack.Push(routine);
            request.Tasks.Add(task);
            task.Coroutine = _owner.StartCoroutine(DriveArenaEntryTask(request, task));
        }

        private IEnumerator RunArenaEntryTask(ArenaEntryRequest request, IEnumerator routine)
        {
            var task = new ArenaEntryTask();
            task.Stack.Push(routine);
            request.Tasks.Add(task);
            return DriveArenaEntryTask(request, task);
        }

        private IEnumerator DriveArenaEntryTask(ArenaEntryRequest request, ArenaEntryTask task)
        {
            try
            {
                while (task.Stack.Count > 0)
                {
                    if (!IsArenaEntryCurrent(request)) { CancelArenaEntry(request, task); yield break; }
                    IEnumerator current = task.Stack.Peek();
                    bool moved;
                    task.Advancing = true;
                    try { moved = current.MoveNext(); }
                    finally { task.Advancing = false; }
                    if (!IsArenaEntryCurrent(request)) { CancelArenaEntry(request, task); yield break; }
                    if (!moved)
                    {
                        task.Stack.Pop();
                        DisposeArenaEntryEnumerator(current);
                        continue;
                    }
                    object yielded = current.Current;
                    // 普通子 IEnumerator 必须递归推进；CustomYieldInstruction 仍交给 Unity 等待。
                    IEnumerator child = yielded as IEnumerator;
                    if (child != null && !(yielded is CustomYieldInstruction)) task.Stack.Push(child);
                    else yield return yielded;
                }
            }
            finally { DisposeArenaEntryTask(request, task); }
        }

        private static void DisposeArenaEntryTask(ArenaEntryRequest request, ArenaEntryTask task)
        {
            while (task.Stack.Count > 0) DisposeArenaEntryEnumerator(task.Stack.Pop());
            request.Tasks.Remove(task);
        }

        private static void DisposeArenaEntryEnumerator(IEnumerator routine)
        {
            try { var disposable = routine as IDisposable; if (disposable != null) disposable.Dispose(); }
            catch (Exception e) { ModBehaviour.DevLog("[BossRush] 释放竞技场入场协程失败: " + e.Message); }
        }
    }
}
