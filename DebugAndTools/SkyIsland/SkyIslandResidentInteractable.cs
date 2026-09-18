using System;
using BossRush.Utils;
using Duckov.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>COMPAT：剧情选项始终位于角色子物体，保留角色移动与受击层。</summary>
    public sealed class SkyIslandResidentInteractable : BossRushBuildingInteractableBase
    {
        private string npcId;
        private Transform speaker;
        private Action<string, Transform> callback;
        private Func<bool> valid;
        private SkyIslandResidentDialogue homeDialogue;

        /// <summary>居民 id（sky_*），按 id 找人不受换语言影响。</summary>
        internal string NpcId { get { return npcId; } }

        protected override string InteractNameKey
        {
            get
            {
                string key = "BossRush_SkyIsland_Talk_" + (npcId ?? "Resident");
                LocalizationHelper.InjectLocalization(key,
                    L10n.T("聊聊航路 · ", "Talk about the lanes · ") + SkyIslandWorldStory.ResidentName(npcId));
                return key;
            }
        }
        protected override string LogPrefix { get { return "[SkyIslandResidents] "; } }
        protected override string InteractionGroupLabel { get { return "[SkyIslandResidents]"; } }
        protected override bool IsBuildingInteractable()
        {
            return speaker != null && callback != null && valid != null && valid();
        }
        internal void Bind(string id, string name, Transform target,
            Action<string, Transform> onTalk, Func<bool> isValid)
        {
            npcId = id;
            speaker = target;
            callback = onTalk;
            valid = isValid;
        }

        internal static void InjectLocalizations()
        {
            foreach (string id in SkyIslandResidents.AllIds)
                LocalizationHelper.InjectLocalization("BossRush_SkyIsland_Talk_" + id,
                    L10n.T("聊聊航路 · ", "Talk about the lanes · ") + SkyIslandWorldStory.ResidentName(id));
        }

        /// <summary>普通生成、婚后恢复共用。回调取当前会话，不把岛上旧 owner 带到基地。</summary>
        internal static void AttachPermanent(CharacterMainControl npc, string id)
        {
            if (npc == null || (id != "sky_qinghe" && id != "sky_weibai")) return;
            PermanentDuckNpcInteractable owner = npc.GetComponentInChildren<PermanentDuckNpcInteractable>(true);
            if (owner == null || owner.transform.Find("IslandStoryOption") != null) return;
            var group = NPCInteractionGroupHelper.GetOrCreateGroupList(owner, "[SkyIslandResidents]");
            NPCInteractionGroupHelper.AddSubInteractable(owner.transform, "IslandStoryOption", group,
                (SkyIslandResidentInteractable component) => component.Bind(id,
                    SkyIslandWorldStory.ResidentName(id), npc.transform, component.TalkPermanent, component.CanTalkPermanent));
            // 配偶可能晚于委托板就绪。岛上恢复时仍给本人同一个任务 ID，不撤掉已经可用的板。
            bool onIsland;
            SkyIslandOfficialQuestStory.Resolve(ModBehaviour.Instance, out onIsland);
            if (onIsland) SkyIslandOfficialQuestGivers.AttachResident(owner.transform, group, id);
        }

        private bool CanTalkPermanent()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (speaker == null || player == null || player.Health == null || player.Health.IsDead ||
                SceneLoader.IsSceneLoading || speaker.gameObject.scene != player.gameObject.scene) return false;
            bool onIsland;
            SkyIslandStoryService story = SkyIslandOfficialQuestStory.Resolve(ModBehaviour.Instance, out onIsland);
            return story != null && story.IsCurrentSlot && (onIsland ||
                (AffinityManager.IsMarriedToPlayer(npcId) &&
                 SceneRuntimeGate.IsBaseHubSceneName(SceneManager.GetActiveScene().name)));
        }

        private void TalkPermanent(string id, Transform target)
        {
            if (!CanTalkPermanent()) return;
            ModBehaviour host = ModBehaviour.Instance;
            bool onIsland;
            SkyIslandStoryService story = SkyIslandOfficialQuestStory.Resolve(host, out onIsland);
            if (onIsland)
            {
                SkyIslandSession session = host == null ? null : host.GetComponent<SkyIslandSession>();
                if (session != null) session.TalkToResident(id, target);
                return;
            }
            if (homeDialogue != null && homeDialogue.Active) return;
            int scene = SceneManager.GetActiveScene().handle;
            CharacterMainControl player = CharacterMainControl.Main;
            // 基地只聊进度；接交任务、用餐、合成都由岛上现有入口执行。
            homeDialogue = SkyIslandResidentDialogue.Run(id, target, story.DescribeNpc(id, true, false),
                delegate { }, () => this != null && isActiveAndEnabled && story.IsCurrentSlot &&
                    scene == SceneManager.GetActiveScene().handle && player == CharacterMainControl.Main && CanTalkPermanent(),
                () => false);
        }

        private void Update()
        {
            if (homeDialogue != null && homeDialogue.Active && !homeDialogue.CanContinue()) homeDialogue.Dispose();
        }

        private void OnDisable() { if (homeDialogue != null) homeDialogue.Dispose(); }

        protected override void OnDestroy()
        {
            if (homeDialogue != null) homeDialogue.Dispose();
            base.OnDestroy();
        }
        protected override void OnInteractCompleted()
        {
            if (!IsBuildingInteractable()) return;
            DuckNpcRuntimeMarker marker = speaker.GetComponent<DuckNpcRuntimeMarker>();
            if (marker != null)
            {
                marker.FacePlayer();
                marker.StartDialogue();
                marker.EndDialogueWithStay(8f);
            }
            callback(npcId, speaker);
        }
    }
}
