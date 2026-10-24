using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Starward.Features.Database;

/// <summary>
/// 备份与还原时，每张表、每个设置键怎么处理。只按名字判断，不碰数据库和文件，便于单元测试。
/// </summary>
/// <remarks>
/// 备份要能拿到别的电脑上用，所以不带登录凭证、设备指纹这类会过期或绑定本机的东西，也不带本机缓存。
/// 新增保存凭证或本机状态的表、设置键时，要在这里登记，否则会被当成普通数据备份出去。
/// </remarks>
internal static class BackupDataPolicy
{

    /// <summary>
    /// 备份文件里记录备份信息（时间、版本、机器名等）的表，主库没有这张表。
    /// </summary>
    public const string InfoTable = "MoonwardBackupInfo";


    /// <summary>
    /// 备份时清空、还原时保留本机内容的表。
    /// </summary>
    private static readonly HashSet<string> LocalTables = new(StringComparer.OrdinalIgnoreCase)
    {
        // 米游社 / HoYoLAB 账号与角色，行里带 Cookie
        "GameRecordUser",
        "GameRecordRole",
        // 抽卡记录链接里的 authkey 大约一天就过期
        "GachaLogUrl",
        // 本机缓存：游戏信息、壁纸列表、签到奖励、时长总计、上次备份记录
        "KVT",
    };


    /// <summary>
    /// 凭证或本机状态，不进备份、还原时保留本机值的设置键。
    /// </summary>
    private static readonly HashSet<string> LocalSettingKeys = new(StringComparer.Ordinal)
    {
        // 更新流程的状态：恢复旧值会再弹一次更新说明，或让已下载的更新对不上
        "LastAppVersion",
        "PendingSilentUpdateContent",
        "LastUpdateCheckTime",
        // ETag 对应本机已下载的千星奇域图鉴文件
        "GenshinBeyondGachaInfoSource",
        "GenshinBeyondGachaInfoETag",
        "GenshinBeyondGachaInfoLastCheckTime",
        // 定时提交的运行记录
        "ZZZGachaMetadataAutoPublishLastSuccessPeriod",
        "ZZZGachaMetadataAutoPublishLastRunTicks",
        "ZZZGachaMetadataAutoPublishLastRunMessage",
        "auto_record_refresh_errors",
        // 备份位置本身：还原后还要能找到刚才那份备份
        "BackupFolder",
    };


    /// <summary>
    /// 按前缀判断的本机设置键，含义同 <see cref="LocalSettingKeys"/>。
    /// </summary>
    private static readonly string[] LocalSettingPrefixes =
    [
        // 米游社设备指纹（HyperionDeviceId、HyperionDeviceFp 及其更新时间、种子等）
        "HyperionDevice",
        // 云游戏凭证，等同云游戏账号的登录态
        "cloud_game_combo_token_",
        // 签到失败后的冷却时间
        "auto_sign_in_last_failure_ticks_",
        // 云游戏免费时长的当日领取记录：每天都会变，备份进去只会让「数据有变化」天天成立
        "cloud_game_free_time_claimed_date_",
        // 已下载的官方背景、版本海报的文件名与背景 ID 列表，文件只在本机缓存里
        "bg_",
        "version_poster_",
        "game_background_ids_",
    ];


    /// <summary>
    /// 本机路径类设置键。
    /// </summary>
    private static readonly HashSet<string> PathSettingKeys = new(StringComparer.Ordinal)
    {
        "DefaultGameInstallationPath",
        "ScreenshotFolder",
    };


    /// <summary>
    /// 按前缀判断的本机路径类设置键。
    /// </summary>
    private static readonly string[] PathSettingPrefixes =
    [
        "last_install_path_",
        "third_party_tool_path_",
        "external_screenshot_folder_",
    ];


    /// <summary>
    /// 某区服安装目录这一组键的前缀。较长的前缀排在前面，免得 <c>install_path_removable_</c> 被当成 <c>install_path_</c>。
    /// </summary>
    private static readonly string[] InstallPathPrefixes =
    [
        "install_path_removable_",
        "install_hard_link_path_",
        "install_hard_link_",
        "install_path_",
    ];


    /// <summary>
    /// 自定义背景这一对键的前缀。
    /// </summary>
    private static readonly string[] CustomBackgroundPrefixes =
    [
        "enable_custom_bg_",
        "custom_bg_",
    ];


    /// <summary>
    /// 自定义背景文件名所在的键前缀，与 <c>AppConfig.GetCustomBg</c> 一致。
    /// </summary>
    public const string CustomBackgroundFilePrefix = "custom_bg_";


    /// <summary>
    /// 安装目录所在的键前缀，与 <c>AppConfig.GetGameInstallPath</c> 一致。
    /// </summary>
    public const string InstallPathPrefix = "install_path_";



    /// <summary>
    /// 判断一张表在备份与还原时的处理方式。
    /// </summary>
    /// <param name="table">表名。</param>
    /// <returns>处理方式。</returns>
    public static BackupTableKind ClassifyTable(string table)
    {
        if (table.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase)
            || table.EndsWith("_dg_tmp", StringComparison.OrdinalIgnoreCase)
            || table.Equals(InfoTable, StringComparison.OrdinalIgnoreCase))
        {
            return BackupTableKind.Ignored;
        }
        if (table.Equals("Setting", StringComparison.OrdinalIgnoreCase))
        {
            return BackupTableKind.Setting;
        }
        if (LocalTables.Contains(table))
        {
            return BackupTableKind.Local;
        }
        return BackupTableKind.Data;
    }



    /// <summary>
    /// 判断一个设置键在备份与还原时的处理方式。
    /// </summary>
    /// <param name="key">设置键。</param>
    /// <param name="group">
    /// <see cref="BackupSettingKind.InstallPath"/> 与 <see cref="BackupSettingKind.CustomBackground"/> 时为区服（键去掉前缀后的部分），
    /// 同一区服的几个键要一起取舍；其他情况为 null。
    /// </param>
    /// <returns>处理方式。</returns>
    public static BackupSettingKind ClassifySetting(string key, out string? group)
    {
        group = null;
        if (LocalSettingKeys.Contains(key) || StartsWithAny(key, LocalSettingPrefixes, out _))
        {
            return BackupSettingKind.Local;
        }
        if (PathSettingKeys.Contains(key) || StartsWithAny(key, PathSettingPrefixes, out _))
        {
            return BackupSettingKind.Path;
        }
        if (StartsWithAny(key, InstallPathPrefixes, out string? prefix))
        {
            group = key[prefix.Length..];
            return BackupSettingKind.InstallPath;
        }
        if (StartsWithAny(key, CustomBackgroundPrefixes, out prefix))
        {
            group = key[prefix.Length..];
            return BackupSettingKind.CustomBackground;
        }
        return BackupSettingKind.Normal;
    }



    /// <summary>
    /// 设置键是否会写进备份。
    /// </summary>
    /// <param name="key">设置键。</param>
    /// <returns>凭证与本机状态返回 false，其余返回 true。</returns>
    public static bool IsSettingBackedUp(string key)
    {
        return ClassifySetting(key, out _) is not BackupSettingKind.Local;
    }



    /// <summary>
    /// 键是否以任一前缀开头，且前缀后面还有内容。
    /// </summary>
    /// <param name="key">设置键。</param>
    /// <param name="prefixes">候选前缀，按顺序匹配。</param>
    /// <param name="matched">匹配到的前缀。</param>
    /// <returns>匹配到时返回 true。</returns>
    private static bool StartsWithAny(string key, string[] prefixes, [NotNullWhen(true)] out string? matched)
    {
        foreach (string prefix in prefixes)
        {
            if (key.Length > prefix.Length && key.StartsWith(prefix, StringComparison.Ordinal))
            {
                matched = prefix;
                return true;
            }
        }
        matched = null;
        return false;
    }

}



/// <summary>
/// 表在备份与还原时的处理方式。
/// </summary>
internal enum BackupTableKind
{
    /// <summary>业务数据：原样备份；覆盖还原时整表替换。</summary>
    Data,

    /// <summary>设置表：按 <see cref="BackupSettingKind"/> 逐键处理。</summary>
    Setting,

    /// <summary>凭证或本机缓存：备份时清空，还原时保留本机内容。</summary>
    Local,

    /// <summary>SQLite 内部表、迁移临时表、备份信息表：不参与。</summary>
    Ignored,
}



/// <summary>
/// 设置键在备份与还原时的处理方式。
/// </summary>
internal enum BackupSettingKind
{
    /// <summary>普通设置：备份；覆盖还原时以备份为准，备份里没有的键恢复默认值。</summary>
    Normal,

    /// <summary>凭证或本机状态：不备份；还原时保留本机的值。</summary>
    Local,

    /// <summary>本机路径：备份；还原时本机已有就不动，本机没有且备份里的路径在本机存在时才用备份的。</summary>
    Path,

    /// <summary>某区服的安装目录、可移动存储标记、硬链接记录：同 <see cref="Path"/>，按区服整组取舍，以安装目录是否存在为准。</summary>
    InstallPath,

    /// <summary>某区服的自定义背景文件名与开关：背景文件不进备份，备份里的文件在本机还在时才整对用备份的。</summary>
    CustomBackground,
}
