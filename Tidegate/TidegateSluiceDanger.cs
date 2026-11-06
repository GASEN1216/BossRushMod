using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>大闸泻湖侧的吸水口。只读主角位置、给提示并报告逾期；死亡处理由会话的危险设施 owner 统一负责。</summary>
    internal sealed class TidegateSluiceDanger : IDisposable
    {
        private const float Radius = TidegateIntakeHold.Radius, Approach = 12f;
        private static readonly Color Idle = new Color(0.94f, 0.77f, 0.39f, 0.56f);
        private static readonly Color Current = new Color(1f, 0.46f, 0.13f, 0.93f);
        private static readonly Color Danger = new Color(1f, 0.17f, 0.12f, 1f);
        private readonly TidegateTide tide;
        private readonly Action<string, bool> announce;
        private readonly TidegateIntakeHold hold = new TidegateIntakeHold();
        private readonly LineRenderer[] ripples = new LineRenderer[3];
        private float bed, previousLake, visualTime, markY;
        private int lastStyle;
        private Vector3 center;
        private GameObject marks;
        private LineRenderer boundary;
        private bool flowing, approached, heardFlow;

        internal TidegateSluiceDanger(TidegateWorld world, TidegateTide tide, Action<string, bool> announce)
        {
            this.tide = tide;
            this.announce = announce;
            if (world == null || tide == null || announce == null) return;
            try
            {
                Transform marker = world.Pois.Find(p => p != null && p.name == "POI_大闸");
                if (marker == null) throw new InvalidOperationException("找不到大闸地标");
                // 作者闸板在 Unity z=-17，坝顶从 -16 起。整圈留在泻湖，向 -z 的箭纹指进闸孔。
                center = marker.position + Vector3.forward * 0.2f;
                if (!world.InLagoon(center) || !world.TryGroundAt(center, out bed))
                    throw new InvalidOperationException("吸口未落在本图泻湖水床");
                previousLake = tide.LagoonLevel;
                marks = new GameObject("TidegateSluiceWarning");
                marks.transform.SetParent(world.Root.transform, false);
                markY = Math.Max(previousLake, bed) + 0.12f;
                marks.transform.position = new Vector3(center.x, markY, center.z);
                boundary = SkyIslandGroundRing.Create(marks.transform, Vector3.zero);
                if (boundary == null || !boundary.enabled) throw new InvalidOperationException("警戒圈无法绘制");
                SkyIslandGroundRing.SetShape(boundary, Radius, 0.15f, Idle);
                for (int i = 0; i < ripples.Length; i++)
                {
                    LineRenderer line = SkyIslandGroundRing.Create(marks.transform, Vector3.zero);
                    if (line == null || !line.enabled) throw new InvalidOperationException("流向标无法绘制");
                    line.gameObject.name = "IntakeCurrent";
                    line.loop = false;
                    line.positionCount = 3;
                    line.widthMultiplier = 0.14f;
                    line.startColor = line.endColor = Current;
                    line.SetPosition(0, new Vector3(-0.52f, 0.18f, 0f));
                    line.SetPosition(1, new Vector3(0f, -0.16f, 0f));
                    line.SetPosition(2, new Vector3(0.52f, 0.18f, 0f));
                    ripples[i] = line;
                    line.gameObject.SetActive(false);
                }
            }
            catch (Exception e)
            {
                Dispose();
                Debug.LogWarning(TidegateRaidScene.LogTag + " 大闸吸口停用：" + e.Message);
            }
        }

        internal bool Tick(Vector3 playerPosition, float delta)
        {
            if (marks == null || boundary == null || !boundary.enabled || !marks.activeInHierarchy) { hold.Step(false, 0f); return false; }
            float lake = tide.LagoonLevel, before = previousLake;
            previousLake = lake;
            if (!(delta > 0f)) return false;
            bool active = TidegateIntakeHold.Flowing(tide.SluiceOpen, before, lake, tide.Level, bed, delta, flowing);
            if (active != flowing)
            {
                flowing = active;
                foreach (LineRenderer line in ripples) line.gameObject.SetActive(active);
                if (!flowing) heardFlow = false;
            }
            float dx = playerPosition.x - center.x, dz = playerPosition.z - center.z;
            float range = dx * dx + dz * dz;
            // 只在水中；人在坝面、岸上或桥上从警戒线投影经过不会被吸走。
            bool core = TidegateIntakeHold.InCore(active, range, playerPosition.y, lake, bed);
            if (range > (Approach + 3f) * (Approach + 3f)) approached = heardFlow = false;
            if (core && !hold.Inside)
            {
                announce(L10n.T("吸水口！马上横向游出红圈，留满 4 秒会被卷进闸底。", "Intake! Swim sideways out of the red ring. Stay four seconds and the gate pulls you under."), true);
                PlayAlarm();
                approached = heardFlow = true;
            }
            else if (range <= Approach * Approach && (!approached || (active && !heardFlow)))
            {
                if (active)
                {
                    announce(L10n.T("大闸正在泄水。橙圈里是吸水口，会致命；沿两侧绕行。", "The sluice is discharging. The orange ring marks a lethal intake. Go around either side."), true);
                    PlayAlarm();
                    heardFlow = true;
                }
                else announce(L10n.T("大闸警戒线：泻湖放水时，这个圈里会形成致命吸流。", "Sluice warning: when the lagoon drains, the current inside this ring is deadly."), false);
                approached = true;
            }
            bool fatal = hold.Step(core, delta);
            float y = Math.Max(lake, bed) + 0.12f;
            if (Math.Abs(markY - y) > 0.01f)
            {
                markY = y;
                marks.transform.position = new Vector3(center.x, y, center.z);
            }
            int style = core ? 2 : active ? 1 : 0;
            if (core || style != lastStyle)
            {
                lastStyle = style;
                SkyIslandGroundRing.SetShape(boundary, Radius, core ? 0.18f + 0.18f * Mathf.Clamp01(hold.Held / TidegateIntakeHold.WarningSeconds) : 0.15f,
                    core ? Danger : active ? Current : Idle);
            }
            if (active)
            {
                visualTime = Mathf.Repeat(visualTime + delta, 1f);
                for (int i = 0; i < ripples.Length; i++)
                {
                    float progress = Mathf.Repeat(visualTime + i / 3f, 1f);
                    ripples[i].transform.localPosition = new Vector3(0f, 0f, Mathf.Lerp(1.25f, -1.25f, progress));
                }
            }
            return fatal;
        }

        private void PlayAlarm()
        {
            SkyIslandBossSfx.Play(marks.transform, SkyIslandBossCue.Telegraph, marks.transform.position);
        }

        public void Dispose()
        {
            if (marks != null) { marks.SetActive(false); UnityEngine.Object.Destroy(marks); }
            marks = null;
            boundary = null;
        }
    }
}
