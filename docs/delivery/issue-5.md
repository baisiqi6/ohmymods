> **Agent provenance:** `Mac Max / Codex` · role=`Maintainer Operator` · acting_for=`ohmymods Mac Operator`

# 钱袋视口候选验收记录

关联 [Issue #5](https://github.com/baisiqi6/ohmymods/issues/5)。基线v9.5.12 / 3b9653156c384375380906569bc3877db8a1a3c2；9个代码/测试文件与此前已审候选逐字节一致，见 [source manifest](issue-5-source.json)。

## 行为与验证

保留原布局，当sprite投影超出owning camera视口时最小平移；X/Y独立计算，Y仅活动且FadingIn/Opening/Open状态生效。隐藏、淡出、无效相机或退化bounds不被强拉回显示；不改金币/缩放/淡入状态。

2026-09-21本PR独立工作树复跑：policy20、adapter35、Greek/StartShow接线10，65项全部通过。policy同时对实际interop编译production adapter。SDK8命令：

```sh
dotnet run --project tests/currency-bag-viewport/Regression.csproj -c Release \
  -p:GameInteropDir=/path/to/interop -p:BepInExCoreDir=/path/to/core
dotnet run --project tests/currency-bag-viewport/adapter/AdapterRegression.csproj -c Release
dotnet run --project tests/greek-scale-adapters/Regression.csproj -c Release
```

2026-09-20前序Mac ARM64候选：窗口钱袋X投影从[1.095,1.445]校正为[.64375,.99375]，窗口/全屏切换完整可见，用户确认水平问题修好。随后增加Y校正，用户授权但现场未自然出现纵向越界；顶/底/角落由受控测试覆盖，不冒称实机见过纵向故障并完成修复验收。P2隐藏/玩家inactive时Y保持，重复P1不漂移。

此前v9.4.5钱袋候选完整构建0W0E；后续v9.5.12+correctness+wallet组合对Mac/Windows实际interop完整构建0W0E。这些是历史组合证据，不是此独立PR的新完整build。

## 待验收

最终整合候选的Mac ARM64、Mac x64、Windows实机分别记录；多相机/分屏、其它钱袋类型、真正纵向越界、窗口尺寸变化与隐藏动画仍保留检查范围。此PR不安装、不发布、不改存档。
