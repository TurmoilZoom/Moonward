using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Controls;
using Starward.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 自定义首页右侧功能栏：左侧选游戏，右侧列出该游戏有的功能项，可拖动排序、用开关隐藏。按游戏分别保存。
/// </summary>
/// <remarks>
/// 改动即时写入设置并发 <see cref="RightToolbarLayoutChangedMessage"/>，首页（同一游戏时）立即重排。
/// </remarks>
public sealed partial class RightToolbarCustomizeDialog : ContentDialog
{

    /// <summary>可自定义的游戏及其先后。</summary>
    private static readonly string[] GameOrder = [GameBiz.hk4e, GameBiz.hkrpg, GameBiz.nap, GameBiz.bh3];


    private readonly ILogger<RightToolbarCustomizeDialog> _logger = AppConfig.GetLogger<RightToolbarCustomizeDialog>();

    /// <summary>游戏列表的滑动选中指示条；持有引用，事件订阅随列表加载 / 卸载自行收放。</summary>
    private readonly ListViewSlidingIndicator _gameIndicator;

    private readonly RightToolbarRowDragReorder _dragReorder;

    /// <summary>右侧正在编辑的游戏（不分区服）。</summary>
    private string? _game;

    /// <summary>正在编辑的游戏的排列，改动后整体写回设置。</summary>
    private RightToolbarLayout _layout = RightToolbarLayout.CreateDefault();


    public RightToolbarCustomizeDialog()
    {
        this.InitializeComponent();
        _gameIndicator = new ListViewSlidingIndicator(ListView_Games, Grid_GameIndicatorHost, Grid_GameIndicatorFollower, Border_GameIndicator);
        _dragReorder = new RightToolbarRowDragReorder(StackPanel_Items, OnRowsReordered);
        foreach (string game in GameOrder)
        {
            Games.Add(new RightToolbarGameView(game));
        }
        this.Loaded += RightToolbarCustomizeDialog_Loaded;
        this.Unloaded += RightToolbarCustomizeDialog_Unloaded;
    }


    /// <summary>打开时选中的游戏（任意区服的 <see cref="GameBiz"/> 均可）；不在列表里时选第一个。</summary>
    public GameBiz InitialGame { get; set; }


    /// <summary>可自定义的游戏。</summary>
    public ObservableCollection<RightToolbarGameView> Games { get; } = new();



    private void RightToolbarCustomizeDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // 从设置页打开也算知道这个功能了，首页不必再弹右键自定义引导
        AppConfig.HasSeenRightToolbarCustomizeHint = true;
        RightToolbarGameView? initial = Games.FirstOrDefault(x => x.Game == InitialGame.Game) ?? Games.FirstOrDefault();
        if (initial is not null)
        {
            ListView_Games.SelectedItem = initial;
        }
    }


    private void RightToolbarCustomizeDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        ClearRows();
    }


    private void ListView_Games_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListView_Games.SelectedItem is RightToolbarGameView view)
        {
            LoadGame(view.Game);
        }
    }


    /// <summary>
    /// 读取某个游戏的排列，按顺序为该游戏有的功能项建行。
    /// </summary>
    /// <param name="game">游戏（不分区服）。</param>
    private void LoadGame(string game)
    {
        try
        {
            _game = game;
            _layout = RightToolbarLayout.Parse(AppConfig.GetRightToolbarItems(game));
            ClearRows();
            foreach (RightToolbarItem item in _layout.Order)
            {
                if (!RightToolbarItemCatalog.IsSupportedByGame(game, item))
                {
                    continue;
                }
                var row = new RightToolbarCustomizeRow(item, RightToolbarItemCatalog.GetServerLimitText(game, item), !_layout.IsHidden(item));
                row.IsShownChanged += Row_IsShownChanged;
                _dragReorder.Attach(row);
                StackPanel_Items.Children.Add(row);
            }
            Button_Reset.IsEnabled = !_layout.IsDefault;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load right toolbar layout of {game} failed.", game);
        }
    }


    /// <summary>
    /// 移除右侧全部行并解除事件。
    /// </summary>
    private void ClearRows()
    {
        foreach (UIElement child in StackPanel_Items.Children)
        {
            if (child is RightToolbarCustomizeRow row)
            {
                row.IsShownChanged -= Row_IsShownChanged;
                _dragReorder.Detach(row);
            }
        }
        StackPanel_Items.Children.Clear();
    }


    private void Row_IsShownChanged(object? sender, EventArgs e)
    {
        if (sender is RightToolbarCustomizeRow row)
        {
            _layout.SetHidden(row.Item, !row.IsShown);
            Save();
        }
    }


    /// <summary>
    /// 拖动换位已提交到面板：按面板里的新先后重排这些项，其余（该游戏没有的）项原位不动。
    /// </summary>
    private void OnRowsReordered()
    {
        List<RightToolbarItem> order = StackPanel_Items.Children.OfType<RightToolbarCustomizeRow>().Select(x => x.Item).ToList();
        _layout.ReorderSubset(order);
        Save();
    }


    /// <summary>
    /// 写回设置（与默认相同时清空，以后默认顺序变了也能跟上），通知首页重排。
    /// </summary>
    private void Save()
    {
        if (_game is null)
        {
            return;
        }
        try
        {
            AppConfig.SetRightToolbarItems(_game, _layout.IsDefault ? null : _layout.Serialize());
            Button_Reset.IsEnabled = !_layout.IsDefault;
            WeakReferenceMessenger.Default.Send(new RightToolbarLayoutChangedMessage(_game));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save right toolbar layout of {game} failed.", _game);
        }
    }


    private void Button_Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null)
        {
            return;
        }
        _layout = RightToolbarLayout.CreateDefault();
        Save();
        LoadGame(_game);
    }


    private void Button_Close_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();
    }


}



/// <summary>
/// 自定义功能栏对话框左侧的一个游戏。
/// </summary>
public sealed class RightToolbarGameView
{

    /// <param name="game">游戏（不分区服），如 <c>hk4e</c>。</param>
    public RightToolbarGameView(string game)
    {
        Game = game;
        Name = new GameBiz(game).ToGameName();
        Icon = game switch
        {
            GameBiz.bh3 => "ms-appx:///Assets/Image/icon_bh3.jpg",
            GameBiz.hk4e => "ms-appx:///Assets/Image/icon_ys.jpg",
            GameBiz.hkrpg => "ms-appx:///Assets/Image/icon_sr.jpg",
            GameBiz.nap => "ms-appx:///Assets/Image/icon_zzz.jpg",
            _ => "ms-appx:///Assets/Image/Transparent.png",
        };
    }


    /// <summary>游戏（不分区服）。</summary>
    public string Game { get; }

    /// <summary>游戏名称。</summary>
    public string Name { get; }

    /// <summary>游戏图标地址。</summary>
    public string Icon { get; }


    /// <summary>
    /// 列表项的无障碍名称取自这里，不重写时读屏读出的是类名。
    /// </summary>
    /// <returns>游戏名称。</returns>
    public override string ToString() => Name;

}
