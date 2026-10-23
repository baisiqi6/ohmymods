#!/usr/bin/env python3
"""Validate the portable R12 art/data graph without installing or running Unity."""
from pathlib import Path
import argparse
import hashlib
import json
import math

from PIL import Image


def pixels(image):
    """Support the current Pillow API and older packaged Pillow releases."""
    getter=getattr(image,'get_flattened_data',None)
    return getter() if getter else image.getdata()


def semantic_sha(document):
    value={k:v for k,v in document.items() if k not in ['readyForIsolatedIntegration','offlineReviewPending']}
    return hashlib.sha256(json.dumps(value,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest()


def finite_number(value):
    return type(value) in (int,float) and math.isfinite(value)


def validate(root,require_offline_ready=False):
    root=Path(root).resolve()
    registry=json.loads((root/'registry.json').read_text())
    layout=json.loads((root/'layout.json').read_text())
    index=json.loads((root/'SHA256.json').read_text())
    if registry.get('nativeAccepted') is not False or layout.get('nativeAccepted') is not False:raise ValueError('Art pack cannot declare Native acceptance')
    ready=registry.get('readyForIsolatedIntegration',False)
    if require_offline_ready and not ready:raise ValueError('Offline review is not complete')
    if ready and registry['offlineReviewPending']:raise ValueError('Offline ready with pending review')

    def contained(relative):
        rel=Path(relative)
        if rel.is_absolute() or '..' in rel.parts:raise ValueError('Nonportable path: '+str(relative))
        result=(root/rel).resolve()
        if not result.is_relative_to(root):raise ValueError('Path escapes package: '+str(relative))
        return result

    def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()

    actual_files={p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file() and p.name!='SHA256.json'}
    if set(index)!=actual_files:raise ValueError('File index does not exactly cover package')
    if not {'registry.json','layout.json'}.issubset(index):raise ValueError('Unpinned registry/layout pair')
    for relative,expected in index.items():
        path=contained(relative)
        if digest(path)!=expected:raise ValueError('File hash mismatch: '+relative)
    if digest(root/'palette.json')!=registry['paletteSHA256']:raise ValueError('Palette pin mismatch')
    palette=json.loads((root/'palette.json').read_text())
    allowed_colours={tuple(c) for c in palette['rgb']}
    if layout['worldDomain']!=[-200,200] or layout['pixelsPerUnitDesign']!=32:raise ValueError('Wrong physical design domain')
    if layout['designStreetReferenceY']!=224 or layout['designXAxis']['gateAxisPixelX']!=6420:raise ValueError('Wrong R12 design origin')
    assets=registry['assets'];byid={a['id']:a for a in assets}
    if len(byid)!=len(assets) or len(assets)!=86:raise ValueError('Missing/duplicate asset id')
    counters={};frames_checked=0;dancer_cells=None
    for a in assets:
        name=a['id'];path=contained(a['path'])
        if a['path'] not in index or digest(path)!=a['SHA256']:raise ValueError('Unpinned asset: '+name)
        image=Image.open(path).convert('RGBA');w,h=image.size
        if [w,h]!=a['size'] or max(w,h)>2048:raise ValueError('Wrong size: '+name)
        if not set(pixels(image.getchannel('A'))).issubset({0,255}):raise ValueError('Soft alpha: '+name)
        if any(p[:3]!=(0,0,0) for p in pixels(image) if p[3]==0):raise ValueError('Transparent RGB: '+name)
        if a['kind']!='actor-atlas' and not {p[:3] for p in pixels(image) if p[3]}.issubset(allowed_colours):raise ValueError('Environment palette drift: '+name)
        if a['masterRect']!=[0,0,w,h]:raise ValueError('Wrong master canvas: '+name)
        if a['localOrigin']!=[0,0] or a['pixelsPerUnitDesign']!=32 or a['filterMode']!='Point':raise ValueError('Wrong art coordinate/filter contract: '+name)
        counters[a['kind']]=counters.get(a['kind'],0)+1
        frames=a.get('frames',[]);pivots=set();cells=set()
        for frame in frames:
            x,y,fw,fh=frame['sourceRect']
            if x<0 or y<0 or fw<=0 or fh<=0 or x+fw>w or y+fh>h:raise ValueError('Frame outside atlas: '+name)
            if frame['masterId']!=a['masterId'] or frame['masterRect']!=frame['sourceRect']:raise ValueError('Frame master mismatch: '+name)
            if frame['durationMs']<=0:raise ValueError('Frame duration: '+name)
            actual=hashlib.sha256(image.crop((x,y,x+fw,y+fh)).tobytes()).hexdigest()
            if actual!=frame['logicalRGBA_SHA256']:raise ValueError('Frame RGBA readback: '+name)
            pivots.add(tuple(frame['pivotBottomLeft']));cells.add(tuple(frame['sourceRect']));frames_checked+=1
        if frames and len(pivots)!=1:raise ValueError('Pivot drift: '+name)
        if name=='court-dancer':
            dancer_cells=len(cells)
            if len(frames)!=26 or len(cells)!=21:raise ValueError('Lost dancer explicit aliases')
    if frames_checked!=358:raise ValueError('Expected 294 actor +64 local-motion temporal frames')
    if dancer_cells is None:raise ValueError('Missing required court-dancer')
    if counters.get('environment-entity')!=54 or counters.get('background-compound')!=13 or counters.get('ground-module')!=5:raise ValueError('Wrong complete environment counts')
    if counters.get('actor-atlas')!=10 or counters.get('local-environment-motion-atlas')!=4:raise ValueError('Wrong animated asset counts')
    gate=byid['central-palace-gate']
    returns=[a for a in gate['interaction'] if a['id']=='return-to-source']
    if len(returns)!=1 or returns[0]['purpose']!='return-source-island' or returns[0]['coordinateSystem']!='asset-local-top-left-pixels' or returns[0]['point']!=[218,176]:raise ValueError('Missing explicit gate return anchor')
    if returns[0]['chargeCoins']!=0:raise ValueError('Return must not charge coins')
    districts={d['id'] for d in layout['districts']}
    if districts!=set(range(1,12)):raise ValueError('Wrong district declaration')
    instance_ids=set();backgrounds=set()
    for p in layout['placements']:
        if p['id'] in instance_ids:raise ValueError('Duplicate instance id: '+p['id'])
        instance_ids.add(p['id'])
        if p['assetId'] not in byid:raise ValueError('Missing placed asset: '+p['assetId'])
        if p['referencePlane'] not in ['native-ground','native-water']:raise ValueError('Unknown reference plane')
        if len(p['supportLocalPixel'])!=2:raise ValueError('Missing attachment')
        asset=byid[p['assetId']];sx,sy=p['supportLocalPixel']
        width,height=asset['frames'][0]['sourceRect'][2:] if asset.get('frames') else asset['size']
        if not all(finite_number(n) for n in [sx,sy,p['supportWorldX'],p['supportOffsetYUnits']]):raise ValueError('Nonfinite placement: '+p['id'])
        if not (0<=sx<=width and 0<=sy<=height):raise ValueError('Support outside canvas: '+p['id'])
        support=asset['support'];declared=support.get('point',support.get('cellPixelTopLeft'))
        if declared!=p['supportLocalPixel']:raise ValueError('Support declaration mismatch: '+p['id'])
        dx,dy=p['designSupportPixel']
        if not all(finite_number(n) for n in [dx,dy]) or not math.isclose(p['supportWorldX'],(dx-6420)/32,abs_tol=1e-9) or not math.isclose(p['supportOffsetYUnits'],(224-dy)/32,abs_tol=1e-9):raise ValueError('Design/world placement mismatch: '+p['id'])
        if not -200<=p['supportWorldX']<=200 and not (asset['kind']=='background-compound' and p.get('allowOutsideWalkDomain') is True and p.get('placementScope')=='decorative-background-outside-walk-domain'):raise ValueError('Placement outside walk domain: '+p['id'])
        if p['drawRole'] not in layout['drawOrderHints'] or p['district'] not in districts:raise ValueError('Unknown draw role/district: '+p['id'])
        if p['drawRole'] in ['far-palace-skyline','mid-courtyard-forest','rear-courtyard-architecture','east-celestial-vista']:backgrounds.add(p['drawRole'])
    if len(backgrounds)!=4:raise ValueError('Empty background layer')
    if len(instance_ids)!=452:raise ValueError('Wrong complete placement count')
    if layout['backgroundMotionRates'] is not None:raise ValueError('Unmeasured Native parallax was frozen')
    if ready:
        approval=json.loads((root/'offline-approval.json').read_text())
        if approval['approvedForIsolatedIntegration'] is not True or approval['layoutSHA256']!=digest(root/'layout.json'):raise ValueError('Offline approval layout mismatch')
        if approval['assetSHA256']!={a['id']:a['SHA256'] for a in assets}:raise ValueError('Offline approval asset mismatch')
        if approval['registrySemanticSHA256']!=semantic_sha(registry):raise ValueError('Offline approval registry mismatch')
    return {'assets':len(assets),'countsByKind':counters,'fileHashesChecked':len(index),'framesReadBack':frames_checked,
            'dancerTemporalFrames':26,'dancerPhysicalCells':dancer_cells,'placements':len(instance_ids),'backgroundLayersNonempty':4,
            'offlineReviewPending':registry['offlineReviewPending'],'readyForIsolatedIntegration':ready,'nativeAccepted':False}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('pack',type=Path);parser.add_argument('--output',type=Path)
    parser.add_argument('--require-offline-ready',action='store_true')
    args=parser.parse_args();result=validate(args.pack,args.require_offline_ready);text=json.dumps(result,ensure_ascii=False,indent=2)+'\n'
    if args.output:args.output.write_text(text)
    print(text,end='')
