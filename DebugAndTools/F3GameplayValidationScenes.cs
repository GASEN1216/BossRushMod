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

            int clicksAtStart = _sceneClicksFed;
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
            // 「点击继续」由 Update 里的 FeedSceneContinueClickWhenWaiting 统一喂（LoadBaseScene 恒开点击门），这里只数本次喂了几下。
            deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
                yield return null;
            _lastSceneClicksFed = _sceneClicksFed - clicksAtStart;
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

        private float _nextSceneClickAt;
        private int _sceneClicksFed;
        private static System.Reflection.FieldInfo _sceneLoaderClickReceiverField;
        private static bool _sceneLoaderClickReceiverResolved;

        /// <summary>
        /// 官方加载停在「点击继续」时替玩家点。验收跑着的每一帧由 Update 调，不管加载是谁发起的：
        /// <c>LoadBaseScene</c> 恒传 clickToConinue=true（IL 实查），Mode F / 丧尸撤离、返基地收尾、岛上返航兜底都会停在这一屏，
        /// 而官方等点击的循环没有超时（2026-09-15 第二轮 Mode F 撤离后停在加载屏十分钟，后面整段连带作废）。
        /// 只在点击接收器激活时喂：官方只在等点击那一段 SetActive(true)，进等待前先把 clicked 复位，
        /// 早喂的点击不算数，不等点击的加载也不会空响点击音效。接收器字段取不到（官方改名）时退回「加载中就喂」，宁可多响不卡死。
        /// </summary>
        private void FeedSceneContinueClickWhenWaiting()
        {
            if (!SceneLoader.IsSceneLoading || Time.realtimeSinceStartup < _nextSceneClickAt) return;
            _nextSceneClickAt = Time.realtimeSinceStartup + SceneClickFeedIntervalSeconds;
            if (IsSceneLoaderWaitingForClick() && FeedSceneContinueClick()) _sceneClicksFed++;
        }

        private static bool IsSceneLoaderWaitingForClick()
        {
            SceneLoader loader = SceneLoader.Instance;
            if (loader == null) return false;
            try
            {
                if (!_sceneLoaderClickReceiverResolved)
                {
                    _sceneLoaderClickReceiverResolved = true;
                    _sceneLoaderClickReceiverField = typeof(SceneLoader).GetField("pointerClickEventRecevier",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (_sceneLoaderClickReceiverField == null)
                        ModBehaviour.DevLog("[Validation] 取不到 SceneLoader.pointerClickEventRecevier，加载中一律喂「点击继续」");
                }
                if (_sceneLoaderClickReceiverField == null) return true;
                Component receiver = _sceneLoaderClickReceiverField.GetValue(loader) as Component;
                return receiver != null && receiver.gameObject.activeSelf;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Validation] 判断「点击继续」失败，按在等处理: " + e.Message);
                return true;
            }
        }

        private static System.Reflection.FieldInfo _officialWaitingForChoiceField;
        private static bool _officialWaitingForChoiceResolved;

        /// <summary>
        /// 官方对话是否已经在等玩家选（私有字段 <c>DialogueUI.waitingForChoice</c>）。
        /// <c>DoMultipleChoice</c> 先激活选项控件并淡入，淡入完才进 <c>WaitForChoice</c>、把 confirmedChoice 清成 -1——
        /// 这之前点的选项会被清掉，对话一直等下去（2026-09-15 第三轮岛内 5 条红，官方 IL 实查）。
        /// 与「点击继续」同属官方界面的等待点探测，放在这里而不放 runner 宿主文件（ModBehaviour partial 预算）。
        /// 取不到字段（官方改名）返回 null，调用方退回「见到选项就点」。
        /// </summary>
        private static bool? OfficialDialogueWaitingForChoice()
        {
            Dialogues.DialogueUI ui = Dialogues.DialogueUI.instance;
            if (ui == null) return false;
            if (!_officialWaitingForChoiceResolved)
            {
                _officialWaitingForChoiceResolved = true;
                _officialWaitingForChoiceField = typeof(Dialogues.DialogueUI).GetField("waitingForChoice",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (_officialWaitingForChoiceField == null)
                    ModBehaviour.DevLog("[Validation] 取不到 DialogueUI.waitingForChoice，点对话选项退回「见到选项就点」");
            }
            if (_officialWaitingForChoiceField == null) return null;
            try { return (bool)_officialWaitingForChoiceField.GetValue(ui); }
            catch (Exception) { return null; }
        }

        private static System.Reflection.FieldInfo _officialContinueIndicatorField, _officialDialogueTextField;
        private static bool _officialLineFieldsResolved;

        private static void ResolveOfficialLineFields()
        {
            if (_officialLineFieldsResolved) return;
            _officialLineFieldsResolved = true;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            _officialContinueIndicatorField = typeof(Dialogues.DialogueUI).GetField("continueIndicator", flags);
            _officialDialogueTextField = typeof(Dialogues.DialogueUI).GetField("text", flags);
            if (_officialContinueIndicatorField == null)
                ModBehaviour.DevLog("[Validation] 取不到 DialogueUI.continueIndicator，等本句打完退回读 TMP 可见字数");
        }

        /// <summary>
        /// 官方对话这一句是否已经完整显示、在等确认（私有字段 <c>DialogueUI.continueIndicator</c> 的 activeSelf）。
        /// 官方 DoSubtitle（IL 实查）：开头置失活 → 逐字显示（speed 序列化 40 字/秒）→ WaitForConfirm 先 SetActive(true) 再清 confirmed
        /// → 等到确认才置回失活、淡出换句。打字途中的 Confirm 只补完这句、不翻页（2026-09-15 第五轮对话截图截在半句上）。
        /// 取不到字段（官方改名）返回 null，调用方退回读 TMP；没有对话界面返回 false。
        /// </summary>
        private static bool? OfficialDialogueLineShown()
        {
            Dialogues.DialogueUI ui = Dialogues.DialogueUI.instance;
            if (ui == null) return false;
            ResolveOfficialLineFields();
            if (_officialContinueIndicatorField == null) return null;
            try
            {
                GameObject indicator = _officialContinueIndicatorField.GetValue(ui) as GameObject;
                return indicator != null && indicator.activeSelf;
            }
            catch (Exception) { return null; }
        }

        /// <summary>官方对话框当前这一句的文字组件（逐字显示时 text 已是整句，只是 maxVisibleCharacters 在涨）。取不到返回 null。</summary>
        private static TMPro.TMP_Text OfficialDialogueText()
        {
            Dialogues.DialogueUI ui = Dialogues.DialogueUI.instance;
            if (ui == null) return null;
            ResolveOfficialLineFields();
            try
            {
                TMPro.TMP_Text field = _officialDialogueTextField == null ? null : _officialDialogueTextField.GetValue(ui) as TMPro.TMP_Text;
                if (field != null) return field;
            }
            catch (Exception) { }
            // 退路：官方层级名（resources.assets 实读：BG/Content/ContentFrame/ContentArea/TextArea/ContentText）。
            foreach (TMPro.TMP_Text candidate in ui.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (candidate != null && candidate.name == "ContentText") return candidate;
            return null;
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
                + ",camera=" + (manager != null && manager.GameCamera != null && manager.GameCamera.isActiveAndEnabled)
                + (SceneLoader.IsSceneLoading ? ",loader_step=" + SceneLoader.LoadingComment : string.Empty);
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
