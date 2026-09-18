// ============================================================================
// SummonStaffAction.cs - 召唤法杖右键技能动作「灵魂投射」
// ============================================================================
// 模块说明：
//   右键技能：朝瞄准方向撕开裂隙，在**前方**投下 3 只短命灵魂战士；
//   它们到期消散或被打死时，原地炸开一团灵魂爆裂（不伤自己与友军）。
//
// 为什么不是「小一号的霜之哀伤」（2026-09-18 定位重做）：
//   原实现与 FrostmourneAction 是同一件事——同一个 Cname_Zombie 预设、同一套阵营与 AI 设置、
//   同样召在自己脚边，差别只有数量 3<5、血量 80<100、冷却 12>10。一件品质 5 的武器
//   完全被品质 6 的霜之哀伤覆盖，玩家没有任何理由选它，是典型的「为了有而有」。
//   现在两件武器各占一条线：
//     霜之哀伤   —— 5 只**常驻**亡灵贴身护卫，打到死为止，近身缠斗线；
//     召唤法杖   —— 3 只**可投放**的短命灵魂，扔进敌堆里拉仇恨，到点炸开，战术投放线。
//   投放与爆裂都是玩家能主动规划的动作，法杖因此有了自己的操作（瞄准哪儿、什么时候扔），
//   而不只是「按右键多三个人」。
//   回退办法：把 SoulBurstDamage 设为 0 关掉爆裂，把 PlacementDistance 设为 0 退回脚边召唤。
//
// 与官方 API 的边界：
//   - 生成仍复用 CharacterRandomPreset.CreateCharacterAsync（与霜之哀伤同一条路径）。
//   - 爆裂走 LevelManager.ExplosionManager.CreateExplosion，canHurtSelf=false，
//     且**只在 Update 里触发**——绝不在 Health.OnDead/OnHurt 的派发栈里调用官方爆炸
//     （它的命中缓冲不支持重入，见 CR-2026-09-17-015）。
// ============================================================================

using System;
using System.Collections.Generic;
using BossRush.Common.Equipment;
using Cysharp.Threading.Tasks;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 召唤法杖右键技能动作 — 灵魂投射
    /// </summary>
    public class SummonStaffAction : EquipmentAbilityAction
    {
        private static SummonStaffConfig _config;
        private bool spawningStarted;
        private bool spawningComplete;
        private int activeSpawnRequestId;

        // 缓存的预设
        private static CharacterRandomPreset cachedPreset;
        private static bool presetSearchAttempted;

        // 已召唤的友军列表
        private static readonly List<CharacterMainControl> summonedAllies = new List<CharacterMainControl>();

        public static void SetConfig(SummonStaffConfig config)
        {
            _config = config;
        }

        protected override EquipmentAbilityConfig GetConfig()
        {
            if (_config == null) _config = new SummonStaffConfig();
            return _config;
        }

        protected override bool ShouldAutoConsumeStamina()
        {
            return false;
        }

        protected override bool IsReadyInternal()
        {
            CleanupDeadAllies();
            if (summonedAllies.Count >= SummonStaffConfig.SummonCount)
            {
                return false;
            }
            return true;
        }

        protected override bool OnAbilityStart()
        {
            activeSpawnRequestId++;
            spawningStarted = false;
            spawningComplete = false;
            return true;
        }

        protected override void OnAbilityUpdate(float deltaTime)
        {
            if (!spawningStarted)
            {
                spawningStarted = true;
                int requestId = activeSpawnRequestId;
                SpawnAlliesAsync(requestId).Forget();
            }

            if (spawningComplete || actionElapsedTime >= SummonStaffConfig.TotalActionDuration)
            {
                StopAction();
            }
        }

        protected override void OnAbilityStop()
        {
            activeSpawnRequestId++;
            spawningStarted = false;
            spawningComplete = false;
        }

        private void OnDestroy()
        {
            activeSpawnRequestId++;
            spawningStarted = false;
        }

        /// <summary>
        /// 异步生成友军
        /// </summary>
        private async UniTaskVoid SpawnAlliesAsync(int requestId)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return;

                CharacterRandomPreset preset = FindPreset();
                if (preset == null)
                {
                    LogIfVerbose("未找到召唤预设，取消召唤");
                    return;
                }

                Teams playerTeam = player.Team;
                Vector3 playerPos = player.transform.position;
                // 投放点：瞄准方向前方一段距离；被墙挡住就贴着墙前落地，地面找不到就退回脚边。
                Vector3 placementCenter = ResolvePlacementCenter(player, playerPos);
                int sceneIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

                CleanupDeadAllies();
                int availableSlots = Mathf.Max(0, SummonStaffConfig.SummonCount - summonedAllies.Count);
                if (availableSlots <= 0) return;

                int successCount = 0;

                for (int i = 0; i < availableSlots; i++)
                {
                    if (!IsRequestValid(requestId, player, sceneIndex)) return;

                    // 计算生成位置：围着投放点等角散开
                    float angle = 360f * i / SummonStaffConfig.SummonCount;
                    Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * SummonStaffConfig.SummonRadius;
                    Vector3 spawnPos = SnapToGround(placementCenter + offset, placementCenter.y);

                    try
                    {
                        CharacterMainControl ally = await preset.CreateCharacterAsync(
                            spawnPos, Vector3.forward, sceneIndex, null, false);

                        if (!IsRequestValid(requestId, player, sceneIndex))
                        {
                            if (ally != null && ally.gameObject != null)
                            {
                                UnityEngine.Object.Destroy(ally.gameObject);
                            }
                            return;
                        }

                        if (ally == null) continue;

                        // 配置友军
                        SetAllyHealth(ally, SummonStaffConfig.SummonHealth);
                        ally.SetTeam(playerTeam);
                        ModBehaviour.Instance?.SanitizeBossRushZombieSpawn(ally, "SummonStaff");
                        ally.dropBoxOnDead = false;
                        ally.gameObject.name = "SummonStaff_Ally_" + (summonedAllies.Count + 1);
                        ally.gameObject.SetActive(true);

                        // 设置 AI
                        SetupAllyAI(ally);

                        // 注册定时销毁 + 消散/阵亡爆裂
                        SummonStaffAllyLifetime lifetime = ally.gameObject.AddComponent<SummonStaffAllyLifetime>();
                        lifetime.Initialize(SummonStaffConfig.SummonLifetime, ally, player);

                        summonedAllies.Add(ally);
                        successCount++;

                        // 表现层：每只灵魂战士落地一圈紫环（配色取自描述文案的 #BA68C8）
                        NewWeaponFx.PlayBurst(spawnPos, NewWeaponPalette.SoulCore, 1.4f, 0.4f, 4);
                    }
                    catch (Exception e)
                    {
                        LogIfVerbose("召唤友军异常: " + e.Message);
                    }

                    // 每只之间让出一帧
                    await UniTask.Yield();
                }

                if (!IsRequestValid(requestId, player, sceneIndex)) return;

                if (successCount > 0)
                {
                    // 表现层：施法者脚下一圈符文环 + 召唤音效，只在真的召出人时播放。
                    // 位置重新取一次：playerPos 是开唱那一刻的坐标，中间隔了 N 次 await，
                    // 拿它画环会画在玩家已经离开的地方（生成点沿用旧坐标是对的，环不是）。
                    Vector3 ringPos = player != null && player.transform != null
                        ? player.transform.position
                        : playerPos;
                    NewWeaponFx.PlayBurst(ringPos, NewWeaponPalette.SoulCore, 2.6f, 0.5f, 8);
                    NewWeaponFx.PlaySound(NewWeaponSfx.SoulSummon);
                    NewWeaponFx.ShowBubble(
                        player,
                        "<color=#BA68C8>灵魂战士已投放！</color>",
                        "<color=#BA68C8>Soul warriors deployed!</color>",
                        2f);
                }
            }
            catch (Exception e)
            {
                LogIfVerbose("SpawnAlliesAsync 异常: " + e.Message);
            }
            finally
            {
                if (requestId == activeSpawnRequestId)
                {
                    spawningComplete = true;
                }
            }
        }

        /// <summary>
        /// 解析投放中心：玩家瞄准方向前方 <see cref="SummonStaffConfig.PlacementDistance"/> 米。
        /// 三级退让，保证永远给得出一个站得住的点：
        ///   1. 朝向退化或距离为 0 -> 直接用玩家脚下（等同旧版行为）；
        ///   2. 中途撞墙 -> 在撞点前 1 米落地；
        ///   3. 目标点下方找不到地面 -> 退回玩家脚下。
        /// 朝向取官方 CurrentAimDirection：根 Transform 从不旋转，transform.forward 是恒定值。
        /// </summary>
        private static Vector3 ResolvePlacementCenter(CharacterMainControl player, Vector3 playerPos)
        {
            float distance = SummonStaffConfig.PlacementDistance;
            if (player == null || distance <= 0.01f) return playerPos;

            Vector3 aim;
            try { aim = player.CurrentAimDirection; }
            catch { return playerPos; }

            aim.y = 0f;
            if (aim.sqrMagnitude < 0.0001f) return playerPos;
            aim = aim.normalized;

            // 墙体阻挡：从胸口高度往瞄准方向打一条射线，撞到就缩到撞点前面
            try
            {
                Vector3 origin = playerPos + Vector3.up * 1.0f;
                RaycastHit wallHit;
                int wallMask = GameplayDataSettings.Layers.wallLayerMask;
                if (Physics.Raycast(origin, aim, out wallHit, distance, wallMask))
                {
                    distance = Mathf.Max(0f, wallHit.distance - 1.0f);
                }
            }
            catch (Exception)
            {
                // 射线不可用时按无遮挡处理，下面的地面检测仍然会兜住
            }

            if (distance <= 0.01f) return playerPos;

            Vector3 candidate = playerPos + aim * distance;

            RaycastHit groundHit;
            try
            {
                int groundMask = GameplayDataSettings.Layers.groundLayerMask;
                if (Physics.Raycast(candidate + Vector3.up * 5f, Vector3.down, out groundHit, 15f, groundMask))
                {
                    return groundHit.point;
                }
            }
            catch (Exception)
            {
                // 同上
            }

            return playerPos;
        }

        private bool IsRequestValid(int requestId, CharacterMainControl player, int sceneIndex)
        {
            if (this == null || requestId != activeSpawnRequestId || !spawningStarted)
                return false;
            if (!isActiveAndEnabled || player == null || CharacterMainControl.Main != player)
                return false;
            if (player.Health != null && player.Health.IsDead)
                return false;
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == sceneIndex;
        }

        private static CharacterRandomPreset FindPreset()
        {
            if (cachedPreset != null) return cachedPreset;
            if (presetSearchAttempted) return null;

            presetSearchAttempted = true;

            try
            {
                CharacterRandomPreset[] allPresets = ObjectCache.GetCharacterPresets();
                foreach (CharacterRandomPreset preset in allPresets)
                {
                    if (preset == null) continue;
                    if (!string.IsNullOrEmpty(preset.nameKey) &&
                        preset.nameKey == SummonStaffConfig.SummonPresetName)
                    {
                        cachedPreset = preset;
                        ModBehaviour.DevLog("[SummonStaff] 找到召唤预设: " + preset.name);
                        return cachedPreset;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SummonStaff] 查找预设异常: " + e.Message);
            }

            return null;
        }

        private void SetAllyHealth(CharacterMainControl ally, float targetHealth)
        {
            try
            {
                if (ally.Health == null) return;
                Item characterItem = ally.CharacterItem;
                if (characterItem == null) return;

                Stat hpStat = characterItem.GetStat("MaxHealth".GetHashCode());
                if (hpStat != null)
                {
                    float currentMax = ally.Health.MaxHealth;
                    if (currentMax > 0.01f)
                    {
                        hpStat.BaseValue *= targetHealth / currentMax;
                    }
                    else
                    {
                        hpStat.BaseValue = targetHealth;
                    }
                }

                ally.Health.SetHealth(ally.Health.MaxHealth);
            }
            catch (Exception e)
            {
                LogIfVerbose("设置友军血量失败: " + e.Message);
            }
        }

        private void SetupAllyAI(CharacterMainControl ally)
        {
            try
            {
                AICharacterController ai = ally.GetComponentInChildren<AICharacterController>();
                if (ai == null) return;

                ai.forceTracePlayerDistance = 0f;
                ai.searchedEnemy = null;
                ai.noticed = false;
            }
            catch  { /* best-effort fallback intentionally ignored */ }
        }

        private static Vector3 SnapToGround(Vector3 position, float fallbackY)
        {
            RaycastHit hit;
            Vector3 samplePoint = position + Vector3.up * 5f;
            int groundMask = GameplayDataSettings.Layers.groundLayerMask;

            if (Physics.Raycast(samplePoint, Vector3.down, out hit, 15f, groundMask))
            {
                return hit.point;
            }

            position.y = fallbackY;
            return position;
        }

        private static void CleanupDeadAllies()
        {
            for (int i = summonedAllies.Count - 1; i >= 0; i--)
            {
                CharacterMainControl ally = summonedAllies[i];
                if (ally == null || ally.gameObject == null ||
                    (ally.Health != null && ally.Health.IsDead))
                {
                    summonedAllies.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 重置预设缓存
        /// </summary>
        internal static void ResetPresetCache()
        {
            cachedPreset = null;
            presetSearchAttempted = false;
        }

        /// <summary>
        /// 清理所有召唤的友军
        /// </summary>
        internal static void CleanupAllSummonedAllies()
        {
            foreach (CharacterMainControl ally in summonedAllies)
            {
                try
                {
                    if (ally != null && ally.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(ally.gameObject);
                    }
                }
                catch  { /* best-effort fallback intentionally ignored */ }
            }
            summonedAllies.Clear();
        }
    }

    /// <summary>
    /// 召唤物生命周期组件 —— 到期消散或阵亡时先炸一团灵魂爆裂，再收尾。
    ///
    /// 为什么爆裂放在这里而不是订阅 Health.OnDead：
    ///   场上最多 3 只，逐个在自己的 Update 里比一次 IsDead 就够了，
    ///   既不用往全局多播链上再挂一个订阅（AGENTS §4.6），
    ///   也天然避开了「在死亡派发栈里调官方爆炸」这个重入坑（CR-2026-09-17-015）。
    /// </summary>
    internal sealed class SummonStaffAllyLifetime : MonoBehaviour
    {
        private float lifetime;
        private float elapsed;
        private Health allyHealth;
        private CharacterMainControl owner;
        private bool burstDone;

        public void Initialize(float duration, CharacterMainControl ally, CharacterMainControl summoner)
        {
            lifetime = duration;
            elapsed = 0f;
            burstDone = false;
            owner = summoner;
            allyHealth = ally != null ? ally.Health : null;
        }

        private void Update()
        {
            // 阵亡：原地爆裂，然后交还尸体给官方（不销毁，与霜之哀伤对死亡亡灵的处理一致）
            if (allyHealth != null && allyHealth.IsDead)
            {
                TriggerSoulBurst();
                enabled = false;
                return;
            }

            elapsed += Time.deltaTime;
            if (elapsed >= lifetime)
            {
                // 到期消散：先炸再收
                TriggerSoulBurst();
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            // 场景卸载 / 被 CleanupAllSummonedAllies 直接销毁时**不补炸**：
            // 那两条路径下玩家已经不在这一局的战斗里了。这里只把闸关上，
            // 挡住「先 Destroy 再有人调 TriggerSoulBurst」的将来改动。
            burstDone = true;
        }

        /// <summary>
        /// 灵魂爆裂：以自身为中心的小范围 ghost 伤害，不伤自己与友军。幂等，只炸一次。
        /// </summary>
        private void TriggerSoulBurst()
        {
            if (burstDone) return;
            burstDone = true;

            float damage = SummonStaffConfig.SoulBurstDamage;
            float radius = SummonStaffConfig.SoulBurstRadius;
            if (damage <= 0f || radius <= 0f) return;

            Vector3 position = transform.position;

            try
            {
                // 表现层先放：即使爆炸管理器不在（过场、基地），玩家也能看到消散
                NewWeaponFx.PlayBurst(position, NewWeaponPalette.SoulCore, radius, 0.4f, 6);

                if (owner == null) return;
                LevelManager level = LevelManager.Instance;
                if (level == null || level.ExplosionManager == null) return;

                DamageInfo blast = new DamageInfo(owner);
                blast.damageValue = damage;
                blast.damagePoint = position;
                blast.isExplosion = true;
                // 自建伤害：不参与击杀链路与 Mode G 计分（与毒爆发 / 雷电释放同口径）
                blast.isFromBuffOrEffect = true;
                blast.fromWeaponItemID = 0;
                blast.AddElementFactor(ElementTypes.ghost, 1f);

                // canHurtSelf=false：官方默认 true 时 selfTeam=Teams.all，站在旁边的玩家自己会吃这一下
                level.ExplosionManager.CreateExplosion(
                    position,
                    radius,
                    blast,
                    ExplosionFxTypes.normal,
                    0.2f,
                    false);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[SummonStaff] 灵魂爆裂失败: " + e.Message);
            }
        }
    }
}
