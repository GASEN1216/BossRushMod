// ============================================================================
// SkyIslandResidentDialogue.cs - 居民叙事走官方对话
// ============================================================================
// 【为什么改】官方对话（NodeCanvas `DialogueTree` + `Dialogues.DialogueUI`）**我们自己早就封装好了**：
//   `Integration/Dialogue/DialogueManager.cs` 的 ShowDialogueSequenceBilingual / ShowMultipleChoiceBilingual，
//   actor 走 `DialogueActorFactory.CreateBilingual`（自带立绘位）。
//   征程（`CampaignDialoguePlayer`）与快递员（`CourierNPCController`）都在用，**唯独天空岛一行没调**——
//   而且代码与文档里从来没写过「为什么不用」。
//
// 【分工：叙事走官方，功能留自绘】
//   - **官方对话**负责说话：逐句推进、自带立绘、玩家按键翻页。
//     居民台词里最长的一段有 156 个中文字符，此前是一次性糊在面板的正文位上。
//   - **自绘面板**（`SkyIslandStoryPresentation`）负责办事：接委托、苔药、整备、合成、手记。
//     它保留的唯一理由是**模态**——官方 `DialogueUI` 只给「台词 + 纯文本选项」，
//     给不了 `timeScale = 0`，而面板里挂着眠苔的苔药与浮舟的整备：
//     没有模态门它就是战斗中的免费暂停 + 回血站。这条理由此前只存在于 repowiki，
//     现在写进 `SkyIslandStoryPresentation` 的文件头。
//
// 【时间不暂停】官方对话**不压 timeScale**。会话侧的 `BlockedByCombat()` 已经挡住战斗中开口，
//   新威胁出现、死亡、切图或关闭界面时取消底层等待，恢复输入，不再推进后续台词。
//
// 【降级】官方对话拿不到 actor 或抛异常且会话仍有效时打开功能面板；
//   主动取消、死亡、战斗或切图不触发降级，避免旧对话在新场景里弹面板。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NodeCanvas.DialogueTrees;
using UnityEngine;

namespace BossRush
{
    /// <summary>每段居民对话独占取消源；actor 仍挂在 NPC 上，随 NPC 销毁。</summary>
    internal sealed class SkyIslandResidentDialogue : IDisposable
    {
        private const string LogPrefix = "[SkyIslandTalk] ";
        private const string LineKeyPrefix = "BossRush_SkyIslandTalk";
        private const string ChoiceKeyPrefix = "BossRush_SkyIslandTalkChoice";

        private CancellationTokenSource cancellation = new CancellationTokenSource();
        private Func<bool> valid;
        internal bool Active { get { return cancellation != null; } }

        /// <summary>同步返回会话 owner；功能面板仅由仍有效的异步回调打开。</summary>
        internal static SkyIslandResidentDialogue Run(string npcId, Transform speaker, string body,
            Action openPanel, Func<bool> valid)
        {
            if (openPanel == null || valid == null || !valid() || DialogueManager.IsDialogueActive) return null;
            var dialogue = new SkyIslandResidentDialogue();
            bool hadSpeaker = speaker != null;
            dialogue.valid = () => valid() && (!hadSpeaker || speaker != null);
            dialogue.RunAsync(npcId, speaker, body, openPanel).Forget();
            return dialogue;
        }

        internal bool CanContinue()
        {
            return cancellation != null && !cancellation.IsCancellationRequested && valid != null && valid();
        }

        public void Dispose()
        {
            // 由异步 finally 释放取消源；这里只取消自己持有的等待，不强制结束其它模块的对话。
            if (cancellation != null) cancellation.Cancel();
        }

        /// <summary>
        /// 取（或建）这位居民的官方对话 actor。立绘喂现成的
        /// <see cref="SkyIslandUiArt.GetPortrait"/>——**不为对话另出一套图**。
        /// actor 是挂在 NPC GameObject 上的组件，随 NPC 一起销毁，不需要额外清理。
        /// </summary>
        private static IDialogueActor EnsureActor(string npcId, Transform speaker)
        {
            if (speaker == null || string.IsNullOrEmpty(npcId)) return null;
            GameObject host = speaker.gameObject;
            // ResidentName 内部已经取过 L10n.T，两个语位填同一个值就是那个名字本身
            // （口径同 Split 里的台词）。每次开口都按当前语言取一次。
            string name = SkyIslandWorldStory.ResidentName(npcId);
            DuckovDialogueActor existing = DialogueActorFactory.Get(host);
            if (existing != null)
            {
                // actor 按 NPC 缓存、名字只在创建时注入：本趟切过语言的话，官方对话框里的名字要跟着换（2026-09-14 审核 F-29）。
                DialogueActorFactory.RefreshBilingualName(npcId, name, name);
                return existing;
            }
            return DialogueActorFactory.CreateBilingual(host, npcId, name, name,
                new Vector3(0f, 2f, 0f), SkyIslandUiArt.GetPortrait(npcId));
        }

        private async UniTaskVoid RunAsync(string npcId, Transform speaker, string body, Action openPanel)
        {
            CancellationToken token = cancellation.Token;
            try
            {
                IDialogueActor actor = EnsureActor(npcId, speaker);
                if (actor == null)
                {
                    if (CanContinue()) openPanel();
                    return;
                }
                string[][] lines = Split(body);
                if (lines.Length > 0)
                    await DialogueManager.ShowDialogueSequenceBilingual(actor, lines, LineKeyPrefix, token);

                if (!CanContinue() || speaker == null) return;

                int picked = await DialogueManager.ShowMultipleChoiceBilingual(actor, new string[][]
                {
                    // 中英成对写在同一条语句里（本地化守卫认的对照表形状）。
                    Pair("我想办点事", "There is something I need"),
                    Pair("先这样", "That is all for now"),
                }, 0f, ChoiceKeyPrefix, token);

                // 超时（-1）也当作「要办事」：宁可多开一次面板，也不要让人白跟 NPC 说了一通。
                if (picked != 1 && CanContinue() && speaker != null) openPanel();
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 官方对话不可用: " + e.Message);
                if (CanContinue()) openPanel();
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                valid = null;
            }
        }

        /// <summary>中英对照的一条选项。</summary>
        private static string[] Pair(string cn, string en) { return new[] { L10n.T(cn, en), L10n.T(cn, en) }; }

        /// <summary>短于这个可见字数的半句（像「嗯。」「Hm.」）并进同一段里的下一句，免得一屏只有一个字。</summary>
        internal const int MinSentenceChars = 8;

        /// <summary>
        /// 台词拆成**一句一屏**：先按换行分段，再在句末标点处断句——中文 。！？；…，英文 . ! ? 后面跟空格或段尾。
        /// 句末标点后紧跟的右引号、右括号属于这一句；太短的半句并进下一句；段落结束一定断开。
        /// 旧写法只按换行拆，居民台词 50 段里 15 段 ≥40 字、最长一屏 66 字三句话
        /// （2026-09-14 审核 F-10，AGENTS §4.14「长文案一句一屏」）。
        ///
        /// `DescribeNpc` 返回的是**已经按当前语言解析过**的字符串（`L10n.T` 在它内部就取过了），
        /// 所以这里两个语位填同一个值——双语接口拿到相同的中英文，结果就是那句话本身。
        /// 空行丢掉：拼接处常留下多余的 "\n"。
        /// </summary>
        internal static string[][] Split(string body)
        {
            var lines = new List<string[]>();
            if (string.IsNullOrEmpty(body)) return lines.ToArray();
            string[] paragraphs = body.Split('\n');
            var sentence = new System.Text.StringBuilder();
            for (int p = 0; p < paragraphs.Length; p++)
            {
                string paragraph = paragraphs[p].Trim();
                for (int i = 0; i < paragraph.Length; i++)
                {
                    sentence.Append(paragraph[i]);
                    if (!EndsSentence(paragraph, i)) continue;
                    while (i + 1 < paragraph.Length && IsClosingMark(paragraph[i + 1]))
                        sentence.Append(paragraph[++i]);
                    if (VisibleLength(sentence) >= MinSentenceChars) Flush(sentence, lines);
                }
                Flush(sentence, lines);
            }
            return lines.ToArray();
        }

        private static bool IsTerminal(char c)
        {
            return c == '。' || c == '！' || c == '？' || c == '；' || c == '…' || c == '!' || c == '?' || c == '.';
        }

        private static bool IsClosingMark(char c)
        {
            return c == '」' || c == '』' || c == '”' || c == '’' || c == '）' || c == ')' || c == '"' || c == '】' || c == '》';
        }

        /// <summary>第 index 个字符是不是一句的结尾。连续的「？！」「……」「...」只在最后一个处断；英文小数点不断。</summary>
        private static bool EndsSentence(string text, int index)
        {
            char c = text[index];
            if (!IsTerminal(c)) return false;
            bool last = index + 1 >= text.Length;
            if (!last && IsTerminal(text[index + 1])) return false;
            if (c == '.' || c == '!' || c == '?')
            {
                if (last) return true;
                char next = text[index + 1];
                return next == ' ' || IsClosingMark(next);
            }
            return true;
        }

        private static int VisibleLength(System.Text.StringBuilder text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
                if (!char.IsWhiteSpace(text[i])) count++;
            return count;
        }

        private static void Flush(System.Text.StringBuilder sentence, List<string[]> lines)
        {
            string line = sentence.ToString().Trim();
            sentence.Length = 0;
            if (line.Length > 0) lines.Add(new[] { line, line });
        }
    }
}
