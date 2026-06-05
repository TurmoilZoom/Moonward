using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;


namespace Starward.Features.Gacha;

internal static class GachaStatsSegmentedListHelper
{

    public static void Bind(Segmented segmented, ItemsRepeater firstList, ItemsRepeater secondList)
    {
        void UpdateVisibility()
        {
            var showFirst = segmented.SelectedIndex == 0;
            firstList.Visibility = showFirst ? Visibility.Visible : Visibility.Collapsed;
            secondList.Visibility = showFirst ? Visibility.Collapsed : Visibility.Visible;
        }

        segmented.RegisterPropertyChangedCallback(Selector.SelectedIndexProperty, (_, _) => UpdateVisibility());
        UpdateVisibility();
    }

}