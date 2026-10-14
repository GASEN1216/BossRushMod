using System;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    public static partial class MutatorManager
    {
        private static Item coopPlayerItem;
        internal static CharacterMainControl CoopLocalPlayer { get { return _currentContext != null ? _currentContext.Player : null; } }
        internal static bool CoopLocalPlayerMatches(CharacterMainControl player)
        { return _currentContext != null && _currentContext.Player == player && coopPlayerItem == player.CharacterItem; }

        internal static string[] CopyCoopIds()
        {
            string[] ids = new string[_activeMutators.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = _activeMutators[i].Id;
            return ids;
        }

        internal static MutatorDefinition FindCoopDefinition(string id)
        {
            foreach (MutatorDefinition definition in MutatorPool.All)
                if (definition != null && definition.Id == id) return definition;
            return null;
        }

        private static bool IsCoopPersonalEffect(MutatorDefinition definition)
        {
            return definition.Category != MutatorCategory.EnemyBuff && definition.Id != "boss_regen" && definition.Id != "explode_on_death";
        }

        internal static void ApplyCoopPersonalIds(CharacterMainControl player, string[] ids, string mode)
        {
            RemoveAll();
            _currentContext = new MutatorContext { Player = player };
            coopPlayerItem = player != null ? player.CharacterItem : null;
            foreach (string id in ids)
            {
                MutatorDefinition definition = FindCoopDefinition(id);
                if (definition == null || !definition.AllowsMode(mode)) continue;
                _activeMutators.Add(definition);
                if (!IsCoopPersonalEffect(definition)) continue;
                try { definition.OnApply?.Invoke(_currentContext); }
                catch (Exception e) { ModBehaviour.DevLog("[Mutator Coop] Personal effect failed: " + e.Message); }
            }
            IsActive = _activeMutators.Count > 0;
        }

        internal static void RebindCoopLocalPlayer(CharacterMainControl player)
        {
            if (_currentContext == null || player == null || (_currentContext.Player == player && coopPlayerItem == player.CharacterItem)) return;
            foreach (var pair in _currentContext.PlayerAppliedModifiers)
                if (pair.stat != null && pair.modifier != null) try { pair.stat.RemoveModifier(pair.modifier); } catch (Exception) { /* 已销毁旧角色的属性清理失败不阻止新身体应用。 */ }
            _currentContext.PlayerAppliedModifiers.Clear();
            foreach (MutatorDefinition definition in _activeMutators)
                if (IsCoopPersonalEffect(definition)) try { definition.OnRemove?.Invoke(_currentContext); } catch (Exception) { /* 其余词条仍须退订并恢复默认倍率。 */ }
            _currentContext.Player = player;
            coopPlayerItem = player.CharacterItem;
            _currentContext.PersonalEnemyKilledCallbacks.Clear();
            _currentContext.EnemyKilledCallbacks.Clear();
            foreach (MutatorDefinition definition in _activeMutators)
                if (IsCoopPersonalEffect(definition)) try { definition.OnApply?.Invoke(_currentContext); } catch (Exception e) { ModBehaviour.DevLog("[Mutator Coop] Rebind failed: " + e.Message); }
        }

        internal static bool ApplyCoopPersonalKill(Vector3 position)
        {
            if (_currentContext == null || _currentContext.Player == null || _currentContext.Player.Health == null) return false;
            if (_currentContext.Player.Health.IsDead) return true;
            foreach (var callback in _currentContext.PersonalEnemyKilledCallbacks)
                try { callback(null, position); } catch (Exception e) { ModBehaviour.DevLog("[Mutator Coop] Kill effect failed: " + e.Message); }
            return true;
        }
    }
}
