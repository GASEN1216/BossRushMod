using UnityEngine;

namespace BossRush
{
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
        private bool CreateZombieModePurificationPoint(int runId, Vector3 position, int value)
        {
            if (!IsZombieModeRunValid(runId))
            {
                return false;
            }

            try
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
