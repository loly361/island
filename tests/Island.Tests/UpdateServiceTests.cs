using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Island.Services;

namespace Island.Tests;

/// <summary>Answers requests from a table instead of the network and records what was asked.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public List<HttpRequestMessage> Requests { get; } = new();

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        var response = _respond(request);
        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }

    public static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    public static HttpResponseMessage Ok(string body) => Ok(Encoding.UTF8.GetBytes(body));

    public static HttpResponseMessage Redirect(string location)
    {
        var r = new HttpResponseMessage(HttpStatusCode.Found);
        r.Headers.Location = new Uri(location);
        return r;
    }
}

public class AppVersionTests
{
    [Theory]
    [InlineData("1.10.0", "1.9.0")]
    [InlineData("v1.2.0", "1.1.0")]
    [InlineData("V2.0", "1.99.99")]
    [InlineData("1.2.1", "1.2.0")]
    [InlineData("1.2.0.1", "1.2.0")]
    public void Newer(string a, string b)
    {
        Assert.True(AppVersion.TryParse(a, out var va));
        Assert.True(AppVersion.TryParse(b, out var vb));
        Assert.True(va.IsNewerThan(vb));
        Assert.False(vb.IsNewerThan(va));
    }

    [Theory]
    [InlineData("1.2.0", "v1.2.0")]
    [InlineData("1.2.0-beta.1", "1.2.0")]
    [InlineData("1.2.0+abc123", "1.2")]
    [InlineData("1.2.0-rc1+meta", "1.2.0")]
    public void SameVersion_IsNotNewer(string a, string b)
    {
        Assert.True(AppVersion.TryParse(a, out var va));
        Assert.True(AppVersion.TryParse(b, out var vb));
        Assert.Equal(0, va.CompareTo(vb));
        Assert.False(va.IsNewerThan(vb));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1..2")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3.4.5")]
    [InlineData("-1.2")]
    [InlineData("１.２")]
    public void Garbage_DoesNotParse(string? text) => Assert.False(AppVersion.TryParse(text, out _));

    [Fact]
    public void ToString_IsShort() => Assert.Equal("1.10.0", new AppVersion(1, 10, 0).ToString());

    [Fact]
    public void CurrentVersion_ComesFromProject_WithoutCommitSuffix()
    {
        var csproj = FindProjectFile();
        using var stream = File.OpenRead(csproj);
        var version = System.Xml.Linq.XDocument.Load(stream).Descendants("Version").Select(e => e.Value.Trim())
            .FirstOrDefault(v => v.Length > 0);
        Assert.False(string.IsNullOrEmpty(version), $"<Version> not found in {csproj}");
        Assert.True(AppVersion.TryParse(version, out var expected));
        Assert.Equal(expected, UpdateService.CurrentVersion);
        Assert.DoesNotContain("+", UpdateService.CurrentVersion.ToString());
    }

    /// <summary>src/Island/Island.csproj, found by walking up from the test output folder to the repository root.</summary>
    private static string FindProjectFile()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Island", "Island.csproj");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("src/Island/Island.csproj not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>Never answers on its own: waits until the request is cancelled.</summary>
internal sealed class HangingHandler : HttpMessageHandler
{
    public int Calls;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        await Task.Delay(Timeout.Infinite, ct);
        throw new InvalidOperationException("unreachable");
    }
}

public class UpdateServiceTests
{
    private const string SetupUrl = "https://github.com/loly361/island/releases/download/v1.3.0/Island-Setup.exe";
    private const string ShaUrl = "https://github.com/loly361/island/releases/download/v1.3.0/Island-Setup.exe.sha256";

    /// <summary>Asset links default to the files of <paramref name="tag"/> itself.</summary>
    private static string ReleaseJson(string tag = "v1.3.0", bool prerelease = false, bool draft = false, long setupSize = 1234,
        string? setupUrl = null, string? shaUrl = null)
    {
        setupUrl ??= $"https://github.com/loly361/island/releases/download/{tag}/Island-Setup.exe";
        shaUrl ??= $"https://github.com/loly361/island/releases/download/{tag}/Island-Setup.exe.sha256";
        return ReleaseJsonRaw(tag, prerelease, draft, setupSize, setupUrl, shaUrl);
    }

    private static string ReleaseJsonRaw(string tag, bool prerelease, bool draft, long setupSize,
        string setupUrl, string shaUrl) => $$"""
        {
          "url": "https://api.github.com/repos/loly361/island/releases/1",
          "tag_name": "{{tag}}",
          "name": "Island {{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": [
            { "name": "Island.zip", "size": 999, "browser_download_url": "https://github.com/loly361/island/releases/download/{{tag}}/Island.zip" },
            { "name": "Island-Setup.exe", "size": {{setupSize}}, "browser_download_url": "{{setupUrl}}" },
            { "name": "Island-Setup.exe.sha256", "size": 83, "browser_download_url": "{{shaUrl}}" }
          ]
        }
        """;

    private static ReleaseInfo Release(long size) => new(new AppVersion(1, 3, 0), "v1.3.0",
        new ReleaseAsset(UpdateService.SetupAssetName, size, SetupUrl),
        new ReleaseAsset(UpdateService.ChecksumAssetName, 83, ShaUrl));

    private static string Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    // ---------- parsing ----------

    [Fact]
    public void ParseRelease_ReadsTagAndAssets()
    {
        var r = UpdateService.ParseRelease(ReleaseJson());
        Assert.NotNull(r);
        Assert.Equal(new AppVersion(1, 3, 0), r!.Version);
        Assert.Equal("v1.3.0", r.Tag);
        Assert.Equal(SetupUrl, r.Setup.Url);
        Assert.Equal(1234, r.Setup.Size);
        Assert.Equal(ShaUrl, r.Checksum.Url);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ParseRelease_SkipsPrereleaseAndDraft(bool prerelease, bool draft) =>
        Assert.Null(UpdateService.ParseRelease(ReleaseJson(prerelease: prerelease, draft: draft)));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"message\":\"Not Found\"}")]
    [InlineData("{\"tag_name\":\"v1.3.0\",\"assets\":[]}")]
    public void ParseRelease_RejectsIncompleteAnswers(string json) => Assert.Null(UpdateService.ParseRelease(json));

    [Fact]
    public void ParseRelease_RejectsUntrustedLinksAndSizes()
    {
        Assert.Null(UpdateService.ParseRelease(ReleaseJson(setupUrl: "https://evil.example/Island-Setup.exe")));
        Assert.Null(UpdateService.ParseRelease(ReleaseJson(setupUrl: "http://github.com/loly361/island/releases/download/v1.3.0/Island-Setup.exe")));
        Assert.Null(UpdateService.ParseRelease(ReleaseJson(setupUrl: "https://github.com/someone/else/releases/download/v1/Island-Setup.exe")));
        Assert.Null(UpdateService.ParseRelease(ReleaseJson(setupSize: 0)));
        Assert.Null(UpdateService.ParseRelease(ReleaseJson(setupSize: UpdateService.MaxSetupBytes + 1)));
    }

    [Fact]
    public void SelectAssets_UsesExactNames()
    {
        var assets = new[]
        {
            new ReleaseAsset("island-setup.exe", 10, SetupUrl),
            new ReleaseAsset("Island-Setup.exe.zip", 10, SetupUrl),
            new ReleaseAsset("Island.zip", 10, SetupUrl),
        };
        var (setup, checksum) = UpdateService.SelectAssets("v1.3.0", assets);
        Assert.Null(setup);
        Assert.Null(checksum);

        (setup, checksum) = UpdateService.SelectAssets("v1.3.0", assets.Append(new ReleaseAsset("Island-Setup.exe", 10, SetupUrl))
            .Append(new ReleaseAsset("Island-Setup.exe.sha256", 83, ShaUrl)));
        Assert.Equal("Island-Setup.exe", setup!.Name);
        Assert.Equal("Island-Setup.exe.sha256", checksum!.Name);
    }

    // ---------- exact asset links (no downgrade by swapping the link) ----------

    [Theory]
    [InlineData(SetupUrl, "v1.3.0", "Island-Setup.exe", true)]
    [InlineData(ShaUrl, "v1.3.0", "Island-Setup.exe.sha256", true)]
    [InlineData("https://GitHub.com/Loly361/Island/releases/download/v1.3.0/Island-Setup.exe", "v1.3.0", "Island-Setup.exe", true)]
    // Another release's file (e.g. an old vulnerable one) is refused.
    [InlineData("https://github.com/loly361/island/releases/download/v1.0.0/Island-Setup.exe", "v1.3.0", "Island-Setup.exe", false)]
    [InlineData("https://github.com/loly361/island/releases/download/V1.3.0/Island-Setup.exe", "v1.3.0", "Island-Setup.exe", false)]
    [InlineData("https://github.com/loly361/island/releases/download/v1.3.0/x/../../v1.0.0/Island-Setup.exe", "v1.3.0", "Island-Setup.exe", false)]
    // Another file name, or the name of the other asset.
    [InlineData("https://github.com/loly361/island/releases/download/v1.3.0/Island-Setup-old.exe", "v1.3.0", "Island-Setup.exe", false)]
    [InlineData("https://github.com/loly361/island/releases/download/v1.3.0/island-setup.exe", "v1.3.0", "Island-Setup.exe", false)]
    [InlineData(ShaUrl, "v1.3.0", "Island-Setup.exe", false)]
    [InlineData(SetupUrl, "v1.3.0", "Island-Setup.exe.sha256", false)]
    [InlineData(SetupUrl, "v1.3.0", "Island.zip", false)]
    // Extra path parts, query, other repository, other host.
    [InlineData("https://github.com/loly361/island/releases/download/v1.3.0/Island-Setup.exe/x", "v1.3.0", "Island-Setup.exe", false)]
    [InlineData("https://github.com/loly361/island/releases/download/v1.3.0/Island-Setup.exe?x=1", "v1.3.0", "Island-Setup.exe", false)]
    [InlineData("https://github.com/someone/island/releases/download/v1.3.0/Island-Setup.exe", "v1.3.0", "Island-Setup.exe", false)]
    [InlineData("https://objects.githubusercontent.com/loly361/island/releases/download/v1.3.0/Island-Setup.exe", "v1.3.0", "Island-Setup.exe", false)]
    [InlineData(SetupUrl, "", "Island-Setup.exe", false)]
    public void TrustedAssetUrl_RequiresExactTagAndName(string url, string tag, string name, bool ok) =>
        Assert.Equal(ok, UpdateService.IsTrustedAssetUrl(url, tag, name));

    [Fact]
    public void ParseRelease_RejectsAssetsOfAnotherRelease()
    {
        // The API says v1.3.0, but the links lead to the v1.0.0 files: that would be a downgrade.
        Assert.Null(UpdateService.ParseRelease(ReleaseJson(
            setupUrl: "https://github.com/loly361/island/releases/download/v1.0.0/Island-Setup.exe")));
        Assert.Null(UpdateService.ParseRelease(ReleaseJson(
            shaUrl: "https://github.com/loly361/island/releases/download/v1.0.0/Island-Setup.exe.sha256")));
    }

    [Fact]
    public async Task Download_RefusesReleaseWhoseLinksPointElsewhere()
    {
        var handler = new FakeHandler(_ => FakeHandler.Ok("x"));
        using var service = new UpdateService(handler);
        var release = new ReleaseInfo(new AppVersion(1, 3, 0), "v1.3.0",
            new ReleaseAsset(UpdateService.SetupAssetName, 100, "https://github.com/loly361/island/releases/download/v1.0.0/Island-Setup.exe"),
            new ReleaseAsset(UpdateService.ChecksumAssetName, 83, ShaUrl));

        await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(release));
        Assert.Empty(handler.Requests);
    }

    // ---------- hosts ----------

    [Theory]
    [InlineData("https://github.com/x", true)]
    [InlineData("https://objects.githubusercontent.com/x", true)]
    [InlineData("https://release-assets.githubusercontent.com/x", true)]
    [InlineData("https://GitHub.com/x", true)]
    [InlineData("http://github.com/x", false)]
    [InlineData("ftp://github.com/x", false)]
    [InlineData("https://github.com:8443/x", false)]
    [InlineData("https://user:pw@github.com/x", false)]
    [InlineData("https://evil.com/x", false)]
    [InlineData("https://github.com.evil.com/x", false)]
    [InlineData("https://evil.objects.githubusercontent.com/x", false)]
    [InlineData("https://raw.githubusercontent.com/x", false)]
    [InlineData("https://api.github.com/x", false)]
    public void DownloadHosts(string url, bool allowed) =>
        Assert.Equal(allowed, UpdateService.IsAllowedDownloadUri(new Uri(url)));

    [Fact]
    public void ApiHost_IsOnlyApiGithubOverHttps()
    {
        Assert.True(UpdateService.IsAllowedApiUri(UpdateService.LatestReleaseUri));
        Assert.False(UpdateService.IsAllowedApiUri(new Uri("http://api.github.com/repos/loly361/island/releases/latest")));
        Assert.False(UpdateService.IsAllowedApiUri(new Uri("https://github.com/repos/loly361/island/releases/latest")));
    }

    [Fact]
    public void LatestReleaseUri_PointsAtTheRepository() =>
        Assert.Equal("https://api.github.com/repos/loly361/island/releases/latest", UpdateService.LatestReleaseUri.ToString());

    // ---------- check ----------

    [Fact]
    public async Task Check_FindsNewerRelease_AndSendsOnlyPublicHeaders()
    {
        var handler = new FakeHandler(_ => FakeHandler.Ok(ReleaseJson()));
        using var service = new UpdateService(handler);

        var result = await service.CheckAsync(new AppVersion(1, 2, 0));

        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Equal(new AppVersion(1, 3, 0), result.Release!.Version);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(UpdateService.LatestReleaseUri, request.RequestUri);
        Assert.Contains("Island-Updater", request.Headers.UserAgent.ToString());
        Assert.Contains("application/vnd.github+json", request.Headers.Accept.ToString());
        Assert.Null(request.Headers.Authorization);
    }

    [Theory]
    [InlineData("v1.2.0")]
    [InlineData("v1.1.0")]
    public async Task Check_SameOrOlder_IsUpToDate(string tag)
    {
        using var service = new UpdateService(new FakeHandler(_ => FakeHandler.Ok(ReleaseJson(tag))));
        Assert.Equal(UpdateCheckStatus.UpToDate, (await service.CheckAsync(new AppVersion(1, 2, 0))).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Check_HttpError_MeansNoUpdate(HttpStatusCode code)
    {
        using var service = new UpdateService(new FakeHandler(_ => new HttpResponseMessage(code) { Content = new StringContent("{\"message\":\"Not Found\"}") }));
        var result = await service.CheckAsync(new AppVersion(1, 2, 0));
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task Check_NetworkError_MeansNoUpdate()
    {
        using var service = new UpdateService(new FakeHandler(_ => throw new HttpRequestException("offline")));
        var result = await service.CheckAsync(new AppVersion(1, 2, 0));
        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Null(result.Release);
    }

    [Fact]
    public async Task Check_OversizedAnswer_MeansNoUpdate()
    {
        var huge = new string(' ', UpdateService.MaxApiBytes + 10) + ReleaseJson();
        using var service = new UpdateService(new FakeHandler(_ => FakeHandler.Ok(huge)));
        Assert.Equal(UpdateCheckStatus.Failed, (await service.CheckAsync(new AppVersion(1, 2, 0))).Status);
    }

    [Fact]
    public async Task Check_RedirectToForeignHost_IsRefused()
    {
        var handler = new FakeHandler(r => r.RequestUri!.Host == "api.github.com"
            ? FakeHandler.Redirect("https://evil.example/latest")
            : FakeHandler.Ok(ReleaseJson()));
        using var service = new UpdateService(handler);
        Assert.Equal(UpdateCheckStatus.Failed, (await service.CheckAsync(new AppVersion(1, 2, 0))).Status);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Check_RedirectLoop_StopsAtLimit_WithoutException()
    {
        var handler = new FakeHandler(_ => FakeHandler.Redirect(UpdateService.LatestReleaseUri.ToString()));
        using var service = new UpdateService(handler);

        var result = await service.CheckAsync(new AppVersion(1, 2, 0));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Null(result.Release);
        // The first request plus at most a handful of hops, not an endless loop.
        Assert.InRange(handler.Requests.Count, 2, 10);
    }

    [Fact]
    public async Task Check_CancelledWhileWaiting_ReturnsFailed()
    {
        var handler = new HangingHandler();
        using var service = new UpdateService(handler);
        using var cts = new CancellationTokenSource();

        var check = service.CheckAsync(new AppVersion(1, 2, 0), cts.Token);
        cts.CancelAfter(50);
        var result = await check.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Check_AlreadyCancelled_ReturnsFailed_WithoutException()
    {
        var handler = new FakeHandler(_ => FakeHandler.Ok(ReleaseJson()));
        using var service = new UpdateService(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.CheckAsync(new AppVersion(1, 2, 0), cts.Token);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public async Task Download_Cancelled_ThrowsCancellation_AndLeavesNoFolder()
    {
        using var service = new UpdateService(new HangingHandler());
        using var cts = new CancellationTokenSource(50);
        var before = TempUpdateFolders();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(Release(100), ct: cts.Token));
        Assert.Empty(TempUpdateFolders().Except(before));
    }

    // ---------- checksum ----------

    [Theory]
    [InlineData("  Island-Setup.exe", true)]
    [InlineData(" *Island-Setup.exe", true)]
    [InlineData("", true)]
    [InlineData("  Other.exe", false)]
    public void ParseChecksum_Format(string suffix, bool ok)
    {
        var hash = new string('A', 64);
        var parsed = UpdateService.ParseChecksum("﻿" + hash + suffix + "\r\n");
        Assert.Equal(ok ? new string('a', 64) : null, parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc  Island-Setup.exe")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz  Island-Setup.exe")]
    public void ParseChecksum_RejectsGarbage(string text) => Assert.Null(UpdateService.ParseChecksum(text));

    [Fact]
    public void VerifyFile_MatchAndMismatch()
    {
        var path = Path.Combine(Path.GetTempPath(), "island-test-" + Guid.NewGuid().ToString("N") + ".bin");
        var data = Encoding.UTF8.GetBytes("installer bytes");
        File.WriteAllBytes(path, data);
        try
        {
            Assert.True(UpdateService.VerifyFile(path, Hex(data)));
            Assert.True(UpdateService.VerifyFile(path, Hex(data).ToUpperInvariant()));
            Assert.False(UpdateService.VerifyFile(path, Hex(Encoding.UTF8.GetBytes("other"))));
            Assert.False(UpdateService.VerifyFile(path, "short"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---------- download ----------

    private static FakeHandler DownloadHandler(byte[] setup, string shaLine) => new(r =>
    {
        var url = r.RequestUri!.ToString();
        if (url == ShaUrl) return FakeHandler.Redirect("https://objects.githubusercontent.com/sha");
        if (url == SetupUrl) return FakeHandler.Redirect("https://release-assets.githubusercontent.com/setup");
        if (url.EndsWith("/sha", StringComparison.Ordinal)) return FakeHandler.Ok(shaLine);
        if (url.EndsWith("/setup", StringComparison.Ordinal)) return FakeHandler.Ok(setup);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    });

    [Fact]
    public async Task Download_VerifiesHash_InRandomTempFolder()
    {
        var data = RandomNumberGenerator.GetBytes(300_000);
        var handler = DownloadHandler(data, Hex(data) + "  Island-Setup.exe\n");
        using var service = new UpdateService(handler);

        var installer = await service.DownloadAsync(Release(data.Length));
        try
        {
            Assert.True(File.Exists(installer.Path));
            Assert.Equal("Island-Setup.exe", Path.GetFileName(installer.Path));
            var folder = Path.GetDirectoryName(installer.Path)!;
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(UpdateService.TempFolderPrefix, Path.GetFileName(folder));
            Assert.Equal(Hex(data), installer.Sha256);
            Assert.True(UpdateService.VerifyFile(installer.Path, installer.Sha256));
            // The checksum is fetched before the installer.
            Assert.Equal(ShaUrl, handler.Requests[0].RequestUri!.ToString());
        }
        finally
        {
            UpdateService.TryDeleteFolder(Path.GetDirectoryName(installer.Path)!);
        }
    }

    [Fact]
    public async Task Download_HashMismatch_DeletesFolder()
    {
        var data = RandomNumberGenerator.GetBytes(5000);
        using var service = new UpdateService(DownloadHandler(data, Hex(new byte[] { 1, 2, 3 }) + "  Island-Setup.exe"));
        var before = TempUpdateFolders();

        await Assert.ThrowsAsync<UpdateIntegrityException>(() => service.DownloadAsync(Release(data.Length)));

        Assert.Empty(TempUpdateFolders().Except(before));
    }

    [Fact]
    public async Task Download_SizeDifferentFromApi_Fails()
    {
        var data = RandomNumberGenerator.GetBytes(5000);
        using var service = new UpdateService(DownloadHandler(data, Hex(data)));
        var before = TempUpdateFolders();

        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(Release(4000)));
        Assert.IsNotType<UpdateIntegrityException>(ex);
        Assert.Empty(TempUpdateFolders().Except(before));
    }

    [Fact]
    public async Task Download_RedirectToForeignOrHttpHost_IsRefused()
    {
        foreach (var target in new[] { "https://evil.example/setup", "http://objects.githubusercontent.com/setup" })
        {
            var data = RandomNumberGenerator.GetBytes(100);
            var handler = new FakeHandler(r => r.RequestUri!.ToString() switch
            {
                ShaUrl => FakeHandler.Ok(Hex(data)),
                SetupUrl => FakeHandler.Redirect(target),
                _ => FakeHandler.Ok(data),
            });
            using var service = new UpdateService(handler);

            await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(Release(data.Length)));
            Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.ToString() == target);
        }
    }

    [Fact]
    public async Task Download_Http404_Fails()
    {
        using var service = new UpdateService(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        await Assert.ThrowsAsync<UpdateException>(() => service.DownloadAsync(Release(100)));
    }

    [Fact]
    public void InstallerStartInfo_IsSilentAndWithoutShell()
    {
        var path = Path.Combine(Path.GetTempPath(), "Island-update-x", "Island-Setup.exe");
        var info = UpdateService.BuildInstallerStartInfo(path);
        Assert.False(info.UseShellExecute);
        Assert.Equal(path, info.FileName);
        Assert.Equal(new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" }, info.ArgumentList);
    }

    [Fact]
    public void TryLaunchInstaller_RefusesTamperedFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "island-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Island-Setup.exe");
        File.WriteAllText(path, "not the installer");

        Assert.False(UpdateService.TryLaunchInstaller(new DownloadedInstaller(path, new string('0', 64))));
        Assert.False(Directory.Exists(folder));
    }

    private static HashSet<string> TempUpdateFolders() =>
        Directory.EnumerateDirectories(Path.GetTempPath(), UpdateService.TempFolderPrefix + "*").ToHashSet();

    // ---------- settings ----------

    [Fact]
    public void Settings_UpdateDefaultsAndCopyFrom()
    {
        var fresh = new Settings();
        Assert.True(fresh.AutoUpdateCheck);
        Assert.Null(fresh.DismissedUpdateVersion);

        fresh.CopyFrom(new Settings { AutoUpdateCheck = false, DismissedUpdateVersion = "1.3.0" });
        Assert.False(fresh.AutoUpdateCheck);
        Assert.Equal("1.3.0", fresh.DismissedUpdateVersion);

        fresh.CopyFrom(new Settings());
        Assert.True(fresh.AutoUpdateCheck);
        Assert.Null(fresh.DismissedUpdateVersion);
    }

    [Fact]
    public void Settings_OldFileWithoutUpdateFields_KeepsChecking()
    {
        var path = Path.Combine(Path.GetTempPath(), "island-test-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{\"City\":\"Genève\",\"Language\":\"fr\"}");
        try
        {
            var s = JsonStore.Load(path, () => new Settings());
            Assert.True(s.AutoUpdateCheck);
            Assert.Null(s.DismissedUpdateVersion);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
