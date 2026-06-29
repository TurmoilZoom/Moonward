namespace Starward.Core.GameRecord.ZZZ.InterKnotReport;

/// <summary>
/// 绳网月报资源类型常量，对应 <c>month_detail</c> 请求的 <c>type</c> 参数及汇总/明细中的 <c>data_type</c> 字段。
/// API 拼写 <c>MatserTapeData</c> 为官方原样，勿自行修正。
/// </summary>
public abstract class InterKnotReportDataType
{

    /// <summary>菲林（Polychrome）。</summary>
    public const string PolychromesData = nameof(PolychromesData);

    /// <summary>加密母带与原装母带（Master Tape，API 字段名为 MatserTapeData）。</summary>
    public const string MatserTapeData = nameof(MatserTapeData);

    /// <summary>邦布券（Boopon）。</summary>
    public const string BooponsData = nameof(BooponsData);



    /// <summary>
    /// 将 <c>data_type</c> 映射为本地化显示名；当前未知类型原样返回。
    /// </summary>
    /// <param name="type"><see cref="PolychromesData"/>、<see cref="MatserTapeData"/> 或 <see cref="BooponsData"/>。</param>
    /// <returns>本地化文案；无映射时返回 <paramref name="type"/> 本身。</returns>
    public static string ToLocalization(string type)
    {
        return type switch
        {
            _ => type,
        };
    }



}