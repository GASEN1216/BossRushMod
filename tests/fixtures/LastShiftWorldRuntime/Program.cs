using System;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static Transform Child(Transform parent, string name)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    private static void Equal(float actual, float expected, string name)
    {
        if (Math.Abs(actual - expected) > 0.0001f) throw new Exception(name + ": " + actual + " != " + expected);
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
    }

    private static Transform Surface(Transform basins, string name, Vector3 center, Vector3 size)
    {
        Transform surface = Child(basins, name);
        surface.gameObject.AddComponent<MeshRenderer>().bounds = new Bounds(center, size);
        return surface;
    }

    private static int Main()
    {
        EntryCollision();
        var root = new GameObject("LastShiftYardWorld");
        Transform markers = Child(root.transform, "Markers");
        Child(markers, "PlayerSpawn");
        Child(Child(markers, "Exits"), "Exit_north");
        Child(Child(markers, "Loot"), "box");
        Child(Child(Child(markers, "Spawns"), "group"), "guard");
        Child(Child(markers, "Interact"), "switch");
        Transform colliders = Child(root.transform, "Collision");
        Child(colliders, "COL_Ground").gameObject.AddComponent<Collider>().Height = -.11f;
        Collider upper = Child(colliders, "COL_Ground_Overhead").gameObject.AddComponent<Collider>();
        upper.Height = 2.43f;
        upper.MinX = 38f;
        Collider backFace = Child(colliders, "COL_Ground_BackFace").gameObject.AddComponent<Collider>();
        backFace.Height = 2.4f;
        backFace.MinX = 38f;
        backFace.Normal = Vector3.down;
        Collider wall = Child(colliders, "COL_Wall").gameObject.AddComponent<Collider>();
        Transform authoredFeet = Child(colliders, "COL_block");
        BoxCollider authored = Child(authoredFeet, "COL_gantryNE").gameObject.AddComponent<BoxCollider>();
        Child(authoredFeet, "COL_gantrySE").gameObject.AddComponent<BoxCollider>().enabled = false;
        Transform authoredCases = Child(colliders, "COL_crate");
        Child(authoredCases, "COL_crate:eastCab").gameObject.AddComponent<BoxCollider>().isTrigger = true;
        BoxCollider hiddenCase = Child(authoredCases, "COL_crate:repairMed").gameObject.AddComponent<BoxCollider>();
        authoredCases.gameObject.SetActive(false);
        Transform basins = Child(Child(Child(root.transform, "Visual"), "VIS_props"), "Sediment and wash basins");
        Surface(basins, "West", new Vector3(-68f, .016f, 30f), new Vector3(8.62f, 0f, 11.62f));
        Surface(basins, "East", new Vector3(66f, .016f, 0f), new Vector3(6.62f, 0f, 8.62f));
        Surface(basins, "Solid", Vector3.zero, new Vector3(5f, 1f, 5f));
        Surface(Child(basins, "Basin rim"), "Grating", Vector3.zero, new Vector3(5f, 0f, 5f));
        using (var world = new LastShiftWorld(root))
        {
            root.SetActive(false);
            world.ApplyLayers();
            Check(wall.gameObject.layer == 9, "official wall layer");
            Transform details = colliders.Find("COL_BuildingDetails");
            Check(authored.gameObject.layer == 9 && details.Find("COL_gantryNE") == null,
                  "inactive world retains authored gantry foot");
            Check(details.Find("COL_gantrySE").GetComponent<BoxCollider>().enabled, "disabled authored foot receives solid fallback");
            Check(!details.Find("COL_crate:eastCab").GetComponent<BoxCollider>().isTrigger, "trigger does not stand in for the missing case");
            Transform repairedCase = details.Find("COL_crate:repairMed");
            Check(hiddenCase.enabled && repairedCase != null && repairedCase.GetComponent<BoxCollider>().enabled,
                  "inactive authored child group receives solid fallback");
            root.SetActive(true);
            world.ApplyLayers();
            world.VerifyGround(1 << 8);
            Check(details.Find("COL_gantryNE") == null && authored.gameObject.activeInHierarchy,
                  "activation keeps exactly the original authored gantry foot");
            Check(!hiddenCase.gameObject.activeInHierarchy, "activation leaves locally disabled authored group off");
            Transform lintel = details.Find("COL_BondEastLintel");
            Equal(lintel.localPosition.y, 3.45f, "doorway center preserved below lintel");
            Equal(lintel.GetComponent<BoxCollider>().size.z, 4.55f, "east lintel spans the door only");
            Check(lintel.gameObject.layer == 9, "added boxes are classified with authored walls");
            float y;
            Check(world.TryGroundAt(new Vector3(40f, -.11f, 49f), out y), "lower bridge layer");
            Equal(y, -.11f, "lower bridge height");
            Check(world.TryGroundAt(new Vector3(40f, 2.4f, 49f), out y), "upper bridge layer");
            Equal(y, 2.43f, "upper bridge height");
            Check(!world.TryGroundAt(new Vector3(40f, -10f, 49f), out y), "ray depth gate");
            Transform west = root.transform.Find("BR_BasinWater_West");
            Transform east = root.transform.Find("BR_BasinWater_East");
            Check(west != null && east != null && root.transform.Find("BR_BasinWater_Solid") == null
                  && root.transform.Find("BR_BasinWater_Grating") == null, "only the two visible basin planes own Water boxes");
            BoxCollider box = west.GetComponent<BoxCollider>();
            Check(box != null && box.isTrigger && west.gameObject.layer == 12, "native Water trigger layer");
            Equal(box.size.x, 8.62f, "visible width");
            Equal(box.size.z, 11.62f, "visible depth");
            Equal(west.position.y + box.size.y / 2f, .016f, "surface height");
            Equal(west.position.y - box.size.y / 2f, -.11f, "ground floor preserved");
            world.ApplyLayers();
            Check(ReferenceEquals(details, colliders.Find("COL_BuildingDetails")), "repeat setup retains the original building details");
            Check(ReferenceEquals(west, root.transform.Find("BR_BasinWater_West")) && west.gameObject.layer == 12,
                  "repeat setup retains exactly the original Water trigger");
        }
        TidegateGroundCases.Run();
        Console.WriteLine("LastShiftWorldRuntime: PASS");
        return 0;
    }

    private static void EntryCollision()
    {
        var entry = new GameObject("Entry_Model").transform;
        Child(entry, "Collision"); // 作者包的空层级不能挡掉运行时实体。
        Transform handle = Child(entry, "Mesh_12");
        Child(entry, "Mesh_13");
        Child(entry, "Mesh_14");
        LastShiftEntryCollision.Attach(entry);
        Transform collision = entry.Find("BR_LastShiftEntryCollision");
        Check(collision != null, "author's empty Collision node must not replace runtime solids");
        foreach (string name in new[] { "DeskBody", "SignPost", "VanCab", "VanBed", "VanWheelFrontLeft" })
        {
            Transform part = collision.Find(name);
            Check(part != null && part.gameObject.layer == 9 && part.GetComponent<BoxCollider>() is BoxCollider box
                  && !box.isTrigger, "entry solid " + name);
        }
        Check(handle.Find("PunchHandle").GetComponent<BoxCollider>() != null, "punch collision follows animated mesh");
        LastShiftEntryCollision.Attach(entry);
        Check(ReferenceEquals(collision, entry.Find("BR_LastShiftEntryCollision")), "one collision root per entry model");
    }
}
