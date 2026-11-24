using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord;


internal class DeviceExtListResult
{

    [JsonPropertyName("code")]
    public int Code { get; set; }


    [JsonPropertyName("msg")]
    public string Message { get; set; }


    [JsonPropertyName("ext_list")]
    public List<string> ExtList { get; set; }


    [JsonPropertyName("pkg_list")]
    public List<string> PkgList { get; set; }


    [JsonPropertyName("pkg_str")]
    public string PkgStr { get; set; }

}
