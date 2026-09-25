角色：有界 worker，OMP deepseek/deepseek-v4-flash thinking=max。仅实现真正白色残影。
cwd C:/Users/ADMIN/projects/ohmymods-wt-choreo-fix，branch win/samurai-choreo-hard-validity，baseline67d39b9。Windows Operator按用户handoff接手既有samurai-slash-guard-20260925，Issue21 OPEN/武士PR均已合并，无其他未合并武士PR。
允许读取本树和C:/Users/ADMIN/projects/ohmymods/game-source。允许修改：il2cpp/SamuraiDashVisuals.cs（优先把小helper留此文件）；tests/samurai-visuals/{Program.cs,Stubs.cs,Regression.csproj}；本任务目录visual-worker-result.md。禁止修改PowerDash（另一worker负责）、Plugin、AGENTS、全局配置，禁止commit/push/merge/reset/clean/部署/启动游戏/接触存档/派子代理。只运行visual独立测试，不编译主项目（Operator统一编译）。
先读docs/project-harness/tasks/samurai-slash-guard-20260925/zcode-handoff.md §6以及design-review.md，按审查修正实现最小方案。
目标：每个姿态源纹理的RGB替换为255保留原alpha，构造真正白剪影Sprite供8ghost及body使用。源Sprite/texture/material/property block不得修改；尺寸/pivot/PPU/翻面/世界位置/排序/2秒线性透明度阶梯保留。实际Unity API必须与本地IL2CPP互操作程序集相容，GetPixels32现有参考在MusketeerVisuals.cs等。
读取失败用RenderTexture/Graphics.Blit/ReadPixels处理不可读texture。RT active恢复+ReleaseTemporary放finally；新Texture/Sprite/material成功与失败都要明确所有权和销毁路径。缓存明确上限与身份（至少纹理+rect，避免pivot/PPU混淆），无每帧整atlas读回；避免淘汰仍被在飞ghost引用的对象。失败限频记录真实降级，不把原色fallback写成白色成功。packed/tight/rotated sprite按审查决定处理，不盲目拿源rect裁atlas。缓存资源场景/owner清理有明确定义，不新增复杂全局设施。
测试直接验证实际ghost sprite.texture以及材质纹理指向白贴图、RGB255/逐像素alpha原值、source未改、pivot/PPU保持、姿态切换ghost冻结、共享/复用缓存、不可读GPU路径和异常归还/销毁、八槽/2秒/排序/降级日志原回归。临时退回原sprite赋值，白色像素用例必须红，再恢复变绿。
最终中文报告visual-worker-result.md，明确测试/红验证与计数、资源限制及实机待验。不要宣称stub验证证明屏幕可见。遇越界/需新权限即停止依赖工作并报告。
