// ============================================================================
// DialogueManager.cs - 大对话管理器
// ============================================================================
// 模块说明：
//   封装游戏原生对话系统，提供统一的大对话接口，支持：
//   - 单条对话显示（带等待玩家点击）
//   - 对话序列（多条对话连续播放）
//   - 多选对话（玩家选择分支）
//   - 自动管理玩家输入禁用/恢复
//   - 双语本地化支持
//
// 使用方式：
//   1. 创建 DialogueActor: var actor = DialogueActorFactory.Create(gameObject, "npc_id", "名字Key");
//   2. 显示单条对话: await DialogueManager.ShowDialogue(actor, "对话Key");
//   3. 显示对话序列: await DialogueManager.ShowDialogueSequence(actor, dialogueKeys);
//   4. 显示多选对话: int choice = await DialogueManager.ShowMultipleChoice(actor, choiceKeys);
//
// 【会话归属与排队（2026-09-14 审核 F-13 / F-22）】
//   官方 DialogueUI 同一时间只服务一段对话：多选的 DisplayOptions 先 ReleaseAll 把前一段的选项顶掉，
//   字幕与多选各自共用 confirmed / confirmedChoice 一个字段。所以这里保证两件事：
//   1. **一次只有一段会话，谁开的谁收**（sessionOwner）。后来的调用方排队等前一段收完再开，
//      不再「开会话失败却照发请求、结束时无条件收掉别人的会话」——那会让一次点击同时答复两段、
//      先结束的一方把界面收掉，另一方在隐藏的界面上一直等。
//   2. **取消要把官方协程推完**。取消只停得了我们这边的等待，官方 DoSubtitle / DoMultipleChoice 还挂着；
//      它迟到的收尾（隐藏文本区、清空文字、置空说话人）会落到下一段头上。取消时替玩家按确认 / 选第一项
//      把它推完；推完、官方实例没了或超过 OfficialDrainSeconds 之前，下一段排队不发。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Dialogues;
using Duckov.UI.Animations;
using NodeCanvas.DialogueTrees;
using SodaCraft.Localizations;

namespace BossRush
{
    /// <summary>
    /// 大对话管理器 - 封装游戏原生对话系统
    /// </summary>
    public static class DialogueManager
    {
        // 日志标签
        private const string LOG_TAG = "[DialogueManager]";

        /// <summary>取消之后替玩家推完官方协程的最长秒数（真实时间）。超过就不再等它的回调。</summary>
        private const float OfficialDrainSeconds = 3f;

        // 当前是否正在显示对话
        private static bool isDialogueActive = false;

        /// <summary>当前会话的归属号，0 表示没有会话。只有开会话的那一方能收掉它。</summary>
        private static int sessionOwner;
        private static int lastSessionOwner;

        /// <summary>已经发给官方 DialogueUI、完成回调还没回来的请求数（字幕 + 多选）。不为 0 时新会话排队。</summary>
        private static int pendingOfficialRequests;

        // 输入禁用令牌（用于恢复输入）
        private static GameObject inputDisableToken = null;

        // 反射缓存：DialogueUI 的私有字段
        private static FieldInfo mainFadeGroupField = null;
        private static FieldInfo textAreaFadeGroupField = null;
        private static FieldInfo dialogueStatusChangedEventField = null;
        private static FieldInfo confirmedChoiceField = null;
        private static bool reflectionInitialized = false;

        /// <summary>
        /// 当前是否正在显示对话
        /// </summary>
        public static bool IsDialogueActive => isDialogueActive;

        /// <summary>一次发给官方 DialogueUI 的请求。完成回调与「推完 / 放弃」都只把计数减一次。</summary>
        private sealed class OfficialRequest
        {
            internal bool Completed;
            private bool released;

            internal void Complete()
            {
                Completed = true;
                Release();
            }

            internal void Release()
            {
                if (released) return;
                released = true;
                pendingOfficialRequests = Math.Max(0, pendingOfficialRequests - 1);
            }
        }

        /// <summary>
        /// 初始化反射字段（用于访问 DialogueUI 的私有成员）
        /// </summary>
        private static void InitializeReflection()
        {
            if (reflectionInitialized) return;

            try
            {
                Type dialogueUIType = typeof(DialogueUI);
                mainFadeGroupField = dialogueUIType.GetField("mainFadeGroup", BindingFlags.NonPublic | BindingFlags.Instance);
                textAreaFadeGroupField = dialogueUIType.GetField("textAreaFadeGroup", BindingFlags.NonPublic | BindingFlags.Instance);
                dialogueStatusChangedEventField = dialogueUIType.GetField("OnDialogueStatusChanged", BindingFlags.NonPublic | BindingFlags.Static);
                // 取消多选时替玩家选第一项，把官方 WaitForChoice 推出来（见文件头「会话归属与排队」）。
                confirmedChoiceField = dialogueUIType.GetField("confirmedChoice", BindingFlags.NonPublic | BindingFlags.Instance);

                if (mainFadeGroupField != null && textAreaFadeGroupField != null)
                {
                    ModBehaviour.DevLog(LOG_TAG + " 反射字段初始化成功");
                }
                else
                {
                    ModBehaviour.DevLog(LOG_TAG + " [WARNING] 反射字段初始化失败，部分字段为空");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] 反射初始化出错: " + e.Message);
            }

            reflectionInitialized = true;
        }

        /// <summary>
        /// 显示对话 UI 主面板（通过反射访问私有字段）
        /// </summary>
        private static void ShowDialogueUIPanel()
        {
            try
            {
                InitializeReflection();

                if (DialogueUI.instance == null || mainFadeGroupField == null) return;

                FadeGroup mainFadeGroup = mainFadeGroupField.GetValue(DialogueUI.instance) as FadeGroup;
                if (mainFadeGroup != null)
                {
                    mainFadeGroup.Show();
                    ModBehaviour.DevLog(LOG_TAG + " 已显示对话 UI 主面板");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 显示对话 UI 面板失败: " + e.Message);
            }
        }

        /// <summary>
        /// 显示对话文本区（通过反射访问私有字段）。官方多选的选项挂在文本区下面（BG/Options），
        /// 文本区的 FadeGroup 管着物体激活：每句字幕播完官方都会把它收起，没有前置字幕时它开局就是收起的，
        /// 而官方 DoMultipleChoice 自己不显示它——不补这一下，选项整棵子树都是失活的，看不见也点不到（2026-09-14 实机）。
        /// 用 Show 而不是 ShowAndReturnTask：后者会先 SkipHide 再淡入，闪一下。
        /// </summary>
        private static void ShowDialogueTextArea()
        {
            try
            {
                InitializeReflection();

                if (DialogueUI.instance == null || textAreaFadeGroupField == null) return;

                FadeGroup textAreaFadeGroup = textAreaFadeGroupField.GetValue(DialogueUI.instance) as FadeGroup;
                if (textAreaFadeGroup != null) textAreaFadeGroup.Show();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 显示对话文本区失败: " + e.Message);
            }
        }

        /// <summary>
        /// 隐藏对话 UI 主面板（通过反射访问私有字段）
        /// </summary>
        private static void HideDialogueUIPanel()
        {
            try
            {
                InitializeReflection();

                if (DialogueUI.instance == null || mainFadeGroupField == null) return;

                FadeGroup mainFadeGroup = mainFadeGroupField.GetValue(DialogueUI.instance) as FadeGroup;
                if (mainFadeGroup != null)
                {
                    mainFadeGroup.Hide();
                    ModBehaviour.DevLog(LOG_TAG + " 已隐藏对话 UI 主面板");
                }

                // 同时隐藏文本区域
                if (textAreaFadeGroupField != null)
                {
                    FadeGroup textAreaFadeGroup = textAreaFadeGroupField.GetValue(DialogueUI.instance) as FadeGroup;
                    if (textAreaFadeGroup != null)
                    {
                        textAreaFadeGroup.Hide();
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 隐藏对话 UI 面板失败: " + e.Message);
            }
        }

        /// <summary>
        /// 手动广播 DialogueUI 状态变化事件（兼容 RequestSubtitles 路径）
        /// 原版 HUDManager 依赖该事件刷新 HUD 显隐。
        /// </summary>
        private static void NotifyDialogueStatusChanged()
        {
            try
            {
                InitializeReflection();
                if (dialogueStatusChangedEventField == null) return;

                Action handler = dialogueStatusChangedEventField.GetValue(null) as Action;
                if (handler != null)
                {
                    handler.Invoke();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 触发 OnDialogueStatusChanged 失败: " + e.Message);
            }
        }

        // ============================================================================
        // 单条对话
        // ============================================================================

        /// <summary>
        /// 显示单条大对话（使用本地化键）
        /// </summary>
        /// <param name="actor">对话角色</param>
        /// <param name="localizationKey">本地化键</param>
        /// <returns>对话完成后返回</returns>
        public static async UniTask ShowDialogue(IDialogueActor actor, string localizationKey)
        {
            if (actor == null)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] actor 为空，跳过对话");
                return;
            }

            if (string.IsNullOrEmpty(localizationKey))
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] localizationKey 为空，跳过对话");
                return;
            }

            try
            {
                // 创建本地化语句
                LocalizedStatement statement = new LocalizedStatement(localizationKey);

                // 显示对话并等待完成
                await ShowDialogueInternal(actor, statement);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] ShowDialogue 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 显示单条大对话（直接使用双语文本，自动注入本地化）
        /// </summary>
        /// <param name="actor">对话角色</param>
        /// <param name="textCN">中文文本</param>
        /// <param name="textEN">英文文本</param>
        /// <param name="tempKey">临时本地化键（可选，默认自动生成）</param>
        /// <returns>对话完成后返回</returns>
        public static async UniTask ShowDialogueBilingual(IDialogueActor actor, string textCN, string textEN, string tempKey = null)
        {
            if (actor == null)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] actor 为空，跳过对话");
                return;
            }

            try
            {
                // 生成临时本地化键
                if (string.IsNullOrEmpty(tempKey))
                {
                    tempKey = "BossRush_TempDialogue_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                }

                // 注入本地化
                string localizedText = L10n.T(textCN, textEN);
                LocalizationHelper.InjectLocalization(tempKey, localizedText);

                // 创建本地化语句
                LocalizedStatement statement = new LocalizedStatement(tempKey);

                // 显示对话并等待完成
                await ShowDialogueInternal(actor, statement);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] ShowDialogueBilingual 出错: " + e.Message);
            }
        }

        // ============================================================================
        // 对话序列
        // ============================================================================

        /// <summary>
        /// 显示对话序列（使用本地化键数组）
        /// </summary>
        /// <param name="actor">对话角色</param>
        /// <param name="localizationKeys">本地化键数组</param>
        /// <returns>对话序列完成后返回</returns>
        public static async UniTask ShowDialogueSequence(IDialogueActor actor, string[] localizationKeys)
        {
            if (actor == null || localizationKeys == null || localizationKeys.Length == 0)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 参数无效，跳过对话序列");
                return;
            }

            int owner = 0;
            try
            {
                // 开始对话序列（另一段对话还开着时排队）
                owner = await AcquireSession(CancellationToken.None);

                for (int i = 0; i < localizationKeys.Length; i++)
                {
                    ModBehaviour.DevLog(LOG_TAG + " 显示对话 " + (i + 1) + "/" + localizationKeys.Length);

                    LocalizedStatement statement = new LocalizedStatement(localizationKeys[i]);
                    await ShowDialogueInternal(actor, statement, skipInputManagement: true);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] ShowDialogueSequence 出错: " + e.Message);
            }
            finally
            {
                // 结束对话序列：只收自己开的会话
                EndDialogueSession(owner);
            }
        }

        /// <summary>
        /// 显示对话序列（使用双语文本数组）
        /// </summary>
        /// <param name="actor">对话角色</param>
        /// <param name="dialogues">对话数组，每项为 [中文, 英文]</param>
        /// <param name="keyPrefix">本地化键前缀</param>
        /// <returns>对话序列完成后返回</returns>
        public static UniTask ShowDialogueSequenceBilingual(IDialogueActor actor, string[][] dialogues, string keyPrefix = "BossRush_Dialogue")
        {
            return ShowDialogueSequenceBilingual(actor, dialogues, keyPrefix, CancellationToken.None);
        }

        /// <summary>带会话取消的重载；取消必须传到实际等待，不能只隐藏 UI。</summary>
        public static async UniTask ShowDialogueSequenceBilingual(IDialogueActor actor, string[][] dialogues, string keyPrefix, CancellationToken cancellationToken)
        {
            if (actor == null || dialogues == null || dialogues.Length == 0)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 参数无效，跳过对话序列");
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            int owner = 0;
            try
            {
                // 开始对话序列（另一段对话还开着时排队）
                owner = await AcquireSession(cancellationToken);

                for (int i = 0; i < dialogues.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (dialogues[i] == null || dialogues[i].Length < 2) continue;

                    string textCN = dialogues[i][0];
                    string textEN = dialogues[i][1];
                    string key = keyPrefix + "_" + i;

                    // 注入本地化
                    string localizedText = L10n.T(textCN, textEN);
                    LocalizationHelper.InjectLocalization(key, localizedText);

                    ModBehaviour.DevLog(LOG_TAG + " 显示对话 " + (i + 1) + "/" + dialogues.Length + ": " + localizedText);

                    LocalizedStatement statement = new LocalizedStatement(key);
                    await ShowDialogueInternal(actor, statement, skipInputManagement: true, cancellationToken: cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] ShowDialogueSequenceBilingual 出错: " + e.Message);
            }
            finally
            {
                // 结束对话序列：只收自己开的会话
                EndDialogueSession(owner);
            }
        }

        // ============================================================================
        // 多选对话
        // ============================================================================

        /// <summary>
        /// 显示多选对话（使用本地化键）
        /// </summary>
        /// <param name="actor">对话角色</param>
        /// <param name="choiceKeys">选项本地化键数组</param>
        /// <param name="timeout">超时时间（秒），0表示无限等待</param>
        /// <returns>玩家选择的索引（从0开始），超时返回-1</returns>
        public static async UniTask<int> ShowMultipleChoice(IDialogueActor actor, string[] choiceKeys, float timeout = 0f)
        {
            if (actor == null || choiceKeys == null || choiceKeys.Length == 0)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 参数无效，跳过多选对话");
                return -1;
            }

            try
            {
                // 构建选项字典
                Dictionary<IStatement, int> options = new Dictionary<IStatement, int>();
                for (int i = 0; i < choiceKeys.Length; i++)
                {
                    LocalizedStatement statement = new LocalizedStatement(choiceKeys[i]);
                    options[statement] = i;
                }

                return await ShowMultipleChoiceInternal(actor, options, timeout);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] ShowMultipleChoice 出错: " + e.Message);
                return -1;
            }
        }

        /// <summary>
        /// 显示多选对话（使用双语文本）
        /// </summary>
        /// <param name="actor">对话角色</param>
        /// <param name="choices">选项数组，每项为 [中文, 英文]</param>
        /// <param name="timeout">超时时间（秒），0表示无限等待</param>
        /// <param name="keyPrefix">本地化键前缀</param>
        /// <returns>玩家选择的索引（从0开始），超时返回-1</returns>
        public static UniTask<int> ShowMultipleChoiceBilingual(IDialogueActor actor, string[][] choices, float timeout = 0f, string keyPrefix = "BossRush_Choice")
        {
            return ShowMultipleChoiceBilingual(actor, choices, timeout, keyPrefix, CancellationToken.None);
        }

        /// <summary>带会话取消的重载；保留原四参签名供既有调用方使用。</summary>
        public static async UniTask<int> ShowMultipleChoiceBilingual(IDialogueActor actor, string[][] choices, float timeout, string keyPrefix, CancellationToken cancellationToken)
        {
            if (actor == null || choices == null || choices.Length == 0)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 参数无效，跳过多选对话");
                return -1;
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // 构建选项字典
                Dictionary<IStatement, int> options = new Dictionary<IStatement, int>();
                for (int i = 0; i < choices.Length; i++)
                {
                    if (choices[i] == null || choices[i].Length < 2) continue;

                    string key = keyPrefix + "_" + i;
                    string localizedText = L10n.T(choices[i][0], choices[i][1]);
                    LocalizationHelper.InjectLocalization(key, localizedText);

                    LocalizedStatement statement = new LocalizedStatement(key);
                    options[statement] = i;
                }

                return await ShowMultipleChoiceInternal(actor, options, timeout, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] ShowMultipleChoiceBilingual 出错: " + e.Message);
                return -1;
            }
        }


        // ============================================================================
        // 内部实现
        // ============================================================================


        /// <summary>
        /// 显示单条对话的内部实现
        /// 使用 DialogueTree.RequestSubtitles() 触发事件，由 DialogueUI 处理显示
        /// 需要先手动显示 mainFadeGroup，因为 OnDialogueStarted 不会被触发
        /// </summary>
        private static async UniTask ShowDialogueInternal(IDialogueActor actor, LocalizedStatement statement, bool skipInputManagement = false, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 检查 DialogueUI 是否可用
            if (DialogueUI.instance == null)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] DialogueUI.instance 为空，跳过对话");
                return;
            }

            int owner = 0;
            OfficialRequest request = null;
            try
            {
                // 管理输入状态（序列里的逐句已经由序列持有会话）
                if (!skipInputManagement)
                {
                    owner = await AcquireSession(cancellationToken);
                }

                // 请求独占：迟到回调只标记这一次请求，不能推进下一段。
                request = new OfficialRequest();
                OfficialRequest captured = request;

                // 创建对话请求信息，带完成回调
                SubtitlesRequestInfo info = new SubtitlesRequestInfo(
                    actor,
                    statement,
                    () => {
                        // 对话完成回调
                        captured.Complete();
                        ModBehaviour.DevLog(LOG_TAG + " 对话回调触发，标记完成");
                    }
                );

                // 使用 DialogueTree.RequestSubtitles 触发事件
                // DialogueUI 已订阅此事件，会调用 DoSubtitle
                pendingOfficialRequests++;
                DialogueTree.RequestSubtitles(info);

                // 等待对话完成回调（玩家点击继续）
                await UniTask.WaitUntil(() => captured.Completed, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (request != null && !request.Completed) DrainOfficialSubtitle(request).Forget();
                throw;
            }
            catch (Exception e)
            {
                // 请求没发出去或官方抛了异常：不再等它的回调，免得后面的对话一直排队。
                if (request != null && !request.Completed) request.Release();
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] ShowDialogueInternal 出错: " + e.Message + "\n" + e.StackTrace);
                throw;  // 重新抛出以便上层捕获
            }
            finally
            {
                if (!skipInputManagement)
                {
                    EndDialogueSession(owner);
                }
            }
        }


        /// <summary>
        /// 显示多选对话的内部实现
        /// 注意：直接调用 DialogueUI 的方法而不是通过 DialogueTree 事件
        /// </summary>
        private static async UniTask<int> ShowMultipleChoiceInternal(IDialogueActor actor, Dictionary<IStatement, int> options, float timeout, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 检查 DialogueUI 是否可用
            if (DialogueUI.instance == null)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] DialogueUI.instance 为空，跳过多选对话");
                return -1;
            }

            int owner = 0;
            OfficialRequest request = null;
            int multipleChoiceResult = -1;
            try
            {
                // 管理输入状态（另一段对话还开着时排队）
                owner = await AcquireSession(cancellationToken);
                // 选项挂在文本区下面，发请求之前先把它显示出来（见 ShowDialogueTextArea）。
                ShowDialogueTextArea();

                request = new OfficialRequest();
                OfficialRequest captured = request;

                // 创建多选请求信息
                MultipleChoiceRequestInfo info = new MultipleChoiceRequestInfo(
                    actor,
                    options,
                    timeout,
                    (selectedIndex) => {
                        multipleChoiceResult = selectedIndex;
                        captured.Complete();
                        ModBehaviour.DevLog(LOG_TAG + " 多选回调触发，选择: " + selectedIndex);
                    }
                );

                // 使用官方的 RequestMultipleChoices 方法（多选对话需要通过事件系统）
                pendingOfficialRequests++;
                DialogueTree.RequestMultipleChoices(info);

                // 等待玩家选择
                await UniTask.WaitUntil(() => captured.Completed, cancellationToken: cancellationToken);

                ModBehaviour.DevLog(LOG_TAG + " 玩家选择了选项: " + multipleChoiceResult);
                return multipleChoiceResult;
            }
            catch (OperationCanceledException)
            {
                if (request != null && !request.Completed) DrainOfficialChoice(request).Forget();
                throw;
            }
            catch (Exception)
            {
                if (request != null && !request.Completed) request.Release();
                throw;
            }
            finally
            {
                EndDialogueSession(owner);
            }
        }

        /// <summary>
        /// 取消之后替玩家把官方 DoSubtitle 推完：逐字显示阶段与等确认阶段都认 <c>DialogueUI.Confirm()</c>。
        /// 推完、官方实例没了或超过 <see cref="OfficialDrainSeconds"/> 就停，并放掉请求计数，下一段才发得出去。
        /// </summary>
        private static async UniTaskVoid DrainOfficialSubtitle(OfficialRequest request)
        {
            float until = Time.realtimeSinceStartup + OfficialDrainSeconds;
            try
            {
                while (!request.Completed && Time.realtimeSinceStartup < until)
                {
                    DialogueUI ui = DialogueUI.instance;
                    if (ui == null) break;
                    ui.Confirm();
                    await UniTask.NextFrame();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 推完被取消的字幕失败: " + e.Message);
            }
            finally
            {
                request.Release();
            }
        }

        /// <summary>
        /// 取消之后替玩家选第一项，把官方 WaitForChoice 推出来：选项列表随之淡出，不会留在屏幕上。
        /// 回调里的选择结果没人再读（我们这边已经按取消返回）。
        /// </summary>
        private static async UniTaskVoid DrainOfficialChoice(OfficialRequest request)
        {
            float until = Time.realtimeSinceStartup + OfficialDrainSeconds;
            try
            {
                while (!request.Completed && Time.realtimeSinceStartup < until)
                {
                    DialogueUI ui = DialogueUI.instance;
                    if (ui == null || confirmedChoiceField == null) break;
                    // WaitForChoice 开头会把它复位成 -1，所以每帧看一眼、没选就选上。
                    if ((int)confirmedChoiceField.GetValue(ui) < 0) confirmedChoiceField.SetValue(ui, 0);
                    await UniTask.NextFrame();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 推完被取消的多选失败: " + e.Message);
            }
            finally
            {
                request.Release();
            }
        }

        // ============================================================================
        // 输入管理
        // ============================================================================

        /// <summary>
        /// 排队拿会话：另一段对话还开着，或者被取消的上一段官方协程还没推完时，逐帧等它空出来再开。
        /// 取消令牌照常生效；官方实例没了（切图）时不再等官方回调。
        /// </summary>
        private static async UniTask<int> AcquireSession(CancellationToken cancellationToken)
        {
            if (sessionOwner != 0 || pendingOfficialRequests > 0)
            {
                await UniTask.WaitUntil(() =>
                {
                    if (DialogueUI.instance == null) pendingOfficialRequests = 0;
                    return sessionOwner == 0 && pendingOfficialRequests == 0;
                }, cancellationToken: cancellationToken);
            }

            int owner = ++lastSessionOwner;
            if (owner <= 0)
            {
                owner = lastSessionOwner = 1;
            }
            BeginDialogueSession(owner);
            return owner;
        }

        /// <summary>
        /// 开始对话会话（禁用玩家输入，显示对话UI）
        /// </summary>
        private static void BeginDialogueSession(int owner)
        {
            sessionOwner = owner;
            if (isDialogueActive) return;

            isDialogueActive = true;

            try
            {
                // 创建输入禁用令牌
                if (inputDisableToken == null)
                {
                    inputDisableToken = new GameObject("DialogueManager_InputToken");
                    UnityEngine.Object.DontDestroyOnLoad(inputDisableToken);
                }

                // 使用游戏原生的输入管理器禁用输入
                InputManager.DisableInput(inputDisableToken);

                // 显示对话 UI 主面板（通过反射）
                ShowDialogueUIPanel();
                NotifyDialogueStatusChanged();

                ModBehaviour.DevLog(LOG_TAG + " 已禁用玩家输入并显示对话UI");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 开始对话会话失败: " + e.Message);
            }
        }

        /// <summary>
        /// 结束对话会话（恢复玩家输入，隐藏对话UI）。只有开会话的那一方收得掉；
        /// 排队没拿到会话（owner 为 0）或会话已被强制结束时什么都不做。
        /// </summary>
        private static void EndDialogueSession(int owner)
        {
            if (owner == 0 || owner != sessionOwner) return;
            sessionOwner = 0;
            CloseDialogueSession();
        }

        private static void CloseDialogueSession()
        {
            if (!isDialogueActive) return;

            isDialogueActive = false;

            try
            {
                // 隐藏对话 UI 主面板（通过反射）
                HideDialogueUIPanel();

                // 使用游戏原生的输入管理器恢复输入
                if (inputDisableToken != null)
                {
                    InputManager.ActiveInput(inputDisableToken);
                }
                NotifyDialogueStatusChanged();

                ModBehaviour.DevLog(LOG_TAG + " 已恢复玩家输入并隐藏对话UI");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 结束对话会话失败: " + e.Message);
            }
        }

        /// <summary>
        /// 强制结束当前对话（用于异常情况）
        /// </summary>
        public static void ForceEndDialogue()
        {
            try
            {
                // 强制重置状态：不管会话是谁开的
                sessionOwner = 0;
                isDialogueActive = true;  // 确保 CloseDialogueSession 会执行
                CloseDialogueSession();

                // 额外尝试隐藏对话UI（双重保险）
                if (DialogueUI.instance != null)
                {
                    DialogueUI.HideTextFadeGroup();
                }

                ModBehaviour.DevLog(LOG_TAG + " 已强制结束对话");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 强制结束对话失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清理资源（在模组卸载时调用）
        /// </summary>
        public static void Cleanup()
        {
            try
            {
                sessionOwner = 0;
                pendingOfficialRequests = 0;
                CloseDialogueSession();

                if (inputDisableToken != null)
                {
                    UnityEngine.Object.Destroy(inputDisableToken);
                    inputDisableToken = null;
                }

                ModBehaviour.DevLog(LOG_TAG + " 资源已清理");
            }
            catch { }
        }
    }
}
