using CommunityToolkit.Mvvm.ComponentModel;
using Starward.Core.GameRecord.SignIn;

namespace Starward.Features.GameRecord.SignIn;

/// <summary>
/// 签到日历中的单日奖励视图模型
/// </summary>
public class SignInAwardView : ObservableObject
{

    /// <summary>
    /// 第几天（1-based）
    /// </summary>
    public int Day { get; set; }

    /// <summary>奖励图标 URL。</summary>
    public string? Icon { get; set; }

    /// <summary>奖励名称。</summary>
    public string? Name { get; set; }

    /// <summary>奖励数量。</summary>
    public int Count { get; set; }


    private bool _isClaimed;
    /// <summary>
    /// 是否已领取（已签到的天数）
    /// </summary>
    public bool IsClaimed
    {
        get => _isClaimed;
        set => SetProperty(ref _isClaimed, value);
    }


    /// <summary>
    /// 从 API 奖励 DTO 创建日历单元格视图模型。
    /// </summary>
    /// <param name="award">单日奖励数据。</param>
    /// <param name="index">在列表中的 0-based 索引，用于计算 Day。</param>
    /// <returns>未标记领取状态的视图模型。</returns>
    public static SignInAwardView Create(SignInAward award, int index)
    {
        return new SignInAwardView
        {
            Day = index + 1,
            Icon = award.Icon,
            Name = award.Name,
            Count = award.Count,
        };
    }

}
