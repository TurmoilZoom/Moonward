using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Starward.Core;
using Starward.Core.Gacha;
using Starward.Core.Gacha.Genshin;
using Starward.Features.Gacha.UIGF;
using Starward.Features.GameLauncher;
using Starward.Frameworks;
using Starward.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;


namespace Starward.Features.Gacha;

public sealed partial class GenshinBeyondGachaPage : PageBase
{


    private readonly ILogger<GenshinBeyondGachaPage> _logger = AppConfig.GetLogger<GenshinBeyondGachaPage>();

    private readonly GenshinBeyondGachaService _gachaLogService = AppConfig.GetService<GenshinBeyondGachaService>();

    private readonly GameLauncherService _gameLauncherService = AppConfig.GetService<GameLauncherService>();



    private GachaStatsSegmentedListHelper.GachaStatsSegmentedListBinding? _segmentedListBinding1000;

    private GachaStatsSegmentedListHelper.GachaStatsSegmentedListBinding? _segmentedListBinding2000;

    private GachaPityBarAnimation.GachaPityBarBinding? _pityBarBinding1000;

    private GachaPityBarAnimation.GachaPityBarBinding? _pityBarBinding2000;


    public GenshinBeyondGachaPage()
    {
        InitializeComponent();
        _segmentedListBinding1000 = GachaStatsSegmentedListHelper.Bind(Segmented_GachaItemList1000, ItemsRepeater_List_4_1000, ItemsRepeater_List_3_1000);
        _segmentedListBinding2000 = GachaStatsSegmentedListHelper.Bind(Segmented_GachaItemList2000, ItemsRepeater_List_5_2000, ItemsRepeater_List_4_2000);
        _pityBarBinding1000 = GachaPityBarAnimation.Bind(ItemsRepeater_List_4_1000);
        _pityBarBinding2000 = GachaPityBarAnimation.Bind(ItemsRepeater_List_5_2000);
    }


    public ObservableCollection<long> UidList { get; set => SetProperty(ref field, value); }


    [ObservableProperty]
    public partial long? SelectUid { get; set; }
    partial void OnSelectUidChanged(long? value)
    {
        AppConfig.SetLastUidInGachaLogPage("hk4eugc", value ?? 0);
        UpdateGachaTypeStats(value);
    }




    protected override async void OnLoaded()
    {
        await Task.Delay(16);
        WeakReferenceMessenger.Default.Register<GachaLogImportedMessage>(this, (s, m) => OnGachaLogImported(m));
        Initialize();
        await EnsureGachaInfoAsync();
    }



    protected override void OnUnloaded()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _segmentedListBinding1000?.Dispose();
        _segmentedListBinding1000 = null;
        _segmentedListBinding2000?.Dispose();
        _segmentedListBinding2000 = null;
        _pityBarBinding1000?.Dispose();
        _pityBarBinding1000 = null;
        _pityBarBinding2000?.Dispose();
        _pityBarBinding2000 = null;
        GachaStatsType1000 = null;
        GachaStatsType2000 = null;
        GachaItemStats = null;
    }


    /// <summary>
    /// UIGF 等本地导入完成后刷新本页（仅处理 hk4eugc 归档）。
    /// </summary>
    private void OnGachaLogImported(GachaLogImportedMessage message)
    {
        try
        {
            var uids = message.ImportedUids
                              .Where(x => x.Game.Value == "hk4eugc" && x.Uid > 0)
                              .Select(x => x.Uid)
                              .Distinct()
                              .ToList();
            if (uids.Count == 0)
            {
                return;
            }
            UidList ??= [];
            foreach (long uid in uids)
            {
                if (!UidList.Contains(uid))
                {
                    UidList.Add(uid);
                }
            }
            long target = SelectUid is long current && current != 0 && uids.Contains(current)
                ? current
                : uids[0];
            if (SelectUid == target)
            {
                UpdateGachaTypeStats(target);
            }
            else
            {
                SelectUid = target;
            }
            StackPanel_Emoji.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh after beyond gacha import");
        }
    }



    private void Initialize()
    {
        try
        {
            SelectUid = null;
            UidList = new(_gachaLogService.GetUids());
            var lastUid = AppConfig.GetLastUidInGachaLogPage("hk4eugc");
            if (UidList.Contains(lastUid))
            {
                SelectUid = lastUid;
            }
            else
            {
                SelectUid = UidList.FirstOrDefault();
            }
            if (UidList.Count == 0)
            {
                StackPanel_Emoji.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initialize");
        }
    }


    public GenshinBeyondGachaTypeStats? GachaStatsType1000 { get; set => SetProperty(ref field, value); }

    public GenshinBeyondGachaTypeStats? GachaStatsType2000 { get; set => SetProperty(ref field, value); }

    public List<GenshinBeyondGachaItemEx>? GachaItemStats { get; set => SetProperty(ref field, value); }


    private int errorCount = 0;


    private void UpdateGachaTypeStats(long? uid)
    {
        try
        {
            GachaStatsType1000 = null;
            GachaStatsType2000 = null;

            if (uid.HasValue && uid.Value != 0)
            {
                GachaStatsType1000 = _gachaLogService.GetGachaTypeStatsType1000(uid.Value);
                GachaStatsType2000 = _gachaLogService.GetGachaTypeStatsType2000(uid.Value);
                GachaItemStats = _gachaLogService.GetGachaItemStats(uid.Value);
            }

            if (GachaStatsType1000 is null && GachaStatsType2000 is null)
            {
                StackPanel_Emoji.Visibility = Visibility.Visible;
            }
            else
            {
                StackPanel_Emoji.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateGachaTypeStats");
        }
    }




    /// <summary>
    /// 兜底：首次启动时 <see cref="GachaItemNameService"/> 已全量下载物品信息（图标），
    /// 此处仅在表仍为空（如启动时无网络）时重试下载；新物品由更新记录时按需增量补全。
    /// </summary>
    private async Task EnsureGachaInfoAsync()
    {
        try
        {
            await _gachaLogService.EnsureGachaInfoAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update wiki data hk4eugc");
        }
    }



    /// <summary>
    /// 更新 Beyond 抽卡记录；默认从 webCaches 多候选校验后取有效 URL。
    /// </summary>
    /// <param name="param"><c>"cache"</c> 用已保存 URL；<c>"all"</c> 全量；其余从网页缓存获取。</param>
    [RelayCommand]
    private async Task UpdateGachaLogAsync(string? param = null)
    {
        try
        {
            string? url = null;
            if (param is "cache")
            {
                if (SelectUid is null or 0)
                {
                    return;
                }
                url = _gachaLogService.GetGachaLogUrlByUid(SelectUid.Value);
                if (string.IsNullOrWhiteSpace(url))
                {
                    // 无法找到 uid {uid} 的已缓存 URL
                    InAppToast.MainWindow?.Warning(null, string.Format(Lang.GachaLogPage_CannotFindSavedURLOfUid, SelectUid));
                    return;
                }
            }
            else
            {
                var path = GameLauncherService.GetGameInstallPath(CurrentGameId);
                if (!Directory.Exists(path))
                {
                    // 游戏未安装
                    InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_GameNotInstalled);
                    return;
                }
                InfoBar? validatingBar = null;
                try
                {
                    validatingBar = new InfoBar
                    {
                        Severity = InfoBarSeverity.Informational,
                        Message = Lang.GachaLogPage_ValidatingGachaUrl,
                        Background = Application.Current.Resources["CustomAcrylicBrush"] as Brush,
                        IsOpen = true,
                    };
                    InAppToast.MainWindow?.Show(validatingBar);
                    url = await _gachaLogService.GetValidatedGachaLogUrlFromWebCacheAsync(CurrentGameBiz, path);
                }
                catch (GachaApiException ex) when (ex.IsAuthkeyExpired)
                {
                    errorCount++;
                    if (errorCount > 1 && IsGachaCacheFileExists())
                    {
                        errorCount = 0;
                        InAppToast.MainWindow?.ShowWithButton(InfoBarSeverity.Warning,
                                                              Lang.GachaLogPage_AlwaysFailedToGetGachaRecords,
                                                              Lang.GachaLogPage_RestartGameAfterDeletingTheCacheFolder,
                                                              Lang.GachaLogPage_DeleteCacheFolder,
                                                              () => _ = DeleteGachaCacheFolderAsync());
                    }
                    else
                    {
                        ShowGachaFeedback(MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.GachaLog));
                    }
                    return;
                }
                finally
                {
                    // 无论成功、无候选还是异常，都关掉校验条（含外层 catch 前漏关）
                    if (validatingBar is not null)
                    {
                        validatingBar.IsOpen = false;
                    }
                }
                if (string.IsNullOrWhiteSpace(url))
                {
                    // 无法找到 URL，请在游戏中打开抽卡记录页面
                    errorCount++;
                    if (errorCount > 2 && IsGachaCacheFileExists())
                    {
                        errorCount = 0;
                        InAppToast.MainWindow?.ShowWithButton(InfoBarSeverity.Warning,
                                                              Lang.GachaLogPage_AlwaysFailedToGetGachaRecords,
                                                              Lang.GachaLogPage_RestartGameAfterDeletingTheCacheFolder,
                                                              Lang.GachaLogPage_DeleteCacheFolder,
                                                              () => _ = DeleteGachaCacheFolderAsync());
                    }
                    else
                    {
                        InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_CannotFindURL);
                    }
                    return;
                }
            }
            await UpdateGachaLogInternalAsync(url, param is "all");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update gacha log");
            InAppToast.MainWindow?.Error(ex);
        }
    }



    /// <summary>
    /// 执行千星奇域抽卡拉取（进度 InfoBar + 取消）。失败时关闭进度条，避免常驻。
    /// </summary>
    /// <param name="url">含 authkey 的抽卡 URL。</param>
    /// <param name="all">是否全量拉取。</param>
    private async Task UpdateGachaLogInternalAsync(string url, bool all = false)
    {
        InfoBar? progressInfoBar = null;
        bool keepProgressInfoBar = false;
        try
        {
            // 有效 authkey 时按 UID 缓存 URL；近 6 个月无记录则返回 0 且不落库
            await _gachaLogService.GetUidFromGachaLogUrl(url);
            var cancelSource = new CancellationTokenSource();
            var button = new Button
            {
                // 取消
                Content = Lang.Common_Cancel,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var infoBar = new InfoBar
            {
                Severity = InfoBarSeverity.Informational,
                Background = Application.Current.Resources["CustomAcrylicBrush"] as Brush,
                ActionButton = button,
            };
            button.Click += (_, _) =>
            {
                cancelSource.Cancel();
                // 操作已取消
                infoBar.Message = Lang.GachaLogPage_OperationCanceled;
                infoBar.ActionButton = null;
            };
            progressInfoBar = infoBar;
            InAppToast.MainWindow?.Show(infoBar);
            var progress = new Progress<string>((str) => infoBar.Message = str);
            var newUid = await _gachaLogService.GetGachaLogAsync(url, all, System.Globalization.CultureInfo.CurrentUICulture.Name, progress, cancelSource.Token);
            infoBar.Title = newUid > 0 ? $"Uid {newUid}" : null;
            infoBar.Severity = InfoBarSeverity.Success;
            infoBar.ActionButton = null;
            keepProgressInfoBar = true;
            ApplyFetchedGachaUid(newUid);
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("Get gacha log canceled");
            if (progressInfoBar is not null)
            {
                progressInfoBar.Message = Lang.GachaLogPage_OperationCanceled;
                progressInfoBar.ActionButton = null;
            }
            keepProgressInfoBar = true;
        }
        catch (GachaApiException ex)
        {
            _logger.LogWarning("Request mihoyo api error: {error}", ex.Message);
            if (ex.IsAuthkeyExpired)
            {
                // authkey timeout
                // 请在游戏中打开抽卡记录页面后再重试
                errorCount++;
                if (errorCount > 1 && IsGachaCacheFileExists())
                {
                    errorCount = 0;
                    InAppToast.MainWindow?.ShowWithButton(InfoBarSeverity.Warning,
                                                          Lang.GachaLogPage_AlwaysFailedToGetGachaRecords,
                                                          Lang.GachaLogPage_RestartGameAfterDeletingTheCacheFolder,
                                                          Lang.GachaLogPage_DeleteCacheFolder,
                                                          () => _ = DeleteGachaCacheFolderAsync());
                }
                else
                {
                    ShowGachaFeedback(MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.GachaLog));
                }
            }
            else
            {
                ShowGachaFeedback(MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.GachaLog));
            }
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Request beyond gacha log HTTP error");
            ShowGachaFeedback(MiHoYoApiErrorFeedbackFactory.Create(ex, MiHoYoApiContext.GachaLog));
        }
        finally
        {
            if (!keepProgressInfoBar && progressInfoBar is not null)
            {
                try
                {
                    progressInfoBar.ActionButton = null;
                    progressInfoBar.IsOpen = false;
                }
                catch
                {
                    // UI 已卸载时忽略
                }
            }
        }
    }


    /// <summary>
    /// 将拉取到的 UID 加入列表并选中。uid ≤ 0 表示近 6 个月无记录，不写入列表。
    /// </summary>
    /// <param name="uid">本次拉取得到的 UID。</param>
    private void ApplyFetchedGachaUid(long uid)
    {
        if (uid <= 0)
        {
            return;
        }
        UidList ??= [];
        if (SelectUid == uid)
        {
            UpdateGachaTypeStats(uid);
            return;
        }
        if (!UidList.Contains(uid))
        {
            UidList.Add(uid);
        }
        SelectUid = uid;
    }



    /// <summary>
    /// 显示千星奇域祈愿记录的 API 反馈。链接失效时仅展示错误信息，不再弹出「输入新链接」恢复按钮（可通过菜单「通过 URL 更新」手动处理）。
    /// </summary>
    /// <param name="feedback">已按祈愿记录场景分类的错误反馈。</param>
    private void ShowGachaFeedback(MiHoYoApiErrorFeedback feedback)
    {
        MiHoYoApiErrorFeedbackFactory.Show(feedback);
    }



    /// <summary>
    /// 通过 URL 更新 Beyond 抽卡记录：弹出对话框，预填当前 UID 已保存的 URL（若有），可直接确认或粘贴新 URL 后拉取。
    /// </summary>
    [RelayCommand]
    private async Task InputUrlAsync()
    {
        try
        {
            var textbox = new TextBox { MinWidth = 400 };
            // 预填当前 UID 已保存的 URL，合并「更新保存的 URL」与「输入 URL」
            if (SelectUid is > 0)
            {
                var saved = _gachaLogService.GetGachaLogUrlByUid(SelectUid.Value);
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    textbox.Text = saved;
                }
            }
            var dialog = new ContentDialog
            {
                // 通过 URL 更新
                Title = Lang.GachaLogPage_InputURL,
                Content = textbox,
                // 确认
                PrimaryButtonText = Lang.Common_Confirm,
                // 取消
                SecondaryButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var url = textbox.Text;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    await UpdateGachaLogInternalAsync(url);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Input url");
            InAppToast.MainWindow?.Error(ex);
        }
    }



    [RelayCommand]
    private async Task CopyUrlAsync()
    {
        try
        {
            if (SelectUid is null or 0)
            {
                return;
            }
            var url = _gachaLogService.GetGachaLogUrlByUid(SelectUid.Value);
            if (!string.IsNullOrWhiteSpace(url))
            {
                ClipboardHelper.SetText(url);
                FontIcon_CopyUrl.Glyph = "\uE8FB"; // accept
                await Task.Delay(1000);
                FontIcon_CopyUrl.Glyph = "\uE8C8";  // copy
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copy url");
        }
    }



    /// <summary>
    /// 打开 UIGF 导入导出窗口。千星奇域数据落在 <c>hk4e_ugc</c>，导出默认 v4.2；
    /// 导入不区分子版本，选文件后按内容自动识别（含 hk4e_ugc）。
    /// </summary>
    /// <param name="parameter">形如 <c>export|v4.2</c> / <c>import</c>。</param>
    [RelayCommand]
    private void OpenUIGF4Window(string? parameter)
    {
        UIGF4Version version = UIGF4Version.V42;
        bool openImport = false;
        if (!string.IsNullOrWhiteSpace(parameter))
        {
            string[] parts = parameter.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && parts[0].Equals("import", StringComparison.OrdinalIgnoreCase))
            {
                openImport = true;
            }
            string verText = parts.Length >= 2 ? parts[1] : parts[0];
            if (UIGF4VersionExtensions.TryParse(verText) is UIGF4Version parsed)
            {
                version = parsed;
            }
        }
        new UIGF4GachaWindow(version, openImport).Activate();
    }



    /// <summary>
    /// 一键删除游戏 webCaches（确认后真正删除；游戏运行中拒绝）。
    /// </summary>
    [RelayCommand]
    private async Task DeleteGachaCacheFolderAsync()
    {
        try
        {
            var installPath = GameLauncherService.GetGameInstallPath(CurrentGameId);
            if (!Directory.Exists(installPath))
            {
                InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_GameNotInstalled);
                return;
            }

            var webCachesPath = GenshinBeyondGachaClient.GetWebCachesFolderPath(CurrentGameBiz, installPath);
            string fullInstall = Path.GetFullPath(installPath);
            string fullCaches = Path.GetFullPath(webCachesPath);
            if (!fullCaches.StartsWith(fullInstall.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !fullCaches.EndsWith($"{Path.DirectorySeparatorChar}webCaches", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Refuse to delete unexpected webCaches path: {Path}", fullCaches);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = Lang.GachaLogPage_DeleteCacheFolderConfirmTitle,
                Content = Lang.GachaLogPage_DeleteCacheFolderConfirmContent,
                PrimaryButtonText = Lang.Common_Delete,
                SecondaryButtonText = Lang.GachaLogPage_OpenCacheFolder,
                CloseButtonText = Lang.Common_Cancel,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary)
            {
                await OpenGachaCacheFolderInExplorerAsync(webCachesPath);
                return;
            }
            if (result is not ContentDialogResult.Primary)
            {
                return;
            }

            var process = await _gameLauncherService.GetGameProcessAsync(CurrentGameId);
            if (process is not null)
            {
                InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_CannotDeleteCacheWhileGameIsRunning);
                return;
            }

            if (!Directory.Exists(webCachesPath))
            {
                InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_CacheFolderNotFound);
                return;
            }

            Directory.Delete(webCachesPath, recursive: true);
            _gachaLogService.DeleteSavedGachaLogUrl(SelectUid);
            errorCount = 0;
            InAppToast.MainWindow?.Success(null, Lang.GachaLogPage_CacheFolderDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete gacha cache file");
            InAppToast.MainWindow?.Error(ex);
        }
    }



    /// <summary>
    /// 在资源管理器中打开并选中 webCaches。
    /// </summary>
    /// <param name="webCachesPath">webCaches 完整路径。</param>
    private async Task OpenGachaCacheFolderInExplorerAsync(string webCachesPath)
    {
        if (!Directory.Exists(webCachesPath))
        {
            InAppToast.MainWindow?.Warning(null, Lang.GachaLogPage_CacheFolderNotFound);
            return;
        }
        var folder = await StorageFolder.GetFolderFromPathAsync(webCachesPath);
        var option = new FolderLauncherOptions();
        option.ItemsToSelect.Add(folder);
        await Launcher.LaunchFolderAsync(await folder.GetParentAsync(), option);
    }



    /// <summary>
    /// 检查 Beyond 抽卡 data_2 缓存是否存在。
    /// </summary>
    /// <returns>存在返回 true。</returns>
    private bool IsGachaCacheFileExists()
    {
        try
        {
            var installPath = GameLauncherService.GetGameInstallPath(CurrentGameId);
            if (Directory.Exists(installPath))
            {
                var path = GenshinBeyondGachaClient.GetGachaCacheFilePath(CurrentGameBiz, installPath);
                return File.Exists(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check gacha cache file exists");
        }
        return false;
    }



    [RelayCommand]
    private void OpenItemStatsPane()
    {
        SplitView_Content.IsPaneOpen = true;
    }


}
