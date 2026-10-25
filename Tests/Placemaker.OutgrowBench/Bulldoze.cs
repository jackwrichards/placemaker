// OutgrowBench --bulldoze <passes dir> [--at x,z] [--size m] [--rounds 2] [--knob k=v] [--out dir]: the player's bulldozer,
// offline. The recorded passes replayed in order (as --bootcamp), then the house nearest --at taken away and the gardens that
// touched its garden planned again, the rest kept as they were (NeighbourPolishSystem.CollectGone: TouchingDirty, gap 2.5 m),
// as the game does. Draws the place before and after (bulldoze.html: lots dark green, gardens light, the house's fences and
// pieces, the planned houses' in red, the bulldozed lot dashed) and counts what the new fences do (gaps, crossings, doubles;
// turns between pieces in a run, pieces doubled back). The user, 25 Oct: "when I bulldoze something and other things fill it
// in, it just acts weird ... It does like squigglies."

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;

namespace Placemaker.OutgrowBench
{
    internal static class Bulldoze
    {
        public static int Run(List<string> args)
        {
            string input = args[0], outDir = null;
            float2? at = null;
            float size = 160f;
            int rounds = 2;
            var knobs = new Dictionary<string, float>();
            for (int i = 1; i < args.Count; i++)
                switch (args[i])
                {
                    case "--at": { var p = args[++i].Split(','); at = new float2(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture)); break; }
                    case "--size": size = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--rounds": rounds = int.Parse(args[++i]); break;
                    case "--knob": { var kv = args[++i].Split('='); knobs[kv[0]] = float.Parse(kv[1], CultureInfo.InvariantCulture); break; }
                    case "--out": outDir = args[++i]; break;
                }
            outDir ??= Path.Combine(Path.GetTempPath(), "outgrow-bulldoze");
            Directory.CreateDirectory(outDir);
            var texts = Directory.GetFiles(input, "pass-*.json").OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText).ToList();

            // ── the replay (as --bootcamp): each house's garden and fences as they stand, the settings it was last planned in ──
            var garden = new Dictionary<long, float2[]>();
            var fences = new Dictionary<long, List<(float2 A, float2 B, string Name, bool Piece)>>();
            var lastSeen = new Dictionary<long, StitchHouse>();
            var lastSettings = new Dictionary<long, StitchSettings>();
            for (int round = 0; round < rounds; round++)
                foreach (var text in texts)
                {
                    var (houses, settings) = StitchDump.Read(text);
                    foreach (var kv in knobs) { settings.Extra ??= new Dictionary<string, float>(); settings.Extra[kv.Key] = kv.Value; }
                    Fix(houses, garden, fences);
                    var plan = YardStitch.Plan(houses, settings);
                    Keep(houses, plan, garden, fences);
                    foreach (var h in houses) { if (!lastSeen.ContainsKey(h.Id) || h.Edit) lastSeen[h.Id] = h; if (h.Edit) lastSettings[h.Id] = settings; }
                }

            // ── the bulldozer ──
            var polished = lastSeen.Values.Where(h => h.Polished && garden.ContainsKey(h.Id)).ToList();
            var gone = at.HasValue ? polished.OrderBy(h => math.distancesq(h.Position, at.Value)).First() : polished[polished.Count / 2];
            var goneLand = garden[gone.Id];
            var goneLot = Lot(gone);
            var dirty = polished.Where(h => h.Id != gone.Id && RingGap(garden[h.Id], goneLand) <= 2.5f).Select(h => h.Id).ToHashSet();
            var before = Snapshot(garden, fences);
            var cluster = lastSeen.Values.Where(h => h.Id != gone.Id && math.distance(h.Position, gone.Position) < 140f).Select(Copy).ToList();
            foreach (var h in cluster) h.Edit = dirty.Contains(h.Id);
            Fix(cluster, garden, fences);
            var s = lastSettings.TryGetValue(gone.Id, out var gs) ? gs : StitchDump.Read(texts[^1]).Settings;
            s = WithoutHouse(s, goneLot);
            garden.Remove(gone.Id);
            fences.Remove(gone.Id);
            var plan2 = YardStitch.Plan(cluster, s);
            Keep(cluster, plan2, garden, fences);
            var after = Snapshot(garden, fences);

            // ── what the new fences do ──
            var sb = new StringBuilder();
            sb.Append($"bulldozed {gone.Id} at {F(gone.Position.x)},{F(gone.Position.y)} ({gone.HalfLot.x * 2:F0} x {gone.HalfLot.y * 2:F0} m); {dirty.Count} gardens touching it planned again: {string.Join(", ", dirty)}\n");
            foreach (var id in dirty)
            {
                var (pieces, turns, back, maxTurn) = Runs(fences.TryGetValue(id, out var f) ? f : new List<(float2, float2, string, bool)>());
                var (p0, t0, b0, _) = Runs(before.Fences.TryGetValue(id, out var f0) ? f0 : new List<(float2, float2, string, bool)>());
                sb.Append($"  {id}: garden {before.Gardens.GetValueOrDefault(id)?.Length ?? 0} -> {garden.GetValueOrDefault(id)?.Length ?? 0} corners; fence pieces {p0} -> {pieces}; turns over 20 deg between pieces of a run {t0} -> {turns} (most {maxTurn:F0}); pieces doubled back {b0} -> {back}\n");
            }
            var bits = new List<Fences.Bit>();
            foreach (var kv in fences) foreach (var (a, b, name, piece) in kv.Value) bits.Add(new Fences.Bit { House = kv.Key, A = a, B = b, Piece = piece, W = Fences.W(name) });
            Console.Write(sb.ToString());
            File.WriteAllText(Path.Combine(outDir, "bulldoze.txt"), sb.ToString());

            // ── the pictures ──
            float2 c = gone.Position, lo = c - size * 0.5f;
            const float S = 4f;
            string P(float2 p) => F((p.x - lo.x) * S) + "," + F((lo.y + size - p.y) * S);
            string Svg((Dictionary<long, float2[]> Gardens, Dictionary<long, List<(float2 A, float2 B, string Name, bool Piece)>> Fences) st, bool afterOne, string title)
            {
                var o = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size * S:F0}\" height=\"{size * S + 28:F0}\"><rect width=\"100%\" height=\"100%\" fill=\"#e9e4d8\"/>");
                foreach (var b in s.Barriers ?? new List<float2[]>()) o.Append($"<polygon points=\"{string.Join(" ", b.Select(P))}\" fill=\"#9a9a9a\"/>");
                foreach (var kv in st.Gardens) o.Append($"<polygon points=\"{string.Join(" ", kv.Value.Select(P))}\" fill=\"#b5dc95\" stroke=\"#4d7a36\" stroke-width=\"1.5\"/>");
                foreach (var h in lastSeen.Values) if (!afterOne || h.Id != gone.Id) o.Append($"<polygon points=\"{string.Join(" ", Lot(h).Select(P))}\" fill=\"#3f7a2c\" fill-opacity=\"0.85\"/>");
                if (afterOne) o.Append($"<polygon points=\"{string.Join(" ", goneLot.Select(P))}\" fill=\"none\" stroke=\"#000\" stroke-width=\"2\" stroke-dasharray=\"8 5\"/>");
                foreach (var kv in st.Fences)
                    foreach (var (a, b, name, piece) in kv.Value)
                    {
                        bool mine = afterOne && dirty.Contains(kv.Key);
                        string col = mine ? "#e0201b" : name != null && name.IndexOf("Hedge", StringComparison.OrdinalIgnoreCase) >= 0 ? "#14451a" : "#7a4a12";
                        o.Append($"<line x1=\"{F((a.x - lo.x) * S)}\" y1=\"{F((lo.y + size - a.y) * S)}\" x2=\"{F((b.x - lo.x) * S)}\" y2=\"{F((lo.y + size - b.y) * S)}\" stroke=\"{col}\" stroke-width=\"{(mine ? 3.5 : 2.5)}\" stroke-linecap=\"round\"/>");
                    }
                o.Append($"<text x=\"8\" y=\"{size * S + 20:F0}\" font-family=\"system-ui\" font-size=\"16\">{title}</text></svg>");
                return o.ToString();
            }
            var html = "<html><body style=\"margin:0;display:flex;gap:8px;background:#fff\">" + Svg(before, false, "before") + Svg(after, true, "after the bulldozer (the planned gardens' fences red)") + "</body></html>";
            var file = Path.Combine(outDir, "bulldoze.html");
            File.WriteAllText(file, html);
            Console.WriteLine(file);
            return 0;
        }

        private static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        private static void Fix(IEnumerable<StitchHouse> houses, Dictionary<long, float2[]> garden, Dictionary<long, List<(float2 A, float2 B, string Name, bool Piece)>> fences)
        {
            foreach (var h in houses)
            {
                if (h.Edit || !h.Polished || !garden.TryGetValue(h.Id, out var g)) continue;
                h.Garden = g;
                h.Standing = fences.TryGetValue(h.Id, out var f) ? f.Select(x => (x.A, x.B)).ToList() : new List<(float2, float2)>();
            }
        }

        private static void Keep(IEnumerable<StitchHouse> houses, StitchPlan plan, Dictionary<long, float2[]> garden, Dictionary<long, List<(float2 A, float2 B, string Name, bool Piece)>> fences)
        {
            foreach (var h in houses)
            {
                if (!h.Edit || !h.Polished) continue;
                if (plan.Plots.TryGetValue(h.Id, out var p) && p != null && p.Length >= 3) garden[h.Id] = p;
                var mine = new List<(float2, float2, string, bool)>();
                foreach (var l in h.Lines)
                {
                    if (plan.HideLines.Contains(l.Id)) continue;
                    var (a, b) = plan.MoveLines.TryGetValue(l.Id, out var mv) ? mv : (l.A, l.B);
                    if (math.distance(a, b) >= 0.3f) mine.Add((a, b, l.Name, false));
                }
                fences[h.Id] = mine;
            }
            foreach (var (house, at, yaw, name) in plan.FencePieces)
            {
                if (!fences.TryGetValue(house, out var list)) fences[house] = list = new List<(float2, float2, string, bool)>();
                var d = new float2(math.sin(yaw), math.cos(yaw)) * ((name != null && name.Contains("Short") ? 2f : 4f) * 0.5f);
                list.Add((at - d, at + d, name, true));
            }
        }

        private static (Dictionary<long, float2[]> Gardens, Dictionary<long, List<(float2 A, float2 B, string Name, bool Piece)>> Fences) Snapshot(Dictionary<long, float2[]> g, Dictionary<long, List<(float2 A, float2 B, string Name, bool Piece)>> f)
            => (new Dictionary<long, float2[]>(g), f.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()));

        private static StitchHouse Copy(StitchHouse h) => new StitchHouse
        {
            Id = h.Id, Position = h.Position, Forward = h.Forward, HalfLot = h.HalfLot, Lines = h.Lines, Lawns = h.Lawns, Hard = h.Hard, Footprint = h.Footprint,
            Props = h.Props, Edit = h.Edit, Garden = h.Garden, Standing = h.Standing, Polished = h.Polished, MainSurface = h.MainSurface, Zone = h.Zone, Theme = h.Theme, Options = h.Options,
        };

        private static float2[] Lot(StitchHouse h)
        {
            var r = h.Right; var f = h.Forward; var a = h.HalfLot;
            return new[] { h.Position - r * a.x - f * a.y, h.Position + r * a.x - f * a.y, h.Position + r * a.x + f * a.y, h.Position - r * a.x + f * a.y };
        }

        /// <summary>The settings without what went with the house: building barriers on its lot, path pieces within 10 m of it.</summary>
        private static StitchSettings WithoutHouse(StitchSettings s, float2[] lot)
        {
            if (s.Barriers == null) return s;
            var keep = new List<int>();
            for (int k = 0; k < s.Barriers.Count; k++)
            {
                var b = s.Barriers[k];
                bool gone = b != null && b.Length >= 3 && b.All(p => DistToRing(p, lot) <= 10f) && math.abs(Area(b)) < 400f;
                if (!gone) keep.Add(k);
            }
            var n = new StitchSettings
            {
                MaxFrontGap = s.MaxFrontGap, MaxBackGap = s.MaxBackGap, MaxFrontOffset = s.MaxFrontOffset, EdgeTolerance = s.EdgeTolerance, TouchingGap = s.TouchingGap,
                LawnContact = s.LawnContact, Extra = s.Extra, Barriers = keep.Select(k => s.Barriers[k]).ToList(),
                BarrierKinds = s.BarrierKinds == null ? null : keep.Select(k => s.BarrierKinds[k]).ToList(),
                BarrierGroups = s.BarrierGroups == null ? null : keep.Select(k => s.BarrierGroups[k]).ToList(),
                BarrierHeights = s.BarrierHeights == null ? null : keep.Select(k => s.BarrierHeights[k]).ToList(),
            };
            return n;
        }

        private static float Area(float2[] r) { float a = 0; for (int k = 0; k < r.Length; k++) { var p = r[k]; var q = r[(k + 1) % r.Length]; a += p.x * q.y - q.x * p.y; } return a * 0.5f; }

        private static float DistToRing(float2 p, float2[] r)
        {
            bool inside = false;
            for (int i = 0, j = r.Length - 1; i < r.Length; j = i++)
                if ((r[i].y > p.y) != (r[j].y > p.y) && p.x < (r[j].x - r[i].x) * (p.y - r[i].y) / (r[j].y - r[i].y) + r[i].x) inside = !inside;
            if (inside) return 0f;
            float best = float.MaxValue;
            for (int k = 0; k < r.Length; k++)
            {
                var a = r[k]; var ab = r[(k + 1) % r.Length] - a;
                best = math.min(best, math.distance(p, a + ab * math.saturate(math.dot(p - a, ab) / math.max(1e-6f, math.lengthsq(ab)))));
            }
            return best;
        }

        private static float RingGap(float2[] a, float2[] b) => math.min(a.Min(p => DistToRing(p, b)), b.Min(p => DistToRing(p, a)));

        /// <summary>A house's fences in runs (pieces end to end within 0.6 m): pieces, turns over 20 degrees between two
        /// pieces of a run, pieces that double back on the one before (a hairpin), the biggest turn.</summary>
        private static (int Pieces, int Turns, int Back, float MaxTurn) Runs(List<(float2 A, float2 B, string Name, bool Piece)> f)
        {
            int turns = 0, back = 0; float most = 0f;
            for (int i = 0; i < f.Count; i++)
                for (int j = 0; j < f.Count; j++)
                {
                    if (i == j) continue;
                    var (a1, b1, _, _) = f[i]; var (a2, b2, _, _) = f[j];
                    if (math.distance(b1, a2) > 0.6f && math.distance(b1, b2) > 0.6f) continue;
                    var d1 = math.normalizesafe(b1 - a1);
                    var d2 = math.distance(b1, a2) <= 0.6f ? math.normalizesafe(b2 - a2) : math.normalizesafe(a2 - b2);
                    float turn = math.degrees(math.acos(math.clamp(math.dot(d1, d2), -1f, 1f)));
                    if (i < j || math.distance(b1, a2) > 0.6f) { if (turn > 20f) turns++; if (turn > 150f) back++; most = math.max(most, turn); }
                }
            return (f.Count, turns, back, most);
        }
    }
}
