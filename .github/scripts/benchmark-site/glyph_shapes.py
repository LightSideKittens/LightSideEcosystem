"""Outlines and outline complexity of the glyph suite's fonts, for the page's gallery: per font every glyph that has an
outline, composite glyphs decomposed into their components' contours, its on- and off-curve points and contours, and
the SVG paths of three mapped glyphs at the 10th, 50th and 90th percentile of point count. Given the characters the
suite rasterized with a font, the same figures for their glyphs come beside the whole font's, and the samples are
drawn from them.

  python -I -X utf8 glyph_shapes.py --out <file> --fonts <search root> <font name> [<font name>...]

A font name is the glyph suite's (`NotoSans-Regular`, `TiltNeon`): the font file whose name before any `[axes]` part
matches it, found under the search roots. Needs fontTools."""
import argparse
import json
import os
import statistics

from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.ttLib import TTFont

EXTENSIONS = ('.ttf', '.otf')
PERCENTILES = (0.1, 0.5, 0.9)
DRAWING = ('moveTo', 'lineTo', 'qCurveTo', 'curveTo')


def find(name, roots):
    """The one font file under `roots` named `name`; raises when there is none or more than one."""
    found = []
    for root in roots:
        for folder, _, files in os.walk(root):
            for f in files:
                stem, ext = os.path.splitext(f)
                if ext.lower() in EXTENSIONS and stem.split('[')[0] == name:
                    found.append(os.path.join(folder, f))
    if len(found) != 1:
        raise SystemExit(f'font {name!r}: {len(found)} files under {roots}: {found}')
    return found[0]


def outline(glyph_set, glyph_name):
    """(points, contours) of a glyph with its components decomposed."""
    pen = DecomposingRecordingPen(glyph_set)
    glyph_set[glyph_name].draw(pen)
    return (sum(len(args) for op, args in pen.value if op in DRAWING), sum(1 for op, _ in pen.value if op == 'moveTo'))


def complexity(shapes):
    points = [s[0] for s in shapes.values()]
    contours = [s[1] for s in shapes.values()]
    return {'glyphs': len(shapes), 'medianPoints': statistics.median(points), 'maxPoints': max(points),
            'medianContours': statistics.median(contours), 'maxContours': max(contours)}


def font_entry(path, characters=None):
    font = TTFont(path)
    glyph_set = font.getGlyphSet()
    upem = font['head'].unitsPerEm
    shapes = {g: outline(glyph_set, g) for g in glyph_set.keys()}
    shapes = {g: s for g, s in shapes.items() if s[1]}
    cmap = font.getBestCmap()
    codes = sorted({ord(c) for c in characters}) if characters else sorted(cmap)
    mapped = sorted({cmap[c]: c for c in reversed(codes) if cmap.get(c) in shapes}.items(),
                    key=lambda item: (shapes[item[0]][0], item[1]))
    if not mapped:
        raise SystemExit(f'{path}: none of the rasterized characters has an outline in the font')
    samples = []
    for q in PERCENTILES:
        glyph_name, code = mapped[min(int(q * len(mapped)), len(mapped) - 1)]
        svg = SVGPathPen(glyph_set)
        glyph_set[glyph_name].draw(svg)
        bounds = BoundsPen(glyph_set)
        glyph_set[glyph_name].draw(bounds)
        x0, y0, x1, y1 = bounds.bounds
        samples.append({'char': chr(code), 'percentile': q, 'd': svg.getCommands(),
                        'bounds': [round(x0), round(y0), round(x1), round(y1)],
                        'points': shapes[glyph_name][0], 'contours': shapes[glyph_name][1]})
    entry = {'file': os.path.basename(path), 'upem': upem, 'font': complexity(shapes), 'samples': samples}
    if characters:
        entry['rasterized'] = complexity({g: shapes[g] for g, _ in mapped})
    return entry


def build(names, roots, characters=None):
    """Gallery entries of the fonts `names`; `characters` maps a font name to the text the suite rasterized with it."""
    return {name: font_entry(find(name, roots), (characters or {}).get(name)) for name in names}


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--out', required=True)
    p.add_argument('--fonts', action='append', required=True)
    p.add_argument('names', nargs='+')
    a = p.parse_args()
    data = build(a.names, a.fonts)
    json.dump(data, open(a.out, 'w', encoding='utf-8'), ensure_ascii=False, separators=(',', ':'))
    for name, entry in data.items():
        font = entry['font']
        print(f'{name}: {font["glyphs"]} glyphs, median {font["medianPoints"]} points / {font["medianContours"]} contours')


if __name__ == '__main__':
    main()
