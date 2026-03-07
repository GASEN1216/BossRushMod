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
        private const int MARRIAGE_VIDEO_CANVAS_ORDER = 32000;
        private const float DIVORCE_RELOCATE_DELAY_SECONDS = 2.8f;
        private static GameObject marriageVideoInputToken;
        private static bool marriageVideoInputLocked = false;
        private static bool marriageVideoPreviousCursorVisible = false;
        private static CursorLockMode marriageVideoPreviousCursorLockState = CursorLockMode.Locked;

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

            RunMarriageSequenceAsync(npcId, npcTransform, npcController, marriageDateTextCN).Forget();
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

            RunDivorceFinalizeAsync(npcId).Forget();
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

                bool playedVideo = await PlayMarriageVideoCutsceneAsync(npcId);
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
            string npcId,
            Transform npcTransform,
            INPCController npcController,
            string marriageDateTextCN)
        {
            try
            {
                // 进入对话态，避免过场期间NPC乱跑
                if (npcController != null)
                {
                    npcController.StartDialogue();
                }

                bool playedVideo = await PlayMarriageVideoCutsceneAsync(npcId);
                if (!playedVideo)
                {
                    var actor = ResolveDialogueActor(npcId, npcTransform);
                    if (actor != null)
                    {
                        string[][] cutsceneDialogues = BuildMarriageCutsceneDialogues(npcId);
                        string keyPrefix = "BossRush_MarriageCutscene_" + npcId + "_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                        await DialogueManager.ShowDialogueSequenceBilingual(actor, cutsceneDialogues, keyPrefix);
                    }
                    else
                    {
                        ModBehaviour.DevLog("[Marriage] [WARNING] 未找到可用的 DialogueActor，跳过过场: " + npcId);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] [ERROR] 结婚过场异常: " + e.Message);
                DialogueManager.ForceEndDialogue();
            }
            finally
            {
                ShowMarriageFeedback(npcId, npcTransform, npcController, marriageDateTextCN);

                // 给反馈留一点展示时间，再进行场景内重定位
                await UniTask.Delay(TimeSpan.FromSeconds(DIVORCE_RELOCATE_DELAY_SECONDS));
                RelocateOrDespawnMarriedNpc(npcId);
            }
        }

        /// <summary>
        /// 播放结婚过场视频（全屏播放，不可跳过，自然结束或超时退出）
        /// 视频文件位置：
        /// 1) Assets/cutscenes/marriage_{npcId}.mp4
        /// 2) Assets/cutscenes/marriage.mp4
        /// </summary>
        /// <returns>播放过视频返回 true；没有视频或播放失败返回 false</returns>
        private static async UniTask<bool> PlayMarriageVideoCutsceneAsync(string npcId)
        {
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

            bool finished = false;
            bool started = false;
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
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                root.AddComponent<GraphicRaycaster>();

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
                rawImage.color = Color.white;

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

                onPrepared = delegate(VideoPlayer vp)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(audioPath))
                        {
                            ModBehaviour mod = ModBehaviour.Instance;
                            if (mod != null)
                            {
                                mod.PlaySoundEffect(audioPath);
                                ModBehaviour.DevLog("[Marriage] 通过PlaySoundEffect播放音频: " + audioPath);
                            }
                            else
                            {
                                ModBehaviour.DevLog("[Marriage] [WARNING] ModBehaviour.Instance为null，无法播放音频");
                            }
                        }
                        started = true;
                        vp.Play();
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[Marriage] [WARNING] prepareCompleted异常: " + e.Message);
                        started = true;
                        try { vp.Play(); } catch { }
                    }
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
                while (!finished && elapsed < MARRIAGE_VIDEO_TIMEOUT_SECONDS)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    elapsed += Time.unscaledDeltaTime;
                }

                if (elapsed >= MARRIAGE_VIDEO_TIMEOUT_SECONDS)
                {
                    ModBehaviour.DevLog("[Marriage] [WARNING] 结婚视频播放超时，强制结束");
                }

                if (!string.IsNullOrEmpty(errorMessage))
                {
                    ModBehaviour.DevLog("[Marriage] [WARNING] 结婚视频播放错误: " + errorMessage);
                    return false;
                }

                return started;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] [WARNING] 播放结婚视频失败: " + e.Message);
                return false;
            }
            finally
            {
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

            var existing = npcTransform.GetComponent<DuckovDialogueActor>();
            if (existing != null)
            {
                return existing;
            }

            string npcName = AffinityManager.GetNPCConfig(npcId)?.DisplayName ?? npcId;
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
                new string[] { "从今天开始，我们一起走下去吧。", "From today on, let's walk this road together." },
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
        private static void RelocateOrDespawnMarriedNpc(string npcId)
        {
            try
            {
                ModBehaviour mod = ModBehaviour.Instance;
                if (mod == null) return;

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
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] [WARNING] 配偶转移失败: " + e.Message);
            }
        }

        /// <summary>
        /// 离婚后延迟执行NPC回收/重刷，给心碎反馈留展示时间
        /// </summary>
        private static async UniTaskVoid RunDivorceFinalizeAsync(string npcId)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(DIVORCE_RELOCATE_DELAY_SECONDS));

                ModBehaviour mod = ModBehaviour.Instance;
                if (mod == null) return;

                mod.HandleDivorceNpcRelocation(npcId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Marriage] [WARNING] 离婚后NPC重定位失败: " + e.Message);
            }
        }
    }
}
