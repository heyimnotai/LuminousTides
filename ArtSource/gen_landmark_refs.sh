#!/bin/bash
# Reference images for the world's large landmarks (image-to-3D input).
CX="/c/Program Files/WindowsApps/OpenAI.Codex_26.1106.6628.0_x64__2p2nqsd0c76g0/app/resources/codex.exe"
cd "$(dirname "$0")/refs"
PLAIN="Single object only, fully visible, centred, plain flat light-grey background, soft even studio lighting, no cast shadow, no text, no labels, no figures or people. No loose hanging chains, ropes, cables or banners. This image will be used for image-to-3D conversion."
one () { # name sheet prompt
  [ -f "$1.png" ] && { echo "skip $1"; return; }
  "$CX" exec --skip-git-repo-check -s workspace-write "Use your built-in image generation tool to generate one image: $3 $PLAIN Save the resulting PNG into the current directory as $1.png. Reply with only the saved file path." -i "$2" < /dev/null > "$1.log" 2>&1
  [ -f "$1.png" ] && echo "ok $1" || echo "FAIL $1"
}
one varrik_ship ../concepts/varrik5.png "A landed raiding ship belonging to the faction on the attached sheet, in exactly their materials and colours: bone-white armour plates over dark charcoal metal, deep teal sailcloth, electric-blue glowing engines. A long, low, narrow alien corsair ketch about forty metres long, resting level on the ground on four short thick landing legs, with a pointed ram prow, a raised armoured bridge at the stern, two short swept fins of teal sailcloth on bone-white ribs along its back, a row of glowing blue engine vents at the rear, and an open boarding ramp lowered to the ground at its side. Solid and chunky, with no thin antennas. Three-quarter view from the front and slightly above, the whole ship in frame." &
one stormgate ../concepts/tideborn5.png "A colossal ancient alien monument, weathered and half reclaimed by nature, in the colour world of the attached sheet: a huge upright ring gate carved from dark blue-grey stone, like a thick stone hoop standing on its edge on a broad stepped stone base, the ring broken open at the top by a missing wedge. Its inner face is inlaid with bands of softly glowing cyan runes, and a few large purple bracket fungi grow from its sides. Massive, simple and solid, far taller than a house. Front view turned slightly to one side, the whole monument in frame from base to top." &
wait
echo ALLDONE
