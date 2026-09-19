// 三种双刃战场事件复用同一临时属性生命周期；玩家和敌人同规则，不产现金或物品。
using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed class RandomEventTempo : RandomEventBase
    {
        private readonly RandomEventId _id;
        private readonly List<ZombieModeAttributeModifierRecord> _records = new List<ZombieModeAttributeModifierRecord>();
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
                CharacterMainControl player = RandomEventEffectHelpers.ResolveAlivePlayer();
                if (owner == null || player == null || ctx == null || ctx.Scope == null) return false;
                ClearModifiers();
                // 先登记撤销动作：任一后续步骤失败，调度器的 DiscardContext 同样能完整回滚。
                ctx.Scope.RegisterCleanup(ClearModifiers);
                _playerModifiers = Apply(player);
                if (_playerModifiers != 2) return false;
                _tracked.Add(player);
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

        private int Apply(CharacterMainControl character)
        {
            int applied = 0;
            if (_id == RandomEventId.WildChase)
            {
                if (Add(character, ZombieModeStatNames.WalkSpeed, RandomEventsTuning.WildChaseMoveBonus)) applied++;
                if (Add(character, ZombieModeStatNames.RunSpeed, RandomEventsTuning.WildChaseMoveBonus)) applied++;
            }
            else if (_id == RandomEventId.MeleeCarnival)
            {
                if (Add(character, ZombieModeStatNames.MeleeDamageMultiplier, RandomEventsTuning.MeleeCarnivalMeleeBonus)) applied++;
                if (Add(character, ZombieModeStatNames.GunDamageMultiplier, RandomEventsTuning.MeleeCarnivalGunPenalty)) applied++;
            }
            else
            {
                if (Add(character, ZombieModeStatNames.RunSpeed, RandomEventsTuning.HeavyStepsMovePenalty)) applied++;
                if (Add(character, ZombieModeStatNames.MeleeDamageMultiplier, RandomEventsTuning.HeavyStepsMeleeBonus)) applied++;
            }
            return applied;
        }

        private bool Add(CharacterMainControl character, string stat, float amount)
        {
            return RuntimeStatModifierTracker.TryAdd(character, stat, amount, this, _records, "RandomEventTempo");
        }

        private void RefreshEnemies(ModBehaviour owner)
        {
            owner.CollectEventBuffTargets(_targets);
            for (int i = 0; i < _targets.Count; i++)
            {
                CharacterMainControl target = _targets[i];
                if (target == null || !_tracked.Add(target)) continue;
                Apply(target);
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
                ModBehaviour owner = RandomEventEffectHelpers.ResolveOwner(ctx);
                if (owner != null) RefreshEnemies(owner);
            }
            catch (Exception) { }
        }

        internal override RandomEventValidationOutcome GetValidationOutcome(out string metrics)
        {
            metrics = "player_modifiers=" + _playerModifiers + ",tracked=" + _tracked.Count + ",modifiers=" + _records.Count;
            return _playerModifiers == 2 && _records.Count >= 2
                ? RandomEventValidationOutcome.Passed : RandomEventValidationOutcome.Failed;
        }

        private void ClearModifiers()
        {
            RuntimeStatModifierTracker.RemoveAll(_records, "RandomEventTempo");
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
