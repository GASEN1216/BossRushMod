using System;
using System.Collections.Generic;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;

namespace BossRush
{
    /// <summary>单场花冠战斗的唯一 owner；规则与预警在同一帧采样。</summary>
    internal sealed class EclipseBloomAbilityController : MonoBehaviour
    {
        private readonly EclipseBloomRules rules = new EclipseBloomRules();
        private readonly EclipseBloomPetal[] petals = new EclipseBloomPetal[4];
        private CharacterMainControl boss;
        private CharacterRandomPreset preset;
        private Item reward;
        private EclipseBloomBody body;
        private Func<bool> isCurrent;
        private int sceneHandle;
        private float baseline;
        private float nextHitAt;
        private float entranceRemaining = 2f;
        private bool subscribed;
        private bool stopped;
        private Modifier bodyArmor;
        private Modifier headArmor;
        private CharacterRandomPreset originalPreset;
        private GameObject coreHit;
        private DamageReceiver coreReceiver;
        private readonly List<Collider> hiddenHitColliders = new List<Collider>();
        private EclipseBloomSolarFlares flares;
        private int seenAttackSerial;
        private Vector3 lastPlayerPosition;
        private Vector3 playerVelocity;
        private bool hasPlayerSample;
        private float nextCoreFeedbackAt;

        internal bool IsFighting
        {
            get
            {
                CharacterMainControl main = CharacterMainControl.Main;
                return !stopped && boss != null && boss.Health != null && !boss.Health.IsDead
                    && main != null && main.Health != null && !main.Health.IsDead
                    && !SceneLoader.IsSceneLoading && EclipseBloomEncounterPlacement.IsLoaded(boss.gameObject.scene)
                    && boss.gameObject.scene.handle == sceneHandle
                    && isCurrent != null && isCurrent();
            }
        }

        internal void Initialize(CharacterMainControl character, CharacterRandomPreset runtimePreset,
            Item preparedReward, float meleeBaseline, Func<bool> current, int handle,
            CharacterRandomPreset officialPreset)
        {
            boss = character;
            preset = runtimePreset;
            originalPreset = officialPreset;
            reward = preparedReward;
            baseline = meleeBaseline;
            isCurrent = current;
            sceneHandle = handle;
            boss.SetMoveInput(Vector3.zero);
            // 底模装备只提供宿主骨架；隐藏护甲不能悄悄改变花冠的可见弱点。
            Stat armor = boss.CharacterItem.GetStat("BodyArmor");
            if (armor != null)
            {
                bodyArmor = new Modifier(ModifierType.PercentageMultiply, 0f, true, int.MaxValue, this);
                armor.AddModifier(bodyArmor);
            }
            armor = boss.CharacterItem.GetStat("HeadArmor");
            if (armor != null)
            {
                headArmor = new Modifier(ModifierType.PercentageMultiply, 0f, true, int.MaxValue, this);
                armor.AddModifier(headArmor);
            }
            body = EclipseBloomBody.Attach(boss);
            if (body == null) throw new InvalidOperationException("eclipse_body_unavailable");
            EclipseBloomEncounterPlacement.BindToScene(body.gameObject, boss.gameObject.scene);
            CreateCoreReceiver();
            flares = EclipseBloomSolarFlares.Create(boss);
            seenAttackSerial = rules.AttackSerial;
            for (int i = 0; i < petals.Length; i++)
            {
                petals[i] = EclipseBloomPetal.Create(this, i, body.GetPetalPosition(i),
                    Mathf.Max(1f, boss.Health.MaxHealth * EclipseBloomTuning.PetalHealthFraction), boss.Team);
                EclipseBloomEncounterPlacement.BindToScene(petals[i].gameObject, boss.gameObject.scene);
            }
            SyncPetals();
            boss.BeforeCharacterSpawnLootOnDead += OnBeforeSpawnLoot;
            boss.Health.OnDeadEvent.AddListener(OnBossDead);
            subscribed = true;
            BossRushAudioManager.Instance?.PlayBossBGM(BossBgmKeys.EclipseBloom, boss);
        }

        /// <summary>由已有官方死亡前缀调用，先恢复官方计数身份，再让官方生成尸体箱。</summary>
        internal static bool RestoreOfficialPresetBeforeDeath(CharacterMainControl character)
        {
            if (character == null) return false;
            EclipseBloomAbilityController controller = character.GetComponent<EclipseBloomAbilityController>();
            if (controller == null || controller.originalPreset == null) return false;
            character.characterPreset = controller.originalPreset;
            return true;
        }

        private void CreateCoreReceiver()
        {
            // 仅关闭底模 DamageReceiver 层的可射击碰撞，官方移动器的实体胶囊继续工作。
            int layer = LayerMask.NameToLayer("DamageReceiver");
            if (layer < 0) throw new InvalidOperationException("eclipse_core_damage_layer_missing");
            foreach (DamageReceiver old in boss.GetComponentsInChildren<DamageReceiver>(true))
            {
                if (old.gameObject == boss.gameObject) continue;
                foreach (Collider collider in old.GetComponents<Collider>())
                    if (collider.enabled && collider.gameObject.layer == layer)
                    {
                        hiddenHitColliders.Add(collider);
                        collider.enabled = false;
                    }
            }
            coreHit = new GameObject("EclipseBloom_CoreHit");
            coreHit.SetActive(false);
            EclipseBloomEncounterPlacement.BindToScene(coreHit, boss.gameObject.scene);
            coreHit.layer = layer;
            SphereCollider sphere = coreHit.AddComponent<SphereCollider>();
            sphere.radius = body.GetCoreRadius();
            Rigidbody rigidbody = coreHit.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            DamageReceiver receiver = coreHit.AddComponent<DamageReceiver>();
            receiver.health = boss.Health;
            receiver.useSimpleHealth = false;
            receiver.OnHurtEvent = new UnityEvent<DamageInfo>();
            receiver.OnDeadEvent = new UnityEvent<DamageInfo>();
            receiver.OnHurtEvent.AddListener(OnCoreHurt);
            coreReceiver = receiver;
            // 花瓣护核：伤害进入官方 Health 之前按剩余花瓣数缩小（DamageReceiver.Hurt 前缀消费）。
            ReceiverDamageScaling.Register(receiver, info => EclipseBloomTuning.CoreDamageScale(rules.Phase, rules.IntactMask));
            coreHit.transform.position = body.GetCorePosition();
            coreHit.SetActive(true);
            Collider physical = boss.GetComponent<Collider>();
            if (physical != null) Physics.IgnoreCollision(sphere, physical, true);
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null)
            {
                Collider playerBody = player.GetComponent<Collider>();
                if (playerBody != null) Physics.IgnoreCollision(sphere, playerBody, true);
            }
        }

        private void Update()
        {
            if (stopped) return;
            if (!IsFighting)
            {
                StopBattle(false);
                if (boss != null) Destroy(boss.gameObject);
                return;
            }
            if (Time.deltaTime <= 0f || BossRushUI.IsGamePaused()) return;
            // 出场构建期可打核心；先给玩家观察四瓣的时间，不抢跑首次伤害。
            if (entranceRemaining > 0f)
            {
                entranceRemaining -= Mathf.Min(Time.deltaTime, EclipseBloomTuning.MaximumStepSeconds);
                SyncPetals();
                return;
            }
            TrackPlayer();
            rules.Advance(Time.deltaTime, boss.Health.CurrentHealth / Mathf.Max(1f, boss.Health.MaxHealth));
            UpdateMotion();
            ApplyFrame();
            SyncPetals();
            TryDamagePlayer();
            UpdateFlares();
        }

        /// <summary>把玩家方位交给规则（只在预警前段转向），并估一个平面速度给日珥预判用。</summary>
        private void TrackPlayer()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return;
            Vector3 position = player.transform.position;
            float step = Mathf.Min(Time.deltaTime, EclipseBloomTuning.MaximumStepSeconds);
            if (hasPlayerSample && step > 0f)
            {
                Vector3 velocity = (position - lastPlayerPosition) * (1f / step);
                velocity.y = 0f;
                // 瞬移、传送或掉帧不当成速度；正常跑动低于 9 m/s。
                if (velocity.sqrMagnitude > 81f) velocity = Vector3.zero;
                playerVelocity = playerVelocity * 0.7f + velocity * 0.3f;
            }
            lastPlayerPosition = position;
            hasPlayerSample = true;
            Vector3 delta = position - boss.transform.position;
            if (delta.x * delta.x + delta.z * delta.z > 0.04f)
                rules.Track(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// 每开始一次新预警，判断玩家是否在扇区射程外放风筝：在外面就按阶段落 1/2/3 道日珥，第一道砸脚下、
        /// 其余按跑动方向预判；第三阶段即使贴身也隔一轮落一道，逼人移动。
        /// </summary>
        private void UpdateFlares()
        {
            if (flares == null) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (rules.AttackSerial != seenAttackSerial)
            {
                seenAttackSerial = rules.AttackSerial;
                if (player != null) LaunchFlares(player);
            }
            flares.Tick(Mathf.Min(Time.deltaTime, EclipseBloomTuning.MaximumStepSeconds), player);
        }

        private void LaunchFlares(CharacterMainControl player)
        {
            Vector3 target = player.transform.position;
            Vector3 delta = target - boss.transform.position;
            float planar = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
            bool far = planar > EclipseBloomTuning.FlareMinimumDistance;
            if (!far && !(rules.Phase == 3 && (seenAttackSerial & 1) == 0)) return;
            int count = far ? rules.Phase : 1;
            float warning = rules.Phase == 1 ? 1.35f : rules.Phase == 2 ? 1.2f : 1.05f;
            float damage = (rules.Phase == 3 ? 34f : 26f) * BossSkillDamageRules.ResolveMeleeDamageScale(boss, baseline);
            flares.Launch(target, EclipseBloomTuning.FlareRadius, warning, damage);
            Vector3 lead = playerVelocity * (warning * 0.85f);
            if (lead.sqrMagnitude > 36f) lead = lead.normalized * 6f;
            Vector3 side = planar > 0.1f ? new Vector3(delta.z, 0f, -delta.x) * (1f / planar) : Vector3.forward;
            for (int i = 1; i < count; i++)
            {
                Vector3 offset = i == 1
                    ? (lead.sqrMagnitude > 2.25f ? lead : side * 3.4f)
                    : (lead.sqrMagnitude > 2.25f ? lead * 0.5f - side * 3.4f : -side * 3.4f);
                flares.Launch(target + offset, EclipseBloomTuning.FlareRadius, warning + i * 0.18f, damage);
            }
        }

        private void OnCoreHurt(DamageInfo info)
        {
            if (stopped || body == null || Time.time < nextCoreFeedbackAt) return;
            nextCoreFeedbackAt = Time.time + 0.07f;
            body.PlayCoreHit(info.damagePoint, EclipseBloomTuning.CoreDamageScale(rules.Phase, rules.IntactMask) < 1f);
        }

        internal void NotifyPetalHurt(int index, Vector3 point)
        {
            if (stopped || body == null) return;
            body.PlayPetalHit(index, point);
        }

        private void UpdateMotion()
        {
            Vector3 direction = Vector3.zero;
            CharacterMainControl player = CharacterMainControl.Main;
            // 仅在没有预警/伤害的休整期追近；官方移动器保留实体碰撞和贴地。
            if (rules.IsResting && player != null)
            {
                Vector3 delta = player.transform.position - boss.transform.position;
                delta.y = 0f;
                // 休整期把玩家压回扇区射程：6 m 外就追，12 m 外全速。
                if (delta.sqrMagnitude > 36f && !Physics.Linecast(boss.transform.position + Vector3.up,
                    player.transform.position + Vector3.up, GameplayDataSettings.Layers.wallLayerMask,
                    QueryTriggerInteraction.Ignore)) direction = delta.normalized * (delta.sqrMagnitude > 144f ? 1f : 0.85f);
            }
            boss.SetMoveInput(direction);
        }

        private void ApplyFrame()
        {
            if (body != null) body.ApplyFrame(rules.Phase, rules.IntactMask, rules.RotationDegrees,
                rules.AttackMask, rules.TelegraphProgress, rules.IsDamaging);
        }

        private void SyncPetals()
        {
            if (coreHit != null && body != null)
            {
                coreHit.transform.position = body.GetCorePosition();
                coreHit.GetComponent<SphereCollider>().radius = body.GetCoreRadius();
            }
            for (int i = 0; i < petals.Length; i++)
            {
                if (petals[i] == null) continue;
                if ((rules.IntactMask & (1 << i)) == 0)
                {
                    Destroy(petals[i].gameObject);
                    petals[i] = null;
                }
                else if (body != null)
                {
                    petals[i].transform.position = body.GetPetalPosition(i);
                    petals[i].transform.rotation = body.GetPetalRotation(i);
                    petals[i].transform.localScale = body.GetPetalScale(i);
                }
            }
        }

        internal void BreakPetal(int index)
        {
            if (!IsFighting || !rules.BreakPetal(index)) return;
            // 受击事件内即时撤除该瓣伤害和预警，不等下一次 Update。
            if (body != null) body.PlayPetalBreak(index);
            ApplyFrame();
            SyncPetals();
        }

        private void TryDamagePlayer()
        {
            if (!rules.IsDamaging || rules.ElapsedSeconds < nextHitAt) return;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.mainDamageReceiver == null || !Team.IsEnemy(boss.Team, player.Team)) return;
            Vector3 offset = player.transform.position - boss.transform.position;
            if (Mathf.Abs(offset.y) > 2f || !rules.IsDangerous(offset.x, offset.z)) return;
            if (body == null || !body.HasGroundWarningAt(player.transform.position)) return;
            Vector3 from = boss.transform.position + Vector3.up * 0.6f;
            Vector3 to = player.transform.position + Vector3.up * 0.6f;
            if (Physics.Linecast(from, to, GameplayDataSettings.Layers.wallLayerMask, QueryTriggerInteraction.Ignore)) return;
            nextHitAt = rules.ElapsedSeconds + 0.55f;
            DamageInfo info = new DamageInfo(boss);
            info.damageType = DamageTypes.normal;
            info.damageValue = (rules.Phase == 3 ? 42f : 34f) * BossSkillDamageRules.ResolveMeleeDamageScale(boss, baseline);
            info.damagePoint = player.transform.position;
            info.damageNormal = offset.normalized;
            info.crit = -1;
            player.mainDamageReceiver.Hurt(info);
        }

        private void OnBeforeSpawnLoot(DamageInfo info)
        {
            // 真实死亡的掉落归已提交实例所有；玩家同帧死亡或引导被放弃不能撤销这次击杀。
            if (stopped || !subscribed || reward == null || boss == null || boss.Health == null || !boss.Health.IsDead
                || !EclipseBloomEncounterPlacement.IsLoaded(boss.gameObject.scene)
                || boss.gameObject.scene.handle != sceneHandle) return;
            try
            {
                Inventory inventory = boss.CharacterItem != null ? boss.CharacterItem.Inventory : null;
                if (inventory != null)
                {
                    int count = inventory.Content != null ? inventory.Content.Count : 0;
                    inventory.SetCapacity(Mathf.Max(inventory.Capacity, count + 1));
                    if (inventory.AddAndMerge(reward, 0)) { reward = null; return; }
                }
            }
            catch (Exception e) { ModBehaviour.DevLog("[EclipseBloom] 奖励入箱失败: " + e.Message); }
            try
            {
                reward.Drop(boss.transform.position + Vector3.up * 0.2f, true, Vector3.forward, 30f);
                reward = null;
            }
            catch (Exception e) { Debug.LogWarning("[EclipseBloom] 专属掉落失败: " + e.Message); }
        }

        private void OnBossDead(DamageInfo info)
        {
            if (stopped) return;
            OnBeforeSpawnLoot(info); // 官方已派发时 reward 为空；兼容没有尸体箱的宿主路径。
            EclipseBloomRuntimeModule.NotifyOwnedBossDefeated(boss);
            CodexKillCollector.OnKnownBossDead(boss != null ? boss.Health : null, info, EclipseBloomBoss.NameKey);
            StopBattle(true);
        }

        internal void StopBattle(bool death)
        {
            if (stopped) return;
            stopped = true;
            rules.Stop();
            if (boss != null) boss.SetMoveInput(Vector3.zero);
            if (bodyArmor != null) bodyArmor.RemoveFromTarget();
            if (headArmor != null) headArmor.RemoveFromTarget();
            bodyArmor = null;
            headArmor = null;
            ReceiverDamageScaling.Unregister(coreReceiver);
            coreReceiver = null;
            if (coreHit != null) Destroy(coreHit);
            coreHit = null;
            if (flares != null) flares.Dispose();
            flares = null;
            foreach (Collider collider in hiddenHitColliders) if (collider != null) collider.enabled = true;
            hiddenHitColliders.Clear();
            for (int i = 0; i < petals.Length; i++)
            {
                if (petals[i] != null) Destroy(petals[i].gameObject);
                petals[i] = null;
            }
            if (body != null)
            {
                if (death) body.Dissipate(); else Destroy(body.gameObject);
                body = null;
            }
            try { BossRushAudioManager.Instance?.StopBossBGM(BossBgmKeys.EclipseBloom, boss); }
            catch (Exception e) { ModBehaviour.DevLog("[EclipseBloom] 音乐收尾失败: " + e.Message); }
            if (subscribed && boss != null)
            {
                boss.BeforeCharacterSpawnLootOnDead -= OnBeforeSpawnLoot;
                if (boss.Health != null) boss.Health.OnDeadEvent.RemoveListener(OnBossDead);
            }
            subscribed = false;
            if (reward != null) Destroy(reward.gameObject);
            reward = null;
        }

        private void OnDestroy()
        {
            StopBattle(false);
            if (preset != null) Destroy(preset);
            preset = null;
            originalPreset = null;
            isCurrent = null;
            boss = null;
        }
    }
}
