using Microsoft.Extensions.Logging;
using Starward.Core;
using Starward.Core.HoYoPlay;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace Starward.RPC.GameInstall;

internal class GameUninstallService
{

    private readonly ILogger<GameUninstallService> _logger;


    private readonly HoYoPlayClient _hoYoPlayClient;


    public GameUninstallService(ILogger<GameUninstallService> logger, HoYoPlayClient hoYoPlayClient)
    {
        _logger = logger;
        _hoYoPlayClient = hoYoPlayClient;
    }





    public async Task UninstallGameAsync(UninstallGameRequest request, CancellationToken cancellationToken = default)
    {
        long start = Stopwatch.GetTimestamp();
        int fileCount = 0, screenshotCount = 0, cacheDirCount = 0;
        bool gameConfigLoaded = false;
        Exception? error = null;
        try
        {
            string installPath = request.InstallPath;
            if (!Directory.Exists(installPath))
            {
                _logger.LogWarning("Game folder does not exist: {installPath}", installPath);
            }
            if (Path.GetPathRoot(installPath) == installPath)
            {
                _logger.LogError("Game folder is the root of drive.");
                throw new InvalidOperationException("Game folder is the root of drive.");
            }
            _logger.LogInformation("Start to uninstall game ({gameBiz}): {installPath}", request.GameBiz, installPath);
            GameId gameId = new GameId { GameBiz = request.GameBiz, Id = request.GameId };
            GameConfig? gameConfig = null;
            try
            {
                gameConfig = await _hoYoPlayClient.GetGameConfigAsync(LauncherId.FromGameId(gameId)!, "en-us", gameId, cancellationToken);
                gameConfigLoaded = gameConfig is not null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get game config.");
            }
            string[] files = Directory.GetFiles(installPath, "*", SearchOption.AllDirectories);
            fileCount = files.Length;
            foreach (string file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            screenshotCount = BackupScreenshot(request, gameConfig);
            _logger.LogInformation("Deleting folder {installPath} ({count} files).", installPath, files.Length);
            Directory.Delete(installPath, true);
            cacheDirCount = ClearCacheDir(request, gameConfig);
            _logger.LogInformation("Finished uninstall game ({gameBiz}): {installPath}", request.GameBiz, installPath);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            Telemetry.Track("uninstall_end", request.GameBiz,
                ("result", error is null ? "success" : "error"),
                ("duration_ms", Stopwatch.GetElapsedTime(start)),
                ("files", fileCount),
                ("screenshots", screenshotCount),
                ("cache_dirs", cacheDirCount),
                ("keep_shared_log_folders", request.KeepSharedLogFolders),
                ("game_config", gameConfigLoaded),
                ("error_type", error?.GetType().Name),
                ("error", error?.Message));
        }
    }




    /// <summary>
    /// 删除前把游戏截图备份到截图目录（同盘 NTFS 优先硬链接）
    /// </summary>
    /// <param name="request">卸载请求</param>
    /// <param name="gameConfig">游戏配置，取截图目录；为 null 时在安装目录里找 Screenshot*</param>
    /// <returns>本次新备份的截图数</returns>
    private int BackupScreenshot(UninstallGameRequest request, GameConfig? gameConfig)
    {
        string userDataFolder = request.UserDataFolder;
        string sourceScreenshotFolder;
        if (gameConfig is null)
        {
            string[] dirs = Directory.GetDirectories(request.InstallPath, "Screenshot*", SearchOption.AllDirectories);
            sourceScreenshotFolder = dirs.FirstOrDefault() ?? "";
        }
        else
        {
            sourceScreenshotFolder = Path.Join(request.InstallPath, gameConfig.GameScreenshotDir);
        }

        string backupBaseFolder;
        string backupScreenshotFolder;
        if (Directory.Exists(request.ScreenshotFolder))
        {
            backupBaseFolder = request.ScreenshotFolder;
        }
        else
        {
            backupBaseFolder = Path.Join(userDataFolder, "Screenshots");
        }
        if (!string.IsNullOrWhiteSpace(request.GameExeName))
        {
            backupScreenshotFolder = Path.Join(backupBaseFolder, Path.GetFileNameWithoutExtension(request.GameExeName));
        }
        else if (!string.IsNullOrWhiteSpace(gameConfig?.ExeFileName))
        {
            backupScreenshotFolder = Path.Join(backupBaseFolder, Path.GetFileNameWithoutExtension(gameConfig.ExeFileName));
        }
        else
        {
            backupScreenshotFolder = Path.Join(backupBaseFolder, ((GameBiz)request.GameBiz).Game);
        }
        if (Directory.Exists(userDataFolder) && Directory.Exists(sourceScreenshotFolder))
        {
            bool canHardLink = CanHardLink(sourceScreenshotFolder, backupScreenshotFolder);
            Directory.CreateDirectory(backupScreenshotFolder);
            string[] files = Directory.GetFiles(sourceScreenshotFolder);
            int count = 0;
            foreach (string file in files)
            {
                string target = Path.Join(backupScreenshotFolder, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    bool result = false;
                    if (canHardLink)
                    {
                        result = Kernel32.CreateHardLink(target, file);
                    }
                    if (!result)
                    {
                        File.Copy(file, target, true);
                    }
                    count++;
                }
            }
            _logger.LogInformation("Backed up {count} screenshots.", count);
            return count;
        }
        return 0;
    }



    /// <summary>
    /// 清理游戏在安装目录外生成的日志与崩溃文件目录（路径须含厂商名，防止误删）
    /// </summary>
    /// <param name="request">卸载请求；<see cref="UninstallGameRequest.KeepSharedLogFolders"/> 为 true 时不清理</param>
    /// <param name="gameConfig">游戏配置，为 null 时不清理</param>
    /// <returns>删除的目录数</returns>
    private int ClearCacheDir(UninstallGameRequest request, GameConfig? gameConfig)
    {
        if (gameConfig is null)
        {
            return 0;
        }
        if (request.KeepSharedLogFolders)
        {
            // 国服与 B 服共用这些目录，另一个区服还装着时删掉会连带清掉它的日志
            _logger.LogInformation("Keep log folders of ({gameBiz}) because another server of the same game is still installed.", request.GameBiz);
            return 0;
        }
        int count = 0;
        if (TryDeleteGameFolder(gameConfig.GameLogGenDir))
        {
            count++;
        }
        // 厂商名检查同时防止原神把 %UserProfile%/AppData/Local/Temp 删了
        if (TryDeleteGameFolder(gameConfig.GameCrashFileGenDir))
        {
            count++;
        }
        return count;
    }


    /// <summary>
    /// 删除游戏配置中的一个安装目录外的目录。游戏目录此时已经删完，这里失败（例如另一个区服正开着日志文件）
    /// 只记录警告，不让整个卸载报错。
    /// </summary>
    /// <param name="configuredPath">配置里的路径，可含环境变量</param>
    /// <returns>是否删除了目录</returns>
    private bool TryDeleteGameFolder(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return false;
        }
        string path = Environment.ExpandEnvironmentVariables(configuredPath);
        if (!Path.IsPathFullyQualified(path)
            || !(path.Contains("miHoYo", StringComparison.OrdinalIgnoreCase)
                || path.Contains("Cognosphere", StringComparison.OrdinalIgnoreCase)
                || path.Contains("HoYoverse", StringComparison.OrdinalIgnoreCase))
            || !Directory.Exists(path))
        {
            return false;
        }
        try
        {
            Directory.Delete(path, true);
            _logger.LogInformation("Deleted folder {path}", path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to delete folder {path}", path);
            return false;
        }
    }




    private static bool CanHardLink(string source, string dest)
    {
        return Path.GetPathRoot(source) == Path.GetPathRoot(dest) && DriveHelper.GetDriveFormat(dest) is "NTFS";
    }




}
