using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class BossRushCoopItemVariable
    {
        public string key;
        public int type;
        public string raw;
        public bool display;
    }

    [Serializable]
    internal sealed class BossRushCoopItemState
    {
        public int schema = 1;
        public int typeId;
        public List<BossRushCoopItemVariable> variables = new List<BossRushCoopItemVariable>();
    }

    /// <summary>物品网络快照的完整 RF_/AFX_ 投影；只有接收方刷新运行时，零存档写入。</summary>
    internal sealed class BossRushCoopContentRuntimeModule : BossRushRuntimeModuleBase
    {
        internal const string SnapshotKey = "BossRush.Content.v1";
        private readonly Dictionary<Item, string> applied = new Dictionary<Item, string>();
        private readonly HashSet<CharacterMainControl> observed = new HashSet<CharacterMainControl>();
        private bool subscribed;
        private static BossRushCoopContentRuntimeModule instance;
        private IDisposable summonChannel, summonAckChannel, summonCountChannel;
        private readonly Dictionary<string, BossRushCoopSummonPacket> summonRequests = new Dictionary<string, BossRushCoopSummonPacket>();
        private readonly HashSet<string> acceptedSummons = new HashSet<string>();
        private readonly Dictionary<string, float> summonCooldowns = new Dictionary<string, float>();
        private readonly HashSet<MonoBehaviour> casting = new HashSet<MonoBehaviour>();
        private readonly Dictionary<int, int> localSummons = new Dictionary<int, int>();
        private readonly Dictionary<CharacterMainControl, SummonOwner> summonEntities = new Dictionary<CharacterMainControl, SummonOwner>();
        private string summonAsset;
        private sealed class SummonOwner { internal string Player, Asset; internal CharacterMainControl Character; }
        private float nextSummonTick;
        public override string ModuleName { get { return "CoopContent"; } }

        public override void OnAwake(ModBehaviour owner)
        {
            if (subscribed) return;
            instance = this;
            summonChannel = BossRushCoop.Register("content.summon", OnSummon);
            summonAckChannel = BossRushCoop.Register("content.summon-ack", OnSummonAck);
            summonCountChannel = BossRushCoop.Register("content.summon-count", OnSummonCount);
            BossRushCoop.ItemSnapshotRequested += OnSnapshotRequested;
            BossRushCoop.ItemSnapshotApplied += OnSnapshotApplied;
            BossRushCoop.PlayerSpawned += OnPlayerSpawned;
            BossRushCoop.SessionChanged += OnSessionChanged;
            subscribed = true;
        }

        private void OnSessionChanged()
        {
            ClearCharacters();
            applied.Clear();
            foreach (MonoBehaviour action in casting) if (action != null) UnityEngine.Object.Destroy(action);
            casting.Clear(); summonRequests.Clear(); acceptedSummons.Clear(); summonCooldowns.Clear(); localSummons.Clear(); nextSummonTick = 0f;
            summonEntities.Clear(); summonAsset = null;
            FrostmourneAction.ResetStaticCaches();
            SummonStaffAction.ResetStaticCaches();
        }

        public override void OnSceneLoaded(SceneRuntimeContext context) { OnSessionChanged(); }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!BossRushCoop.IsConnected) return;
            RefreshSummonAsset();
            PruneSummonOwners();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                OnPlayerSpawned(player);
            if (!BossRushCoop.SessionReady || Time.unscaledTime < nextSummonTick) return;
            nextSummonTick = Time.unscaledTime + .5f;
            if (BossRushCoop.IsClient)
            {
                foreach (BossRushCoopSummonPacket request in summonRequests.Values)
                    if (request.asset == BossRushCoop.LocalAssetId) BossRushCoop.SendToHost("content.summon", request);
            }
            else
                foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                    if (player.IsAlive)
                        BossRushCoop.SendToPlayer(player.Id, "content.summon-count", new BossRushCoopSummonPacket
                        {
                            run = BossRushCoop.RunId, scene = BossRushCoop.SceneGeneration, asset = player.AssetId,
                            staff = SummonStaffAction.GetCountForOwner(player.Character), frost = FrostmourneAction.GetCountForOwner(player.Character)
                        });
        }

        private void OnPlayerSpawned(BossRushCoopPlayer player)
        {
            if (player == null || player.Character == null || !observed.Add(player.Character)) return;
            player.Character.OnHoldAgentChanged += OnHoldChanged;
            OnHoldChanged(player.Character.CurrentHoldItemAgent);
        }

        private void OnHoldChanged(DuckovItemAgent agent)
        {
            if (agent == null || agent.Item == null) return;
            CustomItemRuntimeStateHelper.RestoreRuntimeState(agent.Item, "CoopHold");
            var gun = agent as ItemAgent_Gun;
            if (gun != null)
            {
                DragonBreathWeaponConfig.TryAddFireEffectsToAgent(gun);
                DragonKingBossGunRuntime.TryAddFireEffectsToAgent(gun);
            }
            if (agent.Item.TypeID == FrostmourneIds.WeaponTypeId)
                FrostmourneWeaponConfig.TryAddIceEffectsToGraphic(agent.gameObject);
        }

        private static void OnSnapshotRequested(Item item, Dictionary<string, string> data)
        {
            if (item == null || data == null) return;
            ReforgeDataPersistence.SyncCurrentReforgeState(item);
            var state = new BossRushCoopItemState { typeId = item.TypeID };
            if (item.Variables != null)
                foreach (CustomData variable in item.Variables)
                {
                    if (variable == null || !IsContentKey(variable.Key)) continue;
                    state.variables.Add(new BossRushCoopItemVariable {
                        key = variable.Key, type = (int)variable.DataType, display = variable.Display,
                        raw = Convert.ToBase64String(variable.GetRawCopied() ?? new byte[0]) });
                }
            // 空 manifest 也必须发送：已有实例上一份词缀/重铸可能已经被清空。
            if (state.variables.Count > 0 || BossRushDynamicItemRegistry.IsBossRushDynamicItemType(item.TypeID)
                || AffixItemData.IsAffixEligible(item)) data[SnapshotKey] = BossRushCoopJson.ToJson(state);
        }

        private void OnSnapshotApplied(Item item, IReadOnlyDictionary<string, string> data)
        {
            string json;
            if (item == null || data == null || !data.TryGetValue(SnapshotKey, out json) || string.IsNullOrEmpty(json)) return;
            try
            {
                string previous;
                if (applied.TryGetValue(item, out previous) && previous == json) return;
                BossRushCoopItemState state = BossRushCoopJson.FromJson<BossRushCoopItemState>(json);
                if (!Validate(state, item.TypeID)) return;
                // 内置快照已写入新 KV。先还原 prefab 基线，再按完整 manifest 清掉旧键，
                // 最后走生产恢复；0 差值与缺失键也能撤回先前的重铸。
                ReforgeDataPersistence.PrepareNetworkRestore(item);
                foreach (BossRushCoopItemVariable variable in state.variables)
                    if (variable.key.StartsWith("RF_", StringComparison.Ordinal))
                        item.Variables.SetRaw(variable.key, (CustomDataType)variable.type,
                            Convert.FromBase64String(variable.raw), true, variable.display);
                AffixItemData.ApplyNetworkSnapshot(item, state.variables);
                CustomItemRuntimeStateHelper.RestoreRuntimeState(item, "CoopSnapshot");
                AffixItemData.RefreshDisplayNames(item);
                NewWeaponEquipState.MarkDirty();
                AffixRuntimeService.NotifyItemAffixesChanged(item);
                applied[item] = json;
            }
            catch (Exception e) { ModBehaviour.DevLog("[CoopContent] 快照恢复失败: " + e.Message); }
        }

        internal static bool IsContentKey(string key)
        {
            return !string.IsNullOrEmpty(key) && (key.StartsWith("RF_", StringComparison.Ordinal)
                || AffixItemData.IsAffixVariableKey(key));
        }

        internal static bool Validate(BossRushCoopItemState state, int typeId)
        {
            if (state == null || state.schema != 1 || state.typeId != typeId || state.variables == null
                || state.variables.Count > 128) return false;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (BossRushCoopItemVariable variable in state.variables)
            {
                if (variable == null || !IsContentKey(variable.key) || !keys.Add(variable.key)
                    || variable.raw == null || variable.raw.Length > 8192
                    || !Enum.IsDefined(typeof(CustomDataType), variable.type)) return false;
                byte[] raw;
                try { raw = Convert.FromBase64String(variable.raw); } catch (FormatException) { return false; }
                if ((variable.type == (int)CustomDataType.Float || variable.type == (int)CustomDataType.Int) && raw.Length != 4) return false;
                if (variable.type == (int)CustomDataType.Bool && raw.Length != 1) return false;
                if (variable.type == (int)CustomDataType.Float && (float.IsNaN(BitConverter.ToSingle(raw, 0))
                    || float.IsInfinity(BitConverter.ToSingle(raw, 0)))) return false;
                if (variable.key.StartsWith("RF_", StringComparison.Ordinal)
                    && variable.type != (int)CustomDataType.Float) return false;
                if (AffixItemData.IsAffixVariableKey(variable.key)
                    && !AffixItemData.IsValidNetworkVariable(variable.key, (CustomDataType)variable.type, variable.display)) return false;
            }
            return true;
        }

        private void ClearCharacters()
        {
            foreach (CharacterMainControl character in observed)
                if (!ReferenceEquals(character, null)) character.OnHoldAgentChanged -= OnHoldChanged;
            observed.Clear();
        }

        internal static int LocalSummonCount(int kind)
        {
            if (instance != null) instance.RefreshSummonAsset();
            int count;
            return instance != null && instance.localSummons.TryGetValue(kind, out count) ? count : 0;
        }
        internal static void RequestSummon(int kind)
        {
            if (instance == null || !BossRushCoop.IsClient || !BossRushCoop.SessionReady) return;
            instance.RefreshSummonAsset();
            var packet = new BossRushCoopSummonPacket { id = Guid.NewGuid().ToString("N"), kind = kind,
                run = BossRushCoop.RunId, scene = BossRushCoop.SceneGeneration, asset = BossRushCoop.LocalAssetId };
            instance.summonRequests[packet.id] = packet;
            BossRushCoop.SendToHost("content.summon", packet);
        }
        private static bool CurrentSummon(BossRushCoopSummonPacket packet)
        { return packet != null && packet.run == BossRushCoop.RunId && packet.scene == BossRushCoop.SceneGeneration; }
        private void RefreshSummonAsset()
        {
            if (summonAsset == BossRushCoop.LocalAssetId) return;
            summonAsset = BossRushCoop.LocalAssetId;
            summonRequests.Clear(); localSummons.Clear();
        }
        internal static bool IsSummonOwnerCurrent(CharacterMainControl character, string asset)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(BossRushCoop.GetPlayerId(character));
            return player != null && player.IsAlive && player.Character == character && player.AssetId == asset;
        }
        internal static void TrackSummon(CharacterMainControl character, CharacterMainControl owner)
        {
            if (instance == null || !BossRushCoop.IsHost || character == null) return;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(BossRushCoop.GetPlayerId(owner));
            if (player == null) return;
            instance.summonEntities[character] = new SummonOwner { Player = player.Id, Asset = player.AssetId, Character = owner };
        }
        internal static bool IsSummonOwnedBy(CharacterMainControl character, CharacterMainControl owner)
        {
            if (!BossRushCoop.IsHost || instance == null) return true;
            SummonOwner state;
            return instance.summonEntities.TryGetValue(character, out state) && state.Character == owner
                && IsSummonOwnerCurrent(owner, state.Asset);
        }
        internal static void PruneSummonOwners()
        {
            if (instance == null || !BossRushCoop.IsHost) return;
            var remove = new List<CharacterMainControl>();
            foreach (var pair in instance.summonEntities)
            {
                BossRushCoopPlayer owner = BossRushCoop.FindPlayer(pair.Value.Player);
                if (pair.Key == null) { remove.Add(pair.Key); continue; }
                if (owner != null && owner.IsConnected && owner.AssetId == pair.Value.Asset && owner.Character == pair.Value.Character) continue;
                pair.Key.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(pair.Key.gameObject);
                remove.Add(pair.Key);
            }
            foreach (CharacterMainControl character in remove) instance.summonEntities.Remove(character);
        }
        private void OnSummon(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost || message.IsFromHost) return;
            BossRushCoopSummonPacket packet = message.Read<BossRushCoopSummonPacket>();
            Guid id;
            if (!CurrentSummon(packet) || !Guid.TryParseExact(packet.id, "N", out id) || (packet.kind != 1 && packet.kind != 2)) return;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(message.SenderId);
            if (player == null || player.AssetId != packet.asset) return;
            string key = message.SenderId + ":" + packet.asset + ":" + packet.id;
            if (!acceptedSummons.Add(key)) { BossRushCoop.SendToPlayer(message.SenderId, "content.summon-ack", packet); return; }
            string clock = message.SenderId + ":" + packet.asset + ":" + packet.kind;
            float next;
            bool equipped = player != null && player.IsAlive && player.Character.CurrentHoldItemAgent != null
                && player.Character.CurrentHoldItemAgent.Item != null && player.Character.CurrentHoldItemAgent.Item.TypeID
                    == (packet.kind == 1 ? NewWeaponIds.SummonStaffTypeId : FrostmourneIds.WeaponTypeId);
            if (equipped && (!summonCooldowns.TryGetValue(clock, out next) || Time.time >= next))
            {
                summonCooldowns[clock] = Time.time + (packet.kind == 1 ? new SummonStaffConfig().CooldownTime : new FrostmourneConfig().CooldownTime);
                ExecuteSummon(player.Character, packet.kind).Forget();
            }
            BossRushCoop.SendToPlayer(message.SenderId, "content.summon-ack", packet);
        }
        private async UniTaskVoid ExecuteSummon(CharacterMainControl player, int kind)
        {
            MonoBehaviour action = null;
            try
            {
                if (kind == 1)
                {
                    SummonStaffAction staff = player.gameObject.AddComponent<SummonStaffAction>();
                    action = staff; casting.Add(action); await staff.SpawnForCoop(player);
                }
                else
                {
                    FrostmourneAction frost = player.gameObject.AddComponent<FrostmourneAction>();
                    action = frost; casting.Add(action); await frost.SpawnForCoop(player);
                }
            }
            catch (Exception e) { ModBehaviour.DevLog("[CoopContent] 主人召唤失败: " + e.Message); }
            finally { if (!ReferenceEquals(action, null)) casting.Remove(action); if (action != null) UnityEngine.Object.Destroy(action); }
        }
        private void OnSummonAck(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            BossRushCoopSummonPacket packet = message.Read<BossRushCoopSummonPacket>();
            if (CurrentSummon(packet) && packet.asset == BossRushCoop.LocalAssetId && packet.id != null) summonRequests.Remove(packet.id);
        }
        private void OnSummonCount(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            BossRushCoopSummonPacket packet = message.Read<BossRushCoopSummonPacket>();
            if (!CurrentSummon(packet) || packet.asset != BossRushCoop.LocalAssetId) return;
            RefreshSummonAsset();
            localSummons[1] = Mathf.Clamp(packet.staff, 0, SummonStaffConfig.SummonCount);
            localSummons[2] = Mathf.Clamp(packet.frost, 0, FrostmourneConfig.SummonCount);
        }

        public override void OnDestroy()
        {
            if (summonChannel != null) summonChannel.Dispose();
            if (summonAckChannel != null) summonAckChannel.Dispose();
            if (summonCountChannel != null) summonCountChannel.Dispose();
            summonChannel = summonAckChannel = summonCountChannel = null;
            if (subscribed)
            {
                BossRushCoop.ItemSnapshotRequested -= OnSnapshotRequested;
                BossRushCoop.ItemSnapshotApplied -= OnSnapshotApplied;
                BossRushCoop.PlayerSpawned -= OnPlayerSpawned;
                BossRushCoop.SessionChanged -= OnSessionChanged;
                subscribed = false;
            }
            OnSessionChanged();
            if (instance == this) instance = null;
        }
    }
    [Serializable]
    internal sealed class BossRushCoopSummonPacket
    {
        public string id, run, asset;
        public int kind, scene, staff, frost;
    }
}
