"""One-shot generator for tests/fixtures/sample-text.pdf (committed fixture).

Multi-paragraph Helvetica (base-14, no embedding needed) with xref offsets
computed, so no font files are required. Re-run to regenerate.
"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))

LINES = [
    (50, 700, "The quick brown fox jumps over"),
    (50, 676, "the lazy dog near the river bank"),
    (50, 652, "Pack my box with five dozen"),
    (50, 628, "liquor jugs for the long trip"),
    (50, 580, "Second paragraph begins here now"),
    (50, 556, "and continues with more words"),
]


def build() -> bytes:
    ops = ["BT /F1 14 Tf"]
    ops.append(f"{LINES[0][0]} {LINES[0][1]} Td")
    prev_y = LINES[0][1]
    for i, (x, y, text) in enumerate(LINES):
        if i > 0:
            ops.append(f"0 {y - prev_y} Td")
            prev_y = y
        safe = text.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")
        ops.append(f"({safe}) Tj")
    ops.append("ET")
    content = "\n".join(ops)
    objs = [
        "<</Type/Catalog/Pages 2 0 R>>",
        "<</Type/Pages/Kids[3 0 R]/Count 1>>",
        "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]"
        "/Resources<</Font<</F1 4 0 R>>>>/Contents 5 0 R>>",
        "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
        f"<</Length {len(content.encode('ascii'))}>>\nstream\n{content}\nendstream",
    ]
    out = bytearray(b"%PDF-1.4\n")
    offsets = []
    for i, body in enumerate(objs):
        offsets.append(len(out))
        out += f"{i + 1} 0 obj\n{body}\nendobj\n".encode("ascii")
    xref = len(out)
    out += f"xref\n0 {len(objs) + 1}\n0000000000 65535 f \n".encode("ascii")
    for o in offsets:
        out += f"{o:010d} 00000 n \n".encode("ascii")
    out += (
        f"trailer\n<</Size {len(objs) + 1}/Root 1 0 R>>\n"
        f"startxref\n{xref}\n%%EOF"
    ).encode("ascii")
    return bytes(out)


if __name__ == "__main__":
    pdf = build()
    with open(os.path.join(HERE, "sample-text.pdf"), "wb") as f:
        f.write(pdf)
    print(f"wrote sample-text.pdf ({len(pdf)} bytes)")
