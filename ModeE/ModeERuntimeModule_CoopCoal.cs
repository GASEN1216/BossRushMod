using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopModeECoalState
    {
        public string Entity, Owner;
    }
    [Serializable]
    internal sealed class CoopModeECoalRequest
    {
        public string Id, Entity, AssetId, Run;
        public int Scene, Session;
        public bool Hire, ModeF;
    }

    internal sealed partial class ModeERuntimeModule
    {
        private static ModeERuntimeModule coopCoalOwner;
        private readonly Dictionary<CharacterMainControl, CharacterMainControl> coopCoal = new Dictionary<CharacterMainControl, CharacterMainControl>();
        private readonly Dictionary<string, CoopModeECoalRequest> coopCoalRequests = new Dictionary<string, CoopModeECoalRequest>();
        private readonly Dictionary<string, CoopModeEPersonal> coopCoalResults = new Dictionary<string, CoopModeEPersonal>();
        private string coopCoalRun;
        private int coopCoalScene = -1;
        private float coopCoalRetry;

        internal static bool RequestMerchantCoal()
        {
            if (!BossRushCoop.IsClient) return false;
            if (coopCoalOwner == null || !BossRushCoop.SessionReady) return true;
            coopCoalOwner.RequestCoopCoal(false, null);
            return true;
        }
        internal static void TrackMerchantCoal(CharacterMainControl character)
        {
            if (coopCoalOwner == null || character == null) return;
            coopCoalOwner.RefreshCoopCoalSession();
            coopCoalOwner.coopCoal[character] = null;
            AttachCoopCoalOffer(character);
        }
        private static void AttachCoopCoalOffer(CharacterMainControl character)
        {
            // 原煤球的序列化 required-item 回调在本机消费后会直接把 Main 当主人。
            // 联机改由同一个 AI 的独立交互点提交购买者事务，原组件不再拥有裁决权。
            foreach (InteractableBase interactable in character.GetComponentsInChildren<InteractableBase>(true))
                if (!(interactable is ModeECoopCoalHireInteractable) && interactable.requireItemId == 388) interactable.enabled = false;
            PetAI pet = character.GetComponentInChildren<PetAI>(true);
            if (pet != null) { DetachCoopCoalMaster(pet); pet.setMainCharacterAsMaster = false; pet.enabled = false; }
            BossRush.Utils.NPCInteractionGroupHelper.GetOrCreateStandaloneInteractable<ModeECoopCoalHireInteractable>(
                character.transform, "ModeE_CoopCoalHire", Vector3.up, component => component.Setup(character));
        }
        private static void DetachCoopCoalMaster(PetAI pet)
        {
            if (pet.master == null) return;
            // 原 SetMaster 只退订新主人，换主人前必须移除旧主人的具名回调。
            MethodInfo method = typeof(PetAI).GetMethod("OnMainCharacterSetPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method != null)
            {
                var handler = (Action<CharacterMainControl, Vector3>)Delegate.CreateDelegate(typeof(Action<CharacterMainControl, Vector3>), pet, method);
                pet.master.OnSetPositionEvent -= handler;
            }
            pet.master = null;
        }
        internal static bool HireMerchantCoal(CharacterMainControl character)
        {
            ModeERuntimeModule runtime = coopCoalOwner;
            CharacterMainControl player = CharacterMainControl.Main;
            if (runtime == null || character == null || player == null || !runtime.coopCoal.ContainsKey(character)
                || runtime.coopCoal[character] != null || (character.transform.position - player.transform.position).sqrMagnitude > 36f) return false;
            if (!ItemFactory.ConsumeItem(388, 1)) return false;
            if (BossRushCoop.IsClient) { runtime.RequestCoopCoal(true, character); return true; }
            bool success = runtime.BindCoopCoalMaster(character, player);
            if (!success) runtime.modeEHost.TryGiveItemToPlayerOrDrop(388, L10n.T("煤球雇佣物品", "Coalball hire item"), false);
            return success;
        }
        private void RequestCoopCoal(bool hire, CharacterMainControl character)
        {
            RefreshCoopCoalSession();
            var request = new CoopModeECoalRequest { Id = Guid.NewGuid().ToString("N"), Hire = hire,
                Entity = BossRushCoop.GetEntityId(character), AssetId = BossRushCoop.LocalAssetId,
                Run = BossRushCoop.RunId, Scene = BossRushCoop.SceneGeneration,
                ModeF = modeEHost.IsModeFActive, Session = modeEHost.IsModeFActive ? modeEHost.CurrentModeFSessionToken : CurrentModeESessionToken };
            coopCoalRequests[request.Id] = request;
            coopChannel.Request("coal", text: BossRushCoopJson.ToJson(request));
        }
        private void ExecuteCoopCoal(CoopGameplayCommand command)
        {
            CoopModeECoalRequest request = string.IsNullOrEmpty(command.Text) ? null : BossRushCoopJson.FromJson<CoopModeECoalRequest>(command.Text);
            Guid id;
            if (request == null || request.AssetId != command.AssetId || !Guid.TryParseExact(request.Id, "N", out id)) return;
            string key = command.PlayerId + ":" + request.Id;
            CoopModeEPersonal result;
            if (coopCoalResults.TryGetValue(key, out result))
            { coopPersonal.Deliver(command.PlayerId, "coal:" + request.Id, result, request.AssetId); return; }
            result = new CoopModeEPersonal { CoalId = request.Id };
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(command.PlayerId);
            if (request.Run == BossRushCoop.RunId && request.Scene == BossRushCoop.SceneGeneration
                && (request.ModeF ? modeEHost.IsModeFActive && request.Session == modeEHost.CurrentModeFSessionToken
                    : modeEActive && request.Session == CurrentModeESessionToken && IsCoopModeEParticipant(player)) && player != null && player.IsAlive)
            {
                if (!request.Hire)
                {
                    CharacterMainControl merchant = BossRushCoop.FindEntity(merchantRuntime.CoopEntity);
                    if (merchant != null && (merchant.transform.position - player.Character.transform.position).sqrMagnitude <= 36f)
                    { ModeEPetSpawner.SpawnForPlayer(player.Character); result.CoalSucceeded = true; }
                }
                else
                {
                    CharacterMainControl character = BossRushCoop.FindEntity(request.Entity);
                    CharacterMainControl master;
                    if (character != null && character.Health != null && !character.Health.IsDead && coopCoal.TryGetValue(character, out master)
                        && master == null && (character.transform.position - player.Character.transform.position).sqrMagnitude <= 36f)
                        result.CoalSucceeded = BindCoopCoalMaster(character, player.Character);
                }
            }
            coopCoalResults[key] = result;
            coopPersonal.Deliver(command.PlayerId, "coal:" + request.Id, result, request.AssetId);
        }
        private bool BindCoopCoalMaster(CharacterMainControl character, CharacterMainControl master)
        {
            try
            {
                character.SetTeam(master.Team);
                PetAI pet = character.GetComponentInChildren<PetAI>(true);
                if (pet != null)
                {
                    DetachCoopCoalMaster(pet);
                    pet.setMainCharacterAsMaster = false; pet.SetMaster(master); pet.standBy = false; pet.enabled = true;
                }
                else
                {
                    FrostmourneZombieFollower follower = character.GetComponent<FrostmourneZombieFollower>();
                    if (follower == null) follower = character.gameObject.AddComponent<FrostmourneZombieFollower>();
                    follower.InitializeForModeE(master, 0, 1);
                }
                coopCoal[character] = master;
                var offer = character.GetComponentInChildren<ModeECoopCoalHireInteractable>(true);
                if (offer != null) offer.gameObject.SetActive(false);
                return true;
            }
            catch (Exception e) { ModBehaviour.DevLog("[ModeE/CoopCoal] " + e.Message); return false; }
        }
        private bool ApplyCoopCoalReceipt(CoopModeEPersonal receipt)
        {
            CoopModeECoalRequest request;
            if (!coopCoalRequests.TryGetValue(receipt.CoalId, out request)) return true;
            if (request.AssetId != BossRushCoop.LocalAssetId) return false;
            if (request.Hire && !receipt.CoalSucceeded
                && !modeEHost.TryGiveItemToPlayerOrDrop(388, L10n.T("煤球雇佣物品", "Coalball hire item"), false)) return false;
            coopCoalRequests.Remove(receipt.CoalId);
            return true;
        }
        private CoopModeECoalState[] CaptureCoopCoal()
        {
            var states = new List<CoopModeECoalState>();
            foreach (var pair in coopCoal)
                if (pair.Key != null && pair.Key.Health != null && !pair.Key.Health.IsDead)
                {
                    string entity = BossRushCoop.GetEntityId(pair.Key);
                    if (!string.IsNullOrEmpty(entity)) states.Add(new CoopModeECoalState { Entity = entity, Owner = BossRushCoop.GetPlayerId(pair.Value) });
                }
            return states.ToArray();
        }
        private void ApplyCoopCoal(CoopModeECoalState[] states)
        {
            if (!BossRushCoop.IsClient || states == null) return;
            foreach (CoopModeECoalState state in states)
            {
                CharacterMainControl character = state != null ? BossRushCoop.FindEntity(state.Entity) : null;
                if (character == null) continue;
                if (!coopCoal.ContainsKey(character)) TrackMerchantCoal(character);
                BossRushCoopPlayer master = BossRushCoop.FindPlayer(state.Owner);
                coopCoal[character] = master != null ? master.Character : null;
                var offer = character.GetComponentInChildren<ModeECoopCoalHireInteractable>(true);
                if (offer != null) offer.gameObject.SetActive(string.IsNullOrEmpty(state.Owner));
            }
        }
        private void TickCoopCoal(float deltaTime)
        {
            RefreshCoopCoalSession();
            coopCoalRetry -= Mathf.Max(0f, deltaTime);
            if (coopCoalRetry > 0f) return;
            coopCoalRetry = 1f;
            RefundCancelledCoopCoal();
            if (!BossRushCoop.IsClient) return;
            foreach (CoopModeECoalRequest request in coopCoalRequests.Values)
                if (request.AssetId == BossRushCoop.LocalAssetId && request.Run == BossRushCoop.RunId)
                    coopChannel.RequestRepeat("coal", text: BossRushCoopJson.ToJson(request));
        }
        private void RefreshCoopCoalSession()
        {
            if (coopCoalRun == BossRushCoop.RunId && coopCoalScene == BossRushCoop.SceneGeneration) return;
            CleanupCoopUtilities();
            CleanupCoopCoal(); coopCoalRun = BossRushCoop.RunId; coopCoalScene = BossRushCoop.SceneGeneration;
        }
        private void CleanupCoopCoal()
        {
            RefundCancelledCoopCoal();
            if (coopCoalRun != BossRushCoop.RunId) coopCoalResults.Clear();
            coopCoal.Clear(); coopCoalRetry = 0f;
        }

        private void RefundCancelledCoopCoal()
        {
            var completed = new List<string>();
            foreach (CoopModeECoalRequest request in coopCoalRequests.Values)
            {
                if ((BossRushCoop.IsConnected && request.Run == BossRushCoop.RunId) || request.AssetId != BossRushCoop.LocalAssetId) continue;
                if (request.Hire && !modeEHost.TryGiveItemToPlayerOrDrop(388, L10n.T("煤球雇佣物品", "Coalball hire item"), false)) continue;
                completed.Add(request.Id);
            }
            foreach (string id in completed) coopCoalRequests.Remove(id);
        }
    }

    public sealed class ModeECoopCoalHireInteractable : InteractableBase
    {
        private CharacterMainControl character;
        internal void Setup(CharacterMainControl value)
        {
            character = value; requireItem = false; overrideInteractName = true;
            _overrideInteractNameKey = "BossRush_ModeE_CoopCoalHire";
            LocalizationHelper.InjectLocalization(_overrideInteractNameKey, L10n.T("雇佣煤球", "Hire Coalball"));
            var sphere = GetComponent<SphereCollider>();
            if (sphere != null) { sphere.isTrigger = true; sphere.radius = 2.2f; }
        }
        protected override bool IsInteractable()
        {
            LocalizationHelper.InjectLocalization(_overrideInteractNameKey, L10n.T("雇佣煤球", "Hire Coalball"));
            return character != null && ItemFactory.GetItemCountInInventory(388) > 0;
        }
        protected override void OnTimeOut() { ModeERuntimeModule.HireMerchantCoal(character); }
    }
}
