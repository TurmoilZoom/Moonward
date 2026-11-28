using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Starward.Core;
using Starward.Core.GameRecord;
using Starward.Core.GameRecord.ZZZ.UpgradeGuide;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;


namespace Starward.Features.GameRecord.ZZZ;

/// <summary>
/// 绝区零养成指南：养成计算（含未实装的预告代理人）、技能与音擎、社区推荐方案。数据实时请求，不落库。
/// </summary>
public sealed partial class ZZZCultivatePage : PageBase
{


    private readonly ILogger<ZZZCultivatePage> _logger = AppConfig.GetLogger<ZZZCultivatePage>();

    private readonly GameRecordService _gameRecordService = AppConfig.GetService<GameRecordService>();


    /// <summary>本页已拉过的详情，键为「代理人 Id|含专武|技能升满」，切回同一代理人不再请求。</summary>
    private readonly Dictionary<string, ZZZCultivateAvatarDetail> _detailCache = [];

    /// <summary>物品目录（含持有音擎），全量且与代理人无关，本页只拉一次。</summary>
    private UpgradeGuideItemList? _itemList;

    /// <summary>代理人头像表，随列表一起拉。</summary>
    private UpgradeGuidIconInfo? _iconInfo;

    /// <summary>排好序的全部代理人；<see cref="AgentList"/> 是按搜索和筛选过滤后的结果。</summary>
    private List<ZZZCultivateAgentItem> _allAgents = [];

    private CancellationTokenSource? _detailCancellation;

    private GameRecordRole gameRole;

    /// <summary>预告代理人的材料是否含专属音擎（页面级，切代理人时保持）。</summary>
    private bool _teaserIncludeWeapon = true;

    /// <summary>预告代理人的优先技能是否升到 12 级。</summary>
    private bool _teaserMaxSkills = true;



    public ZZZCultivatePage()
    {
        this.InitializeComponent();
    }



    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is GameRecordRole role)
        {
            gameRole = role;
        }
    }



    protected override async void OnLoaded()
    {
        await Task.Delay(16);
        await LoadAgentListAsync();
    }



    protected override void OnUnloaded()
    {
        _detailCancellation?.Cancel();
        _detailCancellation = null;
        CurrentView = null;
        AgentList = null;
        _allAgents = [];
    }



    private static string CurrentLanguage => CultureInfo.CurrentUICulture.Name;


    public List<ZZZCultivateAgentItem>? AgentList { get; set => SetProperty(ref field, value); }


    public ZZZCultivateDetailView? CurrentView { get; set => SetProperty(ref field, value); }


    public bool IsLoading { get; set => SetProperty(ref field, value); }


    /// <summary>筛选弹层的选项组，随代理人列表生成。</summary>
    public List<ZZZCultivateFilterGroup>? FilterGroups { get; set => SetProperty(ref field, value); }


    /// <summary>有任一筛选项勾选时，筛选按钮上显示圆点。</summary>
    public Visibility FilterActiveVisibility { get; set => SetProperty(ref field, value); } = Visibility.Collapsed;


    /// <summary>批量改勾选（重置）时暂停逐项过滤，改完统一过滤一次。</summary>
    private bool _suppressFilter;



    [RelayCommand]
    private async Task RefreshDataAsync()
    {
        _detailCache.Clear();
        _itemList = null;
        await LoadAgentListAsync(reloadDetail: true);
    }



    /// <summary>
    /// 拉代理人列表（预告代理人固定在最上面），保持原选中项。
    /// </summary>
    /// <param name="reloadDetail">刷新时重新显示选中代理人的详情（缓存已清，会重新请求）。</param>
    private async Task LoadAgentListAsync(bool reloadDetail = false)
    {
        if (gameRole is null || IsLoading)
        {
            return;
        }
        int? selectedId = CurrentView?.AvatarId;
        try
        {
            IsLoading = true;
            Image_Emoji.Visibility = Visibility.Collapsed;
            var list = await _gameRecordService.GetZZZCultivateAvatarListAsync(gameRole, CurrentLanguage);
            if (_iconInfo is null)
            {
                try
                {
                    _iconInfo = await _gameRecordService.GetZZZCultivateIconInfoAsync(gameRole, CurrentLanguage);
                }
                catch (Exception ex)
                {
                    // 头像拿不到不影响数据，只是列表不显示头像
                    _logger.LogWarning(ex, "Get cultivate icon info ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
                }
            }
            _allAgents = ZZZCultivateViewBuilder.SortAgents((list.List ?? [])
                .Where(x => x.Avatar is not null)
                .Select(x => ZZZCultivateAgentItem.From(x, _iconInfo)));
            if (_allAgents.Count == 0)
            {
                AgentList = null;
                Image_Emoji.Visibility = Visibility.Visible;
                return;
            }
            RebuildFilterGroups();
            ApplyFilter();
            var target = _allAgents.FirstOrDefault(x => x.Id == selectedId) ?? AgentList?.FirstOrDefault() ?? _allAgents[0];
            if (reloadDetail || CurrentView?.AvatarId != target.Id)
            {
                await LoadDetailAsync(target);
            }
            SyncListSelection();
        }
        catch (Exception ex)
        {
            HandleException(ex, "Load cultivate agent list");
            if (_allAgents.Count == 0)
            {
                Image_Emoji.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }



    /// <summary>
    /// 按代理人数据重建筛选组（沿用原勾选），并订阅勾选变化。
    /// </summary>
    private void RebuildFilterGroups()
    {
        foreach (var option in FilterGroups?.SelectMany(g => g.Options) ?? [])
        {
            option.PropertyChanged -= FilterOption_PropertyChanged;
        }
        var groups = ZZZCultivateViewBuilder.BuildFilterGroups(_allAgents, FilterGroups);
        foreach (var option in groups.SelectMany(g => g.Options))
        {
            option.PropertyChanged += FilterOption_PropertyChanged;
        }
        FilterGroups = groups;
    }



    private void FilterOption_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_suppressFilter && e.PropertyName == nameof(ZZZCultivateFilterOption.IsChecked))
        {
            ApplyFilter();
        }
    }



    private void Button_FilterReset_Click(object sender, RoutedEventArgs e)
    {
        _suppressFilter = true;
        try
        {
            foreach (var option in FilterGroups?.SelectMany(g => g.Options) ?? [])
            {
                option.IsChecked = false;
            }
        }
        finally
        {
            _suppressFilter = false;
        }
        ApplyFilter();
    }



    /// <summary>
    /// 按搜索词与筛选弹层的勾选过滤列表；排序不变，预告代理人仍在最上面。
    /// </summary>
    private void ApplyFilter()
    {
        // 控件初始化期间 TextChanged 可能先于列表创建触发，此时字段为 null
        if (TextBox_Search is null || ListView_Agents is null)
        {
            return;
        }
        string keyword = TextBox_Search.Text?.Trim() ?? "";
        var groups = FilterGroups ?? [];
        AgentList = _allAgents
            .Where(x => (keyword.Length == 0 || x.Matches(keyword)) && ZZZCultivateViewBuilder.PassesFilter(x, groups))
            .ToList();
        FilterActiveVisibility = groups.Any(g => g.Options.Any(o => o.IsChecked == true)) ? Visibility.Visible : Visibility.Collapsed;
        SyncListSelection();
    }



    /// <summary>
    /// 列表重新赋值后选中项会丢，按当前详情把它选回来（不触发重新加载）。
    /// </summary>
    private void SyncListSelection()
    {
        var current = AgentList?.FirstOrDefault(x => x.Id == CurrentView?.AvatarId);
        if (current is not null && !ReferenceEquals(ListView_Agents.SelectedItem, current))
        {
            ListView_Agents.SelectedItem = current;
        }
    }



    private void TextBox_Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }



    private async void ListView_Agents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 过滤后把当前代理人选回来时也会进来，详情已是它就不重复加载
        if (e.AddedItems.FirstOrDefault() is ZZZCultivateAgentItem item && item.Id != CurrentView?.AvatarId)
        {
            await LoadDetailAsync(item);
        }
    }



    /// <summary>
    /// 显示代理人详情：有缓存直接用，否则请求；快速切换时只显示最后一次选中的结果。
    /// 预告代理人按两个开关请求，详情里直接带算好的材料。
    /// </summary>
    /// <param name="item">列表项。</param>
    private async Task LoadDetailAsync(ZZZCultivateAgentItem item)
    {
        _detailCancellation?.Cancel();
        var request = new ZZZCultivateAvatarDetailRequestItem
        {
            AvatarId = item.Id,
            IsTeaser = item.IsTeaser,
            TeaserNeedWeapon = item.IsTeaser && _teaserIncludeWeapon,
            TeaserSpSkill = item.IsTeaser && _teaserMaxSkills,
        };
        string key = $"{request.AvatarId}|{request.TeaserNeedWeapon}|{request.TeaserSpSkill}";
        if (_detailCache.TryGetValue(key, out var cached))
        {
            ShowDetail(cached, item.IsTeaser);
            return;
        }
        var cts = new CancellationTokenSource();
        _detailCancellation = cts;
        try
        {
            ProgressRing_Detail.IsActive = true;
            var detail = await _gameRecordService.GetZZZCultivateAvatarDetailAsync(gameRole, request, CurrentLanguage, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }
            if (detail?.Avatar is null)
            {
                CurrentView = null;
                return;
            }
            _detailCache[key] = detail;
            ShowDetail(detail, item.IsTeaser);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            HandleException(ex, "Load cultivate agent detail");
        }
        finally
        {
            if (_detailCancellation == cts)
            {
                ProgressRing_Detail.IsActive = false;
            }
        }
    }



    /// <summary>
    /// 换上新详情；换了代理人时滚回顶部，预告开关切换（同一代理人）时保持滚动位置。
    /// </summary>
    /// <param name="detail">代理人详情。</param>
    /// <param name="isTeaser">是否为预告代理人。</param>
    private void ShowDetail(ZZZCultivateAvatarDetail detail, bool isTeaser)
    {
        bool sameAgent = CurrentView?.AvatarId == detail.Avatar.Id;
        CurrentView = ZZZCultivateViewBuilder.BuildDetail(detail, isTeaser, _iconInfo);
        if (!sameAgent)
        {
            ScrollViewer_Detail.ChangeView(null, 0, null, true);
        }
    }



    /// <summary>
    /// 预告代理人的两个开关切换后按新开关重新请求。XAML 里初值为勾选，初始化时也会进来：
    /// 那时另一个复选框可能还没创建，所以逐个判空；当前没有预告代理人时不请求。
    /// </summary>
    private async void TeaserOption_Changed(object sender, RoutedEventArgs e)
    {
        bool includeWeapon = CheckBox_TeaserIncludeWeapon?.IsChecked != false;
        bool maxSkills = CheckBox_TeaserMaxSkills?.IsChecked != false;
        if (includeWeapon == _teaserIncludeWeapon && maxSkills == _teaserMaxSkills)
        {
            return;
        }
        _teaserIncludeWeapon = includeWeapon;
        _teaserMaxSkills = maxSkills;
        if (CurrentView is { IsTeaser: true } view && _allAgents.FirstOrDefault(x => x.Id == view.AvatarId) is { } item)
        {
            await LoadDetailAsync(item);
        }
    }



    [RelayCommand]
    private async Task CalculateAsync()
    {
        var view = CurrentView;
        if (view is null || view.IsTeaser || gameRole is null || view.IsCalculating)
        {
            return;
        }
        try
        {
            view.IsCalculating = true;
            var request = new ZZZCultivateCalcRequest
            {
                AvatarId = view.AvatarId,
                AvatarCurrentLevel = view.CalcRows[0].Current,
                AvatarLevel = ClampTarget(view.CalcRows[0]),
                AvatarCurrentPromotes = view.Promotes,
                Skills = view.CalcRows.Skip(1).Select(x => new ZZZCultivateCalcSkill
                {
                    SkillType = x.SkillType,
                    InitLevel = x.Current,
                    Level = ClampTarget(x),
                }).ToList(),
            };
            if (view.SelectedWeaponOption is { Id: not 0 } option)
            {
                var (level, promotes) = await GetWeaponCurrentAsync(view, option.Id);
                view.WeaponRow.Current = level;
                request.WeaponInfo = new ZZZCultivateCalcWeapon
                {
                    WeaponId = option.Id,
                    WeaponInitLevel = level,
                    WeaponPromotes = promotes,
                    WeaponLevel = Math.Max(level, ClampTarget(view.WeaponRow)),
                };
            }
            var result = await _gameRecordService.CalculateZZZCultivateAsync(gameRole, request, CurrentLanguage);
            if (CurrentView == view)
            {
                view.Result = ZZZCultivateViewBuilder.BuildMaterials(result);
            }
        }
        catch (Exception ex)
        {
            HandleException(ex, "Calculate cultivate materials");
        }
        finally
        {
            view.IsCalculating = false;
        }
    }



    private async void ComboBox_Weapon_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectedItem 只单向绑定（object 无法双向绑到具体类型），选中项在这里写回
        if (CurrentView is { } view && sender is ComboBox { SelectedItem: ZZZCultivateWeaponOption option })
        {
            view.SelectedWeaponOption = option;
            await UpdateWeaponRowAsync(view);
        }
    }



    /// <summary>
    /// 切换音擎后刷新音擎行的当前等级。查不到持有信息时不报错，计算时会再试一次。
    /// </summary>
    /// <param name="view">当前详情。</param>
    private async Task UpdateWeaponRowAsync(ZZZCultivateDetailView view)
    {
        if (view.SelectedWeaponOption is not { Id: not 0 } option)
        {
            return;
        }
        if (option.Id == view.EquippedWeaponId)
        {
            view.WeaponRow.Current = view.EquippedWeaponLevel;
            return;
        }
        try
        {
            var (level, _) = await GetWeaponCurrentAsync(view, option.Id);
            if (view.SelectedWeaponOption == option)
            {
                view.WeaponRow.Current = level;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Get owned W-Engine level ({gameBiz}, {uid}).", gameRole?.GameBiz, gameRole?.Uid);
        }
    }



    /// <summary>
    /// 目标等级取整并限制在「当前 ~ 上限」；NumberBox 清空时为 NaN，按当前等级算。
    /// </summary>
    /// <param name="row">等级输入行。</param>
    /// <returns>目标等级。</returns>
    private static int ClampTarget(ZZZCultivateCalcRow row)
    {
        int target = double.IsNaN(row.Target) ? row.Current : (int)Math.Round(row.Target);
        return Math.Clamp(target, row.Current, Math.Max(row.Current, row.Max));
    }



    /// <summary>
    /// 音擎的当前等级与突破阶段：已装备的取装备等级，否则取持有的最高等级那把；没有就从 0 级起算。
    /// 突破阶段只在物品目录里有，所以要拉一次 <c>item_list</c>。
    /// </summary>
    /// <param name="view">当前详情。</param>
    /// <param name="weaponId">音擎 Id。</param>
    /// <returns>当前等级与突破阶段。</returns>
    private async Task<(int Level, int Promotes)> GetWeaponCurrentAsync(ZZZCultivateDetailView view, int weaponId)
    {
        _itemList ??= await _gameRecordService.GetZZZCultivateItemListAsync(gameRole, view.AvatarId, CurrentLanguage);
        var owned = _itemList.Weapon?.FirstOrDefault(x => x.Id == weaponId)?.UserWeaponInfo ?? [];
        if (weaponId == view.EquippedWeaponId)
        {
            // 同等级可能有突破前后两把，取突破阶段高的那把，避免多算一次突破材料
            var equipped = owned.Where(x => x.Level == view.EquippedWeaponLevel).OrderByDescending(x => x.Promotes).FirstOrDefault();
            return (view.EquippedWeaponLevel, equipped?.Promotes ?? Math.Min(view.EquippedWeaponLevel / 10, 5));
        }
        var best = owned.OrderByDescending(x => x.Level).ThenByDescending(x => x.Promotes).FirstOrDefault();
        return best is null ? (0, 0) : (best.Level, best.Promotes);
    }



    /// <summary>
    /// 统一处理请求异常：米哈游接口错误与网络错误走工具箱的统一提示，其余直接报错。
    /// </summary>
    /// <param name="ex">异常。</param>
    /// <param name="action">日志里的操作名。</param>
    private void HandleException(Exception ex, string action)
    {
        _logger.LogError(ex, "{action} ({gameBiz}, {uid}).", action, gameRole?.GameBiz, gameRole?.Uid);
        switch (ex)
        {
            case miHoYoApiException apiException:
                GameRecordPage.HandleMiHoYoApiException(apiException, preferredRole: gameRole);
                break;
            case HttpRequestException httpException:
                GameRecordPage.HandleMiHoYoHttpException(httpException);
                break;
            default:
                InAppToast.MainWindow?.Error(ex);
                break;
        }
    }

}
