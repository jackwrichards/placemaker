// Placemaker — a new version of the mod's files on Paradox Mods, the page left as it is (GPL-3.0).
//
// The game's uploader (ModPublisher NewVersion) starts from the live mod but then writes every page field from the
// config: the name, both descriptions, tags, links, the forum link (none in the config: cleared) and the access level,
// and its own placeholder thumbnail when the config names none. The 0.3.0 upload (26 Oct) wiped the user's thumbnail,
// pictures and forum link that way. This tool starts from the live mod the same way (PDX SDK
// GetModStagingDataFromExistingMod: its thumbnail, screenshots, name, descriptions, links, tags, access level, game
// version) and changes only the files, the version and the changelog. It reads the page back after the upload and
// says what differs.
//
//   NewVersion <modId> <version> <changelog file> <content folder> [--dry-run]

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK;
using PDX.SDK.Contracts.Configuration;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.Mods.Interfaces;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var plain = args.Where(a => !a.StartsWith("--")).ToArray();
        bool dryRun = args.Contains("--dry-run");
        if (plain.Length != 4) { Console.WriteLine("usage: NewVersion <modId> <version> <changelog file> <content folder> [--dry-run]"); return 2; }
        string modId = plain[0], version = plain[1], changelog = File.ReadAllText(plain[2]).Trim(), content = Path.GetFullPath(plain[3]);
        if (changelog.Length == 0) { Console.WriteLine("STOP: the changelog is empty"); return 1; }
        var files = Directory.Exists(content) ? Directory.GetFiles(content, "*", SearchOption.AllDirectories).Where(f => Path.GetExtension(f) != ".nupkg").ToArray() : new string[0];
        if (files.Length == 0) { Console.WriteLine("STOP: no files in " + content); return 1; }

        var context = await CreateContext();
        if (context == null) return 1;
        if (!await Login(context)) return 1;

        var before = await context.Mods.Publishing.GetModStagingDataFromExistingMod(modId, null, ModPlatform.Windows);
        Console.WriteLine("the page now:");
        var page = Describe(before);
        foreach (var line in page) Console.WriteLine("  " + line);
        // what would be sent back must be the page as it is: stop when any of it did not come down
        var problems = new List<string>();
        if (before.MetaData == null || string.IsNullOrWhiteSpace(before.MetaData.DisplayName)) problems.Add("the page's name and text did not load");
        if (string.IsNullOrEmpty(before.ThumbnailAbsolutePath) || !File.Exists(before.ThumbnailAbsolutePath)) problems.Add("the page's thumbnail did not download");
        foreach (var s in before.ScreenshotsAbsolutePaths ?? new string[0]) if (!File.Exists(s)) problems.Add("a screenshot did not download: " + s);
        if (problems.Count > 0) { foreach (var p in problems) Console.WriteLine("STOP: " + p); return 1; }

        before.WithContentFiles(files);
        before.WithModMetaData(m => m.WithUserModVersion(version).WithChangelog(changelog));
        Console.WriteLine($"sending: {files.Length} files from {content}, version {version}, changelog:");
        foreach (var line in changelog.Split('\n')) Console.WriteLine("  " + line.TrimEnd());
        Console.WriteLine("everything else on the page is sent back as it is above");
        if (dryRun) { Console.WriteLine("dry run: nothing uploaded"); return 0; }

        var result = await context.Mods.PublishMod(before);
        if (!result.Success) { Console.WriteLine("STOP: the upload failed: " + (result.Error?.Message ?? "unknown error")); return 1; }
        Console.WriteLine($"uploaded: mod {result.ModId} version {result.ModVersion}");

        // the page read back: the same as before, but the version and changelog
        var after = await context.Mods.Publishing.GetModStagingDataFromExistingMod(modId, null, ModPlatform.Windows);
        var now = Describe(after);
        int changed = 0;
        for (int i = 0; i < Math.Max(page.Count, now.Count); i++)
        {
            string a = i < page.Count ? page[i] : "", b = i < now.Count ? now[i] : "";
            if (a == b || a.StartsWith("version:")) continue;
            Console.WriteLine($"CHANGED: {a}  ->  {b}");
            changed++;
        }
        Console.WriteLine(changed == 0 ? "the page read back: unchanged but the version and changelog" : $"the page read back: {changed} line(s) changed");
        foreach (var line in now) if (line.StartsWith("version:")) Console.WriteLine("  " + line);
        return changed == 0 ? 0 : 3;
    }

    /// <summary>The page, one comparable line per field (long texts by length and a checksum).</summary>
    private static List<string> Describe(IModStagingDataBuilder d)
    {
        var m = d.MetaData;
        string Sum(string s) => s == null ? "none" : $"{s.Length} chars, sum {s.Aggregate(17u, (h, c) => h * 31u + c):x8}";
        string Pic(string p) => p != null && File.Exists(p) ? $"{new FileInfo(p).Length} bytes" : "none";
        var lines = new List<string>
        {
            "name: " + m?.DisplayName,
            "version: " + m?.UserModVersion,
            "game version: " + m?.RecommendedGameVersion,
            "access: " + m?.AccessControlLevelState,
            "short description: " + Sum(m?.ShortDescription),
            "long description: " + Sum(m?.LongDescription),
            "forum links: " + string.Join(" ", m?.ForumLinks ?? new string[0]),
            "links: " + string.Join(" ", (m?.ExternalLinks ?? new PDX.SDK.Contracts.Service.Mods.Models.ExternalLink[0]).Select(l => l.Type + " " + l.URL)),
            "tags: " + string.Join(", ", m?.Tags ?? new string[0]),
            "dependencies: " + (m?.Dependencies?.Length ?? 0),
            "thumbnail: " + Pic(d.ThumbnailAbsolutePath),
            "screenshots: " + (d.ScreenshotsAbsolutePaths?.Length ?? 0),
        };
        foreach (var s in d.ScreenshotsAbsolutePaths ?? new string[0]) lines.Add("  screenshot: " + Pic(s));
        return lines;
    }

    // as the game's uploader does (ModPublisher CreateContext, Login): the login last used in the game
    private static async Task<Context> CreateContext()
    {
        var config = new Config();
        string root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow", "Colossal Order", "Cities Skylines II"));
        config.Mods.RootPath = Environment.GetEnvironmentVariable("CSII_PDXMODSPATH", EnvironmentVariableTarget.User) ?? Path.Combine(root, ".cache", "Mods");
        config.DiskIORoot = Environment.GetEnvironmentVariable("CSII_PDXCACHEPATH", EnvironmentVariableTarget.User) ?? Path.Combine(root, ".pdxsdk");
        string last = Path.Combine(config.DiskIORoot, "LastUserId.txt");
        if (File.Exists(last))
        {
            string id = File.ReadLines(last).FirstOrDefault();
            if (!string.IsNullOrEmpty(id)) config.DiskIORoot = Path.Combine(config.DiskIORoot, id);
        }
        config.Environment = BackendEnvironment.Live;
        var r = await Context.Create(Platform.Windows, "cities_skylines_2", config);
        if (r.Success == ResultStatus.Success || r.Success == ResultStatus.Partial) return r.Context as Context;
        Console.WriteLine("STOP: no Paradox context");
        return null;
    }

    private static async Task<bool> Login(Context context)
    {
        var startup = await context.Account.Startup();
        if (!startup.Success || !startup.IsLoggedIn) { Console.WriteLine("STOP: not logged in (sign in to Paradox in the game once): " + startup.Error?.Message); return false; }
        var details = await context.Account.GetDetails();
        if (!details.Success) { Console.WriteLine("STOP: no account details"); return false; }
        Console.WriteLine("logged in as the game's Paradox account");
        return true;
    }
}
