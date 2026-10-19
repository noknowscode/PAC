"""The app as launch prints, second pass: real captures at large scale, bleeding off the sheet and layered
over the offscreen plant renders, with big Plex headlines from the app's own copy.

    python media/_source/ui_art2.py   ->  media/art/app-*.jpg, media/art/clean/app-*.jpg, media/art/poster.jpg

UI pixels: RenderTargetBitmap captures of the live app (media/_source/ui). Plant pixels: the product's
renderer offscreen (media/_source/renders). This script places, scales, shadows and sets type; it draws
nothing else.
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
UI, RENDERS = os.path.join(HERE, 'ui'), os.path.join(HERE, 'renders')
ART = os.path.join(REPO, 'media', 'art')
CLEAN = os.path.join(ART, 'clean')
FONTS = os.path.join(REPO, 'PacConsole', 'Assets', 'Fonts')
os.makedirs(CLEAN, exist_ok=True)

LIGHT = {'ground': (0xE9, 0xEC, 0xEF), 'card': (0xFF, 0xFF, 0xFF), 'panel': (0xF4, 0xF5, 0xF6), 'ink': (0x1E, 0x28, 0x33),
         'muted': (0x5A, 0x65, 0x73), 'accent': (0x2F, 0x5D, 0x73), 'alert': (0xC2, 0x41, 0x0C), 'shadow': (0x1E, 0x28, 0x33)}
DARK = {'ground': (0x16, 0x18, 0x1B), 'card': (0x16, 0x18, 0x1B), 'panel': (0x16, 0x18, 0x1B), 'ink': (0xE8, 0xEA, 0xEC),
        'muted': (0xA0, 0xA7, 0xAF), 'accent': (0x7F, 0xB3, 0xC8), 'alert': (0xF0, 0x79, 0x3A), 'shadow': (0, 0, 0)}
W169, H169, W45, H45 = 3840, 2160, 2400, 3000


def F(weight, size):
    return ImageFont.truetype(os.path.join(FONTS, f'IBMPlexSans-{weight}.ttf'), int(size))


def ui(name, card=None, crop=None, scale=1.0, inset=0):
    """A capture, optionally cropped, laid on its card colour (sections without a background are transparent),
    and inset by the inspector's own margin when the capture is a bare panel."""
    im = Image.open(os.path.join(UI, name + '.png')).convert('RGBA')
    if crop:
        im = im.crop(crop)
    if inset:
        framed = Image.new('RGBA', (im.width + 2 * inset, im.height + 2 * inset), (0, 0, 0, 0))
        framed.alpha_composite(im, (inset, inset))
        im = framed
    if card is not None:
        base = Image.new('RGBA', im.size, card + (255,))
        base.alpha_composite(im)
        im = base
    if scale != 1.0:
        im = im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
    return im


def place(sheet, im, x, y, t, radius=30, shadow=0.22, spread=0.05):
    """A card with a soft shadow made from its own outline; anything past the sheet's edge bleeds off."""
    mask = Image.new('L', im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, im.width - 1, im.height - 1], radius=radius, fill=255)
    pad = int(max(240, min(im.size) * spread * 2))
    sh = Image.new('L', (im.width + 2 * pad, im.height + 2 * pad), 0)
    sh.paste(mask, (pad, pad))
    sh = sh.filter(ImageFilter.GaussianBlur(pad / 2.6)).point(lambda v: int(v * shadow))
    layer = Image.new('RGBA', sh.size, t['shadow'] + (0,))
    layer.putalpha(sh)
    sheet.alpha_composite(layer, (x - pad, y - pad + pad // 3))
    card = im.copy()
    card.putalpha(Image.composite(im.split()[-1], Image.new('L', im.size, 0), mask))
    sheet.alpha_composite(card, (x, y))


def backdrop(render, size, focus=(0.5, 0.5), darken=0.55):
    """An offscreen plant render covering the sheet, centred on `focus`, dimmed so cards read over it."""
    im = Image.open(os.path.join(RENDERS, render + '.png')).convert('RGB')
    W, H = size
    s = max(W / im.width, H / im.height) * 1.02
    im = im.resize((int(im.width * s), int(im.height * s)), Image.LANCZOS)
    x = int(min(max(im.width * focus[0] - W / 2, 0), im.width - W))
    y = int(min(max(im.height * focus[1] - H / 2, 0), im.height - H))
    a = np.asarray(im.crop((x, y, x + W, y + H))).astype(np.float32) * darken
    return Image.fromarray(a.astype(np.uint8)).convert('RGBA')


def scrim(size, colour, width):
    """A soft ramp of the ground from the left edge over `width` of the sheet, so type reads over a render."""
    W, H = size
    ramp = np.clip(1.15 - np.linspace(0, 1, W) / width, 0, 1) ** 1.3 * 0.92
    alpha = Image.fromarray((np.tile(ramp, (H, 1)) * 255).astype(np.uint8))
    layer = Image.new('RGBA', size, colour + (0,))
    layer.putalpha(alpha)
    return layer


def headline(sheet, t, number, kicker, title, body, x, y, width, size=1.0):
    d = ImageDraw.Draw(sheet)
    s = min(sheet.size) * size
    d.text((x, y), f'No. {number:02d}  ·  {kicker.upper()}', fill=t['accent'], font=F('Medium', s * 0.022))
    y += int(s * 0.048)
    tf = F('SemiBold', s * 0.072)
    for line in title.split('\n'):
        d.text((x - int(s * 0.004), y), line, fill=t['ink'], font=tf)
        y += int(s * 0.086)
    y += int(s * 0.016)
    bf = F('Regular', s * 0.0235)
    line = ''
    for word in body.split():
        trial = (line + ' ' + word).strip()
        if d.textlength(trial, font=bf) > width and line:
            d.text((x, y), line, fill=t['muted'], font=bf); y += int(s * 0.036); line = word
        else:
            line = trial
    d.text((x, y), line, fill=t['muted'], font=bf)


def wordmark(sheet, t, corner='br'):
    d = ImageDraw.Draw(sheet)
    s = min(sheet.size)
    f = F('SemiBold', s * 0.022)
    text = 'PAC Control Console'
    m = int(s * 0.055)
    x = sheet.width - m - d.textlength(text, font=f) if corner.endswith('r') else m
    y = sheet.height - m - int(s * 0.024) if corner.startswith('b') else m
    d.text((x, y), text, fill=t['ink'], font=f)


def finish(sheet, dark, seed):
    a = np.asarray(sheet.convert('RGB')).astype(np.float32)
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.sqrt(((xx - w / 2) / (w / 2)) ** 2 + ((yy - h / 2) / (h / 2)) ** 2) / np.sqrt(2)
    a *= (1 - (0.30 if dark else 0.07) * np.clip(r, 0, 1) ** 2)[..., None]
    a += np.random.default_rng(seed).normal(0, 2.0 if dark else 1.5, (h, w, 1)).astype(np.float32)
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))


def save(name, clean_sheet, captioned_sheet, dark, seed):
    finish(clean_sheet, dark, seed).save(os.path.join(CLEAN, name + '.jpg'), 'JPEG', quality=95, subsampling=0, optimize=True)
    finish(captioned_sheet, dark, seed).save(os.path.join(ART, name + '.jpg'), 'JPEG', quality=95, subsampling=0, optimize=True)
    print('wrote', name)


def compose(name, number, t, size, art, text, seed, mark='bl'):
    """art(sheet) lays the pictures; text(sheet) sets the type. Clean copies skip text."""
    base = Image.new('RGBA', size, t['ground'] + (255,))
    art(base)
    with_text = base.copy()
    text(with_text)
    wordmark(with_text, t, mark)
    save(name, base, with_text, t is DARK, seed)


# ---------------------------------------------------------------- pieces

def p1():
    """The console, huge, bleeding off right and bottom; the name set large on the paper beside it."""
    def art(s):
        c = ui('console-alarms', crop=(0, 0, 3320, 2300), scale=0.95)
        place(s, c, 1380, 260, LIGHT, radius=34)
    def text(s):
        headline(s, LIGHT, 1, 'The console', 'PAC\nControl\nConsole', 'Plant 02 · Steam-cycle station. The plant, its alarms, the equipment and the trends on one screen.', 190, 300, 960)
    compose('app-01-console', 1, LIGHT, (W169, H169), art, text, 11)


def p2():
    """Alarm to resolution: the breathing CLG-05 behind the real response, confirm bar and log."""
    def art(s):
        s.alpha_composite(backdrop('06-alarm', s.size, focus=(0.58, 0.42), darken=0.62))
        s.alpha_composite(scrim(s.size, DARK['ground'], 0.58))
        place(s, ui('alarm-response', scale=1.05), 1880, 230, DARK, radius=26, shadow=0.55)
        place(s, ui('pending-start-cell6', LIGHT['card'], scale=1.15), 2420, 1150, DARK, radius=22, shadow=0.55)
        place(s, ui('log-resolved', LIGHT['card'], crop=(0, 0, 2380, 858), scale=0.72), 1640, 1580, DARK, radius=22, shadow=0.55)
    def text(s):
        headline(s, DARK, 2, 'Operations', 'Alarm to\nresolution', 'CLG-05 alarms on fan vibration. The operator acknowledges it, starts standby cell 6, stops cell 3, and the vibration falls; the log holds every step.', 190, 300, 1160)
    compose('app-02-alarm-to-resolution', 2, DARK, (W169, H169), art, text, 12)


def p3():
    """Select before operate, portrait: the confirm bar huge across the top, the faceplate bleeding below."""
    def art(s):
        place(s, ui('faceplate-pending', LIGHT['card'], crop=(0, 1070, 702, 2693), scale=2.75, inset=48), 430, 1080, LIGHT, radius=32)
    def text(s):
        headline(s, LIGHT, 3, 'Control', 'Select before\noperate', 'Every command is selected, then confirmed within 10 s, or it lapses.', 170, 190, 1500)
    compose('app-03-select-before-operate', 3, LIGHT, (W45, H45), art, text, 13, mark='tr')


def p4():
    """The trends, wide and bleeding both sides, with the alarm's red marker on the cursor line."""
    def art(s):
        place(s, ui('trends-alarm', LIGHT['card'], scale=1.30), 190, 880, LIGHT, radius=30)
    def text(s):
        headline(s, LIGHT, 4, 'Synchronized trends', 'Generation + selected\nequipment', 'One shared historical cursor across every trend; the alarm sits where it began.', 190, 190, 1700)
    compose('app-04-trends', 4, LIGHT, (W169, H169), art, text, 14)


def p5():
    """The plant panel in Active alarms, large and bleeding right, with the real alarm list floating over it."""
    def art(s):
        place(s, ui('plant-alarms', LIGHT['panel'], scale=1.30), 1000, 330, LIGHT, radius=34)
        place(s, ui('alarm-list', LIGHT['card'], scale=1.18, inset=72), 260, 1240, LIGHT, radius=26, shadow=0.30)
    def text(s):
        headline(s, LIGHT, 5, 'Active alarms', 'The plant\nshows where', 'Active alarms isolates the equipment in alarm and lets it breathe red.', 190, 300, 760)
    compose('app-05-active-alarms', 5, LIGHT, (W169, H169), art, text, 15)


def p6():
    """The resolved console in graphite, over the dark plan render."""
    def art(s):
        s.alpha_composite(backdrop('07-plan-graphite', s.size, darken=0.45))
        s.alpha_composite(scrim(s.size, DARK['ground'], 0.42))
        place(s, ui('console-graphite', crop=(0, 0, 3320, 2300), scale=0.95), 1380, 260, DARK, radius=34, shadow=0.6)
    def text(s):
        headline(s, DARK, 6, 'Graphite', 'After\ndark', 'The resolved shift in the graphite theme: the alarm cleared, cells 3 and 6 swapped, every step in the log.', 190, 300, 960)
    compose('app-06-graphite', 6, DARK, (W169, H169), art, text, 16)


def p7():
    """The alarm response on its own, portrait, over the cooling plumes."""
    def art(s):
        s.alpha_composite(backdrop('04-cooling', s.size, focus=(0.55, 0.35), darken=0.55))
        place(s, ui('alarm-response', scale=1.85), (W45 - int(1053 * 1.85)) // 2, 1150, DARK, radius=30, shadow=0.6)
    def text(s):
        headline(s, DARK, 7, 'Alarm response', 'Every alarm\nexplains itself', 'Likely cause, the steps to take, and what happens if not, from the alarm\'s own guidance.', 170, 190, 1800)
    compose('app-07-alarm-response', 7, DARK, (W45, H45), art, text, 17)


def p8():
    """The operator log, wide, bleeding off right."""
    def art(s):
        place(s, ui('log-resolved', LIGHT['card'], crop=(0, 0, 3600, 858), scale=1.0), 190, 960, LIGHT, radius=30)
    def text(s):
        headline(s, LIGHT, 8, 'Operator log', 'Every step,\ntimed and signed', 'Alarms in and out, actions, acknowledgements and shift notes · Operator 04.', 190, 190, 1500)
    compose('app-08-operator-log', 8, LIGHT, (W169, H169), art, text, 18)


PIECES = [p1, p2, p3, p4, p5, p6, p7, p8]


def poster():
    """A launch sheet: title, one-liner, the eight prints in a grid."""
    # Landscapes in pairs, the two portraits side by side; the captioned prints, since the type is part of each.
    names = ['app-01-console', 'app-02-alarm-to-resolution', 'app-05-active-alarms', 'app-06-graphite',
             'app-04-trends', 'app-08-operator-log', 'app-03-select-before-operate', 'app-07-alarm-response']
    W, M, gap = 3600, 180, 60
    cw = (W - 2 * M - gap) // 2
    tiles = [Image.open(os.path.join(ART, n + '.jpg')) for n in names]
    heights = [int(cw * t.height / t.width) for t in tiles]
    rows = [max(heights[i:i + 2]) for i in range(0, len(tiles), 2)]
    H = 620 + sum(rows) + gap * (len(rows) - 1) + M
    sheet = Image.new('RGB', (W, H), DARK['ground'])
    d = ImageDraw.Draw(sheet)
    d.text((M, 170), 'PAC Control Console', fill=DARK['ink'], font=F('SemiBold', 150))
    d.text((M + 6, 370), 'An operator console for a steam-cycle power plant, built with Uno Platform.', fill=DARK['muted'], font=F('Regular', 58))
    d.text((M + 6, 450), 'Alarm to resolution on one screen.', fill=DARK['accent'], font=F('Regular', 58))
    y = 620
    for r in range(len(rows)):
        for c in range(2):
            i = r * 2 + c
            if i >= len(tiles):
                break
            t = tiles[i].resize((cw, heights[i]), Image.LANCZOS)
            sheet.paste(t, (M + c * (cw + gap), y + (rows[r] - heights[i]) // 2))
        y += rows[r] + gap
    sheet.save(os.path.join(ART, 'poster.jpg'), 'JPEG', quality=95, subsampling=0, optimize=True)
    print('poster', sheet.size)


if __name__ == '__main__':
    for p in PIECES:
        p()
    poster()
