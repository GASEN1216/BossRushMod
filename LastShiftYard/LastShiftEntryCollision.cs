using System;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    /// <summary>官方仓库区剪票处的实体。小包按材质合并网格且网格不可读，实体尺寸取自同一作者模型。</summary>
    internal static class LastShiftEntryCollision
    {
        private const string RuntimeRootName = "BR_LastShiftEntryCollision";

        internal static void Attach(Transform model)
        {
            if (model.Find(RuntimeRootName) != null) return;
            int wallMask = GameplayDataSettings.Layers.wallLayerMask.value;
            if (wallMask == 0) throw new InvalidOperationException("剪票处缺少官方墙体层");
            int layer = 0;
            while ((wallMask & (1 << layer)) == 0) layer++;
            var root = new GameObject(RuntimeRootName).transform;
            root.SetParent(model, false);

            // 本地坐标和尺寸对应 transfer.mjs 的 desk / van / buildEntryProp，进游戏只翻转原型 X。
            Box(root, layer, "DeskBase", 0f, .08f, 0f, 1.75f, .13f, 1.03f);
            Box(root, layer, "DeskBody", 0f, .6f, 0f, 1.55f, 1.02f, .85f);
            Box(root, layer, "DeskTop", 0f, 1.16f, 0f, 1.91f, .18f, 1.13f);
            Box(root, layer, "DeskTrim", 0f, 1.17f, .572f, 1.88f, .04f, .035f);
            Box(root, layer, "DeskLeftTrim", -.70f, .60f, .445f, .07f, 1.01f, .045f);
            Box(root, layer, "DeskRightTrim", .70f, .60f, .445f, .07f, 1.01f, .045f);
            Box(root, layer, "TicketTray", -.23f, 1.26f, .03f, .91f, .055f, .61f);
            Box(root, layer, "TicketPunchBase", -.54f, 1.31f, -.12f, .29f, .069f, .25f);
            Box(root, layer, "TicketPunchStand", -.55f, 1.43f, -.135f, .20f, .19f, .19f);
            Box(root, layer, "TicketPunchAxle", -.55f, 1.53f, -.13f, .26f, .13f, .13f);
            Box(root, layer, "DeskLampBase", .49f, 1.29f, -.22f, .34f, .086f, .34f);
            Box(root, layer, "SignPost", 1.43f, 1.35f, -.28f, .13f, 2.68f, .14f);
            Box(root, layer, "SignBoard", 1.43f, 2.40f, -.23f, 1.70f, .61f, .085f);

            // 车底、驾驶室与开放货斗分开处理，驾驶室下方、斗内及车旁保持模型实际的空隙。
            Box(root, layer, "VanChassis", 3f, .61f, -2.15f, 2.38f, .33f, 4.12f);
            Box(root, layer, "VanCab", 3f, 1.30f, -3.63f, 2.32f, 1.27f, 1.18f);
            Box(root, layer, "VanCabWindows", 3f, 2.06f, -3.63f, 2.22f, .42f, .93f);
            Box(root, layer, "VanCabRoof", 3f, 2.40f, -3.63f, 2.43f, .17f, 1.22f);
            Box(root, layer, "VanHood", 3f, 1.45f, -4.37f, 2.28f, .69f, .85f);
            Box(root, layer, "VanFrontBumper", 3f, .88f, -4.8f, 2.49f, .18f, .21f);
            Box(root, layer, "VanBed", 3f, 1f, -1.86f, 2.39f, .29f, 3.07f);
            Box(root, layer, "VanBedLeft", 1.925f, 1.57f, -1.83f, .14f, .79f, 2.88f);
            Box(root, layer, "VanBedRight", 4.075f, 1.57f, -1.83f, .14f, .79f, 2.88f);
            Box(root, layer, "VanRearBoard", 3f, 1.07f, .07f, 2.34f, .14f, 1.13f).localRotation = Quaternion.Euler(8.594f, 0f, 0f);
            Box(root, layer, "VanRearBumper", 3f, .95f, .69f, 2.44f, .17f, .13f);
            Beam(root, layer, "VanBoardLeftBrace", 1.96f, 1.36f, .095f, 0f, .76f, -.99f, .036f);
            Beam(root, layer, "VanBoardRightBrace", 4.04f, 1.36f, .095f, 0f, .76f, -.99f, .036f);
            Box(root, layer, "VanCargo", 3.27f, 1.39f, -1.76f, 1.20f, .55f, 1.18f);
            Wheel(root, layer, "VanWheelFrontLeft", 1.87f, -3.52f);
            Wheel(root, layer, "VanWheelFrontRight", 4.13f, -3.52f);
            Wheel(root, layer, "VanWheelRearLeft", 1.87f, -.86f);
            Wheel(root, layer, "VanWheelRearRight", 4.13f, -.86f);

            // 打孔钳动件随 LastShiftTicketPunch 原样转动，碰撞与各自的网格一起重设父节点。
            Transform handle = model.Find("Mesh_12"), grip = model.Find("Mesh_13"), lever = model.Find("Mesh_14");
            if (handle != null) Beam(handle, layer, "PunchHandle", -.38f, 1.625f, -.06f, .34f, .19f, .14f, .052f);
            if (grip != null) Beam(grip, layer, "PunchGrip", -.11f, 1.765f, .06f, .20f, .09f, .10f, .072f);
            if (lever != null) Box(lever, layer, "PunchLever", -.25f, 1.545f, .02f, .045f, .31f, .045f);
        }

        private static void Wheel(Transform root, int layer, string name, float x, float z)
        {
            // 轮胎轴沿 X。中段和上下段分别拟合，避免四角凭空挡住脚。
            Box(root, layer, name, x, .49f, z, .29f, .54f, .89f);
            Box(root, layer, name + "Bottom", x, .1325f, z, .29f, .175f, .60f);
            Box(root, layer, name + "Top", x, .8475f, z, .29f, .175f, .60f);
        }

        private static void Beam(Transform root, int layer, string name, float x, float y, float z,
            float dx, float dy, float dz, float width)
        {
            var direction = new Vector3(dx, dy, dz);
            Box(root, layer, name, x, y, z, width, direction.magnitude, width).localRotation = Quaternion.FromToRotation(Vector3.up, direction);
        }

        private static Transform Box(Transform root, int layer, string name, float x, float y, float z, float width, float height, float depth)
        {
            var part = new GameObject(name);
            part.layer = layer;
            part.transform.SetParent(root, false);
            part.transform.localPosition = new Vector3(x, y, z);
            part.AddComponent<BoxCollider>().size = new Vector3(width, height, depth);
            return part.transform;
        }
    }
}
