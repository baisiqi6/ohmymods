from pathlib import Path
import ctypes as c, json, struct
import UnityPy, lz4.block

game=Path('E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/KingdomTwoCrowns_Data')
out=Path('C:/Users/ADMIN/Documents/Codex/2026-09-05/ohmymods-operator-2/hero-live-fixes-20260915/shaders');out.mkdir(exist_ok=True)
dll=c.WinDLL('d3dcompiler_47.dll')
fn=dll.D3DDisassemble
fn.argtypes=[c.c_void_p,c.c_size_t,c.c_uint,c.c_char_p,c.POINTER(c.c_void_p)];fn.restype=c.c_long
def disassemble(data):
    buf=c.create_string_buffer(data); obj=c.c_void_p()
    hr=fn(buf,len(data),0,None,c.byref(obj))
    if hr<0: return None
    vt=c.cast(obj,c.POINTER(c.POINTER(c.c_void_p))).contents
    ptr=c.WINFUNCTYPE(c.c_void_p,c.c_void_p)(vt[3])(obj)
    size=c.WINFUNCTYPE(c.c_size_t,c.c_void_p)(vt[4])(obj)
    result=c.string_at(ptr,size).decode('utf8','replace')
    c.WINFUNCTYPE(c.c_uint,c.c_void_p)(vt[2])(obj)
    return result
results=[]
for file in ('sharedassets0.assets','resources.assets'):
    env=UnityPy.load(str(game/file))
    for o in env.objects:
        if o.type.name!='Shader':continue
        d=o.read_typetree(); name=d.get('m_ParsedForm',{}).get('m_Name','')
        if name not in ('Custom/PowerSprite2','Custom/PowerFire','Custom/SpriteMask','Custom/SpriteMaskAllSprites'):continue
        blob=bytes(d['compressedBlob']); raw=lz4.block.decompress(blob,uncompressed_size=d['decompressedLengths'][0][0])
        pos=0; n=0; entries=[]
        while True:
            pos=raw.find(b'DXBC',pos)
            if pos<0:break
            length=struct.unpack_from('<I',raw,pos+24)[0]
            data=raw[pos:pos+length];pos+=4
            if length<32 or len(data)!=length:continue
            asm=disassemble(data)
            if asm is None:continue
            stage=next((line.strip() for line in asm.splitlines() if line.startswith(('ps_','vs_','gs_'))),'unknown')
            if stage.startswith('ps_'):
                file_out=out/(name.replace('/','_')+'_'+str(n)+'.txt'); file_out.write_text(asm,encoding='utf8')
                entries.append(dict(file=str(file_out),stage=stage,discard=[line.strip() for line in asm.splitlines() if 'discard' in line]))
            n+=1
        results.append(dict(shader=name,programs=n,pixelPrograms=entries))
(out/'audit.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps([dict(shader=r['shader'],programs=r['programs'],pixelPrograms=len(r['pixelPrograms']),withDiscard=sum(bool(p['discard']) for p in r['pixelPrograms'])) for r in results]))
