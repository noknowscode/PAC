"""Three art directions, one sample each, for choosing a style before re-doing the set.

    python media/_source/directions.py     ->  media/_source/directions/{A,B,C}.jpg and compare.jpg

A  Swiss poster: strict grid, huge Plex numerals, the unit's real data in columns, the drawing bleeding
   off the sheet and overprinting the type (darken), one alert-orange accent.
B  Orbit exposure: the camera turning 60 degrees through one long exposure; heavy bloom, minimal type.
C  Macro: one assembly filling the frame, a duotone grade in the graphite and accent tones, and a giant
   catalog numeral the drawing lightens over.
Pixels of the plant come only from media/_source/renders (the product's renderer).
"""
import json
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
R = os.path.join(HERE, 'renders')
OUT = os.path.join(HERE, 'directions')
FONTS = os.path.join(REPO, 'PacConsole', 'Assets', 'Fonts')
os.makedirs(OUT, exist_ok=True)

PAPER, INK, MUTED, FAINT = (0xF4, 0xF5, 0xF6), (0x1E, 0x28, 0x33), (0x5A, 0x65, 0x73), (0x8A, 0x93, 0x9E)
GRAPHITE, G_INK, G_MUTED = (0x16, 0x18, 0x1B), (0xE8, 0xEA, 0xEC), (0xA0, 0xA7, 0xAF)
ALERT, ACCENT_D = (0xC2, 0x41, 0x0C), (0x7F, 0xB3, 0xC8)


def F(w, size, mono=False):
    name = f'IBMPlexMono-{w}.ttf' if mono else f'IBMPlexSans-{w}.ttf'
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def meta(i):
    return json.load(open(os.path.join(R, i + '.meta.json'), encoding='utf-8'))


def arr(im):
    return np.asarray(im).astype(np.float32)


def img(a):
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def grain(a, sigma, seed):
    return a + np.random.default_rng(seed).normal(0, sigma, a.shape[:2] + (1,)).astype(np.float32)


def bloom(a, threshold=48, strength=1.0):
    w = a.shape[1]
    lum = a @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    mask = np.clip((lum - threshold) / (255 - threshold), 0, 1)[..., None]
    bright = img(a * mask)
    glow = sum(wt * arr(bright.filter(ImageFilter.GaussianBlur(w * r))) for r, wt in ((0.003, 0.5), (0.012, 0.45), (0.04, 0.35)))
    return a + glow * strength


def swiss():
    m = meta('A-trb')
    W, H, M = 1920, 2400, 110
    sheet = Image.new('RGB', (W, H), PAPER)
    d = ImageDraw.Draw(sheet)
    # Running head: a hairline grid of small caps.
    head = F('Medium', 30)
    for x, t in ((M, 'PACCONSOLE'), (M + 520, 'PLANT 02'), (M + 900, 'STEAM-CYCLE STATION'), (W - M - 160, 'No. 04')):
        d.text((x, M), t, fill=INK, font=head)
    d.line([(M, M + 58), (W - M, M + 58)], fill=INK, width=3)
    # The code, huge: short code in ink, index in the one accent.
    short, index = m['code'][:3], m['code'][4:]
    big = F('SemiBold', 560)
    d.text((M - 30, M + 60), short, fill=INK, font=big)
    d.text((M - 20, M + 520), index, fill=ALERT, font=big)
    # Name and description in the right columns.
    # Right column beside the code, above where the drawing reaches.
    col = 1190
    d.text((col, M + 120), m['name'], fill=INK, font=F('SemiBold', 64))
    body = F('Regular', 30)
    words, line, y = m['body'].split(), '', M + 210
    for word in words:
        trial = (line + ' ' + word).strip()
        if d.textlength(trial, font=body) > W - M - col:
            d.text((col, y), line, fill=MUTED, font=body); y += 42; line = word
        else:
            line = trial
    d.text((col, y), line, fill=MUTED, font=body)
    # Facts: uptime, service, connections.
    y += 70
    small, mono = F('Medium', 24), F('Medium', 27, mono=True)
    for label, value in (('UP', m['uptime']), ('SERVICED', m['service']), ('FEEDS', m['connections'])):
        d.text((col, y), label, fill=FAINT, font=small)
        d.text((col + 190, y - 3), value, fill=INK, font=mono)
        y += 44
    # Normal bands: a ruled table, every reading the unit reports.
    y += 30
    d.text((col, y), 'NORMAL OPERATING BANDS', fill=INK, font=small)
    y += 40
    for row in m['readings'].split('\n'):
        label, band = row.split('\t')
        d.line([(col, y), (W - M, y)], fill=FAINT, width=1)
        d.text((col, y + 10), label, fill=MUTED, font=F('Regular', 23))
        bw = d.textlength(band, font=F('Regular', 23, mono=True))
        d.text((W - M - bw, y + 10), band, fill=INK, font=F('Regular', 23, mono=True))
        y += 44
    d.line([(col, y), (W - M, y)], fill=FAINT, width=1)
    # The drawing bleeds off the bottom and left, overprinting the numerals (darken: ink shows through paper).
    a = arr(sheet)
    art = arr(Image.open(os.path.join(R, 'A-trb.png')).convert('RGB'))
    ah, aw = art.shape[:2]
    x0, y0 = -90, H - ah + 60
    ys, xs = slice(max(0, y0), min(H, y0 + ah)), slice(max(0, x0), min(W, x0 + aw))
    sub = art[ys.start - y0:ys.stop - y0, xs.start - x0:xs.stop - x0]
    a[ys, xs] = np.minimum(a[ys, xs], sub)
    a = grain(a, 2.2, 4)
    out = img(a)
    d = ImageDraw.Draw(out)
    d.text((W - M - 70, H - M - 10), 'PAC', fill=INK, font=F('SemiBold', 34))
    return out


def orbit():
    a = arr(Image.open(os.path.join(R, 'B-orbit.png')).convert('RGB'))
    a = bloom(a, threshold=90, strength=0.45)
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.sqrt(((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2) / np.sqrt(2)
    a = a * (1 - 0.45 * np.clip(r, 0, 1) ** 1.8)[..., None]
    out = img(grain(a, 3.0, 2))
    d = ImageDraw.Draw(out)
    M = 96
    d.text((M, M), 'ORBIT', fill=G_INK, font=F('Medium', 30))
    d.text((M, M + 44), 'Plant 02, isometric. Ten frames of the renderer in one exposure, the camera turning 50° between the first and the last.',
           fill=G_MUTED, font=F('Regular', 26))
    d.text((w - M - 64, h - M - 30), 'PAC', fill=G_INK, font=F('SemiBold', 30))
    return out


def macro():
    m = meta('C-clg')
    a = arr(Image.open(os.path.join(R, 'C-clg.png')).convert('RGB'))
    h, w = a.shape[:2]
    # Duotone: graphite shadow to the dark theme's accent, through the luminance of the render.
    lum = (a @ np.array([0.2126, 0.7152, 0.0722], np.float32)) / 255
    lo, hi = np.array((0x0E, 0x10, 0x12), np.float32), np.array(ACCENT_D, np.float32)
    t = np.clip(lum, 0, 1) ** 0.9
    duo = lo + (hi - lo) * t[..., None] + np.clip(lum - 0.75, 0, 1)[..., None] * 255 * 0.6
    duo = bloom(duo, strength=0.8)
    # A giant catalog numeral behind the drawing: the drawing lightens over it.
    back = Image.new('RGB', (w, h), (0x0E, 0x10, 0x12))
    db = ImageDraw.Draw(back)
    db.text((-60, h * 0.30), m['code'][4:], fill=(0x2C, 0x35, 0x3E), font=F('SemiBold', 1500))
    a = np.maximum(arr(back), duo)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.sqrt(((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2) / np.sqrt(2)
    a = a * (1 - 0.4 * np.clip(r, 0, 1) ** 1.6)[..., None]
    out = img(grain(a, 3.0, 5))
    d = ImageDraw.Draw(out)
    M = 100
    d.text((M, h - M - 150), m['code'], fill=ACCENT_D, font=F('Medium', 34))
    d.text((M, h - M - 104), m['name'], fill=G_INK, font=F('SemiBold', 60))
    d.text((M, h - M - 26), 'Five fans on duty, one on standby; Cooling isolated.', fill=G_MUTED, font=F('Regular', 28))
    d.text((w - M - 66, h - M - 26), 'PAC', fill=G_INK, font=F('SemiBold', 32))
    return out


if __name__ == '__main__':
    pieces = {'A': swiss(), 'B': orbit(), 'C': macro()}
    for k, im in pieces.items():
        im.save(os.path.join(OUT, k + '.jpg'), 'JPEG', quality=95, subsampling=0)
    # Comparison: the three side by side at feed size.
    thumbs = []
    for k in 'ABC':
        t = pieces[k].copy()
        t.thumbnail((900, 900), Image.LANCZOS)
        thumbs.append(t)
    W = sum(t.width for t in thumbs) + 40 * 4
    sheet = Image.new('RGB', (W, 900 + 80), (38, 38, 38))
    x = 40
    for t in thumbs:
        sheet.paste(t, (x, 40 + (900 - t.height) // 2))
        x += t.width + 40
    sheet.save(os.path.join(OUT, 'compare.jpg'), 'JPEG', quality=92)
    print('ok')
