using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 净化点拾取控制器。
    /// 挂在每个净化点 GameObject 上，负责旋转、脉冲、磁吸拾取等运行时行为。
    /// 视觉外观复用原版 SoulCube prefab（若可获取），否则回退到发光球体。
    /// </summary>
    public sealed class ZombiePurificationPointController : MonoBehaviour
    {
        private const float PICKUP_DISTANCE = ZombieModeTuning.StarPickupDistance;
        private const float AUTO_COLLECT_SECONDS = ZombieModeTuning.SettlementMaxWaitSeconds;

        public int RunId;
        public int Value;
        public ZombiePurificationStar StarRecord;
        private bool collected;
        private float lifeTime;
        private Vector3 baseScale;

        public void Initialize(int runId, int value, ZombiePurificationStar starRecord)
        {
            RunId = runId;
            Value = value;
            StarRecord = starRecord;
            baseScale = transform.localScale;
        }

        private void Update()
        {
            if (collected)
            {
                return;
            }

            ModBehaviour inst = ModBehaviour.Instance;
            if (inst != null && inst.IsZombieModeRuntimePaused())
            {
                return;
            }

            lifeTime += Time.deltaTime;
            float pulse = 1f + Mathf.Sin(Time.time * 7f) * 0.12f;
            transform.localScale = baseScale * pulse;
            transform.Rotate(Vector3.up, 120f * Time.deltaTime, Space.World);

            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null)
            {
                float distanceToPlayer = Vector3.Distance(player.transform.position, transform.position);
                if (distanceToPlayer <= ZombieModeTuning.StarMagnetRadius)
                {
                    float speed = Mathf.Lerp(4f, 18f, 1f - Mathf.Clamp01(distanceToPlayer / ZombieModeTuning.StarMagnetRadius));
                    transform.position = Vector3.MoveTowards(
                        transform.position,
                        player.transform.position + Vector3.up * 0.8f,
                        speed * Time.unscaledDeltaTime);
                }

                if ((player.transform.position - transform.position).sqrMagnitude <= PICKUP_DISTANCE * PICKUP_DISTANCE)
                {
                    Collect();
                    return;
                }
            }

            if (lifeTime >= AUTO_COLLECT_SECONDS)
            {
                Collect();
            }
        }

        private void Collect()
        {
            collected = true;
            ModBehaviour inst = ModBehaviour.Instance;
            if (inst != null)
            {
                inst.CollectZombieModePurificationPoint(RunId, Value, gameObject, StarRecord);
            }
        }
    }

    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // ─── 复用原版 SoulCube prefab 的缓存 ───
        private static SoulCube s_cachedSoulCubePrefab;
        private static bool s_soulCubePrefabSearched;
        private static bool s_soulCubePrefabLoggedOnce;

        /// <summary>
        /// 尝试查找原版 SoulCube prefab 并缓存。
        /// SoulCollector（灵魂收集器背包）上序列化引用了 cubePfb，
        /// 通过 Resources.FindObjectsOfTypeAll 可以在加载时找到它。
        /// </summary>
        private static SoulCube TryGetSoulCubePrefab()
        {
            if (s_soulCubePrefabSearched)
            {
                return s_cachedSoulCubePrefab;
            }

            s_soulCubePrefabSearched = true;

            // 优先从 SoulCollector 的序列化字段获取 prefab（最可靠）
            SoulCollector[] collectors = Resources.FindObjectsOfTypeAll<SoulCollector>();
            if (collectors != null)
            {
                for (int i = 0; i < collectors.Length; i++)
                {
                    if (collectors[i] != null && collectors[i].cubePfb != null)
                    {
                        s_cachedSoulCubePrefab = collectors[i].cubePfb;
                        return s_cachedSoulCubePrefab;
                    }
                }
            }

            // 回退：直接搜索 SoulCube prefab（仅取不在场景中的 prefab 资源）
            SoulCube[] cubes = Resources.FindObjectsOfTypeAll<SoulCube>();
            if (cubes != null)
            {
                for (int i = 0; i < cubes.Length; i++)
                {
                    if (cubes[i] != null &&
                        cubes[i].gameObject != null &&
                        !cubes[i].gameObject.scene.IsValid())
                    {
                        s_cachedSoulCubePrefab = cubes[i];
                        return s_cachedSoulCubePrefab;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 在丧尸模式初始化阶段预热 prefab 查找，避免掉点热路径做全局扫描。
        /// </summary>
        private static void PrewarmSoulCubePrefabCache()
        {
            if (!s_soulCubePrefabSearched)
            {
                TryGetSoulCubePrefab();
            }
        }

        /// <summary>
        /// 每局允许重新打印视觉路径日志，但不强制重新全局搜索 prefab。
        /// </summary>
        private void PrepareSoulCubePrefabCacheForZombieRun()
        {
            s_soulCubePrefabLoggedOnce = false;
            PrewarmSoulCubePrefabCache();
        }

        private bool CreateZombieModePurificationPoint(int runId, Vector3 position, int value)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            try
            {
                SoulCube prefab = TryGetSoulCubePrefab();
                GameObject point;

                if (prefab != null)
                {
                    // ─── 复用原版 SoulCube 视觉 ───
                    // 实例化前先禁用 prefab，防止 SoulCube.Update 在 Instantiate 后、
                    // DestroyImmediate 前的同一帧执行（target==null 会导致自毁）。
                    bool prefabWasActive = prefab.gameObject.activeSelf;
                    prefab.gameObject.SetActive(false);

                    point = Object.Instantiate(prefab.gameObject);

                    prefab.gameObject.SetActive(prefabWasActive);

                    point.name = "ZombieMode_PurificationPoint";
                    point.transform.position = position + Vector3.up * 0.65f;
                    point.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);

                    // 必须用 DestroyImmediate 移除原版 SoulCube 脚本——
                    // SoulCube.Update 中 target==null 时会 Destroy(gameObject) 导致净化点自毁。
                    SoulCube originalScript = point.GetComponent<SoulCube>();
                    if (originalScript != null)
                    {
                        Object.DestroyImmediate(originalScript);
                    }

                    // 移除 collider（不需要物理碰撞，拾取靠距离检测）
                    Collider[] colliders = point.GetComponentsInChildren<Collider>(true);
                    for (int i = 0; i < colliders.Length; i++)
                    {
                        if (colliders[i] != null)
                        {
                            Object.Destroy(colliders[i]);
                        }
                    }

                    // 启用实例
                    point.SetActive(true);

                    if (!s_soulCubePrefabLoggedOnce)
                    {
                        s_soulCubePrefabLoggedOnce = true;
                        DevLog("[ZombieMode] 净化点视觉：使用原版 SoulCube prefab");
                    }
                }
                else
                {
                    // ─── 回退：原版 prefab 不可用时使用发光球体 ───
                    point = CreatePurificationPointFallbackVisual(position);
                    if (!s_soulCubePrefabLoggedOnce)
                    {
                        s_soulCubePrefabLoggedOnce = true;
                        DevLog("[ZombieMode] 净化点视觉：SoulCube prefab 未找到，使用回退发光球体");
                    }
                }

                ZombiePurificationStar star = new ZombiePurificationStar();
                star.RunId = runId;
                star.PointsValue = Mathf.Max(1, value);
                star.SpawnPosition = point.transform.position;
                star.SpawnTime = GetZombieModeRuntimeNow();
                star.Visual = point;
                zombieModeRunState.PendingPurificationStars.Add(star);

                ZombiePurificationPointController controller = point.AddComponent<ZombiePurificationPointController>();
                controller.Initialize(runId, Mathf.Max(1, value), star);
                RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.PurificationPoint, point, controller, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 回退视觉：当 SoulCube prefab 不可用时，创建带发光材质的球体。
        /// </summary>
        private GameObject CreatePurificationPointFallbackVisual(Vector3 position)
        {
            GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            point.name = "ZombieMode_PurificationPoint";
            point.transform.position = position + Vector3.up * 0.65f;
            point.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);

            Collider collider = point.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            Renderer renderer = point.GetComponent<Renderer>();
            if (renderer != null)
            {
                SetZombieModeRendererColor(renderer, new Color(0.25f, 1f, 0.65f, 0.9f));
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return point;
        }

        private bool HasZombieModePendingPurificationStars()
        {
            return zombieModeRunState.PendingPurificationStars.Count > 0;
        }

        private void ForceCollectZombieModePendingPurificationStars(int runId)
        {
            if (!IsZombieModeRunValid(runId) || zombieModeRunState.PendingPurificationStars.Count <= 0)
            {
                return;
            }

            ZombiePurificationStar[] pending = zombieModeRunState.PendingPurificationStars.ToArray();
            for (int i = 0; i < pending.Length; i++)
            {
                ZombiePurificationStar star = pending[i];
                if (star == null || star.Settled)
                {
                    continue;
                }

                CollectZombieModePurificationPoint(runId, star.PointsValue, star.Visual, star);
            }
        }

        public void CollectZombieModePurificationPoint(int runId, int value, GameObject pointObject, ZombiePurificationStar starRecord)
        {
            if (starRecord != null && starRecord.Settled)
            {
                return;
            }

            if (pointObject != null)
            {
                try { Destroy(pointObject); } catch (System.Exception e) { Debug.LogWarning("[ZombieMode] PurificationPoint Destroy 失败: " + e.Message); }
            }

            if (starRecord != null)
            {
                starRecord.Settled = true;
                starRecord.Visual = null;
                zombieModeRunState.PendingPurificationStars.Remove(starRecord);
            }

            if (!IsZombieModeRunValid(runId))
            {
                return;
            }

            zombieModeRunState.PurificationPoints += Mathf.Max(1, value);
        }
    }
}
