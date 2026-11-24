"""Draws the HUD sprites (Assets/Resources/HUD) and the muzzle-flash texture. White shapes are tinted in Unity.

Run:  python ArtSource/make_hud.py
"""
import math, os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
HUD = os.path.join(ROOT, "Assets", "Resources", "HUD")
TEX = os.path.join(ROOT, "Assets", "Art", "Textures")
os.makedirs(HUD, exist_ok=True)
SS = 4  # supersampling factor
WHITE = (255, 255, 255, 255)


def canvas(w, h):
    img = Image.new("RGBA", (w * SS, h * SS), (255, 255, 255, 0))
    return img, ImageDraw.Draw(img)


def save(img, name, folder=HUD):
    w, h = img.size
    img = img.resize((w // SS, h // SS), Image.LANCZOS)
    img.save(os.path.join(folder, name + ".png"))
    print("wrote", name)


def P(points):
    return [(x * SS, y * SS) for x, y in points]


def panel(name, w, h, cut_tl, cut_br, cut_tr=0, cut_bl=0):
    """Dark translucent panel with chamfered corners, a thin outline and corner accents."""
    img, d = canvas(w, h)
    pts = [(cut_tl, 0), (w - 1 - cut_tr, 0), (w - 1, cut_tr), (w - 1, h - 1 - cut_br), (w - 1 - cut_br, h - 1),
           (cut_bl, h - 1), (0, h - 1 - cut_bl), (0, cut_tl)]
    d.polygon(P(pts), fill=(8, 14, 30, 120))
    d.line(P(pts + [pts[0]]), fill=(255, 255, 255, 70), width=SS)
    # brighter accents on the chamfers
    for a, b in ((pts[7], pts[0]), (pts[3], pts[4])):
        if a != b:
            d.line(P([a, b]), fill=(255, 255, 255, 200), width=SS * 2)
    save(img, name)


def slot():
    img, d = canvas(96, 96)
    d.rounded_rectangle(P([(2, 2), (93, 93)]), radius=14 * SS, fill=(8, 14, 30, 140), outline=(255, 255, 255, 150), width=SS * 2)
    save(img, "slot")


def disc(name, size, soft):
    n = size * SS
    ys, xs = np.mgrid[0:n, 0:n]
    r = np.sqrt((xs - n / 2 + 0.5) ** 2 + (ys - n / 2 + 0.5) ** 2) / (n / 2)
    a = np.clip((1 - r) / soft, 0, 1) if soft > 0 else (r < 1).astype(float)
    arr = np.zeros((n, n, 4), np.uint8)
    arr[..., :3] = 255
    arr[..., 3] = (a * 255).astype(np.uint8)
    save(Image.fromarray(arr, "RGBA"), name)


def ring():
    img, d = canvas(256, 256)
    d.ellipse(P([(3, 3), (252, 252)]), outline=(255, 255, 255, 230), width=SS * 3)
    for i in range(24):                                      # tick marks around the rim
        a = i / 24 * 2 * math.pi
        r0, r1 = (112, 122) if i % 6 else (106, 122)
        d.line(P([(128 + math.cos(a) * r0, 128 + math.sin(a) * r0), (128 + math.cos(a) * r1, 128 + math.sin(a) * r1)]),
               fill=(255, 255, 255, 200), width=SS * 2)
    save(img, "ring")


def diamond(name, filled):
    img, d = canvas(64, 64)
    pts = P([(32, 4), (60, 32), (32, 60), (4, 32)])
    if filled:
        d.polygon(pts, fill=WHITE)
    else:
        d.line(pts + [pts[0]], fill=WHITE, width=SS * 6, joint="curve")
    save(img, name)


def arrow():
    img, d = canvas(64, 64)
    d.polygon(P([(32, 4), (56, 58), (32, 46), (8, 58)]), fill=WHITE)
    save(img, "arrow")


def triangle():
    img, d = canvas(32, 32)
    d.polygon(P([(16, 4), (30, 28), (2, 28)]), fill=WHITE)
    save(img, "triangle")


def icon_shield():
    img, d = canvas(64, 64)
    pts = P([(32, 5), (55, 13), (55, 32), (32, 59), (9, 32), (9, 13)])
    d.line(pts + [pts[0]], fill=WHITE, width=SS * 5, joint="curve")
    d.polygon(P([(32, 16), (45, 21), (45, 32), (32, 47)]), fill=WHITE)
    save(img, "icon_shield")


def icon_plus():
    img, d = canvas(64, 64)
    d.rectangle(P([(25, 8), (39, 56)]), fill=WHITE)
    d.rectangle(P([(8, 25), (56, 39)]), fill=WHITE)
    save(img, "icon_plus")


def icon_dash():
    img, d = canvas(64, 64)
    for ox in (6, 28):
        d.polygon(P([(ox, 12), (ox + 10, 12), (ox + 30, 32), (ox + 10, 52), (ox, 52), (ox + 20, 32)]), fill=WHITE)
    save(img, "icon_dash")


def icon_burst():
    img, d = canvas(64, 64)
    pts = []
    for i in range(24):
        a = i / 24 * 2 * math.pi - math.pi / 2
        r = 28 if i % 2 == 0 else (11 if i % 4 == 1 else 14)
        pts.append((32 + math.cos(a) * r, 32 + math.sin(a) * r))
    d.polygon(P(pts), fill=WHITE)
    d.ellipse(P([(25, 25), (39, 39)]), fill=(255, 255, 255, 0))
    save(img, "icon_burst")


def icon_grenade():
    img, d = canvas(64, 64)
    d.rounded_rectangle(P([(16, 22), (48, 58)]), radius=8 * SS, outline=WHITE, width=SS * 5)
    d.rectangle(P([(24, 10), (40, 20)]), fill=WHITE)
    d.line(P([(40, 13), (52, 13), (52, 26)]), fill=WHITE, width=SS * 4)
    d.rectangle(P([(24, 34), (40, 46)]), fill=WHITE)
    save(img, "icon_grenade")


def vignette():
    n = 256
    ys, xs = np.mgrid[0:n, 0:n]
    x = (xs / (n - 1) - 0.5) * 2
    y = (ys / (n - 1) - 0.5) * 2
    r = np.sqrt(x ** 2 * 0.9 + y ** 2 * 1.1)
    a = np.clip((r - 0.55) / 0.75, 0, 1) ** 1.6
    arr = np.zeros((n, n, 4), np.uint8)
    arr[..., :3] = 255
    arr[..., 3] = (a * 255).astype(np.uint8)
    Image.fromarray(arr, "RGBA").save(os.path.join(HUD, "vignette.png"))
    print("wrote vignette")


def damage_arc():
    """Arc at the top of the image, centred on the image centre: points at the attacker when rotated."""
    n = 512
    ys, xs = np.mgrid[0:n, 0:n]
    x = xs - n / 2 + 0.5
    y = n / 2 - ys - 0.5
    r = np.sqrt(x ** 2 + y ** 2)
    ang = np.degrees(np.arctan2(x, y))                      # 0 = up
    radial = np.clip(1 - np.abs(r - 215) / 26, 0, 1) ** 1.5
    spread = np.clip(1 - np.abs(ang) / 34, 0, 1) ** 1.2
    arr = np.zeros((n, n, 4), np.uint8)
    arr[..., :3] = 255
    arr[..., 3] = (radial * spread * 255).astype(np.uint8)
    Image.fromarray(arr, "RGBA").save(os.path.join(HUD, "damage_arc.png"))
    print("wrote damage_arc")


def muzzle_flash():
    n = 256
    ys, xs = np.mgrid[0:n, 0:n]
    x = (xs - n / 2 + 0.5) / (n / 2)
    y = (ys - n / 2 + 0.5) / (n / 2)
    r = np.sqrt(x ** 2 + y ** 2)
    ang = np.arctan2(y, x)
    spikes = 0.34 + 0.5 * np.abs(np.cos(ang * 2.5)) ** 7 + 0.16 * np.abs(np.cos(ang * 5 + 0.6)) ** 5
    star = np.clip(1 - r / spikes, 0, 1) ** 1.3
    core = np.clip(1 - r / 0.28, 0, 1) ** 0.7
    glow = np.clip(1 - r, 0, 1) ** 2.5 * 0.45
    a = np.clip(star * 0.9 + core + glow, 0, 1)
    arr = np.zeros((n, n, 4), np.uint8)
    arr[..., :3] = 255
    arr[..., 3] = (a * 255).astype(np.uint8)
    Image.fromarray(arr, "RGBA").filter(ImageFilter.GaussianBlur(1.0)).save(os.path.join(TEX, "muzzle_flash.png"))
    print("wrote muzzle_flash")


def reticle():
    """Sight reticle, drawn crisp at 4x and stored in two channels so the shader can colour them separately:
    red = main reticle (centre dot, thin ring, four ticks), green = accent (side brackets and range ladder)."""
    n = 512
    main, d = Image.new("L", (n * SS, n * SS), 0), None
    d = ImageDraw.Draw(main)
    c = n * SS / 2

    def px(v):          # reticle units (-1..1) to pixels
        return v * n * SS / 2

    lw = int(SS * 2.2)
    r = px(0.20)
    d.ellipse([c - r, c - r, c + r, c + r], outline=255, width=lw)
    dot = px(0.018)
    d.ellipse([c - dot, c - dot, c + dot, c + dot], fill=255)
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):                       # ticks outside the ring
        d.line([c + dx * px(0.24), c + dy * px(0.24), c + dx * px(0.34), c + dy * px(0.34)], fill=255, width=lw)

    accent = Image.new("L", (n * SS, n * SS), 0)
    d = ImageDraw.Draw(accent)
    for sx in (-1, 1):                                                       # angled side brackets
        pts = [(c + sx * px(0.46), c - px(0.30)), (c + sx * px(0.62), c - px(0.18)),
               (c + sx * px(0.62), c + px(0.18)), (c + sx * px(0.46), c + px(0.30))]
        d.line(pts, fill=255, width=lw, joint="curve")
    for k, w in enumerate((0.10, 0.07, 0.045)):                              # range ladder under the ring
        y = c + px(0.44 + k * 0.10)
        d.line([c - px(w), y, c + px(w), y], fill=255, width=lw)

    main = main.resize((n, n), Image.LANCZOS)
    accent = accent.resize((n, n), Image.LANCZOS)
    out = Image.merge("RGB", (main, accent, Image.new("L", (n, n), 0)))
    out.save(os.path.join(TEX, "reticle.png"))
    print("wrote reticle")


if __name__ == "__main__":
    reticle()
    panel("panel_bars", 500, 124, 22, 22)
    panel("panel_ammo", 330, 128, 30, 30)
    panel("panel_objective", 430, 64, 0, 16)
    slot()
    disc("disc", 128, 0)
    disc("glow", 128, 1.0)
    ring()
    diamond("diamond", True)
    diamond("diamond_outline", False)
    arrow()
    triangle()
    icon_shield(); icon_plus(); icon_dash(); icon_burst(); icon_grenade()
    vignette()
    damage_arc()
    muzzle_flash()
