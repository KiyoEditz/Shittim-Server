using Microsoft.AspNetCore.Mvc;
using BlueArchiveAPI.Configuration;
using Shittim_Server.Services;

namespace Shittim_Server.Controllers
{
    [ApiController]
    [Route("addressables")]
    [Route("r96_3cpn8ebtdjiqi6y9qtn1")]
    [Route("m28_1_0_1_mashiro3")]
    public class AddressablesController : ControllerBase
    {
        private readonly ModCatalogService _catalog;
        private readonly ILogger<AddressablesController> _logger;

        public AddressablesController(ModCatalogService catalog, ILogger<AddressablesController> logger)
        {
            _catalog = catalog;
            _logger = logger;
        }

        [HttpGet("/test.txt")]
        [HttpGet("test.txt")]
        public IActionResult GetTestTxt()
        {
            return Content("ok", "text/plain");
        }

        private static string? GetJpStreamingAssetsPath()
        {
            var metaPath = Config.Instance.ServerConfiguration.ClientMetadataPath;
            if (!string.IsNullOrWhiteSpace(metaPath))
            {
                var idx = metaPath.IndexOf(@"\BlueArchive_Data", StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                {
                    var p = Path.Combine(metaPath[..(idx + @"\BlueArchive_Data".Length)], "StreamingAssets");
                    if (Directory.Exists(p)) return p;
                }
                else if (Directory.Exists(metaPath))
                {
                    var p = Path.Combine(metaPath, "BlueArchive_Data", "StreamingAssets");
                    if (Directory.Exists(p)) return p;
                }
            }

            var envPath = Environment.GetEnvironmentVariable("SHITTIM_JP_GAME_PATH");
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                var p = Path.Combine(envPath, "BlueArchive_Data", "StreamingAssets");
                if (Directory.Exists(p)) return p;
                if (Directory.Exists(envPath)) return envPath;
            }

            var candidates = new[]
            {
                @"D:\YostarGames\BlueArchive_JP\BlueArchive_Data\StreamingAssets",
                @"C:\YostarGames\BlueArchive_JP\BlueArchive_Data\StreamingAssets",
                @"D:\BlueArchive_JP\BlueArchive_Data\StreamingAssets",
                @"C:\BlueArchive_JP\BlueArchive_Data\StreamingAssets"
            };

            foreach (var c in candidates)
            {
                if (Directory.Exists(c)) return c;
            }

            return null;
        }

        // The catalog sits under a path the client derives from the root it was handed and that layout moves between versions, so everything below the root is caught and sorted out by filename instead of being routed.
        [HttpGet("{**path}")]
        public IActionResult Get(string path)
        {
            if (string.IsNullOrEmpty(path))
                return NotFound();

            var file = Path.GetFileName(path);

            if (file.Equals("test.txt", StringComparison.OrdinalIgnoreCase))
                return Content("ok", "text/plain");

            var jpStreaming = GetJpStreamingAssetsPath();

            if (file.StartsWith("catalog_", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Catalog request: {Path}", path);

                if (file.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
                {
                    if (jpStreaming != null)
                    {
                        var jpHashPath = Path.Combine(jpStreaming, "catalog_Remote.hash");
                        if (System.IO.File.Exists(jpHashPath))
                        {
                            var hash = System.IO.File.ReadAllText(jpHashPath).Trim();
                            _logger.LogInformation("Serving JP catalog hash for {File}: {Hash}", file, hash);
                            return Content(hash, "text/plain");
                        }
                    }

                    try
                    {
                        var current = _catalog.Current();
                        return Content(current.Hash, "text/plain");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve mod catalog hash");
                    }
                }
                else if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    if (jpStreaming != null)
                    {
                        var jpJsonPath = Path.Combine(jpStreaming, "catalog_Remote.json");
                        if (System.IO.File.Exists(jpJsonPath))
                        {
                            _logger.LogInformation("Serving JP catalog json from {Path}", jpJsonPath);
                            return PhysicalFile(jpJsonPath, "application/json");
                        }
                    }
                }
                else if (file.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var current = _catalog.Current();
                        return File(current.Bytes, "application/octet-stream");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resolve mod catalog bytes");
                    }
                }
            }

            if (jpStreaming != null)
            {
                if (file.StartsWith("TableCatalog", StringComparison.OrdinalIgnoreCase))
                {
                    var local = Path.Combine(jpStreaming, "TableBundles", "Catalog", file);
                    if (System.IO.File.Exists(local))
                    {
                        if (file.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
                            return Content(System.IO.File.ReadAllText(local).Trim(), "text/plain");
                        return PhysicalFile(local, "application/octet-stream");
                    }
                }

                if (file.StartsWith("MediaCatalog", StringComparison.OrdinalIgnoreCase))
                {
                    var local = Path.Combine(jpStreaming, "MediaPatch", "Catalog", file);
                    if (System.IO.File.Exists(local))
                    {
                        if (file.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
                            return Content(System.IO.File.ReadAllText(local).Trim(), "text/plain");
                        return PhysicalFile(local, "application/octet-stream");
                    }
                }

                if (file.StartsWith("BundlePackingInfo", StringComparison.OrdinalIgnoreCase))
                {
                    var local = Path.Combine(jpStreaming, "AssetBundles", "Catalog", file);
                    if (System.IO.File.Exists(local))
                    {
                        if (file.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
                            return Content(System.IO.File.ReadAllText(local).Trim(), "text/plain");
                        return PhysicalFile(local, "application/octet-stream");
                    }
                }
            }

            if (path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase))
            {
                var mods = Path.GetFullPath(CustomCharacterService.ModsDir);
                var full = Path.GetFullPath(Path.Combine(mods, path[5..].Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(mods, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(full))
                    return NotFound();
                return PhysicalFile(full, "application/octet-stream");
            }

            if (jpStreaming != null)
            {
                var direct = Path.Combine(jpStreaming, path.Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(direct))
                    return PhysicalFile(direct, "application/octet-stream");

                var byName = Path.Combine(jpStreaming, file);
                if (System.IO.File.Exists(byName))
                    return PhysicalFile(byName, "application/octet-stream");
            }

            return Redirect($"{Config.Instance.ServerConfiguration.CdnBaseUrl}/{path}");
        }
    }
}
