#include <dlfcn.h>
#include <stdio.h>
#include <string.h>
#include <sys/mman.h>
#include <unistd.h>
#include <mach/mach.h>
#include <mach/mach_vm.h>
typedef int(*target_fn)(void);
typedef int(*prepare_fn)(void*,void*,void**);
typedef int(*op_fn)(void*);
static target_fn original;
static int replacement(void){return original()+1;}
static int protection(void *address){
 mach_vm_address_t p=(mach_vm_address_t)address;mach_vm_size_t n=0;
 vm_region_basic_info_data_64_t i;mach_msg_type_number_t c=VM_REGION_BASIC_INFO_COUNT_64;mach_port_t object=MACH_PORT_NULL;
 kern_return_t k=mach_vm_region(mach_task_self(),&p,&n,VM_REGION_BASIC_INFO_64,(vm_region_info_t)&i,&c,&object);
 if(object!=MACH_PORT_NULL)mach_port_deallocate(mach_task_self(),object);
 return k==KERN_SUCCESS&&p<=(mach_vm_address_t)address?(int)i.protection:-1;
}
int main(int argc,char **argv){
 setvbuf(stdout,NULL,_IONBF,0);if(argc!=2)return 2;
 void *h=dlopen(argv[1],RTLD_NOW);if(!h){puts(dlerror());return 2;}
 prepare_fn prep=(prepare_fn)dlsym(h,"DobbyPrepare");op_fn commit=(op_fn)dlsym(h,"DobbyCommit"),destroy=(op_fn)dlsym(h,"DobbyDestroy");
 if(!prep||!commit||!destroy)return 2;
 size_t size=(size_t)sysconf(_SC_PAGESIZE);int failures=0;
 const unsigned char code[]={0x55,0x48,0x89,0xe5,0xb8,41,0,0,0,0x5d,0xc3};
 for(int writable=0;writable<2;writable++){
  unsigned char *page=mmap(NULL,size,PROT_READ|PROT_WRITE|PROT_EXEC,MAP_PRIVATE|MAP_ANONYMOUS,-1,0);if(page==MAP_FAILED)return 2;
  memcpy(page,code,sizeof code);int expected=PROT_READ|PROT_EXEC|(writable?PROT_WRITE:0);
  if(mprotect(page,size,expected))return 2;
  target_fn target=(target_fn)page;if(target()!=41)return 3;
  // Rosetta may write-protect executed RWX pages; model the observed CLR page
  // whose live protection at CodePatch entry is RWX.
  if(writable && mprotect(page,size,expected))return 3;
  if(prep(page,(void*)replacement,(void**)&original)||!original||commit(page))return 4;
  int after=protection(page);
  if(writable&&(after&PROT_WRITE)){page[128]=0xa5;if(page[128]!=0xa5)failures++;}
  int hooked=target(),raw=original();
  if(after!=expected||hooked!=42||raw!=41)failures++;
  if(writable && mprotect(page,size,expected))return 3;
  int undo=destroy(page);int restored=protection(page);
  if(writable&&(restored&PROT_WRITE)){page[128]=0x5a;if(page[128]!=0x5a)failures++;}
  int value=target();
  if(undo||restored!=expected||value!=41)failures++;
  printf("case=%s expected_prot=%d after_hook=%d after_destroy=%d hooked=%d original=%d restored=%d\n",writable?"RWX mixed code/data":"RX code",expected,after,restored,hooked,raw,value);
  munmap(page,size);
 }
 printf("MIXED-PAGE=%s failures=%d\n",failures?"FAIL":"PASS",failures);return failures?1:0;
}
