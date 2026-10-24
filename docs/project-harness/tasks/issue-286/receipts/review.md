# #286 managed 候选增量审查通过
**结论：本次managed候选范围审查通过，无剩余已知blocking；不是Native/用户实机/安装验收。** 保留此前r1/r2/r3审查，不改写历史。
- Persistence:764–775：sameTarget活跃责任纯return现已在known staged row拒绝之前，既不换Session/Quota，也不改Unknown；无已知row与已有target row两种真实armed lease用例均核`ValidatePurchase(lease)`及Pending/key/Staged保持。
- :691–696：Tick前复用GenerationOriginExact同时确认Current与owner.Sessions exact origin；:731 Tick后origin不符纯return，已删除对外来replacement的Unknown写入。
- NewGenerationDeltaRegression:444–469的Sessions-only反例只替换成员、不换Current，other.Unknown=false；断言Current仍origin、other.Unknown未改与key/Staged/Quota界限保持，准确覆盖上次漏口。
- firstPreflight:707–711已收窄为`KeyRead==ConfirmedMissing`；真实PreflightKeyWrite正例与外部raw篡改负例均保持，不泛化Present或普通Load。
- 已核原始`order-anchor-red.log`为41项/2失败，分别是known-row armed与Sessions-only；`order-anchor-green.log`同41项/0失败。不是构建失败伪红。本审查未重复执行这些测试。
- 既有generation-slot/enrollment/R2-R3有效green可复用；NoDeploy终态与host由Root集中留证。Native land规范化时序、实际用户槽位renderer族等仍是后续边界，不重新阻塞此managed候选。
- 只读源码/日志，未改W、未执行Native或安装。
- 审查SHA `il2cpp/HeavyShieldPersistence.cs`：`d6349436e4da419ef0646609ab622ddcc6e453459d128afcd5c71947e512c039`
- 审查SHA `tests/heavy-shield-identity/NewGenerationDeltaRegression.cs`：`239ed0274cd3e2765f53058ee7ad0d0371694ea3c2db47a5387fe01a587ca940`

Operator 精度修正：早期阶段审查曾把 Save 全路径写成缺少 land 门；实际 ObserveNativeId/Captured 原已有 land 门。本轮只对 marker 已接受后到 finalizer 之间的晚期漂移补最终检查，并由真实红例证明，未删旧保护制造红测。
