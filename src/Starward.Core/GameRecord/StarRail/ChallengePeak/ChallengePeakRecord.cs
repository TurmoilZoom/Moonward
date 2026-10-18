using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord.StarRail.ChallengePeak;

public class ChallengePeakRecord
{
    [JsonPropertyName("group")]
    public ChallengePeakGroup Group { get; set; }

    [JsonPropertyName("boss_info")]
    public ChallengePeakBossInfo BossInfo { get; set; }

    [JsonPropertyName("mob_infos")]
    public List<ChallengePeakMobInfo> MobInfos { get; set; }

    [JsonPropertyName("has_challenge_record")]
    public bool HasChallengeRecord { get; set; }

    [JsonPropertyName("battle_num")]
    public int BattleNum { get; set; }

    [JsonPropertyName("boss_record")]
    public ChallengePeakBossRecord BossRecord { get; set; }

    [JsonPropertyName("mob_records")]
    public List<ChallengePeakMobRecord> MobRecords { get; set; }

    [JsonPropertyName("boss_stars")]
    public int BossStars { get; set; }

    [JsonPropertyName("mob_stars")]
    public int MobStars { get; set; }


    [JsonExtensionData]
    public Dictionary<string, object>? ExtensionData { get; set; }


    /// <summary>
    /// 王棋以高难度通关时为难度名（如「将杀王棋·绝境」），否则为 null；无通关记录时同样为 null，页面据此隐藏难度前缀。
    /// </summary>
    [JsonIgnore]
    public string? BossHardModeName => BossRecord?.HardMode == true && !string.IsNullOrWhiteSpace(BossInfo?.HardModeNameMi18n) ? BossInfo.HardModeNameMi18n : null;


    [JsonIgnore]
    public List<ChallengePeakRecordMob> Mobs => MobInfos.LeftJoin(MobRecords, x => x.MazeId, y => y.MazeId, (x, y) => new ChallengePeakRecordMob(x, y)).ToList();
}
