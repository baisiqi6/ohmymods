#include <stdio.h>
#include <stdlib.h>
#include <dlfcn.h>
typedef int(*fn)(int);typedef int(*prep_fn)(void*,void*,void**);typedef int(*op_fn)(void*);
static fn orig[8];static int hits[8];
static int replacement0(int x){hits[0]++;return orig[0](x)+1000;}
static int replacement1(int x){hits[1]++;return orig[1](x)+1001;}
static int replacement2(int x){hits[2]++;return orig[2](x)+1002;}
static int replacement3(int x){hits[3]++;return orig[3](x)+1003;}
static int replacement4(int x){hits[4]++;return orig[4](x)+1004;}
static int replacement5(int x){hits[5]++;return orig[5](x)+1005;}
static int replacement6(int x){hits[6]++;return orig[6](x)+1006;}
static int replacement7(int x){hits[7]++;return orig[7](x)+1007;}
int main(int argc,char**argv){setvbuf(stdout,NULL,_IONBF,0);if(argc!=3)return 2;void*d=dlopen(argv[1],RTLD_NOW);void*t=dlopen(argv[2],RTLD_NOW);if(!d||!t){puts(dlerror());return 2;}prep_fn prep=dlsym(d,"DobbyPrepare");op_fn commit=dlsym(d,"DobbyCommit"),destroy=dlsym(d,"DobbyDestroy");fn targets[8];fn repl[8]={replacement0,replacement1,replacement2,replacement3,replacement4,replacement5,replacement6,replacement7};
for(int i=0;i<8;i++){char n[32];snprintf(n,sizeof(n),"target%d",i);targets[i]=dlsym(t,n);for(int j=0;j<10000;j++)if(targets[i](5)!=(8+i)*(7+i))return 3;}
for(int round=0;round<25;round++){
 for(int i=0;i<8;i++){hits[i]=0;if(prep((void*)targets[i],(void*)repl[i],(void**)&orig[i])||commit((void*)targets[i]))return 4;}
 for(int n=0;n<100;n++)for(int i=0;i<8;i++)if(targets[i](5)!=(8+i)*(7+i)+1000+i||orig[i](5)!=(8+i)*(7+i))return 5;
 for(int i=7;i>=0;i--){if(destroy((void*)targets[i])||targets[i](5)!=(8+i)*(7+i)||hits[i]!=100)return 6;}
 printf("round=%d hooks=8 calls=800 original_and_restore=PASS\n",round+1);
}
puts("RESULT=PASS rounds=25 installs=200 hooked_calls=20000");return 0;}
