using System;
using UnityEngine;

namespace UnityEngine
{
    internal sealed class GameObject { internal bool Disabled, Reactivated; }
    internal enum CursorLockMode { None, Locked }
    internal static class Time { internal static float timeScale = 0.7f; }
    internal static class Cursor
    {
        internal static bool visible;
        internal static CursorLockMode lockState = CursorLockMode.Locked;
    }
    internal static class Mathf { internal static int Max(int left, int right) { return Math.Max(left, right); } }
}

namespace Duckov.UI
{
    internal sealed class InputManager
    {
        internal int Releases;
        internal static int ActivateAttempts;
        internal static void DisableInput(GameObject token) { if (LevelManager.Instance?.InputManager != null) token.Disabled = true; }
        internal static void ActiveInput(GameObject token) { ActivateAttempts++; LevelManager.Instance.InputManager.Releases++; token.Reactivated = true; }
    }
}

internal sealed class LevelManager
{
    internal static LevelManager Instance = new LevelManager();
    internal Duckov.UI.InputManager InputManager = new Duckov.UI.InputManager();
}

internal static class ModBehaviour
{
    internal static void DevLog(string message) { }
}

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        checks++;
    }

    private static void Main()
    {
        var confirmToken = new GameObject();
        var zombieToken = new GameObject();
        var confirm = BossRushUIKit.ClaimModalInput(confirmToken, "ConfirmDialog");
        Check(Time.timeScale == 0f && Cursor.visible && Cursor.lockState == CursorLockMode.None,
            "first consumer pauses and opens cursor");
        var zombie = ZombieModeUIHelper.ClaimModalInput(zombieToken, "ZombieChoice");
        Check(BossRushUIKit.ModalInputLeaseCount == 2 && ZombieModeUIHelper.ModalInputLeaseCount == 2,
            "both consumers share one lease count");
        Check(confirmToken.Disabled && zombieToken.Disabled, "both inputs disabled");
        confirm.Release();
        Check(BossRushUIKit.IsModalInputPaused && Time.timeScale == 0f && Cursor.visible,
            "releasing one consumer keeps pause");
        zombie.Release();
        Check(BossRushUIKit.ModalInputLeaseCount == 0 && Time.timeScale == 0.7f,
            "last release restores original time scale");
        Check(!Cursor.visible && Cursor.lockState == CursorLockMode.Locked,
            "last release restores original cursor");
        Check(confirmToken.Reactivated && zombieToken.Reactivated, "both inputs restored");
        zombie.Release(); confirm.Release();
        Check(BossRushUIKit.ModalInputLeaseCount == 0 && Time.timeScale == 0.7f,
            "duplicate release is idempotent");
        var oldHost = LevelManager.Instance;
        var oldToken = new GameObject();
        var old = BossRushUIKit.ClaimModalInput(oldToken, "previous scene");
        int attempts = Duckov.UI.InputManager.ActivateAttempts;
        LevelManager.Instance = null;
        old.Release();
        Check(!BossRushUIKit.IsModalInputPaused && Time.timeScale == 0.7f && !Cursor.visible && !oldToken.Reactivated
            && Duckov.UI.InputManager.ActivateAttempts == attempts,
            "unloaded manager still closes the last modal and restores presentation");
        var absentToken = new GameObject();
        var absent = BossRushUIKit.ClaimModalInput(absentToken, "loading");
        LevelManager.Instance = new LevelManager();
        absent.Release();
        Check(!absentToken.Disabled && !absentToken.Reactivated && LevelManager.Instance.InputManager.Releases == 0,
            "a silent no-op claim cannot activate input on the next scene");
        var changedToken = new GameObject();
        var changed = BossRushUIKit.ClaimModalInput(changedToken, "swap");
        LevelManager.Instance = oldHost;
        int oldReleases = oldHost.InputManager.Releases;
        changed.Release();
        Check(changedToken.Disabled && !changedToken.Reactivated && oldHost.InputManager.Releases == oldReleases,
            "a different live manager is never released in place of the original");
        var gone = BossRushUIKit.ClaimModalInput(new GameObject(), "input component removed");
        oldHost.InputManager = null;
        gone.Release();
        Check(Duckov.UI.InputManager.ActivateAttempts == attempts && BossRushUIKit.ModalInputLeaseCount == 0,
            "a surviving level without an input manager needs no activation call");
        Console.WriteLine("SharedModalInput regression PASS: " + checks + " checks");
    }
}
