using System;

namespace Starward.Features.GameRecord;

/// <summary>
/// Geetest WebView 页面 HTML 构建（Gt3 / Gt4），供验证码登录内嵌 WebView 使用。
/// </summary>
internal static class GeetestHelper
{

    /// <summary>
    /// 构建内嵌 Geetest 的 HTML 页面。
    /// </summary>
    /// <param name="geetestConfigJson">aigis.data 中的极验配置 JSON 文本。</param>
    /// <param name="sessionId">aigis.session_id。</param>
    /// <returns>可 <c>NavigateToString</c> 的完整 HTML。</returns>
    public static string BuildHtml(string geetestConfigJson, string sessionId)
    {
        // 用 base64 传递配置，避免 HTML/JS 转义问题
        string configB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(geetestConfigJson));
        string sessionB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sessionId ?? ""));

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <style>
                html, body { margin: 0; padding: 0; background: transparent; color: #fff; font-family: sans-serif; }
                #wrap { display: flex; flex-direction: column; align-items: center; padding: 8px; }
                #geetest { min-height: 200px; }
                #status { margin-top: 12px; font-size: 13px; opacity: 0.8; text-align: center; }
              </style>
              <script src="https://static.geetest.com/static/js/gt.0.4.9.js"></script>
              <script src="https://static.geetest.com/v4/gt4.js"></script>
            </head>
            <body>
              <div id="wrap">
                <div id="verify"><div id="geetest"></div></div>
                <div id="status">Loading...</div>
              </div>
              <script>
                function post(msg) {
                  try {
                    if (window.chrome && window.chrome.webview) {
                      window.chrome.webview.postMessage(typeof msg === 'string' ? msg : JSON.stringify(msg));
                    }
                  } catch (e) {}
                }
                function b64ToUtf8(b64) {
                  const bin = atob(b64);
                  const bytes = new Uint8Array(bin.length);
                  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
                  return new TextDecoder('utf-8').decode(bytes);
                }
                const props = JSON.parse(b64ToUtf8('{{configB64}}'));
                const sessionId = b64ToUtf8('{{sessionB64}}');
                const status = document.getElementById('status');

                function done(validate) {
                  if (!validate) { post('cancel'); return; }
                  post(JSON.stringify(validate));
                }

                try {
                  if (props.challenge) {
                    // Geetest v3
                    initGeetest({
                      gt: props.gt,
                      challenge: props.challenge,
                      offline: false,
                      new_captcha: true,
                      product: 'popup',
                      width: '280px',
                      https: true
                    }, function (captchaObj) {
                      captchaObj.appendTo('#geetest');
                      captchaObj.onReady(function () { status.textContent = ''; captchaObj.verify(); });
                      captchaObj.onSuccess(function () {
                        const v = captchaObj.getValidate();
                        done(v);
                      });
                      captchaObj.onClose(function () {
                        const v = captchaObj.getValidate && captchaObj.getValidate();
                        if (!v) post('cancel');
                      });
                      captchaObj.onError(function () { status.textContent = 'Error'; post('cancel'); });
                    });
                  } else {
                    // Geetest v4
                    initGeetest4({
                      captchaId: props.gt,
                      riskType: props.risk_type || props.riskType,
                      product: 'popup',
                      nextWidth: '280px',
                      lang: 'zho',
                      userInfo: JSON.stringify({ session_id: sessionId }),
                      https: true,
                      protocol: 'https://'
                    }, function (captchaObj) {
                      captchaObj.appendTo('#geetest');
                      captchaObj.onReady(function () { status.textContent = ''; captchaObj.showCaptcha(); });
                      captchaObj.onSuccess(function () {
                        const v = captchaObj.getValidate();
                        done(v);
                      });
                      captchaObj.onClose(function () { post('cancel'); });
                      captchaObj.onError(function () { status.textContent = 'Error'; post('cancel'); });
                    });
                  }
                } catch (e) {
                  status.textContent = String(e);
                  post('cancel');
                }
              </script>
            </body>
            </html>
            """;
    }

}
