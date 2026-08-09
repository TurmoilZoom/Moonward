using System.Text.Json.Serialization;

namespace Starward.Core.MiyoLive;

/// <summary>
/// <c>miyolive/index</c> 的 data 节点。
/// </summary>
public class MiyoLiveIndexData
{

    [JsonPropertyName("live")]
    public MiyoLiveInfo? Live { get; set; }

}


/// <summary>
/// 直播活动元数据（含兑换码 version）。
/// </summary>
public class MiyoLiveInfo
{

    /// <summary>
    /// 传给 <c>refreshCode</c> 的 version（code_ver）。
    /// </summary>
    [JsonPropertyName("code_ver")]
    public string? CodeVer { get; set; }


    [JsonPropertyName("title")]
    public string? Title { get; set; }


    /// <summary>
    /// 计划开播时间墙钟字符串（东八区常见 <c>yyyy-MM-dd HH:mm:ss</c>）。
    /// </summary>
    [JsonPropertyName("start")]
    public string? Start { get; set; }


    /// <summary>
    /// 是否已结束。
    /// </summary>
    [JsonPropertyName("is_end")]
    public bool IsEnd { get; set; }


    /// <summary>
    /// 剩余相关字段；&gt; 0 时常表示尚未可领码（与云崽一致）。
    /// </summary>
    [JsonPropertyName("remain")]
    public int Remain { get; set; }

}


/// <summary>
/// <c>miyolive/refreshCode</c> 的 data 节点。
/// </summary>
public class MiyoLiveCodeData
{

    [JsonPropertyName("code_list")]
    public List<MiyoLiveCodeItem> CodeList { get; set; } = [];

}


/// <summary>
/// 单条直播兑换码。
/// </summary>
public class MiyoLiveCodeItem
{

    /// <summary>
    /// 奖励说明，常为 HTML。
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }


    [JsonPropertyName("code")]
    public string? Code { get; set; }


    [JsonPropertyName("img")]
    public string? Img { get; set; }


    /// <summary>
    /// 兑换码出现时间戳（秒）；未放出时常为空字符串。
    /// </summary>
    [JsonPropertyName("to_get_time")]
    public string? ToGetTime { get; set; }

}


/// <summary>
/// painter <c>user_instant/list</c> 的 data。
/// </summary>
public class MiyoLiveUserInstantListData
{

    [JsonPropertyName("list")]
    public List<MiyoLiveUserInstantItem> List { get; set; } = [];

}


/// <summary>
/// 动态列表单项。
/// </summary>
public class MiyoLiveUserInstantItem
{

    [JsonPropertyName("post")]
    public MiyoLivePostWrapper? Post { get; set; }

}


/// <summary>
/// 动态帖包装。
/// </summary>
public class MiyoLivePostWrapper
{

    [JsonPropertyName("post")]
    public MiyoLivePost? Post { get; set; }

}


/// <summary>
/// 动态帖正文（用于解析 act_id）。
/// </summary>
public class MiyoLivePost
{

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }


    /// <summary>
    /// 结构化内容 JSON 字符串，内含活动链接。
    /// </summary>
    [JsonPropertyName("structured_content")]
    public string? StructuredContent { get; set; }


    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

}


/// <summary>
/// 米游社首页 <c>home/new</c> 的 data（仅需 navigator）。
/// </summary>
public class MiyoLiveHomeData
{

    [JsonPropertyName("navigator")]
    public List<MiyoLiveNavigatorItem> Navigator { get; set; } = [];

}


/// <summary>
/// 首页导航项。
/// </summary>
public class MiyoLiveNavigatorItem
{

    [JsonPropertyName("name")]
    public string? Name { get; set; }


    [JsonPropertyName("app_path")]
    public string? AppPath { get; set; }

}
