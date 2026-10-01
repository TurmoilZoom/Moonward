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
}