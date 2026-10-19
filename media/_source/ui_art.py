"""The app as prints: real captures of the running console (media/_source/ui, from the scratch copy's
--capture run) composed on the app's own grounds, with captions from the app's own copy.

    python media/_source/ui_art.py      ->  media/art/*.jpg, media/art/clean/*.jpg, media/art/poster.jpg

Nothing is redrawn: every UI pixel is a RenderTargetBitmap of the live app at 2x or 3x. This script only
places each capture on a card (the app's CardBrush) with a soft shadow, on a ground (FillBrush on paper,
the graphite ground on dark), and sets a caption in IBM Plex Sans.
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
UI = os.path.join(HERE, 'ui')
ART = os.path.join(REPO, 'media', 'art')
CLEAN = os.path.join(ART, 'clean')
FONTS = os.path.join(REPO, 'PacConsole', 'Assets', 'Fonts')
os.makedirs(CLEAN, exist_ok=True)

# Tokens (Styles/Tokens.xaml, Styles/Palette.Dark.xaml).
LIGHT = {'ground': (0xE9, 0xEC, 0xEF), 'card': (0xFF, 0xFF, 0xFF), 'ink': (0x1E, 0x28, 0x33), 'muted': (0x5A, 0x65, 0x73),
         'accent': (0x2F, 0x5D, 0x73), 'alert': (0xC2, 0x41, 0x0C), 'shadow': (0x1E, 0x28, 0x33)}
DARK = {'ground': (0x0F, 0x11, 0x13), 'card': (0x16, 0x18, 0x1B), 'ink': (0xE8, 0xEA, 0xEC), 'muted': (0xA0, 0xA7, 0xAF),
        'accent': (0x7F, 0xB3, 0xC8), 'alert': (0xF0, 0x79, 0x3A), 'shadow': (0, 0, 0)}


def F(weight, size):
    return ImageFont.truetype(os.path.join(FONTS, f'IBMPlexSans-{weight}.ttf'), size)


def cap(name, crop=None, card=None):
    """A capture laid on its card colour (sections without a background are transparent)."""
    im = Image.open(os.path.join(UI, name + '.png')).convert('RGBA')
    if crop:
        im = im.crop(crop)
    if card is None:
        return im
    base = Image.new('RGBA', im.size, card + (255,))
    base.alpha_composite(im)
    return base


def place(sheet, im, xy, t, radius=28, shadow=0.16):
    """A card with a soft shadow: the shadow is a blur of the card's own outline."""
    x, y = xy
    mask = Image.new('L', im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, im.width - 1, im.height - 1], radius=radius, fill=255)
    pad = int(max(im.size) * 0.06)
    sh = Image.new('L', (im.width + 2 * pad, im.height + 2 * pad), 0)
    sh.paste(mask, (pad, pad))
    sh = sh.filter(ImageFilter.GaussianBlur(pad / 2.4))
    layer = Image.new('RGBA', sh.size, t['shadow'] + (0,))
    layer.putalpha(sh.point(lambda v: int(v * shadow)))
    sheet.alpha_composite(layer, (x - pad, y - pad + int(pad * 0.35)))
    rounded = im.copy()
    rounded.putalpha(Image.composite(im.split()[-1], Image.new('L', im.size, 0), mask))
    sheet.alpha_composite(rounded, (x, y))


def fit(im, w, h):
    s = min(w / im.width, h / im.height)
    return im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))), Image.LANCZOS)


def finish(sheet, t, seed):
    a = np.asarray(sheet.convert('RGB')).astype(np.float32)
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.sqrt(((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2) / np.sqrt(2)
    a *= (1 - (0.22 if t is DARK else 0.06) * np.clip(r, 0, 1) ** 2)[..., None]
    a += np.random.default_rng(seed).normal(0, 1.6, (h, w, 1)).astype(np.float32)
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def caption(im, t, number, kicker, title, body, at='bottom'):
    d = ImageDraw.Draw(im)
    w, h = im.size
    s = int((w * h) ** 0.5)
    m = int(min(w, h) * 0.06)
    fk, ft, fb = F('Medium', int(s * 0.0135)), F('SemiBold', int(s * 0.030)), F('Regular', int(s * 0.0145))
    lines, line = [], ''
    for word in body.split():
        trial = (line + ' ' + word).strip()
        if d.textlength(trial, font=fb) > w * 0.46 and line:
            lines.append(line); line = word
        else:
            line = trial
    lines.append(line)
    block = int(s * 0.026) + int(s * 0.044) + int(s * 0.024) * len(lines)
    y = m if at == 'top' else h - m - block
    d.text((m, y), f'No. {number:02d}  ·  {kicker.upper()}', fill=t['accent'], font=fk)
    y += int(s * 0.026)
    d.text((m, y), title, fill=t['ink'], font=ft)
    y += int(s * 0.044)
    for l in lines:
        d.text((m, y), l, fill=t['muted'], font=fb)
        y += int(s * 0.024)
    mark = 'PAC Control Console'
    fm = F('Medium', int(s * 0.0135))
    d.text((w - m - d.textlength(mark, font=fm), (m if at == 'top' else h - m - int(s * 0.016))), mark, fill=t['ink'], font=fm)
    return im


def save(name, clean, captioned):
    clean.save(os.path.join(CLEAN, name + '.jpg'), 'JPEG', quality=95, subsampling=0, optimize=True)
    captioned.save(os.path.join(ART, name + '.jpg'), 'JPEG', quality=95, subsampling=0, optimize=True)
    print('wrote', name, clean.size)


def piece(name, number, t, size, build, kicker, title, body, at='bottom'):
    sheet = Image.new('RGBA', size, t['ground'] + (255,))
    build(sheet)
    clean = finish(sheet, t, number)
    save(name, clean, caption(clean.copy(), t, number, kicker, title, body, at))


# ---------------------------------------------------------------- the pieces

W169, H169 = 3840, 2160
W45, H45 = 2400, 3000


def console(sheet, name, crop_h=1960):
    im = cap(name, (0, 0, 3320, crop_h))
    im = fit(im, int(W169 * 0.80), int(H169 * 0.70))
    place(sheet, im, ((W169 - im.width) // 2, int(H169 * 0.07)), LIGHT if 'graphite' not in name else DARK, radius=22)


def story(sheet):
    """Alarm to resolution: four real states, left to right, each card numbered by the step."""
    steps = [('alarm-list', LIGHT['card']), ('alarm-list-acked', LIGHT['card']), ('pending-start-cell6', None), ('log-resolved', LIGHT['card'])]
    labels = ['CLG-05 alarms on fan vibration', 'The operator acknowledges it',
              'Starts standby cell 6: select before operate', 'Vibration falls; the alarm returns to normal']
    gap, top = 70, 330
    cw = (W169 - 2 * 230 - 3 * gap) // 4
    d = ImageDraw.Draw(sheet)
    for i, ((name, card), label) in enumerate(zip(steps, labels)):
        im = cap(name, card=card)
        if name == 'log-resolved':
            im = im.crop((0, 0, min(im.width, 2200), im.height))
        im = fit(im, cw, int(H169 * 0.36))
        x = 230 + i * (cw + gap)
        d.text((x, top - 120), f'{i + 1}', fill=LIGHT['alert'] if i == 0 else LIGHT['accent'], font=F('SemiBold', 88))
        d.text((x + 80, top - 92), label, fill=LIGHT['ink'], font=F('Medium', 34))
        place(sheet, im, (x, top + 10), LIGHT, radius=20)


def single(sheet, name, t, card, box=(0.70, 0.62), top=0.10, crop=None, size=(W45, H45)):
    im = cap(name, crop, card)
    im = fit(im, int(size[0] * box[0]), int(size[1] * box[1]))
    place(sheet, im, ((size[0] - im.width) // 2, int(size[1] * top)), t, radius=24)


PIECES = [
    ('ui-01-console', 1, LIGHT, (W169, H169), lambda s: console(s, 'console-alarms'),
     'The console', 'PAC Control Console', 'Plant 02 · Steam-cycle station. CLG-05 in alarm; the plant isolates it while the inspector opens its alarm list and response.'),
    ('ui-02-console-graphite', 2, DARK, (W169, H169), lambda s: console(s, 'console-graphite', 2120),
     'Graphite', 'The same console after dark', 'The resolved shift in the graphite theme: alarm cleared, cells 3 and 6 swapped, every step in the log.'),
    ('ui-03-alarm-to-resolution', 3, LIGHT, (W169, H169), story,
     'Operations', 'Alarm to resolution', 'Four real states of one shift, in order. Every action is selected, then confirmed, then logged with time and operator.', 'bottom'),
    ('ui-04-select-before-operate', 4, LIGHT, (W45, H45), lambda s: single(s, 'pending-start-cell6', LIGHT, None, (0.78, 0.30), 0.30),
     'Control', 'Select before operate', 'Every command is selected first and confirmed within 10 s, or it lapses.'),
    ('ui-05-alarm-response', 5, LIGHT, (W45, H45), lambda s: single(s, 'alarm-response', LIGHT, None, (0.74, 0.62), 0.10),
     'Alarm response', 'CLG-05 cooling fan vibration high', "Likely cause, the steps to take and what happens if not: the response arrives with the alarm."),
    ('ui-06-plant-alarm', 6, LIGHT, (W169, H169), lambda s: single(s, 'plant-alarms', LIGHT, (0xF4, 0xF5, 0xF6), (0.80, 0.70), 0.07, size=(W169, H169)),
     'Active alarms', 'The plant shows where', 'Active alarms isolates the equipment in alarm and lets it breathe red; the thumbnails keep the other angles in view.'),
    ('ui-07-trends', 7, LIGHT, (W169, H169), lambda s: single(s, 'trends-resolved', LIGHT, LIGHT['card'], (0.84, 0.60), 0.12, size=(W169, H169)),
     'Synchronized trends', 'Generation + selected equipment', 'Shared historical cursor; the alarm sits on the trend where it began and where it cleared.'),
    ('ui-08-faceplate', 8, LIGHT, (W45, H45), lambda s: single(s, 'faceplate-resolved', LIGHT, LIGHT['card'], (0.70, 0.66), 0.08),
     'Faceplate', 'Cooling bank, in hand', 'Plant mode, six cells with their state and vibration, fan speed and valves, all behind select-before-operate.'),
    ('ui-09-operator-log', 9, LIGHT, (W169, H169), lambda s: single(s, 'log-resolved', LIGHT, LIGHT['card'], (0.84, 0.50), 0.16, size=(W169, H169)),
     'Operator log', 'Every step, timed and signed', 'Alarms in and out, actions, acknowledgements and shift notes · Operator 04.'),
    ('ui-10-plant-graphite', 10, DARK, (W169, H169), lambda s: single(s, 'plant-graphite', DARK, DARK['card'], (0.80, 0.70), 0.07, size=(W169, H169)),
     'Graphite', 'The plant panel at night', 'The same drawing, inks and thumbnails in the graphite theme.'),
]


def poster():
    """Overview: the title, then the ten prints in a grid, each matted in its own ground."""
    W, H = 3600, 4500
    sheet = Image.new('RGB', (W, H), LIGHT['ground'])
    d = ImageDraw.Draw(sheet)
    d.text((180, 170), 'PAC Control Console', fill=LIGHT['ink'], font=F('SemiBold', 150))
    d.text((186, 360), 'An operator console for a steam-cycle power plant, built with Uno Platform: alarm to resolution on one screen.',
           fill=LIGHT['muted'], font=F('Regular', 54))
    cols, gap, top = 2, 60, 560
    cw = (W - 2 * 180 - gap) // cols
    ch = int(cw * 9 / 16)
    y = top
    order = [p[0] for p in PIECES]
    for i, name in enumerate(order):
        im = Image.open(os.path.join(CLEAN, name + '.jpg'))
        im = fit(im, cw, ch)
        x = 180 + (i % cols) * (cw + gap)
        yy = top + (i // cols) * (ch + gap)
        box = Image.new('RGB', (cw, ch), tuple(int(v) for v in np.asarray(im).reshape(-1, 3)[:200].mean(axis=0)))
        box.paste(im, ((cw - im.width) // 2, (ch - im.height) // 2))
        sheet.paste(box, (x, yy))
        y = yy + ch
    sheet = sheet.crop((0, 0, W, y + 200))
    sheet.save(os.path.join(ART, 'poster.jpg'), 'JPEG', quality=95, subsampling=0, optimize=True)
    print('poster', sheet.size)


if __name__ == '__main__':
    for name, number, t, size, build, kicker, title, body, *rest in PIECES:
        piece(name, number, t, size, build, kicker, title, body, *rest)
    poster()
