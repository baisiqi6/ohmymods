using System;
using KingdomEnhancedMod;

int failed=0, passed=0;
foreach(int chain in new[]{0,1})
foreach(float speed in new[]{.65f,1.2f,1.6f,2f})
foreach(float wind in new[]{-1f,0f,1f})
foreach(int delay in new[]{0,1,3}) {
 float clock=0;
 bool trajectorySafe=true;
 var c=new HeroArcherClothChain((chain==0?24f:22f)/32f,chain==0?0:1.1f,chain==0?1:.9f,chain==0?1:1.15f,(chain==0?-14f:-17f)/32f);
 void Step(int frames,float v) {
  for(int i=0;i<frames;i++) {
   clock+=1f/30;c.Step(1f/30,v,clock);
   for(int n=1;n<8;n++) {
    float x=c.PointsX[n],y=c.PointsY[n],dx=x-c.PointsX[n-1],dy=y-c.PointsY[n-1];
    float floor=(chain==0?-14f:-17f)/32f+HeroArcherClothMath.HalfWidthUnits(n);
    trajectorySafe&=float.IsFinite(x)&&float.IsFinite(y)&&y>=floor-1e-5f&&Math.Abs(Math.Sqrt(dx*dx+dy*dy)-c.SegmentLength)<1e-5;
   }
  }
 }
 c.SetWind(wind);Step(180,speed);
 var oldX=(float[])c.PointsX.Clone();
 var oldY=(float[])c.PointsY.Clone();
 float offset=(chain==0?10f:12f)/32f;
 c.Rebase(-1,offset);
 bool worldContinuous=true;
 for(int i=1;i<8;i++)worldContinuous&=Math.Abs(c.PointsX[i]-(-oldX[i]+offset))<1e-6f&&c.PointsY[i]==oldY[i];
 c.SetWind(-wind);Step(delay,speed);Step(600,0);
 bool restBehind=c.PointsX[7]<-.1f && c.PointsY[7]<-.25f;
 Step(120,speed);
 bool runsBehind=c.PointsX[7]<-.3f;
 bool safe=true;
 for(int i=1;i<8;i++) {
  float x=c.PointsX[i],y=c.PointsY[i],dx=x-c.PointsX[i-1],dy=y-c.PointsY[i-1];
  safe&=float.IsFinite(x)&&float.IsFinite(y)&&Math.Abs(Math.Sqrt(dx*dx+dy*dy)-c.SegmentLength)<1e-5;
 }
 if(worldContinuous&&restBehind&&runsBehind&&safe&&trajectorySafe)passed++;
 else {failed++;Console.WriteLine($"FAIL chain={chain} speed={speed} wind={wind} delay={delay} continuity={worldContinuous} restBehind={restBehind} restart={runsBehind} safe={safe} trajectory={trajectorySafe}");}
}
Console.WriteLine($"Turn-stop scenarios: {passed} passed, {failed} failed (2 chains x 4 speeds x 3 winds x 3 stop delays).");
return failed==0?0:1;
