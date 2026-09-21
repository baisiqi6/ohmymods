#include <cstdio>
#include <cstdlib>
#include "NearGapSelection.h"
using namespace arm64_near_gap;
int checks=0;void check(bool value,const char*name){++checks;if(!value){fprintf(stderr,"FAIL %s\n",name);exit(1);}printf("PASS %s\n",name);}
int main(){uintptr_t first=0x12b73dee8,third=0x12ba0c3b8,page=0x4000;std::vector<Gap> snapshot={{0x1239f0000,0x123a10000},{0x123a30000,0x123aa4000},{0x1241a8000,0x12b19c000},{0x130010000,0x13373c000}};
 auto ranked=Rank(snapshot,first,page,16);
 check(ranked.size()==4,"all four real snapshot gaps retained");
 check(ranked[0].start==0x12b15c000&&ranked[0].end==0x12b19c000,"closest below gap selects highest 16-page block");
 check(ranked[0].nearest==0x12b198000,"ranking uses closest complete page");
 check(third-(snapshot[0].high-page)>=0x8000000,"all eight old page cursors outside third-hook reach (last by 0x3B8)");
 uintptr_t targets[]={first,0x12b79a828,0x12b798d04,third};for(auto target:targets)check(target-ranked[0].start<0x8000000,"new chosen pool reaches actual early hook");
 check(ranked[1].start==0x130010000,"above gap chooses its lowest page");
 auto small=Rank({{0x1000,0x3000}},0x9000,0x1000,16);check(small[0].start==0x1000,"below gap smaller than sixteen pages retains whole gap");
 auto above=Rank({{0xa000,0x20000}},0x9000,0x1000,16);check(above[0].start==0xa000,"single above gap starts at low edge");
 check(Rank({},0x9000,0x1000,16).empty(),"no gaps stays empty");
 check(Rank({{0x1001,0x4000},{0x8000,0x8000}},0x9000,0x1000,16).empty(),"unaligned and empty input gaps rejected");
 check(Rank(snapshot,first,0,16).empty(),"zero page size rejected");
 check(Rank(snapshot,first,page,UINTPTR_MAX).empty(),"reservation-size overflow rejected");
 printf("PASS %d deterministic gap-policy checks\n",checks);
}
