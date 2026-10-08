using System;
using Duckov.Utilities;
using Pathfinding;
using UnityEngine;

namespace BossRush
{
    /// <summary>临时棍卫身份与战斗 owner。只准本场显式生成的小弟启用 NPC 星阙表现。</summary>
    internal sealed class SandstormChampionMinionMarker : MonoBehaviour
    {
        internal const string PresetName = "SandstormChampion_Minion_Preset";
        internal const string NameKey = "BossRush_SandstormChampion_Minion";
        private SandstormChampionMinions _owner;
        private CharacterMainControl _character;
        private CharacterRandomPreset _preset;
        private AI_PathControl _path;
        private Seeker _seeker;
        private int _generation;
        private bool _active;
        private bool _despawning;
        private float _nextPathAt;
        private float _nextAttackAt;
        private float _stuckSeconds;
        private Vector3 _lastPosition;
        private SandstormChampionBody _body;
        private SandstormChampionMinionArts _arts;

        // 不建静态集合；创建返回前也凭独占 clone 身份挡住额外掉落。
        internal static bool IsSummonedMinion(CharacterMainControl character)
        {
            if (character == null) return false;
            try
            {
                return (character.characterPreset != null
                    && string.Equals(character.characterPreset.name, PresetName, StringComparison.Ordinal))
                    || character.GetComponent<SandstormChampionMinionMarker>() != null;
            }
            catch { return false; }
        }

        internal bool IsCombatActive
        {
            get
            {
                return _active && !_despawning && _owner != null && _owner.IsWaveCurrent(_generation)
                    && _character != null && _character.Health != null && !_character.Health.IsDead;
            }
        }

        internal CharacterMainControl Character { get { return _character; } }

        internal void Initialize(SandstormChampionMinions owner, CharacterMainControl character,
            CharacterRandomPreset preset, int generation)
        {
            _owner = owner;
            _character = character;
            _preset = preset;
            _generation = generation;
            // 不复用已停用的官方 AI 子物体；根节点的官方路径组件只负责移动。
            _seeker = gameObject.AddComponent<Seeker>();
            _path = gameObject.AddComponent<AI_PathControl>();
            _path.seeker = _seeker;
            _path.controller = character;
            _path.nextWaypointDistance = 0.65f;
            _path.stopDistance = 0.25f;
        }

        internal void Activate()
        {
            _body = SandstormChampionBody.Attach(_character, 0.56f, true);
            _body.SetIntensity(0.25f);
            _lastPosition = transform.position;
            _nextAttackAt = Time.time + SandstormChampionConfig.MinionFirstAttackDelay;
            _arts = new SandstormChampionMinionArts(this, _character, _owner != null ? _owner.Controller : null);
            _active = true;
        }

        private void Update()
        {
            if (!_active || _despawning) return;
            if (!IsCombatActive)
            {
                Despawn();
                return;
            }
            if (BossRushUI.IsGamePaused()) return;
            try { TickCombat(); }
            catch (Exception e)
            {
                ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "棍卫战斗已撤销: " + e.Message);
                Despawn();
            }
        }

        private void TickCombat()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.Health == null || player.Health.IsDead)
            {
                Despawn();
                return;
            }
            ItemAgent_MeleeWeapon melee = _character.GetMeleeWeapon();
            if (melee == null || melee.Item == null || melee.Item.TypeID != BossRushItemIds.AstralStaff)
            {
                Despawn();
                return;
            }
            _character.SetAdsInput(false);
            _character.SetRunInput(false);
            // 重击进行中由 SandstormChampionMinionArts 驱动朝向与位移，普通追击和轻击暂停。
            if (_arts != null && _arts.Running)
            {
                _path.StopMove();
                _stuckSeconds = 0f;
                _lastPosition = transform.position;
                return;
            }
            _character.SetAimPoint(player.transform.position + Vector3.up * 0.7f);
            Vector3 towards = player.transform.position - transform.position;
            towards.y = 0f;
            float range = Mathf.Max(0.5f, melee.AttackRange - 0.25f);
            bool sight = !Physics.Linecast(transform.position + Vector3.up * 0.65f,
                player.transform.position + Vector3.up * 0.65f, GameplayDataSettings.Layers.wallLayerMask,
                QueryTriggerInteraction.Ignore);
            if (_arts != null && _arts.TryStart(player, towards.magnitude, sight))
            {
                _path.StopMove();
                return;
            }
            if (towards.sqrMagnitude <= range * range && sight)
            {
                _path.StopMove();
                _stuckSeconds = 0f;
                if (towards.sqrMagnitude > 0.001f && _character.movementControl != null)
                {
                    _character.movementControl.ForceTurnTo(towards.normalized);
                    _character.movementControl.ForceSetAimDirectionToAimPoint();
                }
                if (Time.time >= _nextAttackAt && !BossSkillDamageRules.IsDodging(player)
                    && _character.Attack())
                {
                    _nextAttackAt = Time.time + SandstormChampionConfig.MinionAttackCooldown;
                    AstralStaffHandVisual hand = melee.GetComponent<AstralStaffHandVisual>();
                    if (hand != null) hand.SetSurge(0.25f);
                }
            }
            else if (Time.time >= _nextPathAt)
            {
                _nextPathAt = Time.time + SandstormChampionConfig.MinionRepathSeconds;
                if (!_path.WaitingForPathResult)
                {
                    Vector3 reachable;
                    if (!SpawnPositionHelper.TryResolveReachableFrom(player.transform.position,
                        transform.position, 1.2f, SpawnPositionHelper.DefaultLiftOffset, out reachable))
                    {
                        Despawn();
                        return;
                    }
                    _path.MoveToPos(reachable);
                }
            }

            // 完整寻路仍可能被新建筑堵住；短命召唤物卡住时退场，避免永久停在墙后。
            Vector3 travelled = transform.position - _lastPosition;
            travelled.y = 0f;
            if (towards.sqrMagnitude > range * range && travelled.sqrMagnitude < 0.00001f)
                _stuckSeconds += Time.deltaTime;
            else _stuckSeconds = 0f;
            _lastPosition = transform.position;
            if (_stuckSeconds >= 2.5f) Despawn();
        }

        internal void Despawn()
        {
            if (_despawning) return;
            _despawning = true;
            _active = false;
            if (_arts != null) _arts.Release();
            _arts = null;
            if (_body != null) Destroy(_body.gameObject);
            _body = null;
            if (_character != null) _character.dropBoxOnDead = false;
            try
            {
                if (_seeker != null) _seeker.CancelCurrentPathRequest(true);
                if (_path != null)
                {
                    _path.StopMove();
                    _path.enabled = false;
                }
                if (_character != null)
                {
                    _character.Trigger(false, false, true);
                    _character.SetMoveInput(Vector3.zero);
                }
            }
            catch { /* 清理路径不能阻断回收 */ }
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            _active = false;
            if (_arts != null) _arts.Release();
            _arts = null;
            if (_body != null) Destroy(_body.gameObject);
            _body = null;
            _owner = null;
            if (_preset != null) Destroy(_preset);
            _preset = null;
            // CharacterMainControl.OnDestroy 负责 DestroyTree，星阙也在这棵临时物品树中。
        }
    }
}
