using System;
using System.Collections.Generic;
using System.Threading;
using BossRush.Utils;
using Cysharp.Threading.Tasks;
using Duckov.MiniMaps;
using Duckov.UI.DialogueBubbles;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 余响：剪票处台边的捏脸 NPC（蓝图 `lastshift_yuxiang`，`Assets/Data/DuckNpcs.json`，非永久、不进好感与婚姻）。
    /// 跟着剪票处一起生成与回收（scenes 为空，通用刷新不会碰她）。第七轮 owner 采纳的入口引导也在这里：
    /// 靠近时喊一声「末班车，剪票上车」（每趟一次）、仓库区小地图标出台子、每趟塞一卷台边绷带。
    /// </summary>
    internal sealed class LastShiftYuXiang
    {
        internal const string BlueprintId = "lastshift_yuxiang";
        /// <summary>台的东侧 2 m（2026-11-07 离线选址：余响 (150, 0, 245)），面朝台子；站位与朝向待实机复核。</summary>
        internal static readonly Vector3 Position = new Vector3(150f, 0f, 245f);
        private static readonly Vector3 Facing = Vector3.left;
        private const float CalloutRadius = 9f;
        private const string MapKey = "BossRush_LastShiftYard_MapDesk";
        private const string NameKey = "BossRush_Actor_lastshift_yuxiang_Name";
        private const string DialogueKey = "BossRush_LastShift_YuXiang";
        // 对话点在她胸口，和台上打孔钳的交互错开约 2.2 m。
        private static readonly Vector3 TalkLocal = new Vector3(0f, 0.62f, 2f);
        private static readonly Vector3 ActorOffset = new Vector3(0f, 1.5f, 0f);
        private int generation;
        private bool spawning;
        private float retryAt;
        private CharacterMainControl owned;
        private GameObject ownedObject;
        private DuckovDialogueActor actor;
        private Conversation conversation;
        private uint? calledRaid, bandageRaid;
        /// <summary>官方止血绷带（物品库 #10）。</summary>
        private const int BandageTypeId = 10;

        internal bool IsPresent { get { return owned != null && actor != null && owned.gameObject.activeInHierarchy; } }
        internal bool IsTalking { get { return conversation != null; } }

        internal void Spawn(GameObject entry)
        {
            if (!ReferenceEquals(ownedObject, null) && (ownedObject == null || !ownedObject.activeInHierarchy)) Despawn();
            if (spawning || IsPresent || Time.unscaledTime < retryAt) return;
            if (owned != null || conversation != null) Despawn();
            spawning = true;
            SpawnAsync(entry, ++generation).Forget();
        }

        private async UniTaskVoid SpawnAsync(GameObject entry, int request)
        {
            try
            {
                CharacterMainControl npc = await DuckNpcSpawner.SpawnAsync(BlueprintId, Position, Facing,
                    npcToAdopt => AdoptNpc(entry, request, npcToAdopt));
                if (request != generation || entry == null || !entry.activeInHierarchy || !entry.scene.isLoaded) return;
                if (npc == null || owned != npc) throw new InvalidOperationException("角色生成返回空或已失效");
                actor = DialogueActorFactory.Create(ownedObject, BlueprintId, NameKey, ActorOffset);
                if (actor == null) throw new InvalidOperationException("官方对话角色创建失败");
                Debug.Log(LastShiftRaidScene.LogTag + " YUXIANG_READY handle=" + entry.scene.handle
                    + " raid=" + RaidUtilities.CurrentRaid.ID + " at=" + npc.transform.position);
            }
            catch (Exception e)
            {
                if (request == generation)
                {
                    ReleaseOwnedNpc();
                    retryAt = Time.unscaledTime + 30f;
                    Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响没能生成，30 秒后再试：" + e.Message);
                }
            }
            finally
            {
                if (request == generation) spawning = false;
            }
        }

        private bool AdoptNpc(GameObject entry, int request, CharacterMainControl npc)
        {
            if (request != generation || entry == null || !entry.activeInHierarchy || !entry.scene.isLoaded) return false;
            // 配装还会异步等待。先归入口，卸载时也能清掉这只尚未配完装备的鸭。
            GameObject npcObject = npc.gameObject;
            npc.transform.SetParent(entry.transform, true);
            owned = npc;
            ownedObject = npcObject;
            return true;
        }

        internal void Despawn()
        {
            generation++;
            spawning = false;
            retryAt = 0f;
            Conversation closing = conversation;
            conversation = null;
            if (closing != null) closing.Cancel();
            ReleaseOwnedNpc();
        }

        private void ReleaseOwnedNpc()
        {
            GameObject npcObject = ownedObject;
            CharacterMainControl npc = owned;
            ownedObject = null;
            owned = null;
            actor = null;
            if (ReferenceEquals(npcObject, null)) return;
            try
            {
                DialogueActorFactory.Remove(npcObject);
                if (npcObject != null) npcObject.SetActive(false);
                DuckNpcSpawner.Despawn(npc);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响回收失败：" + e.Message); }
        }

        /// <summary>模块每秒调：玩家走近台子时喊一声，每趟出击一次。</summary>
        internal void Tick(CharacterMainControl player)
        {
            if (conversation != null && !conversation.IsCurrent) conversation.Cancel();
            if (!IsPresent || IsTalking || player == null) return;
            uint raid = RaidUtilities.CurrentRaid.ID;
            if (raid == calledRaid) return;
            Vector3 offset = player.transform.position - owned.transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > CalloutRadius * CalloutRadius) return;
            calledRaid = raid;
            try { DialogueBubblesManager.Show(LastShiftText.YuXiangCallout, owned.transform, 3f, false, false, -1f, 1.2f).Forget(); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响喊话失败：" + e.Message); }
        }

        internal void AddTalk(Transform root, LastShiftEntryInteractable entry)
        {
            LastShiftDeviceInteractable talk = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<LastShiftDeviceInteractable>(root, "LastShiftTalk", TalkLocal, x =>
            {
                x.Bind("yuxiang_talk", 0f, delegate { return LastShiftText.YuXiangTalkVerb; },
                    delegate { return IsPresent && !IsTalking && entry.CanTalk(); }, delegate { Talk(entry, false); });
                SphereCollider sensor = x.GetComponent<SphereCollider>();
                if (sensor != null) { sensor.radius = 1f; sensor.isTrigger = true; }
            });
            if (talk == null) throw new InvalidOperationException("余响对话点创建失败");
        }

        internal void Talk(LastShiftEntryInteractable entry, bool boarding)
        {
            if (!IsPresent || IsTalking || !entry.CanTalk()) return;
            if (Dialogues.DialogueUI.instance == null)
            {
                Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响对话不可用：官方 DialogueUI 未就绪");
                return;
            }
            if (Dialogues.DialogueUI.Active || DialogueManager.IsDialogueActive) return;
            var request = new Conversation(actor, entry, CharacterMainControl.Main);
            conversation = request;
            TalkAsync(request, boarding).Forget();
        }

        private async UniTaskVoid TalkAsync(Conversation request, bool boarding)
        {
            try
            {
                request.Attach();
                if (boarding) await Board(request);
                else await Converse(request);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响对话失败：" + e.Message); }
            finally
            {
                if (ReferenceEquals(conversation, request)) conversation = null;
                request.Dispose();
            }
        }

        private async UniTask Board(Conversation request)
        {
            CharacterMainControl player = request.Player;
            var lines = new List<string[]>(LastShiftText.YuXiangBoarding);
            if (!LastShiftStory.Journal && TidegateInventory.Carries(player, BossRushItemIds.LastShiftTicketStub))
                lines.Add(LastShiftText.YuXiangBoardingStub);
            if (TidegateInventory.Carries(player, BossRushItemIds.LastShiftStamp))
                lines.Add(LastShiftText.YuXiangBoardingStamp);
            lines.Add(LastShiftText.YuXiangBandage);
            if (!await request.Show(lines.ToArray(), "_Boarding")) return;
            if (!await request.Choose(LastShiftText.YuXiangBoardingChoices, "_BoardingChoice") || !request.Entry.CanBoard()) return;

            LastShiftStory.MarkMet();
            if (LastShiftStory.TryHandIn(player) && !await request.Ending()) return;
            if (request.IsCurrent) request.Entry.Depart(player);
        }

        /// <summary>先回应新线索，读过回应再标记。票根由官方分支明确选择后交给她。</summary>
        private async UniTask Converse(Conversation request)
        {
            LastShiftStoryData d = LastShiftStory.Current;
            if (d == null) return;
            bool firstTime = !d.Met;
            LastShiftStory.MarkMet();
            int fresh = d.Journal ? 0 : d.Notes & ~d.Told & LastShiftStory.AllNotes;
            var lines = new List<string[]>();
            for (int i = 0; i < LastShiftStory.NoteCount; i++)
                if ((fresh & (1 << i)) != 0) lines.Add(LastShiftText.YuXiangReaction(i));

            bool canHandIn = !d.Journal && TidegateInventory.Carries(request.Player, BossRushItemIds.LastShiftTicketStub);
            if (canHandIn) lines.Add(LastShiftText.YuXiangSeesStub);
            else if (d.Journal) lines.Add(LastShiftText.YuXiangAfter(d.Runs));
            else if (d.Notes == 0) lines.AddRange(LastShiftText.YuXiangIntro(firstTime, d.Runs > 0));
            else
            {
                int missing = 0;
                while (missing < LastShiftStory.NoteCount && (d.Notes & (1 << missing)) != 0) missing++;
                lines.Add(LastShiftText.YuXiangHint(missing));
            }

            if (!await request.Show(lines.ToArray(), "_Talk")) return;
            LastShiftStory.MarkTold(fresh);
            if (!canHandIn || !await request.Choose(LastShiftText.YuXiangHandInChoices, "_HandInChoice")) return;
            if (LastShiftStory.TryHandIn(request.Player)) await request.Ending();
            else await request.Show(LastShiftText.YuXiangHandInFailure, "_HandInFailure");
        }

        private sealed class Conversation : IDisposable
        {
            private readonly DuckovDialogueActor actor;
            private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
            private bool subscribed, cancelledByPlayer;
            internal readonly LastShiftEntryInteractable Entry;
            internal readonly CharacterMainControl Player;

            internal Conversation(DuckovDialogueActor speaker, LastShiftEntryInteractable entry, CharacterMainControl player)
            {
                actor = speaker;
                Entry = entry;
                Player = player;
            }

            internal bool IsCurrent
            {
                get
                {
                    return actor != null && actor.isActiveAndEnabled && Entry != null && Entry.CanTalk()
                        && Player != null && Player == CharacterMainControl.Main;
                }
            }

            internal void Attach()
            {
                if (subscribed) return;
                UIInputManager.OnCancelEarly += OnCancel;
                subscribed = true;
            }

            internal async UniTask<bool> Show(string[][] lines, string key)
            {
                if (!IsCurrent || cancellation.IsCancellationRequested) return false;
                int failures = DialogueManager.SequenceFailureSerial;
                await DialogueManager.ShowDialogueSequenceBilingual(actor, lines, DialogueKey + key, cancellation.Token);
                return IsCurrent && !cancellation.IsCancellationRequested && failures == DialogueManager.SequenceFailureSerial;
            }

            internal async UniTask<bool> Choose(string[][] choices, string key)
            {
                if (!IsCurrent || cancellation.IsCancellationRequested) return false;
                int choice = await DialogueManager.ShowMultipleChoiceBilingual(actor, choices, 0f, DialogueKey + key, cancellation.Token);
                return choice == 0 && IsCurrent && !cancellation.IsCancellationRequested;
            }

            internal async UniTask<bool> Ending()
            {
                try { if (await Show(LastShiftText.YuXiangEnding, "_Ending")) return true; }
                catch (OperationCanceledException) when (cancelledByPlayer) { }
                // 只跳过已交票后的收尾。先前明确选了上车才会继续发车。
                return cancelledByPlayer && IsCurrent;
            }

            private void OnCancel(UIInputEventData data)
            {
                if (data == null || data.Used || !IsCurrent || !DialogueManager.IsDialogueActiveFor(actor)) return;
                data.Use();
                cancelledByPlayer = true;
                cancellation.Cancel();
            }

            internal void Cancel()
            {
                Detach();
                cancelledByPlayer = false;
                cancellation.Cancel();
            }

            private void Detach()
            {
                if (!subscribed) return;
                UIInputManager.OnCancelEarly -= OnCancel;
                subscribed = false;
            }

            public void Dispose()
            {
                Detach();
                cancellation.Dispose();
            }
        }

        internal static void InjectLocalization()
        {
            LocalizationHelper.InjectLocalization(NameKey, LastShiftText.YuXiangName);
            LocalizationHelper.InjectLocalization(MapKey, L10n.T("末班车剪票处", "Last train ticket desk"));
        }

        internal static void RefreshMapMarker(GameObject entry)
        {
            if (entry == null || !entry.activeInHierarchy) return;
            SimplePointOfInterest poi = entry.GetComponentInChildren<SimplePointOfInterest>();
            if (poi != null) SetupMapMarker(poi);
        }

        /// <summary>每趟都在仓库区地图标出台子。根节点激活前加、激活后 Setup。</summary>
        internal static SimplePointOfInterest AddMapMarker(Transform root)
        {
            var marker = new GameObject("LastShiftMapMarker");
            marker.transform.SetParent(root, false);
            SimplePointOfInterest poi = marker.AddComponent<SimplePointOfInterest>();
            poi.Color = BossRushUIColors.WarningText;
            poi.ScaleFactor = 1.25f;
            poi.IsArea = false;
            return poi;
        }

        internal static void SetupMapMarker(SimplePointOfInterest poi)
        {
            poi.Setup(null, MapKey);
        }

        /// <summary>台边绷带箱：每趟出击一卷，剪票时塞给你（第七轮：带伤上车回不去，台边先给一卷）。返回给没给成。</summary>
        internal bool GiveBandage(CharacterMainControl player)
        {
            uint raid = RaidUtilities.CurrentRaid.ID;
            if (player == null || raid == bandageRaid) return false;
            Item item = null;
            try
            {
                item = ItemAssetsCollection.InstantiateSync(BandageTypeId);
                if (item == null) return false;
                ItemUtilities.SendToPlayer(item, false, false);
            }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 绷带投递通知：" + e.Message); }
            if (item == null) return false;
            if (item.InInventory == null && item.PluggedIntoSlot == null)
            {
                try { item.Drop(player, true); }
                catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 绷带落地失败：" + e.Message); return false; }
            }
            bandageRaid = raid;
            return true;
        }

    }
}
