"""
Generates cat-themed Windows cursors in several colours, including animated
(.ani) busy/loading pointers, plus the app icon and README imagery.

Output: build/<Colour>/ containing one cursor per pointer role:
  - 14 static  .cur  (face pointer, arrow+cat pointer, paw, resize, move, ...)
  - 2 animated .ani  (busy = sleeping cat with z-z-z, working = spinner)
  - preview.png      (128px face used by the app)

Everything is drawn at 4x supersampling and downscaled with Lanczos so edges
stay smooth at every cursor size (32/48/64/96 px).
"""

import os
import math
import struct
from PIL import Image, ImageDraw, ImageFont, ImageFilter

LANCZOS = Image.LANCZOS

# ---- palettes ---------------------------------------------------------------
# fur       main coat colour
# light     muzzle / cheek highlight
# dark      markings (tabby stripes, siamese mask) and shadow
# inner     inner ear
# outline   outline colour (light for dark cats so they read on dark screens)
# eye       iris colour
# marks     "tabby" | "siamese" | "tuxedo" | "calico" | None
PALETTES = {
    "Orange":  dict(fur=(255, 168, 62),  light=(255, 214, 150), dark=(222, 122, 28),
                    inner=(255, 196, 170), outline=(74, 46, 20),   eye=(96, 182, 92),  marks="tabby"),
    "Black":   dict(fur=(56, 58, 68),    light=(92, 95, 108),   dark=(34, 36, 44),
                    inner=(150, 118, 128), outline=(222, 224, 232), eye=(255, 188, 56), marks=None),
    "Grey":    dict(fur=(160, 165, 176), light=(206, 210, 218), dark=(112, 117, 130),
                    inner=(224, 198, 204), outline=(56, 58, 68),   eye=(206, 192, 80), marks="tabby"),
    "White":   dict(fur=(246, 247, 250), light=(255, 255, 255), dark=(212, 215, 224),
                    inner=(255, 206, 206), outline=(96, 98, 110),  eye=(84, 146, 232), marks=None),
    "Siamese": dict(fur=(238, 226, 204), light=(248, 240, 226), dark=(104, 76, 58),
                    inner=(214, 182, 162), outline=(92, 68, 50),   eye=(78, 140, 236), marks="siamese"),
    "Calico":  dict(fur=(250, 247, 242), light=(255, 255, 255), dark=(222, 122, 28),
                    inner=(255, 200, 190), outline=(80, 60, 44),   eye=(226, 170, 62), marks="calico"),
    "Tuxedo":  dict(fur=(48, 50, 60),    light=(248, 248, 250), dark=(30, 32, 40),
                    inner=(170, 130, 140), outline=(222, 224, 232), eye=(120, 200, 110), marks="tuxedo"),
}
COLORS = ["Orange", "Black", "Grey", "White", "Siamese", "Calico", "Tuxedo"]

FUR = FUR_LIGHT = FUR_DARK = INNER_EAR = OUTLINE = EYE = None
MARKS = None

# ---- constant colours -------------------------------------------------------
WHITE    = (255, 255, 255, 255)
BLACK    = (28, 24, 26, 255)
PINK     = (255, 118, 148, 255)
PAD      = (255, 130, 158, 255)
RED      = (224, 48, 48, 255)
YELLOW   = (255, 204, 70, 255)
GRAPHITE = (62, 62, 72, 255)
WOOD     = (246, 222, 172, 255)
SPINNER  = (0, 120, 215, 255)
CAL_BLACK = (52, 54, 64, 255)
INK      = (58, 46, 40, 255)      # fixed dark outline for arrows/beams (always visible on white)

SS = 4
SIZES = [32, 48, 64, 96]
ANI_SIZES = [32, 48, 64]
FONT_DIR = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")


def set_palette(name):
    global FUR, FUR_LIGHT, FUR_DARK, INNER_EAR, OUTLINE, EYE, MARKS
    p = PALETTES[name]
    FUR = p["fur"] + (255,)
    FUR_LIGHT = p["light"] + (255,)
    FUR_DARK = p["dark"] + (255,)
    INNER_EAR = p["inner"] + (255,)
    OUTLINE = p["outline"] + (255,)
    EYE = p["eye"] + (255,)
    MARKS = p["marks"]


def _font(px, bold=True):
    names = ("segoeuib.ttf", "arialbd.ttf") if bold else ("segoeui.ttf", "arial.ttf")
    for name in names:
        try:
            return ImageFont.truetype(os.path.join(FONT_DIR, name), px)
        except Exception:
            pass
    return ImageFont.load_default()


def _canvas(size):
    S = size * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img), S


def _ow(S):
    return max(2, S // 40)


def _fin(img, size):
    return img.resize((size, size), LANCZOS)


# ---- geometry helpers --------------------------------------------------------

def _infl_box(box, k):
    x0, y0, x1, y1 = box
    return [x0 - k, y0 - k, x1 + k, y1 + k]


def _infl_poly(pts, k):
    """Push every vertex outward (away from the centroid) by ~k pixels."""
    cx = sum(p[0] for p in pts) / len(pts)
    cy = sum(p[1] for p in pts) / len(pts)
    out = []
    for x, y in pts:
        dx, dy = x - cx, y - cy
        dist = math.hypot(dx, dy) or 1.0
        f = (dist + k * 1.6) / dist
        out.append((cx + dx * f, cy + dy * f))
    return out


def _union(S, shapes, fill, ow, outline=None):
    """
    Draw a set of overlapping shapes as ONE silhouette with a single clean
    outline: first every shape inflated in the outline colour, then every
    shape in the fill colour. Returns (outline_layer, fill_layer).
    shapes: list of ("e", box) or ("p", points).
    """
    outline = outline or OUTLINE
    lo = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    lf = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    do, df = ImageDraw.Draw(lo), ImageDraw.Draw(lf)
    for kind, g in shapes:
        if kind == "e":
            do.ellipse(_infl_box(g, ow), fill=outline)
        else:
            do.polygon(_infl_poly(g, ow), fill=outline)
    for kind, g in shapes:
        if kind == "e":
            df.ellipse(g, fill=fill)
        else:
            df.polygon(g, fill=fill)
    return lo, lf


def _masked(base_layer, draw_fn, S):
    """Draw onto a fresh layer, then clip it to base_layer's alpha."""
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    draw_fn(ImageDraw.Draw(layer))
    clipped = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    clipped.paste(layer, (0, 0), base_layer.split()[3])
    return clipped


def _place(layer, S, x, y):
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(layer, (x, y), layer)
    return out


def _soft_shadow(layer, S, blur, alpha=70, dy=0):
    """A soft drop shadow built from a layer's alpha (for the app icon)."""
    a = layer.split()[3].filter(ImageFilter.GaussianBlur(blur))
    sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sh.paste((0, 0, 0, alpha), (0, 0), a)
    if dy:
        sh = _place(sh, S, 0, dy)
    return sh


# ---- shared drawing helpers --------------------------------------------------

def _beam(d, p1, p2, w, ow):
    # white bar with a heavier dark edge so it survives downscaling to 32 px
    d.line([p1, p2], fill=INK, width=int(w + 3.2 * ow))
    d.line([p1, p2], fill=WHITE, width=int(max(1, w)))


def _arrowhead(d, tip, ang, sz, ow):
    bx = tip[0] - sz * math.cos(ang)
    by = tip[1] - sz * math.sin(ang)
    px = math.cos(ang + math.pi / 2)
    py = math.sin(ang + math.pi / 2)
    w = sz * 0.9
    d.polygon([tip, (bx + px * w, by + py * w), (bx - px * w, by - py * w)],
              fill=WHITE, outline=INK, width=int(ow * 1.6))


def _eye(d, cx, cy, rx, ry, ow):
    """A cat eye: white, coloured iris, slit pupil and a highlight."""
    d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=WHITE, outline=OUTLINE, width=ow)
    d.ellipse([cx - rx * 0.78, cy - ry * 0.80, cx + rx * 0.78, cy + ry * 0.85], fill=EYE)
    d.ellipse([cx - rx * 0.30, cy - ry * 0.70, cx + rx * 0.30, cy + ry * 0.75], fill=BLACK)
    d.ellipse([cx - rx * 0.52, cy - ry * 0.62, cx - rx * 0.12, cy - ry * 0.18], fill=WHITE)


def _cat_hub(img, cx, cy, r, ow):
    """A small cat face used as the hub of resize/move/crosshair cursors."""
    S = img.size[0]
    ears = [
        ("p", [(cx - r * 0.92, cy - r * 0.30), (cx - r * 0.50, cy - r * 1.30), (cx - r * 0.02, cy - r * 0.55)]),
        ("p", [(cx + r * 0.92, cy - r * 0.30), (cx + r * 0.50, cy - r * 1.30), (cx + r * 0.02, cy - r * 0.55)]),
    ]
    shapes = ears + [("e", [cx - r, cy - r * 0.95, cx + r, cy + r])]
    lo, lf = _union(S, shapes, FUR, ow)
    img.alpha_composite(lo)
    img.alpha_composite(lf)
    d = ImageDraw.Draw(img)
    for sx in (-1, 1):
        inner = [(cx + sx * r * 0.80, cy - r * 0.42), (cx + sx * r * 0.50, cy - r * 1.05), (cx + sx * r * 0.18, cy - r * 0.58)]
        d.polygon(inner, fill=FUR_DARK if MARKS == "siamese" else INNER_EAR)
    if MARKS == "siamese":
        d.ellipse([cx - r * 0.62, cy - r * 0.35, cx + r * 0.62, cy + r * 0.72], fill=FUR_DARK)
    if MARKS == "tuxedo":
        d.ellipse([cx - r * 0.48, cy + r * 0.05, cx + r * 0.48, cy + r * 0.85], fill=FUR_LIGHT)
    er = r * 0.19
    for ex in (cx - r * 0.42, cx + r * 0.42):
        _eye(d, ex, cy + r * 0.05, er, er * 1.15, max(1, ow // 2))
    d.polygon([(cx - r * 0.13, cy + r * 0.38), (cx + r * 0.13, cy + r * 0.38), (cx, cy + r * 0.58)], fill=PINK)


# ---- the designs -------------------------------------------------------------

def draw_cat(size):
    """The main cat face. The left ear is long and sharp: it is the pointer tip."""
    img, _, S = _canvas(size)
    ow = _ow(S)

    def px(x, y):
        return (x * S, y * S)

    def box(x0, y0, x1, y1):
        return [x0 * S, y0 * S, x1 * S, y1 * S]

    ear_l = [px(0.015, 0.015), px(0.40, 0.20), px(0.10, 0.46)]
    ear_r = [px(0.86, 0.05), px(0.975, 0.42), px(0.58, 0.26)]
    head = ("e", box(0.10, 0.20, 0.945, 0.955))
    cheek_l = ("e", box(0.065, 0.54, 0.52, 0.985))
    cheek_r = ("e", box(0.52, 0.54, 0.975, 0.985))
    lo, lf = _union(S, [("p", ear_l), ("p", ear_r), head, cheek_l, cheek_r], FUR, ow)
    img.alpha_composite(lo)
    img.alpha_composite(lf)

    # markings and shading, clipped to the fur silhouette
    def marks(d):
        if MARKS == "tabby":
            d.ellipse(box(0.27, 0.60, 0.76, 0.96), fill=FUR_LIGHT)
            for x0, x1 in ((0.50, 0.50), (0.40, 0.43), (0.60, 0.57)):
                d.line([px(x0, 0.235), px(x1, 0.42)], fill=FUR_DARK, width=int(ow * 2.4))
            d.line([px(0.17, 0.62), px(0.25, 0.70)], fill=FUR_DARK, width=int(ow * 1.6))
            d.line([px(0.87, 0.62), px(0.79, 0.70)], fill=FUR_DARK, width=int(ow * 1.6))
        elif MARKS == "siamese":
            d.ellipse(box(0.26, 0.38, 0.78, 0.90), fill=FUR_DARK)
            d.ellipse(box(0.34, 0.70, 0.70, 0.96), fill=FUR_LIGHT)
            d.polygon(ear_l, fill=FUR_DARK)
            d.polygon(ear_r, fill=FUR_DARK)
        elif MARKS == "tuxedo":
            d.ellipse(box(0.30, 0.56, 0.74, 0.97), fill=FUR_LIGHT)
            d.polygon([px(0.47, 0.22), px(0.56, 0.22), px(0.58, 0.60), px(0.45, 0.60)], fill=FUR_LIGHT)
        elif MARKS == "calico":
            d.ellipse(box(-0.05, 0.10, 0.50, 0.58), fill=FUR_DARK)
            d.polygon(ear_l, fill=FUR_DARK)
            d.ellipse(box(0.60, 0.20, 1.05, 0.62), fill=CAL_BLACK)
            d.polygon(ear_r, fill=CAL_BLACK)
            d.ellipse(box(0.62, 0.72, 0.98, 0.98), fill=FUR_DARK)
            d.ellipse(box(0.27, 0.60, 0.76, 0.96), fill=FUR_LIGHT)
        else:
            d.ellipse(box(0.27, 0.60, 0.76, 0.96), fill=FUR_LIGHT)
    img.alpha_composite(_masked(lf, marks, S))

    d = ImageDraw.Draw(img)
    # inner ears
    if MARKS == "siamese":
        d.polygon([px(0.11, 0.11), px(0.29, 0.21), px(0.15, 0.34)], fill=INNER_EAR)
        d.polygon([px(0.84, 0.16), px(0.90, 0.35), px(0.70, 0.28)], fill=INNER_EAR)
    else:
        d.polygon([px(0.09, 0.09), px(0.33, 0.21), px(0.14, 0.38)], fill=INNER_EAR)
        d.polygon([px(0.84, 0.13), px(0.92, 0.37), px(0.66, 0.28)], fill=INNER_EAR)

    # eyes
    for cx in (0.35, 0.67):
        _eye(d, cx * S, 0.56 * S, 0.095 * S, 0.115 * S, max(1, int(ow * 0.8)))

    # nose + mouth
    d.polygon([px(0.445, 0.705), px(0.575, 0.705), px(0.51, 0.785)],
              fill=PINK, outline=OUTLINE, width=max(1, ow // 2))
    d.line([px(0.51, 0.785), px(0.51, 0.83)], fill=OUTLINE, width=ow)
    d.arc(box(0.40, 0.775, 0.51, 0.875), start=10, end=170, fill=OUTLINE, width=ow)
    d.arc(box(0.51, 0.775, 0.62, 0.875), start=10, end=170, fill=OUTLINE, width=ow)

    # whiskers
    ww = max(1, int(ow * 0.6))
    for y, dy in ((0.70, -0.06), (0.75, -0.015), (0.80, 0.03)):
        d.line([px(0.27, y), px(0.0, y + dy)], fill=OUTLINE, width=ww)
        d.line([px(0.75, y), px(1.0, y + dy)], fill=OUTLINE, width=ww)
    return _fin(img, size)


def draw_arrow_cat(size):
    """Alternative pointer: a classic arrow with a small cat perched on it."""
    img, d, S = _canvas(size)
    ow = _ow(S)

    def px(x, y):
        return (x * S, y * S)

    arrow = [px(0.04, 0.03), px(0.04, 0.72), px(0.215, 0.565), px(0.33, 0.82),
             px(0.455, 0.765), px(0.345, 0.515), px(0.585, 0.515)]
    d.polygon(arrow, fill=WHITE, outline=INK, width=ow)
    _cat_hub(img, 0.73 * S, 0.70 * S, 0.21 * S, ow)
    return _fin(img, size)


def draw_paw(size):
    img, d, S = _canvas(size)
    ow = _ow(S)

    def px(x, y):
        return (x * S, y * S)

    toes = [(0.22, 0.31, 0.095), (0.41, 0.19, 0.105),
            (0.60, 0.19, 0.105), (0.79, 0.33, 0.095)]
    shapes = [("e", [px(cx - r, cy - r)[0], px(cx - r, cy - r)[1], px(cx + r, cy + r)[0], px(cx + r, cy + r)[1]])
              for cx, cy, r in toes]
    shapes.append(("e", [px(0.16, 0.42)[0], px(0.16, 0.42)[1], px(0.84, 0.965)[0], px(0.84, 0.965)[1]]))
    lo, lf = _union(S, shapes, FUR, ow)
    img.alpha_composite(lo)
    img.alpha_composite(lf)
    d = ImageDraw.Draw(img)
    for cx, cy, r in toes:
        pr = r * 0.62
        d.ellipse([px(cx - pr, cy - pr * 0.75)[0], px(cx - pr, cy - pr * 0.75)[1],
                   px(cx + pr, cy + pr * 1.1)[0], px(cx + pr, cy + pr * 1.1)[1]], fill=PAD)
    d.ellipse([px(0.29, 0.55)[0], px(0.29, 0.55)[1], px(0.71, 0.90)[0], px(0.71, 0.90)[1]], fill=PAD)
    d.ellipse([px(0.36, 0.60)[0], px(0.36, 0.60)[1], px(0.50, 0.70)[0], px(0.50, 0.70)[1]],
              fill=(255, 190, 205, 255))
    return _fin(img, size)


def draw_resize(size, angle_deg):
    img, d, S = _canvas(size)
    ow = _ow(S)
    a = math.radians(angle_deg)
    cx = cy = S / 2
    L, head = S * 0.46, S * 0.21
    dx, dy = math.cos(a), math.sin(a)
    _beam(d, (cx - dx * (L - head), cy - dy * (L - head)),
          (cx + dx * (L - head), cy + dy * (L - head)), S * 0.085, ow)
    _arrowhead(d, (cx + dx * L, cy + dy * L), a, head, ow)
    _arrowhead(d, (cx - dx * L, cy - dy * L), a + math.pi, head, ow)
    _cat_hub(img, cx, cy, S * 0.17, ow)
    return _fin(img, size)


def draw_move(size):
    img, d, S = _canvas(size)
    ow = _ow(S)
    cx = cy = S / 2
    L, head = S * 0.46, S * 0.19
    for a in (0, math.pi / 2, math.pi, 3 * math.pi / 2):
        dx, dy = math.cos(a), math.sin(a)
        _beam(d, (cx, cy), (cx + dx * (L - head), cy + dy * (L - head)), S * 0.08, ow)
    for a in (0, math.pi / 2, math.pi, 3 * math.pi / 2):
        dx, dy = math.cos(a), math.sin(a)
        _arrowhead(d, (cx + dx * L, cy + dy * L), a, head, ow)
    _cat_hub(img, cx, cy, S * 0.16, ow)
    return _fin(img, size)


def draw_cross(size):
    img, d, S = _canvas(size)
    ow = _ow(S)
    cx = cy = S / 2
    gap, end, w = S * 0.12, S * 0.47, S * 0.055
    _beam(d, (cx, cy - end), (cx, cy - gap), w, ow)
    _beam(d, (cx, cy + gap), (cx, cy + end), w, ow)
    _beam(d, (cx - end, cy), (cx - gap, cy), w, ow)
    _beam(d, (cx + gap, cy), (cx + end, cy), w, ow)
    d.ellipse([cx - ow, cy - ow, cx + ow, cy + ow], fill=INK)
    _cat_hub(img, S * 0.19, S * 0.19, S * 0.105, max(1, ow // 2))
    return _fin(img, size)


def draw_ibeam(size):
    img, d, S = _canvas(size)
    ow = _ow(S)
    # A bold I-beam with a small cat sitting on top of it.
    cx = S / 2
    top, bot, w, sw = S * 0.36, S * 0.88, S * 0.085, S * 0.15
    _beam(d, (cx, top), (cx, bot), w, ow)
    _beam(d, (cx - sw, top), (cx + sw, top), w * 0.8, ow)
    _beam(d, (cx - sw, bot), (cx + sw, bot), w * 0.8, ow)
    _cat_hub(img, cx, S * 0.225, S * 0.135, ow)
    return _fin(img, size)


def _sleeping_cat(img, S, ow):
    body = ("e", [S * 0.16, S * 0.47, S * 0.90, S * 0.87])
    hx, hy, hr = S * 0.34, S * 0.57, S * 0.165
    head = ("e", [hx - hr, hy - hr, hx + hr, hy + hr])
    ears = [("p", [(hx - hr * 0.9, hy - hr * 0.4), (hx - hr * 0.45, hy - hr * 1.32), (hx - hr * 0.05, hy - hr * 0.55)]),
            ("p", [(hx + hr * 0.9, hy - hr * 0.4), (hx + hr * 0.45, hy - hr * 1.32), (hx + hr * 0.05, hy - hr * 0.55)])]
    lo, lf = _union(S, [body, head] + ears, FUR, ow)
    img.alpha_composite(lo)
    img.alpha_composite(lf)
    d = ImageDraw.Draw(img)
    d.arc([S * 0.60, S * 0.44, S * 0.99, S * 0.90], start=-45, end=215, fill=OUTLINE, width=int(ow * 3.2))
    d.arc([S * 0.60, S * 0.44, S * 0.99, S * 0.90], start=-45, end=215, fill=FUR, width=int(ow * 1.4))
    if MARKS == "tabby":
        for t in (0.50, 0.60, 0.70):
            d.arc([S * t, S * 0.47, S * (t + 0.18), S * 0.87], start=200, end=320, fill=FUR_DARK, width=int(ow * 1.6))
    if MARKS == "siamese":
        d.ellipse([hx - hr * 0.6, hy - hr * 0.35, hx + hr * 0.6, hy + hr * 0.7], fill=FUR_DARK)
    for sx in (-1, 1):
        d.polygon([(hx + sx * hr * 0.78, hy - hr * 0.50), (hx + sx * hr * 0.46, hy - hr * 1.05), (hx + sx * hr * 0.18, hy - hr * 0.60)],
                  fill=FUR_DARK if MARKS == "siamese" else INNER_EAR)
    for ex in (hx - hr * 0.42, hx + hr * 0.42):
        d.arc([ex - hr * 0.22, hy - hr * 0.10, ex + hr * 0.22, hy + hr * 0.30], start=200, end=340, fill=OUTLINE, width=ow)
    d.polygon([(hx - hr * 0.10, hy + hr * 0.30), (hx + hr * 0.10, hy + hr * 0.30), (hx, hy + hr * 0.45)], fill=PINK)


def _zzz(d, S, t):
    bx, by = S * 0.50, S * 0.40
    for k in range(3):
        ph = ((t + k / 3.0) % 1.0)
        x, y = bx + ph * S * 0.36 + math.sin(ph * 6.28) * S * 0.04, by - ph * S * 0.36
        a = int(230 * (1 - ph))
        fs = max(6, int(S * (0.11 + 0.13 * ph)))
        d.text((x, y), "z", font=_font(fs), fill=(INK[0], INK[1], INK[2], a), anchor="mm",
               stroke_width=max(1, S // 64), stroke_fill=(255, 255, 255, a))


def draw_busy_frame(size, t):
    img, d, S = _canvas(size)
    ow = _ow(S)
    _sleeping_cat(img, S, ow)
    _zzz(ImageDraw.Draw(img), S, t)
    return _fin(img, size)


def draw_busy(size):                       # static frame for previews
    return draw_busy_frame(size, 0.5)


def draw_no(size):
    img, d, S = _canvas(size)
    ow = _ow(S)
    cx = cy = S / 2
    _cat_hub(img, cx, cy + S * 0.03, S * 0.21, ow)
    d = ImageDraw.Draw(img)
    R, rw = S * 0.42, int(S * 0.085)
    d.ellipse([cx - R - ow, cy - R - ow, cx + R + ow, cy + R + ow], outline=WHITE, width=rw + 2 * ow)
    d.ellipse([cx - R, cy - R, cx + R, cy + R], outline=RED, width=rw)
    a = math.radians(45)
    d.line([(cx - math.cos(a) * R, cy - math.sin(a) * R),
            (cx + math.cos(a) * R, cy + math.sin(a) * R)], fill=RED, width=rw)
    return _fin(img, size)


def draw_up(size):
    img, d, S = _canvas(size)
    ow = _ow(S)
    cx = S / 2
    _beam(d, (cx, S * 0.30), (cx, S * 0.86), S * 0.08, ow)
    _arrowhead(d, (cx, S * 0.08), -math.pi / 2, S * 0.21, ow)
    _cat_hub(img, cx, S * 0.74, S * 0.13, ow)
    return _fin(img, size)


def draw_pen(size):
    img, d, S = _canvas(size)
    ow = _ow(S)
    start, end = (S * 0.24, S * 0.24), (S * 0.76, S * 0.76)
    d.line([start, end], fill=OUTLINE, width=int(S * 0.18 + 2 * ow))
    d.line([start, end], fill=YELLOW, width=int(S * 0.18))
    d.line([(start[0] + S * 0.03, start[1] - S * 0.03), (end[0] + S * 0.03, end[1] - S * 0.03)],
           fill=(255, 226, 120, 255), width=int(S * 0.035))
    d.polygon([(S * 0.22, S * 0.22), (S * 0.34, S * 0.20), (S * 0.20, S * 0.34)], fill=WOOD)
    d.polygon([(S * 0.10, S * 0.10), (S * 0.27, S * 0.19), (S * 0.19, S * 0.27)],
              fill=GRAPHITE, outline=OUTLINE, width=ow)
    _cat_hub(img, end[0], end[1], S * 0.12, ow)
    return _fin(img, size)


def _badge_cat(size, draw_badge):
    base = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    cat = draw_cat(max(8, int(size * 0.78)))
    base.paste(cat, (0, 0), cat)
    d = ImageDraw.Draw(base)
    b0, bd = int(size * 0.47), size - 1
    ow = max(1, size // 28)
    d.ellipse([b0, b0, bd, bd], fill=WHITE, outline=(60, 50, 40, 255), width=ow)
    draw_badge(d, b0, bd, size)
    return base


def draw_help(size):
    def badge(d, b0, bd, size):
        cx = cy = (b0 + bd) / 2
        d.text((cx, cy + (bd - b0) * 0.04), "?", font=_font(int((bd - b0) * 0.72)), fill=RED, anchor="mm")
    return _badge_cat(size, badge)


def draw_working_frame(size, t):
    def badge(d, b0, bd, size):
        m = int((bd - b0) * 0.17)
        st = t * 360.0
        d.arc([b0 + m, b0 + m, bd - m, bd - m], start=0, end=360,
              fill=(0, 120, 215, 60), width=max(2, size // 22))
        d.arc([b0 + m, b0 + m, bd - m, bd - m], start=st, end=st + 250,
              fill=SPINNER, width=max(2, size // 22))
    return _badge_cat(size, badge)


def draw_working(size):                    # static frame for previews
    return draw_working_frame(size, 0.0)


# ---- .cur / .ani packing -----------------------------------------------------

def _dib(img):
    img = img.convert("RGBA")
    w, h = img.size
    px = img.load()
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    color = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = px[x, y]
            color += bytes((b, g, r, a))
    mask = bytearray()
    row_bytes = ((w + 31) // 32) * 4
    for y in range(h - 1, -1, -1):
        row = bytearray(row_bytes)
        for x in range(w):
            if px[x, y][3] == 0:
                row[x // 8] |= (0x80 >> (x % 8))
        mask += row
    return header + bytes(color) + bytes(mask)


def build_cur_bytes(images_with_hotspots):
    n = len(images_with_hotspots)
    out = bytearray(struct.pack("<HHH", 0, 2, n))
    entries, blobs = [], []
    offset = 6 + 16 * n
    for img, hx, hy in images_with_hotspots:
        w, h = img.size
        blob = _dib(img)
        blobs.append(blob)
        entries.append(struct.pack("<BBBBHHII",
                                   0 if w >= 256 else w, 0 if h >= 256 else h,
                                   0, 0, hx, hy, len(blob), offset))
        offset += len(blob)
    for e in entries:
        out += e
    for b in blobs:
        out += b
    return bytes(out)


def build_cur(images, path):
    with open(path, "wb") as f:
        f.write(build_cur_bytes(images))


def build_ani(frame_curs, path, jiffies):
    def chunk(fourcc, data):
        out = fourcc + struct.pack("<I", len(data)) + data
        if len(data) % 2:
            out += b"\x00"
        return out

    n = len(frame_curs)
    # AF_ICON (0x1): each frame is a .cur (carries its own hotspot)
    anih = struct.pack("<IIIIIIIII", 36, n, n, 0, 0, 0, 0, jiffies, 0x0001)
    fram_data = b"".join(chunk(b"icon", c) for c in frame_curs)
    fram = b"LIST" + struct.pack("<I", 4 + len(fram_data)) + b"fram" + fram_data
    body = chunk(b"anih", anih) + fram
    with open(path, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", 4 + len(body)) + b"ACON" + body)


# ---- role table --------------------------------------------------------------
# (registry role, output file, hotspot fraction, draw fn) - static cursors.
# "Arrow" has two designs; the app picks one (cat_cursor = face, cat_arrow = arrow).
STATIC_DESIGNS = [
    ("Arrow",    "cat_cursor.cur", (0.02, 0.02), draw_cat),
    ("Arrow",    "cat_arrow.cur",  (0.04, 0.03), draw_arrow_cat),
    ("Hand",     "cat_paw.cur",    (0.50, 0.12), draw_paw),
    ("Help",     "cat_help.cur",   (0.02, 0.02), draw_help),
    ("IBeam",    "cat_text.cur",   (0.50, 0.52), draw_ibeam),
    ("Crosshair","cat_cross.cur",  (0.50, 0.50), draw_cross),
    ("No",       "cat_no.cur",     (0.50, 0.50), draw_no),
    ("SizeNS",   "cat_ns.cur",     (0.50, 0.50), lambda s: draw_resize(s, 90)),
    ("SizeWE",   "cat_we.cur",     (0.50, 0.50), lambda s: draw_resize(s, 0)),
    ("SizeNWSE", "cat_nwse.cur",   (0.50, 0.50), lambda s: draw_resize(s, 45)),
    ("SizeNESW", "cat_nesw.cur",   (0.50, 0.50), lambda s: draw_resize(s, -45)),
    ("SizeAll",  "cat_move.cur",   (0.50, 0.50), draw_move),
    ("NWPen",    "cat_pen.cur",    (0.10, 0.10), draw_pen),
    ("UpArrow",  "cat_up.cur",     (0.50, 0.10), draw_up),
]
# animated roles  ->  (file, hotspot, frame fn, frame count, jiffies)
ANIM_DESIGNS = [
    ("Wait",        "cat_busy.ani",    (0.50, 0.50), draw_busy_frame,    14, 9),
    ("AppStarting", "cat_working.ani", (0.02, 0.02), draw_working_frame, 12, 5),
]

# full role -> file order, used by the app/scripts (face pointer by default)
ROLE_FILES = (
    [(r, f) for r, f, _, _ in STATIC_DESIGNS if f != "cat_arrow.cur"] +
    [("Wait", "cat_busy.ani"), ("AppStarting", "cat_working.ani")]
)


def gen_color(name):
    set_palette(name)
    outdir = os.path.join("build", name)
    os.makedirs(outdir, exist_ok=True)
    for role, fname, hs, fn in STATIC_DESIGNS:
        frames = [(fn(s), int(round(s * hs[0])), int(round(s * hs[1]))) for s in SIZES]
        build_cur(frames, os.path.join(outdir, fname))
    for role, fname, hs, fn, count, jiff in ANIM_DESIGNS:
        curs = []
        for i in range(count):
            t = i / float(count)
            imgs = [(fn(s, t), int(round(s * hs[0])), int(round(s * hs[1]))) for s in ANI_SIZES]
            curs.append(build_cur_bytes(imgs))
        build_ani(curs, os.path.join(outdir, fname), jiff)
    # preview face used by the app's colour tiles for this colour
    draw_cat(128).save(os.path.join(outdir, "preview.png"))
    print("built", outdir)


# ---- app icon + README imagery -----------------------------------------------

def draw_app_icon(size):
    """A warm 'sticker' badge with the orange cat on it: reads well at 16px."""
    set_palette("Orange")
    S = size * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    m = int(S * 0.04)
    r = int(S * 0.24)
    badge = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(badge).rounded_rectangle([m, m, S - m, S - m], radius=r, fill=(255, 255, 255, 255))
    grad = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grad)
    top, bot = (255, 222, 170), (255, 158, 56)
    for y in range(S):
        t = y / max(1, S - 1)
        c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)) + (255,)
        gd.line([(0, y), (S, y)], fill=c)
    badge_fill = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    badge_fill.paste(grad, (0, 0), badge.split()[3])
    img.alpha_composite(_soft_shadow(badge_fill, S, S * 0.02, alpha=60, dy=int(S * 0.015)))
    img.alpha_composite(badge_fill)
    ImageDraw.Draw(img).rounded_rectangle([m, m, S - m, S - m], radius=r,
                                          outline=(255, 255, 255, 120), width=max(2, S // 64))
    cat = draw_cat(int(size * 0.72)).resize((int(S * 0.72), int(S * 0.72)), LANCZOS)
    placed = _place(cat, S, int(S * 0.15), int(S * 0.16))
    img.alpha_composite(_soft_shadow(placed, S, S * 0.02, alpha=55, dy=int(S * 0.02)))
    img.alpha_composite(placed)
    return img.resize((size, size), LANCZOS)


def make_gallery():
    """A labelled grid of every cursor (orange) for the README."""
    set_palette("Orange")
    items = [
        ("Normal", draw_cat(72)), ("Normal (arrow)", draw_arrow_cat(72)), ("Link", draw_paw(72)),
        ("Text", draw_ibeam(72)), ("Busy", draw_busy(72)), ("Working", draw_working(72)),
        ("Help", draw_help(72)), ("Unavailable", draw_no(72)), ("Resize N-S", draw_resize(72, 90)),
        ("Resize W-E", draw_resize(72, 0)), ("Resize NW-SE", draw_resize(72, 45)),
        ("Resize NE-SW", draw_resize(72, -45)), ("Move", draw_move(72)),
        ("Precision", draw_cross(72)), ("Pen", draw_pen(72)), ("Up", draw_up(72)),
    ]
    cols, cw, ch = 4, 170, 128
    rows = (len(items) + cols - 1) // cols
    bg = (252, 248, 242, 255)
    sheet = Image.new("RGBA", (cols * cw, rows * ch), bg)
    d = ImageDraw.Draw(sheet)
    f = _font(15, bold=False)
    for i, (label, im) in enumerate(items):
        r, c = divmod(i, cols)
        x, y = c * cw, r * ch
        d.rounded_rectangle([x + 10, y + 8, x + cw - 10, y + ch - 8], radius=14, fill=(255, 255, 255, 255),
                            outline=(236, 228, 216, 255), width=1)
        sheet.paste(im, (x + (cw - 72) // 2, y + 16), im)
        d.text((x + cw / 2, y + 106), label, fill=(70, 55, 40, 255), font=f, anchor="mm")
    os.makedirs("docs", exist_ok=True)
    sheet.convert("RGB").save(os.path.join("docs", "gallery.png"))
    print("built docs/gallery.png")


def make_colors_preview():
    cell = 124
    sheet = Image.new("RGBA", (cell * len(COLORS), cell + 26), (252, 248, 242, 255))
    d = ImageDraw.Draw(sheet)
    f = _font(15, bold=False)
    for i, c in enumerate(COLORS):
        set_palette(c)
        thumb = draw_cat(100)
        sheet.paste(thumb, (i * cell + 12, 10), thumb)
        d.text((i * cell + cell / 2, cell + 10), c, fill=(70, 55, 40, 255), font=f, anchor="mm")
    sheet.convert("RGB").save("colors_preview.png")
    print("built colors_preview.png")


def main():
    for c in COLORS:
        gen_color(c)

    set_palette("Orange")
    draw_cat(128).save("cat_preview_128.png")
    icon = draw_app_icon(256)
    icon.save(os.path.join("build", "icon.ico"),
              sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48),
                     (64, 64), (128, 128), (256, 256)])
    icon.save(os.path.join("docs", "icon.png"))
    print("built build/icon.ico")

    make_colors_preview()
    make_gallery()


if __name__ == "__main__":
    main()
