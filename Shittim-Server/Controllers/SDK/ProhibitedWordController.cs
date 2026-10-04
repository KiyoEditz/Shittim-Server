using Microsoft.AspNetCore.Mvc;

namespace Shittim_Server.Controllers.SDK
{
    [ApiController]
    [Route("/com.nexon.bluearchive/server_config")]
    [Route("/prod/ProhibitedWord")]
    [Route("/api/prod/ProhibitedWord")]
    public class ProhibitedWordController : ControllerBase
    {
        [HttpGet("blacklist.csv")]
        public IResult GetBlacklistCsv()
        {
            return CsvWordList("blacklist");
        }

        [HttpGet("blacklist")]
        public IResult GetBlacklist()
        {
            return WordList("blacklist");
        }

        [HttpGet("chattingblacklist.csv")]
        public IResult GetChattingBlacklistCsv()
        {
            return CsvWordList("chattingblacklist");
        }

        [HttpGet("chattingblacklist")]
        public IResult GetChattingBlacklist()
        {
            return WordList("chattingblacklist");
        }

        [HttpGet("whitelist.csv")]
        public IResult GetWhitelistCsv()
        {
            return CsvWordList("whitelist");
        }

        [HttpGet("whitelist")]
        public IResult GetWhitelist()
        {
            return WordList("whitelist");
        }

        // Blue Archive JP (Yostar) expects plain CSV text for .csv routes, while Global/Steam (Nexon) expects pkzip-encrypted files for routes without .csv.
        private static IResult CsvWordList(string name)
        {
            var csvPath = Path.Combine(AppContext.BaseDirectory, "Data", "ProhibitedWord", name + ".csv");
            if (System.IO.File.Exists(csvPath))
            {
                return Results.File(csvPath, "text/csv; charset=utf-8");
            }
            return Results.Text($"__shittim_{name}__\r\n", "text/csv; charset=utf-8");
        }

        // pkzip-encrypted with base64(TableService.CreatePassword(last url segment)), so the route name is what the file has to be built against. an empty body is not an option: Unity's downloadHandler.data is null rather than a zero-length array, and ProhibitedWordDownLoadService feeds it straight into new MemoryStream()
        private static IResult WordList(string name)
        {
            return Results.File(Path.Combine(AppContext.BaseDirectory, "Data", "ProhibitedWord", name + ".zip"), "application/zip");
        }
    }
}
