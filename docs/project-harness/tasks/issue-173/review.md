# Issue #173 固定候选独立复审

Verdict：**Approve，限定代码交付范围。** 固定产品源 `e06da832a978d64a31bcca2226fec884544bbaa5ab4dc301e898cea2567fa35d`，8输入manifest逐项核验。独立内置reviewer先审错误产生处与状态责任，再审实现，没有产品/peer/runtime写入。

候选在既有entry/等待边界修正原生retention与fallback契约；保守有限hold空间明示为Mod约束，原生仍负责选择、扣币、出货、回执/拒绝/退款。fast不多查closest；fallback使用本付款人并核捕获pointer+instance；RPC forceBlock不凭querynull误断，旧期限/库存/动作/钱/世界/弹药/归还及#169保持。捕获身份、自身transform朝向、带时刻的历史首次拒绝、unknown NaN、native之前Prefix不Injected均核实。

独立公共103/103；自有隔离fixture加4个反例107/107（第二玩家POV、r0合法fallback、有限float相减溢出、RPC走远停止hold后原单笔reply仍履约且不重发）。同107 fixture仅换83ac937旧源：86通过/21失败，行为与诊断断言分别解释。额外4项为独立私有审查证据，没有改变产品或公共测试；公共103足本交付，不强制扩测试。

读取真实ARM Rebuild0W0E日志并独立核DLL SHA `241d28a930f03006993f53f41a92dca1eecdc7083a54cdcbb6220a1700127479`、Cecil回执6817旧方法/34PNG/338targets保持、9授权变化/11新增仅Hold、其余metadata保持；reviewer没有冒称重跑完整ARM编译/Cecil。

自动harness付款主体仍基于旧2.1模拟，本任务2.4选店链有真实ARM/资产参数独立支持，107全绿不能泛称完整2.4状态机完备。用户现场坐标/读取结果、真正detour、购弓、Windows与完整联机/宫廷联动仍待验。无阻断问题，不等于已安装或玩家验收。私有原始报告/ASM/日志位于本机对应task reviewer目录；未上传用户日志/DLL/存档。
