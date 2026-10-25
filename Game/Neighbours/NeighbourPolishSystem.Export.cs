// Placemaker neighbourhood polish — every Outgrow setting written to one file (GPL-3.0).
//
// The user, 25 Oct: "I want a way to be able to export my settings to like a file or something, because I want you to make
// those the default. I'm going to play around with all the settings, all the defaults, and I can export them." The button
// Export Outgrow settings (Options, Placemaker, Maintenance) writes ModsData/Placemaker/outgrow-settings.json and shows it in
// Explorer: every outgrow type (built-in and the player's own) with all its values and what differs from how it ships, the
// editor's rows when they are not saved, each zone type's pick and each kind's, and the Outgrow switches.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Placemaker.Core.Neighbours;
using Placemaker.Core.Tool;
using Placemaker.Game.Settings;

namespace Placemaker.Game.Neighbours
{
    public sealed partial class NeighbourPolishSystem
    {
        /// <summary>The settings written to ModsData/Placemaker/outgrow-settings.json; the file's path.</summary>
        /// <param name="editor">The editor's rows when they differ from the look picked (null: saved): its look id, base theme
        /// and every value.</param>
        public static string ExportSettings(PlacemakerSettings s, (string Look, string Theme, Dictionary<string, float> Values)? editor)
        {
            JsonValue Values(IReadOnlyDictionary<string, float> d, IReadOnlyDictionary<string, float> only = null)
            {
                var o = JsonValue.Obj();
                foreach (var kv in d.OrderBy(k => k.Key, StringComparer.Ordinal))
                    if (only == null || !only.TryGetValue(kv.Key, out var w) || Math.Abs(w - kv.Value) > 1e-4f) o.Set(kv.Key, Math.Round(kv.Value, 3));
                return o;
            }
            var looks = JsonValue.Arr();
            foreach (var l in Looks)
            {
                var shipped = PolishLooks.BuiltIn.FirstOrDefault(b => b.Id == l.Id);
                var values = l.Values();
                var changed = Values(values, shipped != null ? shipped.Values() : (IReadOnlyDictionary<string, float>)PolishLooks.Defaults);
                if (shipped != null && shipped.Theme != l.Theme) changed.Set("theme", l.Theme);
                looks.Add(JsonValue.Obj().Set("id", l.Id).Set("name", l.Name).Set("custom", l.Custom).Set("edited", l.Edited).Set("theme", l.Theme)
                    .Set("color", l.Color ?? "").Set("changed", changed).Set("values", Values(values)));
            }
            var zones = JsonValue.Obj();
            foreach (var kv in s_zoneLooks.OrderBy(k => k.Key, StringComparer.Ordinal)) zones.Set(kv.Key, kv.Value);
            var root = JsonValue.Obj()
                .Set("about", "Placemaker's Outgrow settings, exported from the game (Options, Placemaker, Export Outgrow settings): every outgrow type with all its values (values) and what differs from how it ships (changed), the editor's rows if they were not saved, each zone type's pick (zoneLooks: zone prefab -> outgrow type or off; a zone not listed follows its kind) and each kind's (kindLooks: auto = each zone type its own), and the switches.")
                .Set("exported", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .Set("looks", looks)
                .Set("unsavedEditor", editor == null ? JsonValue.Null : JsonValue.Obj().Set("look", editor.Value.Look ?? "").Set("theme", editor.Value.Theme ?? "")
                    .Set("changed", Values(editor.Value.Values, LookById(editor.Value.Look)?.Values())).Set("values", Values(editor.Value.Values)))
                .Set("zoneLooks", zones)
                .Set("kindLooks", JsonValue.Obj().Set("homes", s?.PolishThemeHomes ?? "").Set("shops", s?.PolishThemeShops ?? "").Set("offices", s?.PolishThemeOffices ?? "").Set("industry", s?.PolishThemeIndustry ?? ""))
                .Set("switches", JsonValue.Obj().Set("outgrow", s?.NeighbourhoodPolish ?? false).Set("outgrowNewBuildings", PolishNew).Set("againOnLevelUp", PolishLevelUps)
                    .Set("againOnGameChange", PolishGameChanges).Set("againOnRemovalNearby", PolishGameRemovals));
            Directory.CreateDirectory(ModPaths.DataFolder);
            var path = Path.Combine(ModPaths.DataFolder, "outgrow-settings.json");
            File.WriteAllText(path, root.Write(true));
            return path;
        }
    }
}
