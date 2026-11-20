using System;
using System.Collections.Generic;
using System.Linq;

namespace Starward.Features.Gacha;

/// <summary>
/// 本地已有的一条绝区零调频记录（真实记录，按 Id 升序排列即抽取顺序）。
/// </summary>
/// <param name="Id">记录 Id。</param>
/// <param name="ItemId">物品 Id。</param>
/// <param name="RankType">稀有度，S/A/B 对应 4/3/2。</param>
/// <param name="Time">服务器本地时间。</param>
public readonly record struct ZZZGapLocalRecord(long Id, int ItemId, int RankType, DateTime Time);


/// <summary>
/// 官方抽卡统计里的一条 S 级记录。
/// </summary>
/// <param name="Sort">在该频段全部历史中的序号（从 1 开始）。</param>
/// <param name="ItemId">物品 Id。</param>
/// <param name="RecordTime">抽取时间（Unix 秒）。</param>
/// <param name="Count">出这个 S 级用了几抽（含本抽）。</param>
public readonly record struct ZZZGapOfficialS(long Sort, int ItemId, long RecordTime, int Count);


/// <summary>
/// 补全结果状态。
/// </summary>
public enum ZZZGapFillStatus
{
    /// <summary>已算出要补的记录。</summary>
    Filled,

    /// <summary>本地条数与官方一致，无需补全。</summary>
    NothingMissing,

    /// <summary>官方有比本地最后一条更新的记录，或缺口在最后一个 S 级之后（多半是最近几抽还没同步到），稍后再试。</summary>
    LocalBehind,

    /// <summary>本地与官方对不上（多出记录、S 级不在官方列表、垫抽校验不过等），无法确定缺口位置。</summary>
    Inconsistent,

    /// <summary>缺口两侧的真实记录 Id 之间放不下补全记录，或与已有 Id 冲突。</summary>
    NoIdSpace,

    /// <summary>对不上，但本地最新一条在 2 小时内：多半是官方统计还没更新到最近几抽（官方说明约有 1 小时延迟），稍后再试。</summary>
    OfficialBehind,
}


/// <summary>
/// 一条要写入的补全记录。
/// </summary>
/// <param name="Id">分配的记录 Id（个位与十位为频段专用后缀，不会是 100 的整数倍）。</param>
/// <param name="IsSRank">true 为按官方数据补的 S 级，false 为 A/B 级占位。</param>
/// <param name="Position">在该频段全部历史中的序号。</param>
/// <param name="ItemId">S 级为物品 Id，占位为 0。</param>
/// <param name="Time">S 级为官方抽取时间（服务器本地时间）；占位取相邻已知记录的时间。</param>
public sealed record ZZZGapFillRecord(long Id, bool IsSRank, long Position, int ItemId, DateTime Time);


/// <summary>
/// 单个频段的补全计算结果。
/// </summary>
public sealed class ZZZGapFillResult
{
    public ZZZGapFillStatus Status { get; init; }

    /// <summary>要写入的补全记录，按抽取顺序。</summary>
    public IReadOnlyList<ZZZGapFillRecord> Records { get; init; } = [];

    /// <summary>补全条数（含 S 级）。</summary>
    public int MissingCount => Records.Count;

    /// <summary>其中按官方数据补的 S 级条数。</summary>
    public int SRankCount => Records.Count(x => x.IsSRank);

    /// <summary>失败原因（写日志用，不展示给用户）。</summary>
    public string? Reason { get; init; }


    public static ZZZGapFillResult Fail(ZZZGapFillStatus status, string reason) => new() { Status = status, Reason = reason };
}


/// <summary>
/// 用官方抽卡统计（累计抽数 + 全部 S 级的序号、垫抽、时间）补全本地缺失的绝区零调频记录。
/// </summary>
/// <remarks>
/// <para>官方 S 级的 <c>sort</c> 是它在该频段全部历史里的序号，与本地同一 S 级的序号之差就是它之前缺了几抽。
/// 以本地 S 级与「本地最后一条 = 官方第 total 抽」为锚点，相邻锚点之间差值变大处就是缺口；锚点间缺了几抽可以精确算出。</para>
/// <para>缺口内具体缺在哪几抽拿不到（没有 A/B 级数据），但后一个 S 级的垫抽只取决于两个 S 级之间的总抽数，
/// 所以占位放在区间内任何位置都不影响 S 级的正确性；这里放在时间断档最大处（最前面的缺口放在最前）。
/// 缺口内的官方 S 级按时间插入，并把区间切成两段分别计算。</para>
/// <para>前提：调用前已做过一次普通同步，本地最后一条就是实际最后一抽。补完后逐个校验 S 级垫抽与官方 count 一致，不一致整个频段放弃。</para>
/// <para>补全记录以 <c>Count = 0</c> 且 Id 不是 100 的整数倍标记（真实记录 Count 为 1、Id 实测全是整百），见 <see cref="IsFillRecord"/>。</para>
/// </remarks>
public static class ZZZGachaGapFiller
{

    /// <summary>
    /// 是否为补全写入的记录（S 级或占位）。
    /// </summary>
    /// <param name="id">记录 Id。</param>
    /// <param name="count">记录 Count 字段。</param>
    /// <returns>补全记录返回 true。</returns>
    public static bool IsFillRecord(long id, int count) => count == 0 && id % 100 != 0;


    /// <summary>
    /// 是否为 A/B 级占位记录（不知道具体物品，只占一抽的位置）。
    /// </summary>
    /// <param name="id">记录 Id。</param>
    /// <param name="count">记录 Count 字段。</param>
    /// <param name="rankType">记录 RankType 字段。</param>
    /// <returns>占位记录返回 true。</returns>
    public static bool IsPlaceholder(long id, int count, int rankType) => IsFillRecord(id, count) && rankType == 0;


    /// <summary>S 级在本地记录中的 RankType。</summary>
    private const int SRankType = 4;


    private enum EntryKind { Real, NewS, Placeholder }


    /// <summary>补全后完整序列中的一项：真实记录（本地序号）、新补 S 级（官方序号）或占位。</summary>
    private readonly record struct Entry(EntryKind Kind, int Index);


    /// <summary>
    /// 计算单个频段要补的记录。
    /// </summary>
    /// <param name="local">本地真实记录（不含已有补全记录），按 Id 升序。</param>
    /// <param name="officialS">官方该频段全部 S 级记录。</param>
    /// <param name="officialTotal">官方该频段累计抽数。</param>
    /// <param name="fallbackUtcOffset">无法从本地 S 级反推时区时使用的服务器时区（如国服 +8）。</param>
    /// <param name="idSuffix">该频段补全记录 Id 的后两位（1~99，各频段互不相同）。</param>
    /// <param name="emptyIdUpperBound">本地该频段一条记录都没有时，补全 Id 须小于此值（取该 UID 最小真实 Id，避免影响增量同步）。</param>
    /// <param name="occupiedIds">该 UID 已占用的全部 Id（不含本频段旧的补全记录），用于冲突检查。</param>
    /// <param name="utcNow">当前 UTC 时间，用于判断本地最新记录是否刚抽；为 null 时不做此判断。</param>
    /// <returns>计算结果。</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="idSuffix"/> 不在 1~99。</exception>
    public static ZZZGapFillResult Compute(
        IReadOnlyList<ZZZGapLocalRecord> local,
        IReadOnlyList<ZZZGapOfficialS> officialS,
        long officialTotal,
        TimeSpan fallbackUtcOffset,
        int idSuffix,
        long emptyIdUpperBound,
        IReadOnlySet<long> occupiedIds,
        DateTime? utcNow = null)
    {
        ZZZGapFillResult result = ComputeCore(local, officialS, officialTotal, fallbackUtcOffset, idSuffix, emptyIdUpperBound, occupiedIds);
        // 官方统计比实际抽卡晚约 1 小时：刚抽完、同步后马上补全时本地会比官方多（条数多、末尾差值变小、新出的 S 级官方还没有），
        // 都表现为对不上。最新一条在 2 小时内就提示稍后再试，过了这个窗口仍对不上才算真的对不上。
        if (result.Status == ZZZGapFillStatus.Inconsistent && utcNow is DateTime now && local.Count > 0
            && now.Add(fallbackUtcOffset) - local[^1].Time < TimeSpan.FromHours(2))
        {
            return ZZZGapFillResult.Fail(ZZZGapFillStatus.OfficialBehind, $"recent local records, official stats may lag ({result.Reason})");
        }
        return result;
    }


    /// <summary>
    /// <see cref="Compute"/> 的主体，不含「官方统计延迟」的判断。
    /// </summary>
    private static ZZZGapFillResult ComputeCore(
        IReadOnlyList<ZZZGapLocalRecord> local,
        IReadOnlyList<ZZZGapOfficialS> officialS,
        long officialTotal,
        TimeSpan fallbackUtcOffset,
        int idSuffix,
        long emptyIdUpperBound,
        IReadOnlySet<long> occupiedIds)
    {
        if (idSuffix is < 1 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(idSuffix), idSuffix, "Id suffix must be between 1 and 99.");
        }
        int localCount = local.Count;
        if (officialTotal == localCount)
        {
            return new ZZZGapFillResult { Status = ZZZGapFillStatus.NothingMissing };
        }
        if (officialTotal < localCount)
        {
            return ZZZGapFillResult.Fail(ZZZGapFillStatus.Inconsistent, $"local count {localCount} > official total {officialTotal}");
        }
        if (officialTotal > int.MaxValue)
        {
            return ZZZGapFillResult.Fail(ZZZGapFillStatus.Inconsistent, $"official total {officialTotal} too large");
        }
        int total = (int)officialTotal;

        var official = officialS.GroupBy(x => x.Sort).Select(x => x.First()).OrderBy(x => x.Sort).ToList();
        if (official.Exists(x => x.Sort < 1 || x.Sort > total))
        {
            return ZZZGapFillResult.Fail(ZZZGapFillStatus.Inconsistent, "official sort out of range");
        }

        // 本地 S 级的序号（从 1 开始）
        var localS = new List<int>();
        for (int i = 0; i < localCount; i++)
        {
            if (local[i].RankType == SRankType)
            {
                localS.Add(i + 1);
            }
        }

        // 时区优先由「本地最后一个 S 级」与同物品的官方记录反推，对不上再用区服默认值
        TimeSpan? detected = DetectUtcOffset(local, localS, official);
        int[]? matched = null;
        TimeSpan offset = fallbackUtcOffset;
        if (detected is TimeSpan d)
        {
            matched = MatchLocalS(local, localS, official, d);
            offset = d;
        }
        if (matched is null && detected != fallbackUtcOffset)
        {
            matched = MatchLocalS(local, localS, official, fallbackUtcOffset);
            offset = fallbackUtcOffset;
        }
        if (matched is null)
        {
            return ZZZGapFillResult.Fail(ZZZGapFillStatus.Inconsistent, "local S records do not match official S records");
        }

        // 与本地记录一致用不带 Kind 的服务器本地时间
        DateTime OfficialTime(int officialIndex) => DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeSeconds(official[officialIndex].RecordTime).UtcDateTime.Add(offset), DateTimeKind.Unspecified);

        // 锚点：已匹配的本地 S 级（官方序号, 本地序号）
        var anchors = new List<(int Pos, int LocalIndex)>();
        for (int k = 0; k < localS.Count; k++)
        {
            anchors.Add(((int)official[matched[k]].Sort, localS[k]));
        }
        // 前提已同步：本地最后一条就是官方第 total 抽
        bool lastIsVirtualAnchor = false;
        if (localCount > 0)
        {
            if (anchors.Count > 0 && anchors[^1].LocalIndex == localCount)
            {
                if (anchors[^1].Pos != total)
                {
                    return ZZZGapFillResult.Fail(ZZZGapFillStatus.LocalBehind, "official has records after the last local S");
                }
            }
            else
            {
                anchors.Add((total, localCount));
                lastIsVirtualAnchor = true;
            }
        }
        int previousGap = 0;
        foreach (var (pos, localIndex) in anchors)
        {
            int gap = pos - localIndex;
            if (gap < previousGap)
            {
                return ZZZGapFillResult.Fail(ZZZGapFillStatus.Inconsistent, $"gap decreases at local index {localIndex}");
            }
            previousGap = gap;
        }

        var matchedOfficial = new HashSet<int>(matched);
        var entries = new Entry[total + 1];
        foreach (var (pos, localIndex) in anchors)
        {
            entries[pos] = new Entry(EntryKind.Real, localIndex);
        }

        // 逐个锚点区间填充；最前面的区间没有左锚点，本地一条都没有时也没有右锚点
        int leftPos = 0, leftLocal = 0;
        for (int a = 0; a <= anchors.Count; a++)
        {
            bool hasRight = a < anchors.Count;
            int rightPos = hasRight ? anchors[a].Pos : total + 1;
            int rightLocal = hasRight ? anchors[a].LocalIndex : localCount + 1;
            bool rightIsLast = hasRight && a == anchors.Count - 1 && lastIsVirtualAnchor;
            string? error = FillInterval(local, official, matchedOfficial, OfficialTime, entries,
                leftPos, leftLocal, a > 0, rightPos, rightLocal, hasRight, rightIsLast, out bool localBehind);
            if (error is not null)
            {
                return ZZZGapFillResult.Fail(localBehind ? ZZZGapFillStatus.LocalBehind : ZZZGapFillStatus.Inconsistent, error);
            }
            if (hasRight)
            {
                leftPos = rightPos;
                leftLocal = rightLocal;
            }
        }

        // 校验：序列完整、真实记录顺序不变、每个 S 级垫抽与官方 count 一致
        int expectLocal = 1;
        int lastSPos = 0;
        var officialBySort = new Dictionary<long, int>();
        for (int i = 0; i < official.Count; i++)
        {
            officialBySort[official[i].Sort] = i;
        }
        for (int pos = 1; pos <= total; pos++)
        {
            Entry e = entries[pos];
            bool isS;
            if (e.Kind == EntryKind.Real)
            {
                if (e.Index != expectLocal)
                {
                    return ZZZGapFillResult.Fail(ZZZGapFillStatus.Inconsistent, $"local order broken at position {pos}");
                }
                expectLocal++;
                isS = local[e.Index - 1].RankType == SRankType;
            }
            else
            {
                isS = e.Kind == EntryKind.NewS;
            }
            if (isS)
            {
                if (!officialBySort.TryGetValue(pos, out int oi) || official[oi].Count != pos - lastSPos)
                {
                    return ZZZGapFillResult.Fail(ZZZGapFillStatus.Inconsistent, $"pity check failed at position {pos}");
                }
                lastSPos = pos;
            }
        }
        if (expectLocal != localCount + 1)
        {
            return ZZZGapFillResult.Fail(ZZZGapFillStatus.Inconsistent, "not all local records placed");
        }

        // 最后一个 S 级之后的缺口分不清是「记录接口还没同步到的最新几抽」还是「丢失的老记录」；
        // 前者补了之后真实记录会追加进来，与占位重复计数，所以一律不补。本地一条都没有时由写入时的清理兜底。
        long lastOfficialSort = official.Count > 0 ? official[^1].Sort : 0;
        if (localCount > 0)
        {
            for (long pos = lastOfficialSort + 1; pos <= total; pos++)
            {
                if (entries[pos].Kind == EntryKind.Placeholder)
                {
                    return ZZZGapFillResult.Fail(ZZZGapFillStatus.LocalBehind, $"gap after the last S at position {pos}");
                }
            }
        }

        return AssignIds(local, official, entries, total, OfficialTime, idSuffix, emptyIdUpperBound, occupiedIds);
    }


    /// <summary>
    /// 用本地最后一个 S 级与官方同物品的最近一条记录反推服务器时区（取整到 15 分钟，误差须在 2 秒内）。
    /// </summary>
    /// <returns>反推出的 UTC 偏移；无法反推返回 null。</returns>
    private static TimeSpan? DetectUtcOffset(IReadOnlyList<ZZZGapLocalRecord> local, List<int> localS, List<ZZZGapOfficialS> official)
    {
        if (localS.Count == 0)
        {
            return null;
        }
        ZZZGapLocalRecord last = local[localS[^1] - 1];
        int oi = official.FindLastIndex(x => x.ItemId == last.ItemId);
        if (oi < 0)
        {
            return null;
        }
        DateTime utc = DateTimeOffset.FromUnixTimeSeconds(official[oi].RecordTime).UtcDateTime;
        double minutes = (new DateTime(last.Time.Ticks) - utc).TotalMinutes;
        double rounded = Math.Round(minutes / 15) * 15;
        if (Math.Abs(minutes - rounded) * 60 > 2 || Math.Abs(rounded) > 14 * 60)
        {
            return null;
        }
        return TimeSpan.FromMinutes(rounded);
    }


    /// <summary>
    /// 按（物品 Id, 抽取时间）把本地 S 级逐个对应到官方 S 级，且官方序号须随本地顺序递增。
    /// </summary>
    /// <returns>每个本地 S 级对应的官方下标；有任何一个对不上返回 null。</returns>
    private static int[]? MatchLocalS(IReadOnlyList<ZZZGapLocalRecord> local, List<int> localS, List<ZZZGapOfficialS> official, TimeSpan offset)
    {
        var queues = new Dictionary<(int ItemId, long Ticks), Queue<int>>();
        for (int i = 0; i < official.Count; i++)
        {
            long ticks = DateTimeOffset.FromUnixTimeSeconds(official[i].RecordTime).UtcDateTime.Add(offset).Ticks;
            var key = (official[i].ItemId, ticks);
            if (!queues.TryGetValue(key, out var queue))
            {
                queues[key] = queue = new Queue<int>();
            }
            queue.Enqueue(i);
        }
        var result = new int[localS.Count];
        long lastSort = 0;
        for (int k = 0; k < localS.Count; k++)
        {
            ZZZGapLocalRecord record = local[localS[k] - 1];
            if (!queues.TryGetValue((record.ItemId, record.Time.Ticks), out var queue) || queue.Count == 0)
            {
                return null;
            }
            int oi = queue.Dequeue();
            if (official[oi].Sort <= lastSort)
            {
                return null;
            }
            lastSort = official[oi].Sort;
            result[k] = oi;
        }
        return result;
    }


    /// <summary>
    /// 填充两个锚点之间的位置：插入缺口内的官方 S 级，把本地记录按时间分到各段，再在每段时间断档最大处放占位。
    /// </summary>
    /// <param name="localBehind">失败原因是官方有比本地最后一条更新的 S 级时为 true。</param>
    /// <returns>成功返回 null，失败返回原因。</returns>
    private static string? FillInterval(
        IReadOnlyList<ZZZGapLocalRecord> local,
        List<ZZZGapOfficialS> official,
        HashSet<int> matchedOfficial,
        Func<int, DateTime> officialTime,
        Entry[] entries,
        int leftPos, int leftLocal, bool hasLeft,
        int rightPos, int rightLocal, bool hasRight, bool rightIsLast,
        out bool localBehind)
    {
        localBehind = false;
        var locals = new List<int>();
        for (int i = leftLocal + 1; i < rightLocal; i++)
        {
            locals.Add(i);
        }
        var missingS = new List<int>();
        for (int i = 0; i < official.Count; i++)
        {
            if (!matchedOfficial.Contains(i) && official[i].Sort > leftPos && official[i].Sort < rightPos)
            {
                missingS.Add(i);
            }
        }
        int capacity = rightPos - leftPos - 1;
        if (capacity < locals.Count)
        {
            return $"interval ({leftPos},{rightPos}) has more local records than positions";
        }
        if (capacity == locals.Count)
        {
            if (missingS.Count > 0)
            {
                return $"interval ({leftPos},{rightPos}) has no gap but official S is missing locally";
            }
            for (int k = 0; k < locals.Count; k++)
            {
                entries[leftPos + 1 + k] = new Entry(EntryKind.Real, locals[k]);
            }
            return null;
        }

        DateTime? leftTime = hasLeft ? local[leftLocal - 1].Time : null;
        DateTime? rightTime = hasRight ? local[rightLocal - 1].Time : null;
        DateTime previousSTime = DateTime.MinValue;
        foreach (int oi in missingS)
        {
            DateTime t = officialTime(oi);
            if (t < previousSTime)
            {
                return "official S times are not in order";
            }
            previousSTime = t;
            if (leftTime is DateTime lt && t <= lt)
            {
                return $"missing S at {official[oi].Sort} is not after the left anchor";
            }
            if (rightTime is DateTime rt && t >= rightTime)
            {
                // 缺口里的 S 级比右锚点还新：右锚点是本地最后一条时说明本地没同步完
                localBehind = rightIsLast && t > rt;
                return $"missing S at {official[oi].Sort} is not before the right anchor";
            }
        }

        // 本地记录按时间分到各段；与缺口 S 级同一秒说明十连被拆开，无法判断先后
        var segments = new List<int>[missingS.Count + 1];
        for (int j = 0; j < segments.Length; j++)
        {
            segments[j] = new List<int>();
        }
        int lastSegment = 0;
        foreach (int li in locals)
        {
            DateTime t = local[li - 1].Time;
            int seg = 0;
            foreach (int oi in missingS)
            {
                DateTime st = officialTime(oi);
                if (st == t)
                {
                    return $"local record {li} has the same time as a missing S";
                }
                if (st < t)
                {
                    seg++;
                }
            }
            if (seg < lastSegment)
            {
                return "local record times are not in order";
            }
            lastSegment = seg;
            segments[seg].Add(li);
        }

        for (int j = 0; j < segments.Length; j++)
        {
            int lo = j == 0 ? leftPos : (int)official[missingS[j - 1]].Sort;
            int hi = j == missingS.Count ? rightPos : (int)official[missingS[j]].Sort;
            var segLocals = segments[j];
            int placeholders = hi - lo - 1 - segLocals.Count;
            if (placeholders < 0)
            {
                return $"segment ({lo},{hi}) has more local records than positions";
            }
            DateTime? loTime = j == 0 ? leftTime : officialTime(missingS[j - 1]);
            DateTime? hiTime = j == missingS.Count ? rightTime : officialTime(missingS[j]);
            int gapIndex = ChooseGap(local, segLocals, loTime, hiTime, isLeading: j == 0 && !hasLeft);

            int pos = lo + 1;
            for (int g = 0; g <= segLocals.Count; g++)
            {
                if (g == gapIndex)
                {
                    for (int p = 0; p < placeholders; p++)
                    {
                        entries[pos++] = new Entry(EntryKind.Placeholder, -1);
                    }
                }
                if (g < segLocals.Count)
                {
                    entries[pos++] = new Entry(EntryKind.Real, segLocals[g]);
                }
            }
            if (j < missingS.Count)
            {
                entries[hi] = new Entry(EntryKind.NewS, missingS[j]);
            }
        }
        return null;
    }


    /// <summary>
    /// 选占位插入的位置：最前面的缺口放最前；其余放在相邻两条记录时间间隔最大处（同样大取靠前的）。
    /// </summary>
    /// <returns>插在第几条本地记录之前（等于本地条数表示放在最后）。</returns>
    private static int ChooseGap(IReadOnlyList<ZZZGapLocalRecord> local, List<int> segLocals, DateTime? loTime, DateTime? hiTime, bool isLeading)
    {
        if (isLeading || segLocals.Count == 0)
        {
            return 0;
        }
        int best = 0;
        TimeSpan bestSpan = TimeSpan.MinValue;
        for (int g = 0; g <= segLocals.Count; g++)
        {
            DateTime? prev = g == 0 ? loTime : local[segLocals[g - 1] - 1].Time;
            DateTime? next = g == segLocals.Count ? hiTime : local[segLocals[g] - 1].Time;
            TimeSpan span = prev is DateTime p && next is DateTime n ? n - p : TimeSpan.MinValue;
            if (span > bestSpan)
            {
                best = g;
                bestSpan = span;
            }
        }
        return best;
    }


    /// <summary>
    /// 给新增记录分配 Id：夹在相邻两条真实记录 Id 之间、个位十位等于频段后缀；最前面的缺口取紧挨第一条真实记录之下的值。
    /// 占位时间取后一条已知记录的时间（没有则取前一条）。
    /// </summary>
    private static ZZZGapFillResult AssignIds(
        IReadOnlyList<ZZZGapLocalRecord> local,
        List<ZZZGapOfficialS> official,
        Entry[] entries,
        int total,
        Func<int, DateTime> officialTime,
        int idSuffix,
        long emptyIdUpperBound,
        IReadOnlySet<long> occupiedIds)
    {
        DateTime? KnownTime(Entry e) => e.Kind switch
        {
            EntryKind.Real => local[e.Index - 1].Time,
            EntryKind.NewS => officialTime(e.Index),
            _ => null,
        };

        var records = new List<ZZZGapFillRecord>();
        int pos = 1;
        while (pos <= total)
        {
            if (entries[pos].Kind == EntryKind.Real)
            {
                pos++;
                continue;
            }
            int start = pos;
            while (pos <= total && entries[pos].Kind != EntryKind.Real)
            {
                pos++;
            }
            int end = pos - 1;
            int n = end - start + 1;
            long? lower = start > 1 ? local[entries[start - 1].Index - 1].Id : null;
            long? upper = end < total ? local[entries[end + 1].Index - 1].Id : null;
            if (upper is null)
            {
                if (lower is not null)
                {
                    return ZZZGapFillResult.Fail(ZZZGapFillStatus.Inconsistent, "new records after the last local record");
                }
                upper = emptyIdUpperBound;
            }
            long last = LargestBelow(upper.Value, idSuffix);
            long first;
            long step;
            if (lower is null)
            {
                first = last - 100L * (n - 1);
                step = 100;
                if (first <= 0)
                {
                    return ZZZGapFillResult.Fail(ZZZGapFillStatus.NoIdSpace, "no positive id below the first record");
                }
            }
            else
            {
                first = SmallestAbove(lower.Value, idSuffix);
                long slots = last >= first ? (last - first) / 100 + 1 : 0;
                if (slots < n)
                {
                    return ZZZGapFillResult.Fail(ZZZGapFillStatus.NoIdSpace, $"only {slots} ids between {lower} and {upper}, need {n}");
                }
                step = slots / n * 100;
            }
            for (int k = 0; k < n; k++)
            {
                long id = first + step * k;
                if (occupiedIds.Contains(id))
                {
                    return ZZZGapFillResult.Fail(ZZZGapFillStatus.NoIdSpace, $"id {id} already exists");
                }
                Entry e = entries[start + k];
                if (e.Kind == EntryKind.NewS)
                {
                    records.Add(new ZZZGapFillRecord(id, true, start + k, official[e.Index].ItemId, officialTime(e.Index)));
                }
                else
                {
                    DateTime? time = null;
                    for (int q = start + k + 1; q <= total && time is null; q++)
                    {
                        time = KnownTime(entries[q]);
                    }
                    for (int q = start + k - 1; q >= 1 && time is null; q--)
                    {
                        time = KnownTime(entries[q]);
                    }
                    if (time is null)
                    {
                        return ZZZGapFillResult.Fail(ZZZGapFillStatus.Inconsistent, "no known time to reference");
                    }
                    records.Add(new ZZZGapFillRecord(id, false, start + k, 0, time.Value));
                }
            }
        }
        return new ZZZGapFillResult { Status = ZZZGapFillStatus.Filled, Records = records };
    }


    /// <summary>小于 <paramref name="bound"/> 且对 100 取余等于 <paramref name="suffix"/> 的最大值。</summary>
    private static long LargestBelow(long bound, int suffix)
    {
        long x = bound - 1;
        long mod = ((x - suffix) % 100 + 100) % 100;
        return x - mod;
    }


    /// <summary>大于 <paramref name="bound"/> 且对 100 取余等于 <paramref name="suffix"/> 的最小值。</summary>
    private static long SmallestAbove(long bound, int suffix)
    {
        long x = bound + 1;
        long mod = ((suffix - x) % 100 + 100) % 100;
        return x + mod;
    }

}
