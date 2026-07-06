namespace Starward.Features.Update;

/// <summary>
/// 更新包下载源（仅影响下载阶段，检查更新默认仍走 CNB）。
/// </summary>
internal enum UpdateDownloadSource
{
    /// <summary>从 GitHub Releases 下载 Velopack 资产。</summary>
    GitHub,

    /// <summary>从 CNB Releases 下载 Velopack 资产（默认）。</summary>
    Cnb,
}