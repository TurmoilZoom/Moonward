using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starward.Core.GameRecord.Passport;
using Starward.Features;
using Starward.Language;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Starward.Features.GameRecord;

/// <summary>
/// 国服短信验证码登录对话框（亚克力背景，表单内自带登录/取消）。
/// 对话框打开期间的错误/提示用框内 <see cref="InfoBar"/>（可关闭）；
/// 映射文案仍经 <see cref="MiHoYoApiErrorFeedbackFactory"/>，关闭后再由页面 Toast。
/// </summary>
public sealed partial class CaptchaLoginDialog : ContentDialog
{

    private readonly CaptchaLoginService _captchaLoginService = AppConfig.GetService<CaptchaLoginService>();
    private DispatcherTimer? _countdownTimer;
    private DispatcherTimer? _feedbackAutoCloseTimer;
    private int _countdownSeconds;
    private string? _actionType;
    private bool _busy;

    /// <summary>登录成功后的 Cookie；取消或失败时为 null。</summary>
    public string? CookieResult { get; private set; }


    /// <summary>
    /// 创建验证码登录对话框。
    /// </summary>
    public CaptchaLoginDialog()
    {
        this.InitializeComponent();
    }


    /// <summary>
    /// 以当前页面 XamlRoot 显示对话框，返回用户操作结果。
    /// </summary>
    /// <param name="xamlRoot">宿主 XamlRoot。</param>
    /// <returns>成功登录为 Primary，取消为 None。</returns>
    public async Task<ContentDialogResult> ShowAsync(XamlRoot xamlRoot)
    {
        XamlRoot = xamlRoot;
        // 未设置 Primary/Secondary 文本时结果为 None；成功登录时主动 Hide 后仍为 None，靠 CookieResult 判断
        await base.ShowAsync();
        return string.IsNullOrWhiteSpace(CookieResult) ? ContentDialogResult.None : ContentDialogResult.Primary;
    }


    private async void Button_SendCode_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _countdownSeconds > 0)
        {
            return;
        }

        string phone = TextBox_Phone.Text?.Trim() ?? "";
        if (!CaptchaLoginService.IsValidPhone(phone))
        {
            ShowFeedback(new ArgumentException("Invalid phone.", "phone"));
            return;
        }

        try
        {
            SetBusy(true);
            ClearFeedback();
            var captchaResult = await _captchaLoginService.CreateCaptchaAsync(phone, ResolveAigisAsync);
            _actionType = captchaResult.ActionType;
            int countdown = captchaResult.Countdown > 0 ? captchaResult.Countdown : 60;
            StartCountdown(countdown);
            ShowInline(InfoBarSeverity.Success, null, Lang.CaptchaLogin_SendSuccess, autoCloseMs: 3000);
        }
        catch (Exception ex)
        {
            ShowFeedback(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }


    private async void Button_Login_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        string phone = TextBox_Phone.Text?.Trim() ?? "";
        string code = TextBox_Code.Text?.Trim() ?? "";

        if (!CaptchaLoginService.IsValidPhone(phone))
        {
            ShowFeedback(new ArgumentException("Invalid phone.", "phone"));
            return;
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowFeedback(new ArgumentException("Captcha required.", "captcha"));
            return;
        }
        if (string.IsNullOrWhiteSpace(_actionType))
        {
            ShowFeedback(new ArgumentException("Send captcha first.", "actionType"));
            return;
        }

        try
        {
            SetBusy(true);
            ClearFeedback();
            CookieResult = await _captchaLoginService.LoginByCaptchaAsync(
                phone,
                code,
                _actionType,
                ResolveAigisAsync);
            Hide();
        }
        catch (Exception ex)
        {
            ShowFeedback(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }


    private void Button_Cancel_Click(object sender, RoutedEventArgs e)
    {
        CookieResult = null;
        Hide();
    }


    /// <summary>
    /// aigis 回调：弹出独立人机验证层（不占用本登录对话框内容区）。
    /// </summary>
    private async Task<string?> ResolveAigisAsync(CaptchaAigis aigis, CancellationToken cancellationToken)
    {
        return await GeetestVerifyPopup.ShowAsync(XamlRoot, aigis, cancellationToken);
    }


    /// <summary>
    /// 经 Factory 映射异常后，在对话框内 InfoBar 展示（可点关闭）。
    /// </summary>
    /// <param name="exception">业务或校验异常。</param>
    private void ShowFeedback(Exception exception)
    {
        var feedback = MiHoYoApiErrorFeedbackFactory.Create(exception, MiHoYoApiContext.PassportCaptcha);
        ShowInline(feedback.Severity, feedback.Title, feedback.Message, autoCloseMs: 0);
    }


    /// <summary>
    /// 在对话框内打开 InfoBar；成功类可自动收起，错误类默认常驻直到用户关闭。
    /// </summary>
    /// <param name="severity">严重级别。</param>
    /// <param name="title">标题；可为空。</param>
    /// <param name="message">正文。</param>
    /// <param name="autoCloseMs">自动关闭毫秒数；0 表示不自动关。</param>
    private void ShowInline(InfoBarSeverity severity, string? title, string? message, int autoCloseMs)
    {
        _feedbackAutoCloseTimer?.Stop();
        InfoBar_Feedback.Severity = severity;
        InfoBar_Feedback.Title = title ?? string.Empty;
        InfoBar_Feedback.Message = message ?? string.Empty;
        InfoBar_Feedback.IsOpen = true;

        if (autoCloseMs > 0)
        {
            _feedbackAutoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(autoCloseMs) };
            _feedbackAutoCloseTimer.Tick += (_, _) =>
            {
                _feedbackAutoCloseTimer.Stop();
                InfoBar_Feedback.IsOpen = false;
            };
            _feedbackAutoCloseTimer.Start();
        }
    }


    /// <summary>
    /// 收起框内提示。
    /// </summary>
    private void ClearFeedback()
    {
        _feedbackAutoCloseTimer?.Stop();
        InfoBar_Feedback.IsOpen = false;
        InfoBar_Feedback.Title = string.Empty;
        InfoBar_Feedback.Message = string.Empty;
    }


    private void StartCountdown(int seconds)
    {
        _countdownSeconds = seconds;
        UpdateSendButtonText();
        _countdownTimer?.Stop();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            _countdownSeconds--;
            if (_countdownSeconds <= 0)
            {
                _countdownTimer.Stop();
                _countdownSeconds = 0;
                Button_SendCode.IsEnabled = !_busy;
                Button_SendCode.Content = Lang.CaptchaLogin_SendCode;
                return;
            }
            UpdateSendButtonText();
        };
        _countdownTimer.Start();
    }


    private void UpdateSendButtonText()
    {
        Button_SendCode.IsEnabled = false;
        Button_SendCode.Content = string.Format(Lang.CaptchaLogin_ResendInSeconds, _countdownSeconds);
    }


    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressBar_Loading.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        TextBox_Phone.IsEnabled = !busy;
        TextBox_Code.IsEnabled = !busy;
        Button_Login.IsEnabled = !busy;
        Button_Cancel.IsEnabled = !busy;
        if (_countdownSeconds <= 0)
        {
            Button_SendCode.IsEnabled = !busy;
        }
    }

}
