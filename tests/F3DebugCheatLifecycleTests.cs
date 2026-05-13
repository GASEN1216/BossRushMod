using System;
using System.IO;

internal static class F3DebugCheatLifecycleTests
{
    private static void AssertTrue(string name, bool value)
    {
        if (!value)
        {
            throw new Exception(name + " expected true but got false");
        }
    }

    private static void AssertFalse(string name, bool value)
    {
        if (value)
        {
            throw new Exception(name + " expected false but got true");
        }
    }

    private static string ExtractBlock(string text, string signature)
    {
        int start = text.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        int braceStart = text.IndexOf('{', start);
        if (braceStart < 0)
        {
            return string.Empty;
        }

        int depth = 0;
        for (int i = braceStart; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text.Substring(start, i - start + 1);
                }
            }
        }

        return string.Empty;
    }

    private static void TestHideRestoresCapturedPresentationState(string source)
    {
        AssertTrue(
            "presentation restore helper exists",
            source.Contains("private void RestoreF3DebugCheatPresentationState()", StringComparison.Ordinal));

        string hideBlock = ExtractBlock(source, "private void HideF3DebugCheatMenu()");
        AssertTrue("HideF3DebugCheatMenu block exists", !string.IsNullOrEmpty(hideBlock));
        AssertTrue(
            "hide restores captured presentation state",
            hideBlock.Contains("RestoreF3DebugCheatPresentationState();", StringComparison.Ordinal));
        AssertFalse(
            "hide does not hard reset timescale",
            hideBlock.Contains("Time.timeScale = 1f;", StringComparison.Ordinal));
        AssertFalse(
            "hide does not hard reset cursor visibility",
            hideBlock.Contains("Cursor.visible = false;", StringComparison.Ordinal));
        AssertFalse(
            "hide does not hard reset cursor lock",
            hideBlock.Contains("Cursor.lockState = CursorLockMode.Locked;", StringComparison.Ordinal));
    }

    private static void TestTryGetMainCharacterAvoidsArbitraryFallback(string source)
    {
        string block = ExtractBlock(source, "private bool TryGetMainCharacter(out CharacterMainControl main)");
        string helperBlock = ExtractBlock(source, "private static bool IsMainCharacterForF3Debug(CharacterMainControl candidate)");
        AssertTrue("TryGetMainCharacter block exists", !string.IsNullOrEmpty(block));
        AssertTrue("IsMainCharacterForF3Debug block exists", !string.IsNullOrEmpty(helperBlock));
        AssertFalse(
            "TryGetMainCharacter must not use arbitrary scene fallback",
            block.Contains("FindObjectOfType<CharacterMainControl>()", StringComparison.Ordinal));
        AssertTrue(
            "TryGetMainCharacter validates candidates via helper",
            block.Contains("IsMainCharacterForF3Debug(candidate)", StringComparison.Ordinal));
        AssertTrue(
            "main character helper validates player fallback as main character",
            helperBlock.Contains("CharacterMainControlExtensions.IsMainCharacter", StringComparison.Ordinal));
    }

    private static void TestOnDestroyDestroysPersistentUi(string source)
    {
        string block = ExtractBlock(source, "private void OnDestroy_F3DebugCheatMenu()");
        AssertTrue("OnDestroy_F3DebugCheatMenu block exists", !string.IsNullOrEmpty(block));
        AssertTrue(
            "OnDestroy destroys the persistent F3 canvas",
            block.Contains("DestroyF3DebugCheatMenuUI();", StringComparison.Ordinal));
    }

    public static int Main()
    {
        string[] sourceFiles =
        {
            Path.Combine("DebugAndTools", "F3DebugCheatMenu.cs"),
            Path.Combine("DebugAndTools", "F3DebugCheatMenuUi.cs"),
            Path.Combine("DebugAndTools", "F3DebugCheatMenuPlayerStats.cs"),
            Path.Combine("DebugAndTools", "F3DebugCheatMenuActions.cs"),
        };

        string source = string.Empty;
        for (int i = 0; i < sourceFiles.Length; i++)
        {
            source += File.ReadAllText(sourceFiles[i]) + "\n";
        }

        TestHideRestoresCapturedPresentationState(source);
        TestTryGetMainCharacterAvoidsArbitraryFallback(source);
        TestOnDestroyDestroysPersistentUi(source);
        Console.WriteLine("F3DebugCheatLifecycleTests: PASS");
        return 0;
    }
}
