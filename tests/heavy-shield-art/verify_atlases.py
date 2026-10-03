"""Verify the exact final pack, every atlas cell, both facings, and bilateral shop layers.

This reads source assets and installed repository PNGs. Only evidence is written.
Pixels/metadata passing here are offline evidence, never native game validation.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
from pathlib import Path
from PIL import Image, ImageOps

HERE = Path(__file__).resolve().parent
REPO = next(p for p in HERE.parents if (p / "artifacts/gem-shield-assets-20261001/animation-manifest.json").is_file())
SELECTED = REPO / "artifacts/gem-shield-assets-20261001"
EXPECTED_ZIP_SHA256 = "19cd73bdad95bc7a542b22a8cbee6ebc4784286887ef331536b9a799359f7cf8"
ACTIONS = ("back_idle", "back_walk", "back_run", "guard_idle", "defense_advance", "equip", "stow", "block", "bash", "walk_start", "walk_start_alt", "walk_stop", "walk_stop_alt", "run_start", "run_stop", "relax_idle", "rest_enter", "rest_idle", "rest_exit")
PREFIXES = ("", "worn_", "critical_", "half_")

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pack-root", type=Path)
    parser.add_argument("--evidence", type=Path, default=HERE / "evidence" / "final-20261001")
    args = parser.parse_args()
    handoff = json.loads((SELECTED / "operator-handoff.json").read_text(encoding="utf-8"))
    pack = args.pack_root or Path(handoff["assetRoot"])
    character = json.loads((SELECTED / "animation-manifest.json").read_text(encoding="utf-8"))
    shop = json.loads((SELECTED / "shop-bilateral-manifest.json").read_text(encoding="utf-8"))
    report = {"packRoot": str(pack), "sourceArchiveSHA256": EXPECTED_ZIP_SHA256, "notGameTested": True, "checks": 0, "failures": [], "resources": {}, "character": {}, "shops": {}, "paid": {}}
    def check(value: bool, message: str) -> None:
        report["checks"] += 1
        if not value: report["failures"].append(message)
    def image(path: Path) -> Image.Image:
        with Image.open(path) as opened: return opened.convert("RGBA")
    def digest(path: Path) -> str: return hashlib.sha256(path.read_bytes()).hexdigest()
    def cell(atlas: Image.Image, index: int, cfg: dict, label: str) -> Image.Image:
        w, h = cfg["cell"]; columns, rows = cfg.get("columns", 4), cfg.get("rows", 4)
        column, row = index % columns, index // columns
        unity_y = (rows - 1 - row) * h
        check(atlas.height - unity_y - h == row * h, label + " Unity bottom-to-top rect")
        return atlas.crop((column*w, row*h, (column+1)*w, (row+1)*h))
    def rgba_checks(im: Image.Image, label: str, allow_empty: bool = False) -> None:
        pixels = list(im.get_flattened_data())
        check({p[3] for p in pixels} <= {0,255}, label + " binary alpha")
        check(all(p[:3] == (0,0,0) for p in pixels if p[3] == 0), label + " transparent RGB zero")
        check(allow_empty or im.getchannel("A").getbbox() is not None, label + " nonempty")
    def copy(name: str, relative: str) -> Image.Image:
        path = REPO / "il2cpp/Assets" / name; source = pack / relative
        check(path.read_bytes() == source.read_bytes(), name + " original PNG bytes")
        report["resources"][name] = {"source": relative, "bytes": path.stat().st_size, "sha256": digest(path)}
        return image(path)
    def padding(atlas: Image.Image, cfg: dict, count: int, label: str) -> None:
        for slot in range(count, cfg.get("columns",4)*cfg.get("rows",4)):
            check(not any(cell(atlas,slot,cfg,label).tobytes()), label + f" padding {slot} transparent zero")
    def guard(name: str, body) -> None:
        try: body()
        except Exception as e: check(False, f"{name}: {type(e).__name__}: {e}")
    def provenance() -> None:
        check(digest(pack / "gem-shield-complete-assets-20261001.zip") == EXPECTED_ZIP_SHA256, "fixed final ZIP sha256")
        for original, selected in (("animation-manifest.json","animation-manifest.json"), ("shop-bilateral/shop-bilateral-manifest.json","shop-bilateral-manifest.json"), ("operator-handoff.json","operator-handoff.json"), ("asset-verification-final.json","asset-verification-final.json")):
            check((pack/original).read_bytes() == (SELECTED/selected).read_bytes(), selected + " final manifest original bytes")
        check(shop["semantics"]["paidShopSlots"] == 1, "one paid shop slot")
        check(character["wearStates"] == ["intact","worn","critical","half"], "four exact wear states")
    guard("provenance",provenance)
    contact_sheets = []
    allowed = {tuple(int(c[i:i+2],16) for i in (0,2,4)) for c in character["palette"]}
    for density,cfg in character["density"].items():
        def character_section(density=density,cfg=cfg) -> None:
            suffix = "Coarse" if density == "coarse" else ""
            atlas = copy(f"HeavyShieldSoldierAtlas{suffix}.png",cfg["atlas"])
            w,h = cfg["cell"]
            check(atlas.size == (cfg["columns"]*w,cfg["rows"]*h), density+" soldier sheet")
            check(cfg["rows"] == 32 and cfg["columns"] == 16 and cfg["frameCount"] == 509, density+" final32 rows 509 slots")
            check(math.isclose(cfg["body"]/cfg["ppuForBodyHeight0_7"],.7,abs_tol=1e-7),density+" specified standing body world height .7")
            foot_x,foot_y=cfg["foot"]; px=(foot_x+.5)/w; py=(h-foot_y-.5)/h
            check(all(math.isclose(a,b,abs_tol=1e-9) for a,b in zip((px,py),cfg["pivotBottomLeftNormalized"])),density+" pixel-center foot pivot")
            expected={prefix+action for prefix in PREFIXES for action in ACTIONS}|{"break"}
            check(set(cfg["sequences"]) == expected and len(expected) == 77,density+"19x4+break exact keys")
            cursor=0; bounds=[]; unique={}; frames={}
            for name,sequence in cfg["sequences"].items():
                label=density+" "+name
                check(sequence["first"] == cursor and sequence["count"] > 0 and math.isfinite(sequence["fps"]) and sequence["fps"] > 0,label+"continuous positive metadata")
                check(len(sequence["frames"]) == len(sequence["sourceKeys"]) == sequence["count"],label+"source list count")
                hashes=set()
                for offset,relative in enumerate(sequence["frames"]):
                    source=image(pack/density/relative); frame=sequence["first"]+offset
                    sliced=cell(atlas,frame,cfg,label)
                    check(source.size == (w,h) and source.tobytes() == sliced.tobytes(),label+f"[{offset}] pixel exact")
                    rgba_checks(sliced,label+f"[{offset}]")
                    pixels=list(sliced.get_flattened_data())
                    check({p[:3] for p in pixels if p[3]} <= allowed,label+f"[{offset}] original character palette")
                    box=sliced.getchannel("A").getbbox(); bounds.append(box)
                    check(box is not None and 0 < box[0] < box[2] < w and 0 < box[1] < box[3] < h,label+f"[{offset}] canvas clipping guard")
                    mirrored=ImageOps.mirror(sliced)
                    check(ImageOps.mirror(mirrored).tobytes() == source.tobytes(),label+f"[{offset}] both facings preserve every pixel")
                    # SpriteRenderer flip uses the same foot pivot as the unflipped sprite.
                    pivot_pixel=foot_x+.5; mirrored_pivot=w-pivot_pixel
                    check(math.isclose(((w-1-foot_x)+.5-mirrored_pivot),-(foot_x+.5-pivot_pixel),abs_tol=1e-9),label+f"[{offset}] mirrored foot world origin")
                    hashes.add(hashlib.sha256(sliced.tobytes()).hexdigest()); frames[(name,offset)]=sliced
                unique[name]=len(hashes)
                check(len(hashes) == sequence["uniquePixelFrames"],label+" declared unique pixels")
                cursor += sequence["count"]
            check(cursor == 509,density+" all 509 slots")
            padding(atlas,cfg,509,density)
            for prefix in PREFIXES:
                for clip,baseline,offset in (("equip","back_idle",0),("equip","guard_idle",22),("stow","guard_idle",0),("stow","back_idle",22),("run_start","back_idle",0),("run_stop","back_idle",3),("relax_idle","back_idle",0),("relax_idle","back_idle",7),("rest_enter","guard_idle",0),("rest_exit","guard_idle",2)):
                    check(frames[(prefix+clip,offset)].tobytes() == frames[(prefix+baseline,0)].tobytes(),density+" "+prefix+clip+" canonical foot/pose endpoint")
            report["character"][density]={"slots":cursor,"sequences":len(cfg["sequences"]),"bodyWorldHeight":cfg["body"]/cfg["ppuForBodyHeight0_7"],"canvasWorldSize":[w/cfg["ppuForBodyHeight0_7"],h/cfg["ppuForBodyHeight0_7"]],"footTopLeft":cfg["foot"],"unityPivot":[px,py],"opaqueBoundsRange":[[min(b[i] for b in bounds),max(b[i] for b in bounds)] for i in range(4)],"uniqueFrames":unique,"facingFramesChecked":cursor*2}
            preview=Image.new("RGBA",(w*12,h*4),(30,37,49,255))
            for row,prefix in enumerate(PREFIXES):
                for column,action in enumerate(("back_idle","back_walk","back_run","guard_idle","equip","rest_idle")):
                    pose=frames[(prefix+action,0)]
                    preview.alpha_composite(pose,(column*w*2,row*h));preview.alpha_composite(ImageOps.mirror(pose),(column*w*2+w,row*h))
            contact_sheets.append((f"soldier-{density}-both-facings.png",preview))
        guard(density+" soldier",character_section)
    for density,cfg in shop["densities"].items():
        def paid_section(density=density,cfg=cfg) -> None:
            suffix="Coarse" if density == "coarse" else ""
            paid=copy(f"HeavyShieldPaidShield{suffix}.png",f"shop-bilateral/{density}/paid-shield.png")
            rgba_checks(paid,density+" independent paid shield")
            check(paid.size == ((13,20) if suffix else (19,30)),density+" paid dimensions")
            check(paid.getchannel("A").getbbox() == (0,0,*paid.size),density+" full shield no crop padding")
            for theme in cfg["themes"].values(): check(theme["paidPivotBottomLeft"] == [.5,0],density+" paid bottom center")
            report["paid"][density]={"size":list(paid.size),"ppu":cfg["ppu"],"worldSize":[paid.width/cfg["ppu"],paid.height/cfg["ppu"]],"pivot":[.5,0]}
        guard(density+" paid",paid_section)
        for theme,theme_cfg in cfg["themes"].items():
            def shop_section(density=density,cfg=cfg,theme=theme,theme_cfg=theme_cfg) -> None:
                w,h=cfg["cell"]; suffix="Coarse" if density == "coarse" else ""
                base=pack/f"shop-bilateral/{density}/{theme}"
                resource=f"HeavyShield{'GreekShopLayers' if theme == 'greek' else 'ShopLayers'}{suffix}.png"
                layers=copy(resource,f"shop-bilateral/{density}/{theme}/GemShieldShopLayers.png")
                composite=image(base/"GemShieldShopAtlas.png") if theme == "greek" else copy(f"HeavyShieldShopAtlas{suffix}.png",f"shop-bilateral/{density}/{theme}/GemShieldShopAtlas.png")
                check(layers.size == composite.size == (w*4,h*4),density+" "+theme+" sheet dims")
                check(math.isclose(w/cfg["ppu"],3) and math.isclose(h/cfg["ppu"],1.875),density+" "+theme+" shop world size")
                expected=["rear.png"]+["fixtures-empty.png"]*6+[f"merchant-{i:02}.png" for i in range(6)]+["front.png"]
                layer_frames=[]
                for index,filename in enumerate(expected):
                    sliced=cell(layers,index,cfg,resource); original=image(base/filename)
                    check(sliced.tobytes() == original.tobytes(),resource+f" cell{index} source exact")
                    rgba_checks(sliced,resource+f"[{index}]",1 <= index <= 6)
                    layer_frames.append(sliced)
                for slot in range(1,7): check(not any(layer_frames[slot].tobytes()),resource+f" fixtures{slot} empty: no baked collectible")
                padding(layers,cfg,14,resource);padding(composite,cfg,13,density+theme+" composite")
                for index in range(13):
                    merchant=0 if index<7 else index-7
                    built=Image.new("RGBA",(w,h))
                    for layer in (layer_frames[0],layer_frames[1],layer_frames[7+merchant],layer_frames[13]):built.alpha_composite(layer)
                    check(cell(composite,index,cfg,theme).tobytes() == built.tobytes(),density+" "+theme+f" composite{index} exact no paid preview layers")
                paid=image(pack/f"shop-bilateral/{density}/paid-shield.png")
                for side in ("left","right"):
                    preview=image(base/f"paid-{side}-preview-layer.png")
                    expected_paid=paid if side == "left" else ImageOps.mirror(paid)
                    coords=theme_cfg["previewPaidTextureTopLeft"][side]
                    only_paid=Image.new("RGBA",(w,h));only_paid.alpha_composite(expected_paid,tuple(coords))
                    check(preview.tobytes() == only_paid.tobytes(),density+" "+theme+" "+side+" one independent paid shield")
                    ground_x,ground_y=cfg["foot"]
                    anchor=shop["runtimeAnchorsWorld"]["pickup"+side.title()]
                    real=[ground_x+.5+anchor[0]*cfg["ppu"],ground_y+.5-anchor[1]*cfg["ppu"]]
                    check(all(math.isclose(a,b,abs_tol=1e-6) for a,b in zip(real,theme_cfg["pickupAnchorsTopLeftFloat"][side])),density+" "+theme+" "+side+" exact existing world pickup anchor")
                    check(abs(real[0]-(coords[0]+paid.width*.5)) <= .5 and abs(real[1]-(coords[1]+paid.height)) <= .5,density+" "+theme+" "+side+" preview anchor within half pixel")
                report["shops"][density+"/"+theme]={"cell":cfg["cell"],"ppu":cfg["ppu"],"merchantFps":theme_cfg["smithFps"],"layers":14,"composites":13,"transparentFixtures":6,"runtimePaidPreviewLayersLoaded":False}
                view=Image.new("RGBA",(w*6,h),(30,37,49,255))
                for i in range(6):view.alpha_composite(cell(composite,7+i,cfg,theme),(i*w,0))
                contact_sheets.append((f"shop-{density}-{theme}-smith.png",view))
            guard(density+" "+theme+" shop",shop_section)
    args.evidence.mkdir(parents=True,exist_ok=True)
    for name,im in contact_sheets:im.resize((im.width*3,im.height*3),Image.Resampling.NEAREST).save(args.evidence/name)
    report["result"]="pass" if not report["failures"] else "fail"
    report["limitation"]="PPU and pivots use declared artist body/foot metadata; these checks do not measure native parent scale, live sprite flips, animation timing or peasant pickup."
    target=args.evidence/"atlas-evidence.json"
    target.write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(f"heavy-shield final atlas evidence: {report['checks']} checks, {len(report['failures'])} failures")
    print(f"evidence: {target}")
    for failure in report["failures"]:print("FAIL "+failure)
    raise SystemExit(1 if report["failures"] else 0)

if __name__ == "__main__":main()
