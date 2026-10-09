using DanBackup.Core.Update;

namespace DanBackup.Tests;

public class UpdateTests
{
    private const string Release = """
        { "tag_name": "v0.3.5", "draft": false, "prerelease": false, "html_url": "https://github.com/x/y/releases/tag/v0.3.5",
          "assets": [
            { "name": "DanBackup.exe", "browser_download_url": "https://example/DanBackup.exe" },
            { "name": "DanBackup.exe.sha256", "browser_download_url": "https://example/DanBackup.exe.sha256" } ] }
        """;

    [Theory]
    [InlineData("v0.3.5", 0, 3, 5)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3-beta+abc", 1, 2, 3)]
    public void ParseTag_reads_versions(string tag, int major, int minor, int build) =>
        Assert.Equal(new Version(major, minor, build), UpdateService.ParseTag(tag));

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseTag_rejects_non_versions(string? tag) => Assert.Null(UpdateService.ParseTag(tag));

    [Fact]
    public void Newer_release_is_offered_with_assets()
    {
        var info = UpdateService.ParseRelease(Release, new Version(0, 3, 4));
        Assert.NotNull(info);
        Assert.Equal(new Version(0, 3, 5), info.Version);
        Assert.Equal("https://example/DanBackup.exe", info.DownloadUrl);
        Assert.Equal("https://example/DanBackup.exe.sha256", info.ChecksumUrl);
    }

    [Theory]
    [InlineData(0, 3, 5)]
    [InlineData(0, 4, 0)]
    public void Same_or_older_release_is_ignored(int major, int minor, int build) =>
        Assert.Null(UpdateService.ParseRelease(Release, new Version(major, minor, build)));

    [Fact]
    public void Release_without_exe_is_ignored() =>
        Assert.Null(UpdateService.ParseRelease("""{ "tag_name": "v9.0.0", "assets": [] }""", new Version(0, 1, 0)));

    [Fact]
    public void Checksum_accepts_sha256sum_format() =>
        Assert.Equal("ABC123", UpdateService.ParseChecksum("ABC123  DanBackup.exe\n"));
}
