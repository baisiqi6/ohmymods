# #272 Root 复核

来源：Mac / Codex / Root Operator。当前游戏基线为 2.4.0 ARM64；这里只证明源码、host 与 NoDeploy 编译，未合并、未安装、未执行新 Native。

字典 first-capture prior/last-owned 归还、未知写入责任、pending 失败和换字典/菜单重入均已复核；旧 attempt 不覆盖新数组、不将新代 poison、不删除新代拒绝记录。详情使用各岛独立贴图租约和 pager，world 预建与实际点击焦点分开；迟到点击拒绝动作但不销毁有效缓存。已删除旧通用降级、无读回回滚和单 active detail slot 补偿。

Root 实际复跑：私有 wrapper 双 TFM 275 Compile /161 references /38 assets，均 0W0E；计划与接线357/357、15个逐字抽取并hashlock的真实方法故障测试81/81、实际MapMountIcons直链UI194/194、callback4/4、缺资源3/3。真实方法测试使用假的字典/menu/数组依赖，不能代替完整 EnsureAppended 或 Unity 重入时序。旧 RuntimeProbe 1037项仍有39个失败，与干净HEAD失败多重集完全相同，不标全绿。

关键复跑（repo根；所有dotnet构建均设置 NoDeploy=true、BepInExPluginsPath为空，bin/obj/CLI缓存指向任务私有目录）：

```sh
python3 tests/mount-island-map/r2host/extract-hosted.py
KEM_SOURCE_ROOT="$PWD" dotnet run --project tests/mount-island-map/Tests.csproj
KEM_SOURCE_ROOT="$PWD" dotnet run --project tests/mount-island-map/r2host/R2Host.csproj
dotnet run --project tests/native-map-runtime/VariantProbe.csproj
```

模型事件、实际源码/产物SHA与独立审查locator见root-results.json；完整raw保留在本机任务目录。Root初次runner漏设源码根、测试fixture未清上一_generation、失败列表排序比较三项均已如实纠正，未放松生产守卫或测试断言。

A23：实际UI10→physical11结果交接成功，随后探针异常触发受控TERM；不是已证原生闪退，具体throw仍Unknown。私有A24诊断已独审通过：精确阶段、严格512/1024单行message/frames、实际node计数、complete=false原始map/catalog输入；不放宽八设施、marker、容量与身份门。观察器不进入发布源码。

下一步用结合#271的全新私有候选验证8→11、详情A/B、八设施/markers、自然SaveQuit和独立新实例冷读；普通游玩存档/配置/DLL和Steam未写，失败A23后像不能冒充合格cold父样本。Windows2.4.3、Android设备及联机未由本证据验收。
