using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>本场冠军拥有的临时持棍小弟；不进入波次、随机奖励或存档管线。</summary>
    internal sealed class SandstormChampionMinions
    {
        internal const int MaxMinions = 3;
        internal const float WaveLifetime = 9f;
        private readonly List<SandstormChampionMinionMarker> _minions = new List<SandstormChampionMinionMarker>(MaxMinions);
        private CharacterMainControl _boss;
        private SandstormChampionController _controller;
        private ModBehaviour _owner;
        private int _sceneHandle;
        private int _generation;
        private bool _stopped;
        private bool _spawning;
        private int _pendingCreates;
        private float _expiresAt;

        internal void Initialize(CharacterMainControl boss, SandstormChampionController controller, ModBehaviour owner)
        {
            Shutdown();
            _boss = boss;
            _controller = controller;
            _owner = owner;
            _sceneHandle = SceneManager.GetActiveScene().handle;
            _stopped = false;
            LocalizationHelper.InjectLocalization(SandstormChampionMinionMarker.NameKey,
                L10n.T("沙暴棍卫", "Sandstorm Staff Guard"));
        }

        internal bool HasActiveMinions
        {
            get
            {
                Prune();
                return IsBattleCurrent && Time.time < _expiresAt && (_spawning || _minions.Count > 0);
            }
        }

        internal bool CanSummon { get { return IsBattleCurrent && _pendingCreates == 0 && !HasActiveMinions; } }

        internal bool IsBattleCurrent
        {
            get
            {
                return !_stopped && _owner != null
                    && _boss != null && _controller != null && _controller.IsFighting
                    && !SceneLoader.IsSceneLoading && SceneManager.GetActiveScene().handle == _sceneHandle;
            }
        }

        internal SandstormChampionController Controller { get { return _controller; } }

        internal bool IsWaveCurrent(int generation)
        {
            return generation == _generation && IsBattleCurrent && Time.time < _expiresAt;
        }

        internal void SummonWave(int phase)
        {
            if (!CanSummon) return;
            DismissWave();
            _expiresAt = Time.time + WaveLifetime;
            _spawning = true;
            SpawnWaveAsync(Mathf.Clamp(phase, 1, 3), _generation).Forget();
        }

        private async UniTask SpawnWaveAsync(int phase, int generation)
        {
            int count = phase >= 3 ? MaxMinions : 2;
            try
            {
                for (int i = 0; i < count && IsWaveCurrent(generation); i++)
                {
                    // 即使官方创建恰好同步完成，也保证同一帧最多准备一名小弟。
                    await UniTask.Yield();
                    if (!IsWaveCurrent(generation)) break;
                    Prune();
                    if (_minions.Count >= MaxMinions) break;
                    Vector3 position;
                    if (!TryFindSpawnPosition(i, count, out position)) continue;
                    await SpawnOneAsync(position, phase, generation);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "小弟召唤中断: " + e.Message);
            }
            finally
            {
                if (generation == _generation) _spawning = false;
            }
        }

        private bool TryFindSpawnPosition(int index, int count, out Vector3 position)
        {
            position = Vector3.zero;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || _boss == null) return false;
            Vector3 center = player.transform.position;
            Vector3 towardsBoss = _boss.transform.position - center;
            towardsBoss.y = 0f;
            if (towardsBoss.sqrMagnitude < 0.1f) towardsBoss = Vector3.forward;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float angle = (index - (count - 1) * 0.5f) * 48f + attempt * 60f;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * towardsBoss.normalized;
                Vector3 candidate = center + direction * (5.2f + (attempt % 2) * 1.2f);
                Vector3 reachable;
                if (!SpawnPositionHelper.TryResolveReachableFrom(candidate, center, 1.2f,
                    SpawnPositionHelper.DefaultLiftOffset, out reachable)) continue;
                Vector3 distance = reachable - center;
                distance.y = 0f;
                if (distance.sqrMagnitude < 16f) continue;
                position = reachable;
                return true;
            }
            return false;
        }

        private async UniTask SpawnOneAsync(Vector3 position, int phase, int generation)
        {
            CharacterRandomPreset preset = null;
            CharacterMainControl character = null;
            SandstormChampionMinionMarker marker = null;
            Item staff = null;
            bool activated = false;
            _pendingCreates++;
            try
            {
                if (!IsWaveCurrent(generation) || _boss.characterPreset == null) return;
                // 身份与无掉落必须先于 await，官方创建窗口内也不能被当成借用的 Boss。
                preset = UnityEngine.Object.Instantiate(_boss.characterPreset);
                preset.name = SandstormChampionMinionMarker.PresetName;
                preset.nameKey = SandstormChampionMinionMarker.NameKey;
                preset.isBoss = false;
                preset.showName = false;
                preset.showHealthBar = true;
                if (BossRushEagerReflectionCache.CharacterRandomPreset_CharacterIconType != null)
                    BossRushEagerReflectionCache.CharacterRandomPreset_CharacterIconType.SetValue(preset, CharacterIconTypes.none);
                preset.dropBoxOnDead = false;
                preset.hasSoul = false;
                preset.exp = 0;
                preset.hasCashChance = 0f;
                preset.hasSkill = false;
                preset.itemSkillChance = 0f;
                preset.specialAttachmentBases = new List<AISpecialAttachmentBase>();
                preset.team = Teams.wolf;
                preset.canDieIfNotRaidMap = true;
                preset.setActiveByPlayerDistance = false;
                preset.forceTracePlayerDistance = 0f;
                preset.health = 150f + 35f * phase;
                preset.aiCombatFactor = 1f;
                preset.moveSpeedFactor = 1f;
                preset.setMeleeDamageMultiplier = true;
                preset.meleeDamageMultiplier = 0.4f * Mathf.Max(0.01f, _controller.DamageScale);
                preset.gunCritRateGain = 0f;
                Vector3 staging = position + Vector3.down * 240f;
                character = await preset.CreateCharacterAsync(staging, Vector3.forward,
                    SceneManager.GetActiveScene().buildIndex, null, false);
                if (character == null || character.Health == null || character.CharacterItem == null) return;
                character.gameObject.SetActive(false);
                character.dropBoxOnDead = false;
                character.Health.hasSoul = false;
                character.CharacterItem.SetInt("Exp", 0, true);
                if (!IsWaveCurrent(generation) || character.Health.IsDead) return;
                SetCharacterStat(character, "MaxHealth", 150f + 35f * phase);
                SetCharacterStat(character, "MeleeDamageMultiplier", 0.4f * Mathf.Max(0.01f, _controller.DamageScale));
                SetCharacterStat(character, "MeleeCritRateGain", 0f);
                SetCharacterStat(character, "WalkSpeed", SandstormChampionConfig.MinionWalkSpeed);
                SetCharacterStat(character, "TurnSpeed", 1200f);
                character.Health.SetHealth(character.Health.MaxHealth);

                // 官方行为树可以独立运行；停用整个 AI 子物体，再由根上的官方寻路器驾驶。
                SandstormChampionBoss.StopVanillaAI(character);
                if (character.CurrentAction != null) character.CurrentAction.StopAction();
                character.Trigger(false, false, true);
                character.ChangeHoldItem(null);
                DestroyGeneratedLoadout(character.CharacterItem);
                if (!Team.IsEnemy(Teams.player, character.Team)) character.SetTeam(Teams.wolf);
                character.isBossCharacter = false;
                character.gameObject.name = "BossRush_SandstormStaffGuard";
                character.transform.localScale = Vector3.one;

                marker = character.gameObject.AddComponent<SandstormChampionMinionMarker>();
                marker.Initialize(this, character, preset, generation);
                preset = null; // marker 独占克隆预设，在角色真正销毁时回收。
                BossRushDynamicItemRegistry.EnsureRegistered(BossRushItemIds.AstralStaff);
                staff = ItemAssetsCollection.InstantiateSync(BossRushItemIds.AstralStaff);
                if (staff == null || staff.TypeID != BossRushItemIds.AstralStaff
                    || !AstralStaffWeaponConfig.TryConfigure(staff)) return;
                Slot slot = character.CharacterItem.Slots.GetSlot("MeleeWeapon");
                if (slot == null) return;
                Item replaced;
                slot.Plug(staff, out replaced);
                if (slot.Content != staff) return;
                if (replaced != null) replaced.DestroyTree();
                character.ChangeHoldItem(staff);
                ItemAgent_MeleeWeapon melee = character.GetMeleeWeapon();
                if (melee == null || melee.Item != staff) return;
                AstralStaffWeaponConfig.PrepareSummonedMinionHoldAgentVisual(melee.gameObject, marker);
                staff = null; // 官方角色物品树负责销毁，绝不转移到地面。

                await UniTask.Yield();
                if (!IsWaveCurrent(generation) || character == null || character.Health.IsDead) return;
                Vector3 reachable;
                if (!SpawnPositionHelper.TryResolveReachableFrom(position, CharacterMainControl.Main.transform.position,
                    1.2f, SpawnPositionHelper.DefaultLiftOffset, out reachable)) return;
                // 官方入口同步 ECM2 物理位置，避免地下 staging 的旧位置在首帧把角色拉回。
                character.SetPosition(reachable);
                marker.Activate();
                _minions.Add(marker);
                character.gameObject.SetActive(true);
                SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(character);
                character.Health.showHealthBar = true;
                character.Health.RequestHealthBar();
                activated = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(SandstormChampionConfig.LogPrefix + "小弟创建失败: " + e.Message);
            }
            finally
            {
                _pendingCreates--;
                if (!activated)
                {
                    if (marker != null) marker.Despawn();
                    else if (character != null)
                    {
                        character.gameObject.SetActive(false);
                        UnityEngine.Object.Destroy(character.gameObject);
                    }
                    if (staff != null && staff.PluggedIntoSlot == null) staff.DestroyTree();
                    if (preset != null) UnityEngine.Object.Destroy(preset);
                }
            }
        }

        private static void SetCharacterStat(CharacterMainControl character, string key, float value)
        {
            Stat stat = character.CharacterItem.GetStat(key);
            if (stat != null) stat.BaseValue = value;
        }

        private static void DestroyGeneratedLoadout(Item characterItem)
        {
            if (characterItem.Slots != null)
            {
                foreach (Slot slot in characterItem.Slots)
                {
                    Item old = slot.Content;
                    if (old == null) continue;
                    old.Detach();
                    old.DestroyTree();
                }
            }
            if (characterItem.Inventory != null)
            {
                for (int i = characterItem.Inventory.Content.Count - 1; i >= 0; i--)
                {
                    Item old = characterItem.Inventory.Content[i];
                    if (old == null) continue;
                    old.Detach();
                    old.DestroyTree();
                }
            }
        }

        private void Prune()
        {
            for (int i = _minions.Count - 1; i >= 0; i--)
            {
                SandstormChampionMinionMarker marker = _minions[i];
                if (marker != null && marker.IsCombatActive) continue;
                if (marker != null) marker.Despawn();
                _minions.RemoveAt(i);
            }
        }

        internal void DismissWave()
        {
            _generation++;
            _spawning = false;
            _expiresAt = 0f;
            foreach (SandstormChampionMinionMarker marker in _minions)
                if (marker != null) marker.Despawn();
            _minions.Clear();
        }

        internal void Shutdown()
        {
            _stopped = true;
            DismissWave();
            _boss = null;
            _controller = null;
            _owner = null;
        }
    }
}
