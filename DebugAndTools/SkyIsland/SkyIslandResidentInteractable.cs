using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>COMPAT：剧情选项始终位于角色子物体，保留角色移动与受击层。</summary>
    public sealed class SkyIslandResidentInteractable : BossRushBuildingInteractableBase
    {
        private string npcId;
        private string displayName;
        private Transform speaker;
        private Action<string, Transform> callback;
        private Func<bool> valid;

        protected override string InteractNameKey
        {
            get
            {
                string key = "BossRush_SkyIsland_Talk_" + (npcId ?? "Resident");
                LocalizationHelper.InjectLocalization(key,
                    L10n.T("聊聊航路 · ", "Island story · ") + (displayName ?? ""));
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
            displayName = name;
            speaker = target;
            callback = onTalk;
            valid = isValid;
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
