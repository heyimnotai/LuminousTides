"""Turns ArtSource/refs/<name>.png into ArtSource/meshy/<name>.glb with Meshy Smart Topology.

Usage: MESHY_API_KEY=... python meshy_gen.py [name ...]
"""
import base64, json, os, sys, time, urllib.request, urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
API = "https://api.meshy.ai/openapi/v1/image-to-3d"
KEY = os.environ["MESHY_API_KEY"]

# name -> (target polycount, extra request parameters)
ASSETS = {
    "enemy":   (20000, {"pose_mode": "a-pose"}),
    "carbine": (14000, {}),
    "arm":     (9000,  {}),
    "fungus":  (10000, {}),
    "station": (18000, {}),
    "crate":   (4000,  {}),
    "arch":    (16000, {}),
    "spire":   (10000, {}),
    "boulder": (6000,  {}),
    "bulbplant": (8000, {}),
    "sight":   (9000,  {}),
    "shotgun": (14000, {}),
    "handcannon": (12000, {}),
    "player":  (24000, {"pose_mode": "a-pose"}),
    "alpha":   (22000, {"pose_mode": "a-pose"}),
    "spitter": (18000, {"pose_mode": "a-pose"}),
    # Varrik faction
    "varrik_thrall":    (16000, {"pose_mode": "a-pose"}),
    "varrik_rifleman":  (18000, {"pose_mode": "a-pose"}),
    "varrik_captain":   (20000, {"pose_mode": "a-pose"}),
    "varrik_chieftain": (22000, {"pose_mode": "a-pose"}),
    "varrik_wraith":    (18000, {"pose_mode": "a-pose"}),
    "varrik_captain2":  (22000, {"pose_mode": "a-pose"}),
    "varrik_rifle":     (6000,  {}),
    "varrik_hound":     (16000, {}),
    "varrik_kite":      (9000,  {}),
    "varrik_strider":   (20000, {}),
    "varrik_queen":     (22000, {"pose_mode": "a-pose"}),
    # World landmarks
    "varrik_ship":      (30000, {}),
    "stormgate":        (24000, {}),
    # The Sunken Citadel kit (demo level)
    "citadel_stack":     (26000, {}),
    "citadel_cliff":     (28000, {}),
    "citadel_colonnade": (24000, {}),
    "citadel_arch":      (20000, {}),
    "citadel_head":      (20000, {}),
    "citadel_wall":      (20000, {}),
    "citadel_stairs": (18000, {}),
    "citadel_bridge": (22000, {}),
    "citadel_tower": (22000, {}),
    "citadel_terrace": (26000, {}),
    "citadel_arenawall": (22000, {}),
    "citadel_tusk": (16000, {}),
    "citadel_dais": (16000, {}),
    "citadel_statue": (24000, {}),
    "citadel_rubble": (14000, {}),
    "citadel_obelisk": (14000, {}),
}


def call(url, body=None):
    req = urllib.request.Request(url, data=json.dumps(body).encode() if body else None,
                                 headers={"Authorization": "Bearer " + KEY, "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"HTTP {e.code}: {e.read().decode(errors='ignore')[:500]}")


def submit(name):
    poly, extra = ASSETS[name]
    with open(os.path.join(HERE, "refs", name + ".png"), "rb") as f:
        uri = "data:image/png;base64," + base64.b64encode(f.read()).decode()
    body = {"image_url": uri, "model_type": "smart-topology", "ai_model": "meshy-t2.5",
            "target_polycount": poly, "should_texture": True, "enable_pbr": True,
            "target_formats": ["glb"], **extra}
    return call(API, body)["result"]


def main():
    names = sys.argv[1:] or list(ASSETS)
    out_dir = os.path.join(HERE, "meshy")
    os.makedirs(out_dir, exist_ok=True)
    tasks = {}
    for n in names:
        try:
            tasks[n] = submit(n)
            print("submitted", n, tasks[n], flush=True)
        except Exception as e:
            print("SUBMIT FAIL", n, e, flush=True)
    with open(os.path.join(out_dir, "tasks.json"), "a") as f:
        f.write(json.dumps(tasks) + "\n")

    pending = dict(tasks)
    deadline = time.time() + 40 * 60
    while pending and time.time() < deadline:
        time.sleep(15)
        for n, tid in list(pending.items()):
            try:
                t = call(API + "/" + tid)
            except Exception as e:
                print("poll error", n, e, flush=True)
                continue
            if t["status"] == "SUCCEEDED":
                urllib.request.urlretrieve(t["model_urls"]["glb"], os.path.join(out_dir, n + ".glb"))
                print("ok", n, flush=True)
                del pending[n]
            elif t["status"] in ("FAILED", "CANCELED"):
                print("FAIL", n, t.get("task_error"), flush=True)
                del pending[n]
    for n in pending:
        print("TIMEOUT", n, flush=True)
    print("ALLDONE", flush=True)


if __name__ == "__main__":
    main()
