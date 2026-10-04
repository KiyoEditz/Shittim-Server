using System;
using System.IO;
using System.Linq;
using BlueArchiveAPI.Configuration;
using Shittim_Server.Services;
using Xunit;

namespace Shittim_Server.Tests;

public class YostarGameLocatorTests
{
    [Fact]
    public void IsValidInstall_RejectsNonexistentOrEmpty()
    {
        Assert.False(YostarGameLocator.IsValidInstall(null));
        Assert.False(YostarGameLocator.IsValidInstall(""));
        Assert.False(YostarGameLocator.IsValidInstall(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void IsValidInstall_ValidatesDirectoryWithMetadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ba-jp-test-{Guid.NewGuid():N}");
        var metaDir = Path.Combine(tempDir, "BlueArchive_Data", "il2cpp_data", "Metadata");
        Directory.CreateDirectory(metaDir);
        var metaFile = Path.Combine(metaDir, "global-metadata.dat");
        File.WriteAllBytes(metaFile, [0xAF, 0x1B, 0xB1, 0xFA]);

        try
        {
            Assert.True(YostarGameLocator.IsValidInstall(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFixedDriveCandidates_YieldsStandardLocations()
    {
        var candidates = YostarGameLocator.EnumerateFixedDriveCandidates().ToList();
        Assert.NotEmpty(candidates);
        Assert.Contains(candidates, c => c.EndsWith("BlueArchive_JP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Locator_FindsExistingJpClient_IfInstalled()
    {
        var defaultJp = @"D:\YostarGames\BlueArchive_JP";
        if (Directory.Exists(defaultJp) && File.Exists(Path.Combine(defaultJp, "BlueArchive_Data", "il2cpp_data", "Metadata", "global-metadata.dat")))
        {
            var root = YostarGameLocator.ResolveInstallRoot();
            Assert.False(string.IsNullOrEmpty(root));
            Assert.True(File.Exists(Path.Combine(root, "BlueArchive_Data", "il2cpp_data", "Metadata", "global-metadata.dat")));

            var meta = YostarGameLocator.FindGameFile(Path.Combine("BlueArchive_Data", "il2cpp_data", "Metadata", "global-metadata.dat"));
            Assert.NotNull(meta);
            Assert.True(File.Exists(meta));
        }
    }

    [Fact]
    public void ClientMetadataPatchService_PrefersJp_WhenEnabled()
    {
        var defaultJpMeta = @"D:\YostarGames\BlueArchive_JP\BlueArchive_Data\il2cpp_data\Metadata\global-metadata.dat";
        if (File.Exists(defaultJpMeta))
        {
            var originalSetting = Config.Instance.ServerConfiguration.EnableJpClient;
            try
            {
                Config.Instance.ServerConfiguration.EnableJpClient = true;
                var resolved = ClientMetadataPatchService.GetMetadataPath();
                Assert.Equal(Path.GetFullPath(defaultJpMeta), Path.GetFullPath(resolved));
            }
            finally
            {
                Config.Instance.ServerConfiguration.EnableJpClient = originalSetting;
            }
        }
    }
}
