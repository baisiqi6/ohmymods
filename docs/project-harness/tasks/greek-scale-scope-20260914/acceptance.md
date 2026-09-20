# 仅希腊自定义缩放：本机交付

自定义缩放统一按当前世界判断：希腊使用原设定，移植到希腊的其他世界角色同样生效；其他世界只归还本补丁实际写入前的原值，不统一强写1。未知加载期保留请求及所有权，关闭/重新开启、世界往返、池复用、暂停Mover、客户端样式同步、外部写入与异常重试均纳入处理。银行助手和弩矢模板中性、实例应用；钱袋按实例基线，金币仅在证明由本补丁容器缩放导致继承偏差时登记间接写入并恢复。

68核心/角色直链、9钱袋直链、22猫（含于13组既有回归）及其余12组既有回归全通过；build0W0E；实际358Unity方法未见unstripping stub；独立审查覆盖原26个setter方法。DLL旧1329方法中1293完全一致，35修改、1删除（旧全局钱袋基准产生的cctor）、48新增，合计1376；原生Harmony发现属性与目标不变。健康Mover事务值类型无每帧事务对象分配；失败缓存重试退避30帧。

精确DLL 494A879E 受控PID13972运行约112秒并进入正常运行，真实日志确认CurrencyBag原y=1→Greek y=2，弩矢生命周期成功注册。加载完成后核对原隐士getter17字节与4处已有原生入口；无新增错误，仍存在20次已知盾牌NRE。测试结束恢复原运行状态，再安装同一候选。存档SHA 3DE1786460A74262F58D2C18772E91722BD7D50CBCF04517F2526BCAD3C5571A、配置逐字节、金库5709→5709保持。

跨世界往返和非1原值恢复已有生产直链回归及原生路径审查；本轮没有在真实游戏中建立幕府/北境新档并完成来回切换，也没有实际联机目视验收。当前希腊存档的角色种类有限，不把其日志外推为所有世界、所有单位实战均已验证。公开6.1.5仍旧包，本轮未commit/push。

用户在本轮末明确指定后续worker必须本机OMP DeepSeek Flash thinking=max，不使用内置subagent；已写入AGENTS及协作约定。前面的实现工作发生在该新指令之前；收到后没有新增内置worker。本机OMP18.1.19模型目录与实际session 01a09ba9-6757-7717-b142-ce068caa6f55确认provider=deepseek/model=deepseek-v4-flash/thinking=max，已用read/write限定工具完成本轮验收材料核查。若OMP后续故障，先诊断，不自动回退。

- Build: 6.1.5-greek-scale-scope-20260914
- DLL SHA256: 494A879EE64A2447C88F9807B40493E20D148BA82CDD96880DEEF095DFBC6171
- Core SHA256: F8F321D5B3A0F89587D88CCC4CA44212543735FBCF9D0BEBBFF2CA315375F30B
- Raw receipts and OMP session are retained in local greek-scale-scope-20260914 task directory; saves/config originals and raw session are not public artifacts.
