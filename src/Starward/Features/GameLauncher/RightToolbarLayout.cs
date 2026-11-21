using System;
using System.Collections.Generic;
using System.Linq;

namespace Starward.Features.GameLauncher;

/// <summary>
/// 首页右侧功能栏在某个游戏下的排列：全部功能项的先后顺序，以及被用户关掉的项。
/// </summary>
/// <remarks>
/// 存成逗号串，如 <c>DailyNote,SignIn,-CloudGame</c>，前缀 <c>-</c> 表示隐藏。
/// 顺序里始终包含全部功能项（含当前游戏不支持的），这样不支持的项不会影响其他项的相对位置。
/// 纯计算，不读写设置，链进单元测试。
/// </remarks>
public sealed class RightToolbarLayout
{

    /// <summary>
    /// 默认顺序，与首页 XAML 里的排列一致。新增功能项要同时加在这里，没记录过它的旧设置会按这里的位置插入。
    /// </summary>
    public static IReadOnlyList<RightToolbarItem> DefaultOrder { get; } =
    [
        RightToolbarItem.CloudGame,
        RightToolbarItem.DailyNote,
        RightToolbarItem.TimeNode,
        RightToolbarItem.MapTool,
        RightToolbarItem.RedeemCode,
        RightToolbarItem.SignIn,
    ];


    private const char HiddenPrefix = '-';

    private static readonly Dictionary<string, RightToolbarItem> ItemsByName = DefaultOrder.ToDictionary(x => x.ToString(), StringComparer.Ordinal);


    private readonly List<RightToolbarItem> _order;

    private readonly HashSet<RightToolbarItem> _hidden;


    private RightToolbarLayout(List<RightToolbarItem> order, HashSet<RightToolbarItem> hidden)
    {
        _order = order;
        _hidden = hidden;
    }


    /// <summary>全部功能项的先后顺序。</summary>
    public IReadOnlyList<RightToolbarItem> Order => _order;


    /// <summary>与默认排列相同（默认顺序、没有隐藏项）。</summary>
    public bool IsDefault => _hidden.Count == 0 && _order.SequenceEqual(DefaultOrder);


    /// <summary>
    /// 创建默认排列。
    /// </summary>
    /// <returns>默认顺序、全部显示的排列。</returns>
    public static RightToolbarLayout CreateDefault()
    {
        return new RightToolbarLayout([.. DefaultOrder], []);
    }


    /// <summary>
    /// 解析设置里存的逗号串。空串或 null 得到默认排列。
    /// </summary>
    /// <remarks>
    /// 不认识的名字（已删掉的功能项）跳过，重复的只取第一次；
    /// 没记录过的功能项（之后新增的）插到默认顺序里它前面那一项的后面，默认显示。
    /// </remarks>
    /// <param name="value">设置值。</param>
    /// <returns>解析得到的排列，始终包含全部功能项。</returns>
    public static RightToolbarLayout Parse(string? value)
    {
        var order = new List<RightToolbarItem>(DefaultOrder.Count);
        var hidden = new HashSet<RightToolbarItem>();
        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (string token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                bool isHidden = token[0] == HiddenPrefix;
                string name = isHidden ? token[1..] : token;
                if (!ItemsByName.TryGetValue(name, out RightToolbarItem item) || order.Contains(item))
                {
                    continue;
                }
                order.Add(item);
                if (isHidden)
                {
                    hidden.Add(item);
                }
            }
        }
        for (int defaultIndex = 0; defaultIndex < DefaultOrder.Count; defaultIndex++)
        {
            RightToolbarItem item = DefaultOrder[defaultIndex];
            if (order.Contains(item))
            {
                continue;
            }
            int insertAt = 0;
            for (int i = defaultIndex - 1; i >= 0; i--)
            {
                int position = order.IndexOf(DefaultOrder[i]);
                if (position >= 0)
                {
                    insertAt = position + 1;
                    break;
                }
            }
            order.Insert(insertAt, item);
        }
        return new RightToolbarLayout(order, hidden);
    }


    /// <summary>
    /// 序列化为设置值，与 <see cref="Parse"/> 互逆。
    /// </summary>
    /// <returns>逗号串，隐藏项带前缀 <c>-</c>。</returns>
    public string Serialize()
    {
        return string.Join(',', _order.Select(x => _hidden.Contains(x) ? $"{HiddenPrefix}{x}" : x.ToString()));
    }


    /// <summary>
    /// 该项是否被用户关掉。
    /// </summary>
    /// <param name="item">功能项。</param>
    /// <returns>关掉时为 <see langword="true"/>。</returns>
    public bool IsHidden(RightToolbarItem item)
    {
        return _hidden.Contains(item);
    }


    /// <summary>
    /// 显示或隐藏某项。
    /// </summary>
    /// <param name="item">功能项。</param>
    /// <param name="hidden">是否隐藏。</param>
    public void SetHidden(RightToolbarItem item, bool hidden)
    {
        if (hidden)
        {
            _hidden.Add(item);
        }
        else
        {
            _hidden.Remove(item);
        }
    }


    /// <summary>
    /// 按新的先后重排一部分功能项（如对话框里列出的、当前游戏支持的项），其余项原位不动：
    /// 这部分项依次填回它们原来占的位置。
    /// </summary>
    /// <param name="subsetOrder">这部分项的新顺序；不认识或重复的项忽略。</param>
    public void ReorderSubset(IReadOnlyList<RightToolbarItem> subsetOrder)
    {
        List<RightToolbarItem> subset = subsetOrder.Where(_order.Contains).Distinct().ToList();
        var members = subset.ToHashSet();
        int next = 0;
        for (int i = 0; i < _order.Count; i++)
        {
            if (members.Contains(_order[i]))
            {
                _order[i] = subset[next++];
            }
        }
    }


}
