// Placemaker — marker kinds as the panel and the overlay see them: label/tooltip keys, icon and
// overlay colour per kind, the kinds a step accepts and how many markers it needs before Build.
//
// Label keys: the generator's own option label when its Markers control lists options for the
// kind (parking: "Placemaker.Parking.Marker.Entrance"), otherwise "Placemaker.<Layer>.Marker.<Kind>"
// (layer and kind in PascalCase, e.g. "Placemaker.Plaza.Marker.Access").

using System;
using System.Collections.Generic;

namespace Placemaker.Core.Tool
{
    public sealed class MarkerKindInfo
    {
        public string Kind = "";
        public string LabelKey = "";
        public string TooltipKey = "";
        public string Icon = "";
        /// <summary>People walk in here: the entrance can come with or without a crosswalk (MarkerVariants.Walks).</summary>
        public bool Crosswalk;
    }

    public static class MarkerKinds
    {
        // Vanilla icons (verified to exist under Cities2_Data/Content/Game/UI/Media, game 1.6.2f1).
        private static readonly Dictionary<string, string> Icons = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "entrance", "Media/Game/Icons/SmallRoad.svg" },
            { "entry", "Media/Glyphs/FilledArrowRight.svg" },
            { "exit", "Media/Glyphs/FilledArrowLeft.svg" },
            { "alley", "Media/Game/Icons/Alley.svg" },
            { "alleyIn", "Media/Game/Icons/OnewayAlley.svg" },
            { "alleyOut", "Media/Game/Icons/OnewayAlley.svg" },
            { "busStop", "Media/Game/Icons/BusStop.svg" },
            { "zoningPatch", "Media/Game/Icons/Zones.svg" },
            { "footpath", "Media/Game/Icons/Pathways.svg" },
            { "access", "Media/Game/Icons/MapMarker.svg" },
            { "split", "Media/Tools/Snap Options/ExistingGeometry.svg" },
        };

        public static string IconFor(string kind) => kind != null && Icons.TryGetValue(kind, out var i) ? i : "Media/Game/Icons/MapMarker.svg";

        /// <summary>Overlay colour (r, g, b) per kind: two-way yellow, entry green, exit orange-red, alley grey, footpath sand, access blue.</summary>
        public static (float r, float g, float b) ColorFor(string kind)
        {
            switch (MarkerVariants.BaseKind(kind))
            {
                case "entry": return (0.35f, 0.9f, 0.4f);
                case "exit": return (1f, 0.45f, 0.25f);
                case "alley": return (0.75f, 0.75f, 0.8f);
                case "alleyIn": return (0.55f, 0.8f, 0.6f);
                case "alleyOut": return (0.9f, 0.6f, 0.5f);
                case "busStop": return (0.6f, 0.45f, 1f);
                case "zoningPatch": return (0.3f, 0.85f, 0.75f);
                case "footpath": return (0.95f, 0.85f, 0.55f);
                case "access": return (0.4f, 0.75f, 1f);
                case "split": return (1f, 0.4f, 0.8f);
                // Park-family entrance kinds (Core/Layers/Common/Entrances.cs EntranceKinds).
                case "pedestrian": return (0.95f, 0.85f, 0.55f);
                case "bike": return (0.3f, 0.85f, 0.45f);
                case "main": return (1f, 0.7f, 0.15f);
                case "service": return (0.6f, 0.65f, 0.75f);
                default: return (1f, 0.85f, 0.2f);
            }
        }

        public static string Pascal(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var parts = s.Split(new[] { '-', '_', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts) sb.Append(char.ToUpperInvariant(p[0])).Append(p.Substring(1));
            return sb.ToString();
        }

        /// <summary>Kinds the generator's Markers controls accept, in display order (no duplicates).</summary>
        public static List<MarkerKindInfo> For(IAreaGenerator gen)
        {
            var list = new List<MarkerKindInfo>();
            if (gen == null) return list;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<ControlSchema> controls;
            try { controls = gen.Controls; } catch (Exception) { return list; }
            if (controls == null) return list;
            var all = new List<string>();
            foreach (var c in controls)
                if (c != null && c.Kind == ControlKind.Markers && c.MarkerKinds != null) all.AddRange(c.MarkerKinds);
            foreach (var c in controls)
            {
                if (c == null || c.Kind != ControlKind.Markers || c.MarkerKinds == null) continue;
                foreach (var kind in c.MarkerKinds)
                {
                    if (string.IsNullOrEmpty(kind) || !seen.Add(kind)) continue;
                    var info = new MarkerKindInfo
                    {
                        Kind = kind,
                        LabelKey = "Placemaker." + Pascal(gen.Id) + ".Marker." + Pascal(kind),
                        Icon = IconFor(kind),
                        Crosswalk = MarkerVariants.Walks(kind, all),
                    };
                    if (c.Options != null)
                        foreach (var o in c.Options)
                            if (o != null && o.Value == kind)
                            {
                                if (!string.IsNullOrEmpty(o.LabelKey)) info.LabelKey = o.LabelKey;
                                if (!string.IsNullOrEmpty(o.TooltipKey)) info.TooltipKey = o.TooltipKey;
                                // The tool owns verified vanilla icons for the kinds it knows; layers may add icons for others.
                                if (!string.IsNullOrEmpty(o.Icon) && !Icons.ContainsKey(kind)) info.Icon = o.Icon;
                            }
                    list.Add(info);
                }
            }
            return list;
        }

        /// <summary>
        /// Markers the step needs before Build: 1 when the generator is not ready without any marker
        /// (parking needs an entrance), 0 when markers are optional.
        /// </summary>
        public static int Required(IAreaGenerator gen, SettingsBag settings)
        {
            if (gen == null || For(gen).Count == 0) return 0;
            try
            {
                var r = gen.CheckReady(new AreaInput { Settings = settings ?? new SettingsBag() });
                return r != null && !r.Ready ? 1 : 0;
            }
            catch (Exception) { return 0; }
        }
    }
}
