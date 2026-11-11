"""从 Sarasa Super TTC 中抽取 UI SC 的 Regular / SemiBold 并做子集化。"""
import os
from fontTools.ttLib import TTCollection
from fontTools import subset

SRC = r"F:\Sarasa-SuperTTC.ttc"
OUT_DIR = r"src\BatteryWidget\Fonts"


def find_faces():
    collection = TTCollection(SRC, lazy=True)
    regular = None
    semibold = None
    for font in collection.fonts:
        families = set()
        subfamilies = set()
        for rec in font["name"].names:
            if rec.nameID == 1:
                families.add(rec.toUnicode())
            elif rec.nameID == 2:
                subfamilies.add(rec.toUnicode())
        if regular is None and ("更纱黑体 UI SC" in families or "Sarasa UI SC" in families):
            if "Regular" in subfamilies:
                regular = font
        if semibold is None and any(f.endswith("UI SC SemiBold") for f in families):
            semibold = font
    return regular, semibold


def build_unicodes():
    chars = set()
    for ch in range(0x20, 0x7F):
        chars.add(ch)
    for ch in range(0xA0, 0x100):
        chars.add(ch)
    for ch in range(0x2000, 0x2070):
        chars.add(ch)
    for ch in range(0x2190, 0x2200):
        chars.add(ch)
    for ch in range(0x3000, 0x3040):
        chars.add(ch)
    for ch in range(0xFF01, 0xFF5F):
        chars.add(ch)

    # GB2312 常用汉字
    for high in range(0xB0, 0xF8):
        for low in range(0xA1, 0xFF):
            try:
                text = bytes([high, low]).decode("gb2312")
            except UnicodeDecodeError:
                continue
            chars.add(ord(text))

    return sorted(chars)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    regular, semibold = find_faces()
    print("regular:", regular is not None, "semibold:", semibold is not None)

    unicodes = build_unicodes()
    print("charset size:", len(unicodes))
    unicode_arg = ",".join(f"U+{u:04X}" for u in unicodes)

    for face, name in ((regular, "Regular"), (semibold, "SemiBold")):
        if face is None:
            continue
        raw = os.path.join(OUT_DIR, f"_raw-{name}.ttf")
        final = os.path.join(OUT_DIR, f"SarasaUiSC-{name}.ttf")
        face.save(raw)
        subset.main([
            raw,
            f"--output-file={final}",
            f"--unicodes={unicode_arg}",
            "--layout-features=*",
            "--name-IDs=*",
            "--drop-tables+=DSIG",
            "--recalc-bounds",
        ])
        os.remove(raw)
        print(name, os.path.getsize(final) // 1024, "KB")


if __name__ == "__main__":
    main()
