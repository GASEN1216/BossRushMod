"""Extract only TypeID, quality, value, stack limit and tag names from the installed game.

Usage: python extract_catalog.py <Duckov_Data/resources.assets> <catalog.txt>
Requires UnityPy only to regenerate the snapshot. The fixture itself works without the game.
"""
from pathlib import Path
import hashlib
import struct
import sys


class Reader:
    def __init__(self, raw, pos=28):
        self.raw, self.pos = raw, pos

    def take(self, fmt):
        value, = struct.unpack_from("<" + fmt, self.raw, self.pos)
        self.pos += struct.calcsize("<" + fmt)
        return value

    def text(self):
        size = self.take("i")
        assert 0 <= size <= len(self.raw) - self.pos
        s = self.raw[self.pos:self.pos + size].decode("utf-8")
        self.pos = (self.pos + size + 3) & ~3
        return s

    def pointer(self):
        return self.take("i"), self.take("q")


def extract(asset, output):
    import UnityPy
    env = UnityPy.load(str(asset))
    objects = {x.path_id: x for x in env.objects if x.type.name == "MonoBehaviour"}
    named = {}
    for obj in objects.values():
        r = Reader(obj.get_raw_data())
        name = r.text()
        if name in ("ItemAssetsCollection", "GameplayDataSettings"):
            named[name] = r
    r = named["ItemAssetsCollection"]
    r.take("i")  # nextTypeID
    count = r.take("i")
    actual_tags = {}

    def tag(ref):
        external, path = ref
        assert external == 0 and path in objects
        if path not in actual_tags:
            actual_tags[path] = Reader(objects[path].get_raw_data()).text()
        name = actual_tags[path]
        assert name and "|" not in name and "," not in name
        return name

    data = []
    for _ in range(count):
        item_id = r.take("i")
        external, prefab = r.pointer()
        assert external == 0 and prefab in objects, "unresolved local item prefab: " + str(item_id)
        assert r.take("i") == item_id
        quality = r.take("i")
        tags = [tag(r.pointer()) for _ in range(r.take("i"))]
        for _ in range(3):
            r.text()
        stack = r.take("i")
        r.take("i")
        r.pointer()
        r.take("i")
        value = r.take("i")
        r.text()
        data.append((item_id, quality, value, stack, ",".join(tags)))
    assert r.pos == len(r.raw) and count == len(set(x[0] for x in data))
    # TagsData is embedded in GameplayDataSettings. Validate its property references and
    # AllTags against serialized data rather than guessing tags by English property names.
    g = named["GameplayDataSettings"]
    # TagsData starts with a series of eleven local tag PPtrs; detect the unique run with
    # valid refs followed by AllTags. Gun is nonserialized: the game finds "Gun" by name.
    layouts = []
    for pos in range(g.pos, len(g.raw) - 160, 4):
        candidate = Reader(g.raw, pos)
        try:
            fixed = [tag(candidate.pointer()) for _ in range(11)]
            n = candidate.take("i")
            if not 20 <= n <= 300:
                continue
            all_tags = [tag(candidate.pointer()) for _ in range(n)]
            if "Gun" in all_tags and set(fixed) <= set(all_tags):
                layouts.append((fixed + ["Gun"], all_tags))
        except (AssertionError, UnicodeError, struct.error):
            continue
    assert len(layouts) == 1, "unrecognized official GameplayDataSettings.TagsData"
    fixed, all_tags = layouts[0]
    with asset.open("rb") as f:
        digest = hashlib.file_digest(f, "sha256").hexdigest()
    head = ["# Extracted from ItemAssetsCollection + GameplayDataSettings.TagsData; no visual assets.",
            "# source SHA256 " + digest,
            "# fixed " + ",".join(fixed), "# all " + ",".join(all_tags),
            "# TypeID|quality|value|maxStackCount|tags"]
    output.write_text("\n".join(head + ["|".join(map(str, x)) for x in data]) + "\n", encoding="utf-8")
    print(f"{len(data)} catalog entries; {len(all_tags)} registered tags")


if __name__ == "__main__":
    extract(Path(sys.argv[1]), Path(sys.argv[2]))
