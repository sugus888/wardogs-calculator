#!/usr/bin/env python3
"""Build the ซู-ปืนครก tie-break glyph atlas from labelled debug captures.

Usage:
    python3 gen_glyphs.py <debug-dir> <output-dir> [--check]

<debug-dir> is a copy of %AppData%\\SuMortar\\Debug (``*_result.json`` plus the
``*_Tesseract_<pipeline>_processed.png`` images the app writes). Only captures
whose coordinate was read successfully are used; their known text labels the
glyphs automatically. Each label's template is the mean of all its samples.

The segmentation and normalisation mirror GlyphSegmenter / GlyphNormalizer in
MortarHUD.Capture so the C# TemplateOcrEngine can load the result directly.
``--check`` re-reads every capture with leave-one-out templates and reports
any capture whose known coordinate is not reproduced.
"""
import collections
import glob
import json
import os
import sys

import cv2
import numpy as np

TW, TH = 20, 28
LABELS = "xy.0123456789"


def segment(img):
    wob = 255 - img
    _, binary = cv2.threshold(wob, 127, 255, cv2.THRESH_BINARY)
    n, lab, st, _ = cv2.connectedComponentsWithStats(binary, connectivity=8)
    out = []
    max_h = img.shape[0] * 0.45
    for i in range(1, n):
        left, top, w, h, area = st[i]
        if area < 8 or h > max_h:
            continue
        iso = np.where(lab == i, wob, 0).astype(np.uint8)[top:top + h, left:left + w]
        out.append((iso, (left, top, w, h)))
    return out


def lines(glyphs):
    if not glyphs:
        return []
    ordered = sorted(glyphs, key=lambda g: g[1][1])
    heights = collections.Counter(g[1][3] for g in ordered)
    modal = sorted(heights.items(), key=lambda kv: (-kv[1], -kv[0]))[0][0]
    tol = max(4.0, modal * 0.8)
    groups = []
    for g in ordered:
        c = g[1][1] + g[1][3] // 2
        for grp in groups:
            if abs(grp[0] - c) <= tol:
                grp[1].append(g)
                grp[0] = np.mean([x[1][1] + x[1][3] // 2 for x in grp[1]])
                break
        else:
            groups.append([c, [g]])
    return [sorted(g[1], key=lambda x: x[1][0]) for g in sorted(groups, key=lambda g: g[0])]


def normalize(glyph):
    canvas = np.zeros((TH, TW), np.uint8)
    ys, xs = np.nonzero(glyph)
    if len(xs) == 0:
        return canvas
    glyph = glyph[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    h, w = glyph.shape
    s = min(TW / w, TH / h)
    tw, th = max(1, round(w * s)), max(1, round(h * s))
    resized = cv2.resize(glyph, (tw, th), interpolation=cv2.INTER_AREA if s < 1 else cv2.INTER_LINEAR)
    ox, oy = (TW - tw) // 2, (TH - th) // 2
    canvas[oy:oy + th, ox:ox + tw] = resized
    return canvas


def similarity(a, b):
    return 1 - np.mean(np.abs(a.astype(float) - b.astype(float))) / 255


def load(debug_dir):
    items = []
    for path in sorted(glob.glob(os.path.join(debug_dir, "*_result.json"))):
        result = json.load(open(path, encoding="utf-8-sig"))
        if not result.get("success"):
            continue
        stem = path[: -len("_result.json")]
        c = result["coordinate"]
        truth = ["y%.2f" % c["y"], "x%.2f" % c["x"]]
        for image in glob.glob(stem + "_Tesseract_*_processed.png"):
            items.append((stem, cv2.imread(image, cv2.IMREAD_GRAYSCALE), truth))
    return items


def collect(items):
    samples = collections.defaultdict(list)
    for stem, img, truth in items:
        found = [ln for ln in lines(segment(img)) if len(ln) >= 4]
        if len(found) != 2 or any(len(ln) != len(t) for ln, t in zip(found, truth)):
            continue
        for ln, text in zip(found, truth):
            for (glyph, _), ch in zip(ln, text):
                samples[ch].append((stem, normalize(glyph)))
    return samples


def templates(samples, exclude=None):
    out = {}
    for label, entries in samples.items():
        arr = [n for s, n in entries if s != exclude]
        if arr:
            out[label] = np.mean(arr, axis=0).astype(np.uint8)
    return out


def read(img, tpl):
    text = []
    for ln in lines(segment(img)):
        s = ""
        for glyph, _ in ln:
            n = normalize(glyph)
            label, score = max(((k, similarity(n, v)) for k, v in tpl.items()), key=lambda kv: kv[1])
            s += label if score >= 0.70 else "?"
        text.append(s)
    return text


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 2
    debug_dir, out_dir = sys.argv[1], sys.argv[2]
    items = load(debug_dir)
    samples = collect(items)
    missing = [c for c in LABELS if c not in samples]
    print("samples:", {k: len(v) for k, v in sorted(samples.items())})
    if missing:
        print("missing labels:", "".join(missing))
        return 1

    if "--check" in sys.argv:
        bad = 0
        for stem, img, truth in items:
            got = read(img, templates(samples, exclude=stem))
            if truth[0] not in got or truth[1] not in got:
                bad += 1
                print("MISS", os.path.basename(stem), truth, got)
        print(f"leave-one-out: {len(items) - bad}/{len(items)} reproduced")

    tpl = templates(samples)
    os.makedirs(out_dir, exist_ok=True)
    sprite = np.vstack([tpl[c] for c in LABELS])
    cv2.imwrite(os.path.join(out_dir, "glyphs.png"), sprite)
    with open(os.path.join(out_dir, "glyphs.json"), "w", encoding="utf-8") as f:
        json.dump([{"label": c, "pipeline": "C-2560x1600"} for c in LABELS], f, indent=2)
    print("wrote", len(LABELS), "templates to", out_dir)
    return 0


if __name__ == "__main__":
    sys.exit(main())
