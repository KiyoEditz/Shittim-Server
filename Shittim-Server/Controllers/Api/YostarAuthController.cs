using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Schale.Data;
using Schale.Data.GameModel;
using Schale.Data.Models;
using BlueArchiveAPI.Configuration;
using BlueArchiveAPI.Services;

namespace Shittim_Server.Controllers.Api
{
    [ApiController]
    [Route("/")]
    public class YostarAuthController : ControllerBase
    {
        private readonly IDbContextFactory<SchaleDataContext> _dbFactory;
        private readonly ILogger<YostarAuthController> _logger;

        public YostarAuthController(
            IDbContextFactory<SchaleDataContext> dbFactory,
            ILogger<YostarAuthController> logger)
        {
            _dbFactory = dbFactory;
            _logger = logger;
        }

        private async Task<Dictionary<string, string>> ParseRequestParametersAsync()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (k, v) in Request.Query)
            {
                dict[k] = v.ToString();
            }

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (var (k, v) in form)
                {
                    dict[k] = v.ToString();
                }
            }
            else
            {
                Request.EnableBuffering();
                using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                Request.Body.Position = 0;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    try
                    {
                        var json = JObject.Parse(body);
                        void ExtractProperties(JObject obj)
                        {
                            foreach (var prop in obj.Properties())
                            {
                                if (prop.Value is JObject nestedObj)
                                {
                                    ExtractProperties(nestedObj);
                                }
                                else if (prop.Value != null)
                                {
                                    dict[prop.Name] = prop.Value.ToString();
                                }
                            }
                        }
                        ExtractProperties(json);
                    }
                    catch
                    {
                        var parts = body.Split('&', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            var kv = part.Split('=', 2);
                            var k = Uri.UnescapeDataString(kv[0]);
                            var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                            dict[k] = v;
                        }
                    }
                }
            }

            return dict;
        }

        [HttpGet("common/config")]
        [HttpPost("common/config")]
        [HttpGet("api/common/config")]
        [HttpPost("api/common/config")]
        public IActionResult CommonConfig()
        {
            return Ok(new
            {
                Code = 0,
                Msg = "success",
                Data = new
                {
                    login_switch = 1,
                    user_agreement = 0
                }
            });
        }

        [HttpGet("common/version")]
        [HttpPost("common/version")]
        [HttpGet("api/common/version")]
        [HttpPost("api/common/version")]
        public IActionResult CommonVersion()
        {
            return Ok(new
            {
                Code = 0,
                Msg = "success",
                Data = new
                {
                    version = "1.73.0"
                }
            });
        }

        [HttpGet("common/agreement")]
        [HttpPost("common/agreement")]
        [HttpGet("common/agreement/confirm")]
        [HttpPost("common/agreement/confirm")]
        [HttpGet("common/client-code")]
        [HttpPost("common/client-code")]
        [HttpGet("common/geo-ip")]
        [HttpPost("common/geo-ip")]
        [HttpGet("user/agreement")]
        [HttpPost("user/agreement")]
        [HttpGet("user/agreement/confirm")]
        [HttpPost("user/agreement/confirm")]
        public IActionResult CommonAgreement()
        {
            return Ok(new
            {
                Code = 0,
                Msg = "success",
                Data = new { }
            });
        }

        [HttpGet("yostar/send-code")]
        [HttpPost("yostar/send-code")]
        [HttpGet("api/yostar/send-code")]
        [HttpPost("api/yostar/send-code")]
        public async Task<IActionResult> SendCode()
        {
            var p = await ParseRequestParametersAsync();
            var email = p.GetValueOrDefault("email") ?? p.GetValueOrDefault("account") ?? "";
            _logger.LogInformation("[Yostar SDK] Verification code requested for fake email: {Email}", email);

            var root = new Dictionary<string, object>
            {
                ["code"] = 0,
                ["Code"] = 0,
                ["status"] = 0,
                ["Status"] = 0,
                ["result"] = 0,
                ["Result"] = 0,
                ["R_CODE"] = 0,
                ["R_MSG"] = "success",
                ["msg"] = "success",
                ["Msg"] = "success",
                ["data"] = new Dictionary<string, object>()
            };
            return Content(Newtonsoft.Json.JsonConvert.SerializeObject(root), "application/json");
        }

        [HttpGet("yostar/get-auth")]
        [HttpPost("yostar/get-auth")]
        [HttpGet("api/yostar/get-auth")]
        [HttpPost("api/yostar/get-auth")]
        public async Task<IActionResult> GetAuth()
        {
            var p = await ParseRequestParametersAsync();
            var email = (p.GetValueOrDefault("email") ?? p.GetValueOrDefault("account") ?? "").Trim().ToLowerInvariant();
            var code = p.GetValueOrDefault("code") ?? "000000";

            if (string.IsNullOrEmpty(email))
                email = "sensei@shittim.local";

            _logger.LogInformation("[Yostar SDK] Auth requested for email: {Email}, code: {Code}", email, code);

            await using var db = await _dbFactory.CreateDbContextAsync();

            var user = await db.UserAccounts
                .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email);

            AccountDBServer? account = null;

            if (user == null)
            {
                // Generate a unique publisher ID for this account
                long publisherId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                while (await db.UserAccounts.AnyAsync(u => u.NpSN == publisherId) || await db.Accounts.AnyAsync(a => a.PublisherAccountId == publisherId))
                    publisherId++;

                var token = $"yostar-{Guid.NewGuid():N}";
                user = new UserAccount
                {
                    Uid = -1,
                    NpSN = publisherId,
                    NpToken = token,
                    Email = email
                };
                db.UserAccounts.Add(user);

                var nick = email.Contains('@') ? email.Split('@')[0] : email;
                if (string.IsNullOrWhiteSpace(nick)) nick = "Sensei";

                account = new AccountDBServer(publisherId)
                {
                    Nickname = nick,
                    CallName = nick
                };
                db.Accounts.Add(account);
                await db.SaveChangesAsync();

                account = await db.Accounts.FirstAsync(a => a.PublisherAccountId == publisherId);
                user = await db.UserAccounts.FirstAsync(u => u.NpSN == publisherId);
                user.Uid = account.ServerId;

                await AccountInitializationService.InitializeCompleteAccount(db, account);
                await db.SaveChangesAsync();
                _logger.LogInformation("[Yostar SDK] Created new account #{ServerId} for email: {Email} (NpSN: {PublisherId})", account.ServerId, email, publisherId);
            }
            else
            {
                if (string.IsNullOrEmpty(user.NpToken))
                {
                    user.NpToken = $"yostar-{Guid.NewGuid():N}";
                    await db.SaveChangesAsync();
                }

                account = await db.Accounts.FirstOrDefaultAsync(a => a.ServerId == user.Uid || a.PublisherAccountId == user.NpSN);
                if (account == null)
                {
                    account = new AccountDBServer(user.NpSN)
                    {
                        Nickname = email.Contains('@') ? email.Split('@')[0] : "Sensei",
                        CallName = "Sensei"
                    };
                    db.Accounts.Add(account);
                    await db.SaveChangesAsync();
                    user.Uid = account.ServerId;
                    await AccountInitializationService.InitializeCompleteAccount(db, account);
                    await db.SaveChangesAsync();
                }
                _logger.LogInformation("[Yostar SDK] Authenticated existing account #{ServerId} for email: {Email} (NpSN: {PublisherId})", account.ServerId, email, user.NpSN);
            }

            return CreateYostarLoginResponse(user, account, email);
        }

        [HttpGet("yostar/token-login")]
        [HttpPost("yostar/token-login")]
        [HttpGet("api/yostar/token-login")]
        [HttpPost("api/yostar/token-login")]
        [HttpGet("user/quick-login")]
        [HttpPost("user/quick-login")]
        [HttpGet("api/user/quick-login")]
        [HttpPost("api/user/quick-login")]
        [HttpGet("user/login")]
        [HttpPost("user/login")]
        [HttpGet("api/user/login")]
        [HttpPost("api/user/login")]
        public async Task<IActionResult> QuickOrTokenLogin()
        {
            var p = await ParseRequestParametersAsync();
            var token = p.GetValueOrDefault("token") ?? p.GetValueOrDefault("auth") ?? p.GetValueOrDefault("ticket");
            var uidStr = p.GetValueOrDefault("uid") ?? p.GetValueOrDefault("userId") ?? p.GetValueOrDefault("id");
            var email = p.GetValueOrDefault("email") ?? p.GetValueOrDefault("account") ?? p.GetValueOrDefault("login_name");

            await using var db = await _dbFactory.CreateDbContextAsync();

            UserAccount? user = null;

            if (!string.IsNullOrEmpty(uidStr) && long.TryParse(uidStr, out var parsedUid))
            {
                user = await db.UserAccounts.FirstOrDefaultAsync(u => u.NpSN == parsedUid || u.Uid == parsedUid);
            }

            if (user == null && !string.IsNullOrEmpty(token))
            {
                user = await db.UserAccounts.FirstOrDefaultAsync(u => u.NpToken == token);
            }

            if (user == null && !string.IsNullOrEmpty(email))
            {
                user = await db.UserAccounts.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email.Trim().ToLowerInvariant());
            }

            // Check if an account is selected in Control Center
            var selectedId = Config.Instance.ServerConfiguration.SelectedAccountId;
            if (user == null && selectedId > 0)
            {
                user = await db.UserAccounts.FirstOrDefaultAsync(u => u.Uid == selectedId);
            }

            // Fallback to first existing user account or create default
            if (user == null)
            {
                user = await db.UserAccounts.FirstOrDefaultAsync();
            }

            if (user == null)
            {
                // Create default sensei account
                long publisherId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var newToken = $"yostar-{Guid.NewGuid():N}";
                user = new UserAccount
                {
                    Uid = -1,
                    NpSN = publisherId,
                    NpToken = newToken,
                    Email = "sensei@shittim.local"
                };
                db.UserAccounts.Add(user);

                var account = new AccountDBServer(publisherId)
                {
                    Nickname = "Sensei",
                    CallName = "Sensei"
                };
                db.Accounts.Add(account);
                await db.SaveChangesAsync();

                account = await db.Accounts.FirstAsync(a => a.PublisherAccountId == publisherId);
                user = await db.UserAccounts.FirstAsync(u => u.NpSN == publisherId);
                user.Uid = account.ServerId;
                await AccountInitializationService.InitializeCompleteAccount(db, account);
                await db.SaveChangesAsync();
            }

            if (string.IsNullOrEmpty(user.NpToken))
            {
                user.NpToken = $"yostar-{Guid.NewGuid():N}";
                await db.SaveChangesAsync();
            }

            var finalEmail = user.Email ?? "sensei@shittim.local";
            var userAccount = await db.Accounts.FirstOrDefaultAsync(a => a.ServerId == user.Uid || a.PublisherAccountId == user.NpSN);

            return CreateYostarLoginResponse(user, userAccount, finalEmail);
        }

        [HttpGet("user/detail")]
        [HttpPost("user/detail")]
        [HttpGet("api/user/detail")]
        [HttpPost("api/user/detail")]
        public async Task<IActionResult> UserDetail()
        {
            var p = await ParseRequestParametersAsync();
            var token = p.GetValueOrDefault("token") ?? p.GetValueOrDefault("auth") ?? p.GetValueOrDefault("ticket");
            var uidStr = p.GetValueOrDefault("uid") ?? p.GetValueOrDefault("userId") ?? p.GetValueOrDefault("id");
            var email = p.GetValueOrDefault("email") ?? p.GetValueOrDefault("account") ?? p.GetValueOrDefault("login_name");

            await using var db = await _dbFactory.CreateDbContextAsync();
            UserAccount? user = null;

            if (!string.IsNullOrEmpty(uidStr) && long.TryParse(uidStr, out var parsedUid))
            {
                user = await db.UserAccounts.FirstOrDefaultAsync(u => u.NpSN == parsedUid || u.Uid == parsedUid);
            }

            if (user == null && !string.IsNullOrEmpty(token))
            {
                user = await db.UserAccounts.FirstOrDefaultAsync(u => u.NpToken == token);
            }

            if (user == null && !string.IsNullOrEmpty(email))
            {
                user = await db.UserAccounts.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email.Trim().ToLowerInvariant());
            }

            var selectedId = Config.Instance.ServerConfiguration.SelectedAccountId;
            if (user == null && selectedId > 0)
            {
                user = await db.UserAccounts.FirstOrDefaultAsync(u => u.Uid == selectedId);
            }

            user ??= await db.UserAccounts.FirstOrDefaultAsync();

            var uidLong = user?.NpSN ?? 1L;
            var finalUid = uidLong.ToString();
            var finalEmail = user?.Email ?? "sensei@shittim.local";
            var account = user != null ? await db.Accounts.FirstOrDefaultAsync(a => a.ServerId == user.Uid || a.PublisherAccountId == user.NpSN) : null;
            var nick = account?.Nickname ?? (finalEmail.Contains('@') ? finalEmail.Split('@')[0] : finalEmail);
            if (string.IsNullOrWhiteSpace(nick)) nick = "Sensei";
            var userToken = user?.NpToken ?? $"yostar-{Guid.NewGuid():N}";
            var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var userInfo = BuildUserInfoDict(finalUid, uidLong, userToken, nowSec);
            var yostar = BuildYostarDict(finalUid, nick, nowSec);

            var detailData = new Dictionary<string, object>
            {
                ["age_verify_method"] = 0,
                ["AgeVerifyMethod"] = 0,
                ["server_now_at"] = nowSec,
                ["ServerNowAt"] = nowSec,
                ["destroy"] = null,
                ["Destroy"] = null,
                ["yostar_destroy"] = null,
                ["YostarDestroy"] = null,
                ["is_test_account"] = 0,
                ["IsTestAccount"] = 0,
                ["keys"] = new object[0],
                ["Keys"] = new object[0],
                ["kmc_info"] = null,
                ["KmcInfo"] = null,
                ["kmc_status"] = 0,
                ["KmcStatus"] = 0,
                ["user_info"] = userInfo,
                ["UserInfo"] = userInfo,
                ["userInfo"] = userInfo,
                ["yostar"] = yostar,
                ["Yostar"] = yostar
            };

            var root = new Dictionary<string, object>
            {
                ["code"] = 0,
                ["Code"] = 0,
                ["msg"] = "success",
                ["Msg"] = "success",
                ["status"] = 0,
                ["Status"] = 0,
                ["result"] = 0,
                ["Result"] = 0,
                ["R_CODE"] = 0,
                ["R_MSG"] = "success",
                ["data"] = detailData,
                ["Data"] = detailData
            };

            var jsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(root);
            return Content(jsonStr, "application/json");
        }

        [HttpGet("user/check-captcha")]
        [HttpPost("user/check-captcha")]
        [HttpGet("api/user/check-captcha")]
        [HttpPost("api/user/check-captcha")]
        public IActionResult CheckCaptcha()
        {
            var root = new Dictionary<string, object>
            {
                ["code"] = 0,
                ["Code"] = 0,
                ["msg"] = "success",
                ["Msg"] = "success",
                ["status"] = 0,
                ["Status"] = 0,
                ["result"] = 0,
                ["Result"] = 0,
                ["R_CODE"] = 0,
                ["R_MSG"] = "success",
                ["data"] = new Dictionary<string, object>
                {
                    ["check"] = 0,
                    ["Check"] = 0
                }
            };
            return Content(Newtonsoft.Json.JsonConvert.SerializeObject(root), "application/json");
        }

        [HttpGet("user/device-list")]
        [HttpPost("user/device-list")]
        [HttpGet("api/user/device-list")]
        [HttpPost("api/user/device-list")]
        public IActionResult DeviceList()
        {
            var root = new Dictionary<string, object>
            {
                ["code"] = 0,
                ["Code"] = 0,
                ["msg"] = "success",
                ["Msg"] = "success",
                ["status"] = 0,
                ["Status"] = 0,
                ["result"] = 0,
                ["Result"] = 0,
                ["R_CODE"] = 0,
                ["R_MSG"] = "success",
                ["data"] = new object[0]
            };
            return Content(Newtonsoft.Json.JsonConvert.SerializeObject(root), "application/json");
        }

        [HttpGet("user/remove-device")]
        [HttpPost("user/remove-device")]
        [HttpGet("user/set")]
        [HttpPost("user/set")]
        [HttpGet("user/set-info")]
        [HttpPost("user/set-info")]
        [HttpGet("user/kmc-url")]
        [HttpPost("user/kmc-url")]
        [HttpGet("user/share/upload")]
        [HttpPost("user/share/upload")]
        [HttpGet("user/token-migrate")]
        [HttpPost("user/token-migrate")]
        [HttpGet("user/unlink")]
        [HttpPost("user/unlink")]
        [HttpGet("user/link")]
        [HttpPost("user/link")]
        [HttpGet("user/relink")]
        [HttpPost("user/relink")]
        [HttpGet("user/destroy")]
        [HttpPost("user/destroy")]
        [HttpGet("user/cancel-destroy")]
        [HttpPost("user/cancel-destroy")]
        [HttpGet("user/check-text")]
        [HttpPost("user/check-text")]
        [HttpGet("heartbeat/pulse")]
        [HttpPost("heartbeat/pulse")]
        [HttpGet("yostar/get-account-center-url")]
        [HttpPost("yostar/get-account-center-url")]
        [HttpGet("change-email/send-code")]
        [HttpPost("change-email/send-code")]
        [HttpGet("change-email/validate")]
        [HttpPost("change-email/validate")]
        [HttpGet("api/open/ip")]
        [HttpPost("api/open/ip")]
        [HttpGet("api/open/red_point")]
        [HttpPost("api/open/red_point")]
        [HttpGet("health-game/duration")]
        [HttpPost("health-game/duration")]
        [HttpGet("health-game/identity-auth")]
        [HttpPost("health-game/identity-auth")]
        [HttpGet("user/identity-auth/send")]
        [HttpPost("user/identity-auth/send")]
        [HttpGet("user/identity-auth/verify")]
        [HttpPost("user/identity-auth/verify")]
        [HttpGet("order/detail")]
        [HttpPost("order/detail")]
        [HttpGet("order/products")]
        [HttpPost("order/products")]
        [HttpGet("order/create")]
        [HttpPost("order/create")]
        [HttpGet("order/confirm")]
        [HttpPost("order/confirm")]
        [HttpGet("order/gmo/creditcard/list")]
        [HttpPost("order/gmo/creditcard/list")]
        [HttpGet("order/gmo/creditcard/delete")]
        [HttpPost("order/gmo/creditcard/delete")]
        [HttpGet("order/gmo/creditcard/add")]
        [HttpPost("order/gmo/creditcard/add")]
        [HttpGet("user/send-sms")]
        [HttpPost("user/send-sms")]
        [HttpGet("user/send-email")]
        [HttpPost("user/send-email")]
        [HttpGet("user/survey")]
        [HttpPost("user/survey")]
        [HttpGet("user/gen-transcode")]
        [HttpPost("user/gen-transcode")]
        [HttpGet("api/user/remove-device")]
        [HttpPost("api/user/remove-device")]
        [HttpGet("api/user/set")]
        [HttpPost("api/user/set")]
        [HttpGet("api/user/set-info")]
        [HttpPost("api/user/set-info")]
        [HttpGet("api/user/kmc-url")]
        [HttpPost("api/user/kmc-url")]
        [HttpGet("api/user/share/upload")]
        [HttpPost("api/user/share/upload")]
        [HttpGet("api/user/token-migrate")]
        [HttpPost("api/user/token-migrate")]
        [HttpGet("api/user/unlink")]
        [HttpPost("api/user/unlink")]
        [HttpGet("api/user/link")]
        [HttpPost("api/user/link")]
        [HttpGet("api/user/relink")]
        [HttpPost("api/user/relink")]
        [HttpGet("api/user/destroy")]
        [HttpPost("api/user/destroy")]
        [HttpGet("api/user/cancel-destroy")]
        [HttpPost("api/user/cancel-destroy")]
        [HttpGet("api/user/check-text")]
        [HttpPost("api/user/check-text")]
        [HttpGet("api/heartbeat/pulse")]
        [HttpPost("api/heartbeat/pulse")]
        [HttpGet("api/yostar/get-account-center-url")]
        [HttpPost("api/yostar/get-account-center-url")]
        [HttpGet("api/change-email/send-code")]
        [HttpPost("api/change-email/send-code")]
        [HttpGet("api/change-email/validate")]
        [HttpPost("api/change-email/validate")]
        [HttpGet("api/health-game/duration")]
        [HttpPost("api/health-game/duration")]
        [HttpGet("api/health-game/identity-auth")]
        [HttpPost("api/health-game/identity-auth")]
        [HttpGet("api/user/identity-auth/send")]
        [HttpPost("api/user/identity-auth/send")]
        [HttpGet("api/user/identity-auth/verify")]
        [HttpPost("api/user/identity-auth/verify")]
        [HttpGet("api/order/detail")]
        [HttpPost("api/order/detail")]
        [HttpGet("api/order/products")]
        [HttpPost("api/order/products")]
        [HttpGet("api/order/create")]
        [HttpPost("api/order/create")]
        [HttpGet("api/order/confirm")]
        [HttpPost("api/order/confirm")]
        [HttpGet("api/order/gmo/creditcard/list")]
        [HttpPost("api/order/gmo/creditcard/list")]
        [HttpGet("api/order/gmo/creditcard/delete")]
        [HttpPost("api/order/gmo/creditcard/delete")]
        [HttpGet("api/order/gmo/creditcard/add")]
        [HttpPost("api/order/gmo/creditcard/add")]
        [HttpGet("api/user/send-sms")]
        [HttpPost("api/user/send-sms")]
        [HttpGet("api/user/send-email")]
        [HttpPost("api/user/send-email")]
        [HttpGet("api/user/survey")]
        [HttpPost("api/user/survey")]
        [HttpGet("api/user/gen-transcode")]
        [HttpPost("api/user/gen-transcode")]
        public IActionResult GenericYostarSuccess()
        {
            var root = new Dictionary<string, object>
            {
                ["code"] = 0,
                ["Code"] = 0,
                ["msg"] = "success",
                ["Msg"] = "success",
                ["status"] = 0,
                ["Status"] = 0,
                ["result"] = 0,
                ["Result"] = 0,
                ["R_CODE"] = 0,
                ["R_MSG"] = "success",
                ["data"] = new Dictionary<string, object>()
            };
            return Content(Newtonsoft.Json.JsonConvert.SerializeObject(root), "application/json");
        }

        [HttpPost("yostar/gen-token")]
        [HttpPost("api/yostar/gen-token")]
        public IActionResult GenToken()
        {
            var root = new Dictionary<string, object>
            {
                ["code"] = 0,
                ["Code"] = 0,
                ["msg"] = "success",
                ["Msg"] = "success",
                ["status"] = 0,
                ["Status"] = 0,
                ["result"] = 0,
                ["Result"] = 0,
                ["R_CODE"] = 0,
                ["R_MSG"] = "success",
                ["data"] = new Dictionary<string, object>
                {
                    ["token"] = Guid.NewGuid().ToString("N")
                }
            };
            return Content(Newtonsoft.Json.JsonConvert.SerializeObject(root), "application/json");
        }

        private ContentResult CreateYostarLoginResponse(UserAccount user, AccountDBServer? account, string fallbackEmail)
        {
            var uidLong = user.NpSN;
            var uidStr = uidLong.ToString();
            var email = user.Email ?? fallbackEmail;
            var nick = account?.Nickname ?? (email.Contains('@') ? email.Split('@')[0] : email);
            if (string.IsNullOrWhiteSpace(nick)) nick = "Sensei";
            var token = user.NpToken ?? $"yostar-{Guid.NewGuid():N}";
            var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var userInfo = BuildUserInfoDict(uidStr, uidLong, token, nowSec);
            var yostar = BuildYostarDict(uidStr, nick, nowSec);
            var data = BuildLoginDataDict(uidStr, uidLong, token, email, userInfo, yostar);

            var rData = new Dictionary<string, object>
            {
                ["auth"] = token,
                ["ticket"] = token,
                ["token"] = token,
                ["uid"] = uidStr,
                ["code"] = 0
            };

            var root = new Dictionary<string, object>
            {
                ["code"] = 0,
                ["Code"] = 0,
                ["msg"] = "success",
                ["Msg"] = "success",
                ["status"] = 0,
                ["Status"] = 0,
                ["result"] = 0,
                ["Result"] = 0,
                ["R_CODE"] = 0,
                ["R_MSG"] = "success",
                ["RESULT_CODE"] = 0,
                ["RESULT_DESCRIPTION"] = "success",
                ["LOGIN_PLATFORM"] = "YOSTAR",
                ["login_platform"] = "YOSTAR",
                ["platform"] = "YOSTAR",
                ["Platform"] = "YOSTAR",
                ["LOGIN_UID"] = uidStr,
                ["LOGIN_UID_2"] = uidStr,
                ["login_uid"] = uidStr,
                ["uid"] = uidStr,
                ["UID"] = uidStr,
                ["LOGIN_TOKEN"] = token,
                ["login_token"] = token,
                ["token"] = token,
                ["Token"] = token,
                ["auth"] = token,
                ["ticket"] = token,
                ["LOGIN_NAME"] = email,
                ["login_name"] = email,
                ["YOSTAR_NAME"] = email,
                ["data"] = data,
                ["Data"] = data,
                ["R_DATA"] = rData
            };

            var jsonStr = Newtonsoft.Json.JsonConvert.SerializeObject(root);
            return Content(jsonStr, "application/json");
        }

        private static Dictionary<string, object> BuildLoginDataDict(
            string uidStr, long uidLong, string token, string email,
            Dictionary<string, object> userInfo, Dictionary<string, object> yostar)
        {
            return new Dictionary<string, object>
            {
                ["uid"] = uidStr,
                ["UID"] = uidStr,
                ["token"] = token,
                ["Token"] = token,
                ["auth"] = token,
                ["ticket"] = token,
                ["login_name"] = email,
                ["LoginName"] = email,
                ["platform"] = "YOSTAR",
                ["Platform"] = "YOSTAR",
                ["is_new"] = 0,
                ["IsNew"] = 0,
                ["isNew"] = 0,
                ["age_verify_method"] = 0,
                ["AgeVerifyMethod"] = 0,
                ["uid2"] = uidLong,
                ["UID2"] = uidLong,
                ["default"] = "0",
                ["DEFAULT"] = "0",
                ["icon_size"] = "0",
                ["ICON_SIZE"] = "0",
                ["sort"] = new string[0],
                ["SORT"] = new string[0],
                ["user_info"] = userInfo,
                ["UserInfo"] = userInfo,
                ["userInfo"] = userInfo,
                ["yostar"] = yostar,
                ["Yostar"] = yostar
            };
        }

        private static Dictionary<string, object> BuildUserInfoDict(string uidStr, long uidLong, string token, long nowSec)
        {
            return new Dictionary<string, object>
            {
                ["id"] = uidStr,
                ["ID"] = uidStr,
                ["uid"] = uidStr,
                ["UID"] = uidStr,
                ["uid2"] = uidLong,
                ["UID2"] = uidLong,
                ["pid"] = uidStr,
                ["PID"] = uidStr,
                ["token"] = token,
                ["Token"] = token,
                ["birthday"] = "2000-01-01",
                ["Birthday"] = "2000-01-01",
                ["reg_channel"] = "YOSTAR",
                ["RegChannel"] = "YOSTAR",
                ["trans_code"] = "",
                ["TransCode"] = "",
                ["state"] = 1L,
                ["State"] = 1L,
                ["device_id"] = "shittim-device",
                ["DeviceID"] = "shittim-device",
                ["created_at"] = nowSec,
                ["CreatedAt"] = nowSec
            };
        }

        private static Dictionary<string, object> BuildYostarDict(string uidStr, string nick, long nowSec)
        {
            return new Dictionary<string, object>
            {
                ["id"] = uidStr,
                ["ID"] = uidStr,
                ["country"] = "JP",
                ["Country"] = "JP",
                ["nickname"] = nick,
                ["Nickname"] = nick,
                ["name"] = nick,
                ["Name"] = nick,
                ["picture"] = "",
                ["Picture"] = "",
                ["state"] = 1L,
                ["State"] = 1L,
                ["agree_ad"] = 1L,
                ["AgreeAd"] = 1L,
                ["created_at"] = nowSec,
                ["CreatedAt"] = nowSec
            };
        }
    }
}

