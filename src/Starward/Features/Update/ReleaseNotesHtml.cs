using Starward.Setup.Core;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.Update;

/// <summary>
/// 发行说明在 WebView2 里的页面：更新窗口用一次渲染好的整页（<see cref="BuildPageAsync"/>），
/// 发行说明窗口只加载一次外壳（<see cref="BuildFlipShellAsync"/>），所有分组竖排成一列，切换时用脚本竖直平移。
/// 页面都带 <c>&lt;base target="_blank"&gt;</c>，链接都会触发 NewWindowRequested，由宿主用系统浏览器打开。
/// </summary>
internal static class ReleaseNotesHtml
{


    /// <summary>细滚动条，深浅色各一套。</summary>
    private const string ScrollbarStyle = """
        <style>
          @media (prefers-color-scheme: light) {
            ::-webkit-scrollbar {
              width: 6px
            }
            ::-webkit-scrollbar-thumb {
              background-color: #b8b8b8;
              border-radius: 1000px 0px 0px 1000px
            }
            ::-webkit-scrollbar-thumb:hover {
              background-color: #8b8b8b
            }
          }
          @media (prefers-color-scheme: dark) {
            ::-webkit-scrollbar {
              width: 6px
            }
            ::-webkit-scrollbar-thumb {
              background-color: #646464;
              border-radius: 1000px 0px 0px 1000px
            }
            ::-webkit-scrollbar-thumb:hover {
              background-color: #8b8b8b
            }
          }
        </style>
        """;



    /// <summary>
    /// 取 github-markdown 样式：优先用随包的本地文件，没有时回落 cdnjs。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可直接放进 head 的 style / link 标签。</returns>
    private static async Task<string> GetMarkdownStyleAsync(CancellationToken cancellationToken)
    {
        var cssFile = Path.Combine(AppContext.BaseDirectory, @"Assets\CSS\github-markdown.css");
        if (File.Exists(cssFile))
        {
            return $"<style>{await File.ReadAllTextAsync(cssFile, cancellationToken)}</style>";
        }
        return """<link href="https://cdnjs.cloudflare.com/ajax/libs/github-markdown-css/5.8.1/github-markdown.min.css" type="text/css" rel="stylesheet" />""";
    }



    /// <summary>
    /// 用 GitHub 的 /markdown 接口渲染 Markdown，再套上 github-markdown 样式与细滚动条，得到完整页面。
    /// </summary>
    /// <param name="releaseClient">发行说明客户端。</param>
    /// <param name="markdown">要渲染的 Markdown。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完整的 HTML 文档。</returns>
    public static async Task<string> BuildPageAsync(ReleaseClient releaseClient, string markdown, CancellationToken cancellationToken = default)
    {
        string html = await releaseClient.RenderGithubMarkdownAsync(markdown, cancellationToken);
        string css = await GetMarkdownStyleAsync(cancellationToken);
        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <base target="_blank">
              {{css}}
              {{ScrollbarStyle}}
            </head>
            <body style="margin: 12px 24px 12px 24px; overflow-x: hidden;">
              <article class="markdown-body" style="background: transparent;">
                {{html}}
              </article>
              <script>
                document.querySelectorAll('img[data-canonical-src]').forEach(img => {
                  const canonical = img.getAttribute('data-canonical-src');
                  if (canonical) {
                    img.src = canonical;
                  }
                  const parent = img.parentElement;
                  if (parent && parent.tagName.toLowerCase() === 'a') {
                    parent.href = canonical;
                  }
                });
              </script>
            </body>
            </html>
            """;
    }



    /// <summary>
    /// 发行说明窗口的页面外壳：每个分组一张铺满的「页」，从上到下排成一列，各自滚动，正文由 <see cref="BuildFillScript"/> 换入。
    /// <para>
    /// 换组时整列竖直平移到目标页，效果同上游游戏设置弹窗里竖排的 FlipView：跨几组时一路经过中间的页。
    /// 平移途中再换组，从当前位置直接转向新目标。滚轮只滚动当前页，不翻页。
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完整的 HTML 文档。</returns>
    public static async Task<string> BuildFlipShellAsync(CancellationToken cancellationToken = default)
    {
        string css = await GetMarkdownStyleAsync(cancellationToken);
        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <base target="_blank">
              {{css}}
              {{ScrollbarStyle}}
              <style>
                html, body {
                  margin: 0;
                  height: 100%;
                  overflow: hidden;
                  background: transparent;
                }
                #stage {
                  width: 100%;
                  height: 100%;
                  overflow: hidden;
                }
                #strip {
                  height: 100%;
                  will-change: transform;
                }
                .page {
                  position: relative;
                  height: 100%;
                  box-sizing: border-box;
                  padding: 12px 24px;
                  overflow-x: hidden;
                  overflow-y: auto;
                }
                /* 还没渲染好的页：居中一个转圈 */
                .page:not(.filled)::after {
                  content: '';
                  position: absolute;
                  left: 50%;
                  top: 50%;
                  width: 28px;
                  height: 28px;
                  margin: -14px 0 0 -14px;
                  border: 3px solid rgba(128, 128, 128, 0.25);
                  border-top-color: rgba(128, 128, 128, 0.8);
                  border-radius: 50%;
                  animation: spin 0.8s linear infinite;
                }
                @keyframes spin {
                  to { transform: rotate(360deg); }
                }
              </style>
            </head>
            <body>
              <div id="stage"><div id="strip"></div></div>
              <script>
                // 与 FlipView 翻页相近的减速曲线；跨的组越多时长略长，最长 620ms
                const FLIP_EASING = 'cubic-bezier(0.1, 0.9, 0.2, 1)';
                let currentIndex = 0;

                function fixImages(root) {
                  root.querySelectorAll('img[data-canonical-src]').forEach(img => {
                    const canonical = img.getAttribute('data-canonical-src');
                    if (canonical) {
                      img.src = canonical;
                    }
                    const parent = img.parentElement;
                    if (parent && parent.tagName.toLowerCase() === 'a') {
                      parent.href = canonical;
                    }
                  });
                }

                // 按分组数建好空页（显示转圈），回到第一页
                function initPages(count) {
                  const strip = document.getElementById('strip');
                  strip.style.transition = 'none';
                  strip.style.transform = 'translateY(0)';
                  strip.innerHTML = '';
                  for (let i = 0; i < count; i++) {
                    const page = document.createElement('div');
                    page.className = 'page';
                    strip.appendChild(page);
                  }
                  currentIndex = 0;
                }

                // 填入一页正文；已填过的不再替换，免得把读到一半的滚动位置冲掉
                function fillPage(index, html) {
                  const page = document.getElementById('strip').children[index];
                  if (!page || page.classList.contains('filled')) {
                    return;
                  }
                  const article = document.createElement('article');
                  article.className = 'markdown-body';
                  article.style.background = 'transparent';
                  article.innerHTML = html;
                  page.appendChild(article);
                  fixImages(page);
                  page.classList.add('filled');
                }

                // 竖直平移到第 index 页；animate 为 false 时直接跳过去
                function goToPage(index, animate) {
                  const strip = document.getElementById('strip');
                  const steps = Math.abs(index - currentIndex);
                  currentIndex = index;
                  if (animate && steps > 0) {
                    const duration = Math.min(380 + 60 * (steps - 1), 620);
                    strip.style.transition = `transform ${duration}ms ${FLIP_EASING}`;
                  } else {
                    strip.style.transition = 'none';
                  }
                  strip.style.transform = `translateY(${-index * 100}%)`;
                }
              </script>
            </body>
            </html>
            """;
    }



    /// <summary>
    /// 生成按分组数建好空页的脚本（外壳页加载后、或分组变化时调用一次）。
    /// </summary>
    /// <param name="count">分组数。</param>
    /// <returns>交给 ExecuteScriptAsync 的脚本。</returns>
    public static string BuildInitScript(int count)
    {
        return $"initPages({count});";
    }



    /// <summary>
    /// 生成把一组正文填进对应页的脚本。
    /// </summary>
    /// <param name="index">分组序号。</param>
    /// <param name="articleHtml">GitHub 渲染出的正文 HTML。</param>
    /// <returns>交给 ExecuteScriptAsync 的脚本。</returns>
    public static string BuildFillScript(int index, string articleHtml)
    {
        // 编码成 JS 字符串字面量；默认编码器会转义 < > & ' 与非 ASCII，放进脚本不会提前闭合
        string literal = JsonSerializer.Serialize(articleHtml, ReleaseNotesHtmlJsonContext.Default.String);
        return $"fillPage({index}, {literal});";
    }



    /// <summary>
    /// 生成平移到某一页的脚本。
    /// </summary>
    /// <param name="index">分组序号。</param>
    /// <param name="animate">是否播放平移动画（系统关闭了动画效果、或首次显示时为 false）。</param>
    /// <returns>交给 ExecuteScriptAsync 的脚本。</returns>
    public static string BuildGoToScript(int index, bool animate)
    {
        return $"goToPage({index}, {(animate ? "true" : "false")});";
    }

}



/// <summary>
/// 把正文 HTML 编码成脚本里的字符串字面量。
/// </summary>
[JsonSerializable(typeof(string))]
internal partial class ReleaseNotesHtmlJsonContext : JsonSerializerContext;
