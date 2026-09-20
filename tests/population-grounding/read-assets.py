"""Read-only installed 2.4 resource evidence, selected fields only; no game/user writes."""
import hashlib
import json
from pathlib import Path
import UnityPy

base = Path("E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/KingdomTwoCrowns_Data")
dest = Path("docs/project-harness/tasks/runtime-anomalies-20260917/population-asset-evidence.json")
evidence = {"source": str(base), "files": {}, "objects": [], "layers": {}, "matrix": {}}
for filename in ["globalgamemanagers", "resources.assets", "level2"]:
    path = base / filename
    evidence["files"][filename] = {"size": path.stat().st_size, "sha256": hashlib.file_digest(path.open("rb"), "sha256").hexdigest()}
    env = UnityPy.load(str(path))
    for obj in env.objects:
        if filename == "globalgamemanagers" and obj.type.name in ["TagManager", "Physics2DSettings"]:
            data = obj.read_typetree()
            if obj.type.name == "TagManager":
                evidence["layers"] = {i: data["layers"][i] for i in [0, 10, 17, 18]}
            else:
                evidence["gravity"] = data["m_Gravity"]
                evidence["matrix"] = {i: {"rawMask": data["m_LayerCollisionMatrix"][i], "collides": [n for n in range(32) if data["m_LayerCollisionMatrix"][i] & (1 << n)]} for i in [0, 10, 17, 18]}
        if obj.type.name != "GameObject":
            continue
        if filename == "resources.assets" and obj.path_id not in [19697, 20090, 20759]:
            continue
        if filename not in ["resources.assets", "level2"]:
            continue
        data = obj.read_typetree()
        if filename == "level2" and data.get("m_Name") != "Ground":
            continue
        row = {"file": filename, "pathID": obj.path_id, "name": data["m_Name"], "layer": data["m_Layer"], "components": []}
        for ref in data["m_Component"]:
            ptr = ref["component"]
            if ptr["m_FileID"] != 0:
                continue
            reader = obj.assets_file.objects[ptr["m_PathID"]]
            if reader.type.name not in ["Transform", "Rigidbody2D", "BoxCollider2D", "CapsuleCollider2D", "CircleCollider2D"]:
                continue
            fields = reader.read_typetree()
            selected = {k: v for k, v in fields.items() if k in ["m_Father", "m_Children", "m_LocalPosition", "m_LocalScale", "m_BodyType", "m_Simulated", "m_GravityScale", "m_CollisionDetection", "m_Constraints", "m_IsTrigger", "m_Enabled", "m_Size", "m_Radius", "m_Offset"]}
            row["components"].append({"type": reader.type.name, "pathID": reader.path_id, "fields": selected})
        evidence["objects"].append(row)
evidence["limits"] = "Serialized resource evidence only. Runtime clones, their native identities, positions, parents and physics overrides at the six logged falls were not captured."
dest.write_text(json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print("wrote", dest, "objects=", len(evidence["objects"]))
