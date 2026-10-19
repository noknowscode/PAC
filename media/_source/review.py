"""Review sheet: the finished pieces as small thumbnails, the way a feed shows them (media/_source/review.png)."""
import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.join(HERE, '..', 'art')
files = sorted(f for f in os.listdir(ART) if f.endswith('.jpg') and f != 'poster.jpg')
cell = 480
cols = 4
rows = (len(files) + cols - 1) // cols
sheet = Image.new('RGB', (cols * cell, rows * cell), (40, 40, 40))
for i, f in enumerate(files):
    im = Image.open(os.path.join(ART, f))
    im.thumbnail((cell - 8, cell - 8), Image.LANCZOS)
    sheet.paste(im, ((i % cols) * cell + (cell - im.width) // 2, (i // cols) * cell + (cell - im.height) // 2))
sheet.save(os.path.join(HERE, 'review.png'))
print(len(files), 'pieces')
