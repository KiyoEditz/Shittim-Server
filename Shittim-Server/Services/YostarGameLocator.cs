using System.Diagnostics;
using System.Text.RegularExpressions;
using BlueArchiveAPI.Configuration;

namespace Shittim_Server.Services
{
    // Locates the standalone Yostar install of Blue Archive JP client wherever it lives.
    // Unlike the Steam build which follows libraryfolders.vdf, the JP client uses an Electron-based standalone launcher
    // whose local storage (LevelDB in %APPDATA%\BlueArchive_JP_Gamelauncher) stores the exact installation path.
    // If not found in launcher metadata, it probes conventional install directories across all fixed drives.
    public static class YostarGameLocator
    {
        private const string MetadataRelativePath = @"BlueArchive_Data\il2cpp_data\Metadata\global-metadata.dat";

        // Resolved once per process; the install location does not move while the server runs.
        private static readonly Lazy<string> InstallRootLazy = new(ResolveInstallRoot);

        // Absolute path to the Blue Archive JP install directory, or "" if not found.
        public static string InstallRoot => InstallRootLazy.Value;

        // <InstallRoot>/<relative> if the install was found and the file exists,
        // otherwise null. Use for files that should already be on disk.
        public static string? FindGameFile(string relative)
        {
            var full = CombineGamePath(relative);
            return full != null && File.Exists(full) ? full : null;
        }

        // <InstallRoot>/<relative> if the install was found (no existence check),
        // otherwise null. Use for files/dirs that may be created later.
        public static string? CombineGamePath(string relative)
        {
            var root = ConfiguredInstallRoot();
            if (string.IsNullOrWhiteSpace(root))
                root = InstallRoot;

            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, relative);
        }

        private static string ConfiguredInstallRoot()
        {
            var fromEnv = Environment.GetEnvironmentVariable("SHITTIM_CLIENT_INSTALL_DIR_JP");
            if (!string.IsNullOrWhiteSpace(fromEnv) && IsValidInstall(fromEnv))
                return fromEnv;

            var configured = Config.Instance.ServerConfiguration.ClientInstallDirectory;
            if (!string.IsNullOrWhiteSpace(configured) && IsValidInstall(configured))
                return configured;

            return "";
        }

        internal static string ResolveInstallRoot()
        {
            try
            {
                // 1. Explicit env or config
                var configured = ConfiguredInstallRoot();
                if (!string.IsNullOrWhiteSpace(configured))
                    return configured;

                // 2. Discover from Yostar Games Launcher LevelDB storage
                foreach (var candidate in EnumerateFromLauncherStorage())
                {
                    if (IsValidInstall(candidate))
                        return candidate;
                }

                // 3. Sibling of launcher install directory
                foreach (var candidate in EnumerateFromLauncherSiblings())
                {
                    if (IsValidInstall(candidate))
                        return candidate;
                }

                // 4. Conventional install paths across all fixed drives
                foreach (var candidate in EnumerateFixedDriveCandidates())
                {
                    if (IsValidInstall(candidate))
                        return candidate;
                }
            }
            catch
            {
                // Discovery is best-effort
            }

            return "";
        }

        public static bool IsValidInstall(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return false;

            var metadataPath = Path.Combine(directory, MetadataRelativePath);
            return File.Exists(metadataPath);
        }

        internal static IEnumerable<string> EnumerateFromLauncherStorage()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
                yield break;

            var levelDbDir = Path.Combine(appData, "BlueArchive_JP_Gamelauncher", "Local Storage", "leveldb");
            if (!Directory.Exists(levelDbDir))
                yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] files;
            try
            {
                files = Directory.GetFiles(levelDbDir, "*.*")
                    .Where(f => f.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".ldb", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch
            {
                yield break;
            }

            var pathRegex = new Regex(@"([A-Za-z]:\\[^""'\r\n\t]+(?:BlueArchive_JP|BlueArchive))", RegexOptions.Compiled);

            foreach (var file in files)
            {
                string text;
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream, System.Text.Encoding.Latin1);
                    text = reader.ReadToEnd();
                }
                catch
                {
                    continue;
                }

                var matches = pathRegex.Matches(text);
                foreach (Match match in matches)
                {
                    var raw = match.Groups[1].Value.Replace(@"\\", @"\").Trim();
                    if (seen.Add(raw))
                        yield return raw;
                }
            }
        }

        internal static IEnumerable<string> EnumerateFromLauncherSiblings()
        {
            var candidates = new List<string>();

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                candidates.Add(Path.Combine(localAppData, "Programs", "BlueArchive_JP_Gamelauncher"));
            }

            foreach (var drive in FixedDriveRoots())
            {
                candidates.Add(Path.Combine(drive, "YostarGames", "BlueArchive_JP_Gamelauncher"));
                candidates.Add(Path.Combine(drive, "Program Files", "YostarGames", "BlueArchive_JP_Gamelauncher"));
                candidates.Add(Path.Combine(drive, "Program Files (x86)", "YostarGames", "BlueArchive_JP_Gamelauncher"));
            }

            foreach (var launcherDir in candidates)
            {
                if (Directory.Exists(launcherDir))
                {
                    var parent = Path.GetDirectoryName(launcherDir);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        yield return Path.Combine(parent, "BlueArchive_JP");
                    }
                }
            }
        }

        internal static IEnumerable<string> EnumerateFixedDriveCandidates()
        {
            foreach (var drive in FixedDriveRoots())
            {
                yield return Path.Combine(drive, "YostarGames", "BlueArchive_JP");
                yield return Path.Combine(drive, "Program Files", "YostarGames", "BlueArchive_JP");
                yield return Path.Combine(drive, "Program Files (x86)", "YostarGames", "BlueArchive_JP");
                yield return Path.Combine(drive, "Games", "BlueArchive_JP");
                yield return Path.Combine(drive, "BlueArchive_JP");
            }
        }

        private static IEnumerable<string> FixedDriveRoots()
        {
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch
            {
                yield break;
            }

            foreach (var drive in drives)
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                    yield return drive.RootDirectory.FullName;
            }
        }
    }
}
