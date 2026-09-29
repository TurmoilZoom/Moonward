namespace Starward.Helpers;

/// <summary>
/// 路径所在磁盘的介质类型
/// </summary>
internal enum DiskMediaType
{

    /// <summary>
    /// 无法识别（驱动器不存在、光驱、设备不支持查询等）
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 机械硬盘（有寻道惩罚）
    /// </summary>
    HDD = 1,

    /// <summary>
    /// 固态硬盘（无寻道惩罚）
    /// </summary>
    SSD = 2,

    /// <summary>
    /// 可移动存储（U 盘、存储卡，以及 USB 外置硬盘）
    /// </summary>
    Removable = 3,

    /// <summary>
    /// 网络位置（映射的网络驱动器或 \\ 开头的网络共享路径）
    /// </summary>
    Network = 4,

}
