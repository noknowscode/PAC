"""Finishing pass for the PacConsole art set: renders -> media/art (captioned), media/art/clean, media/art/poster.jpg.

    python media/_source/finish.py [id ...]     # all pieces, or only the named ones

Every pixel of a piece comes from media/_source/renders (the product's SolidRenderer, driven by capture/).
This script only grades (bloom on bright pixels, vignette, grain), composes the systems grid from six
real renders, and sets the caption. Captions use the words the capture tool wrote from the product's
catalogs (renders/<id>.meta.json). Type is IBM Plex Sans from PacConsole/Assets/Fonts, the app's own face.
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
RENDERS = os.path.join(HERE, 'renders')
ART = os.path.join(REPO, 'media', 'art')
CLEAN = os.path.join(ART, 'clean')
FONTS = os.path.join(REPO, 'PacConsole', 'Assets', 'Fonts')
os.makedirs(CLEAN, exist_ok=True)

# Theme tokens (Styles/Tokens.xaml, Styles/Palette.Dark.xaml).
THEME = {
    'dark': {'ground': (0x16, 0x18, 0x1B), 'ink': (0xE8, 0xEA, 0xEC), 'muted': (0xA0, 0xA7, 0xAF), 'accent': (0x7F, 0xB3, 0xC8)},
    'light': {'ground': (0xF4, 0xF5, 0xF6), 'ink': (0x1E, 0x28, 0x33), 'muted': (0x5A, 0x65, 0x73), 'accent': (0x2F, 0x5D, 0x73)},
}

PIECES = ['01-plant-graphite', '02-flue-gas', '03-steam-water', '04-cooling', '05-electrical', '06-alarm',
          '07-plan-graphite', '08-plant-paper', '09-elevation-paper', '10-plan-paper', '11-stk', '12-trb',
          '13-clg', '14-sil', '15-systems']
# Full-bleed pieces whose caption sits on drawing: always set on a scrim.
BUSY = {'02-flue-gas', '05-electrical', '06-alarm'}
GRID = ['grid-All', 'grid-SteamWater', 'grid-Cooling', 'grid-FuelFlue', 'grid-Electrical', 'grid-Alarms']


def font(weight, size):
    return ImageFont.truetype(os.path.join(FONTS, f'IBMPlexSans-{weight}.ttf'), size)


def meta(id_):
    m = json.load(open(os.path.join(RENDERS, id_ + '.meta.json'), encoding='utf-8'))
    m['id'] = id_
    return m


def load(id_):
    return np.asarray(Image.open(os.path.join(RENDERS, id_ + '.png')).convert('RGB')).astype(np.float32)


def systems_grid(theme):
    """The six system isolations, 3 x 2, on the ground: arranged, not redrawn. Each tile carries its system's title."""
    t = THEME[theme]
    tiles = [Image.open(os.path.join(RENDERS, g + '.png')).convert('RGB') for g in GRID]
    W, H = 2560, 1440
    gap, top, bottom = 24, 70, 260
    tw = (W - 4 * gap) // 3
    th = int(tw * tiles[0].height / tiles[0].width)
    canvas = Image.new('RGB', (W, H), t['ground'])
    d = ImageDraw.Draw(canvas)
    label = font('Medium', 34)
    for i, (g, tile) in enumerate(zip(GRID, tiles)):
        x = gap + (i % 3) * (tw + gap)
        y = top + (i // 3) * (th + gap + 44)
        canvas.paste(tile.resize((tw, th), Image.LANCZOS), (x, y + 44))
        d.text((x + 6, y + 6), meta(g)['title'], fill=t['muted'], font=label)
    return np.asarray(canvas).astype(np.float32)


def grade(img, theme, seed):
    h, w, _ = img.shape
    if theme == 'dark':
        # Bloom on bright pixels only: nothing under the threshold contributes, so the ground stays black.
        lum = img @ np.array([0.2126, 0.7152, 0.0722], np.float32)
        mask = np.clip((lum - 48) / (255 - 48), 0, 1)[..., None]
        bright = Image.fromarray(np.clip(img * mask, 0, 255).astype(np.uint8))
        glow = np.zeros_like(img)
        for radius, weight in ((w * 0.004, 0.55), (w * 0.016, 0.35)):
            glow += weight * np.asarray(bright.filter(ImageFilter.GaussianBlur(radius))).astype(np.float32)
        img = img + glow * 0.9
    # Vignette: a soft radial falloff, stronger on graphite than on paper.
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.sqrt(((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2) / np.sqrt(2)
    strength = 0.30 if theme == 'dark' else 0.07
    img = img * (1 - strength * np.clip(r, 0, 1) ** 2.2)[..., None]
    # Grain: monochrome, seeded per piece so a re-run gives the same file.
    rng = np.random.default_rng(seed)
    img = img + rng.normal(0, 2.4 if theme == 'dark' else 1.8, (h, w, 1)).astype(np.float32)
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))


def wrap(text, f, width, draw):
    lines, line = [], ''
    for word in text.split():
        trial = (line + ' ' + word).strip()
        if draw.textlength(trial, font=f) <= width or not line:
            line = trial
        else:
            lines.append(line)
            line = word
    return lines + [line]


def caption(im, number, m, theme):
    t = THEME[theme]
    w, h = im.size
    # Type scales with the image's area, so a 16:9 piece and a square read the same on a feed.
    s = int((w * h) ** 0.5)
    margin = int(min(w, h) * 0.055)
    f_num, f_title, f_body, f_mark = font('Medium', int(s * 0.020)), font('Medium', int(s * 0.030)), font('Regular', int(s * 0.020)), font('SemiBold', int(s * 0.024))
    d = ImageDraw.Draw(im)
    body = wrap(m['body'], f_body, int(w * (0.58 if w > h else 0.80)), d)
    line_h = int(s * 0.031)
    block = int(s * 0.034) + int(s * 0.044) + line_h * len(body)
    top = h - margin - block

    # Scrim, only where the caption would sit on busy pixels: a soft ramp of the ground colour.
    region = np.asarray(im.crop((0, max(0, top - margin), w, h)).convert('L')).astype(np.float32)
    if region.std() > 12 or m.get('id') in BUSY:
        ramp_h = h - top + 2 * margin
        alpha = np.clip(np.linspace(0, 1, ramp_h) * 1.6, 0, 1) ** 1.4 * 0.86
        scrim = np.asarray(im).astype(np.float32)
        g = np.array(t['ground'], np.float32)
        y0 = h - ramp_h
        scrim[y0:] = scrim[y0:] * (1 - alpha[:, None, None]) + g * alpha[:, None, None]
        im.paste(Image.fromarray(scrim.astype(np.uint8)))
        d = ImageDraw.Draw(im)

    y = top
    d.text((margin, y), f'No. {number:02d}', fill=t['accent'], font=f_num)
    y += int(s * 0.034)
    d.text((margin, y), m['title'], fill=t['ink'], font=f_title)
    y += int(s * 0.044)
    for line in body:
        d.text((margin, y), line, fill=t['muted'], font=f_body)
        y += line_h
    mark = 'PAC'
    mw = d.textlength(mark, font=f_mark)
    d.text((w - margin - mw, h - margin - int(s * 0.030)), mark, fill=t['ink'], font=f_mark)
    return im


def save(im, path):
    im.save(path, 'JPEG', quality=95, subsampling=0, optimize=True)


def finish(id_, number):
    m = meta(id_)
    theme = m['theme']
    img = systems_grid(theme) if id_ == '15-systems' else load(id_)
    graded = grade(img, theme, seed=number)
    save(graded, os.path.join(CLEAN, id_ + '.jpg'))
    save(caption(graded.copy(), number, m, theme), os.path.join(ART, id_ + '.jpg'))
    print('finished', id_, graded.size)


def poster():
    """A 4 x 4 overview: a title card and the fifteen pieces, each fitted on its own ground like a matted print."""
    cell, gap = 600, 20
    W = 4 * cell + 5 * gap
    sheet = Image.new('RGB', (W, W), THEME['dark']['ground'])
    d = ImageDraw.Draw(sheet)
    x0 = y0 = gap
    d.text((x0 + 36, y0 + 60), 'PacConsole', fill=THEME['dark']['ink'], font=font('SemiBold', 54))
    one = wrap('A power plant console built with Uno Platform. Every image here comes straight from its plant renderer.', font('Regular', 24), cell - 72, d)
    for i, line in enumerate(one):
        d.text((x0 + 36, y0 + 150 + i * 36), line, fill=THEME['dark']['muted'], font=font('Regular', 24))
    d.text((x0 + 36, y0 + cell - 80), 'Plant 02 · Steam-cycle station', fill=THEME['dark']['accent'], font=font('Medium', 22))
    for i, id_ in enumerate(PIECES, start=1):
        im = Image.open(os.path.join(CLEAN, id_ + '.jpg'))
        im.thumbnail((cell - 40, cell - 40), Image.LANCZOS)
        # Matted in the average of the piece's own border, so its vignetted edge meets the mat without a seam.
        px = np.asarray(im).astype(np.float32)
        ring = np.concatenate([px[:4].reshape(-1, 3), px[-4:].reshape(-1, 3), px[:, :4].reshape(-1, 3), px[:, -4:].reshape(-1, 3)])
        box = Image.new('RGB', (cell, cell), tuple(int(v) for v in ring.mean(axis=0)))
        box.paste(im, ((cell - im.width) // 2, (cell - im.height) // 2))
        x = gap + (i % 4) * (cell + gap)
        y = gap + (i // 4) * (cell + gap)
        sheet.paste(box, (x, y))
    save(sheet, os.path.join(ART, 'poster.jpg'))
    print('poster', sheet.size)


if __name__ == '__main__':
    wanted = sys.argv[1:] or PIECES
    for n, id_ in enumerate(PIECES, start=1):
        if id_ in wanted:
            finish(id_, n)
    poster()
