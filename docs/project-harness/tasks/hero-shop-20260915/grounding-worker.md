# Bounded shop grounding worker

北京时间2026-09-15周二20点，OMP deepseek/deepseek-v4-flash thinking=max，按用户晚间偏好。不再delegate。

项目C:/Users/ADMIN/projects/ohmymods，IL2CPP2.4。用户实际看到英雄驿站下半部被地面遮挡。当前安装f8c25095已实际owner preflight+ready+purchasecompleted，不能改回out enum。游戏运行中禁止部署/关闭/启动、禁止改用户配置存档/commit/push/reset。

你负责有界修复设计/实现 slice，允许改 il2cpp/HeroShop.cs 与 tests/hero-shop/**，写 docs/project-harness/tasks/hero-shop-20260915/grounding-worker-result.md。主agent在独立提取实际2.4原生shop sprite脚点/ground数据，稍后证据会放 operator hero-shop-20260915/grounding/resource-grounding.json；你可先分析当前错误：Create把rootY=layer.position.y，原ShopPlanner.CreateShop使用shopPrefab.transform.position.y（旧源码510行），还任意取第一个GetComponentInChildren<SpriteRenderer>的z可能取错renderer。不要直接拿任意native renderer bounds.min.y，因为旗/工具/装饰会误基准。不能靠固定抬高截图猜值。

目标最小可靠：跟随实际原生地面商店的ROOT纵向基准（同当前world、active，不取render子件Y），自己的sprite底部pivot位于底部2px；不能把所有外观/购买位置乱分离。若rootY原生也0但renderer子件有offset，则依据实际资源证据挑bodyrenderer实体groundanchor—not any first child。修改只动shop根/自有视觉必要y，不动原生对象或hero等其他功能，保持x选址/z排序/owner桥/币逻辑不变。初始化日志一次记录chosen source/rootY/bodyoffset/groundY/finalY，便于实机验收。

你先写简短diagnosis到result，证据不足明确等待operator资料，不用网络搜索或把20分钟耗在库源码。可使用本机Cecil或实际interop只读，但游戏native不得执行。现代C#，build禁自动部署。已有Core31/Invoker7/actualInterop回归必须过；新测试基准选择或局部纯计算正负/无参考/切world等必要情况，别造“所有都绿色”的实现镜像测试。

如果main给的数据证明root默认y0.88之类，按资料完成直接采样当前nativeShop.transform.position.y与固定素材脚点校正，缺参考延后创建不猜。最多8分钟本轮，三分钟内先给result中间结论。main不写你的生产allowlist。
