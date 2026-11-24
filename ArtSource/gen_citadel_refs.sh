#!/bin/bash
# Reference images (image-to-3D input) for the Sunken Citadel kit: the pieces the demo level is assembled from.
# Usage: gen_citadel_refs.sh
CX="/c/Program Files/WindowsApps/OpenAI.Codex_26.1106.6628.0_x64__2p2nqsd0c76g0/app/resources/codex.exe"
cd "$(dirname "$0")/refs"
STYLE="A single environment prop for a stylised science-fiction action game, in exactly the materials, colours and carving style of the ancient ruins in the attached painting: pale warm weathered sandstone in large fitted blocks, worn edges and cracks, bands of carved alien glyphs and concentric circle motifs, patches of glowing teal moss, and a few orange bracket fungi growing from the stone."
PLAIN="Single object only, standing on its own, fully visible, centred, three-quarter view from slightly above, plain flat light-grey background, soft even studio lighting, no cast shadow, no water, no sky, no ground plane, no characters, no text. Solid and chunky with no thin or floating parts. This image will be used for image-to-3D conversion of a game asset."
one () { # name description
  [ -f "$1.png" ] && { echo "skip $1"; return; }
  "$CX" exec --skip-git-repo-check -s workspace-write "Use your built-in image generation tool to generate one image. $STYLE THE OBJECT: $2 $PLAIN Save the resulting PNG into the current directory as $1.png. Reply with only the saved file path." -i ../concepts/demo_a.png < /dev/null > "$1.log" 2>&1
  [ -f "$1.png" ] && echo "ok $1" || echo "FAIL $1"
}
one citadel_stack "a sea stack: a tall, roughly square pillar of natural rock about three times as tall as it is wide, its top cut perfectly flat and paved with a few large carved flagstones, the sides rough natural strata with remnants of fitted masonry near the top, moss hanging over the rim." &
one citadel_cliff "a section of sea cliff, like a thick wall of rock wider than it is tall: layered natural strata, with the upper third built up in huge fitted masonry blocks and a carved frieze, a level top, moss and fungi in the ledges. Flat on the back, so sections can stand side by side." &
one citadel_colonnade "a ruined colonnade: a straight row of five massive fluted stone columns on a shared stepped plinth, three still carrying a broken carved lintel, two snapped off at different heights." &
one citadel_arch "a monumental freestanding stone gateway arch, thick and heavy, twice as tall as it is wide, with a round-topped opening large enough to walk through, carved glyph bands up both piers and a broken keystone." &
one citadel_head "the head of a colossal fallen statue lying on its side: a smooth, serene, elongated alien face with closed eyes and a tall crest, cracked across the brow, half overgrown with moss." &
one citadel_wall "a ruined wall segment, about three times as long as it is tall: fitted stone blocks with a carved band along the middle, the top edge broken and stepped, one end collapsed into a few fallen blocks." &
wait
# ---- second batch
one citadel_stairs "a broad, straight flight of about fourteen stone steps rising from front to back, three times as wide as a doorway, with a low solid stone balustrade down each side ending in a carved block at the bottom, the steps worn and a few cracked." &
one citadel_bridge "one span of a stone bridge: a long, straight, level walkway of fitted flagstones with a low solid parapet along each side, carried on a single wide round arch underneath, about five times as long as it is wide. Both ends are cut off flat so spans can join end to end." &
one citadel_tower "a ruined round watchtower, four times as tall as it is wide, of fitted stone with narrow window slits and a carved band, its top broken off at an angle, a doorway at the base." &
one citadel_terrace "a tall retaining wall of huge fitted stone blocks in three stepped tiers, each tier set back a little from the one below, with a carved frieze along the top tier and buttresses between, as tall as it is wide. Flat on the back, so sections can stand side by side against a cliff." &
one citadel_arenawall "a straight section of the outer wall of an ancient arena, about twice as long as it is tall: a thick stone wall with three deep round-topped alcoves along it and a heavy carved cornice, the top edge level." &
one citadel_tusk "a single colossal curved stone pylon shaped like a tusk or horn, standing upright on a square carved plinth and curving gently inward to a point, five times as tall as its base is wide, with glyph bands carved up its outer face." &
one citadel_dais "a low circular stone dais, like a wide flat drum about eight times as wide as it is tall, three shallow steps round its edge, its level top carved with concentric rings and a glowing teal inlay circle at the centre." &
one citadel_statue "a colossal standing statue of a tall, slender, robed alien figure with a smooth serene elongated face and a tall crest, hands folded at the chest, on a square plinth, one shoulder cracked away." &
one citadel_rubble "a low heap of fallen masonry: six or seven large squared stone blocks and broken column drums tumbled together, some carved with glyphs, about twice as wide as it is tall." &
one citadel_obelisk "a tall four-sided stone obelisk tapering to a blunt point, on a stepped square base, every face carved with glyph bands, with a band of glowing teal inlay a third of the way up." &
wait
echo ALLDONE
