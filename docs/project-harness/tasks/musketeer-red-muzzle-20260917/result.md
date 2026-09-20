# 五帧红色枪口喷焰：预览候选

用户从侧边任务转交红色短促喷焰需求，随后明确“开火帧短，四五帧我看看”。最终采用五帧各50ms：点燃、喷出、拉长、收束、余焰；火焰可见从约0.1秒延长至0.25秒，但原有Fire总时长本来就是0.25秒，射速/装填/冷却/弹丸/伤害/范围不变。

基线已装96566DEF（白天补货版）。预览候选BCA6AAEBC09BA17487B3802CAD5C95C845788E7A3FD8E7566A0B2FE7716BC5ED，build=9.0.0-musketeer-red-muzzle-20260917，公开版本9.0.0。用户确认采用后，本轮已闭游戏备份安装五帧红焰BCA6AAEB；28份存档/附加档/配置hash保持，详见receipts/install.json。保留白天补货/火枪0.9/伤害可靠性。不启动游戏、不改存档配置、不提交发布。

## 素材与接线

沿现有可编辑像素绘制source与用户已授权的本地素材工作流，root为美术预览绘制固定像素图案，生成脚本为artifacts/musketeer/20260917-red-muzzle/draw_preview.py。最终使用已向用户展示的五帧红焰.gif与对应图集；OMP worker的38KB备选生成器只保留在隔离work，未执行、未成为候选来源。不是用新模型重新绘制人物，也没有运行时程序特效对象。

图集尺寸仍672x192，12列、每格56x32、PPU32、pivot31/2与AppearanceScale0.9保持；有效帧66→67。Fire36..40各.05；Reload41、Lower53、Retreat59，其他clip的时长/holds/loop完全保持。新增第40帧复用原39身体/枪/手/脚与anchor。

new0..35与old0..35像素全同，new41..66逐帧等于old40..65。五个Fire身体分别来自old36/37/38/39/39：前2帧只在x48..54/y12..17清除原十字火光并画新焰，保护x<=47；后3帧保留原枪末端x48，从x49开始画新焰，保护x<=48。x55透明；二值alpha；所有非目标区域逐像素保持。只有一套火焰轮廓，不叠旧橙黄十字。喷焰随自有sprite左右翻转和0.9缩放，在真正NotifyShot驱动的Fire窗口显示，无额外射击/伤害入口。

## 验证与边界

- 79项runtime回归通过，真实2.4接口编译及完整Debug构建0警告0错误（禁用构建自动部署）。
- 67个anchor逐个与新atlas.json相等，9套clip的首帧/holds/count/duration核对一致；所有动作总时长保持。
- root逐像素保护验证与新旧/左右/小尺寸预览目视完成。动图仅素材预览，非游戏录像。
- DLL对96566DEF：3657方法体同、5改、无新增/删除（Plugin.Init、atlas表与常量传播相关），Harmony类型集同；仅MusketeerAtlas.png资源更换，另7张PNG全同。源仅Plugin build、MusketeerAnimation帧表和Atlas修改。
- 新图集SHA256 9D7B39C23B314CABBC03274729BF4B40492A6F3A29B7ECB8D2D0E9EBB728305B。候选与更新说明位于本机operator/musketeer-red-muzzle-20260917/candidate。独立复核见review.md。

OMP DeepSeek Flash max按夜间worker偏好执行，file-only。用户追加5帧时，root核实后仅停止旧两帧任务的omp PID42088，恢复同session按新契约；其他OMP进程未操作。worker交付运行时帧表/anchor及既有测试适配，root整合、像素美术与验证。真正游戏中观感/低帧率采样/左右出膛对齐仍待实机；未把静态预览宣称为游戏验证。
