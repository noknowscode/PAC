#!/bin/sh
# Renders every PAC banner: 1x (1200 x 630) and 2x (2400 x 1260) from the same composition, and the two heroes (1920 x 1080).
# Run from src/ with NODE_PATH reaching playwright:  NODE_PATH=$(npm root -g) sh tools/render-all.sh
set -e
OUT=../finals
B="showcase-desk showcase-phones showcase-daynight focus-plant focus-alarm focus-sbo focus-trends stylized-split stylized-systems stylized-views focus-plant-titled"
args=""
for n in $B; do args="$args $n.html $OUT/$n@1x.png 1200 630 1 $n.html $OUT/$n@2x.png 1200 630 2"; done
args="$args focus-plant-hero.html $OUT/focus-plant-hero.png 1920 1080 1 stylized-systems-hero.html $OUT/stylized-systems-hero.png 1920 1080 1"
node tools/render.js $args
