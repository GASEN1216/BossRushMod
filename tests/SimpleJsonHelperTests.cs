using System;
using System.Collections.Generic;
using System.Text;
using BossRush;

internal static class SimpleJsonHelperTests
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

    private static void TestGetBuilderReturnsIndependentBuilders()
    {
        StringBuilder first = SimpleJsonHelper.GetBuilder();
        first.Append("keep");

        StringBuilder second = SimpleJsonHelper.GetBuilder();
        second.Append("new");

        AssertTrue("GetBuilder independent instance", !object.ReferenceEquals(first, second));
        AssertEqual("GetBuilder first content preserved", first.ToString(), "keep");
        AssertEqual("GetBuilder second content", second.ToString(), "new");
    }

    private static void TestExtractorsDoNotMatchKeyPrefixes()
    {
        const string json = "{\"npcId\":\"wrong\",\"id\":\"right\",\"pointsBonus\":99,\"points\":7}";

        AssertEqual("ExtractString exact key", SimpleJsonHelper.ExtractString(json, "id"), "right");
        AssertEqual("ExtractInt exact key", SimpleJsonHelper.ExtractInt(json, "points"), 7);
    }

    private static void TestStringEscapeRoundTrip()
    {
        string original = "line1\nline2\t\"quoted\"\\slash\rend\b\f";
        StringBuilder sb = new StringBuilder();
        SimpleJsonHelper.EscapeString(sb, original);
        string unescaped = SimpleJsonHelper.UnescapeString(sb.ToString());

        AssertEqual("Escape/Unescape round trip", unescaped, original);
    }

    private static void TestExtractStringHandlesEscapedQuotes()
    {
        const string json = "{\"title\":\"a\",\"text\":\"say \\\"hi\\\" and keep \\\\ slash\",\"tail\":\"z\"}";
        string text = SimpleJsonHelper.ExtractString(json, "text");

        AssertEqual("ExtractString escaped quotes", text, "say \"hi\" and keep \\ slash");
    }

    private static void TestForEachObjectSkipsBracesInsideStrings()
    {
        const string json = "{\"npcDataList\":[{\"npcId\":\"npc{1}\",\"points\":1},{\"npcId\":\"npc2\",\"points\":2}]}";
        int arrayStart = json.IndexOf('[');
        int arrayEnd = json.LastIndexOf(']');

        List<string> ids = new List<string>();
        SimpleJsonHelper.ForEachObject(json, arrayStart, arrayEnd, (source, start, end) =>
        {
            ids.Add(SimpleJsonHelper.ExtractString(source, "npcId", start, end));
        });

        AssertEqual("ForEachObject count", ids.Count, 2);
        AssertEqual("ForEachObject first id", ids[0], "npc{1}");
        AssertEqual("ForEachObject second id", ids[1], "npc2");
    }

    public static int Main()
    {
        TestGetBuilderReturnsIndependentBuilders();
        TestStringEscapeRoundTrip();
        TestExtractStringHandlesEscapedQuotes();
        TestExtractorsDoNotMatchKeyPrefixes();
        TestForEachObjectSkipsBracesInsideStrings();
        Console.WriteLine("SimpleJsonHelperTests: PASS");
        return 0;
    }
}
