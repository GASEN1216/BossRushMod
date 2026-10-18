using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>甩锅许可证：只有主玩家佩戴时监听伤害，只有一枚待爆炸弹存在时创建逐帧驱动。</summary>
    internal static class PassTheBombRuntime
    {
        private static readonly PassTheBombRules rules = new PassTheBombRules(
            PassTheBombConfig.FuseSeconds, PassTheBombConfig.CooldownSeconds);
        private static bool subscribed, hurtSubscribed, levelInitializing;
        private static CharacterMainControl contextPlayer, owner;
        private static Health carrier;
        private static PassTheBombDriver driver;
        private static PassTheBombFx fx;
        private static Vector3 lastPosition;
        private static float markerHeight;
        private static int generation;
        private static float pauseOffset, lastTickTime;

        internal static bool HasPendingBomb { get { return rules.IsArmed; } }
        internal static int CarrierId { get { return rules.CarrierId; } }
        internal static float RemainingSeconds { get { return rules.Remaining(Clock); } }
        internal static float NextTriggerAt { get { return rules.NextTriggerAt; } }
        private static float Clock { get { return Time.time - pauseOffset; } }

        internal static void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            levelInitializing = false;
            CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += OnHoldChanged;
            CharacterMainControl.OnMainCharacterSlotContentChangedEvent += OnSlotChanged;
            Health.OnDead += OnAnyDead;
            SceneLoader.onStartedLoadingScene += OnSceneLoadingStarted;
            SceneLoader.onFinishedLoadingScene += OnSceneLoadingFinished;
            LevelManager.OnLevelBeginInitializing += OnLevelBeginInitializing;
            LevelManager.OnAfterLevelInitialized += OnAfterLevelInitialized;
            RefreshContext();
        }

        internal static void Unsubscribe()
        {
            if (subscribed)
            {
                subscribed = false;
                CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= OnHoldChanged;
                CharacterMainControl.OnMainCharacterSlotContentChangedEvent -= OnSlotChanged;
                Health.OnDead -= OnAnyDead;
                SceneLoader.onStartedLoadingScene -= OnSceneLoadingStarted;
                SceneLoader.onFinishedLoadingScene -= OnSceneLoadingFinished;
                LevelManager.OnLevelBeginInitializing -= OnLevelBeginInitializing;
                LevelManager.OnAfterLevelInitialized -= OnAfterLevelInitialized;
            }
            ResetStaticCaches();
        }

        internal static void ResetStaticCaches()
        {
            Deactivate();
            rules.ResetSession();
            contextPlayer = null;
            pauseOffset = 0f;
        }

        internal static void SetupForScene()
        {
            ResetStaticCaches();
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }

        private static bool IsUsableOwner(CharacterMainControl player)
        {
            return subscribed && !levelInitializing && !SceneLoader.IsSceneLoading
                && player != null && player.IsMainCharacter && player.Health != null && !player.Health.IsDead
                && LevelManager.Instance != null && !LevelManager.Instance.IsBaseLevel
                && NewWeaponEquipState.IsTotemEquipped(BossRushItemIds.PassTheBomb);
        }

        private static void RefreshContext()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (!ReferenceEquals(contextPlayer, player))
                {
                    Deactivate();
                    rules.ResetSession();
                    pauseOffset = 0f;
                    contextPlayer = player;
                }
                if (!IsUsableOwner(player)) { Deactivate(); return; }
                owner = player;
                if (!hurtSubscribed)
                {
                    Health.OnHurt += OnHurt;
                    hurtSubscribed = true;
                }
            }
            catch (Exception e)
            {
                Deactivate();
                ModBehaviour.DevLog(PassTheBombConfig.LogPrefix + " 佩戴绑定失败: " + e.Message);
            }
        }

        private static void Deactivate()
        {
            if (hurtSubscribed)
            {
                Health.OnHurt -= OnHurt;
                hurtSubscribed = false;
            }
            generation++;
            ClearBomb();
            owner = null;
        }

        private static void OnSlotChanged(CharacterMainControl player, Slot slot)
        {
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }

        private static void OnHoldChanged(CharacterMainControl player, DuckovItemAgent agent)
        {
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }

        private static void OnSceneLoadingStarted(SceneLoadingContext context) { ResetStaticCaches(); }
        private static void OnSceneLoadingFinished(SceneLoadingContext context)
        {
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }
        private static void OnLevelBeginInitializing()
        {
            levelInitializing = true;
            ResetStaticCaches();
        }
        private static void OnAfterLevelInitialized()
        {
            levelInitializing = false;
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }

        private static void OnAnyDead(Health health, DamageInfo info)
        {
            if (health == null) return;
            if (health.IsMainCharacterHealth) { ResetStaticCaches(); return; }
            // 官方死亡先于 OnHurt；这里只记录落点，不在死亡回调里引爆或派发新伤害。
            if (ReferenceEquals(health, carrier)) DetachDeadCarrier();
        }

        private static void DetachDeadCarrier()
        {
            if (carrier != null) lastPosition = carrier.transform.position;
            carrier = null;
            markerHeight = 0.75f;
        }

        private static bool IsEnemy(Health health, CharacterMainControl player)
        {
            if (health == null || health.IsMainCharacterHealth || PetNestCompanionAgent.IsCompanionHealth(health)) return false;
            CharacterMainControl character = health.TryGetCharacter();
            return character != null && !ReferenceEquals(character, player) && Team.IsEnemy(player.Team, character.Team);
        }

        private static void OnHurt(Health target, DamageInfo info)
        {
            // 持续伤害、反伤与本炸弹永不传递。无关攻击以 O(1) 身份比较直接返回。
            if (info.isFromBuffOrEffect || !(info.finalDamage > 0f) || info.fromCharacter == null || target == null) return;
            if (rules.IsArmed)
            {
                if (carrier == null || carrier.IsDead || !ReferenceEquals(info.fromCharacter.Health, carrier)) return;
            }
            else if (!ReferenceEquals(info.fromCharacter, owner)) return;
            try
            {
                if (!ReferenceEquals(owner, CharacterMainControl.Main) || !IsUsableOwner(owner)) { Deactivate(); return; }
                if (BossRushUI.IsGamePaused() || target.TryGetCharacter() == null) return;
                if (rules.IsArmed)
                {
                    if (!rules.TryTransfer(carrier.GetInstanceID(), target.GetInstanceID(), Clock)) return;
                    AttachCarrier(target);
                    // 移动同一个标记；接力不销毁重建画布、材质或贴图，也不更改引信。
                    if (fx != null) fx.ShowTransfer();
                }
                else
                {
                    if (!IsEnemy(target, owner) || !rules.TryArm(target.GetInstanceID(), Clock)) return;
                    AttachCarrier(target);
                    GameObject go = new GameObject("PassTheBomb_Runtime");
                    driver = go.AddComponent<PassTheBombDriver>();
                    lastTickTime = Time.time;
                    fx = PassTheBombFx.Create(go.transform);
                    // 无法显示头顶警告时不留下不可见炸弹。
                    if (fx == null) { ClearBomb(); return; }
                }
                UpdateVisual();
            }
            catch (Exception e)
            {
                ClearBomb();
                ModBehaviour.DevLog(PassTheBombConfig.LogPrefix + " 接力处理失败: " + e.Message);
            }
        }

        private static void AttachCarrier(Health target)
        {
            carrier = target;
            lastPosition = target.transform.position;
            markerHeight = Mathf.Max(1f, target.healthBarHeight) + 0.45f;
            if (target.IsDead) DetachDeadCarrier();
        }

        private static void UpdateVisual()
        {
            if (fx != null) fx.SetState(lastPosition + Vector3.up * markerHeight,
                rules.Remaining(Clock), carrier != null && carrier.IsMainCharacterHealth);
        }

        internal static void Tick(PassTheBombDriver source)
        {
            if (!ReferenceEquals(source, driver)) return;
            try
            {
                if (!ReferenceEquals(owner, CharacterMainControl.Main) || !IsUsableOwner(owner)) { Deactivate(); return; }
                float now = Time.time;
                float elapsed = Mathf.Max(0f, now - lastTickTime);
                lastTickTime = now;
                // 兼容仅设置 Paused、没有停 Time.time 的暂停界面；既不引爆也不吃掉引信。
                if (BossRushUI.IsGamePaused()) { pauseOffset += elapsed; return; }
                if (carrier != null)
                {
                    if (carrier.IsDead) DetachDeadCarrier();
                    else lastPosition = carrier.transform.position;
                }
                UpdateVisual();
                if (!rules.IsDue(Clock)) return;
                Vector3 center = lastPosition;
                Health holder = carrier;
                CharacterMainControl blastOwner = owner;
                int blastGeneration = generation;
                // 先摘除活动炸弹再结算，嵌套伤害与死亡不能重复引爆或回传本枚炸弹。
                ClearBomb();
                PassTheBombFx.PlayExplosion(center, PassTheBombConfig.BlastRadius);
                Detonate(center, holder, blastOwner, blastGeneration);
            }
            catch (Exception e)
            {
                ClearBomb();
                ModBehaviour.DevLog(PassTheBombConfig.LogPrefix + " 炸弹结算失败: " + e.Message);
            }
        }

        private static bool IsCurrentExplosion(CharacterMainControl blastOwner, int blastGeneration)
        {
            return blastGeneration == generation && ReferenceEquals(owner, blastOwner)
                && ReferenceEquals(blastOwner, CharacterMainControl.Main) && IsUsableOwner(blastOwner);
        }

        private static void Detonate(Vector3 center, Health holder, CharacterMainControl blastOwner, int blastGeneration)
        {
            // 唯一一次范围扫描发生在引爆；局部快照不受其它系统的嵌套死亡回调改写。
            Collider[] hits = Physics.OverlapSphere(center + Vector3.up * 0.5f,
                PassTheBombConfig.BlastRadius, GameplayDataSettings.Layers.damageReceiverLayerMask,
                QueryTriggerInteraction.Collide);
            HashSet<Health> damaged = new HashSet<Health>();
            if (holder != null && !holder.IsDead && IsCurrentExplosion(blastOwner, blastGeneration))
            {
                damaged.Add(holder);
                Hurt(holder, blastOwner, center, holder.transform.position + Vector3.up * 0.5f);
            }
            int wallMask = GameplayDataSettings.Layers.wallLayerMask | GameplayDataSettings.Layers.groundLayerMask;
            Vector3 from = center + Vector3.up * 0.5f;
            for (int i = 0; i < hits.Length; i++)
            {
                if (!IsCurrentExplosion(blastOwner, blastGeneration)) break;
                Collider hit = hits[i];
                DamageReceiver receiver = hit != null ? hit.GetComponent<DamageReceiver>() : null;
                Health health = receiver != null ? receiver.health : null;
                if (health == null || health.IsDead || damaged.Contains(health)) continue;
                // 玩家会被波及；同伴和中立者仅在自己接到炸弹时受持有者伤害。
                if (!health.IsMainCharacterHealth && !IsEnemy(health, blastOwner)) continue;
                Vector3 point = hit.bounds.center;
                if (Physics.Linecast(from, point, wallMask, QueryTriggerInteraction.Ignore)) continue;
                damaged.Add(health);
                Hurt(health, blastOwner, center, point);
            }
        }

        private static void Hurt(Health target, CharacterMainControl blastOwner, Vector3 center, Vector3 point)
        {
            try
            {
                DamageInfo damage = new DamageInfo(blastOwner);
                damage.damageValue = target.IsMainCharacterHealth ? PassTheBombConfig.PlayerBlastDamage : PassTheBombConfig.BlastDamage;
                damage.damageType = DamageTypes.normal;
                damage.isExplosion = true;
                damage.isFromBuffOrEffect = true;
                // 玩家装备效果不冒充直接武器命中，遵循共享伤害归因契约。
                damage.fromWeaponItemID = 0;
                damage.damagePoint = point;
                damage.damageNormal = (point - center).normalized;
                target.Hurt(damage);
            }
            catch (Exception e) { ModBehaviour.DevLog(PassTheBombConfig.LogPrefix + " 单目标爆炸失败: " + e.Message); }
        }

        private static void ClearBomb()
        {
            rules.Complete();
            carrier = null;
            fx = null;
            PassTheBombDriver old = driver;
            driver = null;
            if (old != null)
            {
                old.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(old.gameObject);
            }
        }

        internal static void OnDriverDestroyed(PassTheBombDriver source)
        {
            if (!ReferenceEquals(source, driver)) return;
            generation++;
            rules.Complete();
            driver = null;
            carrier = null;
            fx = null;
        }
    }

    internal sealed class PassTheBombDriver : MonoBehaviour
    {
        private void Update() { PassTheBombRuntime.Tick(this); }
        private void OnDestroy() { PassTheBombRuntime.OnDriverDestroyed(this); }
    }
}
