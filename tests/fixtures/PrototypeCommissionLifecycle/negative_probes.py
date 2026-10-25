"""Break the late-result and scene-cancellation gates in isolated source copies."""
from pathlib import Path
import hashlib
import tempfile
from run import ROOT, SOURCE, run


def main():
    data = (ROOT / SOURCE).read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    source = data.decode("utf-8-sig")
    newline = "\r\n" if "\r\n" in source else "\n"
    probes = (
        ("late_slot_callback", " || !IsDialogueContextCurrent()) return;", ") return;",
         "silent slot drift: late callback cannot write any slot"),
        ("scene_cancellation", "public override void OnSceneLoaded(SceneRuntimeContext context)\n        {\n            CancelDialogue();",
         "public override void OnSceneLoaded(SceneRuntimeContext context)\n        {", "scene event: cancelled promptly"),
        ("loading_subscription", "SceneLoader.onStartedLoadingScene += OnSceneLoadingStarted;", "",
         "loading event without module tick: cancelled promptly"),
    )
    output = ROOT / "Build/runtime-regressions/PrototypeCommissionLifecycle-negative"
    output.mkdir(parents=True, exist_ok=True)
    for name, old, new, expected in probes:
        old, new = old.replace("\n", newline), new.replace("\n", newline)
        assert source.count(old) == 1, name
        isolated = Path(tempfile.mkdtemp(prefix=name + "-", dir=output))
        target = isolated / SOURCE
        target.parent.mkdir(parents=True)
        target.write_bytes(source.replace(old, new).encode("utf-8"))
        code, text = run(isolated, isolated / "execution")
        assert code != 0 and expected in text, (name, code, expected)
        print("EXPECTED RED: " + name)
    assert hashlib.sha256((ROOT / SOURCE).read_bytes()).hexdigest() == digest
    print("PrototypeCommissionLifecycle negative probes: PASS; original bytes unchanged")


if __name__ == "__main__":
    main()
