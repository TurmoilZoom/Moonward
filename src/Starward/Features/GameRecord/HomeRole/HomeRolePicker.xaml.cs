using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Core.GameRecord;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Starward.Features.GameRecord.HomeRole;

/// <summary>
/// 首页便笺、签到卡片里的账号列表。只负责显示与选择，保存与收起弹层由宿主处理。
/// </summary>
public sealed partial class HomeRolePicker : UserControl
{

    /// <summary>
    /// 初始化控件。
    /// </summary>
    public HomeRolePicker()
    {
        this.InitializeComponent();
    }


    /// <summary>
    /// 列表里的角色。
    /// </summary>
    public ObservableCollection<GameRecordRole> Roles { get; } = new();


    /// <summary>
    /// 用户点了某个角色（包括当前角色）。
    /// </summary>
    public event EventHandler<GameRecordRole>? RoleSelected;


    /// <summary>
    /// 换上新的角色列表，并选中当前显示的角色。宿主在弹层打开前调用；顺带按当前语言刷新标题
    /// （不常驻订阅语言消息，免得卡片被换掉后还在已释放的控件上更新绑定）。
    /// </summary>
    /// <param name="roles">当前区服已登录的角色。</param>
    /// <param name="currentUid">卡片当前显示的角色 UID；没有时传 0。</param>
    public void Load(IEnumerable<GameRecordRole> roles, long currentUid)
    {
        this.Bindings.Update();
        Roles.Clear();
        foreach (GameRecordRole role in roles)
        {
            Roles.Add(role);
        }
        ListView_Roles.SelectedItem = Roles.FirstOrDefault(x => x.Uid == currentUid);
    }


    private void ListView_Roles_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GameRecordRole role)
        {
            RoleSelected?.Invoke(this, role);
        }
    }


    /// <summary>
    /// 给列表项设置读屏名称：不设的话读出来的是数据类的类型名。
    /// </summary>
    /// <remarks>
    /// 列表用不虚拟化的 StackPanel，ContainerContentChanging 不会触发，只能在项模板加载时找到所在的容器。
    /// </remarks>
    private void RoleItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GameRecordRole role } && ListView_Roles.ContainerFromItem(role) is DependencyObject container)
        {
            AutomationProperties.SetName(container, $"{role.Nickname} {role.RegionName} {role.Uid}");
        }
    }


    /// <summary>
    /// 角色头像地址；接口没给头像时按区服用米游社 / HoYoLAB 图标代替（同战绩页）。
    /// </summary>
    /// <param name="headIcon">角色头像地址。</param>
    /// <param name="gameBiz">角色所属区服。</param>
    /// <returns>可直接给图片控件用的地址。</returns>
    public static string GetAvatarUrl(string? headIcon, string? gameBiz)
    {
        if (!string.IsNullOrWhiteSpace(headIcon))
        {
            return headIcon;
        }
        GameBiz biz = gameBiz;
        return $"ms-appx:///Assets/Image/icon_{(biz.IsGlobalServer() ? "hoyolab" : "hyperion")}.png";
    }

}
