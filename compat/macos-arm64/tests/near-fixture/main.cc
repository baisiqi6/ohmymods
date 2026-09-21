#include <cstdio>
#include <cstdlib>
#include <cstdint>
#include <cstring>
#include <dlfcn.h>
#include <sys/mman.h>
#include <mach/mach.h>
#include <mach/mach_vm.h>
#include <unistd.h>
extern "C" long short_stub(long),short_body(long),short_movtail(long),short_tailbody(long),adjacent(long);
extern "C" float short_getter(float*);extern "C" void short_setter(float*,float);
using Prepare=int(*)(void*,void*,void**);using One=int(*)(void*);using Hook=int(*)(void*,void*,void**);
void require(bool b,const char*s){if(!b){fprintf(stderr,"FAIL %s\n",s);exit(1);}}
float getter_replacement(float*){return 99.25f;}void setter_replacement(float*p,float v){*p=v+10;}
int main(int argc,char**argv){require(argc>=2,"library path");void*lib=dlopen(argv[1],RTLD_NOW|RTLD_LOCAL);require(lib!=nullptr,dlerror());auto prepare=(Prepare)dlsym(lib,"DobbyPrepare");auto commit=(One)dlsym(lib,"DobbyCommit");auto destroy=(One)dlsym(lib,"DobbyDestroy");auto hook=(Hook)dlsym(lib,"DobbyHook");require(prepare&&commit&&destroy&&hook,"exports");
 if(argc>2){uint8_t before[24];memcpy(before,(void*)short_stub,24);void*origin=(void*)1;require(prepare((void*)short_stub,(void*)((uintptr_t)short_stub+0x20000000),&origin)!=0,"forced near failure");require(origin==nullptr,"failed Prepare clears origin");require(memcmp(before,(void*)short_stub,24)==0,"failed Prepare writes no entry or neighbor");require(commit((void*)short_stub)!=0,"Commit after failed Prepare refuses");puts("PASS forced near allocation failure: no write, null origin, Commit rejected");return 0;}
 size_t page=getpagesize();mach_vm_address_t far=((uintptr_t)short_stub+0x20000000+page-1)&~(page-1);bool allocated=false;for(int i=0;i<64;i++,far+=page){if(mach_vm_allocate(mach_task_self(),&far,page,VM_FLAGS_FIXED)==KERN_SUCCESS){allocated=true;break;}}require(allocated,"owned far executable fixture page");uint32_t code[]={0xd2800b40,0xd65f03c0};memcpy((void*)far,code,sizeof(code));require(mprotect((void*)far,page,PROT_READ|PROT_EXEC)==0,"fixture RX");
 struct Test{const char*name;long(*fn)(long);long expected;};Test cases[]={{"B+4 stub",short_stub,7},{"8B body",short_body,7},{"mov+tailB",short_movtail,8}};
 for(auto t:cases){uint8_t before[24];memcpy(before,(void*)t.fn,24);for(int cycle=0;cycle<4;cycle++){void*replacement=(cycle%2)?(void*)far:(void*)adjacent;void*origin=nullptr;require(prepare((void*)t.fn,replacement,&origin)==0&&origin,"prepare");require(memcmp(before,(void*)t.fn,24)==0,"Prepare no entry write");require(commit((void*)t.fn)==0,"commit");require((*(uint32_t*)t.fn&0xfc000000)==0x14000000,"4B B entry");require(memcmp(before+4,(uint8_t*)t.fn+4,20)==0,"adjacent bytes intact");require(t.fn(5)==((cycle%2)?90:31),"replacement call");require(((long(*)(long))origin)(5)==t.expected,"original trampoline call");require(adjacent(0)==31&&short_tailbody(5)==8,"adjacent calls intact");require(destroy((void*)t.fn)==0,"destroy");require(memcmp(before,(void*)t.fn,24)==0&&t.fn(5)==t.expected,"full restore");}printf("PASS %s four alternating near/far retarget cycles\n",t.name);}
 uint8_t getbefore[16];memcpy(getbefore,(void*)short_getter,16);void*getorig=nullptr;float value=2.25f;require(hook((void*)short_getter,(void*)getter_replacement,&getorig)==0,"getter hook");require(short_getter(&value)==99.25f&&((float(*)(float*))getorig)(&value)==2.25f,"getter hooked/original");require(memcmp(getbefore+4,(uint8_t*)short_getter+4,12)==0,"getter neighbor bytes");require(destroy((void*)short_getter)==0&&memcmp(getbefore,(void*)short_getter,16)==0,"getter restore");puts("PASS 8B float getter");
 uint8_t setbefore[16];memcpy(setbefore,(void*)short_setter,16);void*setorig=nullptr;require(hook((void*)short_setter,(void*)setter_replacement,&setorig)==0,"setter hook");short_setter(&value,3.25f);require(value==13.25f,"setter hook call");((void(*)(float*,float))setorig)(&value,4.25f);require(value==4.25f,"setter original call");require(memcmp(setbefore+4,(uint8_t*)short_setter+4,12)==0,"setter neighbor bytes");require(destroy((void*)short_setter)==0&&memcmp(setbefore,(void*)short_setter,16)==0,"setter restore");puts("PASS 8B float setter");
 require(commit((void*)((uintptr_t)short_stub+1))!=0,"unknown Commit rejects");puts("PASS all five layouts and unknown Commit");return 0;}
