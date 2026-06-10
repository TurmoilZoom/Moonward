using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Starward.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Gacha;

/// <summary>
/// 抽卡物品名称（多语言）协调服务。
/// <para>抽卡记录中物品名称跟随软件 UI 语言：本服务负责为三个游戏（原神/星铁/绝区零）按当前语言
/// <b>确保</b>「物品id → 名称」映射缓存（缺失才联网下载，已缓存则跳过），并在需要时<b>异步回写</b>
/// 数据库中已有记录的名称。回写进度通过 <see cref="GachaItemNameProgressMessage"/> 广播，
/// <see cref="GachaLogPage"/> 打开时可据此显示处理进度。</para>
/// <para>触发时机：软件启动（首次启动全量下载各游戏图标与名称、更新后自动迁移存量记录）、切换软件语言、导入数据后。
/// 千星奇域物品信息（仅图标，不分语言）也在首次启动时一并全量下载。</para>
/// 单例，注册于 AppConfig.ServiceProvider。
/// </summary>
internal class GachaItemNameService
{

    private readonly ILogger<GachaItemNameService> _logger;

    /// <summary>三个游戏的 (业务线, 抽卡服务) 映射。</summary>
    private readonly (GameBiz Biz, GachaLogService Service)[] _services;

    /// <summary>千星奇域：物品信息（图标）不分语言，仅参与首次启动的全量下载。</summary>
    private readonly GenshinBeyondGachaService _beyond;

    /// <summary>串行化，避免启动、切换语言、导入回写相互重入。</summary>
    private readonly SemaphoreSlim _semaphore = new(1, 1);


    public GachaItemNameService(ILogger<GachaItemNameService> logger,
                                GenshinGachaService genshin,
                                StarRailGachaService starRail,
                                ZZZGachaService zzz,
                                GenshinBeyondGachaService beyond)
    {
        _logger = logger;
        _services =
        [
            (GameBiz.hk4e, genshin),
            (GameBiz.hkrpg, starRail),
            (GameBiz.nap, zzz),
        ];
        _beyond = beyond;
    }


    /// <summary>当前软件 UI 语言（规整为受支持的语言键，如 "zh-cn"）。</summary>
    private static string CurrentLanguage => LanguageUtil.FilterLanguage(CultureInfo.CurrentUICulture.Name);



    /// <summary>
    /// 软件启动时调用：为三个游戏按当前语言确保名称缓存（缺失才下载，首次启动即全量下载图标与名称），
    /// 并确保千星奇域物品信息（图标，不分语言）。
    /// 若 <see cref="AppConfig.LastGachaNameLanguage"/> 与当前语言不一致（含首次启动/更新后为 null 的情况），
    /// 则一次性把所有存量记录名称迁移为当前语言。后台执行，异常仅记日志。
    /// </summary>
    public async Task EnsureCurrentLanguageOnStartupAsync()
    {
        string lang = CurrentLanguage;
        bool migrate = !string.Equals(AppConfig.LastGachaNameLanguage, lang, StringComparison.OrdinalIgnoreCase);
        bool success = await RunAllAsync(lang, rewrite: migrate);
        // 仅在全部成功后记录语言；若联网失败或进程中途退出，下次启动会因语言不一致而重试（回写幂等）。
        if (migrate && success)
        {
            AppConfig.LastGachaNameLanguage = lang;
        }
        // 千星奇域物品信息表为空（首次启动）时全量下载；失败则下次启动或打开页面时重试。
        try
        {
            await _beyond.EnsureGachaInfoAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ensure genshin beyond gacha info on startup");
        }
    }



    /// <summary>
    /// 切换软件语言时调用：为三个游戏确保新语言缓存并回写全部记录名称。
    /// </summary>
    /// <param name="lang">目标语言；为 null 时取当前 UI 语言。</param>
    public async Task ChangeLanguageAsync(string? lang = null)
    {
        string target = lang is null ? CurrentLanguage : LanguageUtil.FilterLanguage(lang);
        bool success = await RunAllAsync(target, rewrite: true);
        // 仅在全部成功后记录语言；若联网失败或进程中途退出，下次启动会因语言不一致而重试（回写幂等）。
        if (success)
        {
            AppConfig.LastGachaNameLanguage = target;
        }
    }



    /// <summary>
    /// 针对单个游戏回写名称（导入数据后调用，把导入记录的名称按 ItemId 替换为当前语言）。
    /// </summary>
    /// <param name="game">游戏业务线。</param>
    /// <param name="lang">目标语言；为 null 时取当前 UI 语言。</param>
    public async Task ApplyForGameAsync(GameBiz game, string? lang = null)
    {
        string target = lang is null ? CurrentLanguage : LanguageUtil.FilterLanguage(lang);
        GachaLogService? service = _services.FirstOrDefault(x => x.Biz.Game == game.Game).Service;
        if (service is null)
        {
            return;
        }
        await _semaphore.WaitAsync();
        try
        {
            var progress = new RelayProgress<GachaNameProgress>(p => WeakReferenceMessenger.Default.Send(new GachaItemNameProgressMessage(game, p)));
            await service.ApplyGachaItemNamesAsync(target, progress);
        }
        catch (Exception ex)
        {
            // 失败时发送终止进度，关闭页面上的进度条。
            WeakReferenceMessenger.Default.Send(new GachaItemNameProgressMessage(game, new GachaNameProgress(0, 0, true)));
            _logger.LogError(ex, "Apply gacha item names for {game} (lang={lang})", game, target);
        }
        finally
        {
            _semaphore.Release();
        }
    }



    /// <summary>
    /// 对三个游戏依次执行：确保缓存（rewrite=false）或确保缓存+回写名称（rewrite=true）。逐游戏容错。
    /// </summary>
    /// <returns>是否全部游戏均成功（任一失败返回 false，调用方据此决定是否记录语言以便下次重试）。</returns>
    private async Task<bool> RunAllAsync(string lang, bool rewrite)
    {
        await _semaphore.WaitAsync();
        bool allSucceeded = true;
        try
        {
            foreach ((GameBiz biz, GachaLogService service) in _services)
            {
                try
                {
                    if (rewrite)
                    {
                        var progress = new RelayProgress<GachaNameProgress>(p => WeakReferenceMessenger.Default.Send(new GachaItemNameProgressMessage(biz, p)));
                        await service.ApplyGachaItemNamesAsync(lang, progress);
                    }
                    else
                    {
                        await service.EnsureNameCacheAsync(lang);
                    }
                }
                catch (Exception ex)
                {
                    allSucceeded = false;
                    // 失败（如网络不通）时发送终止进度，关闭页面进度条，避免一直停在进行中。
                    if (rewrite)
                    {
                        WeakReferenceMessenger.Default.Send(new GachaItemNameProgressMessage(biz, new GachaNameProgress(0, 0, true)));
                    }
                    _logger.LogError(ex, "Update gacha item names for {biz} (lang={lang})", biz, lang);
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
        return allSucceeded;
    }



    /// <summary>同步转发 IProgress。</summary>
    private sealed class RelayProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;

        public RelayProgress(Action<T> action) => _action = action;

        public void Report(T value) => _action(value);
    }

}
