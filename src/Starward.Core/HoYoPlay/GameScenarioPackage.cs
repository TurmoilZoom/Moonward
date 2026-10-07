using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Core.HoYoPlay;


/// <summary>
/// 安装的资源场景，数值与 getGameBranches 的 scenarios_filter 一致
/// </summary>
public enum GameScenarioPackageType
{

    /// <summary>
    /// 未指定或本地没有记录，按完整资源处理
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 完整资源
    /// </summary>
    Full = 1,

    /// <summary>
    /// 基础资源（目前只有绝区零）
    /// </summary>
    Base = 2,

}



/// <summary>
/// 完整资源 / 基础资源的判定与本地记录，与官方启动器共用游戏目录里的 <see cref="GameConfig.LocalScenarioConfigPath"/>（绝区零为 KBasePackage）
/// </summary>
public static class GameScenarioPackage
{

    public const string ScenarioFull = "CATEGORY_SCENARIO_FULL";

    public const string ScenarioBase = "CATEGORY_SCENARIO_BASE";

    public const string CategoryTypeResource = "CATEGORY_TYPE_RESOURCE";



    /// <summary>
    /// 该游戏的这个分支是否提供基础资源选项
    /// </summary>
    /// <param name="config">游戏配置</param>
    /// <param name="package">要安装的分支</param>
    /// <returns>开启了基础资源且分支里有属于基础资源的分类时为 <see langword="true"/></returns>
    public static bool IsSupported(GameConfig? config, GameBranchPackage? package)
    {
        return config?.EnableScenarioPackage is true
            && !string.IsNullOrWhiteSpace(config.LocalScenarioConfigPath)
            && package?.Categories?.Any(x => x.Scenarios?.Contains(ScenarioBase) is true) is true;
    }



    /// <summary>
    /// 只属于完整资源、基础资源不包含的资源分类。语音分类不在其中，语音按所选语言另算
    /// </summary>
    /// <param name="package">要安装的分支</param>
    /// <returns>分类的 matching_field，对应清单的 MatchingField</returns>
    public static HashSet<string> GetFullOnlyMatchingFields(GameBranchPackage? package)
    {
        HashSet<string> fields = new();
        if (package?.Categories is null)
        {
            return fields;
        }
        foreach (GameBranchPackageCategory category in package.Categories)
        {
            // 没有 scenarios 的分类（其他游戏或接口字段缺失）不当作完整资源独有，宁可多下也不漏下
            if (category.Type is CategoryTypeResource
                && category.Scenarios is { Count: > 0 } scenarios
                && !scenarios.Contains(ScenarioBase)
                && !string.IsNullOrWhiteSpace(category.MatchingField))
            {
                fields.Add(category.MatchingField);
            }
        }
        return fields;
    }



    /// <summary>
    /// 读取游戏目录里记录的资源场景
    /// </summary>
    /// <param name="installPath">游戏安装目录</param>
    /// <param name="config">游戏配置</param>
    /// <returns>没有记录或内容无法识别时为 <see cref="GameScenarioPackageType.Unknown"/></returns>
    public static GameScenarioPackageType GetLocalPackageType(string? installPath, GameConfig? config)
    {
        if (string.IsNullOrWhiteSpace(installPath) || string.IsNullOrWhiteSpace(config?.LocalScenarioConfigPath))
        {
            return GameScenarioPackageType.Unknown;
        }
        try
        {
            string file = Path.Join(installPath, config.LocalScenarioConfigPath);
            if (!File.Exists(file))
            {
                return GameScenarioPackageType.Unknown;
            }
            // 按文本读：带 BOM 时 ReadAllText 会去掉，按字节解析会因 BOM 失败
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.ValueKind is JsonValueKind.Object
                && doc.RootElement.TryGetProperty("packageType", out JsonElement value)
                && value.ValueKind is JsonValueKind.String)
            {
                return value.GetString() switch
                {
                    "FULL" => GameScenarioPackageType.Full,
                    "BASE" => GameScenarioPackageType.Base,
                    _ => GameScenarioPackageType.Unknown,
                };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return GameScenarioPackageType.Unknown;
    }



    /// <summary>
    /// 把资源场景写入游戏目录，格式与官方启动器相同：{"packageType":"BASE"}
    /// </summary>
    /// <param name="installPath">游戏安装目录</param>
    /// <param name="config">游戏配置，没有 <see cref="GameConfig.LocalScenarioConfigPath"/> 时不写</param>
    /// <param name="type">资源场景，<see cref="GameScenarioPackageType.Unknown"/> 时不写</param>
    /// <param name="cancellationToken"></param>
    public static async Task SetLocalPackageTypeAsync(string installPath, GameConfig? config, GameScenarioPackageType type, CancellationToken cancellationToken = default)
    {
        if (type is GameScenarioPackageType.Unknown || string.IsNullOrWhiteSpace(config?.LocalScenarioConfigPath))
        {
            return;
        }
        string file = Path.Join(installPath, config.LocalScenarioConfigPath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string value = type is GameScenarioPackageType.Base ? "BASE" : "FULL";
        await File.WriteAllTextAsync(file, $$"""{"packageType":"{{value}}"}""", cancellationToken);
    }


}
