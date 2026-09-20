import mmap,struct,json,sys
p=r'E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091/GameAssembly.dll'
requests=json.loads(sys.stdin.read())
with open(p,'rb') as f,mmap.mmap(f.fileno(),0,access=mmap.ACCESS_READ) as m:
 assert len(m)<128*1024*1024
 u16=lambda o:struct.unpack_from('<H',m,o)[0]
 u32=lambda o:struct.unpack_from('<I',m,o)[0]
 u64=lambda o:struct.unpack_from('<Q',m,o)[0]
 pe=u32(0x3c);opt=pe+24;base=u64(opt+24);sections=[]
 for i in range(u16(pe+6)):
  o=opt+u16(pe+20)+40*i;vs,va,rs,ro=struct.unpack_from('<IIII',m,o+8);sections.append((va,rs,ro))
 def raw(va):
  for v,s,o in sections:
   if v<=va-base<v+s:return o+va-base-v
  raise ValueError(hex(va))
 def va(off):
  for v,s,o in sections:
   if o<=off<o+s:return base+v+off-o
  raise ValueError(hex(off))
 pos=m.find(b'Assembly-CSharp.dll\0');assert pos>=0
 ref=m.find(struct.pack('<Q',va(pos)));assert ref>=0 and ref%8==0
 count=u64(ref+8);table=raw(u64(ref+16));assert 1000<count<100000
 pointers=[u64(table+i*8) for i in range(count)]
 output=[]
 for req in requests:
  pointer=pointers[(req['token']&0xffffff)-1];off=raw(pointer)
  greater=[v for v in pointers if v>pointer];size=min(min(greater)-pointer if greater else 2048,2048)
  output.append(dict(req,va=pointer,rva=hex(pointer-base),same_slots=sum(v==pointer for v in pointers),bytes=list(m[off:off+size])))
 print(json.dumps(output))
