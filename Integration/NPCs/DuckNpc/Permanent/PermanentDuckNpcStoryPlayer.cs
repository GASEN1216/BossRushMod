// ============================================================================
// PermanentDuckNpcStoryPlayer.cs - 永久捏脸 NPC 的好感剧情播放
// ============================================================================
// 模块说明：
//   蓝图 permanent.storyEvents 给某一级配了剧情时，到级后找这位 NPC「聊天」，
//   用官方大对话（DialogueManager.ShowDialogueSequenceBilingual）一句一屏播完，
//   **播完才**打该级剧情标（AffinityManager.MarkStoryTriggered），中途被打断下次聊天重播。
//   一次聊天只播一段，按等级从低到高补。
//
//   「看过」与「剧情标」分开记：
//     story_N      剧情标。婚礼教堂判据（10 级）与好感衰减下限都看它，load-bearing。
//     story_seen_N 剧情真的播完了。旧档里 story_N 是当年只打标、只弹横幅时打上的，
//                  不能当成看过，所以另记一个；旧档到级的人第一次聊天会把新剧情补上。
//
//   没有剧情的等级仍由 PermanentDuckNpcInteractable 当场打标 + 通用横幅。
//   本类是交互组件持有的一个普通对象：状态（播放中、取消源）只属于这一只 NPC，
//   不进宿主 partial，也没有每帧回调（AGENTS.md §4.12、§4.15）。
// ============================================================================

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Duckov.UI;
using ItemStatsSystem;
using NodeCanvas.DialogueTrees;
using UnityEngine;

namespace BossRush
{
    internal sealed class PermanentDuckNpcStoryPlayer : IDisposable
    {
        private const string LogPrefix = "[PermanentDuckNpc]";
        private const string SeenKeyPrefix = "story_seen_";
        private const string RewardKeyPrefix = "duck_story_reward_";

        private readonly string _npcId;
        private CancellationTokenSource _cancellation;

        internal PermanentDuckNpcStoryPlayer(string npcId)
        {
            _npcId = npcId;
        }

        /// <summary>正在播剧情（这一只 NPC）。</summary>
        internal bool IsPlaying
        {
            get { return _cancellation != null; }
        }

        internal static string SeenKey(int level)
        {
            return SeenKeyPrefix + level;
        }

        /// <summary>这一级配了剧情、而且还没播完过。</summary>
        internal static bool IsPending(string npcId, PermanentDuckNpcStoryEvent story)
        {
            return story != null && !AffinityManager.HasTriggeredEvent(npcId, SeenKey(story.level));
        }

        /// <summary>当前等级下最低的一段待播剧情；没有返回 null。</summary>
        internal static PermanentDuckNpcStoryEvent FindPending(string npcId, PermanentDuckNpcData data, int level)
        {
            if (data == null || string.IsNullOrEmpty(npcId))
            {
                return null;
            }
            PermanentDuckNpcStoryEvent[] stories = data.StoryEvents;
            for (int i = 0; i < stories.Length; i++)
            {
                if (stories[i].level <= level && IsPending(npcId, stories[i]))
                {
                    return stories[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 开始播一段剧情。返回 false 表示没开播（已经在播，或建不出对话角色），调用方照常聊天。
        /// <paramref name="onFinished"/> 在结束时回调（参数：是否完整播完），用于收尾站桩与选项刷新。
        /// </summary>
        internal bool TryPlay(PermanentDuckNpcStoryEvent story, Transform speaker, string displayName,
            Action<bool> onFinished)
        {
            if (story == null || speaker == null || IsPlaying)
            {
                return false;
            }

            IDialogueActor actor = EnsureActor(speaker, displayName);
            if (actor == null)
            {
                return false;
            }

            _cancellation = new CancellationTokenSource();
            PlayAsync(story, actor, displayName, _cancellation, onFinished).Forget();
            return true;
        }

        /// <summary>NPC 停用 / 销毁时取消正在播的剧情；被取消的那段不打标，下次聊天重播。</summary>
        public void Dispose()
        {
            if (_cancellation != null)
            {
                _cancellation.Cancel();
            }
        }

        /// <summary>
        /// 取（或建）这只 NPC 的官方对话角色。挂在角色根节点上，与天空岛居民对话、结婚过场共用同一个组件：
        /// 已有就只刷新名字（不传立绘，保留天空岛那边设好的立绘）。
        /// </summary>
        private IDialogueActor EnsureActor(Transform speaker, string displayName)
        {
            try
            {
                DuckovDialogueActor existing = DialogueActorFactory.Get(speaker.gameObject);
                if (existing != null)
                {
                    DialogueActorFactory.RefreshPresentation(existing, displayName, displayName);
                    return existing;
                }
                return DialogueActorFactory.CreateBilingual(speaker.gameObject, _npcId, displayName, displayName,
                    new Vector3(0f, 2f, 0f));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 剧情对话角色创建失败 " + _npcId + ": " + e.Message);
                return null;
            }
        }

        private async UniTaskVoid PlayAsync(PermanentDuckNpcStoryEvent story, IDialogueActor actor, string displayName,
            CancellationTokenSource cancellation, Action<bool> onFinished)
        {
            bool played = false;
            try
            {
                string[][] lines = new string[story.lines.Length][];
                for (int i = 0; i < story.lines.Length; i++)
                {
                    lines[i] = new[] { story.lines[i].cn, story.lines[i].en ?? story.lines[i].cn };
                }

                ModBehaviour.DevLog(LogPrefix + " " + _npcId + " 开始 Lv" + story.level + " 剧情，共 " + lines.Length + " 句");
                int failuresBefore = DialogueManager.SequenceFailureSerial;
                await DialogueManager.ShowDialogueSequenceBilingual(actor, lines,
                    "BossRush_DuckNpcStory_" + _npcId + "_" + story.level, cancellation.Token);
                // 对话系统中途出错会吞掉异常正常返回：这种情况不算看过，下次聊天重播
                played = !cancellation.IsCancellationRequested
                    && DialogueManager.SequenceFailureSerial == failuresBefore;
            }
            catch (OperationCanceledException)
            {
                // 被打断（NPC 停用、切图）：不打标，下次聊天重播。
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] " + _npcId + " 剧情播放失败: " + e.Message);
                DialogueManager.ForceEndDialogue();
            }

            if (played)
            {
                CompleteStory(story, displayName);
            }

            if (_cancellation == cancellation)
            {
                _cancellation = null;
            }
            cancellation.Dispose();

            try
            {
                if (onFinished != null) onFinished(played);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 剧情收尾失败: " + e.Message);
            }
        }

        /// <summary>播完：打剧情标与「看过」标，发一次奖励；10 级首次打标时提示教堂条件。</summary>
        private void CompleteStory(PermanentDuckNpcStoryEvent story, string displayName)
        {
            try
            {
                bool newlyMarked = !AffinityManager.HasTriggeredStory(_npcId, story.level);
                AffinityManager.MarkStoryTriggered(_npcId, story.level);
                AffinityManager.MarkEventTriggered(_npcId, SeenKey(story.level));
                ModBehaviour.DevLog(LogPrefix + " " + _npcId + " Lv" + story.level + " 剧情播完并打标");

                if (newlyMarked && story.level >= 10)
                {
                    NotificationText.Push(L10n.T("婚礼教堂的建造条件已达成", "Wedding Chapel requirement met"));
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 剧情打标失败: " + e.Message);
                return;
            }

            TryGrantReward(story, displayName);
        }

        private void TryGrantReward(PermanentDuckNpcStoryEvent story, string displayName)
        {
            if (story.rewardTypeId <= 0)
            {
                return;
            }
            string rewardKey = RewardKeyPrefix + story.level;
            if (AffinityManager.HasClaimedReward(_npcId, rewardKey))
            {
                return;
            }

            try
            {
                Item reward = ItemAssetsCollection.InstantiateSync(story.rewardTypeId);
                if (reward == null)
                {
                    ModBehaviour.DevLog(LogPrefix + " [WARNING] 剧情奖励实例化失败 typeId=" + story.rewardTypeId);
                    return;
                }
                if (story.rewardCount > 1)
                {
                    int maxStack = reward.MaxStackCount > 0 ? reward.MaxStackCount : story.rewardCount;
                    reward.StackCount = Mathf.Clamp(story.rewardCount, 1, maxStack);
                }
                string itemName = reward.DisplayName;
                int count = reward.StackCount;

                // 官方 SendToPlayer：先进背包，放不下进仓库或快递缓冲。抛异常时不写已领取键，下次补发。
                ItemUtilities.SendToPlayer(reward, false, true);
                AffinityManager.MarkRewardClaimed(_npcId, rewardKey);
                NotificationText.Push(L10n.T(
                    displayName + " 送你：" + itemName + " ×" + count,
                    displayName + " gave you: " + itemName + " x" + count));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + " [WARNING] 剧情奖励发放失败: " + e.Message);
            }
        }
    }
}
