using System;
using System.Collections.Generic;
using BossRush;

internal static class AffinityJsonSerializerTests
{
    private static void AssertEqual<T>(string name, T actual, T expected)
    {
        if (!Equals(actual, expected))
        {
            throw new Exception(name + " expected " + expected + " but got " + actual);
        }
    }

    private static void AssertTrue(string name, bool value)
    {
        if (!value)
        {
            throw new Exception(name + " expected true but got false");
        }
    }

    private static void TestSerializeDeserializeRoundTrip()
    {
        Dictionary<string, AffinityData> source = new Dictionary<string, AffinityData>();
        source["npc_a"] = new AffinityData("npc_a")
        {
            points = 123,
            lastGiftDay = 7,
            lastGiftReaction = 2,
            lastChatDay = 9,
            interactionHistoryDays = "1,2,3",
            hasMet = true,
            hasTriggeredStory5 = true,
            hasTriggeredStory10 = false,
            triggeredEventKeys = "story_5|event_x",
            claimedRewardKeys = "reward_a",
            isMarriedToPlayer = false,
            isFollowingPlayer = true,
            marriageDateText = "2026-04-21 \"special\"",
            cheatingIncidentCount = 1,
            hasPendingCheatingRebuke = true,
            lastDecayCheckDay = 10
        };

        source["npc_b"] = new AffinityData("npc_b")
        {
            points = 0,
            lastGiftDay = -1,
            interactionHistoryDays = null,
            triggeredEventKeys = null,
            claimedRewardKeys = null,
            marriageDateText = null
        };

        string json = AffinityJsonSerializer.Serialize(source);
        Dictionary<string, AffinityData> restored = new Dictionary<string, AffinityData>();
        AssertTrue("Deserialize round trip", AffinityJsonSerializer.Deserialize(json, restored));

        AssertEqual("Restored count", restored.Count, 2);

        AffinityData a = restored["npc_a"];
        AssertEqual("npc_a points", a.points, 123);
        AssertEqual("npc_a lastGiftDay", a.lastGiftDay, 7);
        AssertEqual("npc_a hasMet", a.hasMet, true);
        AssertEqual("npc_a triggeredEventKeys", a.triggeredEventKeys, "story_5|event_x");
        AssertEqual("npc_a marriageDateText", a.marriageDateText, "2026-04-21 \"special\"");
        AssertEqual("npc_a hasPendingCheatingRebuke", a.hasPendingCheatingRebuke, true);

        AffinityData b = restored["npc_b"];
        AssertEqual("npc_b null interactionHistoryDays serialized as empty", b.interactionHistoryDays, "");
        AssertEqual("npc_b null triggeredEventKeys serialized as empty", b.triggeredEventKeys, "");
        AssertEqual("npc_b null claimedRewardKeys serialized as empty", b.claimedRewardKeys, "");
        AssertEqual("npc_b null marriageDateText serialized as empty", b.marriageDateText, "");
    }

    private static void TestDeserializeSkipsMissingNpcId()
    {
        const string json = "{\"npcDataList\":[{\"points\":1},{\"npcId\":\"npc_ok\",\"points\":2}]}";
        Dictionary<string, AffinityData> restored = new Dictionary<string, AffinityData>();

        AssertTrue("Deserialize valid array", AffinityJsonSerializer.Deserialize(json, restored));
        AssertEqual("Skip missing npcId count", restored.Count, 1);
        AssertEqual("Skip missing npcId preserved valid entry", restored["npc_ok"].points, 2);
    }

    private static void TestDeserializeDuplicateNpcIdLastWins()
    {
        const string json = "{\"npcDataList\":[{\"npcId\":\"npc_dup\",\"points\":1},{\"npcId\":\"npc_dup\",\"points\":99}]}";
        Dictionary<string, AffinityData> restored = new Dictionary<string, AffinityData>();

        AssertTrue("Deserialize duplicate ids", AffinityJsonSerializer.Deserialize(json, restored));
        AssertEqual("Duplicate npcId last wins", restored["npc_dup"].points, 99);
    }

    private static void TestDeserializeRejectsMissingArray()
    {
        Dictionary<string, AffinityData> restored = new Dictionary<string, AffinityData>();

        AssertEqual("Deserialize missing array", AffinityJsonSerializer.Deserialize("{\"noList\":true}", restored), false);
        AssertEqual("Deserialize missing array keeps map untouched", restored.Count, 0);
    }

    public static int Main()
    {
        TestSerializeDeserializeRoundTrip();
        TestDeserializeSkipsMissingNpcId();
        TestDeserializeDuplicateNpcIdLastWins();
        TestDeserializeRejectsMissingArray();
        Console.WriteLine("AffinityJsonSerializerTests: PASS");
        return 0;
    }
}
