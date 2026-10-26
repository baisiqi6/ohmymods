# Windows .281.3 安装尝试：未执行

代码提交 `d3da7c9ee421485cdd46b2f1344141dae9f9bb35` 已推送PR #282。已验证候选 `.281.3` SHA256 `63d43ff17ff601d1c0f95f26beeafdde21ddffae4e73419d640119c8060f32ae`；本轮没有安装成功，未合并或关单。

- 03:39:53Z只读核对：游戏0进程，25份存档/配置，已装DLL `a3d49aab…d83c`、清单 `1e09caee…48ede`，Native与launcher指纹符合本轮输入。
- 新独立stage已放入候选／清单／脚本；脚本 `24bb607ec3019f5d24fe28eee7fbc092d6355e9c771848d38cc68647aeb4a278`，新清单 `2a51b8da0ed29000871dcdb420026c89bffa02e6f5ab98baaad8db7ff6140cf5`。原最终模板39函数与主执行尾部保持；仅重绑本轮常量与5条数据指纹。
- 独立准备审查通过，targeted守卫19/19；实际Windows PS5.1.19041.6456 Parser0errors、staged三个文件hash/size匹配、游戏0进程。本轮GLM5.3 max精确执行审查通过，公开模型事件已核；没有复用旧轮次批准。
- 一次Apply发送时SSH尚未连接目标即超时，exit255。命令未送达，安装脚本没有执行；没有本轮成功receipt或新备份，不能把stage当安装、采集指纹当备份。未启动游戏或修改正常存档／配置／DLL／部署清单，旧备份保持。

连接恢复后先重新核对进程、当前DLL/清单、完整数据与Native指纹；使用新的具体安装窗口／审查，不直接重试本次one-shot或复用其旧TTL。仍只写独立E玩家包，不碰Steam，不恢复正常存档。安装成功再由玩家完成出航→第一坐骑岛→保存退出→新进程重进；当前无Windows原生通过结论。

私有证据：`transport-contract/install-2813-20261026T033144Z/` 下 `preflight.json`、`parse-readonly.json`、`local-test-results.json`、`independent-install-review.md`、`execution-review-verdict.md`、`execution-start.json`、`execute-exit.json` 和原始stdout/stderr。首次stdin采集超时及未加本进程ExecutionPolicy开关的只读脚本拒绝日志保留；后改SCP/-File完成只读采集，没有改系统执行策略。
