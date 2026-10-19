"""Survey: every subject once, small, into a labelled 8x8 contact sheet (media/_source/survey/sheet.png).

    python media/_source/survey.py            # writes survey/shots.json, renders, builds the sheet
"""
import json
import os
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'survey')
os.makedirs(OUT, exist_ok=True)

units = ['STK', 'TKA', 'GEN', 'TRB', 'CLG', 'SIL', 'CND']
systems = ['All', 'SteamWater', 'Cooling', 'FuelFlue', 'Electrical', 'Alarms']
shots = []
for theme in ('dark', 'light'):
    for view in ('iso', 'elevation', 'plan'):
        for system in systems:
            shots.append({'id': f'{theme}-{view}-{system}', 'theme': theme, 'view': view, 'system': system,
                          'size': [320, 180], 'scale': 1, 'padding': 8, 'alerting': ['CLG']})
    for unit in units:
        shots.append({'id': f'{theme}-unit-{unit}', 'theme': theme, 'unit': unit, 'size': [180, 180], 'scale': 1, 'padding': 10})
for system in ('All', 'FuelFlue', 'Electrical', 'Cooling', 'SteamWater', 'Alarms'):
    shots.append({'id': f'dark-exposure-{system}', 'theme': 'dark', 'system': system, 'size': [320, 180], 'scale': 1,
                  'padding': 8, 'alerting': ['CLG'], 'exposure': 45})

path = os.path.join(OUT, 'shots.json')
json.dump(shots, open(path, 'w'), indent=1)
tool = os.path.join(HERE, 'capture')
subprocess.run(['dotnet', 'run', '-c', 'Release', '--no-build', '--project', tool, '--', path, OUT], check=True)

cell, label = (240, 135), 16
sheet = Image.new('RGB', (8 * cell[0], 8 * (cell[1] + label)), (12, 12, 12))
font = ImageFont.truetype(os.path.join(HERE, '..', '..', 'PacConsole', 'Assets', 'Fonts', 'IBMPlexSans-Regular.ttf'), 11)
d = ImageDraw.Draw(sheet)
for i, s in enumerate(shots[:64]):
    im = Image.open(os.path.join(OUT, s['id'] + '.png')).convert('RGB')
    im.thumbnail(cell)
    x, y = (i % 8) * cell[0], (i // 8) * (cell[1] + label)
    sheet.paste(im, (x + (cell[0] - im.width) // 2, y + (cell[1] - im.height) // 2))
    d.text((x + 4, y + cell[1] + 1), f'{i + 1:02d} {s["id"]}', fill=(200, 200, 200), font=font)
sheet.save(os.path.join(OUT, 'sheet.png'))
print(len(shots), 'shots; sheet', sheet.size)
