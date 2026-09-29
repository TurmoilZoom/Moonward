using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Helpers;
using System.Threading.Tasks;


namespace Starward.Controls;

/// <summary>
/// 显示路径所在磁盘类型的小标签（SSD / HDD / 可移动 / 网络），样式参照官方启动器安装路径前的标识；无法识别时自动隐藏。
/// </summary>
public sealed partial class DiskTypeBadge : UserControl
{


    public DiskTypeBadge()
    {
        this.InitializeComponent();
    }



    /// <summary>
    /// 要识别的文件夹路径，可以尚不存在
    /// </summary>
    public string? FolderPath
    {
        get { return (string?)GetValue(FolderPathProperty); }
        set { SetValue(FolderPathProperty, value); }
    }

    public static readonly DependencyProperty FolderPathProperty =
        DependencyProperty.Register(nameof(FolderPath), typeof(string), typeof(DiskTypeBadge), new PropertyMetadata(null, OnFolderPathChanged));


    private static void OnFolderPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DiskTypeBadge badge)
        {
            badge.RefreshAsync(e.NewValue as string);
        }
    }



    /// <summary>
    /// 在后台识别磁盘类型后更新标签。
    /// </summary>
    /// <param name="path">本次要识别的路径</param>
    private async void RefreshAsync(string? path)
    {
        try
        {
            DiskMediaType type = DiskMediaType.Unknown;
            if (!string.IsNullOrWhiteSpace(path))
            {
                // DeviceIoControl 碰上休眠的机械盘可能要等唤醒，不放在 UI 线程
                type = await Task.Run(() => DriveHelper.GetDiskMediaType(path));
            }
            // 连续切换路径时，只采用与当前路径一致的结果
            if (path != FolderPath)
            {
                return;
            }
            // 在 UI 线程读取，跟随当前界面语言
            string? text = type switch
            {
                DiskMediaType.SSD => Lang.DiskTypeBadge_SSD,
                DiskMediaType.HDD => Lang.DiskTypeBadge_HDD,
                DiskMediaType.Removable => Lang.DiskTypeBadge_Removable,
                DiskMediaType.Network => Lang.DiskTypeBadge_Network,
                _ => null,
            };
            TextBlock_DiskType.Text = text ?? "";
            Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch { }
    }


}
