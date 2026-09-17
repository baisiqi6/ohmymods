// 本测试程序集共享模块静态状态（统计/注册/life 序号），必须串行执行。
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
