// Placemaker — recipe files (shipped Recipes/*.json and player recipes in ModsData/Placemaker/Recipes).
//
// Format: docs/proposals/recipe-json.md (park-plaza workstream) — recipe metadata plus a nested
// "root" step:
//
//   { "formatVersion": 1, "id": "split-park-plaza", "nameKey": "...", "descriptionKey": "...", "icon": "...",
//     "root": { "layer": "split", "settings": { "ratio": 60 },
//               "children": [ { "layer": "park", "target": "part:0" } ],
//               "regionOverrides": { "part:1": { "layer": "plaza" } } } }
//
// Step settings in files are OVERRIDES on the generator defaults (the game-side RecipeStore
// overlays them). The reader also accepts the root step's fields flat at top level, and an
// optional "order". Markers are written only for receipts, never into recipe files. Unknown
// fields are ignored, missing ones take defaults.

using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Mathematics;

namespace Placemaker.Core.Tool
{
    public sealed class RecipeFile
    {
        public Recipe Recipe;
        public string DescriptionKey = "";
        public int Order = 1000;
        /// <summary>formatVersion found in the file (may be newer than we write).</summary>
        public int FormatVersion = 1;
    }

    public static class RecipeJson
    {
        public const int CurrentFormatVersion = 1;

        public static RecipeFile Read(JsonValue doc)
        {
            if (doc == null || !doc.IsObject) throw new FormatException("Recipe must be a JSON object");
            var r = new Recipe
            {
                Id = doc["id"].AsString(""),
                NameKey = doc["nameKey"].AsString(""),
                Name = doc["name"].AsString(""),
                Icon = doc["icon"].AsString(""),
                FormatVersion = (int)doc["formatVersion"].AsNumber(1),
            };
            var rootJson = doc["root"].IsObject ? doc["root"] : doc;
            r.Root = ReadStep(rootJson, 0);
            Layers.Common.LayerAliases.Normalize(r.Root);   // retired layer ids → canonical (g5)
            if (string.IsNullOrEmpty(r.Root.Layer)) throw new FormatException("Recipe '" + r.Id + "' has no root layer");
            return new RecipeFile
            {
                Recipe = r,
                DescriptionKey = doc["descriptionKey"].AsString(""),
                Order = (int)doc["order"].AsNumber(1000),
                FormatVersion = r.FormatVersion,
            };
        }

        public static RecipeFile Parse(string text) => Read(JsonValue.Parse(text));

        public static RecipeStep ReadStep(JsonValue j, int depth)
        {
            var s = new RecipeStep();
            if (j == null || !j.IsObject || depth > Composer.MaxDepth + 2) return s;
            s.Layer = j["layer"].AsString("");
            s.Target = j["target"].AsString("");
            s.Enabled = j["enabled"].AsBool(true);
            s.Seed = (uint)Math.Max(0, Math.Min(uint.MaxValue, j["seed"].AsNumber(0)));
            s.Settings = ReadSettings(j["settings"]);
            foreach (var l in j["locked"].Items)
                if (l.Kind == JsonKind.String) s.Locked.Add(l.AsString());
            foreach (var m in j["markers"].Items)
            {
                if (!m.IsObject) continue;
                var kind = m["kind"].AsString("");
                if (kind.Length == 0) continue;
                s.Markers.Add(new EdgeMarker
                {
                    Kind = kind,
                    RingSegment = (int)m["segment"].AsNumber(0),
                    RingT = (float)m["t"].AsNumber(0),
                    Position = ReadFloat3(m["position"]),
                });
            }
            foreach (var t in j["touches"].Items)
            {
                if (!t.IsObject) continue;
                var kind = t["kind"].AsString("");
                if (kind.Length == 0) continue;
                // A touch from a newer Placemaker keeps its version, so it is carried along and written back, never applied.
                s.Touches.Add(new HandTouch(kind, ReadFloat3(t["position"]), t["yaw"].Kind == JsonKind.Number ? (float)t["yaw"].AsNumber() : float.NaN, t["arg"].AsString(""))
                {
                    Version = (int)t["v"].AsNumber(1),
                });
            }
            foreach (var c in j["children"].Items)
                if (c.IsObject) s.Children.Add(ReadStep(c, depth + 1));
            foreach (var kv in j["regionOverrides"].Properties)
                if (kv.Value.IsObject && kv.Key.Length > 0) s.RegionOverrides[kv.Key] = ReadStep(kv.Value, depth + 1);
            return s;
        }

        public static SettingsBag ReadSettings(JsonValue j)
        {
            var bag = new SettingsBag();
            foreach (var kv in j.Properties)
            {
                var v = DesignEditor.FromJson(kv.Value);
                if (v != null) bag.Values[kv.Key] = v;
            }
            return bag;
        }

        public static JsonValue WriteSettings(SettingsBag bag)
        {
            var o = JsonValue.Obj();
            if (bag == null) return o;
            var keys = new List<string>(bag.Values.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) o.Set(k, DesignEditor.ToJson(bag.Values[k]));
            return o;
        }

        public static JsonValue Write(Recipe r, string descriptionKey = null, int? order = null)
        {
            var o = JsonValue.Obj();
            o.Set("formatVersion", CurrentFormatVersion);
            o.Set("id", r.Id ?? "");
            if (!string.IsNullOrEmpty(r.NameKey)) o.Set("nameKey", r.NameKey);
            if (!string.IsNullOrEmpty(r.Name)) o.Set("name", r.Name);
            if (!string.IsNullOrEmpty(r.Icon)) o.Set("icon", r.Icon);
            if (!string.IsNullOrEmpty(descriptionKey)) o.Set("descriptionKey", descriptionKey);
            if (order.HasValue) o.Set("order", order.Value);
            o.Set("root", WriteStep(r.Root ?? new RecipeStep()));
            return o;
        }

        public static JsonValue WriteStep(RecipeStep s)
        {
            var o = JsonValue.Obj();
            WriteStepInto(o, s);
            return o;
        }

        private static void WriteStepInto(JsonValue o, RecipeStep s)
        {
            o.Set("layer", s.Layer ?? "");
            if (!string.IsNullOrEmpty(s.Target)) o.Set("target", s.Target);
            if (!s.Enabled) o.Set("enabled", false);
            if (s.Seed != 0) o.Set("seed", s.Seed);
            o.Set("settings", WriteSettings(s.Settings));
            if (s.Locked.Count > 0)
            {
                var l = new List<string>(s.Locked);
                l.Sort(StringComparer.Ordinal);
                var a = JsonValue.Arr();
                foreach (var x in l) a.Add(x);
                o.Set("locked", a);
            }
            if (s.Markers.Count > 0)
            {
                var a = JsonValue.Arr();
                foreach (var m in s.Markers)
                    a.Add(JsonValue.Obj().Set("kind", m.Kind ?? "").Set("segment", m.RingSegment).Set("t", m.RingT)
                        .Set("position", Float3(m.Position)));
                o.Set("markers", a);
            }
            if (s.Touches.Count > 0)
            {
                var a = JsonValue.Arr();
                foreach (var t in s.Touches)
                {
                    var to = JsonValue.Obj().Set("v", t.Version).Set("kind", t.Kind ?? "").Set("position", Float3(t.Position));
                    if (t.HasYaw) to.Set("yaw", Math.Round((double)t.Yaw, 4));
                    if (!string.IsNullOrEmpty(t.Arg)) to.Set("arg", t.Arg);
                    a.Add(to);
                }
                o.Set("touches", a);
            }
            if (s.Children.Count > 0)
            {
                var a = JsonValue.Arr();
                foreach (var c in s.Children) if (c != null) a.Add(WriteStep(c));
                o.Set("children", a);
            }
            if (s.RegionOverrides.Count > 0)
            {
                var ov = JsonValue.Obj();
                var keys = new List<string>(s.RegionOverrides.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var k in keys) if (s.RegionOverrides[k] != null) ov.Set(k, WriteStep(s.RegionOverrides[k]));
                o.Set("regionOverrides", ov);
            }
        }

        public static JsonValue Float3(float3 p) => JsonValue.Arr().Add(Round(p.x)).Add(Round(p.y)).Add(Round(p.z));

        // Millimetre precision keeps files small and diff-friendly without visible loss.
        private static double Round(float v) => Math.Round((double)v, 3);

        public static float3 ReadFloat3(JsonValue j)
        {
            if (j == null || !j.IsArray || j.Count < 3) return float3.zero;
            return new float3((float)j.Items[0].AsNumber(), (float)j.Items[1].AsNumber(), (float)j.Items[2].AsNumber());
        }

        // ───────────── per-layer saved defaults (ModsData/Placemaker/defaults.json) ─────────────

        public static Dictionary<string, SettingsBag> ReadDefaults(JsonValue doc)
        {
            var r = new Dictionary<string, SettingsBag>(StringComparer.Ordinal);
            if (doc == null || !doc.IsObject) return r;
            foreach (var kv in doc["layers"].Properties)
                if (kv.Value.IsObject) r[kv.Key] = ReadSettings(kv.Value);
            return r;
        }

        public static JsonValue WriteDefaults(Dictionary<string, SettingsBag> defaults)
        {
            var layers = JsonValue.Obj();
            var keys = new List<string>(defaults.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) layers.Set(k, WriteSettings(defaults[k]));
            return JsonValue.Obj().Set("formatVersion", CurrentFormatVersion).Set("layers", layers);
        }

        /// <summary>File-name-safe id for a player recipe ("My Grid!" → "my-grid-1a2b3c4d").</summary>
        public static string NewPlayerRecipeId(string name, uint salt)
        {
            var chars = new System.Text.StringBuilder();
            foreach (char c in (name ?? "").ToLowerInvariant())
            {
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9') chars.Append(c);
                else if (chars.Length > 0 && chars[chars.Length - 1] != '-') chars.Append('-');
                if (chars.Length >= 32) break;
            }
            string slug = chars.ToString().Trim('-');
            if (slug.Length == 0) slug = "recipe";
            return "player-" + slug + "-" + salt.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
