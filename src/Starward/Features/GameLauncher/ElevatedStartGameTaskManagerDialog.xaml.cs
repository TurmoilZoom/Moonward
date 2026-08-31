using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core;
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

    private IReadOnlyList<string> _pendingTaskPaths = [];

    public ElevatedStartGameTaskManagerDialog()
    {
        InitializeComponent();
        Loaded += ElevatedStartGameTaskManagerDialog_Loaded;
    }

    public ObservableCollection<ElevatedStartGameTaskGroup> TaskGroups { get; } = [];

    public bool HasTasks => TaskGroups.Count > 0;

    public bool CanEdit => HasTasks && !IsBusy && !IsConfirming;

    public bool CanClose => !IsBusy;

    public bool CanDeleteSelected => CanEdit && TaskGroups.SelectMany(x => x.Tasks).Any(x => x.IsSelected);

    public string DeleteSelectedText => string.Format(
        Lang.StartGameTaskManager_DeleteSelected,
        TaskGroups.SelectMany(x => x.Tasks).Count(x => x.IsSelected));

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConfirming;

    [ObservableProperty]
    private string _confirmationText = string.Empty;

    partial void OnIsBusyChanged(bool value) => RefreshCommandStates();

    partial void OnIsConfirmingChanged(bool value) => RefreshCommandStates();

    private async void ElevatedStartGameTaskManagerDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync(clearFeedback: true);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (ElevatedStartGameTaskItem item in TaskGroups.SelectMany(x => x.Tasks))
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (ElevatedStartGameTaskItem item in TaskGroups.SelectMany(x => x.Tasks))
        {
            item.IsSelected = false;
        }
    }

    private void Button_DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ElevatedStartGameTaskItem item })
        {
            BeginDelete([item.TaskPath]);
        }
    }

    private void Button_DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        BeginDelete(TaskGroups.SelectMany(x => x.Tasks).Where(x => x.IsSelected).Select(x => x.TaskPath));
    }

    private void Button_DeleteAll_Click(object sender, RoutedEventArgs e)
    {
        BeginDelete(TaskGroups.SelectMany(x => x.Tasks).Select(x => x.TaskPath));
    }

    private void Button_CancelDelete_Click(object sender, RoutedEventArgs e)
    {
        IsConfirming = false;
        _pendingTaskPaths = [];
    }

    private async void Button_ConfirmDelete_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<string> taskPaths = _pendingTaskPaths;
        IsConfirming = false;
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

    private void BeginDelete(IEnumerable<string> taskPaths)
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

        ConfirmationText = string.Format(Lang.StartGameTaskManager_ConfirmMessage, _pendingTaskPaths.Count);
        IsConfirming = true;
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
            TaskGroups.Add(new ElevatedStartGameTaskGroup(GetGroupTitle(gameBiz), items));
        }

        OnPropertyChanged(nameof(HasTasks));
        RefreshCommandStates();
    }

    private ElevatedStartGameTaskItem CreateTaskItem(ElevatedStartGameTaskService.StartGameTaskInfo task)
    {
        string profile = string.Equals(task.ProfileId, "follow", StringComparison.OrdinalIgnoreCase)
            ? Lang.StartGameMenu_FollowAppSetting
            : string.IsNullOrWhiteSpace(task.ProfileId) ? task.TaskName : task.ProfileId;
        return new ElevatedStartGameTaskItem(
            task.TaskPath,
            string.Format(Lang.StartGameTaskManager_Profile, profile),
            task.LoginUid is > 0 ? string.Format(Lang.StartGameTaskManager_LoginUid, task.LoginUid.Value) : string.Empty);
    }

    private static string GetGroupTitle(GameBiz? gameBiz)
    {
        if (gameBiz is not GameBiz biz)
        {
            return Lang.StartGameTaskManager_UnknownGame;
        }

        string gameName = biz.ToGameName();
        string serverName = biz.ToGameServerName();
        return string.IsNullOrWhiteSpace(serverName) ? gameName : $"{gameName} · {serverName}";
    }

    private void TaskItem_SelectionChanged(object? sender, EventArgs e)
    {
        RefreshCommandStates();
    }

    private void RefreshCommandStates()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(DeleteSelectedText));
    }

    private void ShowFeedback(InfoBarSeverity severity, string message)
    {
        InfoBar_Feedback.Severity = severity;
        InfoBar_Feedback.Title = string.Empty;
        InfoBar_Feedback.Message = message;
        InfoBar_Feedback.IsOpen = true;
    }
}

public sealed class ElevatedStartGameTaskGroup
{
    public ElevatedStartGameTaskGroup(string title, IReadOnlyList<ElevatedStartGameTaskItem> tasks)
    {
        Title = title;
        Tasks = tasks;
    }

    public string Title { get; }

    public IReadOnlyList<ElevatedStartGameTaskItem> Tasks { get; }
}

[INotifyPropertyChanged]
public sealed partial class ElevatedStartGameTaskItem
{
    public ElevatedStartGameTaskItem(string taskPath, string profileText, string loginUidText)
    {
        TaskPath = taskPath;
        ProfileText = profileText;
        LoginUidText = loginUidText;
    }

    public event EventHandler? SelectionChanged;

    public string TaskPath { get; }

    public string ProfileText { get; }

    public string LoginUidText { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
