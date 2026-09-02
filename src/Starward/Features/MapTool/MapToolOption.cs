namespace Starward.Features.MapTool;

/// <summary>
/// 地图工具一条可跳转站点。
/// x:Bind 要求 public。
/// </summary>
public class MapToolOption
{

    /// <summary>
    /// 初始化一条地图站点。
    /// </summary>
    /// <param name="title">界面显示名。</param>
    /// <param name="url">浏览器打开的地址。</param>
    public MapToolOption(string title, string url)
    {
        Title = title;
        Url = url;
    }


    /// <summary>列表上显示的名称。</summary>
    public string Title { get; }


    /// <summary>跳转 URL。</summary>
    public string Url { get; }

}
