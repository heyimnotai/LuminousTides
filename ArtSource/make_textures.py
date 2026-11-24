"""Generates the procedural / derived textures used by the custom shaders.

  noise.png          tileable RGBA value noise (4 independent fields, uniform histogram)
  water_normal.png   tileable ripple normal map
  caustics.png       tileable caustic lattice
  rock_albedo.png    seamless, contrast-boosted version of the ChatGPT rock tile
  rock_normal.png    normal map derived from it
  grass_base/_emit   blade gradient for the hand-made grass
  <model>_emit.png   emission masks extracted from the cyan glow areas of Meshy base-colour textures

Run:  python ArtSource/make_textures.py
"""
import os
import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
TEX = os.path.join(ROOT, "Assets", "Art", "Textures")
os.makedirs(TEX, exist_ok=True)
rng = np.random.default_rng(7)


def spectral_noise(n, beta, lo=1.0, hi=1e9):
    """Tileable noise with a 1/f^beta spectrum, band-limited to [lo, hi] cycles."""
    fx = np.fft.fftfreq(n) * n
    f = np.sqrt(fx[None, :] ** 2 + fx[:, None] ** 2)
    amp = np.where((f >= lo) & (f <= hi), 1.0 / np.maximum(f, 1e-6) ** beta, 0.0)
    phase = np.exp(2j * np.pi * rng.random((n, n)))
    return np.real(np.fft.ifft2(amp * phase))


def uniform(a):
    """Rank-normalises to a flat 0..1 histogram so shader thresholds map to coverage."""
    flat = a.ravel()
    ranks = np.empty_like(flat)
    ranks[np.argsort(flat)] = np.linspace(0, 1, flat.size)
    return ranks.reshape(a.shape)


def unit(a):
    return (a - a.min()) / (a.max() - a.min())


def save(arr, name, mode=None):
    img = Image.fromarray(np.clip(arr * 255 + 0.5, 0, 255).astype(np.uint8), mode)
    img.save(os.path.join(TEX, name))
    print("wrote", name, img.size)


def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength
    n = np.stack([-dx, dy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def make_noise():
    n = 512
    chans = [uniform(spectral_noise(n, 1.8, 2, 64)), uniform(spectral_noise(n, 1.6, 3, 128)),
             uniform(spectral_noise(n, 1.0, 8, 200)), uniform(spectral_noise(n, 2.2, 1, 32))]
    save(np.stack(chans, -1), "noise.png", "RGBA")


def make_water_normal():
    n = 512
    h = unit(spectral_noise(n, 1.4, 4, 90))
    save(normal_from_height(h, 55.0), "water_normal.png", "RGB")


def make_caustics():
    n, count = 512, 70
    pts = rng.random((count, 2))
    tiled = np.concatenate([pts + np.array([dx, dy]) for dx in (-1, 0, 1) for dy in (-1, 0, 1)])
    ys, xs = np.mgrid[0:n, 0:n] / n
    f1 = np.full((n, n), 9.0)
    f2 = np.full((n, n), 9.0)
    for px, py in tiled:
        d = np.sqrt((xs - px) ** 2 + (ys - py) ** 2)
        f2 = np.where(d < f1, f1, np.minimum(f2, d))
        f1 = np.minimum(f1, d)
    edge = np.clip(1.0 - (f2 - f1) / 0.022, 0, 1) ** 2.2
    img = Image.fromarray((edge * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2))
    img.save(os.path.join(TEX, "caustics.png"))
    print("wrote caustics.png")


def make_rock():
    src = Image.open(os.path.join(ROOT, "ArtSource", "refs", "rock_tile.png")).convert("RGB")
    s = min(src.size)
    half = src.crop((0, 0, s, s)).resize((512, 512), Image.LANCZOS)
    # Mirror-tile so the result is seamless in both directions.
    tile = Image.new("RGB", (1024, 1024))
    tile.paste(half, (0, 0))
    tile.paste(half.transpose(Image.FLIP_LEFT_RIGHT), (512, 0))
    tile.paste(half.transpose(Image.FLIP_TOP_BOTTOM), (0, 512))
    tile.paste(half.transpose(Image.ROTATE_180), (512, 512))
    a = np.asarray(tile).astype(np.float32) / 255.0
    lum = a.mean(-1)
    detail = lum - np.asarray(Image.fromarray((lum * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(24))).astype(np.float32) / 255.0
    mean = a.reshape(-1, 3).mean(0)
    out = mean * 1.55 + (a - mean) * 2.4 + detail[..., None] * 1.6
    save(out, "rock_albedo.png", "RGB")
    h = np.asarray(Image.fromarray((unit(lum) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.5))).astype(np.float32) / 255.0
    save(normal_from_height(h, 9.0), "rock_normal.png", "RGB")


def make_grass():
    v = np.linspace(0, 1, 256)[::-1, None]          # image top = blade tip (v = 1)
    stops = [(0.0, (0.16, 0.05, 0.32)), (0.45, (0.55, 0.14, 0.82)), (0.85, (0.85, 0.35, 1.0)), (1.0, (1.0, 0.70, 1.0))]
    col = np.zeros((256, 1, 3))
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        m = (v >= t0) & (v <= t1)
        k = np.clip((v - t0) / (t1 - t0), 0, 1)
        col = np.where(m[..., None], np.array(c0) * (1 - k[..., None]) + np.array(c1) * k[..., None], col)
    u = np.linspace(-1, 1, 32)[None, :, None]
    col = np.repeat(col, 32, 1) * (1.0 - 0.25 * u ** 2)   # darker edges, lighter midrib
    save(col, "grass_base.png", "RGB")
    save(col * (v[..., None] ** 2.0) * 0.55, "grass_emit.png", "RGB")


def make_particle():
    n = 64
    ys, xs = np.mgrid[0:n, 0:n]
    r = np.sqrt((xs - n / 2 + 0.5) ** 2 + (ys - n / 2 + 0.5) ** 2) / (n / 2)
    a = np.clip(1 - r, 0, 1) ** 2.0
    save(np.stack([np.ones_like(a)] * 3 + [a], -1), "particle_soft.png", "RGBA")


def make_enemy_variants():
    """Recolours the stalker texture for the other enemy kinds, and gives each a glowing frill:
    spitter = dark teal body with a cyan-green frill, boss = near-black crimson body with a burning orange frill."""
    src = Image.open(os.path.join(TEX, "enemy_base.png")).convert("RGB")
    hsv = np.asarray(src.convert("HSV")).astype(np.float32)
    h, s, v = hsv[..., 0] * 360 / 255, hsv[..., 1] / 255, hsv[..., 2] / 255
    frill = (h > 8) & (h < 52) & (s > 0.45) & (v > 0.45)

    def build(name, body_hue_shift, body_sat, body_val, frill_hue, frill_val, emit_strength):
        nh = np.where(frill, frill_hue, (h + body_hue_shift) % 360)
        ns = np.where(frill, np.clip(s * 1.1, 0, 1), np.clip(s * body_sat, 0, 1))
        nv = np.where(frill, np.clip(v * frill_val, 0, 1), np.clip(v * body_val, 0, 1))
        out = np.stack([nh * 255 / 360, ns * 255, nv * 255], -1).astype(np.uint8)
        rgb = Image.fromarray(out, "HSV").convert("RGB")
        rgb.save(os.path.join(TEX, f"enemy_{name}_base.png"))
        mask = np.asarray(Image.fromarray((frill * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2.5))).astype(np.float32) / 255
        emit = np.asarray(rgb).astype(np.float32) / 255 * mask[..., None] * emit_strength
        Image.fromarray(np.clip(emit * 255, 0, 255).astype(np.uint8)).resize((1024, 1024), Image.LANCZOS).save(
            os.path.join(TEX, f"enemy_{name}_emit.png"))
        print(f"wrote enemy_{name} (frill coverage {frill.mean():.2%})")

    build("spitter", -75, 0.9, 0.85, 165, 1.15, 1.0)
    build("boss", 115, 1.1, 0.68, 22, 1.2, 1.0)
    build("burster", 140, 1.25, 0.95, 48, 1.3, 1.0)


def make_emission_masks():
    """Cyan lights and veins painted into Meshy base colours become emission maps so they bloom."""
    for f in sorted(os.listdir(TEX)):
        if not f.endswith("_base.png") or f.startswith("grass") or f.startswith("enemy_"):
            continue
        img = Image.open(os.path.join(TEX, f)).convert("RGB")
        a = np.asarray(img).astype(np.float32) / 255.0
        r, g, b = a[..., 0], a[..., 1], a[..., 2]
        mx, mn = a.max(-1), a.min(-1)
        sat = (mx - mn) / np.maximum(mx, 1e-4)
        cyan = (g > 0.55) & (b > 0.5) & (r < 0.75 * np.minimum(g, b)) & (sat > 0.35)
        if f.startswith("alpha"):
            cyan = cyan | ((r > 0.8) & (g > 0.38) & (g < 0.78) & (b < 0.36))
        if f.startswith("spitter"):
            cyan = cyan | ((g > 0.75) & (b > 0.62) & (r < 0.72))
        coverage = cyan.mean()
        name = f.replace("_base.png", "_emit.png")
        if coverage < 0.0004:
            print(f"skip {name} (coverage {coverage:.4%})")
            continue
        mask = np.asarray(Image.fromarray((cyan * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(2.0))).astype(np.float32) / 255.0
        out = Image.fromarray(np.clip(a * mask[..., None] * 255, 0, 255).astype(np.uint8)).resize((1024, 1024), Image.LANCZOS)
        out.save(os.path.join(TEX, name))
        print(f"wrote {name} (coverage {coverage:.3%})")


if __name__ == "__main__":
    make_noise()
    make_water_normal()
    make_caustics()
    make_rock()
    make_grass()
    make_enemy_variants()
    make_particle()
    make_emission_masks()
