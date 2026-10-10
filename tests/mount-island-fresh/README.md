# 坐骑双岛：只读完成证据状态机、旧/新请求代次隔离和授予提交

`dotnet run --project tests/mount-island-fresh/Tests.csproj -c Debug`。直接 Compile 链接 il2cpp 生产纯逻辑，不使用实现镜像。

不启动游戏、不写存档、不部署；这些测试不证明 Native bridge、首次生成/森林、航行、实际 UI 点击或保存冷读。

可选原生枚举交叉核验：设置 KEM_GAME_INTEROP 指向本机匹配游戏的 Assembly-CSharp.dll；不设置时明确跳过。
