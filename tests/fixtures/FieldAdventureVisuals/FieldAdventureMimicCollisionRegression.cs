#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>实际网格是独立判据；生产更新后不由夹具同步受击体 Transform。</summary>
    public static class FieldAdventureMimicCollisionRegression
    {
        [Serializable] public sealed class Sample
        {
            public int meals, cases, misses;
            public float yaw, bodyWidth, receiverWidth, bodyDepth, receiverDepth;
            public string phase;
            public Vector3 bodyScale, bodyPosition;
        }
        [Serializable] public sealed class Result
        {
            public int checks, edgeCases, rayHits, sphereHits, swallowOverhangCases, meshCases, projectedEmpty,
                officialDamageHits, directQueriesWithoutSync, upperFaceCases, lowerFaceCases, lidCases, explosionCases;
            public bool autoSyncTransforms, pauseGate, receiverIdentityStable, contraction;
            public string physicsBoundary;
            public List<Sample> samples = new List<Sample>();
        }
        private sealed class Phase
        {
            internal string Name;
            internal float Clock, Swallow, Warning;
            internal Phase(string name, float clock, float swallow = 0f, float warning = 0f)
            { Name = name; Clock = clock; Swallow = swallow; Warning = warning; }
        }
        private sealed class MeshOracle : IDisposable
        {
            internal readonly MeshFilter Source;
            internal readonly GameObject Root;
            internal readonly Mesh Mesh;
            internal readonly MeshCollider Collider;
            internal MeshOracle(MeshFilter source)
            {
                Source = source; Root = new GameObject("IndependentWorldMesh_" + source.name); Root.layer = 9;
                Mesh = new Mesh { name = "WorldVertices_" + source.name }; Collider = Root.AddComponent<MeshCollider>();
            }
            internal void Bake()
            {
                // 烘焙真实顶点到世界空间；只更新自己的几何，不发布生产 Rigidbody 的 Transform。
                Collider.sharedMesh = null;
                Vector3[] vertices = Source.sharedMesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = Source.transform.TransformPoint(vertices[i]);
                Mesh.vertices = vertices; Mesh.triangles = Source.sharedMesh.triangles; Mesh.RecalculateBounds(); Collider.sharedMesh = Mesh;
            }
            public void Dispose() { UnityEngine.Object.Destroy(Root); UnityEngine.Object.Destroy(Mesh); }
        }
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static T Get<T>(object subject, string name) { return (T)subject.GetType().GetField(name, Flags).GetValue(subject); }
        private static void Set(object subject, string name, object value) { subject.GetType().GetField(name, Flags).SetValue(subject, value); }
        private static readonly string[] MainParts = { "ChestBelly", "Gums", "IronBand", "RivetedPlank", "LockPlate", "LockHole", "RoundedLid", "LidLip" };
        private static readonly float[] Radii = { .001f, .02f, .1f };

        internal static Result Run(Action<bool, string> require)
        {
            var result = new Result(); int verboseFailures = 0;
            Action<bool, string> check = (pass, label) =>
            {
                result.checks++;
                // 每个失败状态另有汇总断言，限制重复逐射线日志，全部判据仍计数并参与失败结果。
                if (pass || !label.Contains(" face=") || verboseFailures++ < 128) require(pass, label);
            };
            bool previousAutoSync = Physics.autoSyncTransforms; Physics.autoSyncTransforms = false;
            result.autoSyncTransforms = Physics.autoSyncTransforms;
            result.physicsBoundary = "autoSyncTransforms=false; production Tick/Grow queried immediately, without fixture SyncTransforms/Simulate/FixedUpdate. Oracle MeshColliders contain baked real world vertices. Projectile-style sweep/order/GO-dedup adapter is not full Projectile or game PlayerLoop; ExplosionManager is the actual official source.";
            CharacterMainControl previous = CharacterMainControl.Main;
            var player = new GameObject("MimicCollisionPlayer").AddComponent<CharacterMainControl>();
            player.IsMainCharacter = true; player.Health = player.gameObject.AddComponent<Health>(); player.Health.IsMainCharacterHealth = true;
            CharacterMainControl.Main = player;
            try { for (int meals = 0; meals <= 3; meals++) RunMeals(result, check, player, meals); }
            finally { CharacterMainControl.Main = previous; UnityEngine.Object.Destroy(player.gameObject); Physics.autoSyncTransforms = previousAutoSync; BossRushUI.Paused = false; }
            check(result.swallowOverhangCases == 0, "all visible swallowing main chest surfaces must accept damage");
            check(result.upperFaceCases > 500 && result.lowerFaceCases > 500 && result.lidCases > 1000,
                "native geometry coverage must include upper/lower faces and articulated lid surfaces");
            return result;
        }

        private static void RunMeals(Result result, Action<bool, string> check, CharacterMainControl player, int meals)
        {
            Vector3 origin = new Vector3(7f + meals * 35f, 2f, -5f); player.transform.position = origin + Vector3.forward * 5f;
            GreedyMimicEncounter encounter = null; var meshes = new List<MeshOracle>();
            try
            {
                encounter = new GreedyMimicEncounter(new ModBehaviour(), SceneManager.GetActiveScene(), origin, player);
                GreedyMimicVisual visual = Get<GreedyMimicVisual>(encounter, "visual");
                foreach (MeshFilter filter in visual.Body.GetComponentsInChildren<MeshFilter>(true))
                    if (Array.IndexOf(MainParts, filter.name) >= 0) meshes.Add(new MeshOracle(filter));
                check(meshes.Count == 12, "independent oracle must include all twelve main body/lid mesh instances");
                for (int meal = 0; meal < meals; meal++)
                {
                    GameObject before = Get<GameObject>(encounter, "receiverRoot"); HealthSimpleBase oldHealth = Get<HealthSimpleBase>(encounter, "health");
                    Quaternion yaw = visual.Facing.rotation; BossRushConfirmDialog.Latest = null; encounter.Interact(meal);
                    check(BossRushConfirmDialog.Latest != null, "production feeding entry must open a confirmation");
                    if (BossRushConfirmDialog.Latest == null) return;
                    BossRushConfirmDialog.Latest.OnConfirm(); GameObject after = Get<GameObject>(encounter, "receiverRoot");
                    check(encounter.Rules.Meals == meal + 1, "actual feeding callback must advance growth");
                    check(before == after || !before.activeSelf, "growth must reuse or immediately deactivate its prior receiver");
                    check(Quaternion.Angle(visual.Facing.rotation, yaw) < .001f, "growth must retain yaw before next Tick");
                    check(Mathf.Abs(Get<HealthSimpleBase>(encounter, "health").HealthValue - encounter.Rules.MaximumHealth) < .001f,
                        "official health must expose new grown maximum immediately");
                    if (before != after) check(typeof(HealthSimpleBase).GetField("OnHurtEvent", Flags).GetValue(oldHealth) == null,
                        "replaced growth receiver must release official health subscription");
                    Vector3 grownScale = visual.Body.localScale, grownPosition = visual.Body.localPosition;
                    Transform lid = visual.Body.Find("HingedLid"); Quaternion grownLid = lid.localRotation;
                    visual.Tick(0f, player.transform.position, Get<bool>(encounter, "fighting"), 1f, 0f, GreedyMimicAttack.Bite);
                    check(Vector3.Distance(grownScale, visual.Body.localScale) < .0001f && Vector3.Distance(grownPosition, visual.Body.localPosition) < .0001f
                        && Quaternion.Angle(grownLid, lid.localRotation) < .001f, "growth callback must fully apply visible pose before any visual Tick");
                    CaptureGeometry(result, check, encounter, visual, player, meshes, after.GetComponent<BoxCollider>(), "growth callback before Tick", false);
                }
                GameObject receiverRoot = Get<GameObject>(encounter, "receiverRoot"); BoxCollider box = receiverRoot.GetComponent<BoxCollider>();
                DamageReceiver receiver = Get<DamageReceiver>(encounter, "receiver"); HealthSimpleBase health = Get<HealthSimpleBase>(encounter, "health");
                check(receiverRoot.GetComponents<Collider>().Length == 1 && receiver.simpleHealth == health && health.dmgReceiver == receiver,
                    "native main body must retain one collider and one official receiver/health");
                if (meals == 3)
                {
                    check(Get<bool>(encounter, "fighting"), "third actual feeding callback must immediately start combat");
                    CaptureGeometry(result, check, encounter, visual, player, meshes, box, "third meal before Tick");
                }
                var phases = new List<Phase>
                {
                    new Phase("breathing expanded hopping high", Mathf.PI / 4.8f), new Phase("breathing contracted hopping low", Mathf.PI * 3f / 4.8f),
                    new Phase("idle near end", 2.1f), new Phase("warning open lid", 1.2f, 0f, 1f)
                };
                if (meals > 0) phases.AddRange(new[]
                {
                    new Phase("swallow start", .4f, 1.399f), new Phase("swallow positive peak", .81f, (Mathf.PI * 6f + Mathf.PI * .5f) / 17f),
                    new Phase("swallow negative peak", 1.2f, (Mathf.PI * 4f + Mathf.PI * 1.5f) / 17f),
                    new Phase("swallow late negative", 1.49f, Mathf.PI * 1.5f / 17f), new Phase("swallow late positive", 1.67f, Mathf.PI * .5f / 17f),
                    new Phase("swallow almost finished", 1.7999f, .0001f)
                });
                foreach (Phase phase in phases)
                    for (int direction = 0; direction < 8; direction++)
                    {
                        float yaw = direction * 45f; player.transform.position = origin + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 20f;
                        Set(encounter, "cooldown", 1000f); Set(encounter, "warningLeft", phase.Warning); Set(encounter, "attack", GreedyMimicAttack.Bite); Set(encounter, "flashLeft", 0f);
                        Set(visual, "clock", phase.Clock - .4f); Set(visual, "swallow", phase.Swallow > 0 ? phase.Swallow + .4f : 0f);
                        Quaternion locked = visual.Facing.rotation; encounter.Tick(.4f, player);
                        if (phase.Warning > 0) check(Quaternion.Angle(visual.Facing.rotation, locked) < .001f, "warning must preserve locked yaw");
                        else check(Vector3.Distance(visual.Body.forward, (player.transform.position - origin).normalized) < .0001f,
                            "production Tick must turn visible chest toward moved player");
                        CaptureGeometry(result, check, encounter, visual, player, meshes, box, phase.Name);
                        check(Get<GameObject>(encounter, "receiverRoot") == receiverRoot && receiverRoot.GetComponent<BoxCollider>() == box
                            && receiverRoot.GetComponents<Collider>().Length == 1 && Get<HealthSimpleBase>(encounter, "health") == health
                            && Get<DamageReceiver>(encounter, "receiver") == receiver, "animation ticks must preserve receiver health and collider identity");
                    }
                result.receiverIdentityStable = true; CheckContraction(result, check, encounter, visual, player, box); CheckPause(result, check, encounter, visual, player, box);
                CheckExplosion(result, check, encounter, player, box);
                if (meals == 3) RecoveryFromMesh(result, check, encounter, visual, player, meshes, box);
                GameObject root = visual.Root; encounter.Dispose(); encounter.Dispose();
                check(!root.activeSelf && !receiverRoot.activeSelf && Get<GameObject>(encounter, "receiverRoot") == null,
                    "Dispose must immediately deactivate collision hierarchy and clear owner");
                check(typeof(HealthSimpleBase).GetField("OnHurtEvent", Flags).GetValue(health) == null
                    && typeof(HealthSimpleBase).GetField("OnDeadEvent", Flags).GetValue(health) == null, "Dispose must unsubscribe both official health callbacks");
                RaycastHit cleanupHit;
                check(!box.Raycast(new Ray(origin + Vector3.forward * 20 + Vector3.up, Vector3.back), out cleanupHit, 40),
                    "disposed native collider must reject further projectiles immediately");
            }
            finally { if (encounter != null) encounter.Dispose(); foreach (MeshOracle mesh in meshes) mesh.Dispose(); }
        }

        private static void CaptureGeometry(Result result, Action<bool, string> check, GreedyMimicEncounter encounter, GreedyMimicVisual visual,
            CharacterMainControl player, List<MeshOracle> meshes, BoxCollider box, string phase, bool damage = true)
        {
            int before = result.meshCases, misses = 0, upperMisses = 0, lowerMisses = 0; bool swallowing = phase.StartsWith("swallow");
            foreach (MeshOracle mesh in meshes)
            {
                mesh.Bake(); Bounds local = mesh.Source.sharedMesh.bounds;
                for (int axis = 0; axis < 3; axis++) foreach (int sign in new[] { -1, 1 })
                    foreach (float first in new[] { -.985f, 0f, .985f }) foreach (float second in new[] { -.985f, 0f, .985f })
                    {
                        Vector3 point = local.center, normal = Vector3.zero;
                        point[axis] += local.extents[axis] * sign; normal[axis] = sign;
                        point[(axis + 1) % 3] += local.extents[(axis + 1) % 3] * first; point[(axis + 2) % 3] += local.extents[(axis + 2) % 3] * second;
                        point = mesh.Source.transform.TransformPoint(point); normal = mesh.Source.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(normal).normalized;
                        Vector3 tangent = axis == 1 ? visual.Body.forward : Vector3.up;
                        foreach (float slope in new[] { -.55f, 0f, .55f })
                        {
                            Vector3 direction = (normal + tangent * slope).normalized; Ray ray = new Ray(point + direction * 20f, -direction); RaycastHit meshHit;
                            if (!mesh.Collider.Raycast(ray, out meshHit, 40f)) { result.projectedEmpty++; continue; }
                            result.meshCases++;
                            if (axis == 1) { if (sign > 0) result.upperFaceCases++; else result.lowerFaceCases++; }
                            if (mesh.Source.name == "RoundedLid" || mesh.Source.name == "LidLip") result.lidCases++;
                            string detail = " meals=" + encounter.Rules.Meals + " phase=" + phase + " part=" + mesh.Source.name
                                + " face=" + axis + "/" + sign + " edge=" + first + "/" + second + " slope=" + slope;
                            RaycastHit hit; bool rayHit = box.Raycast(ray, out hit, meshHit.distance + .03f);
                            check(rayHit, "mimic visible main mesh ray must reach damage receiver" + detail); result.directQueriesWithoutSync++;
                            if (rayHit) { result.rayHits++; if (damage) Dispatch(result, check, encounter, player, hit, .001f, detail); }
                            else { misses++; if (axis == 1) { if (sign > 0) upperMisses++; else lowerMisses++; } if (swallowing) result.swallowOverhangCases++; }
                            foreach (float radius in Radii)
                            {
                                bool sphereHit = ProjectileHit(box, ray, radius, meshHit.distance + .03f, out hit);
                                check(sphereHit, "mimic visible main mesh sphere must reach damage receiver radius=" + radius + detail); result.directQueriesWithoutSync++;
                                if (sphereHit) { result.sphereHits++; if (damage) Dispatch(result, check, encounter, player, hit, .001f, detail); } else misses++;
                            }
                            result.edgeCases++;
                        }
                    }
            }
            Bounds main = VertexBounds(meshes, visual.Facing);
            check(Vector3.Distance(box.transform.lossyScale, Vector3.one) < .0001f, "receiver transform must keep unit scale for one growth application");
            check(box.size.x <= main.size.x + .03f && box.size.y <= main.size.y + .6f * encounter.Rules.Scale && box.size.z <= main.size.z + .6f * encounter.Rules.Scale,
                "receiver envelope must remain near actual mesh vertices without duplicate growth scale");
            check(misses == 0, "visible mesh coverage phase=" + phase + " meals=" + encounter.Rules.Meals + " yaw=" + visual.Facing.eulerAngles.y + " misses=" + misses);
            if (swallowing)
            {
                check(upperMisses == 0, "swallow upper face ray must reach damage receiver phase=" + phase + " meals=" + encounter.Rules.Meals);
                check(lowerMisses == 0, "swallow lower face ray must reach damage receiver phase=" + phase + " meals=" + encounter.Rules.Meals);
            }
            result.samples.Add(new Sample { meals = encounter.Rules.Meals, yaw = visual.Facing.eulerAngles.y, phase = phase, cases = result.meshCases - before, misses = misses,
                bodyScale = visual.Body.localScale, bodyPosition = visual.Body.localPosition, bodyWidth = main.size.x, bodyDepth = main.size.z,
                receiverWidth = box.size.x, receiverDepth = box.size.z });
        }
        private static Bounds VertexBounds(List<MeshOracle> meshes, Transform facing)
        {
            Bounds bounds = new Bounds(); bool initialized = false;
            foreach (MeshOracle mesh in meshes) foreach (Vector3 vertex in mesh.Source.sharedMesh.vertices)
            {
                Vector3 point = facing.InverseTransformPoint(mesh.Source.transform.TransformPoint(vertex));
                if (!initialized) { bounds = new Bounds(point, Vector3.zero); initialized = true; } else bounds.Encapsulate(point);
            }
            return bounds;
        }
        private static bool ProjectileHit(BoxCollider box, Ray ray, float radius, float length, out RaycastHit hit)
        {
            // 官方 Projectile 的起点后退 .1m、末端冗余 .3m、SphereCastAll、距离排序和 GO 去重语义。
            RaycastHit[] hits = Physics.SphereCastAll(ray.origin - ray.direction * .1f, radius, ray.direction, length + .3f,
                1 << box.gameObject.layer, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance)); var damaged = new HashSet<GameObject>(); hit = new RaycastHit();
            foreach (RaycastHit candidate in hits)
            {
                if (!damaged.Add(candidate.collider.gameObject)) continue;
                DamageReceiver receiver = candidate.collider.GetComponent<DamageReceiver>();
                if (candidate.collider.gameObject == box.gameObject && receiver != null && receiver.Team != Teams.player) { hit = candidate; return true; }
            }
            return false;
        }
        private static void Dispatch(Result result, Action<bool, string> check, GreedyMimicEncounter encounter, CharacterMainControl player,
            RaycastHit hit, float damage, string detail)
        {
            HealthSimpleBase health = Get<HealthSimpleBase>(encounter, "health"); float before = health.HealthValue; DamageReceiver receiver = hit.collider.GetComponent<DamageReceiver>();
            check(receiver == Get<DamageReceiver>(encounter, "receiver"), "physical hit must resolve official receiver on collider GO" + detail);
            if (receiver == null) return;
            receiver.Hurt(new DamageInfo(player) { damageValue = damage, critRate = -1f, damagePoint = hit.point, damageNormal = hit.normal });
            check(Mathf.Abs(before - health.HealthValue - damage) < .0001f, "native mesh hit must deduct health through official DamageReceiver" + detail); result.officialDamageHits++;
        }
        private static void CheckContraction(Result result, Action<bool, string> check, GreedyMimicEncounter encounter, GreedyMimicVisual visual, CharacterMainControl player, BoxCollider box)
        {
            Set(encounter, "cooldown", 1000f); Set(encounter, "warningLeft", 0f); Set(encounter, "flashLeft", 0f);
            Set(visual, "swallow", (Mathf.PI * 6 + Mathf.PI * .5f) / 17f + .01f); encounter.Tick(.01f, player); float expanded = box.size.x;
            Set(visual, "swallow", (Mathf.PI * 4 + Mathf.PI * 1.5f) / 17f + .01f); encounter.Tick(.01f, player);
            check(box.size.x < expanded - .3f * encounter.Rules.Scale, "contracted receiver must shrink after swallow expansion");
            Vector3 outside = box.transform.TransformPoint(box.center + new Vector3(expanded * .5f - .02f, 0, 10f)); RaycastHit ignored;
            check(!box.Raycast(new Ray(outside, -box.transform.forward), out ignored, 20f), "contracted receiver must reject empty air inside previous expanded width"); result.contraction = true;
        }
        private static void CheckPause(Result result, Action<bool, string> check, GreedyMimicEncounter encounter, GreedyMimicVisual visual, CharacterMainControl player, BoxCollider box)
        {
            var module = new GreedyMimicRuntimeModule(); module.OnAwake(new ModBehaviour()); Set(module, "encounter", encounter);
            Vector3 size = box.size, center = box.center, bodyScale = visual.Body.localScale, position = visual.Body.localPosition; float swallow = Get<float>(visual, "swallow");
            BossRushUI.Paused = true; for (int i = 0; i < 5; i++) module.OnUpdate(.1f, .1f);
            check(box.size == size && box.center == center && visual.Body.localScale == bodyScale && visual.Body.localPosition == position
                && Get<float>(visual, "swallow") == swallow, "actual runtime pause gate must freeze animation and receiver geometry");
            BossRushUI.Paused = false; module.OnUpdate(.05f, .05f); check(Get<float>(visual, "swallow") < swallow, "unpaused runtime must resume swallowing updates");
            Set(module, "encounter", null); result.pauseGate = true;
        }
        private static void CheckExplosion(Result result, Action<bool, string> check, GreedyMimicEncounter encounter, CharacterMainControl player, BoxCollider box)
        {
            var root = new GameObject("ActualOfficialExplosionManager"); var manager = root.AddComponent<ExplosionManager>();
            HealthSimpleBase health = Get<HealthSimpleBase>(encounter, "health"); float before = health.HealthValue;
            manager.CreateExplosion(box.transform.TransformPoint(box.center), 20f, new DamageInfo(player) { damageValue = 3f, critRate = -1f }, (ExplosionFxTypes)(-1), 0f, true);
            check(Mathf.Abs(before - health.HealthValue - 3f) < .001f, "actual official explosion must deduct exactly one damage instance from simpleHealth");
            result.explosionCases++; UnityEngine.Object.Destroy(root);
        }
        private static void RecoveryFromMesh(Result result, Action<bool, string> check, GreedyMimicEncounter encounter, GreedyMimicVisual visual,
            CharacterMainControl player, List<MeshOracle> meshes, BoxCollider box)
        {
            Set(encounter, "attackTurn", 5); Set(encounter, "warningLeft", 0f); Set(encounter, "flashLeft", 0f); Set(encounter, "cooldown", 0f); encounter.Tick(.01f, player);
            check(Get<GreedyMimicAttack>(encounter, "attack") == GreedyMimicAttack.Recovery && Get<float>(encounter, "warningLeft") > 0,
                "real learned attack turn must begin recovery charge");
            Set(visual, "swallow", (Mathf.PI * 6 + Mathf.PI * .5f) / 17f + .01f); encounter.Tick(.01f, player);
            MeshOracle belly = meshes.Find(mesh => mesh.Source.name == "ChestBelly"); belly.Bake();
            Vector3 point = belly.Source.transform.TransformPoint(new Vector3(.485f, 0, .5f)); Ray ray = new Ray(point + visual.Body.forward * 20f, -visual.Body.forward);
            RaycastHit meshHit, hit; check(belly.Collider.Raycast(ray, out meshHit, 40f), "recovery interrupt ray must hit actual swollen mesh edge");
            bool found = ProjectileHit(box, ray, .001f, 40f, out hit); check(found, "recovery interrupt swollen edge must physically reach official receiver");
            if (found) Dispatch(result, check, encounter, player, hit, 45f, " recovery swollen outer edge");
            check(Mathf.Abs(Get<float>(encounter, "recoveryDamage") - 45f) < .001f, "physical swollen edge damage must earn actual recovery interruption credit");
            float after = Get<HealthSimpleBase>(encounter, "health").HealthValue; encounter.Tick(.01f, player);
            check(Get<float>(encounter, "warningLeft") == 0f && FieldAdventureSupport.Notices.Contains("The bottle jams! Healing interrupted."),
                "physical swollen edge shot must cancel actual recovery charge");
            encounter.Tick(2.8f, player); check(Mathf.Abs(Get<HealthSimpleBase>(encounter, "health").HealthValue - after) < .001f,
                "interrupted mesh-edge shot must prevent official health recovery");
        }
    }
}
#endif
