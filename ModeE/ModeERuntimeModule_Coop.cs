using System;
using System.Collections.Generic;
using UnityEngine;
using Duckov.UI.DialogueBubbles;
using ItemStatsSystem;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopModeEHireState
    {
        public string Entity, Owner;
        public int Faction, BasePrice;
        public bool Hired;
    }

    [Serializable]
    internal sealed class CoopModeEHireRequest
    {
        public string Id, Epoch, Entity, AssetId, Run;
        public int Scene;
        public int Price;
    }

    [Serializable]
    internal sealed class CoopModeEState
    {
        public bool Active;
        public bool RespawnBusy;
        public string Epoch;
        public int Faction, Session;
        public string Merchant;
        public string[] Enemies;
        public int[] EnemyFactions;
        public int[] Factions, Deaths;
        public CoopModeEHireState[] Hires;
        public CoopModeECoalState[] Coal;
        public CoopModeEParticipant[] Participants;
    }

    [Serializable]
    internal sealed class CoopModeEParticipant
    {
        public string Id, AssetId;
        public bool Admitted, Declined;
        public int BossKills;
    }

    [Serializable]
    internal sealed class CoopModeEPersonal
    {
        public string Epoch;
        public bool Starter, LastHit;
        public int ShellReward, BossKills;
        public string HireId;
        public bool HireSucceeded;
        public string CoalId;
        public bool CoalSucceeded;
        public string UtilityId;
        public bool UtilitySucceeded;
    }

    internal sealed partial class ModeERuntimeModule
    {
        private CoopGameplayChannel<CoopModeEState> coopChannel;
        private CoopPersonalDelivery<CoopModeEPersonal> coopPersonal;
        private string coopEpoch, coopPersonalEpoch;
        private sealed class CoopModeERewardLedger
        {
            internal string AssetId;
            internal int BossKills, PendingKill;
            internal bool SmokeDelivered;
            internal ItemStatsSystem.Data.ItemTreeData Smoke;
        }
        private readonly Dictionary<string, CoopModeERewardLedger> coopRewardLedgers = new Dictionary<string, CoopModeERewardLedger>(StringComparer.Ordinal);
        private void PreserveCoopModeERewardLedger()
        {
            if (string.IsNullOrEmpty(coopEpoch) || string.IsNullOrEmpty(coopModeELocalAsset) || coopRewardLedgers.ContainsKey(coopEpoch)) return;
            coopRewardLedgers.Add(coopEpoch, new CoopModeERewardLedger { AssetId = coopModeELocalAsset });
        }
        private bool ApplyCoopModeEBossReward(CoopModeEPersonal receipt)
        {
            PreserveCoopModeERewardLedger();
            CoopModeERewardLedger ledger;
            if (!coopRewardLedgers.TryGetValue(receipt.Epoch, out ledger) || ledger.AssetId != BossRushCoop.LocalAssetId) return false;
            if (receipt.BossKills <= ledger.BossKills) return true;
            if (receipt.BossKills != ledger.BossKills + 1) return false;
            bool current = modeEActive && receipt.Epoch == coopEpoch && IsCoopModeELocalAdmitted();
            if (ledger.PendingKill == 0)
            {
                ledger.PendingKill = receipt.BossKills;
                if (current)
                {
                    modeERespawnKillCounter = receipt.BossKills;
                    if (receipt.LastHit)
                    {
                        modeEPlayerLastHitKillCount++;
                        RefreshModeEPlayerScaling();
                        ShowModeEPlayerGrowthBubble();
                    }
                }
            }
            if (receipt.BossKills % 10 == 0 && !ledger.SmokeDelivered)
            {
                if (current)
                {
                    if (!modeEHost.TryGiveItemToPlayerOrDrop(RespawnItemConfig.TAUNT_SMOKE_TYPE_ID, GetRespawnItemName(RespawnItemConfig.TAUNT_SMOKE_TYPE_ID), false)) return false;
                }
                else if (!TryStoreCoopModeEReward(ledger)) return false;
                ledger.SmokeDelivered = true;
                if (current)
                    try { modeEHost.ShowBigBanner(L10n.T("击杀 <color=yellow>" + receipt.BossKills + "</color> 个Boss！获得 <color=yellow>挑衅烟雾弹</color> ×1", "Killed <color=yellow>" + receipt.BossKills + "</color> Bosses! Received <color=yellow>Taunt Smoke</color> ×1")); }
                    catch (Exception e) { ModBehaviour.DevLog("[ModeE] 已交付挑衅烟雾弹，横幅失败: " + e.Message); }
            }
            int reward = receipt.ShellReward;
            ledger.BossKills = receipt.BossKills; ledger.PendingKill = 0; ledger.SmokeDelivered = false; ledger.Smoke = null;
            // 贝壳和成长原本随局结束归零；迟到永久物品仍交付原资产，不增新局临时数值。
            current = current && receipt.Epoch == coopEpoch && ledger.AssetId == BossRushCoop.LocalAssetId && IsCoopModeELocalAdmitted();
            if (current && reward > 0 && modeEShellEconomyAvailable)
            {
                if (!modeEShellFirstPositiveRewardGranted) { modeEShellFirstPositiveRewardGranted = true; reward += 10; }
                int balance = CreditModeEShell(reward, "Coop Boss reward");
                DialogueBubblesManager.Show(L10n.T("贝壳 +" + reward + "（持有 " + balance + "）", "Shells +" + reward + " (Held " + balance + ")"), CharacterMainControl.Main.transform, 2.5f, false, false, -1f, 3f);
            }
            return true;
        }
        private static bool TryStoreCoopModeEReward(CoopModeERewardLedger ledger)
        {
            Item item = null;
            try
            {
                if (ledger.Smoke == null)
                {
                    item = ItemAssetsCollection.InstantiateSync(RespawnItemConfig.TAUNT_SMOKE_TYPE_ID);
                    if (item == null) return false;
                    ledger.Smoke = ItemStatsSystem.Data.ItemTreeData.FromItem(item);
                    if (ledger.Smoke == null) return false;
                }
                bool buffered = false;
                foreach (ItemStatsSystem.Data.ItemTreeData data in PlayerStorageBuffer.Buffer)
                    if (ReferenceEquals(data, ledger.Smoke) || data != null && data.rootInstanceID == ledger.Smoke.rootInstanceID && data.RootTypeID == ledger.Smoke.RootTypeID)
                    { buffered = true; break; }
                if (!buffered) PlayerStorageBuffer.Buffer.Add(ledger.Smoke);
                PlayerStorageBuffer.SaveBuffer();
                return true;
            }
            catch (Exception e) { ModBehaviour.DevLog("[ModeE] 原局挑衅烟雾弹待交付: " + e.Message); return false; }
            finally { if (item != null) try { item.DestroyTree(); } catch (Exception e) { ModBehaviour.DevLog("[ModeE] 缓冲物品临时实例清理失败: " + e.Message); } }
        }
        private readonly Dictionary<string, ModeEShellTransactionOwner> coopHireDebits = new Dictionary<string, ModeEShellTransactionOwner>();
        private readonly Dictionary<string, CoopModeEHireRequest> coopHireRequests = new Dictionary<string, CoopModeEHireRequest>();
        private readonly Dictionary<string, CoopModeEPersonal> coopHireResults = new Dictionary<string, CoopModeEPersonal>();
        private float coopHireRetry;
        private string coopModeELocalAsset;
        private readonly Dictionary<string, CoopModeEParticipant> coopModeEParticipants = new Dictionary<string, CoopModeEParticipant>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> coopEntryDecisions = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Item>> coopEntryRefunds = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<int>> coopEntryRefundTypes = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        private float coopEntryAdmissionRetry;

        private void BeginCoopModeEParticipants()
        {
            coopModeELocalAsset = BossRushCoop.LocalAssetId;
            PreserveCoopModeERewardLedger();
            coopModeEParticipants.Clear();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (player.IsAlive && !string.IsNullOrEmpty(player.AssetId))
                    coopModeEParticipants[player.Id] = new CoopModeEParticipant
                    { Id = player.Id, AssetId = player.AssetId, Admitted = player.IsLocal };
        }

        private bool IsCoopModeEParticipant(BossRushCoopPlayer player)
        {
            CoopModeEParticipant participant;
            return player != null && coopModeEParticipants.TryGetValue(player.Id, out participant)
                && participant.AssetId == player.AssetId && participant.Admitted && !participant.Declined;
        }

        internal bool IsCoopModeELocalAdmitted()
        { return !BossRushCoop.IsConnected || coopModeELocalAsset == BossRushCoop.LocalAssetId
            && coopPersonalEpoch == BossRushCoop.LocalAssetId + ":" + coopEpoch; }

        internal bool ReopenCoopEntryDecision(string tag)
        {
            string asset = BossRushCoop.LocalAssetId;
            if (coopEntryRefunds.ContainsKey(asset) || coopEntryRefundTypes.ContainsKey(asset)) return false;
            bool admitted;
            if (coopEntryDecisions.TryGetValue(tag, out admitted) && !admitted) coopEntryDecisions.Remove(tag);
            return true;
        }

        private bool RetryCoopModeEAdmission()
        {
            string tag = "E:" + BossRushCoop.LocalAssetId + ":" + coopEpoch;
            if (coopModeELocalAsset != BossRushCoop.LocalAssetId || !ReopenCoopEntryDecision(tag)) return false;
            ApplyCoopModeEPersonal(new CoopModeEPersonal { Epoch = coopEpoch, Starter = true });
            return IsCoopModeELocalAdmitted();
        }

        // 个人准入先保留原物品，再发装；失败回收新增装备并把原费用留给原资产重试。
        internal bool CommitCoopModeEntry(string tag, bool bloodhunt, Action giveExtras)
        {
            bool previous;
            if (coopEntryDecisions.TryGetValue(tag, out previous)) return previous;
            CharacterMainControl player = CharacterMainControl.Main;
            string asset = BossRushCoop.LocalAssetId;
            bool prepaid = BossRushMapSelectionHelper.HasPendingPrepaidTicket();
            var held = new List<Item>();
            HashSet<int> inventory = null;
            bool committed = false;
            try
            {
                if (player == null || player.Health == null || player.Health.IsDead || !ModeHRuntimeGates.IsLegacyModeEntryAllowed()) return false;
                var flag = DetectFactionFlag();
                Item fee = bloodhunt ? ModeEntryInventory.FindFirstPlayerInventoryItemByTypeId(BloodhuntTransponderConfig.TYPE_ID) : flag.Item2;
                Item ticket = ModeEntryInventory.FindFirstPlayerInventoryItemByTypeId(BossRushMapSelectionHelper.GetBossRushTicketTypeId());
                bool naked = bloodhunt ? ModeEntryInventory.IsPlayerNakedWithAllowedItems("ModeF",
                    BossRushMapSelectionHelper.GetBossRushTicketTypeId(), BloodhuntTransponderConfig.TYPE_ID, false) : modeEHost.IsPlayerNaked();
                if (!naked || fee == null || bloodhunt && flag.Item1.HasValue || ticket == null && !prepaid
                    || !CaptureModeEStartupInventorySnapshot(out inventory)) return false;
                fee.Detach(); held.Add(fee);
                if (!prepaid) { ticket.Detach(); held.Add(ticket); }
                if (!equipment.GivePlayerStarterKit() || BossRushCoop.LocalAssetId != asset) return false;
                giveExtras();
                if (BossRushCoop.LocalAssetId != asset) return false;
                committed = true;
                return true;
            }
            catch (Exception e) { ModBehaviour.DevLog("[Coop Entry] Personal admission failed: " + e.Message); return false; }
            finally
            {
                coopEntryDecisions[tag] = committed;
                if (committed)
                {
                    foreach (Item item in held) try { item.DestroyTree(); } catch (Exception e) { ModBehaviour.DevLog("[Coop Entry] Retired fee: " + e.Message); }
                }
                else
                {
                    if (inventory != null && BossRushCoop.LocalAssetId == asset) RollbackModeEStartupInventory(inventory);
                    if (prepaid)
                    {
                        List<int> types;
                        if (!coopEntryRefundTypes.TryGetValue(asset, out types)) { types = new List<int>(); coopEntryRefundTypes.Add(asset, types); }
                        types.Add(BossRushMapSelectionHelper.GetBossRushTicketTypeId());
                    }
                    if (held.Count > 0)
                    {
                        List<Item> owed;
                        if (!coopEntryRefunds.TryGetValue(asset, out owed)) { owed = new List<Item>(); coopEntryRefunds.Add(asset, owed); }
                        owed.AddRange(held);
                    }
                    modeEHost.ShowMessage(L10n.T("个人入场条件未满足或发装失败，入场费用已保留返还。", "Personal entry was unavailable; entry items are retained for refund."));
                }
                BossRushMapSelectionHelper.ClearPendingEntryFlowState();
                TickCoopEntryRefunds();
            }
        }

        private void TickCoopEntryRefunds()
        {
            List<Item> owed;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;
            string asset = BossRushCoop.LocalAssetId;
            List<int> types;
            if (coopEntryRefundTypes.TryGetValue(asset, out types))
            {
                for (int i = types.Count - 1; i >= 0; i--)
                {
                    Item item = null;
                    try { item = ItemAssetsCollection.InstantiateSync(types[i]); } catch (Exception e) { ModBehaviour.DevLog("[Coop Entry] Refund creation: " + e.Message); }
                    if (item == null) continue;
                    if (!coopEntryRefunds.TryGetValue(asset, out owed)) { owed = new List<Item>(); coopEntryRefunds.Add(asset, owed); }
                    owed.Add(item); types.RemoveAt(i);
                }
                if (types.Count == 0) coopEntryRefundTypes.Remove(asset);
            }
            if (!coopEntryRefunds.TryGetValue(asset, out owed)) return;
            for (int i = owed.Count - 1; i >= 0; i--)
                if (owed[i] != null && SkyIslandInventoryTransaction.TryDeliver(owed[i], player)) owed.RemoveAt(i);
            if (owed.Count == 0) coopEntryRefunds.Remove(BossRushCoop.LocalAssetId);
        }

        private void InitializeCoopModeE()
        {
            coopChannel = new CoopGameplayChannel<CoopModeEState>("ModeE", CaptureCoopModeE, ApplyCoopModeE, ExecuteCoopModeE);
            coopPersonal = new CoopPersonalDelivery<CoopModeEPersonal>("ModeE", ApplyCoopModeEPersonal);
            coopCoalOwner = this;
            coopUtilitySmokeChannel = BossRushCoop.Register("ModeE.utility-smoke", OnCoopUtilitySmoke);
        }

        private string coopProjectionRoom;
        private bool coopProjectionRetiring;

        private void RetireLostCoopProjection()
        {
            if (coopProjectionRoom == null || (BossRushCoop.IsClient && coopProjectionRoom == BossRushCoop.RunId)) return;
            // 原客端镜像只属于原房间；网络停止或换房后先撤销，不能提升为单机世界。
            coopProjectionRetiring = true;
            try
            {
                PreserveCoopModeERewardLedger();
                            EndModeE(false);
            }
            finally { coopProjectionRetiring = false; coopProjectionRoom = null; }
        }

        private void TickCoopModeE(float deltaTime)
        {
            RetireLostCoopProjection();
            TickCoopEntryRefunds();
            coopChannel.Tick(deltaTime);
            coopPersonal.Tick(deltaTime);
            coopEntryAdmissionRetry -= Mathf.Max(0f, deltaTime);
            bool admitted;
            if (BossRushCoop.IsClient && modeEActive && coopModeELocalAsset == BossRushCoop.LocalAssetId && coopEntryAdmissionRetry <= 0f
                && coopEntryDecisions.TryGetValue("E:" + BossRushCoop.LocalAssetId + ":" + coopEpoch, out admitted))
            { coopEntryAdmissionRetry = 1f; coopChannel.Request("admission", text: coopEpoch, flag: admitted); }
            TickCoopCoal(deltaTime);
            TickCoopUtilities(deltaTime);
            coopHireRetry -= Mathf.Max(0f, deltaTime);
            if (BossRushCoop.IsClient && coopHireRetry <= 0f)
            {
                coopHireRetry = 1f;
                foreach (CoopModeEHireRequest request in coopHireRequests.Values)
                    if (request.AssetId == BossRushCoop.LocalAssetId && request.Run == BossRushCoop.RunId)
                        coopChannel.Request("hire", text: BossRushCoopJson.ToJson(request));
            }
            if (!BossRushCoop.IsHost || !modeEActive) return;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                if (!player.IsAlive) continue;
                CoopModeEParticipant participant;
                if (!coopModeEParticipants.TryGetValue(player.Id, out participant))
                { participant = new CoopModeEParticipant { Id = player.Id, AssetId = player.AssetId }; coopModeEParticipants[player.Id] = participant; }
                if (participant.AssetId != player.AssetId || participant.Declined) continue;
                if (participant.Admitted && player.Character.Team != modeEPlayerFaction) player.Character.SetTeam(modeEPlayerFaction);
                coopPersonal.Deliver(player.Id, "start:" + coopEpoch, new CoopModeEPersonal { Epoch = coopEpoch, Starter = true }, participant.AssetId);
            }
        }

        private CoopModeEState CaptureCoopModeE()
        {
            List<string> enemies = new List<string>();
            List<int> teams = new List<int>();
            foreach (CharacterMainControl enemy in modeEAliveEnemies)
            {
                string id = BossRushCoop.GetEntityId(enemy);
                if (string.IsNullOrEmpty(id)) continue;
                enemies.Add(id); teams.Add((int)enemy.Team);
            }
            List<int> factions = new List<int>(), deaths = new List<int>();
            foreach (KeyValuePair<Teams, int> pair in modeEFactionDeathCount) { factions.Add((int)pair.Key); deaths.Add(pair.Value); }
            List<CoopModeEHireState> hires = new List<CoopModeEHireState>();
            foreach (ModeEBossHireState hire in modeEBossHireOffers.Values)
            {
                string entity = BossRushCoop.GetEntityId(hire.Character);
                if (string.IsNullOrEmpty(entity)) continue;
                hires.Add(new CoopModeEHireState { Entity = entity, Owner = BossRushCoop.GetPlayerId(hire.Owner),
                    Faction = (int)hire.Faction, BasePrice = hire.BasePrice, Hired = hire.Hired });
            }
            return new CoopModeEState
            {
                Active = modeEActive, Epoch = coopEpoch, Faction = (int)modeEPlayerFaction, Session = modeESessionToken,
                RespawnBusy = modeERespawnTaskRunning,
                Merchant = merchantRuntime.CoopEntity,
                Hires = hires.ToArray(),
                Coal = CaptureCoopCoal(),
                Participants = new List<CoopModeEParticipant>(coopModeEParticipants.Values).ToArray(),
                Enemies = enemies.ToArray(), EnemyFactions = teams.ToArray(), Factions = factions.ToArray(), Deaths = deaths.ToArray()
            };
        }

        private void ApplyCoopModeE(CoopModeEState state)
        {
            RetireLostCoopProjection();
            if (BossRushCoop.IsClient && state.Active) coopProjectionRoom = BossRushCoop.RunId;
            PreserveCoopModeERewardLedger();
            bool started = state.Active && (!modeEActive || coopEpoch != state.Epoch);
            bool ended = modeEActive && !state.Active;
            if (ended) EndModeE(false);
            modeEActive = state.Active;
            coopEpoch = state.Epoch;
            coopModeEParticipants.Clear();
            if (state.Participants != null) foreach (CoopModeEParticipant participant in state.Participants)
                if (participant != null && !string.IsNullOrEmpty(participant.Id)) coopModeEParticipants[participant.Id] = participant;
            ApplyCoopCoal(state.Coal);
            modeEPlayerFaction = (Teams)state.Faction;
            modeESessionToken = state.Session;
            // E/F 共用活敌索引；E 的空快照不能抹去 F 已登记的镜像。
            if (!state.Active) return;
            if (started)
            {
                coopModeELocalAsset = BossRushCoop.LocalAssetId;
                modeEPlayerLastHitKillCount = 0;
                modeERespawnKillCounter = 0;
                RemoveModeEPlayerScalingModifiers();
                InitializeModeEShellSession(state.Session, UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
                InitializeModeELotteryAndHiringRuntime();
                ResetModeEUiCaches();
                EnsureModeEPlayerNameTag();
                InitializeModeDItemPools();
                equipment.EnsureModeDGlobalItemPool();
            }
            CharacterMainControl local = CharacterMainControl.Main;
            modeERespawnTaskRunning = state.RespawnBusy;
            if (state.Active) merchantRuntime.BindCoopEntity(state.Merchant);
            if (state.Active && IsCoopModeELocalAdmitted() && local != null && local.Team != modeEPlayerFaction) local.SetTeam(modeEPlayerFaction);
            modeEFactionDeathCount.Clear();
            if (state.Factions != null && state.Deaths != null)
                for (int i = 0; i < Math.Min(state.Factions.Length, state.Deaths.Length); i++) modeEFactionDeathCount[(Teams)state.Factions[i]] = state.Deaths[i];
            // 客户端只登记原联机创建的 AI 镜像，不能再次刷同一只 Boss。
            enemyRegistry.ClearAliveEnemies();
            enemyRegistry.ClearAliveEnemySet();
            enemyRegistry.ClearFactionLookup();
            enemyRegistry.ClearFactionAliveLists();
            if (state.Enemies != null && state.EnemyFactions != null)
                for (int i = 0; i < Math.Min(state.Enemies.Length, state.EnemyFactions.Length); i++)
                {
                    CharacterMainControl enemy = BossRushCoop.FindEntity(state.Enemies[i]);
                    if (enemy != null)
                    {
                        CoopAiPredictionHealthProtection.MarkReplica(enemy);
                        TrackModeEAliveEnemy(enemy, (Teams)state.EnemyFactions[i]);
                    }
                }
            if (state.Hires != null)
            {
                modeEHiredBosses.Clear();
                foreach (CoopModeEHireState remote in state.Hires)
                {
                    CharacterMainControl character = remote != null ? BossRushCoop.FindEntity(remote.Entity) : null;
                    if (character == null) continue;
                    CoopAiPredictionHealthProtection.MarkReplica(character);
                    RegisterModeEBossHireOffer(character, (Teams)remote.Faction, true);
                    ModeEBossHireState hire;
                    if (!modeEBossHireOffers.TryGetValue(character, out hire)) continue;
                    hire.Faction = (Teams)remote.Faction; hire.BasePrice = remote.BasePrice;
                    hire.Hired = remote.Hired;
                    BossRushCoopPlayer master = BossRushCoop.FindPlayer(remote.Owner);
                    hire.Owner = master != null ? master.Character : null;
                    if (hire.Hired) modeEHiredBosses[character] = hire;
                }
                RefreshModeEBossHireOffers();
            }
        }

        private bool ApplyCoopModeEPersonal(CoopModeEPersonal receipt)
        {
            if (!string.IsNullOrEmpty(receipt.UtilityId)) return ApplyCoopUtilityReceipt(receipt);
            if (!string.IsNullOrEmpty(receipt.CoalId)) return ApplyCoopCoalReceipt(receipt);
            if (!string.IsNullOrEmpty(receipt.HireId))
            {
                ModeEShellTransactionOwner debit;
                if (!coopHireDebits.TryGetValue(receipt.HireId, out debit)) return true;
                CoopModeEHireRequest request;
                if (!coopHireRequests.TryGetValue(receipt.HireId, out request) || request.AssetId != BossRushCoop.LocalAssetId) return false;
                if (receipt.HireSucceeded) MarkModeEShellTransactionCommitted(debit.TransactionID);
                else RefundIfDebited(debit.TransactionID);
                ClearBusyAndReleaseModeEShellTransactionIfOwned(debit, "Coop hire receipt");
                coopHireDebits.Remove(receipt.HireId); coopHireRequests.Remove(receipt.HireId);
                Duckov.UI.NotificationText.Push(L10n.T(receipt.HireSucceeded ? "雇佣成功" : "雇佣未成交，贝壳已退还",
                    receipt.HireSucceeded ? "Boss hired" : "Hire cancelled; Shells refunded"));
                return true;
            }
            if (!receipt.Starter) return ApplyCoopModeEBossReward(receipt);
            CoopModeERewardLedger oldLedger;
            if ((!modeEActive || receipt.Epoch != coopEpoch) && coopRewardLedgers.TryGetValue(receipt.Epoch, out oldLedger))
                return oldLedger.AssetId == BossRushCoop.LocalAssetId;
            if (!modeEActive || receipt.Epoch != coopEpoch || CharacterMainControl.Main == null) return false;
            if (receipt.Starter)
            {
                string personalEpoch = BossRushCoop.LocalAssetId + ":" + receipt.Epoch;
                if (coopPersonalEpoch == personalEpoch) return true;
                if (coopModeELocalAsset != BossRushCoop.LocalAssetId) return false;
                bool admitted = CommitCoopModeEntry("E:" + personalEpoch, false, delegate
                {
                    ModeEGiveColdWeatherGear();
                    if (modeEPlayerFaction == Teams.player) ModeEGiveLoneWolfSupplies();
                    ShowFactionBubble(modeEPlayerFaction);
                });
                if (admitted) coopPersonalEpoch = personalEpoch;
                if (admitted && CharacterMainControl.Main.Team != modeEPlayerFaction) CharacterMainControl.Main.SetTeam(modeEPlayerFaction);
                coopChannel.Request("admission", text: receipt.Epoch, flag: admitted);
                return true;
            }

            return true;
        }

        private void DeliverCoopModeEDeath(CharacterMainControl enemy, ModeEShellRewardSnapshot snapshot)
        {
            if (!BossRushCoop.IsHost || enemy == null) return;
            string id = "death:" + coopEpoch + ":" + enemy.GetInstanceID();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                if (!player.IsAlive || !IsCoopModeEParticipant(player)) continue;
                bool lastHit = snapshot.Killer == player.Character && enemy.Team != modeEPlayerFaction;
                int reward = 0;
                if (snapshot.Claimed && snapshot.RewardKind != ModeEShellRewardKind.None && snapshot.RegisteredFaction != modeEPlayerFaction)
                {
                    float ratio = snapshot.Killer == player.Character ? 1f : snapshot.HasDeathPosition &&
                        (snapshot.DeathPosition - player.Character.transform.position).sqrMagnitude <= 64f ? 0.5f : 0f;
                    int rewardBase = CalculateModeEShellStandardBossBase(snapshot.BirthMaxHealth);
                    if (snapshot.RewardKind == ModeEShellRewardKind.PromotedBoss) rewardBase = Mathf.CeilToInt(rewardBase * 0.70f);
                    reward = Mathf.CeilToInt(rewardBase * ratio);
                }
                CoopModeEParticipant participant = coopModeEParticipants[player.Id];
                participant.BossKills++;
                coopPersonal.Deliver(player.Id, id, new CoopModeEPersonal { Epoch = coopEpoch, LastHit = lastHit, ShellReward = reward, BossKills = participant.BossKills }, participant.AssetId);
            }
        }

        private void ExecuteCoopModeE(CoopGameplayCommand command)
        {
            if (command.Action == "admission")
            {
                CoopModeEParticipant participant;
                if (command.Text == coopEpoch && coopModeEParticipants.TryGetValue(command.PlayerId, out participant)
                    && participant.AssetId == command.AssetId)
                { participant.Admitted = command.Flag; participant.Declined = !command.Flag; }
                return;
            }
            if (command.Action == "coal") { ExecuteCoopCoal(command); return; }
            if (command.Action == "utility") { ExecuteCoopUtility(command); return; }
            if (command.Action == "hire") { ExecuteCoopModeEHire(command); return; }
            if (!BossRushCoop.SessionReady || modeEHost == null || !modeEHost.IsCurrentSceneValidBossRushArena()) return;
            // 房间采用房主所选阵营；客人只请求进入，不能覆写共同阵营或绕过房主准入。
            if (command.Action == "enter" && !modeEActive) TryStartModeE();
        }

        private bool RequestCoopModeEHire(CharacterMainControl character)
        {
            if (!BossRushCoop.SessionReady || coopChannel == null || !CanInteractWithModeEBossHireOffer(character)) return false;
            ModeEBossHireState state;
            ModeEShellTransactionOwner debit;
            if (!modeEBossHireOffers.TryGetValue(character, out state) || !TryAcquireModeEShellRuntimeTransaction(character, out debit)) return false;
            int price = GetCurrentModeEBossHirePrice(state);
            if (!TryDebitModeEShell(price, debit.TransactionID))
            { ClearBusyAndReleaseModeEShellTransactionIfOwned(debit, "Coop hire debit failed"); return false; }
            var request = new CoopModeEHireRequest { Id = Guid.NewGuid().ToString("N"), Epoch = coopEpoch,
                Entity = BossRushCoop.GetEntityId(character), Price = price, AssetId = BossRushCoop.LocalAssetId,
                Run = BossRushCoop.RunId, Scene = BossRushCoop.SceneGeneration };
            coopHireDebits[request.Id] = debit; coopHireRequests[request.Id] = request;
            coopChannel.Request("hire", text: BossRushCoopJson.ToJson(request));
            return true;
        }

        private void ExecuteCoopModeEHire(CoopGameplayCommand command)
        {
            CoopModeEHireRequest request = string.IsNullOrEmpty(command.Text) ? null : BossRushCoopJson.FromJson<CoopModeEHireRequest>(command.Text);
            Guid id;
            if (request == null || request.AssetId != command.AssetId || !Guid.TryParseExact(request.Id, "N", out id)) return;
            string key = command.PlayerId + ":" + request.Id;
            CoopModeEPersonal result;
            if (coopHireResults.TryGetValue(key, out result))
            { coopPersonal.Deliver(command.PlayerId, "hire:" + request.Id, result, request.AssetId); return; }
            result = new CoopModeEPersonal { Epoch = request.Epoch, HireId = request.Id };
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(command.PlayerId);
            CharacterMainControl character = BossRushCoop.FindEntity(request.Entity);
            ModeEBossHireState state;
            if (request.Run == BossRushCoop.RunId && request.Scene == BossRushCoop.SceneGeneration
                && modeEActive && request.Epoch == coopEpoch && player != null && player.IsAlive && IsCoopModeEParticipant(player) && character != null
                && modeEBossHireOffers.TryGetValue(character, out state) && IsModeEBossHireStateCurrent(state) && !state.Hired
                && state.Owner == null && request.Price == GetCurrentModeEBossHirePrice(state, player.Character)
                && (character.transform.position - player.Character.transform.position).sqrMagnitude <= 36f)
                result.HireSucceeded = ConvertCoopModeEHire(state, player.Character);
            coopHireResults[key] = result;
            coopPersonal.Deliver(command.PlayerId, "hire:" + request.Id, result, request.AssetId);
        }

        private bool ConvertCoopModeEHire(ModeEBossHireState state, CharacterMainControl player)
        {
            FrostmourneZombieFollower follower = null;
            ModeEBossHireConversionSnapshot snapshot = null;
            bool committed = false;
            try
            {
                if (state.Character.GetComponent<FrostmourneZombieFollower>() != null) return false;
                follower = state.Character.gameObject.AddComponent<FrostmourneZombieFollower>();
                if (follower == null || !TryConvertModeEBossToPlayerFaction(state, player, out snapshot)) return false;
                state.Follower = follower; state.Owner = player; state.Hired = true;
                modeEHiredBosses[state.Character] = state;
                committed = true;
                RefreshModeEHiredBossFormation(); RefreshModeEBossHireOffers();
                return true;
            }
            catch (Exception e) { ModBehaviour.DevLog("[ModeE/CoopHire] " + e.Message); return committed; }
            finally
            {
                if (!committed)
                {
                    RollbackModeEBossFactionConversion(state, snapshot);
                    state.Follower = null; state.Owner = null; state.Hired = false;
                    modeEHiredBosses.Remove(state.Character);
                    if (follower != null) { follower.DisablePlayerFollow(); UnityEngine.Object.Destroy(follower); }
                }
            }
        }

        private void ClearCoopModeEHires()
        {
            var completed = new List<string>();
            foreach (KeyValuePair<string, ModeEShellTransactionOwner> pair in coopHireDebits)
            {
                CoopModeEHireRequest request;
                if (!coopHireRequests.TryGetValue(pair.Key, out request)) continue;
                ClearBusyAndReleaseModeEShellTransactionIfOwned(pair.Value, "Coop hire cleanup");
                // 同一房间中保留未确认义务，查询房主原事务事实；错槽不能给当前余额退款。
                if ((BossRushCoop.IsConnected && request.Run == BossRushCoop.RunId) || request.AssetId != BossRushCoop.LocalAssetId) continue;
                RefundIfDebited(pair.Value.TransactionID); completed.Add(pair.Key);
            }
            foreach (string id in completed) { coopHireDebits.Remove(id); coopHireRequests.Remove(id); }
            if (coopCoalRun != BossRushCoop.RunId) coopHireResults.Clear();
            coopHireRetry = 0f;
        }
    }
}
