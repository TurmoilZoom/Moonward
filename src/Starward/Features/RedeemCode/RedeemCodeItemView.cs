using CommunityToolkit.Mvvm.ComponentModel;

namespace Starward.Features.RedeemCode;

/// <summary>
/// Flyout 列表项视图（x:Bind；须 public 供 XAML 生成代码访问）。
/// </summary>
public partial class RedeemCodeItemView : ObservableObject
{

    public string RewardText { get; set; } = "";

    public string Code { get; set; } = "";

    public bool HasRewardText => !string.IsNullOrWhiteSpace(RewardText);


    /// <summary>
    /// 为 true 时右侧显示对勾（刚复制成功）。
    /// </summary>
    [ObservableProperty]
    private bool isCopied;


    internal static RedeemCodeItemView FromModel(RedeemCodeItem item) => new()
    {
        RewardText = item.RewardText,
        Code = item.Code,
    };

}
