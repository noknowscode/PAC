# media/ inventory

What `media/` holds (93 MB tracked, 204 files, measured 2026-11-04), so the owner can decide what moves with the sample.
Nothing here ships with the app: no project file, code or XAML references `media/`.

"Referenced by" lists who points at the folder: the root `README.md`, the docs (`SPEC*.md`, `HANDOFF.md`, `docs/`), the
app (`PacConsole*/`, `.github/`), and the art set's own `media/_source/README.md`.

| Folder | Size | Files | What it is | Referenced by |
|---|---|---|---|---|
| `art/` (top level) | 17 MB | 10 | The app prints (`app-01` … `app-08`, captioned), `poster.jpg` (the launch sheet) and `readme-hero.jpg` | README (`readme-hero.jpg`, and "launch prints" in `media/art/`); `_source/README.md` |
| `art/clean/` | 13 MB | 8 | The app prints without type | `_source/README.md` |
| `art/plant/` (top level) | 15 MB | 15 | The plant prints, captioned (`01-plant-graphite` … `15-systems`) | `_source/README.md` |
| `art/plant/clean/` | 15 MB | 15 | The plant prints without type | `_source/README.md` |
| `_source/` (loose files) | 76 KB | 9 | The art scripts and their inputs: `ui_art2.py` (app prints), `finish.py` (grade, caption, poster), `survey.py`, `review.py`, `shots.json` (the plant shots), `directions.py` and `directions.json`, `ui_art.py`, and `README.md` | `_source/README.md` documents `ui_art2.py`, `finish.py`, `survey.py`, `review.py` and `shots.json`; `ui_art.py` (an earlier version of `ui_art2.py`), `directions.py` and `directions.json` are referenced by nothing |
| `_source/capture/` | 36 KB | 6 | `PacArt`, the console tool that draws the plant shots offscreen with the product's renderer (builds against the current renderer, A pass 2) | README ("`media/_source/README.md` explains how they were made"); SPEC.md (Decisions, A pass 2); `_source/README.md` |
| `_source/capture-app/` | 16 KB | 2 | `CaptureRunner.cs.txt` and its README: the capture switch for the app prints, added to a scratch copy of the app only | `_source/README.md` |
| `_source/directions/` | 4.7 MB | 4 | Three art-direction trials (`A.jpg`, `B.jpg`, `C.jpg`) and `compare.jpg`, written by `directions.py` | Nothing |
| `_source/renders/` | 13 MB | 48 | The offscreen plant renders (`<id>.png` and `<id>.meta.json` captions) that `finish.py` turns into `art/plant/` | `_source/README.md` |
| `_source/review/` | 8.1 MB | 10 | Review sheets: the pieces as feed-size thumbnails and poster checks | `_source/README.md` |
| `_source/survey/` | 2.7 MB | 58 | The contact sheet of every subject and its frames (`survey.py`) | `_source/README.md` |
| `_source/ui/` | 6.3 MB | 19 | The app captures (`RenderTargetBitmap` of named elements) that `ui_art2.py` composes into `art/` | `_source/README.md`, `_source/capture-app/README.md` |

Not referenced by any document: `_source/directions/`, `_source/directions.py`, `_source/directions.json` and
`_source/ui_art.py`. They were kept in the pre-transfer cleanup, since the owner has not decided what transfers.
