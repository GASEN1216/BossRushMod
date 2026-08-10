// ============================================================================
// ModeELotteryAndHiring.cs - Mode E category lottery and all-faction Boss hire
// ============================================================================

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const float MODE_E_BOSS_HIRE_REFERENCE_HEALTH = 1000f;
        private const int MODE_E_BOSS_HIRE_REFERENCE_PRICE = 200;
        private const int MODE_E_BOSS_HIRE_MIN_BASE_PRICE = 50;
        private const int MODE_E_BOSS_HIRE_MAX_BASE_PRICE = 2000;
        private const int MODE_E_BOSS_HIRE_PRICE_ROUNDING = 10;

        private static readonly float[][] ModeELotteryQualityWeightAnchors =
        {
            new float[] { 4f, 9f, 18f, 27f, 23f, 12f, 5f, 2f },
            new float[] { 3f, 7f, 15f, 24f, 24f, 15f, 8f, 4f },
            new float[] { 2f, 5f, 11f, 20f, 25f, 18f, 12f, 7f },
            new float[] { 1f, 3f, 8f, 15f, 23f, 20f, 18f, 12f }
        };

        private sealed class ModeELotteryPoolState
        {
            internal long MerchantGeneration;
            internal int Price;
            internal int[] Qualities;
            internal Dictionary<int, int[]> TypeIDsByQuality;
        }

        private sealed class ModeEBossHireState
        {
            internal int SessionToken;
            internal CharacterMainControl Character;
            internal CharacterMainControl Owner;
            internal Teams Faction;
            internal int BasePrice;
            internal ModeEBossHireInteractable Interactable;
            internal FrostmourneZombieFollower Follower;
            internal bool Hired;
        }

        private sealed class ModeEBossHireConversionSnapshot
        {
            internal Teams OfferFaction;
            internal Teams TrackedFaction;
            internal Teams CharacterTeam;
            internal AICharacterController AI;
            internal DamageReceiver SearchedEnemy;
            internal bool Noticed;
            internal float ForceTracePlayerDistance;
            internal ModeEEnemyScalingState ScalingState;
            internal int DeathBaseline;
            internal ModeEShellRewardKind RewardKind;
            internal Teams RegisteredFaction;
            internal bool RewardStateComplete;
            internal bool RewardSettled;
        }

        private readonly Dictionary<StockShop, ModeELotteryPoolState> modeELotteryPools
            = new Dictionary<StockShop, ModeELotteryPoolState>();
        private readonly Dictionary<CharacterMainControl, ModeEBossHireState> modeEBossHireOffers
            = new Dictionary<CharacterMainControl, ModeEBossHireState>();
        private readonly Dictionary<CharacterMainControl, ModeEBossHireState> modeEHiredBosses
            = new Dictionary<CharacterMainControl, ModeEBossHireState>();
        private readonly List<ModeEBossHireState> modeEBossHireStateScratch
            = new List<ModeEBossHireState>(16);
        private readonly List<CharacterMainControl> modeEBossHireCharacterScratch
            = new List<CharacterMainControl>(16);

        private float modeELotterySessionStartTime = -1f;
        private bool modeEBossHireBalanceSubscribed = false;

        private void InitializeModeELotteryAndHiringRuntime()
        {
            CleanupModeELotteryAndHiringRuntime();
            modeELotterySessionStartTime = Time.time;
            ModeEShellBalanceChanged -= OnModeEBossHireShellBalanceChanged;
            ModeEShellBalanceChanged += OnModeEBossHireShellBalanceChanged;
            modeEBossHireBalanceSubscribed = true;
        }

        private void CleanupModeELotteryAndHiringRuntime()
        {
            if (modeEBossHireBalanceSubscribed)
            {
                ModeEShellBalanceChanged -= OnModeEBossHireShellBalanceChanged;
                modeEBossHireBalanceSubscribed = false;
            }

            modeEBossHireStateScratch.Clear();
            foreach (KeyValuePair<CharacterMainControl, ModeEBossHireState> pair in modeEBossHireOffers)
            {
                if (pair.Value != null)
                {
                    modeEBossHireStateScratch.Add(pair.Value);
                }
            }

            for (int i = 0; i < modeEBossHireStateScratch.Count; i++)
            {
                DestroyModeEBossHireStateObjects(modeEBossHireStateScratch[i]);
            }

            modeEBossHireStateScratch.Clear();
            modeEBossHireOffers.Clear();
            modeEHiredBosses.Clear();
            modeELotteryPools.Clear();
            modeELotterySessionStartTime = -1f;
        }

        private void ClearModeELotteryMerchantRuntime()
        {
            modeELotteryPools.Clear();
        }

        private void BuildModeELotteryPoolState(StockShop shop, long merchantGeneration)
        {
            if (!IsCurrentModeEShellMerchantScope(shop, merchantGeneration) ||
                shop.entries == null)
            {
                return;
            }

            SortedDictionary<int, List<int>> mutableBuckets =
                new SortedDictionary<int, List<int>>();
            List<int> prices = new List<int>(shop.entries.Count);

            for (int i = 0; i < shop.entries.Count; i++)
            {
                StockShop.Entry entry = shop.entries[i];
                if (entry == null || !entry.Show) continue;

                int quality = 1;
                try
                {
                    quality = ItemAssetsCollection.GetMetaData(entry.ItemTypeID).quality;
                }
                catch
                {
                    // Keep the existing category candidate even when metadata is incomplete.
                }

                List<int> bucket;
                if (!mutableBuckets.TryGetValue(quality, out bucket))
                {
                    bucket = new List<int>();
                    mutableBuckets[quality] = bucket;
                }
                bucket.Add(entry.ItemTypeID);

                int price;
                if (TryGetModeEShellPrice(shop, entry.ItemTypeID, out price) && price > 0)
                {
                    prices.Add(price);
                }
            }

            if (mutableBuckets.Count == 0 || prices.Count == 0)
            {
                modeELotteryPools.Remove(shop);
                return;
            }

            prices.Sort();
            int medianPrice = prices[prices.Count / 2];
            Dictionary<int, int[]> immutableBuckets = new Dictionary<int, int[]>();
            int[] qualities = new int[mutableBuckets.Count];
            int qualityIndex = 0;
            foreach (KeyValuePair<int, List<int>> pair in mutableBuckets)
            {
                qualities[qualityIndex++] = pair.Key;
                immutableBuckets[pair.Key] = pair.Value.ToArray();
            }

            modeELotteryPools[shop] = new ModeELotteryPoolState
            {
                MerchantGeneration = merchantGeneration,
                Price = Mathf.Max(1, medianPrice),
                Qualities = qualities,
                TypeIDsByQuality = immutableBuckets
            };
        }

        internal bool TryGetModeELotteryUiState(StockShop shop, out int price)
        {
            price = 0;
            ModeELotteryPoolState state;
            if (!IsCurrentModeEShellCapability(shop) ||
                !modeELotteryPools.TryGetValue(shop, out state) ||
                state == null ||
                state.MerchantGeneration != modeEShellMerchantGeneration ||
                state.Qualities == null || state.Qualities.Length == 0 ||
                state.Price <= 0)
            {
                return false;
            }

            price = state.Price;
            return true;
        }

        private bool TryRollModeELotteryTypeID(
            ModeELotteryPoolState state,
            out int itemTypeID)
        {
            itemTypeID = -1;
            if (state == null || state.Qualities == null ||
                state.TypeIDsByQuality == null || state.Qualities.Length == 0)
            {
                return false;
            }

            float elapsedMinutes = modeELotterySessionStartTime >= 0f
                ? Mathf.Max(0f, Time.time - modeELotterySessionStartTime) / 60f
                : 0f;
            float totalWeight = 0f;
            for (int i = 0; i < state.Qualities.Length; i++)
            {
                totalWeight += GetModeELotteryQualityWeight(
                    state.Qualities[i],
                    elapsedMinutes);
            }
            if (totalWeight <= 0f) return false;

            float roll = UnityEngine.Random.value * totalWeight;
            int selectedQuality = state.Qualities[state.Qualities.Length - 1];
            for (int i = 0; i < state.Qualities.Length; i++)
            {
                int quality = state.Qualities[i];
                roll -= GetModeELotteryQualityWeight(quality, elapsedMinutes);
                if (roll <= 0f)
                {
                    selectedQuality = quality;
                    break;
                }
            }

            int[] bucket;
            if (!state.TypeIDsByQuality.TryGetValue(selectedQuality, out bucket) ||
                bucket == null || bucket.Length == 0)
            {
                return false;
            }

            itemTypeID = bucket[UnityEngine.Random.Range(0, bucket.Length)];
            return itemTypeID > 0;
        }

        private static float GetModeELotteryQualityWeight(int quality, float elapsedMinutes)
        {
            int qualityIndex = Mathf.Clamp(quality, 1, 8) - 1;
            if (elapsedMinutes >= 30f)
            {
                return ModeELotteryQualityWeightAnchors[3][qualityIndex];
            }

            int lowerAnchor = Mathf.Clamp(Mathf.FloorToInt(elapsedMinutes / 10f), 0, 2);
            float interpolation = Mathf.Clamp01(
                (elapsedMinutes - lowerAnchor * 10f) / 10f);
            return Mathf.Lerp(
                ModeELotteryQualityWeightAnchors[lowerAnchor][qualityIndex],
                ModeELotteryQualityWeightAnchors[lowerAnchor + 1][qualityIndex],
                interpolation);
        }

        internal async UniTask<bool> BuyModeELotteryAsync(
            StockShop shop,
            long uiBindingID)
        {
            int lotteryPrice;
            if (!IsCurrentModeEShellUiBinding(shop, uiBindingID) ||
                !TryGetModeELotteryUiState(shop, out lotteryPrice) ||
                modeEShellBalance < lotteryPrice)
            {
                return false;
            }

            ModeEShellTransactionOwner owner;
            if (!TryAcquireModeEShellTransaction(shop, false, out owner)) return false;

            Item deliveryItem = null;
            bool debited = false;
            bool committed = false;
            try
            {
                if (shop.Busy) return false;
                SetModeEShellBusyField(modeEShellBuyingField, shop, true, "buying");
                owner.OwnsBuying = true;

                ModeELotteryPoolState state;
                int itemTypeID;
                if (!modeELotteryPools.TryGetValue(shop, out state) ||
                    state == null ||
                    state.MerchantGeneration != owner.MerchantGeneration ||
                    !TryRollModeELotteryTypeID(state, out itemTypeID))
                {
                    return false;
                }

                deliveryItem = await ItemAssetsCollection.InstantiateAsync(itemTypeID);
                int currentPrice;
                if (deliveryItem == null ||
                    !IsModeEShellTransactionOwner(owner) ||
                    !IsCurrentModeEShellUiBinding(shop, uiBindingID) ||
                    !TryGetModeELotteryUiState(shop, out currentPrice) ||
                    currentPrice != lotteryPrice ||
                    modeEShellBalance < lotteryPrice)
                {
                    DestroyModeEShellTemporarySample(deliveryItem);
                    deliveryItem = null;
                    return false;
                }

                NormalizeModeEShellStackForShop(shop, deliveryItem);
                deliveryItem.FromInfoKey = "UI_Trade";
                string capturedDisplayName = deliveryItem.DisplayName;

                if (!TryDebitModeEShell(lotteryPrice, owner.TransactionID))
                {
                    DestroyModeEShellTemporarySample(deliveryItem);
                    deliveryItem = null;
                    return false;
                }
                debited = true;

                int sourceInstanceID = deliveryItem.GetInstanceID();
                int expectedStackCount = deliveryItem.StackCount;
                bool saveCharacter = false;
                try { saveCharacter = LevelConfig.SaveCharacter; }
                catch { /* Optional scene persistence metadata may be unavailable. */ }
                int incomingBufferStart = -1;
                if (saveCharacter &&
                    !TryGetModeEShellIncomingBufferCount(out incomingBufferStart))
                {
                    incomingBufferStart = -1;
                }

                committed = true;
                MarkModeEShellTransactionCommitted(owner.TransactionID);
                try { ItemUtilities.SendToPlayerCharacterInventory(deliveryItem, false); }
                catch (Exception e)
                {
                    DevLog("[ModeE/Lottery] character inventory delivery threw: " + e.Message);
                }

                bool allowPurchasedObserver;
                HandleCommittedModeEShellDeliveryRemainder(
                    deliveryItem,
                    sourceInstanceID,
                    itemTypeID,
                    expectedStackCount,
                    saveCharacter,
                    incomingBufferStart,
                    out allowPurchasedObserver);
                if (allowPurchasedObserver)
                {
                    InvokeModeEShellItemPurchased(shop, deliveryItem);
                }
                PushModeELotteryRewardNotification(capturedDisplayName);
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE/Lottery] transaction failed: " + e.Message);
                if (committed)
                {
                    FailModeEShellEconomyDuringCommittedTransaction(
                        "unexpected lottery exception after delivery commit");
                }
                return committed;
            }
            finally
            {
                if (!committed && debited)
                {
                    RefundIfDebited(owner.TransactionID);
                }
                if (!committed && deliveryItem != null)
                {
                    DestroyModeEShellTemporarySample(deliveryItem);
                }
                ClearBusyAndReleaseModeEShellTransactionIfOwned(owner, "Lottery finally");
            }
        }

        private static void PushModeELotteryRewardNotification(string displayName)
        {
            try
            {
                NotificationText.Push(
                    L10n.T("抽奖获得：", "Lottery reward: ") +
                    (displayName ?? string.Empty));
            }
            catch (Exception e)
            {
                DevLog("[ModeE/Lottery] reward notification failed: " + e.Message);
            }
        }

        private bool TryAcquireModeEShellRuntimeTransaction(
            UnityEngine.Object scope,
            out ModeEShellTransactionOwner owner)
        {
            owner = null;
            if (scope == null || !modeEActive || !modeEShellEconomyAvailable ||
                modeEShellSessionToken <= 0 || modeEShellTransactionOwner != null)
            {
                return false;
            }

            owner = new ModeEShellTransactionOwner
            {
                SessionToken = modeEShellSessionToken,
                MerchantGeneration = modeEShellMerchantGeneration,
                TransactionID = NextModeEShellCounter(ref modeEShellNextTransactionID),
                Shop = null,
                OwnsBuying = false,
                OwnsSelling = false,
                IsSellAll = false
            };
            modeEShellTransactionOwner = owner;
            PublishModeEShellTransactionGateChanged(owner, true);
            return true;
        }

        private void OnModeEBossHireShellBalanceChanged(ModeEShellBalanceChangedEvent evt)
        {
            if (evt == null || evt.SessionToken != modeEShellSessionToken ||
                evt.SessionGeneration != modeEShellSessionGeneration)
            {
                return;
            }
            RefreshModeEBossHireOffers();
        }

        private void RegisterModeEBossHireOffer(
            CharacterMainControl character,
            Teams faction,
            bool isBoss)
        {
            if (!isBoss || character == null ||
                !modeEActive || modeESessionToken <= 0 ||
                modeEBossHireOffers.ContainsKey(character))
            {
                return;
            }

            GameObject offerObject = null;
            try
            {
                ModeEBossHireInteractable interactable =
                    BossRush.Utils.NPCInteractionGroupHelper
                        .GetOrCreateStandaloneInteractable<ModeEBossHireInteractable>(
                            character.transform,
                            "ModeE_BossHireInteractable",
                            new Vector3(0f, 1.1f, 0f),
                            component =>
                            {
                                SphereCollider collider =
                                    component.GetComponent<SphereCollider>();
                                if (collider != null)
                                {
                                    collider.isTrigger = true;
                                    collider.radius = 2.2f;
                                }
                                component.Setup(character);
                            });
                if (interactable == null)
                {
                    throw new InvalidOperationException(
                        "failed to create safe Boss hire interactable");
                }
                offerObject = interactable.gameObject;
                ModeEBossHireState state = new ModeEBossHireState
                {
                    SessionToken = modeESessionToken,
                    Character = character,
                    Faction = faction,
                    BasePrice = ResolveModeEBossHireBasePrice(character),
                    Interactable = interactable,
                    Follower = null,
                    Hired = false
                };
                modeEBossHireOffers[character] = state;
                RefreshModeEBossHireOffer(state);
            }
            catch (Exception e)
            {
                DevLog("[ModeE/Hire] create offer failed: " + e.Message);
                if (offerObject != null) UnityEngine.Object.Destroy(offerObject);
            }
        }

        private int ResolveModeEBossHireBasePrice(CharacterMainControl character)
        {
            float maxHealth = GetModeEMaxHealthValue(character);
            if ((float.IsNaN(maxHealth) || float.IsInfinity(maxHealth) || maxHealth <= 0f) &&
                character != null && character.Health != null)
            {
                maxHealth = character.Health.MaxHealth;
            }

            return CalculateModeEBossHireBasePrice(maxHealth);
        }

        private static int CalculateModeEBossHireBasePrice(float maxHealth)
        {
            if (float.IsNaN(maxHealth) || float.IsInfinity(maxHealth) || maxHealth <= 0f)
            {
                return MODE_E_BOSS_HIRE_REFERENCE_PRICE;
            }

            float rawPrice = maxHealth * MODE_E_BOSS_HIRE_REFERENCE_PRICE /
                MODE_E_BOSS_HIRE_REFERENCE_HEALTH;
            if (rawPrice >= MODE_E_BOSS_HIRE_MAX_BASE_PRICE)
            {
                return MODE_E_BOSS_HIRE_MAX_BASE_PRICE;
            }

            int roundedPrice = Mathf.CeilToInt(
                rawPrice / MODE_E_BOSS_HIRE_PRICE_ROUNDING) *
                MODE_E_BOSS_HIRE_PRICE_ROUNDING;
            return Mathf.Clamp(
                roundedPrice,
                MODE_E_BOSS_HIRE_MIN_BASE_PRICE,
                MODE_E_BOSS_HIRE_MAX_BASE_PRICE);
        }

        private int GetCurrentModeEBossHirePrice(ModeEBossHireState state)
        {
            long price = state != null
                ? Mathf.Max(1, state.BasePrice)
                : MODE_E_BOSS_HIRE_REFERENCE_PRICE;
            int aliveHireCount = modeEHiredBosses.Count;
            for (int i = 0; i < aliveHireCount; i++)
            {
                if (price > int.MaxValue / 2L)
                {
                    return int.MaxValue;
                }
                price *= 2L;
            }
            return (int)Math.Min((long)int.MaxValue, price);
        }

        private bool IsModeEBossHireStateCurrent(ModeEBossHireState state)
        {
            return state != null && state.SessionToken == modeESessionToken &&
                   modeEActive && state.Character != null &&
                   state.Character.gameObject != null &&
                   state.Character.Team == state.Faction &&
                   state.Character.Health != null && !state.Character.Health.IsDead;
        }

        internal bool CanInteractWithModeEBossHireOffer(CharacterMainControl character)
        {
            ModeEBossHireState state;
            return character != null &&
                   modeEBossHireOffers.TryGetValue(character, out state) &&
                   IsModeEBossHireStateCurrent(state) && !state.Hired &&
                   state.Owner == null &&
                   modeEShellTransactionOwner == null &&
                   modeEShellEconomyAvailable &&
                   modeEShellBalance >= GetCurrentModeEBossHirePrice(state);
        }

        private bool ShouldShowModeEBossHireOffer(ModeEBossHireState state, out int price)
        {
            price = state != null ? GetCurrentModeEBossHirePrice(state) : int.MaxValue;
            return IsModeEBossHireStateCurrent(state) && !state.Hired &&
                   state.Owner == null &&
                   modeEShellEconomyAvailable && modeEShellBalance >= price;
        }

        private void RefreshModeEBossHireOffers()
        {
            PruneModeEBossHireStates();
            foreach (KeyValuePair<CharacterMainControl, ModeEBossHireState> pair in modeEBossHireOffers)
            {
                RefreshModeEBossHireOffer(pair.Value);
            }
        }

        private void RefreshModeEBossHireOffer(ModeEBossHireState state)
        {
            if (state == null || state.Interactable == null)
            {
                return;
            }

            int price;
            bool visible = ShouldShowModeEBossHireOffer(state, out price);
            state.Interactable.SetPrice(price);
            if (state.Interactable.gameObject.activeSelf != visible)
            {
                state.Interactable.gameObject.SetActive(visible);
            }
        }

        private void PruneModeEBossHireStates()
        {
            modeEBossHireCharacterScratch.Clear();
            foreach (KeyValuePair<CharacterMainControl, ModeEBossHireState> pair in modeEBossHireOffers)
            {
                if (pair.Key == null || pair.Value == null || pair.Value.Character == null ||
                    pair.Value.Character.gameObject == null)
                {
                    modeEBossHireCharacterScratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < modeEBossHireCharacterScratch.Count; i++)
            {
                UnregisterModeEBossHireRuntime(
                    modeEBossHireCharacterScratch[i],
                    false);
            }
            modeEBossHireCharacterScratch.Clear();
        }

        internal bool TryHireModeEBoss(CharacterMainControl character)
        {
            ModeEBossHireState state;
            if (!CanInteractWithModeEBossHireOffer(character) ||
                !modeEBossHireOffers.TryGetValue(character, out state))
            {
                return false;
            }

            ModeEShellTransactionOwner owner;
            if (!TryAcquireModeEShellRuntimeTransaction(character, out owner)) return false;

            FrostmourneZombieFollower follower = null;
            ModeEBossHireConversionSnapshot conversionSnapshot = null;
            bool debited = false;
            bool committed = false;
            try
            {
                int price = GetCurrentModeEBossHirePrice(state);
                if (!IsModeEBossHireStateCurrent(state) || state.Hired ||
                    state.Owner != null ||
                    modeEShellBalance < price ||
                    modeEHiredBosses.ContainsKey(character) ||
                    character.GetComponent<FrostmourneZombieFollower>() != null)
                {
                    return false;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return false;

                follower = character.gameObject.AddComponent<FrostmourneZombieFollower>();
                if (follower == null) return false;

                if (!TryDebitModeEShell(price, owner.TransactionID)) return false;
                debited = true;

                if (!TryConvertModeEBossToPlayerFaction(
                        state,
                        player,
                        out conversionSnapshot))
                {
                    return false;
                }

                follower.InitializeForModeE(
                    player,
                    modeEHiredBosses.Count,
                    modeEHiredBosses.Count + 1);

                state.Follower = follower;
                state.Owner = player;
                state.Hired = true;
                modeEHiredBosses[character] = state;
                MarkModeEShellTransactionCommitted(owner.TransactionID);
                committed = true;

                RefreshModeEHiredBossFormation();
                RefreshModeEBossHireOffers();
                NotificationText.Push(L10n.T(
                    "雇佣成功，花费 " + price + " 贝壳",
                    "Boss hired for " + price + " Shells"));
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE/Hire] transaction failed: " + e.Message);
                return committed;
            }
            finally
            {
                if (!committed)
                {
                    modeEHiredBosses.Remove(character);
                    RollbackModeEBossFactionConversion(state, conversionSnapshot);
                    if (state != null)
                    {
                        state.Follower = null;
                        state.Owner = null;
                        state.Hired = false;
                    }
                    if (follower != null)
                    {
                        try { follower.DisablePlayerFollow(); }
                        catch { /* Best-effort transaction rollback. */ }
                        try { UnityEngine.Object.Destroy(follower); }
                        catch { /* Best-effort transaction rollback. */ }
                    }
                    if (debited) RefundIfDebited(owner.TransactionID);
                }
                ClearBusyAndReleaseModeEShellTransactionIfOwned(owner, "Boss hire finally");
            }
        }

        private bool TryConvertModeEBossToPlayerFaction(
            ModeEBossHireState state,
            CharacterMainControl owner,
            out ModeEBossHireConversionSnapshot snapshot)
        {
            snapshot = null;
            if (state == null || state.Character == null || owner == null)
            {
                return false;
            }

            CharacterMainControl character = state.Character;
            Teams trackedFaction;
            if (!modeEAliveEnemyFactionMap.TryGetValue(character, out trackedFaction))
            {
                trackedFaction = state.Faction;
            }

            ModeEEnemyScalingState scalingState = null;
            modeEEnemyScalingStates.TryGetValue(character, out scalingState);
            AICharacterController ai = character.GetComponentInChildren<AICharacterController>();
            snapshot = new ModeEBossHireConversionSnapshot
            {
                OfferFaction = state.Faction,
                TrackedFaction = trackedFaction,
                CharacterTeam = character.Team,
                AI = ai,
                SearchedEnemy = ai != null ? ai.searchedEnemy : null,
                Noticed = ai != null && ai.noticed,
                ForceTracePlayerDistance = ai != null ? ai.forceTracePlayerDistance : 0f,
                ScalingState = scalingState,
                DeathBaseline = scalingState != null ? scalingState.deathBaseline : 0,
                RewardKind = scalingState != null
                    ? scalingState.rewardKind
                    : ModeEShellRewardKind.None,
                RegisteredFaction = scalingState != null
                    ? scalingState.registeredFaction
                    : trackedFaction,
                RewardStateComplete = scalingState != null &&
                    scalingState.rewardStateComplete,
                RewardSettled = scalingState != null && scalingState.rewardSettled
            };

            try
            {
                character.SetTeam(modeEPlayerFaction);
                if (ai != null)
                {
                    ai.searchedEnemy = null;
                    ai.noticed = false;
                    ai.forceTracePlayerDistance = 0f;
                }

                UntrackModeEAliveEnemy(character, trackedFaction);
                TrackModeEAliveEnemy(character, modeEPlayerFaction);
                state.Faction = modeEPlayerFaction;

                if (scalingState != null)
                {
                    int preservedStacks = Mathf.Max(0, scalingState.appliedStacks);
                    scalingState.deathBaseline =
                        GetModeEFactionDeathCount(modeEPlayerFaction) - preservedStacks;
                    scalingState.registeredFaction = modeEPlayerFaction;
                    scalingState.rewardKind = ModeEShellRewardKind.None;
                    scalingState.rewardStateComplete = false;
                    scalingState.rewardSettled = true;
                }

                UnregisterModeEEnemyLootHandler(character);
                RegisterModeEEnemyLootHandler(character, modeEPlayerFaction);
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE/Hire] faction conversion failed: " + e.Message);
                RollbackModeEBossFactionConversion(state, snapshot);
                snapshot = null;
                return false;
            }
        }

        private void RollbackModeEBossFactionConversion(
            ModeEBossHireState state,
            ModeEBossHireConversionSnapshot snapshot)
        {
            if (state == null || snapshot == null || state.Character == null)
            {
                return;
            }

            CharacterMainControl character = state.Character;
            try
            {
                UnregisterModeEEnemyLootHandler(character);
                UntrackModeEAliveEnemy(character, modeEPlayerFaction);
                character.SetTeam(snapshot.CharacterTeam);
                TrackModeEAliveEnemy(character, snapshot.TrackedFaction);
                RegisterModeEEnemyLootHandler(character, snapshot.TrackedFaction);

                state.Faction = snapshot.OfferFaction;
                if (snapshot.ScalingState != null)
                {
                    snapshot.ScalingState.deathBaseline = snapshot.DeathBaseline;
                    snapshot.ScalingState.rewardKind = snapshot.RewardKind;
                    snapshot.ScalingState.registeredFaction = snapshot.RegisteredFaction;
                    snapshot.ScalingState.rewardStateComplete =
                        snapshot.RewardStateComplete;
                    snapshot.ScalingState.rewardSettled = snapshot.RewardSettled;
                }

                if (snapshot.AI != null)
                {
                    snapshot.AI.searchedEnemy = snapshot.SearchedEnemy;
                    snapshot.AI.noticed = snapshot.Noticed;
                    snapshot.AI.forceTracePlayerDistance =
                        snapshot.ForceTracePlayerDistance;
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeE/Hire] faction rollback failed: " + e.Message);
            }
        }

        internal void AttributeModeEHiredBossKillToOwner(ref DamageInfo damageInfo)
        {
            if (!modeEActive || damageInfo.fromCharacter == null)
            {
                return;
            }

            ModeEBossHireState state;
            if (!modeEHiredBosses.TryGetValue(damageInfo.fromCharacter, out state) ||
                state == null || !state.Hired || state.Owner == null ||
                state.Owner != CharacterMainControl.Main ||
                state.SessionToken != modeESessionToken)
            {
                return;
            }

            damageInfo.fromCharacter = state.Owner;
        }

        internal bool ShouldBlockModeEHiredBossTeamChange(
            CharacterMainControl character,
            Teams requestedTeam)
        {
            if (!modeEActive || character == null ||
                requestedTeam == modeEPlayerFaction)
            {
                return false;
            }

            ModeEBossHireState state;
            return modeEHiredBosses.TryGetValue(character, out state) &&
                   state != null && state.Hired &&
                   state.Owner == CharacterMainControl.Main &&
                   state.SessionToken == modeESessionToken;
        }

        private void RefreshModeEHiredBossFormation()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;

            modeEBossHireStateScratch.Clear();
            foreach (KeyValuePair<CharacterMainControl, ModeEBossHireState> pair in modeEHiredBosses)
            {
                ModeEBossHireState state = pair.Value;
                if (state != null && state.Character != null &&
                    state.Follower != null && state.Character.Health != null &&
                    !state.Character.Health.IsDead)
                {
                    modeEBossHireStateScratch.Add(state);
                }
            }

            int count = modeEBossHireStateScratch.Count;
            for (int i = 0; i < count; i++)
            {
                modeEBossHireStateScratch[i].Follower.InitializeForModeE(player, i, count);
            }
            modeEBossHireStateScratch.Clear();
        }

        private void UnregisterModeEBossHireRuntime(
            CharacterMainControl character,
            bool refreshOffers)
        {
            ModeEBossHireState state;
            if (object.ReferenceEquals(character, null) ||
                !modeEBossHireOffers.TryGetValue(character, out state))
            {
                return;
            }

            bool wasHired = state != null && state.Hired;
            modeEBossHireOffers.Remove(character);
            wasHired = modeEHiredBosses.Remove(character) || wasHired;
            DestroyModeEBossHireStateObjects(state);

            if (refreshOffers && wasHired)
            {
                RefreshModeEHiredBossFormation();
                RefreshModeEBossHireOffers();
            }
        }

        private static void DestroyModeEBossHireStateObjects(ModeEBossHireState state)
        {
            if (state == null) return;
            if (state.Follower != null)
            {
                try { state.Follower.DisablePlayerFollow(); }
                catch { /* Best-effort runtime cleanup. */ }
                try { UnityEngine.Object.Destroy(state.Follower); }
                catch { /* Best-effort runtime cleanup. */ }
                state.Follower = null;
            }
            if (state.Interactable != null && state.Interactable.gameObject != null)
            {
                try { UnityEngine.Object.Destroy(state.Interactable.gameObject); }
                catch { /* Best-effort runtime cleanup. */ }
                state.Interactable = null;
            }
            state.Owner = null;
            state.Hired = false;
        }
    }

    public sealed class ModeEBossHireInteractable : InteractableBase
    {
        private CharacterMainControl boss;
        private int displayedPrice = -1;

        public void Setup(CharacterMainControl targetBoss)
        {
            boss = targetBoss;
            overrideInteractName = true;
            zoomIn = false;
            interactMarkerOffset = new Vector3(0f, 0.35f, 0f);
        }

        public void SetPrice(int price)
        {
            int safePrice = Mathf.Max(1, price);
            if (displayedPrice == safePrice) return;
            displayedPrice = safePrice;

            string key = "BossRush_ModeE_Hire_" + safePrice;
            string text = L10n.T(
                "雇佣（" + safePrice + " 贝壳）",
                "Hire (" + safePrice + " Shells)");
            LocalizationHelper.InjectLocalization(key, text);
            overrideInteractName = true;
            _overrideInteractNameKey = key;
            InteractName = key;
        }

        protected override bool IsInteractable()
        {
            ModBehaviour owner = ModBehaviour.Instance;
            return owner != null && boss != null &&
                   owner.CanInteractWithModeEBossHireOffer(boss);
        }

        protected override void OnTimeOut()
        {
            ModBehaviour owner = ModBehaviour.Instance;
            if (owner == null || boss == null || !owner.TryHireModeEBoss(boss))
            {
                try
                {
                    NotificationText.Push(L10n.T(
                        "当前无法雇佣该 Boss",
                        "This Boss cannot be hired right now"));
                }
                catch { /* Notification failure must not break interaction cleanup. */ }
            }
        }
    }
}
