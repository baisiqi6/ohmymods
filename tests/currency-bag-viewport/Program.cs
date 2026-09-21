using KingdomEnhancedMod;
Console.WriteLine("REAL_FADE_STATES " + string.Join(",", Enum.GetNames(typeof(CurrencyBag.FadeState))));
int tests=0;
void Check(bool result,string name){if(!result)throw new Exception(name);tests++;Console.WriteLine("PASS "+name);}
void Shift(float lo,float hi,int px,float expected,string name){bool changed=CurrencyBagViewportPolicy.TryGetShift(lo,hi,px,out float delta);Check(changed&&Math.Abs(delta-expected)<.00001f,name);}
void Unchanged(float lo,float hi,int px,string name){Check(!CurrencyBagViewportPolicy.TryGetShift(lo,hi,px,out float delta)&&delta==0f,name);}
Shift(1.095f,1.445f,1280,-.45125f,"actual 1280 window right-offscreen bag fits with 8px inset");
Shift(1.063333f,1.296667f,1920,-.30083367f,"actual 1920 fullscreen right-offscreen bag fits with 8px inset");
Unchanged(.61f,.96f,1920,"approved visible Windows position remains exact");
Unchanged(0f,1f,1280,"exact full viewport unchanged");
Unchanged(0f,.35f,1280,"visible left edge unchanged");
Unchanged(.65f,1f,1280,"visible right edge unchanged");
Shift(-.4f,-.05f,1280,.40625f,"fully left offscreen");
Shift(-.01f,.34f,1280,.01625f,"partially left");
Shift(.9f,1.25f,1280,-.25625f,"partially right");
Shift(.8f,2f,1280,-.9f,"oversized bag centers without resizing");
Unchanged(-.25f,1.25f,1280,"exactly centered oversized bag remains unchanged");
Shift(-.01f,.98f,1,.015f,"near-full-width bag reduces margin to fit narrow camera");
Shift(1.095f,1.445f,1,-.455f,"one-pixel camera padding remains bounded");
Unchanged(float.NaN,1f,1280,"NaN rejects");
Unchanged(0f,float.PositiveInfinity,1280,"infinity rejects");
Unchanged(.8f,.2f,1280,"inverted interval rejects");
Unchanged(1.2f,1.4f,0,"zero pixel width rejects");
Unchanged(1.2f,1.4f,-20,"negative pixel width rejects");
Shift(1.095f,1.445f,640,-.455f,"split camera uses own pixel width");
CurrencyBagViewportPolicy.TryGetShift(1.095f,1.445f,1280,out float correction);
Unchanged(1.095f+correction,1.445f+correction,1280,"corrected interval is idempotent");
Console.WriteLine($"RESULT {tests} viewport policy checks passed; production adapter compiled against actual interop");
