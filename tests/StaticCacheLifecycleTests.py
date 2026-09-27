"""Regression cases for the production static-cache guard's input and call graph."""
from pathlib import Path
import tempfile

from StaticCacheLifecycleGuard import load_production_sources, find_reset_calls_on_ondestroy_path


CACHE = "public static class Cache { public static void ResetStaticCaches() { } }"


def reached(owner):
    return "Cache" in find_reset_calls_on_ondestroy_path({Path("Owner.cs"): owner + CACHE})


def main():
    cases = {
        "direct": ("public void OnDestroy() { Cache.ResetStaticCaches(); }", True),
        "cleanup chain": ("public void OnDestroy() { Cleanup(); } private void Cleanup() { Cache.ResetStaticCaches(); }", True),
        "unused Cleanup name": ("public void OnDestroy() { } private void CleanupOnDestroy() { Cache.ResetStaticCaches(); }", False),
        "unused overload": ("public void OnDestroy() { Cleanup(); } private void Cleanup() { } private void Cleanup(int unused) { Cache.ResetStaticCaches(); }", False),
        "not a destroy callback": ("public void OnDestroy(int unused) { Cache.ResetStaticCaches(); }", False),
        "optional parameter is not a destroy callback": ("public void OnDestroy(bool unused = false) { Cache.ResetStaticCaches(); }", False),
        "static method is not a destroy callback": ("public static void OnDestroy() { Cache.ResetStaticCaches(); }", False),
        "iterator is not a destroy callback": ("public IEnumerator OnDestroy() { Cache.ResetStaticCaches(); yield break; }", False),
        "optional cleanup argument": ("public void OnDestroy() { Cleanup(); } private void Cleanup(bool keep = false) { Cache.ResetStaticCaches(); }", True),
        "reset-only cycle": ("public void OnDestroy() { } private void ResetStaticCaches() { Other(); } private void Other() { ResetStaticCaches(); Cache.ResetStaticCaches(); }", False),
        "comment": ("public void OnDestroy() { /* Cache.ResetStaticCaches(); */ }", False),
        "string": ('public void OnDestroy() { Log("Cache.ResetStaticCaches()"); }', False),
        "disabled": ("public void OnDestroy() {\n#if false\nCache.ResetStaticCaches();\n#endif\n}", False),
        "false branch": ("public void OnDestroy() { if (false) { Cache.ResetStaticCaches(); } }", False),
        "return before reset": ("public void OnDestroy() { return; Cache.ResetStaticCaches(); }", False),
        "return before helper": ("public void OnDestroy() { return; Cleanup(); } private void Cleanup() { Cache.ResetStaticCaches(); }", False),
        "true branch return": ("public void OnDestroy() { if (true) return; Cache.ResetStaticCaches(); }", False),
        "conditional return": ("public void OnDestroy() { if (done) return; Cache.ResetStaticCaches(); }", True),
        "false else": ("public void OnDestroy() { if (true) { } else Cache.ResetStaticCaches(); }", False),
        "false while": ("public void OnDestroy() { while (false) { Cache.ResetStaticCaches(); } }", False),
        "unused lambda": ("public void OnDestroy() { Action later = () => Cache.ResetStaticCaches(); }", False),
        "unused local function": ("public void OnDestroy() { void Later() { Cache.ResetStaticCaches(); } }", False),
        "safe synchronous lambda": ('public void OnDestroy() { SafeRuntime.Run("reset", () => Cache.ResetStaticCaches()); }', True),
        "exception guard": ("public void OnDestroy() { try { Cleanup(); } catch { } } private void Cleanup() { Cache.ResetStaticCaches(); }", True),
        "region text": ("#region paths Foo/*.json\npublic void OnDestroy() { Cache.ResetStaticCaches(); }\n#endregion\n", True),
    }
    for name, (body, expected) in cases.items():
        actual = reached("public class Owner {\n" + body + "\n}")
        assert actual == expected, name + ": expected reachable=" + str(expected)
        print("PASS " + name)

    partial = {
        Path("Owner.cs"): "public partial class Owner { private Worker worker; public void OnDestroy() { worker.Cleanup(); } }",
        Path("Worker.cs"): "public partial class Worker { public void Cleanup() { this.Reset(); } }",
        Path("WorkerReset.cs"): "public partial class Worker { private void Reset() { Cache.ResetStaticCaches(); } }" + CACHE,
    }
    assert "Cache" in find_reset_calls_on_ondestroy_path(partial), "typed receiver and partial methods"
    with tempfile.TemporaryDirectory(prefix="bossrush-cache-inputs-") as directory:
        root = Path(directory).resolve()
        assert root.is_relative_to(Path(tempfile.gettempdir()).resolve())
        (root / "compile_official.bat").write_text("echo(Current.cs\n", encoding="utf-8")
        (root / "Current.cs").write_text("public class Current { public void OnDestroy() {} }" + CACHE, encoding="utf-8")
        (root / "old-snapshot.cs").write_text("public class Old { public void OnDestroy() { Cache.ResetStaticCaches(); } }", encoding="utf-8")
        current = load_production_sources(root)
        assert len(current) == 1 and not find_reset_calls_on_ondestroy_path(current), "uncompiled snapshots cannot prove cleanup"
        (root / "compile_official.bat").write_text("echo(Missing.cs\n", encoding="utf-8")
        try:
            load_production_sources(root)
        except OSError:
            pass
        else:
            raise AssertionError("missing compile inputs must fail closed")
    print("StaticCacheLifecycleTests: PASS (28 cases)")


if __name__ == "__main__":
    main()
