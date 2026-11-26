using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Starward.Core.GameRecord.Passport;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;

namespace Starward.Features.GameRecord;

/// <summary>
/// 国际服账号密码登录对话框（亚克力背景，表单内自带登录/取消）。
/// 极验与身份验证用独立弹层；对话框打开期间的错误用框内 <see cref="InfoBar"/>，文案经 <see cref="MiHoYoApiErrorFeedbackFactory"/>。
/// </summary>
public sealed partial class GlobalPasswordLoginDialog : ContentDialog
{

    private readonly GlobalPasswordLoginService _loginService = AppConfig.GetService<GlobalPasswordLoginService>();
    private bool _busy;


    /// <summary>登录成功后的 Cookie；取消或失败时为 null。</summary>
    public string? CookieResult { get; private set; }


    /// <summary>
    /// 创建密码登录对话框。
    /// </summary>
    public GlobalPasswordLoginDialog()
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
        // 没有系统按钮，成功时主动 Hide 后结果仍为 None，靠 CookieResult 判断
        await base.ShowAsync();
        return string.IsNullOrWhiteSpace(CookieResult) ? ContentDialogResult.None : ContentDialogResult.Primary;
    }


    private void PasswordBox_Password_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Enter)
        {
            e.Handled = true;
            Button_Login_Click(sender, e);
        }
    }


    private async void Button_Login_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        string account = TextBox_Account.Text?.Trim() ?? "";
        string password = PasswordBox_Password.Password ?? "";
        if (string.IsNullOrWhiteSpace(account))
        {
            ShowFeedback(new ArgumentException("Account is required.", "account"));
            return;
        }
        if (string.IsNullOrEmpty(password))
        {
            ShowFeedback(new ArgumentException("Password is required.", "password"));
            return;
        }

        try
        {
            SetBusy(true);
            ClearFeedback();
            CookieResult = await _loginService.LoginAsync(account, password, ResolveAigisAsync, ResolveGeetestAsync, ResolveIdentityAsync);
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
    /// <c>x-rpc-aigis</c>：弹出独立人机验证层。
    /// </summary>
    private async Task<string?> ResolveAigisAsync(CaptchaAigis aigis, CancellationToken cancellationToken)
    {
        return await GeetestVerifyPopup.ShowAsync(XamlRoot, aigis, cancellationToken);
    }


    /// <summary>
    /// <c>x-rpc-verify</c> 人机验证：弹出独立人机验证层，返回极验结果原文。
    /// </summary>
    private async Task<string?> ResolveGeetestAsync(string geetestConfigJson, CancellationToken cancellationToken)
    {
        return await GeetestVerifyPopup.ShowRawAsync(XamlRoot, geetestConfigJson, "", cancellationToken);
    }


    /// <summary>
    /// <c>x-rpc-verify</c> 身份验证：弹出账号中心安全验证页。
    /// </summary>
    private async Task<bool> ResolveIdentityAsync(string actionTicket, CancellationToken cancellationToken)
    {
        return await HoyoverseIdentityVerifyPopup.ShowAsync(XamlRoot, actionTicket, GlobalPasswordLoginService.PassportAppId, GlobalPasswordLoginService.PassportGameBiz, cancellationToken);
    }


    /// <summary>
    /// 经 Factory 映射异常后，在对话框内 InfoBar 展示（可点关闭）。
    /// </summary>
    /// <param name="exception">业务或校验异常。</param>
    private void ShowFeedback(Exception exception)
    {
        var feedback = MiHoYoApiErrorFeedbackFactory.Create(exception, MiHoYoApiContext.PassportPassword);
        InfoBar_Feedback.Severity = feedback.Severity;
        InfoBar_Feedback.Title = feedback.Title;
        InfoBar_Feedback.Message = feedback.Message;
        InfoBar_Feedback.IsOpen = true;
    }


    /// <summary>
    /// 收起框内提示。
    /// </summary>
    private void ClearFeedback()
    {
        InfoBar_Feedback.IsOpen = false;
        InfoBar_Feedback.Title = string.Empty;
        InfoBar_Feedback.Message = string.Empty;
    }


    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressBar_Loading.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        TextBox_Account.IsEnabled = !busy;
        PasswordBox_Password.IsEnabled = !busy;
        Button_Login.IsEnabled = !busy;
        Button_Cancel.IsEnabled = !busy;
    }

}
