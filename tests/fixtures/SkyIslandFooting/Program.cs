using System;
using System.Collections.Generic;
using Duckov.Utilities;
using UnityEngine;

namespace BossRush
{
    // Supply the fields normally owned by SkyIslandSession.cs. Decisions execute in the production partial.
    internal sealed partial class SkyIslandSession
    {
        private int rescueCount, sameSpotRescues;
        private Vector3 safePosition, lastRescueAt;
        private bool rescueLoopReported;
        private Player player;
        private float airborneSince, extractionHeld;
        private readonly List<Transform> landmarks = new List<Transform>();
        private Transform playerSpawn;
        private GameObject root;
        private int groundMask;

        private SkyIslandSession()
        {
            root = new GameObject();
            player = new Player();
            groundMask = Physics.GroundMask = 256;
            GameplayDataSettings.Layers.wallLayerMask = Physics.WallMask = 512;
            Physics.Hits.Clear();
            Physics.WallX = float.NaN;
            airborneSince = extractionHeld = 42;
        }
        private void Status(string message, bool error) { }
        private Transform Marker(float x, float y, bool spawn = false)
        {
            var marker = new GameObject().transform;
            marker.position = new Vector3(x, y, 0);
            if (spawn) playerSpawn = marker; else landmarks.Add(marker);
            return marker;
        }
        private void Ground(float x, float y, bool foreign = false)
        {
            Physics.Hits[x] = new Surface { Height = y, Transform = new Transform { parent = foreign ? new Transform() : root.transform } };
        }
        private bool Find(out Vector3 target) { return TryFallbackFooting(new Vector3(0, 0, 0), out target); }
        private static void Equal(float actual, float expected, string message)
        {
            if (Math.Abs(actual - expected) > 0.0001f)
                throw new Exception(message + " actual=" + actual + " expected=" + expected);
        }
        private static void Require(bool value, string message) { if (!value) throw new Exception(message); }

        public static int Main()
        {
            var cases = new Dictionary<string, Action>
            {
                { "anchor above ground uses hit height", () =>
                    {
                        var s = new SkyIslandSession(); s.Marker(10, 1.5f); s.Ground(10, 0);
                        Vector3 p; Require(s.Find(out p), "no valid footing"); Equal(p.y, .15f, "unsafe height");
                    } },
                { "anchor below ground uses hit height", () =>
                    {
                        var s = new SkyIslandSession(); s.Marker(10, -1.5f); s.Ground(10, 0);
                        Vector3 p; Require(s.Find(out p), "no valid footing"); Equal(p.y, .15f, "underground target");
                    } },
                { "wall at actual floor skips nearest marker", () =>
                    {
                        var s = new SkyIslandSession(); s.Marker(10, 1.5f); s.Marker(20, 0);
                        s.Ground(10, 0); s.Ground(20, 0); Physics.WallX = 10;
                        Vector3 p; Require(s.Find(out p), "valid second marker rejected"); Equal(p.x, 20, "selected wall-overlapping target");
                    } },
                { "spawn without ground is rejected", () =>
                    {
                        var s = new SkyIslandSession(); s.Marker(30, 0, true);
                        Vector3 p; Require(!s.Find(out p), "accepted unsupported spawn");
                    } },
                { "spawn over foreign ground is rejected", () =>
                    {
                        var s = new SkyIslandSession(); s.Marker(30, 0, true); s.Ground(30, 0, true);
                        Vector3 p; Require(!s.Find(out p), "accepted foreign ground");
                    } },
                { "spawn inside wall is rejected", () =>
                    {
                        var s = new SkyIslandSession(); s.Marker(30, 0, true); s.Ground(30, 0); Physics.WallX = 30;
                        Vector3 p; Require(!s.Find(out p), "accepted blocked spawn");
                    } },
                { "valid spawn is snapped to actual floor", () =>
                    {
                        var s = new SkyIslandSession(); s.Marker(30, 1.5f, true); s.Ground(30, 0);
                        Vector3 p; Require(s.Find(out p), "spawn fallback unavailable"); Equal(p.y, .15f, "spawn height");
                    } },
                { "nearest usable anchor excludes failed spot", () =>
                    {
                        var s = new SkyIslandSession(); s.landmarks.Add(null);
                        var destroyed = s.Marker(5, 0); s.Ground(5, 0); UnityEngine.Object.Destroy(destroyed.gameObject);
                        Require(destroyed == null, "destroyed Transform must compare equal to null");
                        s.Marker(1, 0); s.Marker(40, 0); s.Marker(10, 0);
                        s.Ground(1, 0); s.Ground(40, 0); s.Ground(10, 0);
                        Vector3 p; Require(s.Find(out p), "no valid footing"); Equal(p.x, 10, "wrong nearest anchor");
                    } },
                { "foreign landmark ground falls back to valid spawn", () =>
                    {
                        var s = new SkyIslandSession(); s.Marker(10, 0); s.Ground(10, 0, true);
                        s.Marker(30, 0, true); s.Ground(30, 0);
                        Vector3 p; Require(s.Find(out p), "no valid spawn"); Equal(p.x, 30, "foreign anchor selected");
                    } },
                { "missing anchors fail without inventing coordinates", () =>
                    {
                        var s = new SkyIslandSession(); Vector3 p; Require(!s.Find(out p), "unexpected footing");
                    } },
                { "third repeated rescue teleports to validated surface", () =>
                    {
                        var s = new SkyIslandSession(); s.Marker(10, -1.5f); s.Ground(10, 0);
                        s.safePosition = new Vector3(0, 0, 0); s.Rescue(); s.Rescue(); s.Rescue();
                        Equal(s.player.Position.x, 10, "rescue anchor"); Equal(s.player.Position.y, .15f, "rescue underground");
                        Require(s.RescueCount == 3, "rescue counter");
                        Equal(s.airborneSince, -1, "airborne timer not reset"); Equal(s.extractionHeld, -1, "extraction timer not reset");
                    } }
            };
            int failed = 0;
            foreach (var test in cases)
            {
                try { test.Value(); Console.WriteLine("PASS " + test.Key); }
                catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Key + ": " + error.Message); }
            }
            Console.WriteLine("Cases=" + cases.Count + ", Failed=" + failed);
            return failed == 0 ? 0 : 1;
        }
    }
}
