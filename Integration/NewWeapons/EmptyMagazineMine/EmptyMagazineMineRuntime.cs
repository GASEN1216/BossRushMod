using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem.Items;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 空仓地雷盒：实际佩戴时接入主角事件，只有待爆地雷存在时才创建逐帧组件。
    /// 官方 Shoot 事件在 UseABullet 之后；成功开始 CA_Reload 就清次数，不等装填完成。
    /// </summary>
    internal static class EmptyMagazineMineRuntime
    {
        private static readonly EmptyMagazineMineRules rules = new EmptyMagazineMineRules(
            EmptyMagazineMineConfig.MinimumShots, EmptyMagazineMineConfig.CooldownSeconds);
        private static bool isSubscribed;
        private static bool levelInitializing;
        private static CharacterMainControl owner;
        private static ItemAgent_Gun observedGun;
        private static EmptyMagazineMineDriver pendingMine;
        private static EmptyMagazineMineFx pendingFx;
        private static float fuseElapsed;
        private static int generation;

        internal static int CurrentShotCount { get { return rules.ShotCount; } }
        internal static bool HasPendingMine { get { return pendingMine != null; } }

        internal static void Subscribe()
        {
            if (isSubscribed) return;
            isSubscribed = true;
            levelInitializing = false;
            CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += OnHoldChanged;
            CharacterMainControl.OnMainCharacterSlotContentChangedEvent += OnSlotChanged;
            ItemAgent_Gun.OnMainCharacterShootEvent += OnShoot;
            Health.OnDead += OnAnyDead;
            SceneLoader.onStartedLoadingScene += OnSceneLoadingStarted;
            LevelManager.OnLevelBeginInitializing += OnLevelBeginInitializing;
            LevelManager.OnAfterLevelInitialized += OnAfterLevelInitialized;
            RefreshContext();
        }

        internal static void Unsubscribe()
        {
            if (isSubscribed)
            {
                isSubscribed = false;
                CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= OnHoldChanged;
                CharacterMainControl.OnMainCharacterSlotContentChangedEvent -= OnSlotChanged;
                ItemAgent_Gun.OnMainCharacterShootEvent -= OnShoot;
                Health.OnDead -= OnAnyDead;
                SceneLoader.onStartedLoadingScene -= OnSceneLoadingStarted;
                LevelManager.OnLevelBeginInitializing -= OnLevelBeginInitializing;
                LevelManager.OnAfterLevelInitialized -= OnAfterLevelInitialized;
            }
            ResetStaticCaches();
        }

        internal static void ResetStaticCaches()
        {
            Deactivate();
            rules.ResetAll();
        }

        private static bool IsUsableOwner(CharacterMainControl player)
        {
            return isSubscribed && !levelInitializing && !SceneLoader.IsSceneLoading
                && player != null && player.IsMainCharacter
                && player.Health != null && !player.Health.IsDead
                && LevelManager.Instance != null && !LevelManager.Instance.IsBaseLevel
                && NewWeaponEquipState.IsTotemEquipped(BossRushItemIds.EmptyMagazineMine);
        }

        private static void RefreshContext()
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (!IsUsableOwner(player))
                {
                    Deactivate();
                    return;
                }
                if (!ReferenceEquals(owner, player))
                {
                    Deactivate();
                    owner = player;
                    owner.OnActionStartEvent += OnActionStarted;
                }
                ObserveGun(player.CurrentHoldItemAgent as ItemAgent_Gun);
            }
            catch (Exception e)
            {
                Deactivate();
                ModBehaviour.DevLog("[EmptyMagazineMine] 装备状态绑定失败: " + e.Message);
            }
        }

        private static void ObserveGun(ItemAgent_Gun gun)
        {
            if (ReferenceEquals(observedGun, gun)) return;
            if (!ReferenceEquals(observedGun, null)) observedGun.OnLoadedEvent -= OnLoaded;
            observedGun = gun;
            rules.ResetMagazine();
            if (!ReferenceEquals(observedGun, null)) observedGun.OnLoadedEvent += OnLoaded;
        }

        private static void Deactivate()
        {
            CancelMine();
            ObserveGun(null);
            if (!ReferenceEquals(owner, null)) owner.OnActionStartEvent -= OnActionStarted;
            owner = null;
            rules.ResetMagazine();
        }

        private static void OnSlotChanged(CharacterMainControl player, Slot slot)
        {
            // 显式置脏，不依赖同一多播事件上 NewWeaponEquipState 的订阅先后顺序。
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }

        private static void OnHoldChanged(CharacterMainControl player, DuckovItemAgent agent)
        {
            rules.ResetMagazine();
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }

        private static void OnActionStarted(CharacterActionBase action)
        {
            // OnActionStartEvent 只在 OnStart 成功之后发出，失败的换弹请求不会吃掉已有次数。
            if (action is CA_Reload) rules.ResetMagazine();
        }

        private static void OnLoaded()
        {
            rules.ResetMagazine();
        }

        private static void OnAnyDead(Health health, DamageInfo info)
        {
            if (health != null && health.IsMainCharacterHealth) ResetStaticCaches();
        }

        private static void OnLevelBeginInitializing()
        {
            levelInitializing = true;
            ResetStaticCaches();
        }

        private static void OnSceneLoadingStarted(SceneLoadingContext context)
        {
            // 官方先渐变黑幕再卸旧场景；不能等目标场景的 LevelBeginInitializing 才取消。
            // 不设置 levelInitializing：加载尝试结束但没进入目标场景时，仍应能在旧场景恢复。
            ResetStaticCaches();
        }

        private static void OnAfterLevelInitialized()
        {
            levelInitializing = false;
            NewWeaponEquipState.MarkDirty();
            RefreshContext();
        }

        private static void OnShoot(ItemAgent_Gun gun)
        {
            try
            {
                RefreshContext();
                if (owner == null || gun == null || !ReferenceEquals(gun, observedGun)
                    || !ReferenceEquals(gun.Holder, owner)) return;
                if (rules.ObserveShot(gun.GetInstanceID(), gun.BulletCount, Time.time, HasPendingMine))
                {
                    DeployMine();
                }
            }
            catch (Exception e)
            {
                rules.ResetMagazine();
                ModBehaviour.DevLog("[EmptyMagazineMine] 空仓事件失败: " + e.Message);
            }
        }

        private static void DeployMine()
        {
            if (pendingMine != null || owner == null) return;
            Vector3 position = owner.transform.position;
            int groundMask = GameplayDataSettings.Layers.groundLayerMask | GameplayDataSettings.Layers.wallLayerMask;
            RaycastHit floor;
            if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out floor, 3f, groundMask, QueryTriggerInteraction.Ignore))
            {
                position = floor.point;
            }
            GameObject go = new GameObject("EmptyMagazineMine_Runtime");
            go.transform.position = position;
            pendingMine = go.AddComponent<EmptyMagazineMineDriver>();
            fuseElapsed = 0f;
            pendingFx = EmptyMagazineMineFx.Create(position, owner.transform.eulerAngles.y, EmptyMagazineMineConfig.BlastRadius);
            if (pendingFx != null) pendingFx.transform.SetParent(go.transform, true);
        }

        internal static void Tick(EmptyMagazineMineDriver source)
        {
            if (!ReferenceEquals(source, pendingMine)) return;
            try
            {
                if (!ReferenceEquals(owner, CharacterMainControl.Main) || !IsUsableOwner(owner))
                {
                    Deactivate();
                    return;
                }
                if (BossRushUI.IsGamePaused()) return;
                fuseElapsed += Time.deltaTime;
                // 视觉对象或音频失效不能取消本次已放下的地雷，也不在逐帧路径刷日志。
                try { if (pendingFx != null) pendingFx.ShowFuse(fuseElapsed, EmptyMagazineMineConfig.FuseSeconds); }
                catch (Exception) { }
                if (fuseElapsed < EmptyMagazineMineConfig.FuseSeconds) return;

                Vector3 center = source.transform.position;
                CharacterMainControl blastOwner = owner;
                int blastGeneration = generation;
                // 先摘掉在飞实体，再派发伤害；死亡链若卸装备或换场景，就使后续目标作废。
                ClearMineObject();
                EmptyMagazineMineFx.PlayExplosion(center, EmptyMagazineMineConfig.BlastRadius);
                DamageEnemies(center, blastOwner, blastGeneration);
            }
            catch (Exception e)
            {
                CancelMine();
                ModBehaviour.DevLog("[EmptyMagazineMine] 地雷结算失败: " + e.Message);
            }
        }

        private static void DamageEnemies(Vector3 center, CharacterMainControl blastOwner, int blastGeneration)
        {
            // 一次引爆才扫描；使用本次局部快照，避免嵌套死亡回调改写全局爆炸缓冲。
            // 不使用固定长度 NonAlloc 数组，密集尸潮也不能因碰撞体截断而漏掉圈内敌人。
            Collider[] hits = Physics.OverlapSphere(center + Vector3.up * 0.2f,
                EmptyMagazineMineConfig.BlastRadius, GameplayDataSettings.Layers.damageReceiverLayerMask,
                QueryTriggerInteraction.Collide);
            HashSet<Health> damaged = new HashSet<Health>();
            int wallMask = GameplayDataSettings.Layers.wallLayerMask | GameplayDataSettings.Layers.groundLayerMask;
            Vector3 from = center + Vector3.up * 0.25f;
            for (int i = 0; i < hits.Length; i++)
            {
                if (blastGeneration != generation || !ReferenceEquals(owner, blastOwner) || !IsUsableOwner(blastOwner)) break;
                Collider hit = hits[i];
                DamageReceiver receiver = hit != null ? hit.GetComponent<DamageReceiver>() : null;
                Health health = receiver != null ? receiver.health : null;
                if (health == null || health.IsDead || health.IsMainCharacterHealth || damaged.Contains(health)) continue;
                if (PetNestCompanionAgent.IsCompanionHealth(health)) continue;
                CharacterMainControl enemy = health.TryGetCharacter();
                if (enemy == null || ReferenceEquals(enemy, blastOwner) || !Team.IsEnemy(blastOwner.Team, enemy.Team)) continue;
                Vector3 point = hit.bounds.center;
                if (Physics.Linecast(from, point, wallMask, QueryTriggerInteraction.Ignore)) continue;
                damaged.Add(health);

                DamageInfo damage = new DamageInfo(blastOwner);
                damage.damageValue = EmptyMagazineMineConfig.BlastDamage;
                damage.damageType = DamageTypes.normal;
                damage.isExplosion = true;
                damage.isFromBuffOrEffect = true;
                damage.fromWeaponItemID = 0;
                damage.damagePoint = point;
                damage.damageNormal = (point - center).normalized;
                try { receiver.Hurt(damage); }
                catch (Exception e) { ModBehaviour.DevLog("[EmptyMagazineMine] 单目标伤害失败: " + e.Message); }
            }
        }

        private static void CancelMine()
        {
            generation++;
            ClearMineObject();
        }

        private static void ClearMineObject()
        {
            EmptyMagazineMineDriver mine = pendingMine;
            pendingMine = null;
            pendingFx = null;
            fuseElapsed = 0f;
            if (mine != null)
            {
                mine.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(mine.gameObject);
            }
        }

        internal static void OnMineDestroyed(EmptyMagazineMineDriver source)
        {
            if (!ReferenceEquals(source, pendingMine)) return;
            pendingMine = null;
            pendingFx = null;
            fuseElapsed = 0f;
            generation++;
        }
    }

    /// <summary>只有一枚已部署地雷存在的 1.5 秒内才有此组件，不驻留在玩家或宿主上。</summary>
    internal sealed class EmptyMagazineMineDriver : MonoBehaviour
    {
        private void Update() { EmptyMagazineMineRuntime.Tick(this); }
        private void OnDestroy() { EmptyMagazineMineRuntime.OnMineDestroyed(this); }
    }
}
