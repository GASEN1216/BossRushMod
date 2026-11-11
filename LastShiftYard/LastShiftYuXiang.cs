using System;
using BossRush.Utils;
using Cysharp.Threading.Tasks;
using Duckov.MiniMaps;
using Duckov.UI.DialogueBubbles;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 余响：剪票处台边的捏脸 NPC（蓝图 `lastshift_yuxiang`，`Assets/Data/DuckNpcs.json`，非永久、不进好感与婚姻）。
    /// 跟着剪票处一起生成与回收（scenes 为空，通用刷新不会碰她）。第七轮 owner 采纳的入口引导也在这里：
    /// 靠近时喊一声「末班车，剪票上车」（每趟一次）、没去过编组站时仓库区小地图标出台子、每趟塞一卷台边绷带。
    /// </summary>
    internal static class LastShiftYuXiang
    {
        internal const string BlueprintId = "lastshift_yuxiang";
        /// <summary>台的东侧 2 m（2026-11-07 离线选址：余响 (150, 0, 245)），面朝台子；站位与朝向待实机复核。</summary>
        internal static readonly Vector3 Position = new Vector3(150f, 0f, 245f);
        private static readonly Vector3 Facing = Vector3.left;
        private const float CalloutRadius = 9f;
        private const string MapKey = "BossRush_LastShiftYard_MapDesk";
        private static int generation;
        private static bool spawning;
        private static float retryAt;
        private static CharacterMainControl owned;
        private static uint calledRaid, bandageRaid;
        /// <summary>官方止血绷带（物品库 #10，2026-11-11 按 escapefromduckov.net 核对）。</summary>
        private const int BandageTypeId = 10;

        internal static void Spawn(Scene scene)
        {
            if (spawning || owned != null || Time.unscaledTime < retryAt) return;
            int request = ++generation;
            SpawnAsync(scene, request).Forget();
        }

        private static async UniTaskVoid SpawnAsync(Scene scene, int request)
        {
            spawning = true;
            try
            {
                CharacterMainControl npc = await DuckNpcSpawner.SpawnAsync(BlueprintId, Position, Facing);
                if (npc == null) { retryAt = Time.unscaledTime + 30f; return; }
                owned = npc;
                if (request != generation || !scene.IsValid() || !scene.isLoaded) { Despawn(); return; }
                // 跟着仓库区子场景一起卸载（口径同潮闸镇闸伯）。
                if (npc.transform.parent == null && npc.gameObject.scene.handle != scene.handle)
                    SceneManager.MoveGameObjectToScene(npc.gameObject, scene);
                Debug.Log(LastShiftRaidScene.LogTag + " YUXIANG_READY at=" + npc.transform.position);
            }
            catch (Exception e) { retryAt = Time.unscaledTime + 30f; Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响没能生成，30 秒后再试：" + e.Message); }
            finally { spawning = false; }
        }

        internal static void Despawn()
        {
            generation++;
            CharacterMainControl npc = owned;
            owned = null;
            if (npc == null) return;
            try { DuckNpcSpawner.Despawn(npc); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响回收失败：" + e.Message); }
        }

        /// <summary>模块每秒调：玩家走近台子时喊一声，每趟出击一次。</summary>
        internal static void Tick(CharacterMainControl player)
        {
            if (owned == null || player == null) return;
            uint raid = RaidUtilities.CurrentRaid.ID;
            if (raid == calledRaid) return;
            Vector3 offset = player.transform.position - owned.transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude > CalloutRadius * CalloutRadius) return;
            calledRaid = raid;
            try { DialogueBubblesManager.Show(LastShiftText.YuXiangCallout, owned.transform, 3f, false, false, -1f, 1.2f).Forget(); }
            catch (Exception e) { Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响喊话失败：" + e.Message); }
        }

        /// <summary>台边「和余响说话」的交互点：站在她面前（剪票交互在台子正面，两处错开约 1.4 m）。</summary>
        private static readonly Vector3 TalkLocal = new Vector3(0f, 1f, 2.3f);
        private static ModBehaviour talkOwner;

        /// <summary>剪票处搭好时挂上对话点（owner 2026-11-11：故事的主角得能说上话，票根在这里就能交）。</summary>
        internal static void AddTalk(Transform root, ModBehaviour owner)
        {
            talkOwner = owner;
            LastShiftDeviceInteractable talk = NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<LastShiftDeviceInteractable>(root, "LastShiftTalk", TalkLocal, x =>
            {
                x.Bind("yuxiang_talk", 0f, delegate { return LastShiftText.YuXiangTalkVerb; }, CanTalk, Talk);
                SphereCollider sensor = x.GetComponent<SphereCollider>();
                if (sensor != null) { sensor.radius = 1f; sensor.isTrigger = true; }
            });
            if (talk == null) Debug.LogWarning(LastShiftRaidScene.LogTag + " 余响对话点创建失败");
        }

        private static bool CanTalk()
        {
            return talkOwner != null && talkOwner.GetComponent<LastShiftSession>() == null;
        }

        /// <summary>按进度说话：带着票根、故事没收尾就请你交给她；收尾后说现在的事；否则提第一条还没带出去的线索。</summary>
        private static void Talk()
        {
            LastShiftStoryData d = LastShiftStory.Current;
            if (d == null) return;
            bool firstTime = !d.Met;
            LastShiftStory.MarkMet();
            CharacterMainControl player = CharacterMainControl.Main;
            // 新带出来、她还没接过话的线索：先说她的反应，再往下说（每条只说一次）。
            int fresh = d.Journal ? 0 : d.Notes & ~d.Told & LastShiftStory.AllNotes;
            string reactions = "";
            for (int i = 0; i < LastShiftStory.NoteCount; i++)
                if ((fresh & (1 << i)) != 0) reactions += LastShiftText.YuXiangReaction(i) + "\n\n";
            LastShiftStory.MarkTold(fresh);
            if (!d.Journal && TidegateInventory.Carries(player, BossRushItemIds.LastShiftTicketStub))
            {
                BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
                {
                    Title = LastShiftText.YuXiangName,
                    Body = reactions + LastShiftText.YuXiangSeesStub,
                    ConfirmLabel = L10n.T("把票根给她", "Give her the stub"),
                    CancelLabel = L10n.T("先留着", "Not yet"),
                    Danger = false,
                    OnConfirm = HandOver,
                });
                return;
            }
            string body;
            if (d.Journal) body = LastShiftText.YuXiangAfter(d.Runs);
            else
            {
                int missing = 0;
                while (missing < LastShiftStory.NoteCount && (d.Notes & (1 << missing)) != 0) missing++;
                // 第一次只聊站和末班车，不报线索在哪（owner 2026-11-11）；带出第一条线索后她才往下说，线索位置在编组站的地图上。
                body = d.Notes == 0 ? LastShiftText.YuXiangIntro(firstTime, d.Runs > 0) : reactions + LastShiftText.YuXiangHint(missing);
            }
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = LastShiftText.YuXiangName,
                Body = body,
                SingleAction = true,
                ConfirmLabel = L10n.T("嗯", "Right"),
                Danger = false,
                OnConfirm = delegate { },
            });
        }

        /// <summary>对话里交票根：和上车时交同一个入口（先写存档再拿走票根），交成了弹收尾页。</summary>
        private static void HandOver()
        {
            if (LastShiftStory.TryHandIn(CharacterMainControl.Main))
            {
                LastShiftStory.ShowEnding(L10n.T("收好", "Done"), null);
                return;
            }
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = LastShiftText.YuXiangName,
                Body = L10n.T("她没接，摆摆手：「下回再说。」\n（存档暂时写不进去，票根还在你身上。）", "She doesn't take it, just waves a hand. \"Next time.\"\n(The save can't be written right now. You still have the stub.)"),
                SingleAction = true,
                ConfirmLabel = L10n.T("好", "OK"),
                Danger = false,
                OnConfirm = delegate { },
            });
        }

        /// <summary>没去过编组站时在仓库区小地图标出台子（第七轮：「第一次进仓库区时在小地图标出余响的台子」）。根节点激活前加、激活后 Setup。</summary>
        internal static GameObject AddMapMarker(Transform root)
        {
            if (LastShiftStory.Runs > 0) return null;
            LocalizationHelper.InjectLocalization(MapKey, L10n.T("末班车剪票处", "Last train ticket desk"));
            var marker = new GameObject("LastShiftMapMarker");
            marker.transform.SetParent(root, false);
            SimplePointOfInterest poi = marker.AddComponent<SimplePointOfInterest>();
            poi.Color = BossRushUIColors.WarningText;
            poi.ScaleFactor = 1.25f;
            poi.IsArea = false;
            return marker;
        }

        internal static void SetupMapMarker(GameObject marker)
        {
            if (marker == null) return;
            SimplePointOfInterest poi = marker.GetComponent<SimplePointOfInterest>();
            if (poi != null) poi.Setup(null, MapKey);
        }

        /// <summary>台边绷带箱：每趟出击一卷，剪票时塞给你（第七轮：带伤上车回不去，台边先给一卷）。返回给没给成。</summary>
        internal static bool GiveBandage(CharacterMainControl player)
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



        internal static void ResetStaticCaches()
        {
            Despawn();
            spawning = false;
            retryAt = 0f;
            calledRaid = bandageRaid = 0;
            talkOwner = null;
        }
    }
}
