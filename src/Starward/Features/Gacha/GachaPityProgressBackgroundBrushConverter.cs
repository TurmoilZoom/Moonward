using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Starward.Core.Gacha.Genshin;
using Starward.Core.Gacha.StarRail;
using Starward.Core.Gacha.ZZZ;
using System;
using Windows.Foundation;
using Windows.UI;

namespace Starward.Features.Gacha;

internal partial class GachaPityProgressBackgroundBrushConverter : IValueConverter
{

    private static Color Red = Color.FromArgb(0xFF, 0xC8, 0x3C, 0x23);
    private static Color Green = Color.FromArgb(0xFF, 0x00, 0xE0, 0x79);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is GachaLogItemEx item)
        {
            int pity = item.Pity;
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), Opacity = 0.4 };
            var (point, guarantee) = GetPityThresholds(item.GachaType);
            double offset = pity / (double)guarantee;
            if (pity < point)
            {
                brush.GradientStops.Add(new GradientStop { Color = Green, Offset = 0 });
                brush.GradientStops.Add(new GradientStop { Color = Green, Offset = offset });
                brush.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = offset });
            }
            else
            {
                brush.GradientStops.Add(new GradientStop { Color = Red, Offset = 0 });
                brush.GradientStops.Add(new GradientStop { Color = Red, Offset = offset });
                brush.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = offset });
            }
            return brush;
        }
        return null!;
    }


    /// <summary>
    /// 按卡池取保底色条的变红点与硬保底抽数（分享图绘制同样使用）。
    /// </summary>
    /// <param name="gachaType">记录所属卡池（千星奇域为 1000 / 2000）。</param>
    /// <returns>Point：抽数达到即画红色；Guarantee：色条满格对应的硬保底抽数。</returns>
    public static (int Point, int Guarantee) GetPityThresholds(int gachaType)
    {
        if (gachaType is GenshinGachaType.WeaponEventWish or StarRailGachaType.LightConeEventWarp or StarRailGachaType.LightConeCollaborationWarp)
        {
            return (63, 80);
        }
        if (gachaType is ZZZGachaType.WEngineChannel or ZZZGachaType.WEngineReverberation or ZZZGachaType.BangbooChannel)
        {
            return (65, 80);
        }
        if (gachaType is GenshinBeyondGachaType.StandardOde or GenshinBeyondGachaType.EventOde)
        {
            return (64, 70);
        }
        return (74, 90);
    }


    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

}
