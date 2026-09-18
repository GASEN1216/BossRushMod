// 本场擂台规则与敌军伤势。只接管已入场的临时参赛者，不接触看台身体或真实装备。
// 属性沿用官方 Modifier 和 RuntimeStatModifierTracker；区域标识复用现有贴地环。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal sealed class ModeHMatchRules
    {
        internal const float WoundedHealthFraction = 0.75f;
        internal const float CoverRadiusFraction = 0.30f;
        internal const float SafeRadiusFraction = 0.65f;
        internal const float EdgeGraceSeconds = 5f;
        internal const float EdgeDamageFractionPerSecond = 0.02f;
        internal const float EntryPowerSeconds = 8f;
        private const float PollSeconds = 0.25f;

        private readonly List<Participant> _participants = new List<Participant>();
        private ModeHMatchPlanDto _plan;
        private Vector3 _center;
        private float _radius;
        private float _pollRemaining;
        private GameObject _visualRoot;
        private DamageInfo _edgeDamage;

        internal static bool HasRequiredHostSupport(CharacterMainControl character)
        {
            return character != null && character.Health != null && character.Health.OnHealthChange != null
                && character.Health.MaxHealth > 0f && character.CharacterItem != null
                && character.CharacterItem.GetStat("GunDamageMultiplier") != null
                && character.CharacterItem.GetStat("MeleeDamageMultiplier") != null
                && character.CharacterItem.GetStat("ElementFactor_Physics") != null;
        }

        internal bool Begin(ModeHMatchPlanDto plan, ModeHSupportedMap map, out string reason)
        {
            RestoreAll();
            reason = null;
            if (plan == null || map == null || map.ArenaRadius <= 0f)
            { reason = "match_rules_input_missing"; return false; }
            if (plan.conditionId != "center_cover" && plan.conditionId != "danger_edge"
                && plan.conditionId != "medical_limited" && plan.conditionId != "narrow_cage"
                && plan.conditionId != "open_field" && plan.conditionId != "residual_might")
            { reason = "match_rule_unknown"; return false; }
            _plan = plan;
            _center = map.ArenaCenter;
            _radius = map.ArenaRadius;
            _edgeDamage = new DamageInfo(null) { damageType = DamageTypes.realDamage,
                ignoreArmor = true, ignoreDifficulty = true, isFromBuffOrEffect = true };
            _edgeDamage.AddElementFactor(ElementTypes.physics, 1f);
            if (plan.conditionId != "center_cover" && plan.conditionId != "danger_edge") return true;
            try
            {
                _visualRoot = new GameObject("ModeHMatchRuleArea");
                LineRenderer ring = SkyIslandGroundRing.Create(_visualRoot.transform,
                    _center + Vector3.up * SkyIslandGroundRing.GroundLift);
                if (ring == null || ring.sharedMaterial == null)
                    throw new InvalidOperationException("match_rule_ring_unavailable");
                bool cover = plan.conditionId == "center_cover";
                SkyIslandGroundRing.SetShape(ring, _radius * (cover ? CoverRadiusFraction : SafeRadiusFraction),
                    0.22f, cover ? new Color(0.35f, 0.8f, 1f, 0.9f) : new Color(1f, 0.42f, 0.15f, 0.9f));
                return true;
            }
            catch (Exception e)
            {
                reason = "match_rule_area_failed:" + e.GetType().Name;
                RestoreAll();
                return false;
            }
        }

        internal bool Enter(ModeHParticipantRef reference, out string reason)
        {
            reason = null;
            if (_plan == null) return true;
            if (reference == null || reference.Character == null || reference.Character.Health == null)
            { reason = "match_rule_participant_missing"; return false; }
            for (int i = 0; i < _participants.Count; i++)
                if (ReferenceEquals(_participants[i].Reference.Character, reference.Character)) return true;
            Participant participant = new Participant(reference, _plan.conditionId);
            _participants.Add(participant); // 失败也交给同一个 cleanup owner。
            if (reference.IsEnemy && ModeHEncounterPlanner.IsWoundedEnemy(_plan, reference.PlanSlotIndex))
                participant.Health.SetHealth(participant.Health.MaxHealth * WoundedHealthFraction);
            if (!participant.Enter())
            { reason = "match_rule_stat_missing:" + _plan.conditionId; return false; }
            if (!participant.UpdateArea(HorizontalDistanceSquared(reference.Character.transform.position, _center), _radius))
            { reason = "match_rule_stat_missing:" + _plan.conditionId; return false; }
            return true;
        }

        internal bool Tick(float deltaTime, out string reason)
        {
            reason = null;
            if (_plan == null || deltaTime <= 0f) return true;
            _pollRemaining -= deltaTime;
            bool poll = _pollRemaining <= 0f;
            if (poll) _pollRemaining = PollSeconds;
            for (int i = _participants.Count - 1; i >= 0; i--)
            {
                Participant participant = _participants[i];
                if (participant.Reference.Character == null || participant.Health == null || participant.Health.IsDead)
                {
                    participant.Restore();
                    _participants.RemoveAt(i);
                    continue;
                }
                participant.Elapsed += deltaTime;
                if (!poll) continue;
                float distanceSquared = HorizontalDistanceSquared(participant.Reference.Character.transform.position, _center);
                if (!participant.UpdateArea(distanceSquared, _radius))
                { reason = "match_rule_stat_missing:" + _plan.conditionId; return false; }
                if (_plan.conditionId != "danger_edge") continue;
                // 以每人的实际入场时刻计时；增援不吃尚未入场时积累的伤害。
                int pulse = (int)Math.Max(0f, participant.Elapsed - EdgeGraceSeconds);
                if (pulse <= participant.LastEdgePulse) continue;
                participant.LastEdgePulse = pulse; // 不补发卡帧或离区时错过的多轮伤害。
                if (distanceSquared <= _radius * _radius * SafeRadiusFraction * SafeRadiusFraction) continue;
                _edgeDamage.damageValue = participant.Health.MaxHealth * EdgeDamageFractionPerSecond;
                _edgeDamage.damagePoint = participant.Reference.Character.transform.position;
                participant.Health.Hurt(_edgeDamage);
            }
            return true;
        }

        internal static float HorizontalDistanceSquared(Vector3 position, Vector3 center)
        {
            float x = position.x - center.x, z = position.z - center.z;
            return x * x + z * z;
        }

        internal void RestoreAll()
        {
            for (int i = _participants.Count - 1; i >= 0; i--) _participants[i].Restore();
            _participants.Clear();
            if (_visualRoot != null) UnityEngine.Object.Destroy(_visualRoot);
            _visualRoot = null;
            _plan = null;
            _pollRemaining = 0f;
        }

        private sealed class Participant
        {
            internal readonly ModeHParticipantRef Reference;
            internal readonly Health Health;
            internal float Elapsed;
            internal int LastEdgePulse;
            private readonly string _condition;
            private readonly List<ZombieModeAttributeModifierRecord> _modifiers = new List<ZombieModeAttributeModifierRecord>();
            private bool _areaActive;
            private bool _healingSubscribed;
            private bool _adjustingHealth;
            private float _lastHealth;

            internal Participant(ModeHParticipantRef reference, string condition)
            { Reference = reference; Health = reference.Character.Health; _condition = condition; }

            internal bool Enter()
            {
                if (_condition == "medical_limited")
                {
                    if (Health.OnHealthChange == null) return false;
                    _lastHealth = Health.CurrentHealth;
                    if (!_healingSubscribed)
                    { Health.OnHealthChange.AddListener(OnHealthChanged); _healingSubscribed = true; }
                }
                if (_condition == "narrow_cage") return Add("MeleeDamageMultiplier", 0.20f) && Add("GunDamageMultiplier", -0.20f);
                if (_condition == "open_field") return Add("GunDamageMultiplier", 0.15f);
                if (_condition == "residual_might")
                { _areaActive = true; return Add("GunDamageMultiplier", 0.20f) && Add("MeleeDamageMultiplier", 0.20f); }
                return true;
            }

            private bool Add(string stat, float value)
            { return RuntimeStatModifierTracker.TryAdd(Reference.Character, stat, value, this, _modifiers, "ModeHMatchRules"); }

            internal bool UpdateArea(float distanceSquared, float radius)
            {
                if (_condition != "center_cover" && _condition != "residual_might") return true;
                bool active = _condition == "center_cover"
                    ? distanceSquared <= radius * radius * CoverRadiusFraction * CoverRadiusFraction
                    : Elapsed < EntryPowerSeconds;
                if (active == _areaActive) return true;
                _areaActive = active;
                RuntimeStatModifierTracker.RemoveAll(_modifiers, "ModeHMatchRules");
                return !active || Add("ElementFactor_Physics", -0.25f);
            }

            private void OnHealthChanged(Health health)
            {
                if (!_healingSubscribed || _adjustingHealth || health == null) return;
                float current = health.CurrentHealth;
                if (current > _lastHealth && _lastHealth > 0f && !health.IsDead)
                {
                    _adjustingHealth = true;
                    try { health.SetHealth(_lastHealth + (current - _lastHealth) * 0.5f); }
                    finally { _adjustingHealth = false; }
                }
                _lastHealth = health.CurrentHealth;
            }

            internal void Restore()
            {
                if (_healingSubscribed && Health != null) Health.OnHealthChange.RemoveListener(OnHealthChanged);
                _healingSubscribed = false;
                RuntimeStatModifierTracker.RemoveAll(_modifiers, "ModeHMatchRules");
                _areaActive = false;
            }
        }
    }
}
