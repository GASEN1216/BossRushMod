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
    internal static class InputManager
    {
        internal static void DisableInput(GameObject token) { token.Disabled = true; }
        internal static void ActiveInput(GameObject token) { token.Reactivated = true; }
    }
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
        Console.WriteLine("SharedModalInput regression PASS: " + checks + " checks");
    }
}
