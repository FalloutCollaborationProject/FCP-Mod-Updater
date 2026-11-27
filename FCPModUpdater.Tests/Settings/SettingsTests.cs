using System.Net.Http.Headers;
using FCPModUpdater.Services;
using FCPModUpdater.Settings;
using FCPModUpdater.UI;

namespace FCPModUpdater.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"fcp_settings_{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_dir, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            GitService.DeleteDirectory(_dir);
    }

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        var store = new SettingsStore(FilePath);

        Assert.Null(store.Current.ModsDirectory);
        Assert.Null(store.Current.GitHubToken);
        Assert.True(store.Current.CheckForUpdatesOnLaunch);
        Assert.Null(store.Current.IncludePrereleases);
    }

    [Fact]
    public void Update_RoundTripsThroughFile()
    {
        new SettingsStore(FilePath).Update(s => s with
        {
            ModsDirectory = "/games/Mods",
            GitHubToken = "tok",
            CheckForUpdatesOnLaunch = false,
            IncludePrereleases = true,
        });

        AppSettings loaded = new SettingsStore(FilePath).Current;
        Assert.Equal("/games/Mods", loaded.ModsDirectory);
        Assert.Equal("tok", loaded.GitHubToken);
        Assert.False(loaded.CheckForUpdatesOnLaunch);
        Assert.True(loaded.IncludePrereleases);
        Assert.True(loaded.EffectiveIncludePrereleases);
        Assert.DoesNotContain("Effective", File.ReadAllText(FilePath));
    }

    [Fact]
    public void CorruptFile_GivesDefaults_AndIsReplacedOnSave()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json");

        var store = new SettingsStore(FilePath);
        Assert.Null(store.Current.ModsDirectory);

        store.Update(s => s with { ModsDirectory = "/x" });
        Assert.Equal("/x", new SettingsStore(FilePath).Current.ModsDirectory);
    }

    [Fact]
    public void Save_IsOwnerOnlyOnUnix()
    {
        if (OperatingSystem.IsWindows())
            return;

        new SettingsStore(FilePath).Update(s => s with { GitHubToken = "secret" });

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(FilePath));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Update_RaisesChanged()
    {
        var store = SettingsStore.InMemory();
        AppSettings? seen = null;
        store.Changed += s => seen = s;

        store.Update(s => s with { GitHubToken = "t" });

        Assert.Equal("t", seen?.GitHubToken);
    }

    [Fact]
    public void InMemory_NeverWritesAFile()
    {
        SettingsStore store = SettingsStore.InMemory();
        store.Update(s => s with { ModsDirectory = "/y" });

        Assert.Equal("/y", store.Current.ModsDirectory);
        Assert.False(File.Exists(store.FilePath));
    }
}

public class GitHubTokenProviderTests
{
    private static SettingsStore WithSavedToken(string? token) =>
        SettingsStore.InMemory(new AppSettings { GitHubToken = token });

    [Fact]
    public void Argument_BeatsEnvironmentAndSettings()
    {
        var provider = new GitHubTokenProvider(WithSavedToken("saved"), "arg", () => "env");
        Assert.Equal(("arg", TokenSource.Argument), provider.Resolve());
    }

    [Fact]
    public void Environment_BeatsSettings()
    {
        var provider = new GitHubTokenProvider(WithSavedToken("saved"), null, () => "env");
        Assert.Equal(("env", TokenSource.Environment), provider.Resolve());
    }

    [Fact]
    public void Settings_UsedWhenNothingElse()
    {
        var provider = new GitHubTokenProvider(WithSavedToken(" saved "), "", () => "  ");
        Assert.Equal(("saved", TokenSource.Settings), provider.Resolve());
    }

    [Fact]
    public void NoToken_ReportsNone()
    {
        var provider = new GitHubTokenProvider(WithSavedToken(null), null, () => null);
        Assert.Equal((null, TokenSource.None), provider.Resolve());
    }

    [Fact]
    public void SavedTokenChange_AppliesImmediately()
    {
        SettingsStore store = WithSavedToken(null);
        var provider = new GitHubTokenProvider(store, null, () => null);

        store.Update(s => s with { GitHubToken = "new" });

        Assert.Equal("new", provider.Token);
    }

    [Fact]
    public async Task AuthHandler_AddsBearer_ButKeepsExplicitHeader()
    {
        var provider = new GitHubTokenProvider(WithSavedToken("saved"), null, () => null);
        var capture = new CaptureHandler();
        using var client = new HttpClient(new GitHubAuthHandler(provider) { InnerHandler = capture })
            { BaseAddress = new Uri("https://example.invalid") };
        CancellationToken ct = TestContext.Current.CancellationToken;

        await client.GetAsync("a", ct);
        Assert.Equal("Bearer saved", capture.Last?.ToString());

        using var request = new HttpRequestMessage(HttpMethod.Get, "b");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "typed");
        await client.SendAsync(request, ct);
        Assert.Equal("Bearer typed", capture.Last?.ToString());
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}

public class SettingsEditorTests
{
    [Theory]
    [InlineData("check-updates", "off", false)]
    [InlineData("check-updates", "YES", true)]
    public void CheckUpdates_ParsesBooleans(string key, string value, bool expected)
    {
        var (result, error) = SettingsEditor.Apply(new AppSettings(), key, value);
        Assert.Null(error);
        Assert.Equal(expected, result!.CheckForUpdatesOnLaunch);
    }

    [Fact]
    public void IncludePrereleases_UnsetReturnsToDefault()
    {
        var (result, _) = SettingsEditor.Apply(new AppSettings { IncludePrereleases = true }, "include-prereleases", null);
        Assert.Null(result!.IncludePrereleases);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData(" 1 ", 1)]
    public void UpdateParallelism_AcceptsOneToMax(string value, int expected)
    {
        var (result, error) = SettingsEditor.Apply(new AppSettings(), "update-parallelism", value);
        Assert.Null(error);
        Assert.Equal(expected, result!.UpdateParallelism);
    }

    [Fact]
    public void UpdateParallelism_AcceptsMax()
    {
        var max = ParallelRunner.MaxParallelism;
        var (result, error) = SettingsEditor.Apply(new AppSettings(), "update-parallelism", max.ToString());
        Assert.Null(error);
        Assert.Equal(max, result!.UpdateParallelism);
    }

    public static TheoryData<string> OutOfRangeParallelism =>
        ["0", (ParallelRunner.MaxParallelism + 1).ToString(), "lots"];

    [Theory]
    [MemberData(nameof(OutOfRangeParallelism))]
    public void UpdateParallelism_OutOfRange_IsAnError(string value)
    {
        var (result, error) = SettingsEditor.Apply(new AppSettings(), "update-parallelism", value);
        Assert.Null(result);
        Assert.Contains($"1 to {ParallelRunner.MaxParallelism}", error);
    }

    [Fact]
    public void UpdateParallelism_Unset_RestoresDefault_AndOutOfRangeFileValuesAreClamped()
    {
        var (result, _) = SettingsEditor.Apply(new AppSettings { UpdateParallelism = 2 }, "update-parallelism", null);
        Assert.Equal(AppSettings.DefaultUpdateParallelism, result!.UpdateParallelism);
        Assert.Equal(ParallelRunner.MaxParallelism, new AppSettings { UpdateParallelism = 50 }.EffectiveUpdateParallelism);
        Assert.Equal(1, new AppSettings { UpdateParallelism = 0 }.EffectiveUpdateParallelism);
    }

    [Fact]
    public void BadBoolean_IsAnError()
    {
        var (result, error) = SettingsEditor.Apply(new AppSettings(), "include-prereleases", "maybe");
        Assert.Null(result);
        Assert.Contains("true or false", error);
    }

    [Fact]
    public void ModsDirectory_MustExist()
    {
        var (result, error) = SettingsEditor.Apply(new AppSettings(), "mods-directory",
            Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}"));
        Assert.Null(result);
        Assert.Contains("does not exist", error);

        var (ok, _) = SettingsEditor.Apply(new AppSettings(), "mods-directory", Path.GetTempPath());
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), ok!.ModsDirectory);
    }

    [Fact]
    public void UnknownKey_ListsKnownKeys()
    {
        var (_, error) = SettingsEditor.Apply(new AppSettings(), "colour", "green");
        Assert.Contains("mods-directory", error);
    }

    [Fact]
    public void Token_IsTrimmed_AndBlankClears()
    {
        Assert.Equal("abc", SettingsEditor.Apply(new AppSettings(), "github-token", " abc ").Result!.GitHubToken);
        Assert.Null(SettingsEditor.Apply(new AppSettings { GitHubToken = "x" }, "github-token", " ").Result!.GitHubToken);
    }

    [Fact]
    public void MaskToken_ShowsOnlyEnds()
    {
        Assert.Equal("abcd••••••••6789", SettingsEditor.MaskToken("abcdefghij6789"));
        Assert.Equal("(not set)", SettingsEditor.MaskToken(null));
    }

    [Fact]
    public void FindOption_ReadsSpacedAndEqualsForms()
    {
        Assert.Equal("t1", CommandLine.FindOption(["scan", "--github-token", "t1"], "--github-token"));
        Assert.Equal("t2", CommandLine.FindOption(["--github-token=t2"], "--github-token"));
        Assert.Null(CommandLine.FindOption(["--github-token"], "--github-token"));
    }
}

public class UpdateCheckFilterTests
{
    private const string Releases = """
        [
          {"tag_name":"v99.1.0-beta.1","name":"beta","html_url":"https://x/beta","published_at":"2026-11-10T00:00:00Z","prerelease":true,"draft":false},
          {"tag_name":"v99.0.0","name":"stable","html_url":"https://x/stable","published_at":"2026-11-01T00:00:00Z","prerelease":false,"draft":false,"body":"Stable notes"},
          {"tag_name":"v100.0.0","name":"draft","html_url":"https://x/draft","published_at":"2026-11-12T00:00:00Z","prerelease":false,"draft":true}
        ]
        """;

    private static UpdateCheckService Service() =>
        new(new HttpClient(new JsonHandler(Releases)) { BaseAddress = new Uri("https://api.github.invalid") });

    [Fact]
    public async Task IncludingPrereleases_PicksNewestPrerelease()
    {
        UpdateCheckResult? result = await Service().CheckForUpdateAsync(true, TestContext.Current.CancellationToken);
        Assert.Equal("99.1.0-beta.1", result?.LatestVersion);
        Assert.True(result?.IsPrerelease);
    }

    [Fact]
    public async Task ExcludingPrereleases_PicksNewestStable()
    {
        UpdateCheckResult? result = await Service().CheckForUpdateAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal("99.0.0", result?.LatestVersion);
        Assert.Equal("Stable notes", result?.ReleaseNotes);
    }

    [Fact]
    public async Task CheckAndRecord_StoresTimeAndResult()
    {
        SettingsStore store = SettingsStore.InMemory(new AppSettings { IncludePrereleases = false });

        await Service().CheckAndRecordAsync(store, TestContext.Current.CancellationToken);

        Assert.NotNull(store.Current.LastUpdateCheck);
        Assert.Equal("99.0.0", store.Current.LastUpdateCheckResult);
    }

    [Fact]
    public async Task RateLimit_ParsesCoreResource()
    {
        const string json = """{"resources":{"core":{"limit":5000,"remaining":4982,"reset":1700000000}}}""";
        var api = new GitHubApiService(new HttpClient(new JsonHandler(json)) { BaseAddress = new Uri("https://api.github.invalid") });

        RateLimitCheck check = await api.GetRateLimitAsync(ct: TestContext.Current.CancellationToken);

        Assert.True(check.Success);
        Assert.Equal(4982, check.Info!.Remaining);
        Assert.Equal(5000, api.RateLimitTotal);
    }

    [Fact]
    public async Task RateLimit_RejectedToken_IsAnError_AndLeavesSidebarNumbersAlone()
    {
        var api = new GitHubApiService(new HttpClient(new JsonHandler("{}", HttpStatusCode.Unauthorized))
            { BaseAddress = new Uri("https://api.github.invalid") });

        RateLimitCheck check = await api.GetRateLimitAsync("bad", TestContext.Current.CancellationToken);

        Assert.False(check.Success);
        Assert.Contains("401", check.Error);
        Assert.Null(api.RemainingRateLimit);
    }

    private sealed class JsonHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
    }

    [Fact]
    public void GitPath_AcceptsFileOrInstallFolder_AndRejectsMissing()
    {
        var dir = Directory.CreateTempSubdirectory("git-path-").FullName;
        try
        {
            var cmd = Directory.CreateDirectory(Path.Combine(dir, "cmd")).FullName;
            var exe = Path.Combine(cmd, OperatingSystem.IsWindows() ? "git.exe" : "git");
            File.WriteAllText(exe, "");

            Assert.Equal(exe, GitService.NormalizeGitPath(exe));
            Assert.Equal(exe, GitService.NormalizeGitPath($"\"{dir}\""));
            Assert.Null(GitService.NormalizeGitPath(Path.Combine(dir, "nope")));

            var (set, error) = SettingsEditor.Apply(new AppSettings(), "git-path", dir);
            Assert.Null(error);
            Assert.Equal(exe, set!.GitPath);
            Assert.NotNull(SettingsEditor.Apply(new AppSettings(), "git-path", Path.Combine(dir, "nope")).Error);
            Assert.Null(SettingsEditor.Apply(set, "git-path", null).Result!.GitPath);
        }
        finally
        {
            GitService.DeleteDirectory(dir);
        }
    }
}
