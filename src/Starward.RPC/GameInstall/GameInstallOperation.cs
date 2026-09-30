namespace Starward.RPC.GameInstall;

public enum GameInstallOperation
{

    None = 0,


    Install = 1,


    Predownload = 2,


    Update = 3,


    Repair = 4,


    Uninstall = 5,


    /// <summary>
    /// 只修复 WPF 包（原神的千星沙箱），不动游戏资源
    /// </summary>
    RepairWPFPackage = 6,


    /// <summary>
    /// 只把 WPF 包（原神的千星沙箱）更新到官方最新版本，本地已是最新时什么也不做；由「自动更新千星沙箱」在后台发起
    /// </summary>
    UpdateWPFPackage = 7,
}