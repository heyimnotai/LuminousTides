#!/bin/bash
# Concept paintings for the demo level: the same establishing view in three different art directions.
# Usage: gen_demo_concepts.sh
CX="/c/Program Files/WindowsApps/OpenAI.Codex_26.1106.6628.0_x64__2p2nqsd0c76g0/app/resources/codex.exe"
cd "$(dirname "$0")/concepts"
VIEW="Wide cinematic establishing shot, 16:9, seen from a high cliff ledge at the start of a level in a stylised science-fiction action game, looking out across a bay. In the foreground the ledge drops away to a line of tall sea stacks stepping down toward the water, a route a player would jump along. Across the bay, in the middle distance, a large circular arena platform stands on a spit of rock, and above it hangs a long, narrow alien raiding ship with swept sail-fins and glowing blue engines. To the left the coast curves round through cliffs and a broken ridge to reach the arena, with a colossal stone ring gate standing on the heights. In the sky a huge ringed gas giant, two small moons and faint aurora. Strong sense of scale, depth and atmosphere, layered haze between near, middle and far, clear readable silhouettes, one dominant light direction. Epic space-fantasy concept art, painterly but detailed, professional environment painting for a big-budget game. No characters, no text, no user interface, no borders."
one () { # name direction
  [ -f "$1.png" ] && { echo "skip $1"; return; }
  "$CX" exec --skip-git-repo-check -s workspace-write "Use your built-in image generation tool to generate one landscape image in 16:9. $VIEW ART DIRECTION: $2 Save the resulting PNG into the current directory as $1.png. Reply with only the saved file path." < /dev/null > "$1.log" 2>&1
  [ -f "$1.png" ] && echo "ok $1" || echo "FAIL $1"
}
one demo_a "THE SUNKEN CITADEL. The coast is the ruin of an ancient alien civilisation: colossal carved stone architecture built into the cliffs, great stairways, broken colonnades, toppled statues and arches of pale weathered stone, half reclaimed by glowing teal moss and giant orange bracket fungi. Late golden hour: warm low sunlight raking across the stone, long shadows, turquoise water, a soft gold and rose sky. Majestic, melancholy, sunlit." &
one demo_b "THE CORSAIR STRONGHOLD. The coast is black volcanic basalt, columnar and jagged, and alien raiders have fortified it: bone-white armour plating and dark iron scaffolds bolted to the rock, watchtowers, gantries and chain bridges, tattered teal sail-banners, braziers and electric-blue lamps, cranes and docked skiffs. Stormy overcast with breaks in the cloud: shafts of pale light striking the sea, spray, mist, dramatic and moody, cold steel-blue palette with teal and blue accents." &
one demo_c "THE LUMINOUS SHORE. A bioluminescent alien coast at deep twilight: indigo and violet rock, glowing cyan tide pools and veins of light in the stone, forests of giant orange and magenta fungi with waterfalls pouring from their caps, tall crystal spires that glow from within, and slender pale temples with glowing doorways set among them. The sky is a deep star-filled blue with vivid green and violet aurora. Dreamlike, saturated, luminous, every surface lit by its own glow." &
wait
echo ALLDONE
