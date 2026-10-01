using Microsoft.AspNetCore.Mvc;
using BlueArchiveAPI.Configuration;
using Newtonsoft.Json.Linq;
using System.Text.Json.Nodes;

namespace Shittim_Server.Controllers.Api
{
    [ApiController]
    [Route("/")]
    public class ServerInfoController : ControllerBase
    {
        private readonly ILogger<ServerInfoController> _logger;

        public ServerInfoController(ILogger<ServerInfoController> logger)
        {
            _logger = logger;
        }

        [HttpGet("com.nexon.bluearchive/server_config/{*filename}")]
        [HttpGet("com.nexon.bluearchivesteam/server_config/{*filename}")]
        public ActionResult GetServerUrl(string filename)
        {
            if (filename.EndsWith(".csv"))
            {
                Response.ContentType = "text/csv";
                return Content(string.Empty);
            }
            
            if (!filename.Contains("_Live") || !filename.EndsWith(".json"))
            {
                return NotFound();
            }

            var serverInfoConfig = Config.GetServerInfoConfig();
            
            var result = new JObject
            {
                ["DefaultConnectionGroup"] = serverInfoConfig.DefaultConnectionGroup,
                ["DefaultConnectionMode"] = serverInfoConfig.DefaultConnectionMode,
                ["ConnectionGroupsJson"] = serverInfoConfig.ConnectionGroupsJson,
                ["desc"] = serverInfoConfig.Desc
            };

            return Content(result.ToString(Newtonsoft.Json.Formatting.None), "text/plain");
        }

        [HttpGet("{filename}.json")]
        public ActionResult GetYostarServerConfig(string filename)
        {
            var hostAddr = Config.Instance.ServerConfiguration.HostAddress;
            var hostPort = Config.Instance.ServerConfiguration.HostPort;
            var gatewayPort = Config.Instance.ServerConfiguration.GatewayPort;

            var jpConfig = new JObject
            {
                ["ConnectionGroups"] = new JArray
                {
                    new JObject
                    {
                        ["Name"] = "Prod-Audit",
                        ["ManagementDataUrl"] = "https://prod-noticeindex.bluearchiveyostar.com/prod/index.json",
                        ["IsProductionAddressables"] = true,
                        ["ApiUrl"] = $"http://{hostAddr}:{hostPort}/api/",
                        ["GatewayUrl"] = $"http://{hostAddr}:{gatewayPort}/api/",
                        ["KibanaLogUrl"] = $"http://{hostAddr}:{hostPort}/log",
                        ["ProhibitedWordBlackListUri"] = "https://prod-notice.bluearchiveyostar.com/prod/ProhibitedWord/blacklist.csv",
                        ["ProhibitedWordWhiteListUri"] = "https://prod-notice.bluearchiveyostar.com/prod/ProhibitedWord/whitelist.csv",
                        ["CustomerServiceUrl"] = "https://bluearchive.jp/contact-1-hint",
                        ["OverrideConnectionGroups"] = new JArray
                        {
                            new JObject
                            {
                                ["Name"] = "1.0",
                                ["AddressablesCatalogUrlRoot"] = "https://prod-clientpatch.bluearchiveyostar.com/m28_1_0_1_mashiro3"
                            },
                            new JObject
                            {
                                ["Name"] = "1.73",
                                ["AddressablesCatalogUrlRoot"] = "https://prod-clientpatch.bluearchiveyostar.com/r96_3cpn8ebtdjiqi6y9qtn1"
                            }
                        },
                        ["BundleVersion"] = "s8tloc7lo3",
                        ["IsLivePublished"] = true
                    }
                }
            };

            return Content(jpConfig.ToString(Newtonsoft.Json.Formatting.None), "application/json");
        }

        [HttpGet("prod/index.json")]
        [HttpGet("api/prod/index.json")]
        public ActionResult GetProdNoticeIndex()
        {
            return Content("{}", "application/json");
        }

        [HttpPost("log")]
        [HttpPost("")]
        public async Task<IResult> GetLog()
        {
            using var reader = new StreamReader(Request.Body);

            var payload = JsonNode.Parse(await reader.ReadToEndAsync());
            if (payload?["Message"] is JsonValue msgNode && msgNode.TryGetValue<string>(out var message))
            {
                _logger.LogError("Game Client Error Detected!");
                _logger.LogError("Time: {Time}", payload["Time"]);
                _logger.LogError("Account: ID {AccountId} | {Account}", payload["AccountId"], payload["Account"]);
                _logger.LogError("Error Type: {Type}", payload["Type"]);
                _logger.LogError("Error Message: {ErrorMessage}", message);
                _logger.LogError("Server: {LastServer}", payload["LastServer"]);
                _logger.LogError("LastLoginName: {LastLoginName}", payload["LastLoginName"]);
                _logger.LogError("Revision: {Revision}", payload["Revision"]);
                _logger.LogError("PublisherAccountId: {PublisherAccountId}", payload["PublisherAccountId"]);
            }

            return Results.Ok();
        }
    }
}
