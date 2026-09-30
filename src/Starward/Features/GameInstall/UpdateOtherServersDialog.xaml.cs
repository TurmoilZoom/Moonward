using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Starward.Core;
using Starward.RPC;
using System.Collections.Generic;
using System.Linq;


namespace Starward.Features.GameInstall;

/// <summary>
/// 更新某个区服时，询问是否一并更新与它硬链接、也有新版本的其他区服。
/// </summary>
public sealed partial class UpdateOtherServersDialog : ContentDialog
{


    /// <summary>
    /// 正在更新的区服，只用于埋点。
    /// </summary>
    public GameBiz CurrentGameBiz { get; set; }


    // Servers 与 SelectedServers 用 internal：公开的话 XAML 类型信息会收录 record 并生成 init 属性的赋值，编译不过

    /// <summary>
    /// 可一并更新的区服，本体在前。
    /// </summary>
    internal IReadOnlyList<OtherServerUpdate> Servers { get; set; } = [];


    /// <summary>
    /// 用户点了更新；为 <see langword="false"/> 时当前区服也不更新。
    /// </summary>
    public bool Confirmed { get; private set; }


    /// <summary>
    /// 用户勾选、要一并更新的区服，只在 <see cref="Confirmed"/> 时有意义。
    /// </summary>
    internal List<OtherServerUpdate> SelectedServers { get; private set; } = [];


    private readonly List<(CheckBox CheckBox, OtherServerUpdate Server)> _checkBoxes = new();



    public UpdateOtherServersDialog()
    {
        this.InitializeComponent();
        TextBlock_SettingHint.Text = string.Format(Lang.UpdateOtherServersDialog_SettingHint, Lang.SettingPage_Download);
    }



    private void ContentDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (_checkBoxes.Count > 0)
        {
            return;
        }
        foreach (OtherServerUpdate server in Servers)
        {
            var title = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = server.GameId.GameBiz.ToGameServerName(), FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = $"{server.LocalVersion} → {server.LatestVersion}", Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush },
                },
            };
            if (server.IsHardLinkSource)
            {
                // 当前区服是从它硬链接产生的，它会最先更新
                title.Children.Add(new Border
                {
                    Padding = new Thickness(6, 0, 6, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    CornerRadius = new CornerRadius(4),
                    Background = Application.Current.Resources["AccentFillColorDefaultBrush"] as Brush,
                    Child = new TextBlock
                    {
                        Text = Lang.UpdateOtherServersDialog_Source,
                        FontSize = 12,
                        Foreground = Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"] as Brush,
                    },
                });
            }
            // 游戏在运行时文件被占用，更新必然失败，只列出来说明原因，不让勾选
            var detail = new TextBlock
            {
                Text = server.IsGameRunning ? Lang.LauncherPage_GameIsRunning : server.InstallPath,
                FontSize = 12,
                Foreground = Application.Current.Resources[server.IsGameRunning ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush"] as Brush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTipService.SetToolTip(detail, server.InstallPath);
            var checkBox = new CheckBox
            {
                Content = new StackPanel { Spacing = 2, Children = { title, detail } },
                IsChecked = !server.IsGameRunning,
                IsEnabled = !server.IsGameRunning,
            };
            StackPanel_Servers.Children.Add(checkBox);
            _checkBoxes.Add((checkBox, server));
        }
        Telemetry.Track("update_linked_dialog_show", CurrentGameBiz,
            ("servers", Servers.Select(x => x.GameId.GameBiz.Value).ToList()),
            ("source", Servers.FirstOrDefault(x => x.IsHardLinkSource)?.GameId.GameBiz.Value),
            ("running", Servers.Where(x => x.IsGameRunning).Select(x => x.GameId.GameBiz.Value).ToList()));
    }



    [RelayCommand]
    private void Update()
    {
        SelectedServers = _checkBoxes.Where(x => x.CheckBox.IsEnabled && x.CheckBox.IsChecked is true).Select(x => x.Server).ToList();
        Confirmed = true;
        bool doNotAskAgain = CheckBox_DoNotAskAgain.IsChecked is true;
        if (doNotAskAgain)
        {
            // 只在确认更新时记住：取消表示这次什么都不做，不应改变以后的行为
            AppConfig.UpdateHardLinkedGamesTogether = true;
        }
        Telemetry.Track("update_linked_dialog_click", CurrentGameBiz, ("button", "update"), ("selected", SelectedServers.Select(x => x.GameId.GameBiz.Value).ToList()), ("do_not_ask_again", doNotAskAgain));
        this.Hide();
    }



    [RelayCommand]
    private void Cancel()
    {
        Telemetry.Track("update_linked_dialog_click", CurrentGameBiz, ("button", "cancel"));
        this.Hide();
    }


}
