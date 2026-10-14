using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.Utilities;
using Duckov.UI.DialogueBubbles;
using BossRush.Common.Effects;

namespace BossRush
{
    /// <summary>
    /// 龙王岩浆伤害区域组件
    /// 玩家进入时造成火焰伤害并施加点燃Buff
    /// </summary>
    public class DragonKingLavaZone : MonoBehaviour
    {
        private const float PLAYER_COLLIDER_REFRESH_INTERVAL = 1f;
        private static readonly Collider[] emptyPlayerColliders = new Collider[0];
        private static readonly List<DragonKingLavaZone> activeZones = new List<DragonKingLavaZone>(48);

        /// <summary>
        /// 每次伤害值
        /// </summary>
        private float damage = 5f;

        /// <summary>
        /// 伤害间隔（秒）
        /// </summary>
        private float damageInterval = 0.5f;

        /// <summary>
        /// 持续时间（秒）
        /// </summary>
        private float duration = 3f;

        /// <summary>
        /// 检测半径
        /// </summary>
        private float radius = 1f;

        /// <summary>
        /// Boss角色引用（用于伤害来源）
        /// </summary>
        private CharacterMainControl bossCharacter;

        /// <summary>
        /// 上次伤害时间
        /// </summary>
        private float lastDamageTime = 0f;

        /// <summary>
        /// 创建时间
        /// </summary>
        private float createTime = 0f;

        /// <summary>
        /// 缓存的点燃Buff
        /// </summary>
        private static Duckov.Buffs.Buff cachedBurnBuff = null;
        private static CharacterMainControl cachedMainPlayer = null;
        private static Transform cachedMainPlayerRoot = null;
        private static Collider[] cachedMainPlayerColliders = emptyPlayerColliders;
        private static float lastPlayerColliderRefreshTime = float.NegativeInfinity;
        private static DragonKingLavaZoneUpdater updater = null;
        private bool isInitialized = false;
        private readonly Dictionary<CharacterMainControl, float> coopDamageTimes = new Dictionary<CharacterMainControl, float>();
        private sealed class PlayerZoneGeometry { internal Transform Root; internal Collider[] Colliders; internal float UpdatedAt; }
        private static readonly Dictionary<CharacterMainControl, PlayerZoneGeometry> coopGeometry = new Dictionary<CharacterMainControl, PlayerZoneGeometry>();

        /// <summary>
        /// 球形碰撞器
        /// </summary>
        private SphereCollider sphereCollider;

        /// <summary>
        /// 初始化岩浆区域
        /// </summary>
        public void PrepareForPooling()
        {
            EnsureUpdater();
            EnsureSphereCollider();
            ConfigureDisabledTrigger();
            RefreshMainPlayerCache(false);
            ResetRuntimeState();
        }

        public void Initialize(float dmg, float interval, float dur, float rad, CharacterMainControl boss)
        {
            damage = dmg;
            damageInterval = interval;
            duration = dur;
            radius = rad;
            bossCharacter = boss;
            createTime = Time.time;
            lastDamageTime = 0f;
            isInitialized = true;

            // 缓存点燃Buff
            if (cachedBurnBuff == null)
            {
                cachedBurnBuff = GameplayDataSettings.Buffs.Burn;
            }

            EnsureUpdater();
            RefreshMainPlayerCache(false);

            // 保留碰撞器组件供对象池复用，但关闭触发检测，改由集中式距离采样统一处理
            EnsureSphereCollider();
            sphereCollider.radius = radius;
            sphereCollider.isTrigger = true;
            sphereCollider.enabled = false;

            // 设置层级
            gameObject.layer = LayerMask.NameToLayer("Default");
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            RegisterZone(this);
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            UnregisterZone(this);
            ResetRuntimeState();
        }

        private void OnDestroy()
        {
            UnregisterZone(this);
        }

        private void TickActiveZone(CharacterMainControl player)
        {
            if (!CoopAiPredictionHealthProtection.HasEntityAuthority(bossCharacter)) return;
            if (!isInitialized || !gameObject.activeInHierarchy)
            {
                return;
            }

            if (player == null || player.Health == null || player.Health.IsDead)
            {
                return;
            }

            if (Time.time - createTime > duration)
            {
                return;
            }

            float previous = lastDamageTime;
            if (BossRushCoop.IsConnected) coopDamageTimes.TryGetValue(player, out previous);
            if (Time.time - previous < damageInterval)
            {
                return;
            }

            if (BossRushCoop.IsConnected ? !IsCoopPlayerInsideZone(player) : !IsMainPlayerInsideZone())
            {
                return;
            }

            if (BossRushCoop.IsConnected) coopDamageTimes[player] = Time.time;
            else lastDamageTime = Time.time;
            ApplyLavaDamage(player);
        }

        /// <summary>
        /// 对玩家造成岩浆伤害
        /// </summary>
        private void ApplyLavaDamage(CharacterMainControl player)
        {
            if (!CoopAiPredictionHealthProtection.HasEntityAuthority(bossCharacter) || player == null || player.Health == null || player.Health.IsDead) return;

            try
            {
                // 创建伤害信息
                DamageInfo damageInfo = new DamageInfo(bossCharacter);
                damageInfo.damageValue = damage;
                damageInfo.damageType = DamageTypes.normal;
                damageInfo.damagePoint = player.transform.position;
                damageInfo.AddElementFactor(ElementTypes.fire, 1f);

                // 造成伤害
                player.Health.Hurt(damageInfo);

                // 施加点燃Buff
                if (cachedBurnBuff != null)
                {
                    player.AddBuff(cachedBurnBuff, bossCharacter, 0);
                }
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] 岩浆伤害失败: {e.Message}");
            }
        }

        private static Collider[] enemyZoneHits = new Collider[64];
        private static readonly List<CharacterMainControl> enemyZoneVictims = new List<CharacterMainControl>(8);

        /// <summary>按伤害间隔烧一次范围内与龙皇敌对的角色（每人一次；看台玩家与龙皇自己除外）。</summary>
        private void TickActiveZoneAgainstEnemies()
        {
            if (!CoopAiPredictionHealthProtection.HasEntityAuthority(bossCharacter)) return;
            if (bossCharacter == null || Time.time - createTime > duration || Time.time - lastDamageTime < damageInterval) return;
            try
            {
                int count = Physics.OverlapSphereNonAlloc(transform.position, radius + 0.5f, enemyZoneHits,
                    GameplayDataSettings.Layers.damageReceiverLayerMask, QueryTriggerInteraction.Collide);
                while (count == enemyZoneHits.Length && enemyZoneHits.Length < 512)
                {
                    enemyZoneHits = new Collider[enemyZoneHits.Length * 2];
                    count = Physics.OverlapSphereNonAlloc(transform.position, radius + 0.5f, enemyZoneHits,
                        GameplayDataSettings.Layers.damageReceiverLayerMask, QueryTriggerInteraction.Collide);
                }
                if (count == enemyZoneHits.Length)
                {
                    enemyZoneHits = Physics.OverlapSphere(transform.position, radius + 0.5f,
                        GameplayDataSettings.Layers.damageReceiverLayerMask, QueryTriggerInteraction.Collide);
                    count = enemyZoneHits.Length;
                }
                enemyZoneVictims.Clear();
                Teams bossTeam = bossCharacter.Team;
                for (int i = 0; i < count; i++)
                {
                    Collider hit = enemyZoneHits[i];
                    DamageReceiver receiver = hit != null ? hit.GetComponent<DamageReceiver>() : null;
                    Health health = receiver != null ? receiver.health : null;
                    if (health == null || health.IsDead) continue;
                    CharacterMainControl victim = health.TryGetCharacter();
                    if (victim == null || victim == bossCharacter || victim.IsMainCharacter || BossRushCoop.IsPlayer(victim)
                        || !Team.IsEnemy(bossTeam, victim.Team) || enemyZoneVictims.Contains(victim)) continue;
                    enemyZoneVictims.Add(victim);
                }
                if (enemyZoneVictims.Count == 0) return;
                lastDamageTime = Time.time;
                for (int i = 0; i < enemyZoneVictims.Count; i++) ApplyLavaDamage(enemyZoneVictims[i]);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[DragonKing] 岩浆（阵营模式）伤害失败: " + e.Message);
            }
            finally
            {
                enemyZoneVictims.Clear();
                System.Array.Clear(enemyZoneHits, 0, enemyZoneHits.Length);
            }
        }

        private bool IsMainPlayerInsideZone()
        {
            Vector3 zoneCenter = transform.position;
            float radiusSqr = radius * radius;
            Collider[] playerColliders = cachedMainPlayerColliders;

            for (int i = 0; i < playerColliders.Length; i++)
            {
                Collider collider = playerColliders[i];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3 closestPoint = collider.ClosestPoint(zoneCenter);
                if ((closestPoint - zoneCenter).sqrMagnitude <= radiusSqr)
                {
                    return true;
                }
            }

            if (cachedMainPlayerRoot == null)
            {
                return false;
            }

            Vector3 toPlayer = cachedMainPlayerRoot.position - zoneCenter;
            return toPlayer.sqrMagnitude <= radiusSqr;
        }

        private bool IsCoopPlayerInsideZone(CharacterMainControl player)
        {
            PlayerZoneGeometry geometry;
            // 原生 COOP 可把多个角色挂在同一容器下；只缓存这名角色自己的碰撞器。
            Transform playerRoot = player.transform;
            if (!coopGeometry.TryGetValue(player, out geometry))
            { geometry = new PlayerZoneGeometry(); coopGeometry[player] = geometry; }
            if (geometry.Root != playerRoot || geometry.Colliders == null || Time.time - geometry.UpdatedAt >= PLAYER_COLLIDER_REFRESH_INTERVAL)
            {
                geometry.Root = playerRoot; geometry.UpdatedAt = Time.time;
                geometry.Colliders = playerRoot != null ? playerRoot.GetComponentsInChildren<Collider>(true) : emptyPlayerColliders;
            }
            Vector3 center = transform.position;
            float reach = radius * radius;
            foreach (Collider collider in geometry.Colliders)
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy
                    && (collider.ClosestPoint(center) - center).sqrMagnitude <= reach) return true;
            return playerRoot != null && (playerRoot.position - center).sqrMagnitude <= reach;
        }

        private static void UpdateActiveZones()
        {
            if (activeZones.Count == 0)
            {
                coopGeometry.Clear();
                return;
            }
            if (!BossRushCoop.HasWorldAuthority) return;

            if (BossRushCoop.IsConnected && !DragonKingAbilityController.IsFactionTargetMode())
            {
                for (int i = activeZones.Count - 1; i >= 0; i--)
                {
                    DragonKingLavaZone zone = activeZones[i];
                    if (zone == null) { activeZones.RemoveAt(i); continue; }
                    foreach (BossRushCoopPlayer member in BossRushCoop.Players)
                        if (member.IsAlive) zone.TickActiveZone(member.Character);
                }
                return;
            }

            // Mode E / 鸭王杯：岩浆烧的是龙皇的敌人（其他 Boss），不是看台上的玩家
            if (DragonKingAbilityController.IsFactionTargetMode())
            {
                for (int i = activeZones.Count - 1; i >= 0; i--)
                {
                    DragonKingLavaZone zone = activeZones[i];
                    if (zone == null)
                    {
                        activeZones.RemoveAt(i);
                        continue;
                    }
                    zone.TickActiveZoneAgainstEnemies();
                }
                return;
            }

            CharacterMainControl player;
            if (!TryGetMainPlayer(out player))
            {
                return;
            }

            for (int i = activeZones.Count - 1; i >= 0; i--)
            {
                DragonKingLavaZone zone = activeZones[i];
                if (zone == null)
                {
                    activeZones.RemoveAt(i);
                    continue;
                }

                zone.TickActiveZone(player);
            }
        }

        private static bool TryGetMainPlayer(out CharacterMainControl player)
        {
            RefreshMainPlayerCache(false);
            player = cachedMainPlayer;
            return player != null && player.Health != null && !player.Health.IsDead;
        }

        private static void RefreshMainPlayerCache(bool forceRefresh)
        {
            CharacterMainControl mainPlayer = CharacterMainControl.Main;
            Transform mainPlayerRoot = mainPlayer != null ? mainPlayer.transform.root : null;
            bool needsRefresh =
                forceRefresh ||
                cachedMainPlayer != mainPlayer ||
                cachedMainPlayerRoot != mainPlayerRoot ||
                cachedMainPlayerColliders == null ||
                Time.time - lastPlayerColliderRefreshTime >= PLAYER_COLLIDER_REFRESH_INTERVAL;

            if (!needsRefresh)
            {
                return;
            }

            cachedMainPlayer = mainPlayer;
            cachedMainPlayerRoot = mainPlayerRoot;
            cachedMainPlayerColliders = emptyPlayerColliders;

            if (mainPlayerRoot != null)
            {
                Collider[] colliders = mainPlayerRoot.GetComponentsInChildren<Collider>(true);
                if (colliders != null && colliders.Length > 0)
                {
                    cachedMainPlayerColliders = colliders;
                }
            }

            lastPlayerColliderRefreshTime = Time.time;
        }

        private static void RegisterZone(DragonKingLavaZone zone)
        {
            if (zone == null)
            {
                return;
            }

            EnsureUpdater();
            if (!activeZones.Contains(zone))
            {
                activeZones.Add(zone);
            }
        }

        private static void UnregisterZone(DragonKingLavaZone zone)
        {
            if (zone == null)
            {
                return;
            }

            activeZones.Remove(zone);
            if (activeZones.Count == 0)
            {
                coopGeometry.Clear(); cachedMainPlayer = null; cachedMainPlayerRoot = null;
                cachedMainPlayerColliders = emptyPlayerColliders;
            }
        }

        internal static void ResetStaticCaches()
        {
            foreach (DragonKingLavaZone zone in activeZones) if (zone != null) zone.ResetRuntimeState();
            activeZones.Clear(); coopGeometry.Clear(); enemyZoneVictims.Clear();
            System.Array.Clear(enemyZoneHits, 0, enemyZoneHits.Length);
            enemyZoneHits = new Collider[64]; cachedBurnBuff = null;
            cachedMainPlayer = null; cachedMainPlayerRoot = null; cachedMainPlayerColliders = emptyPlayerColliders;
            lastPlayerColliderRefreshTime = float.NegativeInfinity;
            DragonKingLavaZoneUpdater old = updater; updater = null;
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
        }

        private static void EnsureUpdater()
        {
            if (updater != null)
            {
                return;
            }

            GameObject updaterObject = new GameObject("DragonKing_LavaZoneUpdater");
            updaterObject.hideFlags = HideFlags.HideInHierarchy;
            updater = updaterObject.AddComponent<DragonKingLavaZoneUpdater>();
        }

        private void ResetRuntimeState()
        {
            isInitialized = false;
            bossCharacter = null;
            createTime = 0f;
            lastDamageTime = 0f;
            coopDamageTimes.Clear();
        }

        private void EnsureSphereCollider()
        {
            if (sphereCollider == null)
            {
                sphereCollider = gameObject.GetComponent<SphereCollider>();
            }
            if (sphereCollider == null)
            {
                sphereCollider = gameObject.AddComponent<SphereCollider>();
            }

            ConfigureDisabledTrigger();
        }

        private void ConfigureDisabledTrigger()
        {
            if (sphereCollider == null)
            {
                return;
            }

            sphereCollider.isTrigger = true;
            sphereCollider.enabled = false;
        }

        private sealed class DragonKingLavaZoneUpdater : MonoBehaviour
        {
            private void Update()
            {
                DragonKingLavaZone.UpdateActiveZones();
            }

            private void OnDisable()
            {
                if (updater == this)
                {
                    updater = null;
                }
            }

            private void OnDestroy()
            {
                if (updater == this)
                {
                    updater = null;
                }
            }
        }
    }
}
