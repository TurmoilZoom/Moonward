using CommunityToolkit.Mvvm.ComponentModel;
using Starward.Core.HoYoPlay;
using Starward.Features.GameSelector;

namespace Starward.Features.GameInstall;

/// <summary>
/// 卸载对话框中的一个区服
/// </summary>
public sealed partial class UninstallServerItem : ObservableObject
{


    public UninstallServerItem(GameId gameId, GameBizIcon icon, string installPath, bool isCurrent)
    {
        GameId = gameId;
        Icon = icon;
        InstallPath = installPath;
        IsCurrent = isCurrent;
    }


    public GameId GameId { get; }

    /// <summary>
    /// 游戏图标、区服图标与区服名
    /// </summary>
    public GameBizIcon Icon { get; }

    /// <summary>
    /// 去掉末尾分隔符的完整安装目录
    /// </summary>
    public string InstallPath { get; }

    /// <summary>
    /// 是打开对话框的那个区服，总是会卸载
    /// </summary>
    public bool IsCurrent { get; }

    /// <summary>
    /// 安装目录与当前区服相同或位于其中，删除当前区服的目录时会一并删掉，勾选保留其他区服也保留不了
    /// </summary>
    public bool InCurrentFolder { get; set; }

    /// <summary>
    /// 列表中不是第一行，顶部显示分隔线
    /// </summary>
    public bool ShowDivider { get; set; }


    /// <summary>
    /// 与当前区服通过硬链接共用文件，后台查完才会赋值
    /// </summary>
    public bool IsHardLinked { get; set; }

    /// <summary>
    /// 共用文件的区服（含当前区服）中的本体，其余是后来通过硬链接产生的
    /// </summary>
    public bool IsHardLinkSource { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// 通过硬链接从本体产生的区服，可能是当前区服自己
    /// </summary>
    public bool IsHardLinkCopy { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 不能卸载的原因，显示在安装目录的位置；为 <see langword="null"/> 时可以卸载
    /// </summary>
    public string? BlockReason
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsBlocked));
                OnPropertyChanged(nameof(IsNotBlocked));
            }
        }
    }

    public bool IsBlocked => BlockReason is not null;

    public bool IsNotBlocked => BlockReason is null;


    /// <summary>
    /// 目录本身不能删除的原因（驱动器根目录、包含数据文件夹或程序），打开对话框时检查一次
    /// </summary>
    internal string? PathBlockReason { get; set; }

    /// <summary>
    /// <see cref="PathBlockReason"/> 对应的埋点原因
    /// </summary>
    internal string? PathBlockTelemetry { get; set; }


    /// <summary>
    /// 按目录检查结果与游戏是否在运行更新 <see cref="BlockReason"/>
    /// </summary>
    /// <param name="isGameRunning">该区服的游戏正在运行</param>
    internal void UpdateBlockReason(bool isGameRunning)
    {
        BlockReason = PathBlockReason ?? (isGameRunning ? Lang.LauncherPage_GameIsRunning : null);
    }


    /// <summary>
    /// 勾选保留其他区服后，不会卸载的区服变淡
    /// </summary>
    public double RowOpacity { get; set => SetProperty(ref field, value); } = 1;


    public bool IsRunning { get; set => SetProperty(ref field, value); }

    public bool IsDone { get; set => SetProperty(ref field, value); }

    public bool IsFailed { get; set => SetProperty(ref field, value); }


    /// <summary>
    /// 按当前的勾选是否会卸载
    /// </summary>
    /// <param name="keepOtherServers">勾选了保留其他区服</param>
    /// <returns></returns>
    public bool WillUninstall(bool keepOtherServers) => IsCurrent || InCurrentFolder || !keepOtherServers;


}
