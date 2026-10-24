using System;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static int assertions, scenarios;
    private static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new Exception(label);
    }
    private static bool Near(Vector3 a, Vector3 b) { return Vector3.Distance(a, b) < 0.0001f; }
    private static void Reset()
    {
        scenarios++;
        Physics.Reset();
        FieldAdventureSupport.Allowed = true;
        BossRushUI.Paused = false;
        ModBehaviour.Errors.Clear();
    }
    private static DangerousPrototypeRuntimeModule Setup(Vector3 playerPosition, Vector3 muzzlePosition,
        Vector3 forward, Vector3 aim, float moveability, DangerousPrototypeKind kind,
        out CharacterMainControl player, out ItemAgent_Gun gun)
    {
        Reset();
        player = new CharacterMainControl { Aim = aim, CharacterMoveability = moveability, Team = Teams.player };
        player.transform.position = playerPosition;
        gun = new ItemAgent_Gun { Holder = player };
        gun.muzzle.position = muzzlePosition; gun.muzzle.forward = forward.normalized;
        player.CurrentHoldItemAgent = gun;
        return new DangerousPrototypeRuntimeModule(player, gun, kind);
    }
    private static Vector3 Official(CharacterMainControl player, ItemAgent_Gun gun)
    {
        return new OfficialGunFixture { Holder = player, muzzle = gun.muzzle }.Direction();
    }
    private static void DirectionCases()
    {
        foreach (float z in new[] { 0f, 0.2f, 0.5f, 0.8f, 1f, 1.05f, 1.1f, 1.1002f, 2f, 50f, -10f })
        foreach (float height in new[] { -10f, 0.5f, 10f })
        foreach (float mobility in new[] { 0f, 0.5f, 0.5001f, 1f })
        foreach (DangerousPrototypeKind kind in new[] { DangerousPrototypeKind.JetCannon, DangerousPrototypeKind.RicochetSun })
        {
            CharacterMainControl player; ItemAgent_Gun gun;
            var runtime = Setup(Vector3.zero, new Vector3(0f, 0.5f, 1f), Vector3.forward,
                new Vector3(0f, height, z), mobility, kind, out player, out gun);
            Vector3 expected = Official(player, gun);
            runtime.Fire(gun);
            Check(ModBehaviour.Errors.Count == 0, "firing must not silently catch a fixture or production exception");
            Check(Physics.Rays.Count == 1, "charged shot executes actual TraceBlast once without walls");
            Check(Near(Physics.Rays[0].Direction, expected), "prototype direction must match official near/moveability/high-ground gate");
            Check(Near(Physics.Rays[0].Origin, gun.muzzle.position), "unobstructed shot keeps actual muzzle position");
            Check(Math.Abs(Physics.Rays[0].Direction.magnitude - 1f) < 0.0001f, "shot direction stays normalized");
            if (kind == DangerousPrototypeKind.JetCannon)
            {
                Vector3 recoil = -Vector3.ProjectOnPlane(expected, Vector3.up).normalized;
                Check(Near(runtime.RecoilDirection, recoil), "jet recoil opposes actual official horizontal firing direction");
                runtime.Recoil(0.02f);
                Check(Near(player.ForceVelocity.normalized, recoil), "actual recoil tick moves in the stored reverse direction");
                runtime.Recoil(0.3f);
                Check(Near(player.ForceVelocity, Vector3.zero), "recoil expiry clears forced velocity");
            }
            else Check(Near(runtime.RecoilDirection, Vector3.zero), "ricochet sun does not start jet recoil");
            runtime.Fire(gun);
            Check(Physics.Rays.Count == 1, "uncharged repeated event does not emit another beam");
        }
        // Translation and horizontal rotation must not alter the official distance gate.
        foreach (Vector3 forward in new[] { new Vector3(1f, 0f, 0f), new Vector3(-0.6f, 0.2f, 0.8f) })
        foreach (float distance in new[] { 0.5f, 1.1f, 20f })
        {
            CharacterMainControl player; ItemAgent_Gun gun;
            Vector3 origin = new Vector3(140f, 7f, -85f);
            var runtime = Setup(origin, origin + forward.normalized, forward, origin + forward.normalized * distance,
                1f, DangerousPrototypeKind.JetCannon, out player, out gun);
            runtime.Fire(gun);
            Check(Near(Physics.Rays[0].Direction, Official(player, gun)), "translated and rotated firing uses official geometry");
        }
    }
    private static void WallCases()
    {
        foreach (Vector3 aim in new[] { new Vector3(0f, 0.5f, -10f), new Vector3(0f, 0.5f, 20f),
            new Vector3(20f, 0.5f, 1f), new Vector3(0f, 25f, 5f), new Vector3(0f, -25f, 5f) })
        foreach (Vector3 normal in new[] { new Vector3(0f, 0f, -1f), new Vector3(0.6f, 0.4f, -1f) })
        foreach (float fraction in new[] { 0.02f, 0.5f, 0.98f })
        {
            CharacterMainControl player; ItemAgent_Gun gun;
            var runtime = Setup(Vector3.zero, new Vector3(0f, 0.5f, 1f), Vector3.forward,
                aim, 1f, DangerousPrototypeKind.JetCannon, out player, out gun);
            Vector3 chest = player.transform.position + Vector3.up * 0.7f;
            var wall = new PlaneWall { Point = chest + (gun.muzzle.position - chest) * fraction, Normal = normal.normalized };
            Physics.Walls.Add(wall);
            runtime.Fire(gun);
            Check(ModBehaviour.Errors.Count == 0, "wall case executes complete production OnShot and TraceBlast");
            Check(Physics.Rays.Count == 1, "jet ray stops at first wall");
            Vector3 start = Physics.Rays[0].Origin;
            Check(wall.Side(start) > 0.00001f, "obstructed origin stays on the character side for backward/tangent/high-ground shots");
            Vector3 inspectionDirection = (gun.muzzle.position - chest).normalized;
            Check(Vector3.ProjectOnPlane(start - chest, inspectionDirection).magnitude < 0.0001f,
                "obstructed origin remains on the checked chest-to-muzzle segment");
            Check(Vector3.Dot(start - chest, inspectionDirection) >= -0.00001f,
                "short obstruction retreat never overshoots behind the chest");
            Check(Near(Physics.Rays[0].Direction, Official(player, gun)), "wall correction changes origin without changing shot direction");
        }
        foreach (DangerousPrototypeKind kind in new[] { DangerousPrototypeKind.JetCannon, DangerousPrototypeKind.RicochetSun })
        {
            CharacterMainControl player; ItemAgent_Gun gun;
            var runtime = Setup(Vector3.zero, new Vector3(0f, 0.5f, 1f), Vector3.forward,
                new Vector3(0f, 0.5f, 20f), 1f, kind, out player, out gun);
            Physics.Walls.Add(new PlaneWall { Point = new Vector3(0f, 0f, 0.5f), Normal = -Vector3.forward });
            var health = new Health();
            Physics.Targets.Add(new Collider { bounds = new Bounds { center = new Vector3(0f, 0.5f, 2f) },
                Receiver = new DamageReceiver { health = health, Team = Teams.wolf } });
            runtime.Fire(gun);
            Check(health.CurrentHealth == 1000f && runtime.Progress == 0, "muzzle beyond wall does not hit or count a target behind the wall");
            if (kind == DangerousPrototypeKind.RicochetSun)
            {
                Check(Physics.Rays.Count == 2, "ricochet traces the reflected leg after the wall");
                Check(Physics.Rays[1].Direction.z < 0f && Physics.Rays[1].Origin.z < 0.5f,
                    "ricochet remains on the near side and reflects away from the wall");
            }
        }
    }
    private static void SafetyCases()
    {
        foreach (int blocked in new[] { 0, 1, 2 })
        {
            CharacterMainControl player; ItemAgent_Gun gun;
            var runtime = Setup(Vector3.zero, new Vector3(0f, 0.5f, 1f), Vector3.forward,
                new Vector3(0f, 0.5f, 20f), 1f, DangerousPrototypeKind.JetCannon, out player, out gun);
            runtime.Fire(gun);
            if (blocked == 0) Physics.RecoilBlocked = true;
            if (blocked == 1) Physics.GroundAvailable = false;
            if (blocked == 2) player.Swimming = true;
            runtime.Recoil(0.02f);
            Check(Near(player.ForceVelocity, Vector3.zero), "blocked, ungrounded or swimming recoil must stop");
        }
        foreach (int rejection in new[] { 0, 1, 2, 3, 4 })
        {
            CharacterMainControl player; ItemAgent_Gun gun;
            var runtime = Setup(Vector3.zero, new Vector3(0f, 0.5f, 1f), Vector3.forward,
                new Vector3(0f, 0.5f, 0.5f), 1f, DangerousPrototypeKind.JetCannon, out player, out gun);
            if (rejection == 0) BossRushUI.Paused = true;
            if (rejection == 1) FieldAdventureSupport.Allowed = false;
            if (rejection == 2) player.CurrentHoldItemAgent = null;
            if (rejection == 3) gun.Holder = null;
            if (rejection == 4) UnityEngine.Object.Destroy(player.gameObject);
            runtime.Fire(gun);
            Check(Physics.Rays.Count == 0, "invalid lease/raid/pause/destroyed player cannot start a prototype shot: " + rejection);
            Check(ModBehaviour.Errors.Count == 0, "invalid owner is rejected before dereferencing");
            if (rejection == 4) Check(player == null && player.gameObject.Destroyed, "destroyed GameObject also destroys player component");
        }
    }
    private static int Main()
    {
        DirectionCases(); WallCases(); SafetyCases();
        Console.WriteLine("FieldAdventurePrototypeRuntime: " + scenarios + " scenarios / " + assertions + " assertions PASS");
        return 0;
    }
}
