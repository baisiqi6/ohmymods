using System;
using UnityEngine;
using KingdomEnhancedMod;

internal static class Program
{
    private static int passed;
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--metrics") { MovementMetrics(args.Length>1 ? float.Parse(args[1],System.Globalization.CultureInfo.InvariantCulture) : 0.9f); return 0; }
            InvalidInputs();
            SyntheticGeometry();
            AttachmentBridge();
            RuntimeGeometry();
            ActualRunStop();
            Console.WriteLine("ALL SCARF GEOMETRY TESTS PASSED (" + passed + ")");
            return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }

    private static void InvalidInputs()
    {
        var x = new float[8]; var y = new float[8];
        for (int i=0;i<8;i++) y[i]=-i/10f;
        Check(!HeroArcherScarfGeometry.TrySegment(null,y,0,0,0,out _), "null rejected");
        Check(!HeroArcherScarfGeometry.TrySegment(new float[2],y,0,0,0,out _), "short array rejected");
        Check(!HeroArcherScarfGeometry.TrySegment(x,y,-1,0,0,out _) && !HeroArcherScarfGeometry.TrySegment(x,y,7,0,0,out _), "invalid segment rejected");
        Check(!HeroArcherScarfGeometry.TrySegment(x,y,0,2,0,out _), "invalid ribbon rejected");
        x[0]=float.NaN;
        Check(!HeroArcherScarfGeometry.TrySegment(x,y,0,0,0,out _), "NaN position rejected");
        x[0]=float.PositiveInfinity;
        Check(!HeroArcherScarfGeometry.TrySegment(x,y,0,0,0,out _), "infinite position rejected");
        x[0]=11f;
        Check(!HeroArcherScarfGeometry.TrySegment(x,y,0,0,0,out _), "out of bounds position rejected");
        x[0]=0;
        Check(HeroArcherScarfGeometry.TrySegment(x,y,0,0,float.NaN,out var a)
            && HeroArcherScarfGeometry.TrySegment(x,y,0,0,0,out var b) && a.StartOuter.X==b.StartOuter.X, "invalid clock uses fixed initial fold");
        y[1]=y[0];
        Check(!HeroArcherScarfGeometry.TrySegment(x,y,0,0,0,out _), "collapsed segment rejected without invented floor movement");
    }

    private static void SyntheticGeometry()
    {
        var x = new float[8]; var y = new float[8];
        bool valid=true, widths=true, faces=true, envelope=true, varied=false, distinct=false;
        float minBody=100,maxBody=0,minTip=100,maxTip=0;
        // Every heading and every sub-pixel grid phase, not only the simulator's usual down/back direction.
        for (int heading=0; heading<360;heading+=3)
        for (int fraction=0;fraction<8;fraction++)
        {
            double angle=heading*Math.PI/180;
            for (int i=0;i<8;i++) { x[i]=(float)(i*3.3*Math.Cos(angle)+fraction/8.0)/32; y[i]=(float)(i*3.3*Math.Sin(angle)+fraction/8.0)/32; }
            for (int ribbon=0;ribbon<2;ribbon++)
            for (int segment=0;segment<7;segment++)
            {
                valid &= HeroArcherScarfGeometry.TrySegment(x,y,segment,ribbon,heading/10f,out var g);
                float w=Distance(g.StartOuter,g.StartInner)*32;
                widths &= segment<5 ? w>=3 && w<=4 : w>=2 && w<=3;
                faces &= Distance(g.StartOuter,g.StartFold)*32>=1 && Distance(g.StartFold,g.StartInner)*32>=1
                    && Area(g.StartOuter,g.StartFold,g.EndOuter)>0 && Area(g.EndOuter,g.StartFold,g.EndFold)>0
                    && Area(g.StartFold,g.StartInner,g.EndFold)>0 && Area(g.EndFold,g.StartInner,g.EndInner)>0;
                float integerGround=(float)Math.Floor(y[segment]*32-HeroArcherClothMath.HalfWidthUnits(segment)*32)/32;
                envelope &= g.StartOuter.Y>=integerGround-1e-6 && g.StartInner.Y>=integerGround-1e-6;
                if (segment==0) { minBody=Math.Min(minBody,w);maxBody=Math.Max(maxBody,w); }
                if (segment==6) { minTip=Math.Min(minTip,w);maxTip=Math.Max(maxTip,w); }
            }
        }
        Check(valid,"all 120 headings and 8 pixel phases form valid geometry");
        Check(widths,"body stays 3–4px and tail 2–3px across headings");
        Check(faces,"both faces retain cardinal Manhattan width >=1 source pixel and positive triangle areas");
        Check(envelope,"actual half width plus integer ground keeps rounded lowest edges above ground");
        varied=minBody<maxBody && minTip<maxTip;
        Check(varied,"slow fold changes both body and tail width within bounds");
        for(int i=0;i<8;i++) { x[i]=-i*3.3f/32; y[i]=-i/32f; }
        for(int frame=0;frame<300;frame++)
        {
            HeroArcherScarfGeometry.TrySegment(x,y,2,0,frame/30f,out var a);
            HeroArcherScarfGeometry.TrySegment(x,y,2,1,frame/30f,out var b);
            distinct |= a.StartOuter.Y!=b.StartOuter.Y || a.StartFold.Y!=b.StartFold.Y;
        }
        Check(distinct,"two chains have different fold timing on identical centerlines");
        for(int warm=0;warm<100;warm++) HeroArcherScarfGeometry.TrySegment(x,y,1,0,warm,out _);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++) HeroArcherScarfGeometry.TrySegment(x,y,i%7,i%2,i/60f,out _,attachmentBridge:i%2==1);
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"pure geometry allocates zero bytes after warmup");
    }

    private static void AttachmentBridge()
    {
        var x=new float[8];var y=new float[8];var savedX=new float[8];var savedY=new float[8];
        bool finite=true, nondegenerate=true, unmodified=true, centers=true, safe=true, defaultUnchanged=true;
        for(int heading=0;heading<360;heading+=3)
        {
            double angle=heading*Math.PI/180;
            for(int node=0;node<8;node++) { x[node]=savedX[node]=(float)(node*(22.0/7)*Math.Cos(angle))/32; y[node]=savedY[node]=(float)(node*(22.0/7)*Math.Sin(angle))/32; }
            for(int segment=0;segment<2;segment++)
            {
                bool ok=HeroArcherScarfGeometry.TrySegment(x,y,segment,1,heading/30f,out var g,attachmentBridge:true);
                finite &= ok;
                if(!ok)continue;
                nondegenerate &= Area(g.StartOuter,g.StartFold,g.EndOuter)>0 && Area(g.EndOuter,g.StartFold,g.EndFold)>0
                    && Area(g.StartFold,g.StartInner,g.EndFold)>0 && Area(g.EndFold,g.StartInner,g.EndInner)>0;
                float expectedStart=y[segment]*32-(segment==0?2:1);
                float expectedEnd=y[segment+1]*32-(segment==0?1:0);
                centers &= Math.Abs((g.StartOuter.Y+g.StartInner.Y)*16-expectedStart)<=0.50001
                    && Math.Abs((g.EndOuter.Y+g.EndInner.Y)*16-expectedEnd)<=0.50001;
                safe &= Math.Min(Math.Min(g.StartOuter.Y,g.StartInner.Y),Math.Min(g.EndOuter.Y,g.EndInner.Y))>-17f/32;
                HeroArcherScarfGeometry.TrySegment(x,y,segment,1,heading/30f,out var normal);
                defaultUnchanged &= Math.Abs((normal.StartOuter.Y+normal.StartInner.Y)*16-y[segment]*32)<=0.50001;
            }
            for(int node=0;node<8;node++)unmodified &= x[node]==savedX[node] && y[node]==savedY[node];
        }
        Check(finite && nondegenerate,"secondary attachment bridge has no collapsed or inverted face across 120 headings");
        Check(centers,"bridge lowers only drawn node0 by2px and node1 by1px within pixel rounding");
        Check(safe,"bridged root sections remain well above the actual secondary -17px ground");
        Check(unmodified && defaultUnchanged,"bridge never writes simulation inputs and generic geometry stays unbridged by default");
    }

    private static void RuntimeGeometry()
    {
        var parent=new GameObject("hero"); var body=new GameObject("body"); body.transform.SetParent(parent.transform,false);
        var reference=body.AddComponent<SpriteRenderer>(); reference.Initialize(Color.white,false,1,5,new Material(Shader.Find("Sprites/Default")));
        var h=HeroArcherCloth.Create(parent.transform,reference);
        Check(h!=null,"runtime creation succeeds");
        var verts0=h.Vertices[0]; var colors0=h.Meshes[0].colors; var indices0=h.Meshes[0].triangles;
        bool grid=true, floor=true, triangle=true, flat=true, thickness=true, narrowFold=true, palette=true, neckConnected=true, bridgeValid=true;
        int checkedFrames=0;
        for (int frame=0;frame<2400;frame++)
        {
            int stage=frame/200;
            float velocity=stage%4==0 ? 0 : stage%4==1 ? 1.3f : stage%4==2 ? 3.5f : -1.3f;
            if(frame%173==0) HeroArcherCloth.SetWind(h,(frame%346==0 ? -1 : 1));
            HeroArcherCloth.Tick(h,1f/60,velocity,true);
            for(int chain=0;chain<2;chain++)
            {
                var vertices=h.Vertices[chain]; var colors=h.Meshes[chain].colors; var indices=h.Meshes[chain].triangles;
                float ground=chain==0 ? -14/32f : -17/32f;
                for(int i=0;i<vertices.Length;i++)
                {
                    var v=vertices[i];grid &= v.x*32==Math.Round(v.x*32) && v.y*32==Math.Round(v.y*32);
                    floor &= v.y>=ground-1e-6;
                    var c=colors[i];palette &= c.r>c.g*3 && c.r>c.b*2 && c.a==1 && c.r<=228/255f;
                }
                for(int face=0;face<14;face++)
                {
                    int o=face*4;var a=vertices[o];var b=vertices[o+1];var c=vertices[o+2];var d=vertices[o+3];
                    float first=Area(a,b,c),second=Area(c,b,d);
                    triangle &= first>0 && second>0 && first<=24f/1024 && second<=24f/1024;
                    thickness &= Distance(a,b)>=1f/32-1e-6 && Distance(c,d)>=1f/32-1e-6;
                    for(int corner=1;corner<4;corner++) flat &= SameColor(colors[o],colors[o+corner]);
                    if(face%2==1) narrowFold &= Distance(a,b)<=2f/32+1e-6;
                }
                for(int i=0;i<indices.Length;i++) triangle &= indices[i]>=0 && indices[i]<vertices.Length;
            }
            var secondary=h.Vertices[1];
            float visibleRootY=(secondary[0].y+secondary[5].y)*0.5f+h.RootTransform.localPosition.y+h.Renderers[1].transform.localPosition.y;
            neckConnected &= Math.Abs(visibleRootY*32-15)<=0.50001;
            for(int segment=0;segment<2;segment++)bridgeValid &= HeroArcherScarfGeometry.TrySegment(h.Chains[1].PointsX,h.Chains[1].PointsY,segment,1,h.WindClock,out _,attachmentBridge:true);
            checkedFrames++;
        }
        Check(checkedFrames==2400 && grid,"all mesh vertices stay on pixel grid through 40s movement and wind");
        Check(floor,"lowest rendered edges remain above each chain's actual ground");
        Check(neckConnected && bridgeValid,"actual secondary visible root meets neck top at15px with valid bridge faces every frame");
        var secondaryMain=h.Meshes[1].colors[0];var secondaryFold=h.Meshes[1].colors[4];
        Check(secondaryMain.r==228/255f && secondaryMain.g==57/255f && secondaryMain.b==35/255f
            && secondaryFold.r==137/255f && secondaryFold.g==18/255f && secondaryFold.b==29/255f,
            "secondary actual faces use stable bright fire red and shade red to distinguish the two tails");
        Check(triangle,"all runtime triangles nondegenerate, same winding, bounded area, valid indices");
        Check(thickness && narrowFold,"each face cardinal Manhattan width >=1 source pixel and folded edge stays small");
        Check(flat && palette,"each runtime quad has one fixed red palette color without interpolation");
        Check(ReferenceEquals(verts0,h.Vertices[0]) && ReferenceEquals(colors0,h.Meshes[0].colors)
            && ReferenceEquals(indices0,h.Meshes[0].triangles),"vertex/color/index buffers reused for full run");
        var snapshot=new Vector3[verts0.Length];for(int i=0;i<snapshot.Length;i++)snapshot[i]=verts0[i];
        float clock=h.WindClock;float sim=h.Chains[0].PointsX[7];
        for(int i=0;i<90;i++)HeroArcherCloth.Tick(h,1f/60,3.5f,false);
        bool frozen=h.WindClock==clock && h.Chains[0].PointsX[7]==sim;
        for(int i=0;i<snapshot.Length;i++)frozen &= snapshot[i].x==verts0[i].x && snapshot[i].y==verts0[i].y;
        Check(frozen,"hidden freezes fold clock, simulation and all mesh faces");
        var material=h.Renderers[0].sharedMaterial; HeroArcherCloth.Destroy(h);
        Check(h.Vertices[0]==null && !material.destroyed,"destroy returns mesh buffers while retaining shared material");
    }
    private static void ActualRunStop()
    {
        float runEdgeMin=100, maxStep=0, maxComponentStep=0, stopClearanceMax=0;string stepAt="";
        bool safe=true;
        foreach(float speed in new[]{1.2f,1.6f})
        for(int phase=0;phase<24;phase++)
        {
            var parent=new GameObject("phaseHero");var body=new GameObject("phaseBody");body.transform.SetParent(parent.transform,false);
            var reference=body.AddComponent<SpriteRenderer>();reference.Initialize(Color.white,false,1,5,new Material(Shader.Find("Sprites/Default")));
            var h=HeroArcherCloth.Create(parent.transform,reference);
            for(int idle=0;idle<phase*30;idle++)HeroArcherCloth.Tick(h,1f/60,0,true);
            var px=new float[16];var py=new float[16];
            for(int c=0;c<2;c++)for(int n=0;n<8;n++){px[c*8+n]=h.Chains[c].PointsX[n];py[c*8+n]=h.Chains[c].PointsY[n];}
            for(int frame=0;frame<540;frame++)
            {
                HeroArcherCloth.Tick(h,1f/60,frame<360?speed:0,true);
                for(int c=0;c<2;c++)
                {
                    var chain=h.Chains[c];var mesh=h.Meshes[c];
                    float worldAnchor=h.RootTransform.localPosition.y+h.Renderers[c].transform.localPosition.y;
                    for(int n=1;n<8;n++)
                    {
                        float dx=chain.PointsX[n]-px[c*8+n],dy=chain.PointsY[n]-py[c*8+n];
                        maxComponentStep=Math.Max(maxComponentStep,Math.Max(Math.Abs(dx),Math.Abs(dy))*32);
                        float delta=(float)Math.Sqrt(dx*dx+dy*dy)*32;
                        if(delta>maxStep){maxStep=delta;stepAt=$"speed={speed} phase={phase} frame={frame} chain={c} node={n}";}
                        px[c*8+n]=chain.PointsX[n];py[c*8+n]=chain.PointsY[n];
                    }
                    for(int v=0;v<mesh.vertices.Length;v++)
                    {
                        float above=(mesh.vertices[v].y+worldAnchor)*32;
                        safe &= above>=-1e-5;
                        if(frame>=149 && frame<360)runEdgeMin=Math.Min(runEdgeMin,above);
                    }
                    if(frame==539)stopClearanceMax=Math.Max(stopClearanceMax,(chain.PointsY[7]-chain.FloorY)*32);
                }
            }
            HeroArcherCloth.Destroy(h);
        }
        Console.WriteLine($"actual-view 24 phases speeds1.2/1.6 runEdgeMinPx={runEdgeMin:F3} maxSimulationStepPx={maxStep:F4} maxComponentStepPx={maxComponentStep:F4} stop3sTipClearanceMaxPx={stopClearanceMax:F4} worstAt={stepAt}");
        Check(safe,"actual view remains above ground through start, run and stop in every sampled phase");
        Check(runEdgeMin>=2,"both actual wide ribbons visibly clear ground at 1.2 and 1.6 steady run");
        Check(maxComponentStep<=2,"actual configured chains keep per-axis startup and stop simulation steps below 2px");
        Check(stopClearanceMax<=3,"actual configured tails settle near ground within three seconds after stop");
    }

    private static void MovementMetrics(float secondaryDrag)
    {
        Console.WriteLine("secondaryDrag="+secondaryDrag);
        Console.WriteLine("halfWidthPx="+HeroArcherClothMath.HalfWidthUnits(0)*32+"/"+HeroArcherClothMath.HalfWidthUnits(7)*32);
        foreach (float ground in new[]{-14f,-13f,-11f,-17f})
        foreach (float speed in new[]{0.65f,1.2f,1.6f})
        {
            bool primary=ground==-14;
            float worstClear=float.MaxValue,bestClear=float.MinValue,worstRender=float.MaxValue,meanDrop=0,meanExt=0;
            int contacts=0,samples=0;
            for(int phase=0;phase<24;phase++)
            {
                var chain=new HeroArcherClothChain((primary?24f:22f)/32,primary?0:1.1f,primary?1:secondaryDrag,primary?1:1.15f,ground/32);
                float phaseMin=float.MaxValue;
                for(int frame=0;frame<360;frame++)
                {
                    float clock=phase*0.5f+(frame+1)/60f;
                    chain.Step(1f/60,speed,clock);
                    if(frame<149)continue;
                    float clearance=(chain.PointsY[7]-chain.FloorY)*32;
                    phaseMin=Math.Min(phaseMin,clearance);
                    if(clearance<0.5f)contacts++;
                    meanDrop-=chain.PointsY[7]*32;meanExt-=chain.PointsX[7]*32;samples++;
                    HeroArcherScarfGeometry.TrySegment(chain.PointsX,chain.PointsY,6,primary?0:1,clock,out var g);
                    worstRender=Math.Min(worstRender,Math.Min(g.EndOuter.Y,g.EndInner.Y)*32-ground);
                }
                worstClear=Math.Min(worstClear,phaseMin);bestClear=Math.Max(bestClear,phaseMin);
            }
            Console.WriteLine($"ground={ground} speed={speed} phases=24 minClearRangePx={worstClear:F5}..{bestClear:F5} renderBottomMinPx={worstRender:F3} nearContactPct={contacts*100f/samples:F2} meanDropPx={meanDrop/samples:F4} meanExtPx={meanExt/samples:F4}");
        }
        var walkSamples=new float[24]; var runSamples=new float[24];
        foreach(float speed in new[]{0.65f,1.6f})
        {
            float low=float.MaxValue,high=float.MinValue,sum=0;
            for(int phase=0;phase<24;phase++)
            {
                var c=new HeroArcherClothChain(24f/32,0,1,1,-14f/32);
                for(int f=0;f<240;f++)c.Step(1f/60,speed,phase*0.5f+(f+1)/60f);
                float v=0;for(int n=1;n<=4;n++)v-=c.PointsX[n]/4;
                low=Math.Min(low,v);high=Math.Max(high,v);sum+=v;
                (speed<1 ? walkSamples : runSamples)[phase]=v;
            }
            Console.WriteLine($"airborneMeanX speed={speed} phases=24 min={low:F7} max={high:F7} average={sum/24:F7}");
        }
        float minDifference=float.MaxValue,maxDifference=float.MinValue;
        for(int phase=0;phase<24;phase++) { float difference=runSamples[phase]-walkSamples[phase];minDifference=Math.Min(minDifference,difference);maxDifference=Math.Max(maxDifference,difference); }
        Console.WriteLine($"matchedPhaseRunWalkDifference phases=24 min={minDifference:F7} max={maxDifference:F7}");
    }

    private static float Distance(HeroArcherScarfGeometry.Point a,HeroArcherScarfGeometry.Point b)=>Math.Abs(a.X-b.X)+Math.Abs(a.Y-b.Y);
    private static float Area(HeroArcherScarfGeometry.Point a,HeroArcherScarfGeometry.Point b,HeroArcherScarfGeometry.Point c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    private static float Distance(Vector3 a,Vector3 b)=>Math.Abs(a.x-b.x)+Math.Abs(a.y-b.y);
    private static float Area(Vector3 a,Vector3 b,Vector3 c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
    private static bool SameColor(Color a,Color b)=>a.r==b.r&&a.g==b.g&&a.b==b.b&&a.a==b.a;
}
