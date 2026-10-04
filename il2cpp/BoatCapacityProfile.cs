namespace KingdomEnhancedMod;

/// <summary>
/// 主船（Boat）乘员容量的共享策略常量：PC 与 Android 的 Boat.OnEnable 借用补丁在原生
/// RegisterUnitSlots 消费窗口内写这些目标值，调用结束后按进入值归还。纯常量类，无方法、
/// 无状态、不引用 Unity/Il2Cpp/loader 类型，两个平台的工程编译同一份源码。
/// </summary>
public static class BoatCapacityProfile
{
    public const int Workers = 8;
    public const int Knights = 6;
    public const int Pikemen = 8;
    public const int Farmers = 3;
}
