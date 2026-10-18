namespace Starward.Core.GameRecord.StarRail.ChallengePeak;

public class ChallengePeakRecordMob
{
    public ChallengePeakRecordMob(ChallengePeakMobInfo mobInfo, ChallengePeakMobRecord? mobRecord)
    {
        MobInfo = mobInfo;
        MobRecord = mobRecord;
    }

    public ChallengePeakMobInfo MobInfo { get; set; }

    public ChallengePeakMobRecord? MobRecord { get; set; }


    public bool HasChallengeRecord => MobRecord?.HasChallengeRecord ?? false;

    /// <summary>
    /// 是否快速通关；无记录时为 false。页面须绑这里而非 MobRecord.IsFast：x:Bind 路径中间为 null 时不更新目标，Visibility 会停在默认的 Visible。
    /// </summary>
    public bool IsFast => MobRecord?.IsFast ?? false;

}
