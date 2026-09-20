# 8.0.0 正式发布前独立复核

Reviewer：内置 /root/package800_review，只读，独立于文档worker与Operator。最终verdict：通过，无阻塞，可按用户明确授权推新标签、上传并核远端摘要后发布。

最终提交dd5c86fccb132be65aa9fca204d8cd3be1035121，父648ddf0；452项源码选集逐路径复核，没有新增私人存档/配置/日志、原生游戏二进制或常见凭据格式。对a6df601仅csproj引用与release plan修正，无玩法源码变动、Mono引用段不变。

独立Cecil：正式DLL80522bf18952fe609c4f6f91fd6b52c26ab79cbf87ee918159770306953348f4的2675方法体/locals/EH与已验8.0候选一致，2张PNG逐字节相同；5处Input调用明确绑定Assembly-CSharp-firstpass global Input。assembly8.0.0.0，clean构建0W0E。

独立ZIP：39,636,093字节、313项，全CRC通过。SHA1095ceb2dbc587fc60366835e7b7c360b87f3408a6005184f5325c446d78db8f；306项运行依赖逐字节等于公开7.6.5。Manifest准确GitCommit/DLL摘要，没有旧local-snapshot字段。五文档匹配提交，不含个人数据；英雄仅单机及所有待实机边界保留。

精确提交重跑：56可执行项目、4组真实xUnit共392/392、8组Library编译，全通过。不把接口编译或离线像素验证扩写为游戏实测。

Reviewer未提交、发布、安装或操作游戏。远端上传摘要、公开Latest/标签读回和本机DLL备份安装由Operator执行并另存receipt；首次未修引用a6df601未推送发布。
