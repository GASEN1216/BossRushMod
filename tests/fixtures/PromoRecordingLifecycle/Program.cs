using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private static int assertions;
    private static ModBehaviour host;
    private static CharacterMainControl player;
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }
    private static T New<T>() where T : Component, new() { return new GameObject(typeof(T).Name).AddComponent<T>(); }
    private static void Reset()
    {
        PromoRecordingKit.ResetStaticCaches();
        host = New<ModBehaviour>(); player = New<CharacterMainControl>(); CharacterMainControl.Main = player;
        Scene.Loaded.Clear(); Scene.Loaded.Add(1); FieldAdventureSupport.Scene = new Scene { handle = 1 };
        FieldAdventureSupport.RaidAllowed = true; SceneLoader.IsSceneLoading = false;
        CharacterRandomPreset.Requests.Clear(); EclipseBloomBoss.Requests.Clear(); EclipseBloomBoss.Validity.Clear();
        SpawnedEnemyActivationHelper.Activations = 0;
        Input.Pressed.Clear(); Time.unscaledDeltaTime = 0.1f; Time.timeScale = 1f;
        InputManager.InputActived = true; BossRushUI.Paused = false; BossRushUIKit.IsModalInputPaused = false;
        UnityEngine.EventSystems.EventSystem.current = null;
    }
    private static List<PromoRecordingStep> Script()
    { return new List<PromoRecordingStep> { new PromoRecordingStep(), new PromoRecordingStep() }; }
    private static T Read<T>(PromoRecordingDirector director, string name)
    { return (T)typeof(PromoRecordingDirector).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director); }
    private static void Press(PromoRecordingDirector director, KeyCode key)
    {
        Input.Pressed.Add(key);
        typeof(PromoRecordingDirector).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(director, null);
        Input.Pressed.Clear();
    }
    private static void SetSelected(Component component)
    { UnityEngine.EventSystems.EventSystem.current = new UnityEngine.EventSystems.EventSystem { currentSelectedGameObject = component.gameObject }; }
    private static void BlockedInput(string label, Action block)
    {
        foreach (KeyCode key in new[] { KeyCode.PageDown, KeyCode.Keypad3, KeyCode.PageUp, KeyCode.Keypad9,
            KeyCode.Home, KeyCode.Keypad7, KeyCode.Insert, KeyCode.Keypad0, KeyCode.End, KeyCode.Keypad1 })
        {
            Reset();
            var director = PromoRecordingDirector.Begin(host, Script(), 0);
            typeof(PromoRecordingDirector).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, 1f);
            block(); Press(director, key);
            Check(director != null, label + ": " + key + " cannot stop recording");
            Check(Read<int>(director, "index") == 0 && !Read<bool>(director, "paused"), label + ": " + key + " cannot change shot or pause state");
            Check(Read<float>(director, "timer") < 2f, label + ": " + key + " cannot restart lead-in");
            Check(CharacterRandomPreset.Requests.Count == 0, label + ": " + key + " cannot spawn targets");
        }
    }
    private static void RecordingInput()
    {
        BlockedInput("official pause menu", () => BossRushUI.Paused = true);
        BlockedInput("F3 zero time scale", () => Time.timeScale = 0f);
        BlockedInput("shared modal lease", () => BossRushUIKit.IsModalInputPaused = true);
        BlockedInput("official input blocked", () => InputManager.InputActived = false);
        BlockedInput("legacy text focus", () => { var field = New<UnityEngine.UI.InputField>(); field.isFocused = true; SetSelected(field); });
        BlockedInput("TMP text focus", () => { var field = New<TMPro.TMP_InputField>(); field.isFocused = true; SetSelected(field); });
        foreach (KeyCode key in new[] { KeyCode.PageDown, KeyCode.Keypad3 })
        {
            Reset(); var director = PromoRecordingDirector.Begin(host, Script(), 0); Press(director, key);
            Check(Read<int>(director, "index") == 1, key + " advances a normal recording");
        }
        foreach (KeyCode key in new[] { KeyCode.PageUp, KeyCode.Keypad9 })
        {
            Reset(); var director = PromoRecordingDirector.Begin(host, Script(), 0);
            typeof(PromoRecordingDirector).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, 1f);
            Press(director, key);
            Check(Read<float>(director, "timer") > 4f, key + " restarts lead-in");
        }
        foreach (KeyCode key in new[] { KeyCode.Home, KeyCode.Keypad7 })
        {
            Reset(); var director = PromoRecordingDirector.Begin(host, Script(), 0); Press(director, key);
            Check(Read<bool>(director, "paused"), key + " pauses only recording");
            Press(director, key); Check(!Read<bool>(director, "paused"), key + " resumes its own paused recording");
        }
        foreach (KeyCode key in new[] { KeyCode.Insert, KeyCode.Keypad0 })
        {
            Reset(); var director = PromoRecordingDirector.Begin(host, Script(), 0); Press(director, key);
            Check(CharacterRandomPreset.Requests.Count == 3, key + " spawns three targets in gameplay");
        }
        foreach (KeyCode key in new[] { KeyCode.End, KeyCode.Keypad1 })
        {
            Reset(); var director = PromoRecordingDirector.Begin(host, Script(), 0); Press(director, key);
            Check(director == null, key + " stops recording in gameplay");
        }
        Reset(); var selectedDirector = PromoRecordingDirector.Begin(host, Script(), 0);
        SetSelected(New<Transform>()); Press(selectedDirector, KeyCode.Keypad3);
        Check(Read<int>(selectedDirector, "index") == 1, "non-text selection allows normal hotkeys");
        Reset(); selectedDirector = PromoRecordingDirector.Begin(host, Script(), 0);
        SetSelected(New<UnityEngine.UI.InputField>()); Press(selectedDirector, KeyCode.Keypad3);
        Check(Read<int>(selectedDirector, "index") == 1, "unfocused text field does not hold stale input ownership");
    }
    private static void TargetCancelled(string label, Action invalidate)
    {
        Reset();
        Check(PromoRecordingKit.SpawnTargets(host, 1) == null && CharacterRandomPreset.Requests.Count == 1, label + ": dispatched");
        invalidate();
        var enemy = New<CharacterMainControl>(); CharacterRandomPreset.Requests[0].SetResult(enemy);
        Check(enemy == null && enemy.gameObject == null, label + ": late target and its components destroyed");
        Check(enemy.TeamChanges == 0 && SpawnedEnemyActivationHelper.Activations == 0, label + ": no late enemy activation");
    }
    private static void BloomCancelled(string label, Action invalidate)
    {
        Reset();
        Check(PromoRecordingKit.SpawnBloom(host) == null && EclipseBloomBoss.Requests.Count == 1, label + ": dispatched");
        Func<bool> valid = EclipseBloomBoss.Validity[0];
        Check(valid(), label + ": initial factory ownership"); invalidate();
        Check(!valid(), label + ": factory sees cancellation");
        // Deliberately ignore cancellation in the external factory and deliver a live late object.
        var enemy = New<CharacterMainControl>(); EclipseBloomBoss.Requests[0].SetResult(enemy);
        Check(enemy == null && enemy.gameObject == null, label + ": rejected late bloom");
        PromoRecordingKit.ResetStaticCaches();
    }
    private static void Main()
    {
        RecordingInput();
        TargetCancelled("end without director", () => PromoRecordingDirector.End(host));
        TargetCancelled("loading before scene unload", () => SceneLoader.IsSceneLoading = true);
        TargetCancelled("old scene unloaded", () => Scene.Loaded.Remove(1));
        TargetCancelled("player replaced", () => CharacterMainControl.Main = New<CharacterMainControl>());
        TargetCancelled("player dead", () => player.Health.IsDead = true);
        TargetCancelled("host destroyed", () => UnityEngine.Object.Destroy(host.gameObject));
        TargetCancelled("mode takes raid", () => FieldAdventureSupport.RaidAllowed = false);
        TargetCancelled("active raid changes while old is loaded", () => { Scene.Loaded.Add(2); FieldAdventureSupport.Scene = new Scene { handle = 2 }; });
        TargetCancelled("begin new recording", () => PromoRecordingDirector.Begin(host, Script(), -1));
        TargetCancelled("retake shot", () => PromoRecordingDirector.Begin(host, Script(), -1).StartStep(0));

        Reset(); PromoRecordingKit.SpawnTargets(host, 1);
        var target = New<CharacterMainControl>(); CharacterRandomPreset.Requests[0].SetResult(target);
        Check(target != null && target.Team == Teams.wolf && SpawnedEnemyActivationHelper.Activations == 1, "valid target committed with hostility and distance sleep release");
        PromoRecordingDirector.End(host);
        Check(target != null, "end preserves committed filming target");

        BloomCancelled("bloom stop", () => PromoRecordingDirector.End(host));
        BloomCancelled("bloom loading", () => SceneLoader.IsSceneLoading = true);
        BloomCancelled("bloom player replaced", () => CharacterMainControl.Main = New<CharacterMainControl>());
        BloomCancelled("bloom player dead", () => player.Health.IsDead = true);
        BloomCancelled("bloom host destroyed", () => UnityEngine.Object.Destroy(host.gameObject));
        BloomCancelled("bloom switches shot", () => PromoRecordingDirector.Begin(host, Script(), 1));

        Reset(); PromoRecordingKit.SpawnBloom(host); PromoRecordingKit.SpawnBloom(host);
        Check(EclipseBloomBoss.Requests.Count == 1, "in-flight bloom reserves its single filming slot");
        var bloom = New<CharacterMainControl>(); EclipseBloomBoss.Requests[0].SetResult(bloom);
        PromoRecordingKit.SpawnBloom(host);
        Check(EclipseBloomBoss.Requests.Count == 1, "committed live bloom reused by next shield/kill shot");
        PromoRecordingDirector.End(host);
        Check(bloom != null && EclipseBloomBoss.Validity[0](), "stop preserves committed bloom combat validity");
        SceneLoader.IsSceneLoading = true;
        Check(!EclipseBloomBoss.Validity[0](), "committed bloom still rejects scene loading");

        Reset(); PromoRecordingKit.SpawnBloom(host); PromoRecordingDirector.End(host); PromoRecordingKit.SpawnBloom(host);
        var stale = New<CharacterMainControl>(); EclipseBloomBoss.Requests[0].SetResult(stale);
        PromoRecordingKit.SpawnBloom(host);
        Check(stale == null && EclipseBloomBoss.Requests.Count == 2, "old completion cannot release a newer bloom reservation");
        var current = New<CharacterMainControl>(); EclipseBloomBoss.Requests[1].SetResult(current);
        PromoRecordingKit.SpawnBloom(host);
        Check(current != null && EclipseBloomBoss.Requests.Count == 2, "old completion cannot overwrite the new bloom reference");

        Reset(); PromoRecordingKit.SpawnBloom(host); EclipseBloomBoss.Requests[0].SetResult(null); PromoRecordingKit.SpawnBloom(host);
        Check(EclipseBloomBoss.Requests.Count == 2, "failed factory completion allows retry");

        Reset(); var director = PromoRecordingDirector.Begin(host, Script(), -1); PromoRecordingKit.SpawnBloom(host);
        UnityEngine.Object.Destroy(director);
        Check(!EclipseBloomBoss.Validity[0](), "actual director OnDestroy cancels pending factory");
        var afterDestroy = New<CharacterMainControl>(); EclipseBloomBoss.Requests[0].SetResult(afterDestroy);
        Check(afterDestroy == null && host.GetComponent<PromoRecordingDirector>() == null, "OnDestroy rejects late object and removes director");
        Console.WriteLine("PromoRecordingLifecycle: " + assertions + " PASS / 0 FAIL");
    }
}
