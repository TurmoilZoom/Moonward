using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Starward.Features.Database;

/// <summary>
/// 备份文件的命名、解析与保留份数。只处理文件名，不碰磁盘，便于单元测试。
/// </summary>
/// <remarks>
/// 新格式：<c>MoonwardBackup_20261024_153000.7z</c>（手动）、<c>MoonwardBackup_20261024_153000_BeforeUpgrade.7z</c>（其他类型）。
/// 旧格式（含 Cookie，只读取、不清理）：<c>StarwardDatabase_*.7z</c>、<c>StarwardDatabase_AutoBackup_*.7z</c>、<c>StarwardDatabase_BeforeLanSync_*.db</c>。
/// </remarks>
internal static partial class DatabaseBackupNaming
{

    private const string TimeFormat = "yyyyMMdd_HHmmss";


    /// <summary>
    /// 生成新格式的备份文件名。
    /// </summary>
    /// <param name="time">备份时间（本机时间）。</param>
    /// <param name="kind">备份类型。</param>
    /// <returns>文件名，不含目录。</returns>
    public static string GetFileName(DateTime time, BackupKind kind)
    {
        string stamp = time.ToString(TimeFormat, CultureInfo.InvariantCulture);
        return kind is BackupKind.Manual ? $"MoonwardBackup_{stamp}.7z" : $"MoonwardBackup_{stamp}_{kind}.7z";
    }



    /// <summary>
    /// 从文件名解析备份时间与类型。
    /// </summary>
    /// <param name="fileName">文件名，不含目录。</param>
    /// <param name="time">备份时间。</param>
    /// <param name="kind">备份类型。</param>
    /// <param name="isLegacy">是否为旧格式（含 Cookie 的整库备份）。</param>
    /// <returns>是可识别的备份文件时返回 true。</returns>
    public static bool TryParse(string fileName, out DateTime time, out BackupKind kind, out bool isLegacy)
    {
        time = default;
        kind = BackupKind.Manual;
        isLegacy = false;
        Match match = NewFileNameRegex().Match(fileName);
        if (match.Success)
        {
            // 手动备份不带类型后缀，旧版自动备份不会出现在新格式里
            if (match.Groups[2].Success && (!Enum.TryParse(match.Groups[2].Value, out kind) || kind is BackupKind.Manual or BackupKind.LegacyAuto))
            {
                return false;
            }
            return DateTime.TryParseExact(match.Groups[1].Value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
        }
        match = LegacyFileNameRegex().Match(fileName);
        if (match.Success)
        {
            isLegacy = true;
            kind = match.Groups[1].Value switch
            {
                "AutoBackup_" => BackupKind.LegacyAuto,
                "BeforeLanSync_" => BackupKind.BeforeLanSync,
                _ => BackupKind.Manual,
            };
            // 同步前副本未压缩（.db），手动与自动备份是 .7z，扩展名与类型对不上的不是本程序生成的
            bool isDb = match.Groups[3].Value.Equals("db", StringComparison.OrdinalIgnoreCase);
            if (isDb != (kind is BackupKind.BeforeLanSync))
            {
                return false;
            }
            return DateTime.TryParseExact(match.Groups[2].Value, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
        }
        return false;
    }



    /// <summary>
    /// 每种备份保留的份数：手动备份留得多，其他类型是操作前的保险，留几份就够。
    /// </summary>
    /// <param name="kind">备份类型。</param>
    /// <returns>保留份数。</returns>
    public static int GetKeepCount(BackupKind kind)
    {
        return kind is BackupKind.Manual ? 10 : 3;
    }



    /// <summary>
    /// 找出超出保留份数、该删掉的新格式备份。旧格式文件一律不删。
    /// </summary>
    /// <param name="fileNames">备份目录里的文件名。</param>
    /// <param name="kind">要清理的类型。</param>
    /// <returns>该删的文件名，旧的在后。</returns>
    public static List<string> GetFilesToPrune(IEnumerable<string> fileNames, BackupKind kind)
    {
        return fileNames
            .Select(name => (Name: name, Ok: TryParse(name, out DateTime time, out BackupKind k, out bool legacy), Time: time, Kind: k, Legacy: legacy))
            .Where(x => x.Ok && !x.Legacy && x.Kind == kind)
            .OrderByDescending(x => x.Time)
            .Skip(GetKeepCount(kind))
            .Select(x => x.Name)
            .ToList();
    }



    [GeneratedRegex(@"^MoonwardBackup_(\d{8}_\d{6})(?:_([A-Za-z]+))?\.7z$", RegexOptions.IgnoreCase)]
    private static partial Regex NewFileNameRegex();


    [GeneratedRegex(@"^StarwardDatabase_(AutoBackup_|BeforeLanSync_)?(\d{8}_\d{6})\.(7z|db)$", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyFileNameRegex();

}



/// <summary>
/// 备份类型。名称会写进文件名，不要改名。
/// </summary>
internal enum BackupKind
{
    /// <summary>设置页手动备份。</summary>
    Manual,

    /// <summary>数据库升级（执行新的迁移脚本）之前。</summary>
    BeforeUpgrade,

    /// <summary>还原之前，用来撤销这次还原。</summary>
    BeforeRestore,

    /// <summary>局域网同步合并之前。</summary>
    BeforeLanSync,

    /// <summary>旧版每 7 天的自动备份，只出现在旧文件里，不再生成。</summary>
    LegacyAuto,
}
