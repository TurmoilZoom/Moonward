namespace Starward.Features.Gacha.UIGF;

/// <summary>
/// UIGF v4.x 协议子版本。
/// <list type="bullet">
/// <item><see cref="V40"/>：多游戏合并格式（原神 / 星铁 / 绝区零）。</item>
/// <item><see cref="V41"/>：在 v4.0 基础上标注支持星铁联动池（gacha_type 21/22）。</item>
/// <item><see cref="V42"/>：在 v4.1 基础上支持千星奇域（hk4e_ugc）。</item>
/// </list>
/// </summary>
public enum UIGF4Version
{
    /// <summary>UIGF v4.0</summary>
    V40 = 40,

    /// <summary>UIGF v4.1（星铁联动池）</summary>
    V41 = 41,

    /// <summary>UIGF v4.2（千星奇域 hk4e_ugc）</summary>
    V42 = 42,
}


/// <summary><see cref="UIGF4Version"/> 与协议版本字符串互转。</summary>
public static class UIGF4VersionExtensions
{
    /// <summary>协议版本字符串，如 <c>v4.0</c>。</summary>
    public static string ToVersionString(this UIGF4Version version) => version switch
    {
        UIGF4Version.V41 => "v4.1",
        UIGF4Version.V42 => "v4.2",
        _ => "v4.0",
    };


    /// <summary>从 <c>v4.0</c> / <c>4.0</c> 等解析；无法识别时返回 null。</summary>
    public static UIGF4Version? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        string s = text.Trim().TrimStart('v', 'V');
        return s switch
        {
            "4.0" => UIGF4Version.V40,
            "4.1" => UIGF4Version.V41,
            "4.2" => UIGF4Version.V42,
            _ => null,
        };
    }


    /// <summary>该导出版本是否包含千星奇域（hk4e_ugc）。</summary>
    public static bool SupportsHk4eUgc(this UIGF4Version version) => version >= UIGF4Version.V42;
}
