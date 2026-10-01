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
                        foreach (var prop in json.Properties())
                        {
                            dict[prop.Name] = prop.Value?.ToString() ?? "";
                        }
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

        [HttpPost("yostar/send-code")]
        [HttpPost("api/yostar/send-code")]
        public async Task<IActionResult> SendCode()
        {
            var p = await ParseRequestParametersAsync();
            var email = p.GetValueOrDefault("email") ?? p.GetValueOrDefault("account") ?? "";
            _logger.LogInformation("[Yostar SDK] Verification code requested for fake email: {Email}", email);

            return Ok(new
            {
                Code = 0,
                Msg = "success",
                Data = new { }
            });
        }

        [HttpPost("yostar/get-auth")]
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

            var uidStr = user.NpSN.ToString();
            return Ok(new
            {
                Code = 0,
                Msg = "success",
                LOGIN_PLATFORM = "YOSTAR",
                LOGIN_UID = uidStr,
                LOGIN_TOKEN = user.NpToken,
                LOGIN_NAME = user.Email ?? email,
                Data = new
                {
                    uid = uidStr,
                    token = user.NpToken,
                    login_name = user.Email ?? email,
                    platform = "YOSTAR"
                }
            });
        }

        [HttpPost("yostar/token-login")]
        [HttpPost("api/yostar/token-login")]
        [HttpPost("user/quick-login")]
        [HttpPost("api/user/quick-login")]
        [HttpPost("user/login")]
        [HttpPost("api/user/login")]
        public async Task<IActionResult> QuickOrTokenLogin()
        {
            var p = await ParseRequestParametersAsync();
            var token = p.GetValueOrDefault("token");
            var uidStr = p.GetValueOrDefault("uid") ?? p.GetValueOrDefault("userId");

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

            var finalUid = user.NpSN.ToString();
            var finalEmail = user.Email ?? "sensei@shittim.local";
            if (string.IsNullOrEmpty(user.NpToken))
            {
                user.NpToken = $"yostar-{Guid.NewGuid():N}";
                await db.SaveChangesAsync();
            }

            return Ok(new
            {
                Code = 0,
                Msg = "success",
                LOGIN_PLATFORM = "YOSTAR",
                LOGIN_UID = finalUid,
                LOGIN_TOKEN = user.NpToken,
                LOGIN_NAME = finalEmail,
                Data = new
                {
                    uid = finalUid,
                    token = user.NpToken,
                    login_name = finalEmail,
                    platform = "YOSTAR"
                }
            });
        }

        [HttpPost("yostar/gen-token")]
        [HttpPost("api/yostar/gen-token")]
        public IActionResult GenToken()
        {
            return Ok(new
            {
                Code = 0,
                Msg = "success",
                Data = new
                {
                    token = Guid.NewGuid().ToString("N")
                }
            });
        }
    }
}
