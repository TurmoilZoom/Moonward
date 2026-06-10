namespace Starward.Core;

/// <summary>
/// 语言代码工具类。
/// 负责将用户输入或系统语言代码规范化为项目支持的格式，
/// 同时提供音频语言过滤和支持语言列表查询功能。
/// </summary>
public static class LanguageUtil
{

    /// <summary>
    ///  "zh-CN"转 "zh-cn"
    /// 将任意语言代码规范化为项目支持的 UI/文本语言代码。
    /// </summary>
    /// <param name="lang">原始语言代码（支持 "zh-CN"、"en"、"ja-JP"、"zh-tw" 等任意大小写输入）。</param>
    /// <returns>
    /// 规范化后的语言代码。支持以下值：
    /// zh-cn, zh-tw, en-us, de-de, es-es, fr-fr, id-id, it-it, ja-jp, ko-kr, pt-pt, ru-ru, th-th, tr-tr, vi-vn。
    /// 规则：
    /// <list type="bullet">
    /// <item>中文变体：zh-hk/zh-mo/zh-tw → zh-tw；zh-cn/zh-sg → zh-cn。</item>
    /// <item>其他语言按前两个字母匹配（de→de-de, ja→ja-jp 等）。</item>
    /// <item>无法识别或输入过短时默认返回 "en-us"。</item>
    /// </list>
    /// </returns>
    public static string FilterLanguage(string? lang)
    {
        // 支持的语言：zh-cn,zh-tw,en-us,de-de,es-es,fr-fr,id-id,it-it,ja-jp,ko-kr,pt-pt,ru-ru,th-th,tr-tr,vi-vn
        var low = lang?.ToLower() ?? "";
        if (low.Length < 2)
        {
            low = "..";
        }
        return low switch
        {
            "zh-hk" or "zh-mo" or "zh-tw" => "zh-tw",
            "zh-cn" or "zh-sg" => "zh-cn",
            _ => low[..2] switch
            {
                "de" => "de-de",
                "es" => "es-es",
                "fr" => "fr-fr",
                "id" => "id-id",
                "it" => "it-it",
                "ja" => "ja-jp",
                "ko" => "ko-kr",
                "pt" => "pt-pt",
                "ru" => "ru-ru",
                "th" => "th-th",
                "tr" => "tr-tr",
                "vi" => "vi-vn",
                _ => "en-us",
            }
        };
    }


    /// <summary>
    /// 将语言代码过滤为支持的语音（音频）语言。
    /// 仅返回游戏实际存在的语音包语言。
    /// </summary>
    /// <param name="lang">原始语言代码。</param>
    /// <returns>
    /// 仅以下四种之一：zh-cn, ja-jp, ko-kr, en-us。
    /// 所有中文变体统一为 "zh-cn"；日语/韩语分别映射；其他一律返回 "en-us"。
    /// </returns>
    public static string FilterAudioLanguage(string? lang)
    {
        var low = lang?.ToLower() ?? "";
        if (low.Length < 2)
        {
            low = "..";
        }
        return low switch
        {
            _ => low[..2] switch
            {
                "zh" => "zh-cn",
                "ja" => "ja-jp",
                "ko" => "ko-kr",
                _ => "en-us",
            }
        };
    }


    /// <summary>
    /// 获取项目支持的所有 UI 语言代码列表。
    /// </summary>
    /// <returns>
    /// 包含 15 种语言代码的列表（顺序固定）：
    /// zh-cn, zh-tw, en-us, de-de, es-es, fr-fr, id-id, it-it, ja-jp, ko-kr, pt-pt, ru-ru, th-th, tr-tr, vi-vn。
    /// </returns>
    public static List<string> GetAllLanguages()
    {
        return new List<string>
        {
            "zh-cn",
            "zh-tw",
            "en-us",
            "de-de",
            "es-es",
            "fr-fr",
            "id-id",
            "it-it",
            "ja-jp",
            "ko-kr",
            "pt-pt",
            "ru-ru",
            "th-th",
            "tr-tr",
            "vi-vn",
        };
    }

}
