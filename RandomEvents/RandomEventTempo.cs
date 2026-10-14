// 三种双刃战场事件复用同一临时属性生命周期；玩家和敌人同规则，不产现金或物品。
using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed class RandomEventTempo : RandomEventBase
    {
        private readonly RandomEventId _id;
        private readonly List<BossRushStatModifierRecord> _records = new List<BossRushStatModifierRecord>();
        private readonly List<BossRushStatModifierRecord> _playerRecords = new List<BossRushStatModifierRecord>(2);
        private CharacterMainControl _player;
        private readonly List<CharacterMainControl> _targets = new List<CharacterMainControl>();
        private readonly HashSet<CharacterMainControl> _tracked = new HashSet<CharacterMainControl>();
        private float _refresh;
        private int _playerModifiers;

        internal RandomEventTempo(RandomEventId id) { _id = id; }
        internal override RandomEventId Id { get { return _id; } }
        internal override float DurationSeconds { get { return RandomEventsTuning.TempoDurationSeconds; } }
        internal override float Weight
        {
            get
            {
                if (_id == RandomEventId.WildChase) return RandomEventsTuning.WeightWildChase;
                if (_id == RandomEventId.MeleeCarnival) return RandomEventsTuning.WeightMeleeCarnival;
                return RandomEventsTuning.WeightHeavySteps;
            }
        }
        internal override string DisplayName
        {
            get
            {
                if (_id == RandomEventId.WildChase) return L10n.T("疾风追逐", "Wild Chase");
                if (_id == RandomEventId.MeleeCarnival) return L10n.T("近战狂欢", "Melee Carnival");
                return L10n.T("沉重脚步", "Heavy Steps");
            }
        }

        internal override bool OnTrigger(RandomEventContext ctx)
        {
            try
            {
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                CharacterMainControl player = CharacterMainControl.Main;
                if (owner == null || player == null || ctx == null || ctx.Scope == null) return false;
                ClearModifiers();
                // 先登记撤销动作：任一后续步骤失败，调度器的 DiscardContext 同样能完整回滚。
                ctx.Scope.RegisterCleanup(ClearModifiers);
                if (!RefreshPlayer()) return false;
                RefreshEnemies(owner);
                _refresh = RandomEventsTuning.TempoRefreshSeconds;
                owner.ShowRandomEventBanner(DisplayName + L10n.T("：敌我同时", ": everyone ") + Description());
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(RandomEventsTuning.LogPrefix + "战场节奏事件触发失败: " + e.Message);
                return false;
            }
        }

        private string Description()
        {
            if (_id == RandomEventId.WildChase) return L10n.T("移动 +25%，持续 24 秒", "moves 25% faster for 24s");
            if (_id == RandomEventId.MeleeCarnival) return L10n.T("近战 +35%、枪伤 -15%，持续 24 秒", "gets +35% melee, -15% gun damage for 24s");
            return L10n.T("奔跑 -20%、近战 +20%，持续 24 秒", "gets -20% run speed, +20% melee damage for 24s");
        }

        private bool RefreshPlayer()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null && player == _player && _playerRecords.Count == 2
                && ReferenceEquals(player.CharacterItem, _playerRecords[0].CharacterItem)) return true;
            // 死亡重生或本机 CharacterItem 换绑时，只撤销本人的旧记录，再给新实体挂一次。
            RuntimeStatModifierTracker.RemoveAll(_playerRecords, "RandomEventTempo player");
            _player = player;
            _playerModifiers = player != null ? Apply(player, _playerRecords) : 0;
            if (_playerModifiers == 2) return true;
            RuntimeStatModifierTracker.RemoveAll(_playerRecords, "RandomEventTempo partial player");
            return false;
        }

        private int Apply(CharacterMainControl character, List<BossRushStatModifierRecord> records)
        {
            int applied = 0;
            if (_id == RandomEventId.WildChase)
            {
                if (Add(character, ZombieModeStatNames.WalkSpeed, RandomEventsTuning.WildChaseMoveBonus, records)) applied++;
                if (Add(character, ZombieModeStatNames.RunSpeed, RandomEventsTuning.WildChaseMoveBonus, records)) applied++;
            }
            else if (_id == RandomEventId.MeleeCarnival)
            {
                if (Add(character, ZombieModeStatNames.MeleeDamageMultiplier, RandomEventsTuning.MeleeCarnivalMeleeBonus, records)) applied++;
                if (Add(character, ZombieModeStatNames.GunDamageMultiplier, RandomEventsTuning.MeleeCarnivalGunPenalty, records)) applied++;
            }
            else
            {
                if (Add(character, ZombieModeStatNames.RunSpeed, RandomEventsTuning.HeavyStepsMovePenalty, records)) applied++;
                if (Add(character, ZombieModeStatNames.MeleeDamageMultiplier, RandomEventsTuning.HeavyStepsMeleeBonus, records)) applied++;
            }
            return applied;
        }

        private bool Add(CharacterMainControl character, string stat, float amount, List<BossRushStatModifierRecord> records)
        {
            return RuntimeStatModifierTracker.TryAdd(character, stat, amount, this, records, "RandomEventTempo");
        }

        private void RefreshEnemies(ModBehaviour owner)
        {
            if (!BossRushCoop.HasWorldAuthority) return;
            owner.CollectEventBuffTargets(_targets);
            for (int i = 0; i < _targets.Count; i++)
            {
                CharacterMainControl target = _targets[i];
                if (target == null || BossRushCoop.IsPlayer(target) || !_tracked.Add(target)) continue;
                Apply(target, _records);
            }
            _targets.Clear();
        }

        internal override void OnTick(RandomEventContext ctx, float deltaTime)
        {
            try
            {
                _refresh -= deltaTime;
                if (_refresh > 0f) return;
                _refresh = RandomEventsTuning.TempoRefreshSeconds;
                RefreshPlayer();
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                if (owner != null) RefreshEnemies(owner);
            }
            catch (Exception) { }
        }

        internal override RandomEventValidationOutcome GetValidationOutcome(out string metrics)
        {
            metrics = "player_modifiers=" + _playerModifiers + ",tracked=" + _tracked.Count + ",modifiers=" + (_records.Count + _playerRecords.Count);
            return _playerModifiers == 2 && _playerRecords.Count == 2
                ? RandomEventValidationOutcome.Passed : RandomEventValidationOutcome.Failed;
        }

        private void ClearModifiers()
        {
            RuntimeStatModifierTracker.RemoveAll(_records, "RandomEventTempo");
            RuntimeStatModifierTracker.RemoveAll(_playerRecords, "RandomEventTempo player");
            _player = null;
            _targets.Clear();
            _tracked.Clear();
            _playerModifiers = 0;
            _refresh = 0f;
        }

        internal override void OnCleanup(RandomEventContext ctx, RandomEventEndReason reason)
        {
            RandomEventEffectHelpers.ClearScope(ctx, reason);
            ClearModifiers();
        }
    }
}
