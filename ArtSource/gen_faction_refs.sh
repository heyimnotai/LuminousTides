#!/bin/bash
# Image-to-3D reference images for faction units, one character per image, drawn from the faction's concept sheet.
# Usage: gen_faction_refs.sh <faction>      (varrik)
CX="/c/Program Files/WindowsApps/OpenAI.Codex_26.1106.6628.0_x64__2p2nqsd0c76g0/app/resources/codex.exe"
cd "$(dirname "$0")/refs"
COMMON="Keep the species, armour, materials and colours of the attached faction sheet exactly. Full body, front three-quarter view, standing upright and symmetrical in a neutral A-pose: both arms straight and held away from the body at 45 degrees, legs straight and shoulder-width apart with a clear gap between them, feet flat. Any cloth is short: a mantle over the shoulders and upper back only, ending above the waist, never hanging between or in front of the legs or over the arms. Single character only, fully visible head to toe, centred, plain flat light-grey background, soft even studio lighting, no cast shadow, no text, no labels. This image will be used for image-to-3D conversion."
gen () { # name sheet prompt
  [ -f "$1.png" ] && { echo "skip $1"; return; }
  "$CX" exec --skip-git-repo-check -s workspace-write "Use your built-in image generation tool to generate one image: $3 $COMMON Save the resulting PNG into the current directory as $1.png. Reply with only the saved file path." -i "../concepts/$2.png" < /dev/null > "$1.log" 2>&1
  [ -f "$1.png" ] && echo "ok $1" || echo "FAIL $1"
}
if [ "$1" = "varrik" ]; then
  V="A Varrik raider from the attached sheet: a tall gaunt two-armed alien with digitigrade legs, a long neck, a bone-white rebreather mask with two slanted eye slits and short backswept horns, in worn charcoal leather and bone-white plates with deep teal cloth."
  gen varrik_thrall varrik "$V The lowest rank, the first figure on the sheet: small, hunched and thin with only scraps of armour and bare forearms, both hands empty with long clawed fingers spread." &
  gen varrik_rifleman varrik "$V The second figure on the sheet, a rifleman: medium build, chest plate and a short teal shoulder mantle. It holds a long slim rifle in its right hand only, gripped at the middle, the rifle pointing down and outward away from its leg. Left hand empty and open." &
  gen varrik_captain varrik "$V The third figure on the sheet, a captain: tall, with ornate bone-white pauldrons, a fuller chest plate and a short teal mantle. It holds a straight glowing electric-blue energy sabre in its right hand, the blade pointing down and outward away from its leg. Left hand empty and open." &
  gen varrik_chieftain varrik "$V The fourth figure on the sheet, the chieftain: towering and broad, in heavy layered bone-white plate with a horned crown-helm and a short fur collar over the shoulders. It holds a massive spear with a glowing electric-blue blade upright in its right hand, the butt resting on the ground beside its foot, held well away from its body. Left hand empty and open." &
  gen varrik_wraith varrik "$V A specialist not on the sheet, an assassin: lean and medium height, in tight dark wrappings with very little plate, a deep teal hood drawn over its horns, and a short energy blade glowing electric-blue held in EACH hand, both blades pointing down and outward." &
  wait
fi
if [ "$1" = "varrik2" ]; then   # final roster (concepts/varrik5.png): units not covered by the first batch
  gen varrik_captain2 varrik5 "The CAPTAIN from the attached roster, the fourth figure from the left on the ground: a tall, broad-shouldered, heavily built Varrik noble with a horned bone-white helm-mask, heavy ornate bone-white pauldrons and chest plate, charcoal leather and deep teal cloth. In its right hand a curved energy sabre glowing electric-blue, blade pointing down and outward away from the leg. On its left forearm a round energy buckler glowing electric-blue, held out to the side. Its teal cape hangs straight down its back only, clear of the arms and legs." &
  CX2="$CX"
  [ -f varrik_rifle.png ] && echo "skip varrik_rifle" || { "$CX2" exec --skip-git-repo-check -s workspace-write "Use your built-in image generation tool to generate one image: the long slim rifle carried by the RAIDER, the second figure in the attached roster, on its own with no hands: a long-barrelled alien rifle of dark gunmetal and weathered bone-white plates with a teal cloth wrap around the stock and a glowing electric-blue energy cell along the barrel. Exact side view, barrel pointing right, horizontal. Single object only, fully visible, centred, plain flat light-grey background, soft even studio lighting, no cast shadow, no text. This image will be used for image-to-3D conversion. Save the resulting PNG into the current directory as varrik_rifle.png. Reply with only the saved file path." -i ../concepts/varrik5.png < /dev/null > varrik_rifle.log 2>&1; [ -f varrik_rifle.png ] && echo "ok varrik_rifle" || echo "FAIL varrik_rifle"; } &
  wait
fi
if [ "$1" = "varrik3" ]; then   # the rest of the roster: beast, flyer, walker and boss
  PLAIN="Keep the materials and colours of the attached faction roster exactly: bone-white plates and masks, charcoal leather and dark metal, deep teal cloth, electric-blue glow. Single subject only, fully visible, centred, plain flat light-grey background, soft even studio lighting, no cast shadow, no text, no labels. No loose hanging chains, charms, ropes or banners. This image will be used for image-to-3D conversion."
  one () { # name prompt
    [ -f "$1.png" ] && { echo "skip $1"; return; }
    "$CX" exec --skip-git-repo-check -s workspace-write "Use your built-in image generation tool to generate one image: $2 $PLAIN Save the resulting PNG into the current directory as $1.png. Reply with only the saved file path." -i ../concepts/varrik5.png < /dev/null > "$1.log" 2>&1
    [ -f "$1.png" ] && echo "ok $1" || echo "FAIL $1"
  }
  one varrik_hound "The HOUND from the attached roster: a lean, hairless, sinewy four-legged hunting beast wearing a bone-white skull mask over its head and a teal cloth and leather harness with bone-white plates along its back. Standing square and still on all four straight legs, legs well apart with clear gaps between them, head level and facing forward, long thin tail held straight out behind. Side view turned slightly towards the camera, facing left." &
  one varrik_kite "The KITE from the attached roster: a flying scout drone. A wide, thin, bat-like wing of stretched teal sailcloth on a bone-white rib frame, a small central body pod with a single round glowing electric-blue lamp eye at the front, and a short gun barrel slung beneath the body. Wings spread flat and level. Viewed from the front and slightly above so the whole wingspan and the top of the wings are visible." &
  one varrik_strider "The STRIDER from the attached roster: a towering three-legged walking war machine. Three very long, thick, jointed mechanical legs of dark metal with bone-white armour plates, spread evenly like a tripod and all planted on the ground, carrying a compact armoured pod of bone-white plate with a curved crest over the top, and one large long-barrelled cannon with glowing electric-blue coils pointing forward from under the pod. No pilot visible, the pod is closed. Three-quarter front view, the whole machine in frame from feet to crest." &
  one varrik_queen "The CORSAIR QUEEN from the attached roster, the last figure on the right: a very tall, regal, slender Varrik matriarch with an ornate bone-white crown-mask with four tall horns, bone-white shoulder plates and breastplate, and a long layered deep teal robe falling straight to the ground in a narrow column, with no cloth trailing behind. Full body, front view, standing upright and symmetrical in an A-pose: both arms straight and held away from the body at 45 degrees, both hands empty and open, no weapon, no drones." &
  wait
fi
echo ALLDONE
