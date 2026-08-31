using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
using Starward.Features.GameSelector;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 管理本应用创建的免 UAC 启动计划任务。
/// </summary>
[INotifyPropertyChanged]
public sealed partial class ElevatedStartGameTaskManagerDialog : ContentDialog
{
    private readonly ILogger<ElevatedStartGameTaskManagerDialog> _logger = AppConfig.GetLogger<ElevatedStartGameTaskManagerDialog>();

    /// <summary>确认气泡当前指向的任务；确认后才真正删除。</summary>
    private IReadOnlyList<string> _pendingTaskPaths = [];

    public ElevatedStartGameTaskManagerDialog()
    {
        InitializeComponent();
        Loaded += ElevatedStartGameTaskManagerDialog_Loaded;
    }

    public ObservableCollection<ElevatedStartGameTaskGroup> TaskGroups { get; } = [];

    public bool HasTasks => TaskGroups.Count > 0;

    public bool CanEdit => HasTasks && !IsBusy;

    public bool CanClose => !IsBusy;

    /// <summary>列表为空且不在加载中时才显示空态，避免与进度环叠在一起。</summary>
    public bool ShowEmptyState => !HasTasks && !IsBusy;

    public bool CanDeleteSelected => CanEdit && SelectedCount > 0;

    /// <summary>三态全选：无选中为 false，全部选中为 true，部分选中为 null。</summary>
    public bool? AllSelected
    {
        get
        {
            int total = AllTasks.Count();
            if (total == 0)
            {
                return false;
            }
            int selected = SelectedCount;
            return selected == 0 ? false : selected == total ? true : null;
        }
    }

    public string DeleteSelectedText => string.Format(Lang.StartGameTaskManager_DeleteSelected, SelectedCount);

    public string ConfirmationText => string.Format(Lang.StartGameTaskManager_ConfirmMessage, _pendingTaskPaths.Count);

    private IEnumerable<ElevatedStartGameTaskItem> AllTasks => TaskGroups.SelectMany(x => x.Tasks);

    private int SelectedCount => AllTasks.Count(x => x.IsSelected);

    [ObservableProperty]
    private bool _isBusy;

    partial void OnIsBusyChanged(bool value) => RefreshCommandStates();

    private async void ElevatedStartGameTaskManagerDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync(clearFeedback: true);
    }

    /// <summary>
    /// 底部三态全选：未全选时全选，已全选时清除选择。
    /// </summary>
    private void CheckBox_SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool selectAll = AllSelected is not true;
        foreach (ElevatedStartGameTaskItem item in AllTasks)
        {
            item.IsSelected = selectAll;
        }
        RefreshCommandStates();
    }

    private void Button_DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ElevatedStartGameTaskItem item } element)
        {
            BeginDelete([item.TaskPath], element);
        }
    }

    private void Button_DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            BeginDelete(AllTasks.Where(x => x.IsSelected).Select(x => x.TaskPath), element);
        }
    }

    private void Button_CancelDelete_Click(object sender, RoutedEventArgs e)
    {
        Flyout_ConfirmDelete.Hide();
        _pendingTaskPaths = [];
    }

    private async void Button_ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        Flyout_ConfirmDelete.Hide();
        IReadOnlyList<string> taskPaths = _pendingTaskPaths;
        _pendingTaskPaths = [];
        if (taskPaths.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            ElevatedStartGameTaskService.CleanupResult result = await Task.Run(
                () => ElevatedStartGameTaskService.DeleteStartGameTasks(taskPaths));
            await LoadTasksAsync();
            if (result.RemainingCount == 0)
            {
                ShowFeedback(InfoBarSeverity.Success, string.Format(Lang.StartGameTaskManager_Deleted, result.DeletedCount));
            }
            else
            {
                ShowFeedback(InfoBarSeverity.Warning, string.Format(Lang.StartGameTaskManager_Partial, result.DeletedCount, result.RemainingCount));
            }
        }
        catch (Exception ex) when (ElevatedStartGameTaskService.IsElevationCancelled(ex))
        {
            _logger.LogInformation(ex, "Elevated start-game task deletion cancelled by user");
            ShowFeedback(InfoBarSeverity.Informational, Lang.StartGameTaskManager_Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete elevated start-game tasks");
            ShowFeedback(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Button_Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    /// <summary>
    /// 记下待删除任务，并把确认气泡弹到触发的按钮上。
    /// </summary>
    /// <param name="taskPaths">待删除的计划任务完整路径。</param>
    /// <param name="anchor">确认气泡的锚点（单项删除按钮或「删除所选」按钮）。</param>
    private void BeginDelete(IEnumerable<string> taskPaths, FrameworkElement anchor)
    {
        if (!CanEdit)
        {
            return;
        }

        _pendingTaskPaths = taskPaths
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (_pendingTaskPaths.Count == 0)
        {
            return;
        }

        OnPropertyChanged(nameof(ConfirmationText));
        Flyout_ConfirmDelete.ShowAt(anchor);
    }

    private async Task RefreshAsync(bool clearFeedback)
    {
        IsBusy = true;
        if (clearFeedback)
        {
            InfoBar_Feedback.IsOpen = false;
        }
        try
        {
            await LoadTasksAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List elevated start-game tasks");
            ShowFeedback(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadTasksAsync()
    {
        IReadOnlyList<ElevatedStartGameTaskService.StartGameTaskInfo> tasks = await Task.Run(
            ElevatedStartGameTaskService.ListStartGameTasks);

        TaskGroups.Clear();
        foreach (IGrouping<string, ElevatedStartGameTaskService.StartGameTaskInfo> group in tasks
            .OrderBy(x => x.GameBiz is null ? 1 : 0)
            .ThenBy(x => x.GameBiz?.Value, StringComparer.OrdinalIgnoreCase)
            .GroupBy(x => x.GameBiz?.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            GameBiz? gameBiz = group.First().GameBiz;
            var items = group
                .OrderBy(x => x.TaskPath, StringComparer.OrdinalIgnoreCase)
                .Select(CreateTaskItem)
                .ToList();
            foreach (ElevatedStartGameTaskItem item in items)
            {
                item.SelectionChanged += TaskItem_SelectionChanged;
            }
            TaskGroups.Add(new ElevatedStartGameTaskGroup(gameBiz, items));
        }

        OnPropertyChanged(nameof(HasTasks));
        RefreshCommandStates();
    }

    private ElevatedStartGameTaskItem CreateTaskItem(ElevatedStartGameTaskService.StartGameTaskInfo task)
    {
        return new ElevatedStartGameTaskItem(
            task.TaskPath,
            GetProfileDisplayName(task),
            task.LoginUid is > 0 ? string.Format(Lang.StartGameTaskManager_LoginUid, task.LoginUid.Value) : null);
    }

    /// <summary>
    /// 任务名中的配置标识换成用户看到的配置名（如「配置文件 1」或自定义名）。
    /// </summary>
    /// <param name="task">任务信息。</param>
    /// <returns>配置显示名；无法识别时回退为任务名。</returns>
    private static string GetProfileDisplayName(ElevatedStartGameTaskService.StartGameTaskInfo task)
    {
        string profileId = task.ProfileId?.Trim() ?? string.Empty;
        if (profileId.Length == 0)
        {
            return task.TaskName;
        }
        if (string.Equals(profileId, "follow", StringComparison.OrdinalIgnoreCase))
        {
            return Lang.StartGameMenu_FollowAppSetting;
        }
        if (GameLaunchProfile.IsNoneId(profileId))
        {
            return Lang.StartGameMenu_LaunchMethodNone;
        }
        if (task.GameBiz is GameBiz biz && GameLaunchProfile.TryGetIndex(profileId) is int index)
        {
            // 配置名保存在设置里：config1 存 legacy 键，其余在额外配置文件 JSON 中
            string? customName = index == 1
                ? AppConfig.GetDefaultLaunchProfileName(biz)
                : AppConfig.GetExtraLaunchProfiles(biz)
                    .FirstOrDefault(x => string.Equals(x.Id, profileId, StringComparison.OrdinalIgnoreCase))?.Name;
            return string.IsNullOrWhiteSpace(customName)
                ? string.Format(Lang.GameLauncherSettingDialog_ProfileNameFormat, index)
                : customName.Trim();
        }
        return profileId;
    }

    private void TaskItem_SelectionChanged(object? sender, EventArgs e)
    {
        RefreshCommandStates();
    }

    private void RefreshCommandStates()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(DeleteSelectedText));
        OnPropertyChanged(nameof(AllSelected));
    }

    private void ShowFeedback(InfoBarSeverity severity, string message)
    {
        InfoBar_Feedback.Severity = severity;
        InfoBar_Feedback.Title = string.Empty;
        InfoBar_Feedback.Message = message;
        InfoBar_Feedback.IsOpen = true;
    }
}

/// <summary>
/// 按游戏区服分组的免 UAC 启动任务。
/// </summary>
public sealed class ElevatedStartGameTaskGroup
{
    public ElevatedStartGameTaskGroup(GameBiz? gameBiz, IReadOnlyList<ElevatedStartGameTaskItem> tasks)
    {
        if (gameBiz is GameBiz biz)
        {
            var icon = new GameBizIcon(biz);
            GameIcon = icon.GameIcon;
            GameName = icon.GameName;
            ServerName = icon.ServerName;
        }
        else
        {
            // 无法识别游戏的历史任务：不显示图标，只给出统一标题
            GameName = Lang.StartGameTaskManager_UnknownGame;
            ServerName = string.Empty;
        }
        Tasks = tasks;
    }

    /// <summary>游戏图标；无法识别游戏时为 null，分组标题不显示图标。</summary>
    public string? GameIcon { get; }

    public string GameName { get; }

    public string ServerName { get; }

    public IReadOnlyList<ElevatedStartGameTaskItem> Tasks { get; }
}

public sealed partial class ElevatedStartGameTaskItem : ObservableObject
{
    public ElevatedStartGameTaskItem(string taskPath, string profileText, string? loginUidText)
    {
        TaskPath = taskPath;
        ProfileText = profileText;
        LoginUidText = loginUidText;
    }

    public event EventHandler? SelectionChanged;

    public string TaskPath { get; }

    public string ProfileText { get; }

    /// <summary>登录 UID 文案；任务未绑定 UID 时为 null，该列不显示。</summary>
    public string? LoginUidText { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
