using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Island.Services;

/// <summary>A release version: major.minor.patch[.build]. Pre-release suffixes ("-beta") and build metadata ("+abc") are ignored.</summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch, int Build = 0) : IComparable<AppVersion>
{
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        var cut = s.IndexOfAny(new[] { '+', '-' });
        if (cut >= 0) s = s[..cut];

        var parts = s.Split('.');
        if (parts.Length is < 1 or > 4) return false;

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)) return false;
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }

        version = new AppVersion(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c == 0) c = Build.CompareTo(other.Build);
        return c;
    }

    public bool IsNewerThan(AppVersion other) => CompareTo(other) > 0;

    public override string ToString() => Build == 0 ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}.{Build}";
}

/// <summary>A downloadable file of a release, as reported by the GitHub API.</summary>
public sealed record ReleaseAsset(string Name, long Size, string Url);

/// <summary>The latest stable release that carries both the installer and its checksum.</summary>
public sealed record ReleaseInfo(AppVersion Version, string Tag, ReleaseAsset Setup, ReleaseAsset Checksum);

/// <summary>A verified installer on disk and the SHA-256 it was checked against.</summary>
public sealed record DownloadedInstaller(string Path, string Sha256);

public enum UpdateCheckStatus
{
    UpToDate,
    Available,
    /// <summary>Network error, 404 (private repository), 403 (rate limit), bad JSON and so on: treated as "no update".</summary>
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, ReleaseInfo? Release = null)
{
    public static UpdateCheckResult UpToDate { get; } = new(UpdateCheckStatus.UpToDate);
    public static UpdateCheckResult Failed { get; } = new(UpdateCheckStatus.Failed);
}

/// <summary>The download failed for a reason other than a bad checksum.</summary>
public class UpdateException : Exception
{
    public UpdateException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The installer did not match its published SHA-256; it has been deleted.</summary>
public sealed class UpdateIntegrityException : UpdateException
{
    public UpdateIntegrityException(string message) : base(message) { }
}

/// <summary>
/// Checks GitHub for a newer release of Island, downloads <c>Island-Setup.exe</c> into a fresh random folder in %TEMP%,
/// verifies it against <c>Island-Setup.exe.sha256</c> and starts it silently. No credentials are ever sent: when the
/// repository is private the API answers 404 and the check simply reports <see cref="UpdateCheckStatus.Failed"/>.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string Owner = "loly361";
    public const string Repository = "island";
    public const string SetupAssetName = "Island-Setup.exe";
    public const string ChecksumAssetName = "Island-Setup.exe.sha256";
    public const string UserAgent = "Island-Updater";
    public const long MaxSetupBytes = 200L * 1024 * 1024;
    public const int MaxApiBytes = 1024 * 1024;
    public const int MaxChecksumBytes = 4 * 1024;
    public const string TempFolderPrefix = "Island-update-";

    public static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");

    /// <summary>Hosts a download may touch: the release link itself and GitHub's asset storage it redirects to.</summary>
    public static IReadOnlyCollection<string> AllowedDownloadHosts { get; } = new[]
    {
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
    };

    public static readonly string[] InstallerArguments = { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" };

    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);
    private const int MaxRedirects = 5;

    private readonly HttpClient _http;

    /// <param name="handler">Network layer; tests pass a fake. The default one never follows redirects on its own,
    /// so every hop is checked against the host list here.</param>
    public UpdateService(HttpMessageHandler? handler = null)
    {
        handler ??= new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>The running app's version from AssemblyInformationalVersion, without the "+commit" suffix.</summary>
    public static AppVersion CurrentVersion { get; } = ReadCurrentVersion();

    private static AppVersion ReadCurrentVersion()
    {
        var assembly = typeof(UpdateService).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (AppVersion.TryParse(info, out var v)) return v;
        var name = assembly.GetName().Version;
        return name is null ? default : new AppVersion(name.Major, name.Minor, Math.Max(0, name.Build));
    }

    // ---------- check ----------

    /// <summary>Never throws: any problem (including cancellation) is reported as <see cref="UpdateCheckStatus.Failed"/>.</summary>
    public Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default) => CheckAsync(CurrentVersion, ct);

    public async Task<UpdateCheckResult> CheckAsync(AppVersion current, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ApiTimeout);

            using var response = await SendAsync(LatestReleaseUri, IsAllowedApiUri, "application/vnd.github+json", timeout.Token)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return UpdateCheckResult.Failed;

            var bytes = await ReadLimitedAsync(response, MaxApiBytes, timeout.Token).ConfigureAwait(false);
            var release = ParseRelease(Encoding.UTF8.GetString(bytes));
            if (release is null) return UpdateCheckResult.Failed;

            return release.Version.IsNewerThan(current)
                ? new UpdateCheckResult(UpdateCheckStatus.Available, release)
                : UpdateCheckResult.UpToDate;
        }
        catch
        {
            return UpdateCheckResult.Failed;
        }
    }

    /// <summary>
    /// Reads the "latest release" JSON. Returns null for drafts, pre-releases, unparsable tags and releases without
    /// both <see cref="SetupAssetName"/> and <see cref="ChecksumAssetName"/> on a trusted GitHub link.
    /// </summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;

            var tag = Str(root, "tag_name");
            if (tag is null || !AppVersion.TryParse(tag, out var version)) return null;

            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in list.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object) continue;
                    var name = Str(a, "name");
                    var url = Str(a, "browser_download_url");
                    var size = a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n) ? n : -1;
                    if (name is not null && url is not null) assets.Add(new ReleaseAsset(name, size, url));
                }
            }

            var (setup, checksum) = SelectAssets(tag, assets);
            return setup is null || checksum is null ? null : new ReleaseInfo(version, tag, setup, checksum);
        }
        catch (JsonException)
        {
            return null;
        }

        static bool Bool(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>Picks the installer and its checksum by exact name; each must have a sane size and a link to exactly
    /// that file of release <paramref name="tag"/>.</summary>
    public static (ReleaseAsset? Setup, ReleaseAsset? Checksum) SelectAssets(string tag, IEnumerable<ReleaseAsset> assets)
    {
        ReleaseAsset? setup = null, checksum = null;
        foreach (var a in assets)
        {
            if (!IsTrustedAssetUrl(a.Url, tag, a.Name)) continue;
            if (a.Name == SetupAssetName && a.Size is > 0 and <= MaxSetupBytes) setup ??= a;
            else if (a.Name == ChecksumAssetName && a.Size is > 0 and <= MaxChecksumBytes) checksum ??= a;
        }

        return (setup, checksum);
    }

    // ---------- host checks ----------

    private static bool IsPlainHttps(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo);

    public static bool IsAllowedApiUri(Uri uri) =>
        IsPlainHttps(uri) && string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>HTTPS on port 443 to one of <see cref="AllowedDownloadHosts"/> (exact host, no subdomains).</summary>
    public static bool IsAllowedDownloadUri(Uri uri) =>
        IsPlainHttps(uri) && AllowedDownloadHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A release asset link must be exactly https://github.com/{Owner}/{Repository}/releases/download/{tag}/{assetName}
    /// on github.com itself, with the tag of the parsed release: a link to another (older) release is refused, so the
    /// update cannot be turned into a downgrade by swapping the link.
    /// </summary>
    public static bool IsTrustedAssetUrl(string? url, string tag, string assetName)
    {
        if (string.IsNullOrEmpty(tag) || assetName is not (SetupAssetName or ChecksumAssetName)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !IsAllowedDownloadUri(uri)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return false;

        var prefix = $"/{Owner}/{Repository}/releases/download/";
        var path = uri.AbsolutePath;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && string.Equals(path[prefix.Length..], Uri.EscapeDataString(tag) + "/" + assetName, StringComparison.Ordinal);
    }

    // ---------- download ----------

    /// <summary>
    /// Downloads the checksum, then the installer into a new random folder in %TEMP%, and verifies SHA-256.
    /// Returns the installer path and hash. On any failure the folder is deleted and an <see cref="UpdateException"/>
    /// (or <see cref="UpdateIntegrityException"/> for a hash mismatch) is thrown.
    /// </summary>
    public async Task<DownloadedInstaller> DownloadAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!IsTrustedAssetUrl(release.Setup.Url, release.Tag, SetupAssetName)
            || !IsTrustedAssetUrl(release.Checksum.Url, release.Tag, ChecksumAssetName))
            throw new UpdateException("untrusted asset link");
        if (release.Setup.Size is <= 0 or > MaxSetupBytes) throw new UpdateException("bad installer size");

        string expected;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ApiTimeout);
            using var response = await SendAsync(new Uri(release.Checksum.Url), IsAllowedDownloadUri, "application/octet-stream", timeout.Token)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) throw new UpdateException($"checksum: HTTP {(int)response.StatusCode}");
            var bytes = await ReadLimitedAsync(response, MaxChecksumBytes, timeout.Token).ConfigureAwait(false);
            expected = ParseChecksum(Encoding.UTF8.GetString(bytes)) ?? throw new UpdateException("bad checksum file");
        }
        catch (UpdateException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new UpdateException("checksum download failed", ex); }

        var folder = CreateRandomTempFolder();
        var path = Path.Combine(folder, SetupAssetName);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(DownloadTimeout);
            using var response = await SendAsync(new Uri(release.Setup.Url), IsAllowedDownloadUri, "application/octet-stream", timeout.Token)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) throw new UpdateException($"installer: HTTP {(int)response.StatusCode}");

            var declared = release.Setup.Size;
            if (response.Content.Headers.ContentLength is { } length && length != declared)
                throw new UpdateException("installer size differs from the release");

            string actual;
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                actual = await CopyHashedAsync(response, file, declared, progress, timeout.Token).ConfigureAwait(false);
            }

            if (!HashEquals(actual, expected)) throw new UpdateIntegrityException("SHA-256 mismatch");
            return new DownloadedInstaller(path, expected);
        }
        catch (Exception ex)
        {
            TryDeleteFolder(folder);
            if (ex is UpdateException) throw;
            if (ex is OperationCanceledException && ct.IsCancellationRequested) throw;
            throw new UpdateException("installer download failed", ex);
        }
    }

    /// <summary>Accepts "&lt;64 hex&gt;  Island-Setup.exe" (sha256sum format, optional '*'), or the bare hash. Returns lower-case hex.</summary>
    public static string? ParseChecksum(string text)
    {
        var line = text.TrimStart('﻿').Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line is null) return null;
        var m = Regex.Match(line, @"^([0-9a-fA-F]{64})(?:\s+\*?(\S+))?$");
        if (!m.Success) return null;
        if (m.Groups[2].Success && m.Groups[2].Value != SetupAssetName) return null;
        return m.Groups[1].Value.ToLowerInvariant();
    }

    public static bool HashEquals(string actualHex, string expectedHex)
    {
        if (actualHex.Length != 64 || expectedHex.Length != 64) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actualHex.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(expectedHex.ToLowerInvariant()));
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>True when the file exists and its SHA-256 equals the expected hex.</summary>
    public static bool VerifyFile(string path, string expectedHex)
    {
        try
        {
            return File.Exists(path) && HashEquals(ComputeSha256(path), expectedHex);
        }
        catch
        {
            return false;
        }
    }

    private static string CreateRandomTempFolder()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var dir = Path.Combine(Path.GetTempPath(), TempFolderPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
            if (Directory.Exists(dir) || File.Exists(dir)) continue;
            Directory.CreateDirectory(dir);
            return dir;
        }

        throw new UpdateException("cannot create a temporary folder");
    }

    public static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch
        {
            // Best effort; %TEMP% is cleaned up later.
        }
    }

    /// <summary>Removes update folders left by earlier runs (the installer cannot delete the folder it runs from).</summary>
    public static void CleanupOldDownloads(TimeSpan olderThan)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(Path.GetTempPath(), TempFolderPrefix + "*"))
            {
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) > olderThan) TryDeleteFolder(dir);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    // ---------- launch ----------

    /// <summary>The installer runs directly (no shell) with the silent-install switches from the installer contract.</summary>
    public static ProcessStartInfo BuildInstallerStartInfo(string installerPath)
    {
        var info = new ProcessStartInfo(installerPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installerPath)!,
        };
        foreach (var arg in InstallerArguments) info.ArgumentList.Add(arg);
        return info;
    }

    /// <summary>Re-checks the hash right before starting; returns false (and deletes the folder on a mismatch) when it cannot start.</summary>
    public static bool TryLaunchInstaller(DownloadedInstaller installer)
    {
        var installerPath = installer.Path;
        if (!VerifyFile(installerPath, installer.Sha256))
        {
            TryDeleteFolder(Path.GetDirectoryName(installerPath)!);
            return false;
        }

        try
        {
            using var process = Process.Start(BuildInstallerStartInfo(installerPath));
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    // ---------- HTTP plumbing ----------

    /// <summary>GET with redirects followed by hand, so every hop (and the final host) is checked against <paramref name="allowed"/>.</summary>
    private async Task<HttpResponseMessage> SendAsync(Uri uri, Func<Uri, bool> allowed, string accept, CancellationToken ct)
    {
        for (var hop = 0; ; hop++)
        {
            if (!allowed(uri)) throw new UpdateException("host not allowed: " + uri.Host);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.ParseAdd(accept);

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode))
            {
                // Handlers that follow redirects themselves report the final address here; check it too.
                var final = response.RequestMessage?.RequestUri;
                if (final is not null && !allowed(final))
                {
                    response.Dispose();
                    throw new UpdateException("redirected to a host that is not allowed");
                }

                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || hop >= MaxRedirects) throw new UpdateException("bad redirect");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
    }

    private static bool IsRedirect(HttpStatusCode code) => code is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
        or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int limit, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > limit) throw new UpdateException("response too large");

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit) throw new UpdateException("response too large");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Copies at most <paramref name="expectedSize"/> bytes (never more than the hard limit), hashing on the way.</summary>
    private static async Task<string> CopyHashedAsync(HttpResponseMessage response, Stream target, long expectedSize,
        IProgress<double>? progress, CancellationToken ct)
    {
        var limit = Math.Min(expectedSize, MaxSetupBytes);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > limit) throw new UpdateException("installer larger than declared");
            hash.AppendData(chunk, 0, read);
            await target.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            progress?.Report((double)total / expectedSize);
        }

        if (total != expectedSize) throw new UpdateException("installer size differs from the release");
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public void Dispose() => _http.Dispose();
}
