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
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
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
        
        // 当前是否正在显示对话
        private static bool isDialogueActive = false;
        
        // 输入禁用令牌（用于恢复输入）
        private static GameObject inputDisableToken = null;
        
        // 反射缓存：DialogueUI 的私有字段
        private static FieldInfo mainFadeGroupField = null;
        private static FieldInfo textAreaFadeGroupField = null;
        private static bool reflectionInitialized = false;
        
        /// <summary>
        /// 当前是否正在显示对话
        /// </summary>
        public static bool IsDialogueActive => isDialogueActive;
        
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
            
            try
            {
                // 开始对话序列
                BeginDialogueSession();
                
                for (int i = 0; i < localizationKeys.Length; i++)
                {
                    ModBehaviour.DevLog(LOG_TAG + " 显示对话 " + (i + 1) + "/" + localizationKeys.Length);
                    
                    LocalizedStatement statement = new LocalizedStatement(localizationKeys[i]);
                    await ShowDialogueInternal(actor, statement, skipInputManagement: true);
                }
                
                // 结束对话序列
                EndDialogueSession();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] ShowDialogueSequence 出错: " + e.Message);
                EndDialogueSession();
            }
        }
        
        /// <summary>
        /// 显示对话序列（使用双语文本数组）
        /// </summary>
        /// <param name="actor">对话角色</param>
        /// <param name="dialogues">对话数组，每项为 [中文, 英文]</param>
        /// <param name="keyPrefix">本地化键前缀</param>
        /// <returns>对话序列完成后返回</returns>
        public static async UniTask ShowDialogueSequenceBilingual(IDialogueActor actor, string[][] dialogues, string keyPrefix = "BossRush_Dialogue")
        {
            if (actor == null || dialogues == null || dialogues.Length == 0)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 参数无效，跳过对话序列");
                return;
            }
            
            try
            {
                // 开始对话序列
                BeginDialogueSession();
                
                for (int i = 0; i < dialogues.Length; i++)
                {
                    if (dialogues[i] == null || dialogues[i].Length < 2) continue;
                    
                    string textCN = dialogues[i][0];
                    string textEN = dialogues[i][1];
                    string key = keyPrefix + "_" + i;
                    
                    // 注入本地化
                    string localizedText = L10n.T(textCN, textEN);
                    LocalizationHelper.InjectLocalization(key, localizedText);
                    
                    ModBehaviour.DevLog(LOG_TAG + " 显示对话 " + (i + 1) + "/" + dialogues.Length + ": " + localizedText);
                    
                    LocalizedStatement statement = new LocalizedStatement(key);
                    await ShowDialogueInternal(actor, statement, skipInputManagement: true);
                }
                
                // 结束对话序列
                EndDialogueSession();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] ShowDialogueSequenceBilingual 出错: " + e.Message);
                EndDialogueSession();
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
        public static async UniTask<int> ShowMultipleChoiceBilingual(IDialogueActor actor, string[][] choices, float timeout = 0f, string keyPrefix = "BossRush_Choice")
        {
            if (actor == null || choices == null || choices.Length == 0)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 参数无效，跳过多选对话");
                return -1;
            }
            
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
                
                return await ShowMultipleChoiceInternal(actor, options, timeout);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] ShowMultipleChoiceBilingual 出错: " + e.Message);
                return -1;
            }
        }

        
        // ============================================================================
        // 内部实现
        // ============================================================================
        
        // 对话完成标志（用于等待回调）
        private static bool dialogueCompleted = false;
        
        /// <summary>
        /// 显示单条对话的内部实现
        /// 使用 DialogueTree.RequestSubtitles() 触发事件，由 DialogueUI 处理显示
        /// 需要先手动显示 mainFadeGroup，因为 OnDialogueStarted 不会被触发
        /// </summary>
        private static async UniTask ShowDialogueInternal(IDialogueActor actor, LocalizedStatement statement, bool skipInputManagement = false)
        {
            // 检查 DialogueUI 是否可用
            if (DialogueUI.instance == null)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] DialogueUI.instance 为空，跳过对话");
                return;
            }
            
            // 管理输入状态
            if (!skipInputManagement)
            {
                BeginDialogueSession();
            }
            
            try
            {
                // 重置完成标志
                dialogueCompleted = false;
                
                // 创建对话请求信息，带完成回调
                SubtitlesRequestInfo info = new SubtitlesRequestInfo(
                    actor,
                    statement,
                    () => { 
                        // 对话完成回调
                        dialogueCompleted = true;
                        ModBehaviour.DevLog(LOG_TAG + " 对话回调触发，标记完成");
                    }
                );
                
                // 使用 DialogueTree.RequestSubtitles 触发事件
                // DialogueUI 已订阅此事件，会调用 DoSubtitle
                DialogueTree.RequestSubtitles(info);
                
                // 等待对话完成回调（玩家点击继续）
                // 使用 UniTask.WaitUntil 等待标志变为 true
                await UniTask.WaitUntil(() => dialogueCompleted);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [ERROR] ShowDialogueInternal 出错: " + e.Message + "\n" + e.StackTrace);
                throw;  // 重新抛出以便上层捕获
            }
            finally
            {
                if (!skipInputManagement)
                {
                    EndDialogueSession();
                }
            }
        }
        
        // 多选对话结果（用于等待回调）
        private static int multipleChoiceResult = -1;
        private static bool multipleChoiceCompleted = false;
        
        /// <summary>
        /// 显示多选对话的内部实现
        /// 注意：直接调用 DialogueUI 的方法而不是通过 DialogueTree 事件
        /// </summary>
        private static async UniTask<int> ShowMultipleChoiceInternal(IDialogueActor actor, Dictionary<IStatement, int> options, float timeout)
        {
            // 检查 DialogueUI 是否可用
            if (DialogueUI.instance == null)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] DialogueUI.instance 为空，跳过多选对话");
                return -1;
            }
            
            // 管理输入状态
            BeginDialogueSession();
            
            try
            {
                // 重置完成标志
                multipleChoiceCompleted = false;
                multipleChoiceResult = -1;
                
                // 创建多选请求信息
                MultipleChoiceRequestInfo info = new MultipleChoiceRequestInfo(
                    actor,
                    options,
                    timeout,
                    (selectedIndex) => { 
                        multipleChoiceResult = selectedIndex;
                        multipleChoiceCompleted = true;
                        ModBehaviour.DevLog(LOG_TAG + " 多选回调触发，选择: " + selectedIndex);
                    }
                );
                
                // 使用官方的 RequestMultipleChoices 方法（多选对话需要通过事件系统）
                DialogueTree.RequestMultipleChoices(info);
                
                // 等待玩家选择
                await UniTask.WaitUntil(() => multipleChoiceCompleted);
                
                ModBehaviour.DevLog(LOG_TAG + " 玩家选择了选项: " + multipleChoiceResult);
                return multipleChoiceResult;
            }
            finally
            {
                EndDialogueSession();
            }
        }
        
        // ============================================================================
        // 输入管理
        // ============================================================================
        
        /// <summary>
        /// 开始对话会话（禁用玩家输入，显示对话UI）
        /// </summary>
        private static void BeginDialogueSession()
        {
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
                
                ModBehaviour.DevLog(LOG_TAG + " 已禁用玩家输入并显示对话UI");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LOG_TAG + " [WARNING] 开始对话会话失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 结束对话会话（恢复玩家输入，隐藏对话UI）
        /// </summary>
        private static void EndDialogueSession()
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
                // 强制重置状态
                isDialogueActive = true;  // 确保 EndDialogueSession 会执行
                EndDialogueSession();
                
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
                EndDialogueSession();
                
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
