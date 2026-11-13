using System;
using BossRush;
using UnityEngine;

internal static class TidegateGroundCases
{
    internal static void Run()
    {
        var root = new GameObject("TidegateWorld").transform;
        Transform markers = Add(root, "Markers");
        Add(markers, "PlayerSpawn").position = new Vector3(0f, 1f, 0f);
        Add(Add(markers, "Exits"), "Exit_road").position = new Vector3(0f, 1f, 0f);
        Add(Add(markers, "Loot"), "Loot_test");
        Add(Add(markers, "Spawns"), "Spawn_test");
        Transform terrain = Add(Add(root, "Visual"), "VIS_terrain");
        Collider bed = Surface(terrain, "COL_Ground_Terrain", 1f);
        Transform collision = Add(root, "Collision");
        Collider bridge = Surface(collision, "COL_Ground_Decks", 1.957f);
        bridge.MinX = 22f;
        bridge.MaxX = 42f;
        Collider underside = Surface(collision, "COL_Ground_BackFace", 1.85f);
        underside.Normal = Vector3.down;
        Collider disabled = Surface(collision, "COL_Ground_Disabled", 1.02f);
        disabled.enabled = false;
        Collider trigger = Surface(collision, "COL_Ground_Trigger", 1.02f);
        trigger.isTrigger = true;
        Collider wall = Surface(collision, "COL_Wall_Rails", 1.02f);
        Transform hiddenGround = Add(collision, "HiddenGround");
        Surface(hiddenGround, "COL_Ground_Unused", 1.01f);
        hiddenGround.gameObject.SetActive(false);
        Transform objects = Add(collision, "COL_Objects");
        const string deskTop = "COL_Prop_O_B_hall_furniture_8_0";
        const string deskSide = "COL_Prop_O_B_hall_furniture_8_1";
        const string deskLeg = "COL_Prop_O_B_hall_furniture_8_2";
        BoxCollider authored = Add(objects, deskTop).gameObject.AddComponent<BoxCollider>();
        Add(objects, deskSide).gameObject.AddComponent<BoxCollider>().isTrigger = true;
        using (var world = new TidegateWorld(root.gameObject))
        {
            root.gameObject.SetActive(false);
            world.ApplyLayers();
            if (objects.Find(deskTop).GetComponent<BoxCollider>() != authored)
                throw new Exception("inactive Tidegate world retains authored desk top");
            if (bed.gameObject.layer != 8 || bridge.gameObject.layer != 8 || wall.gameObject.layer != 9)
                throw new Exception("Tidegate does not use the official ground and wall layers");
            BoxCollider leg = objects.Find(deskLeg).GetComponent<BoxCollider>();
            if (objects.Find(deskTop).GetComponent<BoxCollider>() != authored || leg.isTrigger || !leg.enabled
                || leg.gameObject.layer != 9 || Math.Abs(leg.size.x - .04f) > .001f
                || objects.Find(deskSide).GetComponent<BoxCollider>().isTrigger)
                throw new Exception("Tidegate missing solids or authored collider reuse");
            Transform boundary = collision.Find("COL_Wall_Boundary");
            root.gameObject.SetActive(true);
            world.VerifyGround();
            world.ApplyLayers();
            if (objects.Find(deskLeg).GetComponent<BoxCollider>() != leg || !authored.gameObject.activeInHierarchy
                || objects.Find(deskTop).GetComponent<BoxCollider>() != authored
                || collision.Find("COL_Wall_Boundary") != boundary || BoundaryCount(collision) != 1)
                throw new Exception("Tidegate restored duplicate solids");
            Ground(world, new Vector3(22.667f, 1.02f, 53.667f), 1f, "under the North Bridge");
            Ground(world, new Vector3(22.667f, 1.9f, 53.667f), 1.957f, "on the North Bridge");
            Ground(world, new Vector3(22.667f, 1.77f, 53.667f), 1.957f, "nearby upper step");
            Ground(world, new Vector3(50f, 1.83f, 53.667f), 1f, "underside and distant bridge ignored");
            float y;
            if (world.TryGroundAt(new Vector3(22.667f, .74f, 53.667f), out y)
                || world.TryGroundAt(new Vector3(22.667f, 12f, 53.667f), out y))
                throw new Exception("Tidegate ground probe crossed its vertical bounds");
        }
        RestoreBesideDisabledGroup();
        Console.WriteLine("Tidegate ground probe: PASS");
    }

    private static int BoundaryCount(Transform collision)
    {
        int count = 0;
        foreach (Transform node in collision)
            if (node.name == "COL_Wall_Boundary") count++;
        return count;
    }

    private static void RestoreBesideDisabledGroup()
    {
        var root = new GameObject("TidegateWorld").transform;
        Transform collision = Add(root, "Collision");
        Transform objects = Add(collision, "COL_Objects");
        const string part = "COL_Prop_O_B_hall_furniture_8_0";
        BoxCollider hidden = Add(objects, part).gameObject.AddComponent<BoxCollider>();
        objects.gameObject.SetActive(false);
        TidegateBuildingCollision.RestoreMissingParts(root);
        Transform restored = collision.Find("COL_RestoredObjects");
        if (restored == null)
            throw new Exception("inactive Tidegate authored group has no active replacement group");
        BoxCollider solid = restored.Find(part).GetComponent<BoxCollider>();
        TidegateBuildingCollision.RestoreMissingParts(root);
        if (hidden.gameObject.activeInHierarchy || !solid.gameObject.activeInHierarchy
            || solid.isTrigger || !solid.enabled || restored.Find(part).GetComponent<BoxCollider>() != solid)
            throw new Exception("inactive Tidegate authored group receives a solid outside its disabled hierarchy");
    }

    private static void Ground(TidegateWorld world, Vector3 foot, float expected, string label)
    {
        float actual;
        if (!world.TryGroundAt(foot, out actual) || Math.Abs(actual - expected) > .0001f)
            throw new Exception(label + ": expected " + expected + ", got " + actual);
    }

    private static Collider Surface(Transform parent, string name, float height)
    {
        var collider = Add(parent, name).gameObject.AddComponent<Collider>();
        collider.Height = height;
        return collider;
    }

    private static Transform Add(Transform parent, string name)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }
}
