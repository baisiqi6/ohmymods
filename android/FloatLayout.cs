using System;
namespace OhMyMods.AndroidProbe;
internal sealed class FloatLayout
{
 public float Scale => Math.Clamp(Math.Min(Width,Height)/720f,.35f,2f);
 public float Diameter => 48*Scale;
 public float TouchSize => 72*Scale;
 public float PanelWidth => Math.Min(280*Scale,Math.Max(16,Width-8*Scale));
 public float PanelHeight => Math.Min((VegetationPage?406:PopulationPage?376:PlayerPage?562:WorldPage?640:GenerationPage?250:562)*Scale,Math.Max(16,Height-8*Scale));
 public float X { get; private set; } = 36;
 public float Y { get; private set; }
 public bool Expanded { get; set; }
 public bool PopulationPage {get;set;}
 public bool PlayerPage {get;set;}
 public bool WorldPage {get;set;}
 public bool GenerationPage {get;set;}
 public bool VegetationPage {get;set;}
 public bool Captured { get; private set; }
 public bool Dragged { get; private set; }
 public float Width { get; private set; }
 public float Height { get; private set; }
 private float startX, startY, downX, downY;
 public void Resize(float width, float height)
 {
  if (width == Width && height == Height) return;
  float fy = Height > 0 ? Y / Height : .22f;
  bool right = Width > 0 && X > Width / 2;
  Width = Math.Max(40, width); Height = Math.Max(40, height);
  X = right ? Width - TouchSize / 2 : TouchSize / 2;
  Y = Clamp(fy * Height, TouchSize / 2, Height - TouchSize / 2);
  Captured = false; Dragged = false;
 }
 public bool HitBall(float x, float y) => Math.Abs(x-X) <= TouchSize/2 && Math.Abs(y-Y) <= TouchSize/2;
 public float PanelX => Clamp(X < Width/2 ? X+Diameter/2+5*Scale : X-Diameter/2-5*Scale-PanelWidth, 4, Math.Max(4, Width-PanelWidth-4));
 public float PanelY => Clamp(Y-PanelHeight/2, 4, Math.Max(4, Height-PanelHeight-4));
 public bool HitPanel(float x, float y) => Expanded && x >= PanelX && x <= PanelX+PanelWidth && y >= PanelY && y <= PanelY+PanelHeight;
 public bool Begin(float x, float y)
 {
  if (!HitBall(x,y)) return false;
  Captured = true; Dragged = false; downX=x; downY=y; startX=X; startY=Y; return true;
 }
 public void Move(float x, float y)
 {
  if (!Captured) return;
  float dx=x-downX, dy=y-downY;
  if (dx*dx+dy*dy >= 100*Scale*Scale) Dragged=true;
  if (!Dragged) return;
  X=Clamp(startX+dx,TouchSize/2,Width-TouchSize/2);
  Y=Clamp(startY+dy,TouchSize/2,Height-TouchSize/2);
 }
 public bool End(float x, float y)
 {
  if (!Captured) return false;
  Move(x,y); Captured=false;
  bool clicked=!Dragged;
  if (clicked) Expanded=!Expanded;
  X=X < Width/2 ? TouchSize/2 : Width-TouchSize/2;
  return clicked;
 }
 public void Cancel() { Captured=false; Dragged=false; }
 private static float Clamp(float v,float lo,float hi) => Math.Min(hi,Math.Max(lo,v));
}
