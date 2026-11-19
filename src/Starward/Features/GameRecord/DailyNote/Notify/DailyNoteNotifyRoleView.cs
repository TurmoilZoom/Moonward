using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Starward.Core;
using Starward.Core.GameRecord;

namespace Starward.Features.GameRecord.DailyNote.Notify;

/// <summary>
/// 提醒设置对话框左侧账号列表的一项。x:Bind 数据模板实例化，不能用 <c>required</c> / <c>init</c>。
/// </summary>
public sealed partial class DailyNoteNotifyRoleView : ObservableObject
{

    /// <summary>
    /// 创建一个账号项。
    /// </summary>
    /// <param name="role">战绩角色。</param>
    /// <param name="hasReminder">该账号是否开了至少一项提醒。</param>
    internal DailyNoteNotifyRoleView(GameRecordRole role, bool hasReminder)
    {
        Role = role;
        HasReminder = hasReminder;
        GameAndServer = $"{new GameBiz(role.GameBiz).ToGameName()} · {role.RegionName}";
    }


    /// <summary>战绩角色。</summary>
    internal GameRecordRole Role { get; }

    /// <summary>昵称。</summary>
    public string Nickname => Role.Nickname;

    /// <summary>头像地址。</summary>
    public string HeadIcon => Role.HeadIcon;

    /// <summary>游戏与区服，如「原神 · 天空岛」。</summary>
    public string GameAndServer { get; }

    /// <summary>游戏、区服与 UID，右侧详情标题下的次要文字。</summary>
    public string GameServerAndUid => $"{GameAndServer} · UID {Role.Uid}";


    /// <summary>该账号是否开了至少一项提醒，开了时在列表里显示铃铛。</summary>
    public bool HasReminder
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ReminderVisibility));
            }
        }
    }

    /// <summary>铃铛标记的可见性。</summary>
    public Visibility ReminderVisibility => HasReminder ? Visibility.Visible : Visibility.Collapsed;


    /// <summary>
    /// 是否为同一个账号（区服 + UID）。
    /// </summary>
    /// <param name="role">要比较的战绩角色。</param>
    /// <returns>同一账号返回 <see langword="true"/>。</returns>
    internal bool IsSameRole(GameRecordRole? role) => role is not null && role.Uid == Role.Uid && role.GameBiz == Role.GameBiz;

}
