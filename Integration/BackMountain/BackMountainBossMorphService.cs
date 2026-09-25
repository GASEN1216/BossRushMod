// 后山三种收成：30 秒对应 Boss 形态，共用一份 owner、计时与清理。
// 仅替换模型和临时战斗属性；玩家生命、真实 Item 树与通行碰撞体保持原样，不写档。
using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal struct BackMountainBossMorphProfile
    {
        internal string NameKey;
        internal int Helm, Armor, Weapon;
        internal float Scale, GunBonus, MeleeBonus, SpeedBonus, Range, Damage, Cooldown, MinDot;
        internal bool Fire;
    }

    internal static class BackMountainBossMorphService
    {
        internal const float DurationSeconds = 30f;
        internal static bool IsActive
        {
            get
            {
                CharacterMainControl main = CharacterMainControl.Main;
                BackMountainBossMorphRuntime runtime = main != null ? main.GetComponent<BackMountainBossMorphRuntime>() : null;
                return runtime != null && runtime.Active;
            }
        }
        internal static bool CanUse
        {
            get
            {
                CharacterMainControl main = CharacterMainControl.Main;
                return main != null && main.Health != null && !main.Health.IsDead
                    && !SceneLoader.IsSceneLoading && !IsActive;
            }
        }
        internal static bool TryGetProfile(int fruitTypeId, out BackMountainBossMorphProfile profile)
        {
            profile = new BackMountainBossMorphProfile();
            profile.Scale = 1f;
            switch (fruitTypeId)
            {
                case BossRushItemIds.DragonFruit:
                    profile.NameKey = DragonDescendantConfig.BasePresetNameKey;
                    profile.Helm = DragonDescendantConfig.DRAGON_HELM_TYPE_ID;
                    profile.Armor = DragonDescendantConfig.DRAGON_ARMOR_TYPE_ID;
                    profile.GunBonus = 0.30f; profile.MeleeBonus = 0.30f;
                    profile.Range = 8f; profile.Damage = 24f; profile.Cooldown = 0.8f;
                    profile.MinDot = 0.82f; profile.Fire = true;
                    return true;
                case BossRushItemIds.EmberChili:
                    // DragonKingBoss.FindDragonKingBasePreset 同样使用龙裔基础预制体。
                    profile.NameKey = DragonDescendantConfig.BasePresetNameKey;
                    profile.Helm = DragonKingConfig.DRAGON_KING_HELM_TYPE_ID;
                    profile.Armor = DragonKingConfig.DRAGON_KING_ARMOR_TYPE_ID;
                    profile.GunBonus = 0.15f; profile.MeleeBonus = 0.50f;
                    profile.Range = 6f; profile.Damage = 36f; profile.Cooldown = 1.2f;
                    profile.MinDot = -1f; profile.Fire = true;
                    return true;
                case BossRushItemIds.PhantomMushroom:
                    profile.NameKey = PhantomWitchConfig.BasePresetNameKey;
                    profile.Weapon = PhantomWitchConfig.ReservedScytheTypeId;
                    profile.Scale = PhantomWitchConfig.BossModelScale;
                    profile.MeleeBonus = 0.40f; profile.SpeedBonus = 0.20f;
                    profile.Range = 5f; profile.Damage = 32f; profile.Cooldown = 0.65f;
                    profile.MinDot = 0.5f;
                    return true;
                default: return false;
            }
        }
        internal static bool TryBegin(int fruitTypeId, ModBehaviour owner)
        {
            BackMountainBossMorphProfile profile;
            if (!CanUse || !TryGetProfile(fruitTypeId, out profile)) return false;
            try
            {
                if (owner == null || !owner.IsBackMountainConfiguredEnabled()) return false;
                CharacterRandomPreset[] all = ObjectCache.GetCharacterPresets();
                if (all == null) return false;
                CharacterModel model = null;
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].nameKey == profile.NameKey)
                    { model = all[i].CharacterModel; break; }
                if (model == null) return false;
                CharacterMainControl main = CharacterMainControl.Main;
                BackMountainBossMorphRuntime runtime = main.gameObject.AddComponent<BackMountainBossMorphRuntime>();
                if (!runtime.Begin(main, model, profile, DurationSeconds, owner))
                { runtime.Restore(); return false; }
                // 提示失败不能把已成功开始的变身当作失败，避免免费使用。
                try { owner.ShowMessage(L10n.T("Boss 形态：30 秒，攻击触发专属能力", "Boss form: 30s. Attack to trigger its ability.")); }
                catch (Exception) { }
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "果实变身失败: " + e.Message);
                return false;
            }
        }
        internal static void Clear()
        {
            CharacterMainControl main = CharacterMainControl.Main;
            BackMountainBossMorphRuntime runtime = main != null ? main.GetComponent<BackMountainBossMorphRuntime>() : null;
            if (runtime != null) runtime.Restore();
        }
    }

    /// <summary>只在变身期间存在。真实射击/近战事件排队，下一次 Update 结算，避免嵌套伤害链。</summary>
    internal sealed class BackMountainBossMorphRuntime : MonoBehaviour
    {
        private readonly List<ZombieModeAttributeModifierRecord> _modifiers = new List<ZombieModeAttributeModifierRecord>();
        private readonly List<GameObject> _costume = new List<GameObject>();
        private readonly List<Renderer> _hiddenEquipment = new List<Renderer>();
        private readonly Dictionary<DuckovItemAgent, HandheldAnimationType> _handAnimations = new Dictionary<DuckovItemAgent, HandheldAnimationType>();
        private readonly HashSet<Health> _damaged = new HashSet<Health>();
        private readonly Collider[] _hits = new Collider[32];
        private CharacterMainControl _character;
        private ModBehaviour _owner;
        private CharacterModel _model;
        private BackMountainBossMorphProfile _profile;
        private CapsuleCollider _bodyCollider, _damageCollider;
        private float _bodyRadius, _bodyHeight, _damageRadius, _damageHeight;
        private Vector3 _bodyCenter, _damageCenter;
        private bool _bodyEnabled, _damageEnabled;
        private float _remaining;
        private float _nextAbility;
        private bool _active, _subscribed, _pendingAbility, _refreshEquipment, _cleaned;
        private int _scene, _damageMask, _wallMask;
        internal bool Active { get { return _active; } }

        internal bool Begin(CharacterMainControl character, CharacterModel prefab,
            BackMountainBossMorphProfile profile, float duration, ModBehaviour owner)
        {
            if (_active || character == null || prefab == null) return false;
            try
            {
                // 必需外观先验证；缺包时不做半套变身，也不消费果实。
                ItemAgent helm = profile.Helm != 0 ? AgentPrefab(profile.Helm, CharacterEquipmentController.equipmentModelHash) : null;
                ItemAgent armor = profile.Armor != 0 ? AgentPrefab(profile.Armor, CharacterEquipmentController.equipmentModelHash) : null;
                ItemAgent weapon = profile.Weapon != 0 ? AgentPrefab(profile.Weapon, "Handheld".GetHashCode()) : null;
                if ((profile.Helm != 0 && helm == null) || (profile.Armor != 0 && armor == null)
                    || (profile.Weapon != 0 && weapon == null)) return false;
                _owner = owner;
                _character = character;
                _profile = profile;
                _model = UnityEngine.Object.Instantiate(prefab);
                if (_model == null || (helm != null && _model.HelmatSocket == null)
                    || (armor != null && _model.ArmorSocket == null)
                    || (weapon != null && _model.RightHandSocket == null)) return false;
                // 女巫只放大模型。官方 SetCharacterModel 会改胶囊，立即还原避免通行范围变大。
                CaptureCollision();
                _model.transform.localScale *= profile.Scale;
                _active = true;
                character.SetCharacterModel(_model);
                RestoreCollision();
                HidePlayerEquipment();
                if (helm != null) AttachCostume(helm, _model.HelmatSocket, false);
                if (armor != null) AttachCostume(armor, _model.ArmorSocket, false);
                if (weapon != null) AttachCostume(weapon, _model.RightHandSocket, true);
                if (!AddBonus("GunDamageMultiplier", profile.GunBonus)
                    || !AddBonus("MeleeDamageMultiplier", profile.MeleeBonus)
                    || !AddBonus("RunSpeed", profile.SpeedBonus)
                    || !AddBonus("WalkSpeed", profile.SpeedBonus)
                    || (profile.Fire && !RuntimeStatModifierTracker.TryAdd(character, "ElementFactor_Fire", -1f,
                        this, _modifiers, "BossFruit", ItemStatsSystem.Stats.ModifierType.PercentageMultiply)))
                    throw new InvalidOperationException("玩家战斗属性不完整");
                _remaining = Mathf.Max(0.1f, duration);
                _scene = SceneManager.GetActiveScene().handle;
                _damageMask = LayerMask.GetMask("Character", "DamageReceiver");
                _wallMask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
                _character.OnShootEvent += OnAttack;
                _character.OnAttackEvent += OnAttack;
                _character.OnHoldAgentChanged += OnHoldChanged;
                CharacterMainControl.OnMainCharacterSlotContentChangedEvent += OnEquipmentChanged;
                _subscribed = true;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "应用 Boss 形态失败: " + e.Message);
                Restore();
                return false;
            }
        }

        private bool AddBonus(string stat, float value)
        {
            return value == 0f || RuntimeStatModifierTracker.TryAdd(_character, stat, value, this, _modifiers, "BossFruit");
        }
        private void CaptureCollision()
        {
            _bodyCollider = _character.GetComponent<CapsuleCollider>();
            if (_bodyCollider != null)
            {
                _bodyRadius = _bodyCollider.radius; _bodyHeight = _bodyCollider.height;
                _bodyCenter = _bodyCollider.center; _bodyEnabled = _bodyCollider.enabled;
            }
            _damageCollider = _character.mainDamageReceiver != null
                ? _character.mainDamageReceiver.GetComponent<CapsuleCollider>() : null;
            if (_damageCollider != null)
            {
                _damageRadius = _damageCollider.radius; _damageHeight = _damageCollider.height;
                _damageCenter = _damageCollider.center; _damageEnabled = _damageCollider.enabled;
            }
        }
        private void RestoreCollision()
        {
            if (_bodyCollider != null)
            {
                _bodyCollider.radius = _bodyRadius; _bodyCollider.height = _bodyHeight;
                _bodyCollider.center = _bodyCenter; _bodyCollider.enabled = _bodyEnabled;
            }
            if (_damageCollider != null)
            {
                _damageCollider.radius = _damageRadius; _damageCollider.height = _damageHeight;
                _damageCollider.center = _damageCenter; _damageCollider.enabled = _damageEnabled;
            }
        }
        private static ItemAgent AgentPrefab(int typeId, int kind)
        {
            Item item = ItemAssetsCollection.GetPrefab(typeId);
            return item != null ? item.AgentUtilities.GetPrefab(kind) : null;
        }

        private void AttachCostume(ItemAgent prefab, Transform socket, bool weapon)
        {
            // 在 inactive 根下复制，先关逻辑与碰撞再显现；永不绑定 Item/Holder，装备词条不会流入玩家。
            GameObject staging = new GameObject("BossFruitCostumeStaging");
            staging.SetActive(false);
            try
            {
                ItemAgent visual = UnityEngine.Object.Instantiate(prefab, staging.transform);
                _costume.Add(visual.gameObject);
                visual.gameObject.hideFlags = HideFlags.None;
                MonoBehaviour[] scripts = visual.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < scripts.Length; i++) scripts[i].enabled = false;
                Collider[] colliders = visual.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
                if (weapon) PhantomWitchScytheWeaponConfig.PrepareRuntimeHoldAgentVisual(visual.gameObject);
                visual.transform.SetParent(socket, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.gameObject.SetActive(true);
            }
            finally { Destroy(staging); }
        }

        private void HidePlayerEquipment()
        {
            if (_model == null) return;
            ItemAgent[] agents = _model.GetComponentsInChildren<ItemAgent>(true);
            for (int i = 0; i < agents.Length; i++)
            {
                ItemAgent agent = agents[i];
                if (agent == null || _costume.Contains(agent.gameObject)) continue;
                // 女巫的真武器仍执行射击/近战，只隐藏 renderer，不能停用武器 GameObject。
                if (agent.AgentType != ItemAgent.AgentTypes.equipment && _profile.Weapon == 0) continue;
                DuckovItemAgent handheld = agent as DuckovItemAgent;
                if (_profile.Weapon != 0 && handheld != null && !_handAnimations.ContainsKey(handheld))
                {
                    _handAnimations.Add(handheld, handheld.handAnimationType);
                    handheld.handAnimationType = HandheldAnimationType.meleeWeapon;
                }
                Renderer[] renderers = agent.GetComponentsInChildren<Renderer>(true);
                for (int j = 0; j < renderers.Length; j++)
                {
                    Renderer renderer = renderers[j];
                    if (renderer == null || renderer.forceRenderingOff || _hiddenEquipment.Contains(renderer)) continue;
                    _hiddenEquipment.Add(renderer);
                    renderer.forceRenderingOff = true;
                }
            }
        }
        private void OnEquipmentChanged(CharacterMainControl character, ItemStatsSystem.Items.Slot slot)
        {
            if (_active && character == _character) _refreshEquipment = true;
        }
        private void OnHoldChanged(DuckovItemAgent agent) { if (_active) _refreshEquipment = true; }
        private void OnAttack(DuckovItemAgent agent)
        {
            if (_active && !BossRushUI.IsGamePaused() && Time.time >= _nextAbility)
            {
                _pendingAbility = true;
                _nextAbility = Time.time + _profile.Cooldown;
            }
        }

        private void Update()
        {
            if (!_active) return;
            if (_character == null || _character != CharacterMainControl.Main || _character.Health == null
                || _character.Health.IsDead || _character.characterModel != _model || SceneLoader.IsSceneLoading
                || SceneManager.GetActiveScene().handle != _scene
                || _owner == null || !_owner.IsBackMountainConfiguredEnabled())
            { Restore(); return; }
            if (BossRushUI.IsGamePaused()) return;
            if (_refreshEquipment) { _refreshEquipment = false; HidePlayerEquipment(); }
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f) { Restore(); return; }
            if (_pendingAbility)
            {
                _pendingAbility = false;
                try { ReleaseAbility(); }
                catch (Exception) { }
            }
        }

        private void ReleaseAbility()
        {
            Vector3 origin = _character.transform.position + Vector3.up * 0.8f;
            Vector3 direction = _character.CurrentAimDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = _character.transform.forward;
            direction.Normalize();
            int count = Physics.OverlapSphereNonAlloc(origin, _profile.Range, _hits, _damageMask);
            _damaged.Clear();
            for (int i = 0; i < count; i++)
            {
                if (!_active || _character == null || _character.Health == null || _character.Health.IsDead) break;
                Collider hit = _hits[i]; _hits[i] = null;
                CharacterMainControl target = hit != null ? hit.GetComponentInParent<CharacterMainControl>() : null;
                if (target == null || target == _character || target.Health == null || target.Health.IsDead
                    || !Team.IsEnemy(_character.Team, target.Team)) continue;
                Vector3 point = target.transform.position + Vector3.up * 0.8f;
                Vector3 offset = point - origin; offset.y = 0f;
                if (offset.sqrMagnitude > _profile.Range * _profile.Range
                    || Vector3.Dot(direction, offset.normalized) < _profile.MinDot
                    || Physics.Linecast(origin, point, _wallMask, QueryTriggerInteraction.Ignore)
                    || !_damaged.Add(target.Health)) continue;
                DamageInfo info = new DamageInfo(_character);
                info.damageValue = _profile.Damage;
                info.damagePoint = point;
                info.isFromBuffOrEffect = true;
                info.fromWeaponItemID = 0;
                if (_profile.Fire) info.AddElementFactor(ElementTypes.fire, 1f);
                target.Health.Hurt(info);
                DragonKingFxShared.HitBurst(point, _profile.Fire ? new Color(1f, 0.38f, 0.08f) : new Color(0.5f, 0.7f, 1f));
            }
            if (_profile.Weapon != 0)
                PhantomWitchScytheSwingFx.PlayAt(origin, Quaternion.LookRotation(direction), _profile.Range / PhantomWitchScytheConfig.BaseAttackRange, false);
            else
                DragonKingFxShared.HitBurst(origin + direction * 1.2f, new Color(1f, 0.46f, 0.12f));
        }

        internal void Restore()
        {
            if (_cleaned) return;
            _cleaned = true;
            bool changedModel = _active;
            _active = false;
            _pendingAbility = false;
            if (_subscribed)
            {
                if (_character != null)
                {
                    _character.OnShootEvent -= OnAttack;
                    _character.OnAttackEvent -= OnAttack;
                    _character.OnHoldAgentChanged -= OnHoldChanged;
                }
                CharacterMainControl.OnMainCharacterSlotContentChangedEvent -= OnEquipmentChanged;
                _subscribed = false;
            }
            RuntimeStatModifierTracker.RemoveAll(_modifiers, "BossFruit");
            foreach (KeyValuePair<DuckovItemAgent, HandheldAnimationType> saved in _handAnimations)
                if (saved.Key != null) saved.Key.handAnimationType = saved.Value;
            _handAnimations.Clear();
            for (int i = 0; i < _hiddenEquipment.Count; i++)
                if (_hiddenEquipment[i] != null) _hiddenEquipment[i].forceRenderingOff = false;
            _hiddenEquipment.Clear();
            for (int i = 0; i < _costume.Count; i++) if (_costume[i] != null) Destroy(_costume[i]);
            _costume.Clear();
            try
            {
                if (changedModel && _character != null && _character == CharacterMainControl.Main
                    && _character.Health != null && _character.characterModel == _model)
                    _character.SetCharacterModel(null);
                else if (!changedModel && _model != null) Destroy(_model.gameObject);
            }
            catch (Exception e) { ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "恢复玩家形态失败: " + e.Message); }
            RestoreCollision();
            _model = null;
            _character = null;
            _owner = null;
            Destroy(this);
        }

        private void OnDestroy() { Restore(); }
    }
}
