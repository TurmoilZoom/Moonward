using Starward.Features.Database;
using System;
using Xunit;

namespace Starward.Tests;

/// <summary>
/// 备份内容规则（<see cref="BackupDataPolicy"/>）与备份文件命名（<see cref="DatabaseBackupNaming"/>）：
/// 凭证与本机状态不进备份，路径与自定义背景按区服成组，前缀之间不会互相吃掉。
/// </summary>
public class BackupDataPolicyTests
{

    [Theory]
    [InlineData("GameRecordUser", nameof(BackupTableKind.Local))]
    [InlineData("GameRecordRole", nameof(BackupTableKind.Local))]
    [InlineData("GachaLogUrl", nameof(BackupTableKind.Local))]
    [InlineData("KVT", nameof(BackupTableKind.Local))]
    [InlineData("Setting", nameof(BackupTableKind.Setting))]
    [InlineData("sqlite_sequence", nameof(BackupTableKind.Ignored))]
    [InlineData("PlayTimeItem_dg_tmp", nameof(BackupTableKind.Ignored))]
    [InlineData(BackupDataPolicy.InfoTable, nameof(BackupTableKind.Ignored))]
    [InlineData("GenshinGachaItem", nameof(BackupTableKind.Data))]
    [InlineData("PlayTimeItem", nameof(BackupTableKind.Data))]
    [InlineData("GachaItemName", nameof(BackupTableKind.Data))]
    public void Table_IsClassified(string table, string expected)
    {
        // 枚举是 internal，公开的测试方法不能直接用它做参数
        Assert.Equal(Enum.Parse<BackupTableKind>(expected), BackupDataPolicy.ClassifyTable(table));
    }


    [Theory]
    // 设备指纹与云游戏凭证
    [InlineData("HyperionDeviceId")]
    [InlineData("HyperionDeviceFp")]
    [InlineData("HyperionDeviceFpLastUpdateTime")]
    [InlineData("HyperionDeviceFpPayloadVersion")]
    [InlineData("HyperionDeviceAndroidId")]
    [InlineData("cloud_game_combo_token_nap_cn_123456")]
    // 本机状态与缓存
    [InlineData("LastAppVersion")]
    [InlineData("LastUpdateCheckTime")]
    [InlineData("GenshinBeyondGachaInfoETag")]
    [InlineData("ZZZGachaMetadataAutoPublishLastRunTicks")]
    [InlineData("auto_sign_in_last_failure_ticks_hk4e_cn_100000001")]
    [InlineData("cloud_game_free_time_claimed_date_nap_cn_123456")]
    [InlineData("bg_hk4e_cn")]
    [InlineData("version_poster_hk4e_cn")]
    [InlineData("game_background_ids_hk4e_cn")]
    [InlineData("BackupFolder")]
    public void LocalSetting_IsNotBackedUp(string key)
    {
        Assert.Equal(BackupSettingKind.Local, BackupDataPolicy.ClassifySetting(key, out _));
        Assert.False(BackupDataPolicy.IsSettingBackedUp(key));
    }


    [Theory]
    [InlineData("EnablePreviewRelease")]
    [InlineData("CurrentGameBiz")]
    [InlineData("use_version_poster_hk4e_cn")]
    [InlineData("start_argument_hk4e_cn")]
    [InlineData("cloud_game_selected_account_nap_cn")]
    [InlineData("ZZZGachaMetadataAutoPublishEnabled")]
    [InlineData("IgnoreVersion")]
    public void NormalSetting_IsBackedUp(string key)
    {
        Assert.Equal(BackupSettingKind.Normal, BackupDataPolicy.ClassifySetting(key, out string? group));
        Assert.Null(group);
        Assert.True(BackupDataPolicy.IsSettingBackedUp(key));
    }


    [Theory]
    [InlineData("DefaultGameInstallationPath")]
    [InlineData("ScreenshotFolder")]
    [InlineData("last_install_path_hk4e_cn")]
    [InlineData("third_party_tool_path_hk4e_cn")]
    [InlineData("external_screenshot_folder_hk4e_cn")]
    public void PathSetting_IsBackedUp(string key)
    {
        Assert.Equal(BackupSettingKind.Path, BackupDataPolicy.ClassifySetting(key, out _));
        Assert.True(BackupDataPolicy.IsSettingBackedUp(key));
    }


    [Theory]
    [InlineData("install_path_hk4e_cn", "hk4e_cn")]
    [InlineData("install_path_removable_hk4e_cn", "hk4e_cn")]
    [InlineData("install_hard_link_hk4e_cn", "hk4e_cn")]
    [InlineData("install_hard_link_path_hk4e_cn", "hk4e_cn")]
    public void InstallPathKeys_GroupByBiz(string key, string biz)
    {
        Assert.Equal(BackupSettingKind.InstallPath, BackupDataPolicy.ClassifySetting(key, out string? group));
        Assert.Equal(biz, group);
    }


    [Fact]
    public void LastInstallPath_IsNotPartOfInstallGroup()
    {
        Assert.Equal(BackupSettingKind.Path, BackupDataPolicy.ClassifySetting("last_install_path_hk4e_cn", out string? group));
        Assert.Null(group);
    }


    [Theory]
    [InlineData("custom_bg_hkrpg_cn", "hkrpg_cn")]
    [InlineData("enable_custom_bg_hkrpg_cn", "hkrpg_cn")]
    public void CustomBackgroundKeys_GroupByBiz(string key, string biz)
    {
        Assert.Equal(BackupSettingKind.CustomBackground, BackupDataPolicy.ClassifySetting(key, out string? group));
        Assert.Equal(biz, group);
    }


    [Fact]
    public void BarePrefix_IsNormal()
    {
        // 前缀本身没有区服部分，不当成成组的键
        Assert.Equal(BackupSettingKind.Normal, BackupDataPolicy.ClassifySetting("install_path_", out _));
        Assert.Equal(BackupSettingKind.Normal, BackupDataPolicy.ClassifySetting("custom_bg_", out _));
    }



    [Fact]
    public void FileName_RoundTrips()
    {
        var time = new DateTime(2026, 10, 24, 15, 30, 5);
        foreach (BackupKind kind in new[] { BackupKind.Manual, BackupKind.BeforeUpgrade, BackupKind.BeforeRestore, BackupKind.BeforeLanSync })
        {
            string name = DatabaseBackupNaming.GetFileName(time, kind);
            Assert.True(DatabaseBackupNaming.TryParse(name, out DateTime parsedTime, out BackupKind parsedKind, out bool legacy));
            Assert.Equal(time, parsedTime);
            Assert.Equal(kind, parsedKind);
            Assert.False(legacy);
        }
        Assert.Equal("MoonwardBackup_20261024_153005.7z", DatabaseBackupNaming.GetFileName(time, BackupKind.Manual));
    }


    [Theory]
    [InlineData("StarwardDatabase_20260901_120000.7z", nameof(BackupKind.Manual))]
    [InlineData("StarwardDatabase_AutoBackup_20260901_120000.7z", nameof(BackupKind.LegacyAuto))]
    [InlineData("StarwardDatabase_BeforeLanSync_20260901_120000.db", nameof(BackupKind.BeforeLanSync))]
    public void LegacyFileName_IsRecognized(string name, string kind)
    {
        Assert.True(DatabaseBackupNaming.TryParse(name, out DateTime time, out BackupKind parsedKind, out bool legacy));
        Assert.Equal(new DateTime(2026, 9, 1, 12, 0, 0), time);
        Assert.Equal(Enum.Parse<BackupKind>(kind), parsedKind);
        Assert.True(legacy);
    }


    [Theory]
    [InlineData("MoonwardBackup_20261024_153005_Manual.7z")]
    [InlineData("MoonwardBackup_20261024_153005_LegacyAuto.7z")]
    [InlineData("MoonwardBackup_20261024_153005_Unknown.7z")]
    [InlineData("MoonwardBackup_20261024_153005.db")]
    [InlineData("StarwardDatabase_20260901_120000.db")]
    [InlineData("StarwardDatabase_BeforeLanSync_20260901_120000.7z")]
    [InlineData("MoonwardBackup_20261324_153005.7z")]
    [InlineData("notes.txt")]
    public void UnknownFileName_IsRejected(string name)
    {
        Assert.False(DatabaseBackupNaming.TryParse(name, out _, out _, out _));
    }


    [Fact]
    public void Prune_KeepsNewestOfSameKind_AndNeverLegacy()
    {
        var names = new System.Collections.Generic.List<string>();
        for (int i = 1; i <= 12; i++)
        {
            names.Add(DatabaseBackupNaming.GetFileName(new DateTime(2026, 10, i, 8, 0, 0), BackupKind.Manual));
        }
        for (int i = 1; i <= 5; i++)
        {
            names.Add(DatabaseBackupNaming.GetFileName(new DateTime(2026, 10, i, 9, 0, 0), BackupKind.BeforeRestore));
        }
        names.Add("StarwardDatabase_20200101_000000.7z");
        names.Add("StarwardDatabase_BeforeLanSync_20200101_000000.db");

        var manual = DatabaseBackupNaming.GetFilesToPrune(names, BackupKind.Manual);
        Assert.Equal(new[] { DatabaseBackupNaming.GetFileName(new DateTime(2026, 10, 2, 8, 0, 0), BackupKind.Manual), DatabaseBackupNaming.GetFileName(new DateTime(2026, 10, 1, 8, 0, 0), BackupKind.Manual) }, manual);

        var restore = DatabaseBackupNaming.GetFilesToPrune(names, BackupKind.BeforeRestore);
        Assert.Equal(2, restore.Count);
        Assert.All(restore, name => Assert.Contains("_BeforeRestore", name));

        Assert.Empty(DatabaseBackupNaming.GetFilesToPrune(names, BackupKind.BeforeLanSync));
    }

}
