"""Execute the production delivery owner and official interaction/HUD paths on deterministic host doubles."""
from pathlib import Path
from xml.sax.saxutils import escape
import argparse
import hashlib
import json
import re
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tools"))
from run_runtime_regressions import run_project_fixture


def member(source, signature):
    hits = list(re.finditer(r"^([ \t]*)" + re.escape(signature), source, re.M))
    assert len(hits) == 1, signature
    hit = hits[0]
    opening = source.index("{", hit.end())
    end = re.search(r"^" + re.escape(hit.group(1)) + r"\}", source[opening:], re.M)
    assert end, signature
    return source[hit.start():opening + end.end()]


def once(source, before, after):
    assert source.count(before) == 1, before
    return source.replace(before, after)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--negative-probes", action="store_true")
    parser.add_argument("--evidence-dir", type=Path)
    args = parser.parse_args()
    out = args.evidence_dir or ROOT / "Build/runtime-regressions/FieldAdventureDeliveryRuntime"
    out.mkdir(parents=True, exist_ok=True)
    invocation = Path(tempfile.mkdtemp(prefix="source-", dir=out))
    relative = [
        "Integration/FieldAdventures/Delivery/OddParcelRuntimeModule.cs",
        "Integration/FieldAdventures/Delivery/OddParcelInteractable.cs",
        "Integration/FieldAdventures/Delivery/OddParcelRules.cs",
        "Integration/FieldAdventures/Delivery/OddParcelVisual.cs",
        "Interactables/BossRushBuildingInteractableBase.cs",
        "鸭科夫源码/TeamSoda.Duckov.Core/CA_Interact.cs",
        "鸭科夫源码/TeamSoda.Duckov.Core/InteractHUD.cs",
        "鸭科夫源码/TeamSoda.Duckov.Core/InteractSelectionHUD.cs",
        "鸭科夫源码/TeamSoda.Duckov.Core/CharacterMainControl.cs",
        "鸭科夫源码/TeamSoda.Duckov.Core/InteractableBase.cs",
        "鸭科夫源码/TeamSoda.Duckov.Core/CharacterActionBase.cs",
    ]
    texts = {p: (ROOT / p).read_text(encoding="utf-8-sig") for p in relative}
    generated = {}
    for path in relative[:3] + relative[4:8] + relative[10:11]:
        text = texts[path]
        if path == relative[0]:
            # Only the async return carrier changes; the complete owner body stays intact.
            text = text.replace("async UniTaskVoid", "async System.Threading.Tasks.Task")
        if path == relative[6]:
            # The decompiler emitted ref for Unity's output local; initialize it for C# definite assignment.
            text = once(text, "Vector2 vector3;", "Vector2 vector3 = default(Vector2);")
        generated[Path(path).name] = text
    visual = texts[relative[3]]
    generated["Voice.cs"] = "using System; using UnityEngine; namespace BossRush { internal sealed partial class OddParcelVisual {\n" + "\n".join(
        member(visual, signature) for signature in (
            "private void BuildVoice()", "internal void Squawk()", "internal void SyncVoiceVolume()", "internal void Burst(bool celebrate)"
        )) + "\n}}"
    generated["OfficialContracts.cs"] = "using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.Events; using SodaCraft.Localizations; using Duckov.Scenes;\npublic sealed partial class CharacterMainControl {\n" + member(texts[relative[8]], "public Vector3 CurrentAimDirection") + "\n}\npublic partial class InteractableBase {\n" + "\n".join(
        member(texts[relative[9]], signature) for signature in (
            "public List<InteractableBase> GetInteractableList()", "public InteractableBase GetInteractableInGroup(int index)",
            "public string GetInteractName()", "public string InteractName", "public bool Interacting",
            "public bool StartInteract(", "public void InternalStopInteract()", "public void StopInteract()",
            "public void UpdateInteract(", "public void FinishInteract(", "public bool CheckInteractable()"
        )) + "\n}"
    hashes = {p: hashlib.sha256((ROOT / p).read_bytes()).hexdigest() for p in relative}
    (invocation / "production-sha256.json").write_text(json.dumps(hashes, indent=2), encoding="utf-8")

    probe_results = []

    def execute(label, source):
        folder = invocation / label
        folder.mkdir()
        paths = [HERE / "Program.cs", HERE / "CourierAvailability.cs", HERE / "Stubs.cs"]
        for name, body in source.items():
            file = folder / name
            file.write_text(body, encoding="utf-8")
            paths.append(file)
        includes = "".join('<Compile Include="' + escape(str(p), {'"': '&quot;'}) + '" />' for p in paths)
        project = folder / "Delivery.csproj"
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><LangVersion>7.3</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NoWarn>0649;0414</NoWarn></PropertyGroup><ItemGroup>' + includes + '</ItemGroup></Project>', encoding="utf-8")
        code, output = run_project_fixture(project, folder / "execution", ROOT)
        (folder / "result.log").write_text(output, encoding="utf-8")
        if label != "baseline":
            # 保留失败日志和本次执行的二进制，隔离源码按基线原字节还原。
            for name in source:
                original = (invocation / "baseline" / name).read_bytes()
                (folder / name).write_bytes(original)
                assert (folder / name).read_bytes() == original, "Isolated source restoration failed: " + name
            restored = {name: hashlib.sha256((folder / name).read_bytes()).hexdigest() for name in source}
            baseline = {name: hashlib.sha256((invocation / "baseline" / name).read_bytes()).hexdigest() for name in source}
            assert restored == baseline, "Restored source hashes differ from baseline"
            (folder / "restored-sha256.json").write_text(json.dumps(restored, indent=2), encoding="utf-8")
            probe_results.append({"name": label, "exit_code": code, "restored_sha256": restored})
        print(label + ":\n" + output)
        return code, output

    code, output = execute("baseline", generated)
    if code:
        return code
    if args.negative_probes:
        old_geometry = dict(generated)
        old_geometry["OddParcelRuntimeModule.cs"] = once(old_geometry["OddParcelRuntimeModule.cs"],
            "Vector3 forward = player.CurrentAimDirection;", "Vector3 forward = player.transform.forward;")
        old_geometry["OddParcelRuntimeModule.cs"] = once(old_geometry["OddParcelRuntimeModule.cs"],
            "forward.normalized + Vector3.up * .35f", "forward.normalized * 1.25f + Vector3.up * .35f")
        old_geometry["OddParcelRuntimeModule.cs"] = once(old_geometry["OddParcelRuntimeModule.cs"],
            "new Vector3(0f, .35f, carried ? -1f : 0f)", "new Vector3(0f, .35f, 0f)")
        code, output = execute("prior-carry-geometry", old_geometry)
        if code == 0 or "all-heading official carry entry remains reachable" not in output or "error CS" in output:
            raise AssertionError("Prior geometry did not fail at the official interaction entry")
        print("Expected regression detected: prior-carry-geometry")
        old_membership = dict(generated)
        old_membership["OddParcelRuntimeModule.cs"] = once(old_membership["OddParcelRuntimeModule.cs"],
            member(old_membership["OddParcelRuntimeModule.cs"], "private void SyncCourierOption(bool clearing = false)"),
            "        private void SyncCourierOption(bool clearing = false) { }")
        old_membership["OddParcelRuntimeModule.cs"] = once(old_membership["OddParcelRuntimeModule.cs"],
            "                    group.Remove(courierOption);", "                    // Prior behavior: helper keeps its member attached.")
        code, output = execute("prior-courier-membership", old_membership)
        if code == 0 or "removed selected offer converges to Awen master" not in output or "error CS" in output:
            raise AssertionError("Prior courier membership did not fail at the official selected entry")
        print("Expected regression detected: prior-courier-membership")
        mutations = [
            ("stale-courier-content", "OddParcelInteractable.cs", "                refreshHudContent.Invoke(labelHud, null);", "                // Mutant: retain stale official content.", "active order removes offer immediately / visible rows"),
            ("lose-service-selection", "OddParcelRuntimeModule.cs", "int index = courierMaster.GetInteractableList().IndexOf(selected);", "int index = 0;", "cancellation while menu open preserves other selected service"),
            ("repeat-retained-event", "OddParcelInteractable.cs", "                    if (retainedRow != null) retainedRow.OnSelectedEvent = null;", "                    // Mutant: retrigger retained selection.", "cancellation reattachment has no repeated selected event"),
            ("stale-selection-cache", "OddParcelInteractable.cs", "                    hudIndex.SetValue(labelHud, player.interactAction.InteractIndexInGroup);", "                    // Mutant: next official Update repeats the selection event.", "next official HUD update does not duplicate fallback event"),
            ("skip-dialogue-finally-sync", "OddParcelRuntimeModule.cs", "if (operation == generation) { dialogueOpen = false; SyncCourierOption(); }", "if (operation == generation) { dialogueOpen = false; }", "first completion restores offer before menu closes / group"),
            ("skip-owner-removal", "OddParcelRuntimeModule.cs", "            SyncCourierOption(true);", "            // Mutant: destroy owned option without removing the raw group reference.", "runtime cleanup removes raw courier group member before destruction"),
            ("exhausted-offer-attached", "OddParcelRuntimeModule.cs", "case OddParcelAction.Offer: return condition == null && acceptedOrders < MaxOrders;", "case OddParcelAction.Offer: return condition == null && acceptedOrders <= MaxOrders;", "quota exhaustion shares action rejection gate"),
            ("hidden-hud-not-found", "OddParcelInteractable.cs", "                labelHud = LevelManager.Instance.GetComponentInChildren<InteractHUD>(true);", "                labelHud = null;", "first hidden attachment refreshes cached same-master HUD / visible rows"),
            ("hidden-selection-not-retained", "OddParcelInteractable.cs", "InteractSelectionHUD[] oldRows = labelHud.GetComponentsInChildren<InteractSelectionHUD>(true);", "InteractSelectionHUD[] oldRows = labelHud.GetComponentsInChildren<InteractSelectionHUD>();", "late attachment does not repeat retained selection event"),
            ("stale-generation-choice", "OddParcelRuntimeModule.cs", "if (operation == generation && (choice == 0 || choice == 1))", "if (choice == 0 || choice == 1)", "old pending choice cannot populate the new generation order"),
            ("stale-generation-finally", "OddParcelRuntimeModule.cs", "if (operation == generation) { dialogueOpen = false; SyncCourierOption(); }", "if (true) { dialogueOpen = false; SyncCourierOption(); }", "old finally cannot release or reattach the newer pending dialogue"),
            ("root-forward", "OddParcelRuntimeModule.cs", "Vector3 forward = player.CurrentAimDirection;", "Vector3 forward = player.transform.forward;", "carry pose follows official aim"),
            ("parcel-centered-collider", "OddParcelRuntimeModule.cs", "new Vector3(0f, .35f, carried ? -1f : 0f)", "new Vector3(0f, .35f, 0f)", "movement and turn before owner update keep menu reachable"),
            ("stale-hud", "OddParcelInteractable.cs", "            RefreshVisibleLabel();", "            // Prior behavior: localization only.", "same-target HUD tracks pressure"),
            ("fixed-volume", "Voice.cs", "                ? .7f * Mathf.Clamp01(master.Volume) * Mathf.Clamp01(sfx.Volume) : 0f;", "                ? .7f : .7f;", "master volume zero suppresses voice"),
            ("ignore-mute", "Voice.cs", " && !master.Mute && !sfx.Mute", "", "master mute suppresses voice"),
            ("paused-stale-audio", "OddParcelRuntimeModule.cs", "                if (parcelFx != null) parcelFx.SyncVoiceVolume();", "                // Prior behavior: no update while paused.", "paused settings update active voice"),
        ]
        for label, name, before, after, expected in mutations:
            changed = dict(generated)
            changed[name] = once(changed[name], before, after)
            code, output = execute(label, changed)
            if code == 0 or expected not in output or "error CS" in output:
                raise AssertionError("Negative probe did not fail at its contract: " + label)
            print("Expected regression detected: " + label)
    assert hashes == {p: hashlib.sha256((ROOT / p).read_bytes()).hexdigest() for p in relative}, "Production inputs changed during probes"
    (invocation / "result.json").write_text(json.dumps({
        "passed": True, "negative_probes": len(probe_results), "probe_results": probe_results,
        "isolated_sources_restored": True, "production_inputs_unchanged": True,
        "production_sha256": hashes,
    }, indent=2), encoding="utf-8")
    print("PASS delivery runtime; production inputs unchanged; evidence: " + str(invocation))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
