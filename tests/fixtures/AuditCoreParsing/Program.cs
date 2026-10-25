using System;
using System.Collections.Generic;
using BossRush;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
    }
}
namespace BossRush
{
    public static class L10n { public static string T(string a, string b) { return a; } }
    public static class ModBehaviour
    {
        public static string GetModPath() { return Environment.CurrentDirectory; }
        public static void DevLog(string text) { }
        public static void CriticalLog(string key, string text) { }
    }
}
internal static class Program
{
    private static int checks;
    private static void Check(bool value, string name) { checks++; if (!value) throw new Exception(name); }
    private static string Map(string optional, string points = "[[1,2,3]]")
    {
        return "{\"sceneName\":\"test\",\"sceneID\":\"test-id\",\"displayNameCN\":\"test\",\"displayNameEN\":\"test\",\"spawnPoints\":" + points + ",\"customSpawnPos\":" + optional + "}";
    }
    private static void Main()
    {
        foreach (string value in new[] { "9223372036854775808", "9.223372036854776e18", "-9.223372036854778e18" })
        {
            BossRushJsonValue root; string error;
            Check(BossRushJsonParser.TryParse("{\"v\":" + value + "}", out root, out error), "large finite token parses: " + value);
            Check(root.GetLong("v", 17L) == 17L, "out-of-range long uses fallback: " + value);
        }
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var token = BossRushJsonValue.NewFloat(value);
            Check(token.AsInt(17) == 17 && token.AsLong(17L) == 17L, "non-finite constructed token uses integer fallbacks");
        }
        Check(BossRushJsonValue.NewInteger(long.MaxValue).AsLong(17L) == long.MaxValue, "exact integer maximum remains valid");
        Check(BossRushJsonValue.NewFloat(-9223372036854775808d).AsLong(17L) == long.MinValue, "exact floating lower boundary remains valid");
        Check(BossRushJsonValue.NewFloat(9223372036854774784d).AsLong(17L) == 9223372036854774784L, "largest representable double below upper boundary remains valid");
        Check(BossRushJsonValue.NewFloat(12.5).AsLong(17L) == 12L && BossRushJsonValue.NewFloat(13.5).AsInt(17) == 14,
            "existing midpoint rounding remains unchanged");
        foreach (string value in new[] { "[]", "[1,2]", "[1,2,3,4]", "[\"bad\",5,6]", "[1e39,0,0]", "null", "{}" })
        {
            var map = MapSpawnPointRegistry.DeserializeFromJson(Map(value));
            Check(map != null && !map.customSpawnPos.HasValue, "invalid optional point uses fallback: " + value);
            Check(MapSpawnPointRegistry.DeserializeFromJson(Map("null", "[" + value + "]")) == null, "invalid required points reject map: " + value);
        }
        var origin = MapSpawnPointRegistry.DeserializeFromJson(Map("[0,0,0]"));
        Check(origin.customSpawnPos.HasValue && origin.customSpawnPos.Value.x == 0f, "intentional origin stays valid");
        Check(MapSpawnPointRegistry.DeserializeFromJson(Map("[1,2,3]") + "garbage") == null, "trailing corruption rejected");
        Check(LootBlacklistRegistry.ParseItemIds("{\"itemIds\":[42,500001]}").Length == 2, "valid blacklist");
        foreach (string value in new[] { "null", "\"text\"", "false", "{}", "[1,\"2\"]", "[2147483648]", "[1,]" })
            Check(LootBlacklistRegistry.ParseItemIds("{\"itemIds\":" + value + ",\"other\":[500001]}") == null, "bad blacklist ignores unrelated array: " + value);
        foreach (string value in new[] { "1e39", "-1e39", "3.5e38", "-3.5e38" })
        {
            BossRushJsonValue root; string error; float f;
            Check(BossRushJsonParser.TryParse("{\"v\":" + value + "}", out root, out error), "finite double remains valid token");
            Check(!root.TryGetFloat("v", out f), "overflow is not a valid float");
            Check(root.GetProperty("v").AsFloat(8f) == 8f && root.GetFloat("v", 8f) == 8f, "all float readers use fallback");
        }
        foreach (string value in new[] { "0", "0.5", "-12.25", "3.4028234663852886e38" })
        {
            BossRushJsonValue root; string error; float f;
            Check(BossRushJsonParser.TryParse("{\"v\":" + value + "}", out root, out error) && root.TryGetFloat("v", out f), "representable number preserved: " + value);
        }
        var factors = new Dictionary<string, float> { { "boss\"key", 2.5f }, { "disabled", 0f }, { "rare", 0.125f } };
        string configuration = BossPoolFactorJson.Write("{\"waveIntervalSeconds\":15,\"disabledBosses\":[]}", factors);
        var restored = BossPoolFactorJson.Read(configuration);
        Check(restored.Count == 3 && restored["boss\"key"] == 2.5f && restored["disabled"] == 0f && restored["rare"] == 0.125f,
            "boss factors survive real configuration write/read including escaped names and zero");
        Check(BossPoolFactorJson.Read("{\"waveIntervalSeconds\":15}").Count == 0, "old configuration without factors keeps defaults");
        foreach (string malformed in new[] { "[]", "{\"x\":1e39}", "{\"x\":-1}", "{\"x\":\"2\"}" })
        {
            bool rejected = false;
            try { BossPoolFactorJson.Read("{\"bossInfiniteHellFactors\":" + malformed + "}"); }
            catch (FormatException) { rejected = true; }
            Check(rejected, "malformed factors do not silently replace valid configuration: " + malformed);
        }
        Console.WriteLine("AuditCoreParsing: PASS " + checks);
    }
}
