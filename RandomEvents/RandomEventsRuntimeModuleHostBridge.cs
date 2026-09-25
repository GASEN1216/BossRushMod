using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Duckov.Economy;
using Duckov.ItemUsage;

namespace BossRush
{
    /// <summary>随机事件旧入口与共享基础设施的窄转发；业务由唯一运行模块执行。</summary>
    public partial class ModBehaviour
    {
        internal bool IsRandomEventInfiniteHellRun()
        { return randomEventsRuntime.IsRandomEventInfiniteHellRun(); }

        internal Vector3[] GetRandomEventSpawnPointsSafe()
        { return randomEventsRuntime.GetRandomEventSpawnPointsSafe(); }

        internal Vector3 GetRandomEventSafePointAwayFromPlayer(float minDistance)
        { return randomEventsRuntime.GetRandomEventSafePointAwayFromPlayer(minDistance); }

        internal void CollectEventBuffTargets(List<CharacterMainControl> buffer)
        { randomEventsRuntime.CollectEventBuffTargets(buffer); }

        internal void ShowRandomEventBanner(string text)
        { randomEventsRuntime.ShowRandomEventBanner(text); }

        internal void ShowRandomEventDirectionalBanner(string eventName, Vector3 worldPos)
        { randomEventsRuntime.ShowRandomEventDirectionalBanner(eventName, worldPos); }

        internal bool TryApplyRandomEventForcedWeather(
            Duckov.Weathers.Weather weather,
            out bool prevForce,
            out Duckov.Weathers.Weather prevValue)
        { return randomEventsRuntime.TryApplyRandomEventForcedWeather(weather, out prevForce, out prevValue); }

        internal void RestoreRandomEventForcedWeather(bool prevForce, Duckov.Weathers.Weather prevValue)
        { randomEventsRuntime.RestoreRandomEventForcedWeather(prevForce, prevValue); }

        internal void MakeRandomEventAiSound(Vector3 pos, float radius, SoundTypes soundType)
        { randomEventsRuntime.MakeRandomEventAiSound(pos, radius, soundType); }

        internal void PopRandomEventText(string text, Vector3 worldPos, Color color, float size)
        { randomEventsRuntime.PopRandomEventText(text, worldPos, color, size); }

        internal void PlayRandomEventModSound(string relativePath)
        { randomEventsRuntime.PlayRandomEventModSound(relativePath); }

        internal void PlayRandomEventStinger(string stingerKey)
        { randomEventsRuntime.PlayRandomEventStinger(stingerKey); }

        internal void SpawnRandomEventIntruderBoss(
            EnemyPresetInfo preset,
            Vector3 position,
            Func<bool> isStillValid,
            Action<CharacterMainControl> onSpawned,
            Action onFailed)
        { randomEventsRuntime.SpawnRandomEventIntruderBoss(preset, position, isStillValid, onSpawned, onFailed); }

        internal void DespawnRandomEventIntruderBoss(CharacterMainControl boss)
        { randomEventsRuntime.DespawnRandomEventIntruderBoss(boss); }

        internal void SpawnRandomEventParadeDucks(
            Vector3 startPos,
            Vector3 forward,
            int count,
            Func<bool> isStillValid,
            Action<CharacterMainControl> onSpawned,
            Action<int, int> onCompleted)
        { randomEventsRuntime.SpawnRandomEventParadeDucks(startPos, forward, count, isStillValid, onSpawned, onCompleted); }

        internal bool HasRandomEventMerchantPreset()
        { return randomEventsRuntime.HasRandomEventMerchantPreset(); }

        internal void SpawnRandomEventMerchant(
            Vector3 position,
            Func<bool> isStillValid,
            Action<CharacterMainControl, StockShop> onSpawned,
            Action onFailed)
        { randomEventsRuntime.SpawnRandomEventMerchant(position, isStillValid, onSpawned, onFailed); }

        internal void DespawnRandomEventMerchant(CharacterMainControl merchant, StockShop shop)
        { randomEventsRuntime.DespawnRandomEventMerchant(merchant, shop); }

        internal InteractableLootbox CreateRandomEventAirdropLootbox(
            Vector3 position,
            int itemCount,
            int qualityMin,
            int qualityMax)
        { return randomEventsRuntime.CreateRandomEventAirdropLootbox(position, itemCount, qualityMin, qualityMax); }

        internal IEnumerator RandomEventAirdropDropRoutine(
            GameObject crate,
            Vector3 groundPos,
            float height,
            float seconds,
            Action onLanded)
        { return randomEventsRuntime.RandomEventAirdropDropRoutine(crate, groundPos, height, seconds, onLanded); }

        internal void SpawnRandomEventCashPiles(
            Vector3 center,
            long totalCash,
            int pileCount,
            float radius,
            Action<int, int> onCompleted,
            Func<bool> isStillValid = null)
        { randomEventsRuntime.SpawnRandomEventCashPiles(center, totalCash, pileCount, radius, onCompleted, isStillValid); }

        internal bool RandomEventInfiniteHellForRuntime { get { return infiniteHellMode; } }
        internal SpawnEgg RandomEventSpawnEggBehaviorForRuntime { get { return cachedSpawnEggBehavior; } set { cachedSpawnEggBehavior = value; } }
        internal CharacterRandomPreset RandomEventEggSpawnPresetForRuntime { set { eggSpawnPreset = value; } }
        internal IReadOnlyList<CharacterMainControl> RandomEventCachedCharactersForRuntime { get { return _cachedCharacters; } }
        internal void RefreshRandomEventCharacterCacheForRuntime() { RefreshCharacterCache(); }
        internal string GetRandomEventDirectionForRuntime(Vector3 position, Vector3 playerPosition) { return GetDirectionFromPlayer(position, playerPosition); }
        internal HashSet<int> BuildRandomEventLootCandidateIdsForRuntime() { return BuildGeneralBossLootCandidateIdSet(); }
        internal void RegisterRandomEventRecoveryAnchorForRuntime(CharacterMainControl character, Vector3 position) { RegisterEnemyRecoveryAnchor(character, position); }
        internal void UnregisterRandomEventRecoveryForRuntime(CharacterMainControl character) { UnregisterEnemyRecovery(character); }
        internal CharacterRandomPreset GetRandomEventMerchantPresetForRuntime() { return GetModeEMerchantPreset(); }
        internal List<Tuple<List<Duckov.Utilities.Tag>, string, string>> GetRandomEventMerchantCategoriesForRuntime(Duckov.Utilities.GameplayDataSettings.TagsData tagsData) { return GetModeEMerchantCategories(tagsData); }
        internal List<int> SearchRandomEventMerchantItemsForRuntime(List<Duckov.Utilities.Tag> tags, Duckov.Utilities.Tag[] excludeTags) { return ModeESearchItemsMultiTag(tags, excludeTags); }
        internal void SetRandomEventMerchantHealthForRuntime(CharacterMainControl character) { SetModeEMerchantHealth(character); }
    }
}
