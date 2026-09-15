// 本测试程序集共享模块静态状态（Env/账本/统计），必须串行执行。
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
