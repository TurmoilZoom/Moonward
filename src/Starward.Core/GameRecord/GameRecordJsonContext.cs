using Starward.Core.GameRecord.BH3.DailyNote;
using Starward.Core.GameRecord.Genshin.DailyNote;
using Starward.Core.GameRecord.Genshin.ImaginariumTheater;
using Starward.Core.GameRecord.Genshin.SpiralAbyss;
using Starward.Core.GameRecord.Genshin.StygianOnslaught;
using Starward.Core.GameRecord.Genshin.TravelersDiary;
using Starward.Core.GameRecord.StarRail.ApocalypticShadow;
using Starward.Core.GameRecord.StarRail.ChallengePeak;
using Starward.Core.GameRecord.StarRail.DailyNote;
using Starward.Core.GameRecord.StarRail.ForgottenHall;
using Starward.Core.GameRecord.StarRail.PureFiction;
using Starward.Core.GameRecord.StarRail.SimulatedUniverse;
using Starward.Core.GameRecord.StarRail.TrailblazeCalendar;
using Starward.Core.GameRecord.ZZZ.DailyNote;
using Starward.Core.GameRecord.ZZZ.DeadlyAssault;
using Starward.Core.GameRecord.ZZZ.GachaRecord;
using Starward.Core.GameRecord.ZZZ.InterKnotReport;
using Starward.Core.GameRecord.ZZZ.ShiyuDefense;
using Starward.Core.GameRecord.SignIn;
using Starward.Core.GameRecord.Passport;
using Starward.Core.GameRecord.ZZZ.ThresholdSimulation;
using Starward.Core.GameRecord.ZZZ.UpgradeGuide;
using Starward.Core.JsonConverter;
using System.Text.Json.Serialization;

namespace Starward.Core.GameRecord;


[JsonSerializable(typeof(miHoYoApiWrapper<GameRecordUserWrapper>))]
[JsonSerializable(typeof(miHoYoApiWrapper<GameRecordRoleWrapper>))]
[JsonSerializable(typeof(miHoYoApiWrapper<GameRecordIndex>))]
[JsonSerializable(typeof(miHoYoApiWrapper<SpiralAbyssInfo>))]
[JsonSerializable(typeof(miHoYoApiWrapper<StygianOnslaughtWrapper>))]
[JsonSerializable(typeof(miHoYoApiWrapper<TravelersDiarySummary>))]
[JsonSerializable(typeof(miHoYoApiWrapper<TravelersDiaryDetail>))]
[JsonSerializable(typeof(miHoYoApiWrapper<TrailblazeCalendarSummary>))]
[JsonSerializable(typeof(miHoYoApiWrapper<TrailblazeCalendarDetail>))]
[JsonSerializable(typeof(miHoYoApiWrapper<ForgottenHallInfo>))]
[JsonSerializable(typeof(miHoYoApiWrapper<PureFictionInfo>))]
[JsonSerializable(typeof(miHoYoApiWrapper<ApocalypticShadowInfo>))]
[JsonSerializable(typeof(miHoYoApiWrapper<SimulatedUniverseInfo>))]
[JsonSerializable(typeof(miHoYoApiWrapper<ChallengePeakData>))]
[JsonSerializable(typeof(miHoYoApiWrapper<DeviceFpResult>))]
[JsonSerializable(typeof(miHoYoApiWrapper<ImaginariumTheaterWarpper>))]
[JsonSerializable(typeof(miHoYoApiWrapper<InterKnotReportSummary>))]
[JsonSerializable(typeof(miHoYoApiWrapper<InterKnotReportDetail>))]
[JsonSerializable(typeof(miHoYoApiWrapper<UpgradeGuideItemList>))]
[JsonSerializable(typeof(miHoYoApiWrapper<UpgradeGuidIconInfo>))]
[JsonSerializable(typeof(miHoYoApiWrapper<GenshinDailyNote>))]
[JsonSerializable(typeof(miHoYoApiWrapper<StarRailDailyNote>))]
[JsonSerializable(typeof(miHoYoApiWrapper<ShiyuDefenseWrapper>))]
[JsonSerializable(typeof(miHoYoApiWrapper<DeadlyAssaultInfo>))]
[JsonSerializable(typeof(miHoYoApiWrapper<ZZZDailyNote>))]
[JsonSerializable(typeof(miHoYoApiWrapper<ZZZGachaRecordData>))]
[JsonSerializable(typeof(miHoYoApiWrapper<GameAuthKey>))]
[JsonSerializable(typeof(GenAuthKeyPostBody))]
[JsonSerializable(typeof(GameAuthKey))]
[JsonSerializable(typeof(miHoYoApiWrapper<BH3DailyNote>))]
[JsonSerializable(typeof(miHoYoApiWrapper<ThresholdSimulationAbstractInfo>))]
// 每日签到（luna/sol）请求与响应类型
[JsonSerializable(typeof(miHoYoApiWrapper<SignInRewardInfo>))]
[JsonSerializable(typeof(miHoYoApiWrapper<SignInReward>))]
[JsonSerializable(typeof(miHoYoApiWrapper<SignInResignInfo>))]
[JsonSerializable(typeof(miHoYoApiWrapper<SignInResult>))]
[JsonSerializable(typeof(SignInPostBody))]
// 短信验证码登录（passport）
[JsonSerializable(typeof(miHoYoApiWrapper<CreateLoginCaptchaResult>))]
[JsonSerializable(typeof(miHoYoApiWrapper<LoginByMobileCaptchaResult>))]
[JsonSerializable(typeof(miHoYoApiWrapper<LTokenBySTokenResult>))]
[JsonSerializable(typeof(miHoYoApiWrapper<CookieTokenBySTokenResult>))]
[JsonSerializable(typeof(CaptchaAigis))]
[JsonSerializable(typeof(CreateLoginCaptchaResult))]
[JsonSerializable(typeof(LoginByMobileCaptchaResult))]
[JsonSerializable(typeof(PassportToken))]
[JsonSerializable(typeof(PassportUserInfo))]
[JsonSerializable(typeof(LTokenBySTokenResult))]
[JsonSerializable(typeof(CookieTokenBySTokenResult))]
[JsonSerializable(typeof(DateTimeObjectJsonConverter.DateTimeObject))]
internal partial class GameRecordJsonContext : JsonSerializerContext
{

}
