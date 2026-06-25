"""
Generates cat-themed Windows cursors in several colours, including animated
(.ani) busy/loading pointers.

Output: build/<Colour>/ containing one cursor per pointer role:
  - 13 static  .cur  (arrow, paw, resize, move, ...)
  - 2 animated .ani  (busy = sleeping cat with z-z-z, working = spinner)
"""

import os
import math
import struct
from PIL import Image, ImageDraw, ImageFont

LANCZOS = Image.LANCZOS

# ---- palette (the four values below are swapped per colour) ----------------
FUR = FUR_DARK = INNER_EAR = OUTLINE = None

PALETTES = {
    "Orange":  dict(fur=(255, 170, 65),  dark=(225, 130, 30), inner=(255, 205, 170), outline=(70, 45, 20)),
    "Black":   dict(fur=(64, 66, 76),    dark=(40, 42, 50),   inner=(150, 120, 130), outline=(212, 214, 222)),
    "Grey":    dict(fur=(158, 163, 172), dark=(120, 125, 135),inner=(220, 200, 205), outline=(58, 60, 68)),
    "White":   dict(fur=(245, 246, 250), dark=(210, 212, 220),inner=(255, 210, 210), outline=(95, 97, 108)),
    "Siamese": dict(fur=(236, 222, 198), dark=(120, 90, 70),  inner=(210, 180, 160), outline=(95, 72, 55)),
}
COLORS = ["Orange", "Black", "Grey", "White", "Siamese"]

# ---- constant colours ------------------------------------------------------
WHITE    = (255, 255, 255, 255)
BLACK    = (30, 25, 25, 255)
PINK     = (255, 120, 150, 255)
PAD      = (255, 120, 150, 255)
RED      = (220, 45, 45, 255)
YELLOW   = (255, 205, 70, 255)
GRAPHITE = (60, 60, 70, 255)
WOOD     = (245, 220, 170, 255)
SPINNER  = (0, 120, 215, 255)

SS = 4
SIZES = [32, 48, 64, 96]
ANI_SIZES = [32, 48]
FONT_DIR = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")


def set_palette(name):
    global FUR, FUR_DARK, INNER_EAR, OUTLINE
    p = PALETTES[name]
    FUR = p["fur"] + (255,)
    FUR_DARK = p["dark"] + (255,)
    INNER_EAR = p["inner"] + (255,)
    OUTLINE = p["outline"] + (255,)


def _font(px):
    for name in ("arialbd.ttf", "segoeui.ttf", "arial.ttf"):
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


# ---- shared drawing helpers (supersampled pixel space) ---------------------

def _beam(d, p1, p2, w, ow):
    d.line([p1, p2], fill=OUTLINE, width=int(w + 2 * ow))
    d.line([p1, p2], fill=WHITE, width=int(max(1, w)))


def _arrowhead(d, tip, ang, sz, ow):
    bx = tip[0] - sz * math.cos(ang)
    by = tip[1] - sz * math.sin(ang)
    px = math.cos(ang + math.pi / 2)
    py = math.sin(ang + math.pi / 2)
    w = sz * 0.85
    d.polygon([tip, (bx + px * w, by + py * w), (bx - px * w, by - py * w)],
              fill=WHITE, outline=OUTLINE, width=ow)


def _cat_hub(d, cx, cy, r, ow):
    d.polygon([(cx - r * 0.85, cy - r * 0.4), (cx - r * 0.35, cy - r * 1.25),
               (cx - r * 0.05, cy - r * 0.45)], fill=FUR, outline=OUTLINE, width=ow)
    d.polygon([(cx + r * 0.85, cy - r * 0.4), (cx + r * 0.35, cy - r * 1.25),
               (cx + r * 0.05, cy - r * 0.45)], fill=FUR, outline=OUTLINE, width=ow)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=FUR, outline=OUTLINE, width=ow)
    er = r * 0.16
    for ex in (cx - r * 0.42, cx + r * 0.42):
        d.ellipse([ex - er, cy - er * 0.6, ex + er, cy + er * 1.4], fill=BLACK)
    d.polygon([(cx - r * 0.13, cy + r * 0.22), (cx + r * 0.13, cy + r * 0.22),
               (cx, cy + r * 0.45)], fill=PINK)


# ---- the designs -----------------------------------------------------------

def draw_cat(size):
    img, d, S = _canvas(size)
    ow = _ow(S)

    def px(x, y):
        return (x * S, y * S)

    for ear in ([px(0.04, 0.02), px(0.40, 0.16), px(0.10, 0.42)],
                [px(0.78, 0.10), px(0.96, 0.40), px(0.55, 0.30)]):
        d.polygon(ear, fill=FUR, outline=OUTLINE, width=ow)
    d.polygon([px(0.10, 0.07), px(0.34, 0.17), px(0.15, 0.34)], fill=INNER_EAR)
    d.polygon([px(0.80, 0.16), px(0.90, 0.37), px(0.62, 0.30)], fill=INNER_EAR)
    d.ellipse([px(0.10, 0.18)[0], px(0.10, 0.18)[1], px(0.92, 0.96)[0], px(0.92, 0.96)[1]],
              fill=FUR, outline=OUTLINE, width=ow)
    d.line([px(0.50, 0.22), px(0.50, 0.40)], fill=FUR_DARK, width=ow * 2)
    d.line([px(0.40, 0.24), px(0.42, 0.40)], fill=FUR_DARK, width=ow * 2)
    d.line([px(0.60, 0.24), px(0.58, 0.40)], fill=FUR_DARK, width=ow * 2)
    for cx in (0.36, 0.66):
        d.ellipse([px(cx - 0.08, 0.45)[0], px(cx - 0.08, 0.45)[1],
                   px(cx + 0.08, 0.65)[0], px(cx + 0.08, 0.65)[1]],
                  fill=WHITE, outline=OUTLINE, width=ow)
        d.ellipse([px(cx - 0.035, 0.50)[0], px(cx - 0.035, 0.50)[1],
                   px(cx + 0.035, 0.62)[0], px(cx + 0.035, 0.62)[1]], fill=BLACK)
        d.ellipse([px(cx - 0.01, 0.51)[0], px(cx - 0.01, 0.51)[1],
                   px(cx + 0.02, 0.55)[0], px(cx + 0.02, 0.55)[1]], fill=WHITE)
    d.polygon([px(0.46, 0.68), px(0.56, 0.68), px(0.51, 0.75)],
              fill=PINK, outline=OUTLINE, width=max(1, ow // 2))
    d.line([px(0.51, 0.75), px(0.51, 0.80)], fill=OUTLINE, width=ow)
    d.arc([px(0.40, 0.74)[0], px(0.40, 0.74)[1], px(0.51, 0.84)[0], px(0.51, 0.84)[1]],
          start=20, end=160, fill=OUTLINE, width=ow)
    d.arc([px(0.51, 0.74)[0], px(0.51, 0.74)[1], px(0.62, 0.84)[0], px(0.62, 0.84)[1]],
          start=20, end=160, fill=OUTLINE, width=ow)
    for y in (0.70, 0.76):
        d.line([px(0.30, y), px(0.02, y - 0.03)], fill=OUTLINE, width=max(1, ow // 2))
        d.line([px(0.72, y), px(1.0, y - 0.03)], fill=OUTLINE, width=max(1, ow // 2))
    return _fin(img, size)


def draw_paw(size):
    img, d, S = _canvas(size)
    ow = _ow(S)

    def px(x, y):
        return (x * S, y * S)

    toes = [(0.24, 0.30, 0.085), (0.42, 0.20, 0.095),
            (0.60, 0.20, 0.095), (0.78, 0.32, 0.085)]
    for cx, cy, r in toes:
        d.ellipse([px(cx - r, cy - r)[0], px(cx - r, cy - r)[1],
                   px(cx + r, cy + r)[0], px(cx + r, cy + r)[1]],
                  fill=FUR, outline=OUTLINE, width=ow)
    d.ellipse([px(0.18, 0.42)[0], px(0.18, 0.42)[1], px(0.82, 0.96)[0], px(0.82, 0.96)[1]],
              fill=FUR, outline=OUTLINE, width=ow)
    for cx, cy, r in toes:
        pr = r * 0.6
        d.ellipse([px(cx - pr, cy - pr * 0.7)[0], px(cx - pr, cy - pr * 0.7)[1],
                   px(cx + pr, cy + pr * 1.1)[0], px(cx + pr, cy + pr * 1.1)[1]], fill=PAD)
    d.ellipse([px(0.30, 0.54)[0], px(0.30, 0.54)[1], px(0.70, 0.90)[0], px(0.70, 0.90)[1]], fill=PAD)
    return _fin(img, size)


def draw_resize(size, angle_deg):
    img, d, S = _canvas(size)
    ow = _ow(S)
    a = math.radians(angle_deg)
    cx = cy = S / 2
    L, head = S * 0.42, S * 0.16
    dx, dy = math.cos(a), math.sin(a)
    _beam(d, (cx - dx * (L - head), cy - dy * (L - head)),
          (cx + dx * (L - head), cy + dy * (L - head)), S * 0.06, ow)
    _arrowhead(d, (cx + dx * L, cy + dy * L), a, head, ow)
    _arrowhead(d, (cx - dx * L, cy - dy * L), a + math.pi, head, ow)
    _cat_hub(d, cx, cy, S * 0.15, ow)
    return _fin(img, size)


def draw_move(size):
    img, d, S = _canvas(size)
    ow = _ow(S)
    cx = cy = S / 2
    L, head = S * 0.42, S * 0.15
    for a in (0, math.pi / 2, math.pi, 3 * math.pi / 2):
        dx, dy = math.cos(a), math.sin(a)
        _beam(d, (cx, cy), (cx + dx * (L - head), cy + dy * (L - head)), S * 0.055, ow)
    for a in (0, math.pi / 2, math.pi, 3 * math.pi / 2):
        dx, dy = math.cos(a), math.sin(a)
        _arrowhead(d, (cx + dx * L, cy + dy * L), a, head, ow)
    _cat_hub(d, cx, cy, S * 0.14, ow)
    return _fin(img, size)


def draw_cross(size):
    img, d, S = _canvas(size)
    ow = _ow(S)
    cx = cy = S / 2
    gap, end, w = S * 0.12, S * 0.46, S * 0.035
    _beam(d, (cx, cy - end), (cx, cy - gap), w, ow)
    _beam(d, (cx, cy + gap), (cx, cy + end), w, ow)
    _beam(d, (cx - end, cy), (cx - gap, cy), w, ow)
    _beam(d, (cx + gap, cy), (cx + end, cy), w, ow)
    _cat_hub(d, S * 0.17, S * 0.17, S * 0.10, max(1, ow // 2))
    return _fin(img, size)


def draw_ibeam(size):
    img, d, S = _canvas(size)
    ow = _ow(S)
    cx = S / 2
    top, bot, w, sw = S * 0.20, S * 0.80, S * 0.05, S * 0.10
    _beam(d, (cx, top), (cx, bot), w, ow)
    _beam(d, (cx - sw, top), (cx + sw, top), w * 0.7, ow)
    _beam(d, (cx - sw, bot), (cx + sw, bot), w * 0.7, ow)
    d.polygon([(cx - sw, top - S * 0.01), (cx - sw * 0.5, top - S * 0.15),
               (cx - sw * 0.05, top - S * 0.01)], fill=FUR, outline=OUTLINE, width=max(1, ow // 2))
    d.polygon([(cx + sw, top - S * 0.01), (cx + sw * 0.5, top - S * 0.15),
               (cx + sw * 0.05, top - S * 0.01)], fill=FUR, outline=OUTLINE, width=max(1, ow // 2))
    return _fin(img, size)


def _sleeping_cat(d, S, ow):
    d.ellipse([S * 0.16, S * 0.46, S * 0.88, S * 0.86], fill=FUR, outline=OUTLINE, width=ow)
    d.arc([S * 0.58, S * 0.42, S * 0.97, S * 0.9], start=-40, end=210, fill=OUTLINE, width=int(ow * 2.2))
    hx, hy, hr = S * 0.34, S * 0.56, S * 0.16
    d.polygon([(hx - hr * 0.9, hy - hr * 0.4), (hx - hr * 0.4, hy - hr * 1.3),
               (hx - hr * 0.1, hy - hr * 0.5)], fill=FUR, outline=OUTLINE, width=ow)
    d.polygon([(hx + hr * 0.9, hy - hr * 0.4), (hx + hr * 0.4, hy - hr * 1.3),
               (hx + hr * 0.1, hy - hr * 0.5)], fill=FUR, outline=OUTLINE, width=ow)
    d.ellipse([hx - hr, hy - hr, hx + hr, hy + hr], fill=FUR, outline=OUTLINE, width=ow)
    d.arc([hx - hr * 0.55, hy - hr * 0.1, hx + hr * 0.05, hy + hr * 0.4],
          start=0, end=180, fill=OUTLINE, width=ow)
    d.polygon([(hx - hr * 0.95, hy + hr * 0.15), (hx - hr * 1.5, hy + hr * 0.0),
               (hx - hr * 0.95, hy + hr * 0.35)], fill=OUTLINE)


def _zzz(d, S, t):
    bx, by = S * 0.52, S * 0.40
    for k in range(3):
        ph = ((t + k / 3.0) % 1.0)
        x, y = bx + ph * S * 0.34, by - ph * S * 0.34
        a = int(225 * (1 - ph))
        fs = max(6, int(S * (0.10 + 0.12 * ph)))
        d.text((x, y), "z", font=_font(fs), fill=(OUTLINE[0], OUTLINE[1], OUTLINE[2], a), anchor="mm")


def draw_busy(size):                       # static fallback frame
    img, d, S = _canvas(size)
    ow = _ow(S)
    _sleeping_cat(d, S, ow)
    _zzz(d, S, 0.5)
    return _fin(img, size)


def draw_busy_frame(size, t):
    img, d, S = _canvas(size)
    ow = _ow(S)
    _sleeping_cat(d, S, ow)
    _zzz(d, S, t)
    return _fin(img, size)


def draw_no(size):
    img, d, S = _canvas(size)
    ow = _ow(S)
    cx = cy = S / 2
    _cat_hub(d, cx, cy, S * 0.20, ow)
    R, rw = S * 0.40, int(S * 0.09)
    d.ellipse([cx - R, cy - R, cx + R, cy + R], outline=RED, width=rw)
    a = math.radians(45)
    d.line([(cx - math.cos(a) * R, cy - math.sin(a) * R),
            (cx + math.cos(a) * R, cy + math.sin(a) * R)], fill=RED, width=rw)
    return _fin(img, size)


def draw_up(size):
    img, d, S = _canvas(size)
    ow = _ow(S)
    cx = S / 2
    _beam(d, (cx, S * 0.30), (cx, S * 0.86), S * 0.06, ow)
    _arrowhead(d, (cx, S * 0.10), -math.pi / 2, S * 0.18, ow)
    _cat_hub(d, cx, S * 0.74, S * 0.11, ow)
    return _fin(img, size)


def draw_pen(size):
    img, d, S = _canvas(size)
    ow = _ow(S)
    start, end = (S * 0.24, S * 0.24), (S * 0.76, S * 0.76)
    d.line([start, end], fill=OUTLINE, width=int(S * 0.18 + 2 * ow))
    d.line([start, end], fill=YELLOW, width=int(S * 0.18))
    d.polygon([(S * 0.22, S * 0.22), (S * 0.34, S * 0.20), (S * 0.20, S * 0.34)], fill=WOOD)
    d.polygon([(S * 0.10, S * 0.10), (S * 0.27, S * 0.19), (S * 0.19, S * 0.27)],
              fill=GRAPHITE, outline=OUTLINE, width=ow)
    er = S * 0.11
    d.ellipse([end[0] - er, end[1] - er, end[0] + er, end[1] + er], fill=PINK, outline=OUTLINE, width=ow)
    d.polygon([(end[0] - er * 0.6, end[1] - er * 0.5), (end[0] - er * 0.1, end[1] - er * 1.3),
               (end[0] + er * 0.2, end[1] - er * 0.4)], fill=FUR, outline=OUTLINE, width=max(1, ow // 2))
    return _fin(img, size)


def _badge_cat(size, draw_badge):
    base = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    cat = draw_cat(max(8, int(size * 0.78)))
    base.paste(cat, (0, 0), cat)
    d = ImageDraw.Draw(base)
    b0, bd = int(size * 0.46), size - 1
    ow = max(1, size // 28)
    d.ellipse([b0, b0, bd, bd], fill=WHITE, outline=(60, 50, 40, 255), width=ow)
    draw_badge(d, b0, bd, size)
    return base


def draw_help(size):
    def badge(d, b0, bd, size):
        cx = cy = (b0 + bd) / 2
        d.text((cx, cy), "?", font=_font(int((bd - b0) * 0.7)), fill=RED, anchor="mm")
    return _badge_cat(size, badge)


def draw_working_frame(size, t):
    def badge(d, b0, bd, size):
        m = int((bd - b0) * 0.16)
        st = t * 360.0
        d.arc([b0 + m, b0 + m, bd - m, bd - m], start=st, end=st + 270,
              fill=SPINNER, width=max(2, size // 22))
    return _badge_cat(size, badge)


def draw_working(size):                    # static fallback frame
    return draw_working_frame(size, 0.0)


# ---- .cur / .ani packing ---------------------------------------------------

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


# ---- role table ------------------------------------------------------------
# (registry role, output file, hotspot fraction, draw fn) - static cursors
STATIC_DESIGNS = [
    ("Arrow",    "cat_cursor.cur", (0.04, 0.02), draw_cat),
    ("Hand",     "cat_paw.cur",    (0.50, 0.14), draw_paw),
    ("Help",     "cat_help.cur",   (0.03, 0.02), draw_help),
    ("IBeam",    "cat_text.cur",   (0.50, 0.50), draw_ibeam),
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
    ("AppStarting", "cat_working.ani", (0.03, 0.02), draw_working_frame, 12, 5),
]

# full role -> file order, used by the app/scripts
ROLE_FILES = (
    [(r, f) for r, f, _, _ in STATIC_DESIGNS] +
    [("Wait", "cat_busy.ani"), ("AppStarting", "cat_working.ani")]
)


def gen_color(name):
    set_palette(name)
    outdir = os.path.join("build", name)
    os.makedirs(outdir, exist_ok=True)
    for role, fname, hs, fn in STATIC_DESIGNS:
        frames = [(fn(s), int(s * hs[0]), int(s * hs[1])) for s in SIZES]
        build_cur(frames, os.path.join(outdir, fname))
    for role, fname, hs, fn, count, jiff in ANIM_DESIGNS:
        curs = []
        for i in range(count):
            t = i / float(count)
            imgs = [(fn(s, t), int(s * hs[0]), int(s * hs[1])) for s in ANI_SIZES]
            curs.append(build_cur_bytes(imgs))
        build_ani(curs, os.path.join(outdir, fname), jiff)
    print("built", outdir)


def main():
    for c in COLORS:
        gen_color(c)

    # GUI logo preview (orange face)
    set_palette("Orange")
    draw_cat(128).save("cat_preview_128.png")

    # colour preview montage
    cell = 130
    sheet = Image.new("RGBA", (cell * len(COLORS), cell + 20), (255, 255, 255, 255))
    d = ImageDraw.Draw(sheet)
    f = _font(15)
    for i, c in enumerate(COLORS):
        set_palette(c)
        thumb = draw_cat(104)
        sheet.paste(thumb, (i * cell + 13, 8), thumb)
        d.text((i * cell + cell / 2, cell + 6), c, fill=(20, 20, 20, 255), font=f, anchor="mm")
    sheet.save("colors_preview.png")
    print("built colors_preview.png")


if __name__ == "__main__":
    main()
