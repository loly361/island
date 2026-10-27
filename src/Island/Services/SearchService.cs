using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Island.Services;

public enum ResultKind { App, File, Ask, Web }

public sealed record SearchResult(ResultKind Kind, string Title, string Subtitle, string Target);

public sealed record AppEntry(string Name, string Path);

public static class SearchRanker
{
    public static List<AppEntry> RankApps(IEnumerable<AppEntry> apps, string query, int max)
    {
        query = query.Trim();
        if (query.Length == 0) return new List<AppEntry>();

        return apps
            .Select(a => (App: a, Score: Score(a.Name, query)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.App.Name.Length)
            .ThenBy(x => x.App.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(max)
            .Select(x => x.App)
            .ToList();
    }

    public static int Score(string name, string query)
    {
        if (name.Equals(query, StringComparison.CurrentCultureIgnoreCase)) return 100;
        if (name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return 80;
        if (name.Contains(" " + query, StringComparison.CurrentCultureIgnoreCase)) return 60;
        if (name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return 40;
        return 0;
    }
}

public sealed class SearchService
{
    private static readonly string[] SkipWords = { "uninstall", "удалить", "удаление", "readme", "справка", "help" };
    private List<AppEntry> _apps = new();

    public Task IndexAppsAsync() => Task.Run(() =>
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
        };

        var found = new Dictionary<string, AppEntry>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    if (SkipWords.Any(w => name.Contains(w, StringComparison.CurrentCultureIgnoreCase))) continue;
                    found.TryAdd(name, new AppEntry(name, file));
                }
            }
            catch
            {
                // An unreadable subfolder should not abort indexing.
            }
        }

        _apps = found.Values.ToList();
    });

    public async Task<List<SearchResult>> SearchAsync(string query, Settings settings, CancellationToken ct)
    {
        query = query.Trim();
        var results = new List<SearchResult>();
        if (query.Length == 0) return results;

        foreach (var app in SearchRanker.RankApps(_apps, query, 5))
            results.Add(new SearchResult(ResultKind.App, app.Name, "Приложение", app.Path));

        var files = await Task.Run(() => FindFiles(query, 5, ct), ct);
        results.AddRange(files);

        var encoded = Uri.EscapeDataString(query);
        results.Add(new SearchResult(ResultKind.Ask, $"Спросить ИИ: «{query}»", "claude.ai", string.Format(settings.AskUrl, encoded)));
        results.Add(new SearchResult(ResultKind.Web, $"Искать в браузере: «{query}»", "Поиск в интернете", string.Format(settings.SearchUrl, encoded)));
        return results;
    }

    /// <summary>Queries the Windows Search index; returns nothing if indexing is off or unavailable.</summary>
    private static List<SearchResult> FindFiles(string query, int max, CancellationToken ct)
    {
        var list = new List<SearchResult>();
        try
        {
            var safe = query.Replace("'", "''").Replace("%", "").Replace("_", "");
            if (safe.Length == 0) return list;

            using var conn = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties=\"Application=Windows\"");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT TOP 40 System.ItemName, System.ItemPathDisplay FROM SystemIndex " +
                $"WHERE System.ItemName LIKE '%{safe}%' AND SCOPE='file:' ORDER BY System.DateModified DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read() && list.Count < max)
            {
                ct.ThrowIfCancellationRequested();
                var name = reader.GetValue(0)?.ToString();
                var path = reader.GetValue(1)?.ToString();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) continue;
                if (path.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.Contains(@"\node_modules\", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new SearchResult(ResultKind.File, name, path, path));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Windows Search disabled or provider missing: app and web results still work.
        }

        return list;
    }
}
