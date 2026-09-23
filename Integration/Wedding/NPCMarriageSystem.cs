// ============================================================================
// NPCMarriageSystem.cs - NPC结婚系统
// ============================================================================
// 模块说明：
//   处理“钻石戒指赠送成功 => 结婚”的完整流程：
//   1) 持久化记录配偶状态
//   2) 优先播放结婚过场视频（缺失时回退大对话）
//   3) 过场结束后显示爱心+纪念日期气泡
//   4) 尝试将配偶转移到婚礼教堂刷新点（找不到教堂则从当前地图移除）
//   5) 处理离婚流程（关系解除、心碎反馈、恢复普通地图刷新）
// ============================================================================

using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Cysharp.Threading.Tasks;
using Dialogues;
using NodeCanvas.DialogueTrees;

namespace BossRush
{
    /// <summary>
    /// NPC结婚流程管理器
    /// </summary>
    public static class NPCMarriageSystem
    {
        private const float MARRIAGE_VIDEO_TIMEOUT_SECONDS = 120f;
        // 过场进出与回放跳过提示的节奏（UD-47）
        private const float MARRIAGE_VIDEO_FADE_IN_SECONDS = 0.5f;
        private const float MARRIAGE_VIDEO_REVEAL_SECONDS = 0.4f;
        private const float MARRIAGE_VIDEO_FADE_OUT_SECONDS = 0.6f;
        private const float MARRIAGE_SKIP_HINT_DELAY_SECONDS = 1f;
        private const float MARRIAGE_SKIP_HINT_FADE_SECONDS = 0.3f;
        private const int MARRIAGE_VIDEO_CANVAS_ORDER = BossRushUILayers.WeddingCutscene;
        private const float DIVORCE_RELOCATE_DELAY_SECONDS = 2.8f;
        private static GameObject marriageVideoInputToken;
        private static bool marriageVideoInputLocked = false;
        private static bool marriageVideoPreviousCursorVisible = false;
        private static CursorLockMode marriageVideoPreviousCursorLockState = CursorLockMode.Locked;

        // 只保留单调代际，不在静态字段里持有角色。新的关系操作使旧收尾失效。
        private static long operationGeneration;

        private sealed class MarriageOperation
        {
            internal ModBehaviour Host;
            internal CharacterMainControl Player;
            internal GameObject Npc;
            internal string NpcId;
            internal int Slot, SceneHandle;
            internal long Generation;
            internal bool Married;

            internal bool IsCurrent()
            {
                try
                {
                    if (Generation != operationGeneration || Host == null || Host != ModBehaviour.Instance
                        || Player == null || Player != CharacterMainControl.Main || Player.Health == null || Player.Health.IsDead
                        || Npc == null || SceneLoader.IsSceneLoading
                        || Slot != Saves.SavesSystem.CurrentSlot
                        || SceneHandle != UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle
                        || Host.GetSpouseInstance(NpcId) != Npc) return false;
                    string spouse = AffinityManager.GetCurrentSpouseNpcId();
                    return Married
                        ? spouse == NpcId && AffinityManager.IsMarriedToPlayer(NpcId) && !AffinityManager.IsSpouseFollowingPlayer(NpcId)
                        : string.IsNullOrEmpty(spouse) && !AffinityManager.IsMarriedToPlayer(NpcId);
                }
                catch (Exception) { return false; }
            }
        }

        private static MarriageOperation BeginOperation(string npcId, Transform target, bool married)
        {
            long generation = ++operationGeneration;
            try
            {
                ModBehaviour host = ModBehaviour.Instance;
                GameObject npc = host == null ? null : host.GetSpouseInstance(npcId);
                if (npc == null || target == null || (target != npc.transform && !target.IsChildOf(npc.transform))) return null;
                var operation = new MarriageOperation
                {
                    Host = host, Player = CharacterMainControl.Main, Npc = npc, NpcId = npcId,
                    Slot = Saves.SavesSystem.CurrentSlot,
                    SceneHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle,
                    Generation = generation, Married = married
                };
                return operation.IsCurrent() ? operation : null;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] [WARNING] 无法建立婚姻收尾上下文: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 钻石戒指赠送成功后的入口
        /// </summary>
        public static void HandleRingGiftAccepted(string npcId, Transform npcTransform, INPCController npcController)
        {
            if (string.IsNullOrEmpty(npcId)) return;

            string marriageDateTextCN = DateTime.Now.ToString("yyyy年M月d日");
            bool changed = AffinityManager.MarkMarriedToPlayer(npcId, marriageDateTextCN);
            if (!changed)
            {
                ModBehaviour.DevLog("[Marriage] 配偶状态未变化，跳过结婚过场: " + npcId);
                return;
            }

            MarriageOperation operation = BeginOperation(npcId, npcTransform, true);
            if (operation != null) RunMarriageSequenceAsync(operation, npcTransform, npcController, marriageDateTextCN).Forget();
        }

        /// <summary>
        /// 离婚入口：解除关系 + 心碎反馈 + 恢复普通地图刷新
        /// </summary>
        public static void HandleDivorceRequested(string npcId, Transform npcTransform, INPCController npcController)
        {
            if (string.IsNullOrEmpty(npcId)) return;

            string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
            if (string.IsNullOrEmpty(spouseNpcId) || spouseNpcId != npcId)
            {
                ModBehaviour.DevLog("[Marriage] [WARNING] 非当前配偶，拒绝离婚请求: " + npcId);
                try
                {
                    if (npcController != null)
                    {
                        npcController.EndDialogueWithStay(1f, false);
                    }
                }
                catch { }
                return;
            }

            bool divorced = AffinityManager.DivorceFromPlayer(npcId, resetAffinityToZero: true);
            if (!divorced)
            {
                ModBehaviour.DevLog("[Marriage] [WARNING] 离婚状态未变化: " + npcId);
                try
                {
                    if (npcController != null)
                    {
                        npcController.EndDialogueWithStay(1f, false);
                    }
                }
                catch { }
                return;
            }

            MarriageOperation operation = BeginOperation(npcId, npcTransform, false);
            if (operation == null) return;

            try
            {
                INPCController controller = npcController;
                if (controller == null && npcTransform != null)
                {
                    controller = npcTransform.GetComponent<INPCController>();
                }

                if (controller != null)
                {
                    controller.ShowBrokenHeartBubble();
                    controller.EndDialogueWithStay(DIVORCE_RELOCATE_DELAY_SECONDS + 0.4f, false);
                }

                if (npcTransform != null)
                {
                    string text = L10n.T(
                        "看来我们要分道扬镳了...",
                        "Looks like we have to go our separate ways...");
                    NPCDialogueSystem.ShowDialogue(npcId, npcTransform, text, 4.5f);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] [WARNING] 离婚反馈显示失败: " + e.Message);
            }

            RunDivorceFinalizeAsync(operation).Forget();
        }

        /// <summary>
        /// 重播结婚过场（由婚礼教堂"回忆当天"交互触发）
        /// 仅播放视频或文字过场，不修改任何婚姻状态
        /// </summary>
        public static void ReplayMarriageScene(string npcId)
        {
            ReplayMarriageSceneAsync(npcId).Forget();
        }

        /// <summary>
        /// 异步重播结婚过场
        /// </summary>
        private static async UniTaskVoid ReplayMarriageSceneAsync(string npcId)
        {
            try
            {
                ModBehaviour.DevLog("[Marriage] 回忆当天：开始重播结婚过场, npcId=" + npcId);

                // 回放允许按 Esc 跳过（第一次结婚的过场仍不可跳过，见 RunMarriageSequenceAsync）
                bool playedVideo = await PlayMarriageVideoCutsceneAsync(npcId, null, true);
                if (!playedVideo)
                {
                    Transform npcTransform = null;
                    ModBehaviour mod = ModBehaviour.Instance;
                    if (mod != null)
                    {
                        npcTransform = mod.GetWeddingNpcTransform();
                    }

                    if (npcTransform != null)
                    {
                        var actor = ResolveDialogueActor(npcId, npcTransform);
                        if (actor != null)
                        {
                            string[][] cutsceneDialogues = BuildMarriageCutsceneDialogues(npcId);
                            string keyPrefix = "BossRush_MarriageReplay_" + npcId + "_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                            await DialogueManager.ShowDialogueSequenceBilingual(actor, cutsceneDialogues, keyPrefix);
                        }
                        else
                        {
                            ModBehaviour.DevLog("[Marriage] 回忆当天：未找到 DialogueActor");
                        }
                    }
                    else
                    {
                        ModBehaviour.DevLog("[Marriage] 回忆当天：未找到配偶 Transform，跳过文字过场");
                    }
                }

                ModBehaviour.DevLog("[Marriage] 回忆当天：过场播放完毕");
            }
            catch (OperationCanceledException)
            {
                // 共享对话已回收本次会话，不能再强行关闭后继会话。
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] 回忆当天异常: " + e.Message);
                DialogueManager.ForceEndDialogue();
            }
        }

        /// <summary>
        /// 执行结婚过场与后续反馈
        /// </summary>
        private static async UniTaskVoid RunMarriageSequenceAsync(
            MarriageOperation operation,
            Transform npcTransform,
            INPCController npcController,
            string marriageDateTextCN)
        {
            string npcId = operation.NpcId;
            try
            {
                if (!operation.IsCurrent()) return;
                // 进入对话态，避免过场期间NPC乱跑
                if (npcController != null)
                {
                    npcController.StartDialogue();
                }

                bool playedVideo = await PlayMarriageVideoCutsceneAsync(npcId, operation.IsCurrent);
                if (!operation.IsCurrent()) return;
                if (!playedVideo)
                {
                    var actor = ResolveDialogueActor(npcId, npcTransform);
                    if (actor != null)
                    {
                        string[][] cutsceneDialogues = BuildMarriageCutsceneDialogues(npcId);
                        string keyPrefix = "BossRush_MarriageCutscene_" + npcId + "_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                        await DialogueManager.ShowDialogueSequenceBilingual(actor, cutsceneDialogues, keyPrefix,
                            operation.Npc.GetCancellationTokenOnDestroy());
                    }
                    else
                    {
                        ModBehaviour.DevLog("[Marriage] [WARNING] 未找到可用的 DialogueActor，跳过过场: " + npcId);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] [ERROR] 结婚过场异常: " + e.Message);
                if (operation.IsCurrent()) DialogueManager.ForceEndDialogue();
            }
            finally
            {
                if (operation.IsCurrent())
                {
                    ShowMarriageFeedback(npcId, npcTransform, npcController, marriageDateTextCN);
                    // 等待后再次复核；旧场景/旧关系的收尾不得动当前 registry。
                    await UniTask.Delay(TimeSpan.FromSeconds(DIVORCE_RELOCATE_DELAY_SECONDS));
                    RelocateOrDespawnMarriedNpc(operation);
                }
            }
        }

        /// <summary>
        /// 播放结婚过场视频（全屏播放，自然结束或超时退出）
        /// 视频文件位置：
        /// 1) Assets/cutscenes/marriage_{npcId}.mp4
        /// 2) Assets/cutscenes/marriage.mp4
        ///
        /// 2026-09-23 审美审查 UD-47（owner 拍板）：
        /// - 进场先 0.5 秒 SmoothStep 淡到黑，淡完且视频 Prepare 好了才同时开播画面与音频，画面再 0.4 秒从黑底里淡出；
        ///   旧版同一帧建好黑底，视频还在 Prepare，画面一帧切黑。
        /// - 出场 0.6 秒 SmoothStep 淡回游戏再销毁（音频跟着压下去）；旧版结束时 Destroy 一帧切回。
        /// - 第一次结婚仍不可跳过；「回忆当天」回放（skippable）1 秒后右下角淡入「按 Esc 跳过」，
        ///   按 Esc 跳过时连单独播放的音频一起淡出并停掉。
        /// 淡入淡出走 unscaled 时间，暂停菜单开着时停推进。
        /// </summary>
        /// <returns>播放过视频（含回放中途跳过）返回 true；没有视频或播放失败返回 false</returns>
        private static async UniTask<bool> PlayMarriageVideoCutsceneAsync(string npcId, Func<bool> valid = null, bool skippable = false)
        {
            if (valid != null && !valid()) return false;
            string videoPath = DiamondRingConfig.GetMarriageVideoPath(npcId);
            if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
            {
                ModBehaviour.DevLog("[Marriage] 未找到结婚视频文件，回退文字过场");
                return false;
            }

            GameObject root = null;
            RenderTexture renderTexture = null;
            VideoPlayer videoPlayer = null;
            RawImage rawImage = null;
            CanvasGroup rootGroup = null;
            TMPro.TextMeshProUGUI skipHint = null;
            object audioHandle = null;

            bool finished = false;
            bool prepared = false;
            bool started = false;
            bool skipped = false;
            string errorMessage = null;
            bool inputLocked = false;

            VideoPlayer.EventHandler onPrepared = null;
            VideoPlayer.EventHandler onFinished = null;
            VideoPlayer.ErrorEventHandler onError = null;

            try
            {
                inputLocked = BeginMarriageVideoInputLock();

                root = new GameObject("BossRush_MarriageVideoCutscene");
                UnityEngine.Object.DontDestroyOnLoad(root);

                Canvas canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = MARRIAGE_VIDEO_CANVAS_ORDER;

                CanvasScaler scaler = root.AddComponent<CanvasScaler>();
                ZombieModeUIHelper.ConfigureCanvasScaler(scaler);
                root.AddComponent<GraphicRaycaster>();
                // 整个过场一起淡入淡出：第一帧全透明，不再一帧切黑。
                rootGroup = root.AddComponent<CanvasGroup>();
                rootGroup.alpha = 0f;

                GameObject bgObj = new GameObject("Background");
                bgObj.transform.SetParent(root.transform, false);
                RectTransform bgRt = bgObj.AddComponent<RectTransform>();
                StretchToFullScreen(bgRt);
                Image bgImage = bgObj.AddComponent<Image>();
                bgImage.color = Color.black;

                GameObject videoObj = new GameObject("Video");
                videoObj.transform.SetParent(root.transform, false);
                RectTransform videoRt = videoObj.AddComponent<RectTransform>();
                StretchToFullScreen(videoRt);
                rawImage = videoObj.AddComponent<RawImage>();
                // 开播前 RenderTexture 内容未定义：画面先透明，开播后从黑底里淡出来。
                rawImage.color = new Color(1f, 1f, 1f, 0f);

                if (skippable)
                {
                    skipHint = CreateSkipHint(root.transform);
                }

                int width = Mathf.Clamp(Screen.width, 1, 1920);
                int height = Mathf.Clamp(Screen.height, 1, 1080);
                renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                renderTexture.Create();
                rawImage.texture = renderTexture;

                videoPlayer = root.AddComponent<VideoPlayer>();
                videoPlayer.playOnAwake = false;
                videoPlayer.source = VideoSource.Url;
                videoPlayer.url = videoPath;
                videoPlayer.renderMode = VideoRenderMode.RenderTexture;
                videoPlayer.targetTexture = renderTexture;
                videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                videoPlayer.isLooping = false;
                videoPlayer.skipOnDrop = true;
                videoPlayer.waitForFirstFrame = true;
                videoPlayer.aspectRatio = VideoAspectRatio.FitInside;

                string audioPath = FindMarriageAudioPath(videoPath);
                ModBehaviour.DevLog("[Marriage] 音频文件路径: " + (audioPath ?? "null"));

                // 只记下「准备好了」：真正开播放到淡到黑之后，画面与音频同一帧起。
                onPrepared = delegate(VideoPlayer vp)
                {
                    prepared = true;
                };
                onFinished = delegate(VideoPlayer vp)
                {
                    finished = true;
                };
                onError = delegate(VideoPlayer vp, string msg)
                {
                    errorMessage = msg;
                    finished = true;
                };

                videoPlayer.prepareCompleted += onPrepared;
                videoPlayer.loopPointReached += onFinished;
                videoPlayer.errorReceived += onError;
                videoPlayer.Prepare();

                ModBehaviour.DevLog("[Marriage] 开始播放结婚视频: " + videoPath);

                float elapsed = 0f;
                float fadeIn = 0f;
                float reveal = 0f;
                float hintTimer = 0f;
                while (!finished && elapsed < MARRIAGE_VIDEO_TIMEOUT_SECONDS)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    if (valid != null && !valid()) return false;
                    float deltaTime = Time.unscaledDeltaTime;
                    elapsed += deltaTime;
                    if (BossRushUI.IsGamePaused())
                    {
                        continue;
                    }

                    fadeIn = Mathf.Min(1f, fadeIn + deltaTime / MARRIAGE_VIDEO_FADE_IN_SECONDS);
                    rootGroup.alpha = BossRushUI.SmoothStep(fadeIn);

                    if (!started && prepared && fadeIn >= 1f)
                    {
                        started = true;
                        if (!string.IsNullOrEmpty(audioPath))
                        {
                            audioHandle = MarriageCutsceneAudio.Play(audioPath);
                            ModBehaviour.DevLog("[Marriage] 播放过场音频: " + audioPath + (audioHandle != null ? "" : "（未取得句柄，跳过时无法停止）"));
                        }
                        try { videoPlayer.Play(); }
                        catch (Exception e) { ModBehaviour.DevLog("[Marriage] [WARNING] 开播结婚视频异常: " + e.Message); }
                    }

                    if (started && reveal < 1f)
                    {
                        reveal = Mathf.Min(1f, reveal + deltaTime / MARRIAGE_VIDEO_REVEAL_SECONDS);
                        rawImage.color = new Color(1f, 1f, 1f, BossRushUI.SmoothStep(reveal));
                    }

                    if (skipHint != null)
                    {
                        hintTimer += deltaTime;
                        skipHint.alpha = BossRushUI.SmoothStep((hintTimer - MARRIAGE_SKIP_HINT_DELAY_SECONDS) / MARRIAGE_SKIP_HINT_FADE_SECONDS);
                        if (Input.GetKeyDown(KeyCode.Escape))
                        {
                            skipped = true;
                            ModBehaviour.DevLog("[Marriage] 回忆当天：玩家按 Esc 跳过过场");
                            break;
                        }
                    }
                }

                if (elapsed >= MARRIAGE_VIDEO_TIMEOUT_SECONDS)
                {
                    ModBehaviour.DevLog("[Marriage] [WARNING] 结婚视频播放超时，强制结束");
                }

                // 出场：淡回游戏（音频跟着压下去），淡完由 finally 销毁。
                float fadeOut = 0f;
                float fromAlpha = rootGroup.alpha;
                while (fadeOut < 1f)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    if (valid != null && !valid()) return false;
                    if (BossRushUI.IsGamePaused())
                    {
                        continue;
                    }
                    fadeOut = Mathf.Min(1f, fadeOut + Time.unscaledDeltaTime / MARRIAGE_VIDEO_FADE_OUT_SECONDS);
                    float remain = 1f - BossRushUI.SmoothStep(fadeOut);
                    rootGroup.alpha = fromAlpha * remain;
                    MarriageCutsceneAudio.SetVolume(audioHandle, remain);
                }

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    ModBehaviour.DevLog("[Marriage] [WARNING] 结婚视频播放错误: " + errorMessage);
                    return false;
                }

                return started || skipped;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] [WARNING] 播放结婚视频失败: " + e.Message);
                return false;
            }
            finally
            {
                // 跳过、超时、失效、异常都要把单独播放的音频停掉（自然播完时它已结束，停一次无害）。
                MarriageCutsceneAudio.Stop(audioHandle);

                if (inputLocked)
                {
                    EndMarriageVideoInputLock();
                }

                if (videoPlayer != null)
                {
                    try
                    {
                        if (onPrepared != null) videoPlayer.prepareCompleted -= onPrepared;
                        if (onFinished != null) videoPlayer.loopPointReached -= onFinished;
                        if (onError != null) videoPlayer.errorReceived -= onError;
                        videoPlayer.Stop();
                        videoPlayer.targetTexture = null;
                    }
                    catch { }
                }

                if (rawImage != null)
                {
                    try { rawImage.texture = null; } catch { }
                }

                if (renderTexture != null)
                {
                    try
                    {
                        if (renderTexture.IsCreated()) renderTexture.Release();
                        UnityEngine.Object.Destroy(renderTexture);
                    }
                    catch { }
                }

                if (root != null)
                {
                    UnityEngine.Object.Destroy(root);
                }
            }
        }

        /// <summary>回放专用的「按 Esc 跳过」：右下角 16 号次级字，带描边（压在视频画面上），初始透明、1 秒后淡入。</summary>
        private static TMPro.TextMeshProUGUI CreateSkipHint(Transform parent)
        {
            GameObject hintObj = ZombieModeUIHelper.CreateRect("SkipHint", parent,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-48f, 36f), new Vector2(420f, 28f), new Vector2(1f, 0f));
            TMPro.TextMeshProUGUI hint = ZombieModeUIHelper.CreateTMPText(hintObj,
                L10n.T("按 Esc 跳过", "Press Esc to skip"), 16f, TMPro.TextAlignmentOptions.BottomRight, BossRushUIColors.TextSecondary);
            hint.enableAutoSizing = false;
            hint.enableWordWrapping = false;
            hint.margin = Vector4.zero;
            BossRushUIKit.ApplyWorldTextOutline(hint);
            hint.alpha = 0f;
            return hint;
        }

        /// <summary>
        /// 过场音频：与 ModBehaviour.PlaySoundEffect 同一条官方路径（AudioManager.PostCustomSFX，挂在玩家身上），
        /// 但留住返回的 FMOD EventInstance，回放跳过时能压音量并停掉。
        /// 编译清单没有 FMOD 引用（见 IntegrationUIFeedback 的注释），EventInstance 的 setVolume / stop 走反射；
        /// MethodInfo 解析一次缓存，只在过场开始与淡出的那几帧调用。任何一步失败都静默降级（音频不是关键路径）。
        /// </summary>
        private static class MarriageCutsceneAudio
        {
            private static MethodInfo postCustomSfx;
            private static bool postResolved;
            private static MethodInfo setVolumeMethod;
            private static MethodInfo stopMethod;
            private static object stopAllowFadeOut;
            private static Type handleType;

            internal static object Play(string filePath)
            {
                try
                {
                    if (!postResolved)
                    {
                        postResolved = true;
                        postCustomSfx = typeof(global::Duckov.AudioManager).GetMethod("PostCustomSFX",
                            BindingFlags.Public | BindingFlags.Static, null,
                            new Type[] { typeof(string), typeof(GameObject), typeof(bool) }, null);
                    }
                    if (postCustomSfx == null)
                    {
                        return null;
                    }
                    GameObject target = null;
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main != null && main.gameObject.activeInHierarchy)
                    {
                        target = main.gameObject;
                    }
                    // 返回 EventInstance?：有值时装箱成 EventInstance，没有时为 null。
                    return postCustomSfx.Invoke(null, new object[] { filePath, target, false });
                }
                catch (Exception e)
                {
                    ModBehaviour.DevLog("[Marriage] [WARNING] 播放过场音频失败: " + e.Message);
                    return null;
                }
            }

            internal static void SetVolume(object handle, float volume)
            {
                if (handle == null || !Resolve(handle) || setVolumeMethod == null)
                {
                    return;
                }
                try
                {
                    setVolumeMethod.Invoke(handle, new object[] { Mathf.Clamp01(volume) });
                }
                catch (Exception)
                {
                    // 音频不是关键路径：FMOD 句柄已失效（事件已自然结束并回收）时静默跳过
                }
            }

            internal static void Stop(object handle)
            {
                if (handle == null || !Resolve(handle) || stopMethod == null)
                {
                    return;
                }
                try
                {
                    stopMethod.Invoke(handle, new object[] { stopAllowFadeOut });
                }
                catch (Exception)
                {
                    // 同上：句柄失效或 FMOD 未就绪时静默跳过，过场收尾不能因为停音频抛异常
                }
            }

            private static bool Resolve(object handle)
            {
                Type type = handle.GetType();
                if (type == handleType)
                {
                    return true;
                }
                try
                {
                    handleType = type;
                    setVolumeMethod = type.GetMethod("setVolume", new Type[] { typeof(float) });
                    stopMethod = null;
                    stopAllowFadeOut = null;
                    MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    for (int i = 0; i < methods.Length; i++)
                    {
                        ParameterInfo[] parameters = methods[i].GetParameters();
                        if (methods[i].Name == "stop" && parameters.Length == 1 && parameters[0].ParameterType.IsEnum)
                        {
                            stopMethod = methods[i];
                            // FMOD.Studio.STOP_MODE.ALLOWFADEOUT = 0：按事件自带的淡出停，不硬切。
                            stopAllowFadeOut = Enum.ToObject(parameters[0].ParameterType, 0);
                            break;
                        }
                    }
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private static bool BeginMarriageVideoInputLock()
        {
            if (marriageVideoInputLocked)
            {
                return true;
            }

            bool previousCursorVisible = Cursor.visible;
            CursorLockMode previousCursorLockState = Cursor.lockState;

            try
            {
                if (marriageVideoInputToken == null)
                {
                    marriageVideoInputToken = new GameObject("BossRush_MarriageVideoInputToken");
                    UnityEngine.Object.DontDestroyOnLoad(marriageVideoInputToken);
                }

                InputManager.DisableInput(marriageVideoInputToken);
                marriageVideoPreviousCursorVisible = previousCursorVisible;
                marriageVideoPreviousCursorLockState = previousCursorLockState;
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
                marriageVideoInputLocked = true;
                return true;
            }
            catch (Exception e)
            {
                try
                {
                    Cursor.visible = previousCursorVisible;
                    Cursor.lockState = previousCursorLockState;
                }
                catch { }

                ModBehaviour.DevLog("[Marriage] [WARNING] 锁定结婚过场输入失败: " + e.Message);
                return false;
            }
        }

        private static void EndMarriageVideoInputLock()
        {
            if (!marriageVideoInputLocked)
            {
                return;
            }

            try
            {
                if (marriageVideoInputToken != null)
                {
                    InputManager.ActiveInput(marriageVideoInputToken);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] [WARNING] 恢复结婚过场输入失败: " + e.Message);
            }
            finally
            {
                marriageVideoInputLocked = false;

                try
                {
                    Cursor.visible = marriageVideoPreviousCursorVisible;
                    Cursor.lockState = marriageVideoPreviousCursorLockState;
                }
                catch { }
            }
        }

        private static void StretchToFullScreen(RectTransform rectTransform)
        {
            if (rectTransform == null) return;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>
        /// 查找与视频同名的音频文件（优先 .mp3/.ogg/.wav，找不到则回退 .mp4 让 FMOD 尝试解码）
        /// </summary>
        private static string FindMarriageAudioPath(string videoPath)
        {
            if (string.IsNullOrEmpty(videoPath)) return null;

            try
            {
                string dir = Path.GetDirectoryName(videoPath);
                string baseName = Path.GetFileNameWithoutExtension(videoPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(baseName)) return videoPath;

                string[] audioExts = { ".mp3", ".ogg", ".wav", ".flac" };
                foreach (string ext in audioExts)
                {
                    string candidate = Path.Combine(dir, baseName + ext);
                    if (File.Exists(candidate))
                    {
                        ModBehaviour.DevLog("[Marriage] 找到独立音频文件: " + candidate);
                        return candidate;
                    }
                }
            }
            catch { }

            return videoPath;
        }

        /// <summary>
        /// 获取/创建结婚过场需要的对话Actor
        /// </summary>
        private static IDialogueActor ResolveDialogueActor(string npcId, Transform npcTransform)
        {
            if (npcTransform == null) return null;

            string npcName = AffinityManager.GetNPCConfig(npcId)?.DisplayName ?? npcId;
            var existing = npcTransform.GetComponent<DuckovDialogueActor>();
            if (existing != null)
            {
                DialogueActorFactory.RefreshPresentation(existing, npcName, npcName);
                return existing;
            }

            return DialogueActorFactory.CreateBilingual(
                npcTransform.gameObject,
                "marriage_" + npcId,
                npcName,
                npcName,
                new Vector3(0f, 2f, 0f));
        }

        /// <summary>
        /// 结婚过场文案（双语）
        /// </summary>
        private static string[][] BuildMarriageCutsceneDialogues(string npcId)
        {
            string npcName = AffinityManager.GetNPCConfig(npcId)?.DisplayName ?? npcId;
            return new string[][]
            {
                new string[] { "这枚钻石戒指...是送给我的吗？", "This diamond ring... is for me?" },
                new string[] { npcName + "轻轻握住了你的手。", npcName + " gently holds your hand." },
                new string[] { "从今天开始，我们一起走下去吧。", "From today on, it's you and me." },
                new string[] { "我愿意。", "I do." }
            };
        }

        /// <summary>
        /// 过场结束后的爱心与纪念气泡
        /// </summary>
        private static void ShowMarriageFeedback(string npcId, Transform npcTransform, INPCController npcController, string marriageDateTextCN)
        {
            try
            {
                INPCController controller = npcController;
                if (controller == null && npcTransform != null)
                {
                    controller = npcTransform.GetComponent<INPCController>();
                }

                if (controller != null)
                {
                    controller.ShowLoveHeartBubble();
                    controller.EndDialogueWithStay(3f, false);
                }

                if (npcTransform != null)
                {
                    string dateCn = string.IsNullOrEmpty(marriageDateTextCN)
                        ? DateTime.Now.ToString("yyyy年M月d日")
                        : marriageDateTextCN;
                    string dateEn = DateTime.Now.ToString("MMMM d, yyyy");
                    string bubbleText = L10n.T(
                        dateCn + "我会永远记住这个日子的~",
                        "I will always remember this day: " + dateEn + "~");
                    NPCDialogueSystem.ShowDialogue(npcId, npcTransform, bubbleText, 4f);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] [WARNING] 显示结婚反馈失败: " + e.Message);
            }
        }

        /// <summary>
        /// 将已婚NPC转移到婚礼教堂；若当前场景无教堂则先移除该NPC
        /// </summary>
        private static void RelocateOrDespawnMarriedNpc(MarriageOperation operation)
        {
            try
            {
                if (!operation.IsCurrent()) return;
                string npcId = operation.NpcId;
                ModBehaviour mod = operation.Host;

                Transform weddingNpc = mod.TrySpawnMarriedNpcAtWeddingPoint();
                if (weddingNpc != null)
                {
                    ModBehaviour.DevLog("[Marriage] 已将配偶转移到婚礼教堂: " + npcId);
                    return;
                }

                // 当前场景无婚礼教堂时，移除配偶；后续仅在教堂刷新
                if (npcId == GoblinAffinityConfig.NPC_ID)
                {
                    mod.DestroyGoblinNPC();
                }
                else if (npcId == NurseAffinityConfig.NPC_ID)
                {
                    mod.DestroyNurseNPC();
                }
                else if (PermanentDuckNpcRegistry.IsPermanentDuckNpc(npcId))
                {
                    CharacterMainControl duckNpc = PermanentDuckNpcRegistry.GetInstance(npcId);
                    if (duckNpc != null)
                    {
                        DuckNpcSpawner.Despawn(duckNpc);
                    }
                    PermanentDuckNpcRegistry.UnregisterInstance(npcId);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] [WARNING] 配偶转移失败: " + e.Message);
            }
        }

        /// <summary>
        /// 离婚后延迟执行NPC回收/重刷，给心碎反馈留展示时间
        /// </summary>
        private static async UniTaskVoid RunDivorceFinalizeAsync(MarriageOperation operation)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(DIVORCE_RELOCATE_DELAY_SECONDS));
                if (!operation.IsCurrent()) return;
                operation.Host.HandleDivorceNpcRelocation(operation.NpcId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] [WARNING] 离婚后NPC重定位失败: " + e.Message);
            }
        }
    }
    // 建造资格每次由当前槽决定；资源注册仍保留，以恢复旧档中已经放置的教堂。
    [HarmonyLib.HarmonyPatch(typeof(Duckov.Buildings.BuildingInfo), "RequirementsSatisfied")]
    internal static class WeddingBuildingRequirementsPatch
    {
        [HarmonyLib.HarmonyPostfix]
        private static void Postfix(Duckov.Buildings.BuildingInfo __instance, ref bool __result)
        {
            if (__instance.id == "wedding_chapel" && __instance.prefabName == "WeddingChapel")
                __result = __result && AffinityManager.CanWrite && AffinityManager.HasAnyNPCEverReachedMaxLevel();
        }
    }

    internal static class WeddingBuildingRuntimePolicy
    {
        internal static bool IsPlacedSceneObject(GameObject candidate, GameObject template)
        {
            if (candidate == null || ReferenceEquals(candidate, template)) return false;
            var scene = candidate.scene;
            if (!scene.IsValid() || !scene.isLoaded) return false;
            var mainScene = Duckov.Scenes.MultiSceneCore.MainScene;
            return scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()
                || (mainScene.HasValue && scene == mainScene.Value);
        }
    }

}
