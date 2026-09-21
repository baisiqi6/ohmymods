#include <stdio.h>
#include <dlfcn.h>
#include <stdlib.h>
typedef int(*fn)(void);static int replacement(void){return 7777;}
int main(int n,char**v){setvbuf(stdout,0,_IONBF,0);void*d=dlopen(v[1],2),*t=dlopen(v[2],2);if(!d||!t){puts(dlerror());return 2;}int(*prep)(void*,void*,void**)=dlsym(d,"DobbyPrepare");int(*commit)(void*)=dlsym(d,"DobbyCommit");int(*destroy)(void*)=dlsym(d,"DobbyDestroy");fn f[512],orig[512];for(int i=0;i<512;i++){char s[40];sprintf(s,"pressure_target%d",i);f[i]=dlsym(t,s);if(!f[i]||f[i]()!=i)return 3;}for(int round=0;round<3;round++){for(int i=0;i<512;i++)if(prep(f[i],replacement,(void**)&orig[i])||commit(f[i]))return 4;for(int i=0;i<512;i++)if(f[i]()!=7777||orig[i]()!=i){printf("FAIL index=%d round=%d\n",i,round);return 5;}for(int i=511;i>=0;i--)if(destroy(f[i])||f[i]()!=i)return 6;printf("round=%d targets=512 simultaneous original/restored=PASS\n",round+1);}puts("RESULT=PASS installs=1536");return 0;}
