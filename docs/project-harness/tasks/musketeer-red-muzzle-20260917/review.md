# 五帧红色枪口喷焰独立复核

**结论：作为供用户查看的预览候选 PASS，无技术阻断项。** 精确 DLL：`BCA6AAEBC09BA17487B3802CAD5C95C845788E7A3FD8E7566A0B2FE7716BC5ED`；新图集：`9D7B39C23B314CABBC03274729BF4B40492A6F3A29B7ECB8D2D0E9EBB728305B`。本轮不安装，已装基线仍为 `96566DEF…`。技术复核不替代用户对动图外观的确认。

## 素材与像素

- 审查了实际来源 `artifacts/musketeer/20260917-red-muzzle/draw_preview.py`。脚本只使用标准库与 Pillow，读取冻结 baseline 和旧 manifest，写本目录素材/预览/验证数据；没有游戏、用户档、网络或命令执行操作。未采用或执行 worker 的备用生成器；reviewer 也未重跑绘制脚本。
- 独立读取新旧 PNG 重算所有 RGBA：尺寸均为 672×192；旧图 SHA256 为 `0E22B8E1A8E8B1CB51F2DA6C4364EED9DD3BE0E33910FB25C386C773B3523364`。新 0..35 与旧同帧完全一致；新 41..66 分别与旧 40..65 完全一致；新空白槽 67..71 与旧相应空白槽一致。
- Fire 36..40 身体基底分别对应旧 36/37/38/39/39。相对这些基底，改变像素数分别为 **11、17、11、6、2**，全部局限于指定 ROI：前两帧 x48..54、后三帧 x49..54，y12..17。其他 RGBA 完全保持，因此前三/后两不同枪管末端均受保护；x55 全透明。新图 alpha 只有 0/255，五帧互不相同，没有旧闪光叠层。
- 目视检查了五帧左右对照及 2×小图：可辨点燃、喷出、拉长、收束、余焰，暖亮根部连接枪口，红色轮廓前端变细；没有新增光圈、模糊或持续光束。GIF 数据为五个 50ms 帧，加一个无焰 550ms 停留帧；标明素材预览、非游戏录像。此处是静态帧与 GIF 数据复核，不宣称实机播放验收。

## 动作表与产物

- 独立把运行时 **67 个 anchor** 逐项与 atlas.json 比较，全部一致；九个 clip 的 holds 也逐项一致。Fire 为 36..40、每帧 .05、总 .25；Reload 起点 41、Lower 53、Retreat 59，新增 anchor40 等于旧39。其他动作的时长与循环规则保持；FireSeconds=.25、ReloadSeconds=1.4 和 AppearanceScale=.9 保持。
- 新素材 PNG 与生产 Assets/MusketeerAtlas.png 完全相同，候选 DLL 内嵌图集字节也与该素材相同。
- 独立重算源文件差异与 hash：生产仅 Plugin build 标记、MusketeerAnimation 表和 MusketeerAtlas PNG 三项改变，全部匹配 source-delta receipt。
- 独立 Mono.Cecil 比较精确 before.dll / candidate：**3657 方法保持、5 改变、无新增或移除**；改变为 Atlas.FrameToCell、Atlas 静态初始化、Visuals.DecodeAtlas、Visuals.SetAtlasForTests 和 Plugin.Init，包含帧数常量传播。Harmony 类型不变，只有 MusketeerAtlas 资源改变，其他 **7 张 PNG 字节一致**。战斗、伤害、出膛时机、装填/射速逻辑和白天补货未改。
- 已读取 root 的 **79 项 runtime 通过**日志，实际 2.4 interop 和完整构建均 **0 warnings / 0 errors**。reviewer 未重复执行这些套件。

本次仅审图、只读计算像素/表/DLL并写审核证据，没有安装或启动游戏，没有保存、修改用户配置、提交或发布。游戏中实际观感和低帧下五帧能否逐帧可见尚未实测；总动画时长保持 .25 秒，不保证任何帧率下每张素材都会显示一次。
