using System;
using System.Collections;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>
        /// 返回基地必须走官方完整入口，不能单独加载 Base_SceneV2 子场景。
        /// 加载任务结束后仍需核对目标场景、玩家、相机和初始化，才可记 PASS。
        /// </summary>
        private IEnumerator LoadScene(string sceneId, string caseId, bool clickToContinue = false,
            bool returnToBase = false)
        {
            Stopwatch sw = Stopwatch.StartNew();
            _operationSucceeded = false;
            _operationReason = null;
            _lastSceneClicksFed = 0;
            string expectedScene = returnToBase ? BaseSceneNameForValidation() : _host.GetArenaSceneName();
            WriteRaw("SCENE_BEGIN | " + caseId + " | target=" + expectedScene
                + ",entry=" + (returnToBase ? "LoadBaseScene" : sceneId));

            // Mode H 认证失败 / Mode F 撤离可能已经启动返程，禁止叠加第二次加载。
            float deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
            while (SceneLoader.IsSceneLoading && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (SceneLoader.IsSceneLoading)
            {
                _operationReason = "previous_scene_load_timeout";
                Record(caseId, "FAIL", sw.ElapsedMilliseconds, DescribeSceneReadiness(expectedScene), _operationReason);
                yield break;
            }
            // 取消和套件超时也必须完成返基地收尾；场内新用例则不再启动。
            if (!returnToBase && ShouldAbort())
            {
                _operationReason = DescribeAbortReason();
                Record(caseId, "SKIP", sw.ElapsedMilliseconds, string.Empty, _operationReason);
                yield break;
            }

            UniTask task;
            try
            {
                task = returnToBase
                    ? SceneLoader.Instance.LoadBaseScene(null, true)
                    : SceneLoader.Instance.LoadScene(sceneId, null, clickToContinue, false, true, false,
                        default(MultiSceneLocation), true, false);
            }
            catch (Exception e)
            {
                _operationReason = e.ToString();
                Record(caseId, "FAIL", sw.ElapsedMilliseconds, DescribeSceneReadiness(expectedScene), _operationReason);
                yield break;
            }
            deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
            float nextClickAt = Time.realtimeSinceStartup + SceneClickFeedIntervalSeconds;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
            {
                if (clickToContinue && Time.realtimeSinceStartup >= nextClickAt)
                {
                    nextClickAt = Time.realtimeSinceStartup + SceneClickFeedIntervalSeconds;
                    if (FeedSceneContinueClick()) _lastSceneClicksFed++;
                }
                yield return null;
            }
            if (task.Status == UniTaskStatus.Pending)
            {
                _operationReason = "scene_load_timeout";
                Record(caseId, "FAIL", sw.ElapsedMilliseconds, DescribeSceneReadiness(expectedScene), _operationReason);
                // 本次任务仍由官方执行；观察其最终异常，下一次加载仍须等待 IsSceneLoading。
                task.Forget();
                yield break;
            }
            try { task.GetAwaiter().GetResult(); }
            catch (Exception e) { _operationReason = e.ToString(); }
            if (_operationReason != null)
            {
                Record(caseId, "FAIL", sw.ElapsedMilliseconds, DescribeSceneReadiness(expectedScene), _operationReason);
                yield break;
            }

            while (!IsRuntimeReady(expectedScene) && Time.realtimeSinceStartup < deadline)
                yield return null;
            _operationSucceeded = IsRuntimeReady(expectedScene);
            if (!_operationSucceeded) _operationReason = "scene_loaded_but_runtime_not_ready";
            Record(caseId, _operationSucceeded ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                DescribeSceneReadiness(expectedScene) + ",click_to_continue=" + clickToContinue
                    + ",clicks_fed=" + _lastSceneClicksFed, _operationReason);
        }

        private bool FeedSceneContinueClick()
        {
            try
            {
                if (SceneLoader.Instance == null) return false;
                SceneLoader.Instance.NotifyPointerClick(null);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Validation] 喂「点击继续」失败: " + e.Message);
                return false;
            }
        }

        private IEnumerator WaitRuntimeReady(string caseId, float timeout,
            string expectedScene = null, bool isFinalCleanup = false)
        {
            if (expectedScene == null) expectedScene = _host.GetArenaSceneName();
            Stopwatch sw = Stopwatch.StartNew();
            _operationSucceeded = false;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < deadline && (isFinalCleanup || !ShouldAbort()))
            {
                if (IsRuntimeReady(expectedScene)) { _operationSucceeded = true; break; }
                yield return null;
            }
            _operationReason = _operationSucceeded ? null : "runtime_ready_timeout";
            Record(caseId, _operationSucceeded ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                DescribeSceneReadiness(expectedScene), _operationReason);
        }

        private static bool IsRuntimeReady(string expectedScene)
        {
            LevelManager manager = LevelManager.Instance;
            CharacterMainControl player = CharacterMainControl.Main;
            return !SceneLoader.IsSceneLoading
                && string.Equals(SceneManager.GetActiveScene().name, expectedScene, StringComparison.Ordinal)
                && manager != null && LevelManager.AfterInit
                && player != null && player.gameObject.activeInHierarchy && player.CharacterItem != null
                && player.Health != null && !player.Health.IsDead
                && manager.GameCamera != null && manager.GameCamera.isActiveAndEnabled;
        }

        private static string DescribeSceneReadiness(string expectedScene)
        {
            LevelManager manager = LevelManager.Instance;
            CharacterMainControl player = CharacterMainControl.Main;
            return "scene=" + SceneManager.GetActiveScene().name + ",expected=" + expectedScene
                + ",loading=" + SceneLoader.IsSceneLoading + ",level_manager=" + (manager != null)
                + ",after_init=" + (manager != null && LevelManager.AfterInit)
                + ",player=" + (player != null) + ",player_active=" + (player != null && player.gameObject.activeInHierarchy)
                + ",player_alive=" + (player != null && player.Health != null && !player.Health.IsDead)
                + ",camera=" + (manager != null && manager.GameCamera != null && manager.GameCamera.isActiveAndEnabled);
        }

        private IEnumerator EnsureArenaForCase(string caseId)
        {
            _operationSucceeded = false;
            if (ShouldAbort()) yield break;
            string expectedScene = _host.GetArenaSceneName();
            if (IsRuntimeReady(expectedScene)) { _operationSucceeded = true; yield break; }
            WriteRaw("SCENE_RECOVERY | " + caseId + " | " + DescribeSceneReadiness(expectedScene));
            _host.ValidationSafeCleanup();
            yield return LoadScene(BossRushArenaSceneIDForValidation(), caseId + "_RESTORE_ARENA");
        }
    }
}
