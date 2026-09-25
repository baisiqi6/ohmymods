# 本机 API 核验

2026-09-25 Operator 使用 ilspycmd 只读反编译 E 盘 2.4 interop/UnityEngine.CoreModule.dll，输出在当前Codex任务work/UnityEngine.*.cs。确认 Sprite.Create(Texture2D,Rect,Vector2,float,uint,SpriteMeshType)、textureRect、textureRectOffset、packed、packingRotation、vertices/triangles/uv 均存在；Texture2D.GetPixels32 返回Il2CppStructArray<Color32>，SetPixels32同类型；ReadPixels(Rect,int,int,bool)、Apply(bool,bool)及RGBA32构造存在；Graphics.Blit(Texture,RenderTexture,Vector2,Vector2)与RenderTexture.active/GetTemporary/ReleaseTemporary可用。此仅证明API存在，编译与运行时GPU读回仍需各自验证。

E候选基线：MD5 97030311ED804CC2D703C5F5DEDB105A；SHA256 3A5D37068A4785A23EBFD718D338C29150665195315CCD697232A121A77BE12B；DLL内嵌build=9.14.24-choreo-20260925；mtime 2026-09-25 02:39。handoff的短hash是MD5，不是SHA256。

补充真实资源证据：UnityPy解析真实SpriteAtlas而非旧Sprite.m_RD，knight_charge_bamboo_0 mesh=7顶点/5三角形，pivotPixels=(14.35,0)、PPU32、atlas2048。UV由serialized uvTransform=(32,1482.3499756,32,170)推导（非runtime捕获），存native-charge-fixture.json。独立凸多边形像素中心掩膜+实际atlas偏移1468,170得到245非透明目标像素，矩形内另46个非透明像素位于角色网格外，证明单纯裁矩形会串入邻居。期望alpha见native-charge-expected.json。
