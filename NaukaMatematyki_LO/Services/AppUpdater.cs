using Velopack;
using Velopack.Sources;

namespace NaukaMatematyki_LO;

/// <summary>
/// Szuka nowego wydania w GitHub Releases i podmienia zainstalowaną kopię programu.
/// Działa dopiero po instalacji z Setup.exe. Uruchomienie z katalogu projektu nic nie pobiera.
/// </summary>
internal sealed class AppUpdater
{
    public const string RepoUrl = "https://github.com/WaldemarPredkiDGTL/NaukaMatematyki_LO";

    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    public AppUpdater()
    {
        _manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
    }

    public bool CanUpdate => _manager.IsInstalled;

    public async Task<string?> CheckVersionAsync()
    {
        if (!_manager.IsInstalled) return null;
        _pending = await _manager.CheckForUpdatesAsync();
        return _pending?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(Action<int> progress)
    {
        if (_pending == null) return Task.CompletedTask;
        return _manager.DownloadUpdatesAsync(_pending, progress);
    }

    public void ApplyAndRestart()
    {
        if (_pending == null) return;
        _manager.ApplyUpdatesAndRestart(_pending);
    }
}
