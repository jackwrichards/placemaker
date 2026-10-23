// Placemaker neighbourhood polish — one plan's input written down exactly, and read back (GPL-3.0).
//
// The game runner's lab op "polishPassDump" writes every pass the game plans (its houses, settings, barriers and ground) to
// a file; Tests/Placemaker.Tests/OutgrowBench replays them offline, times YardStitch.Plan and checks that a faster planner
// plans exactly the same (StitchDump.Signature). Floats are written as the doubles they are, so a replay sees the very same
// numbers the game did.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Placemaker.Core.Tool;
using Unity.Mathematics;

namespace Placemaker.Core.Neighbours
{
    public static class StitchDump
    {
        public static string Write(IReadOnlyList<StitchHouse> houses, StitchSettings s, HeightRaster ground)
        {
            JsonValue P(float2 p) => JsonValue.Arr().Add(p.x).Add(p.y);
            JsonValue Ring(IReadOnlyList<float2> r)
            {
                if (r == null) return JsonValue.Null;
                var a = JsonValue.Arr();
                foreach (var p in r) a.Add(P(p));
                return a;
            }
            var hs = JsonValue.Arr();
            foreach (var h in houses)
            {
                var o = JsonValue.Obj().Set("id", h.Id.ToString(CultureInfo.InvariantCulture)).Set("pos", P(h.Position)).Set("fwd", P(h.Forward)).Set("half", P(h.HalfLot));
                var lines = JsonValue.Arr();
                foreach (var l in h.Lines) lines.Add(JsonValue.Obj().Set("id", l.Id.ToString(CultureInfo.InvariantCulture)).Set("name", l.Name ?? "").Set("a", P(l.A)).Set("b", P(l.B)));
                o.Set("lines", lines);
                var lawns = JsonValue.Arr();
                foreach (var l in h.Lawns) lawns.Add(JsonValue.Obj().Set("id", l.Id.ToString(CultureInfo.InvariantCulture)).Set("ring", Ring(l.Ring)));
                o.Set("lawns", lawns);
                var hard = JsonValue.Arr();
                foreach (var r in h.Hard) hard.Add(Ring(r));
                o.Set("hard", hard);
                o.Set("footprint", Ring(h.Footprint));
                var props = JsonValue.Arr();
                foreach (var r in h.Props) props.Add(Ring(r));
                o.Set("props", props);
                o.Set("edit", h.Edit).Set("polished", h.Polished);
                if (h.Garden != null) o.Set("garden", Ring(h.Garden));
                if (h.Standing != null)
                {
                    var st = JsonValue.Arr();
                    foreach (var (a, b) in h.Standing) st.Add(JsonValue.Arr().Add(P(a)).Add(P(b)));
                    o.Set("standing", st);
                }
                if (h.MainSurface != null) o.Set("main", h.MainSurface);
                if (h.Zone != null) o.Set("zone", h.Zone);
                if (h.Theme != null) o.Set("theme", h.Theme);
                if (h.Options != null)
                {
                    var op = JsonValue.Obj();
                    foreach (var kv in h.Options.OrderBy(k => k.Key, StringComparer.Ordinal)) op.Set(kv.Key, kv.Value);
                    o.Set("options", op);
                }
                hs.Add(o);
            }
            var set = JsonValue.Obj().Set("maxFrontGap", s.MaxFrontGap).Set("maxBackGap", s.MaxBackGap).Set("maxFrontOffset", s.MaxFrontOffset)
                .Set("edgeTolerance", s.EdgeTolerance).Set("touchingGap", s.TouchingGap).Set("lawnContact", s.LawnContact);
            var extra = JsonValue.Obj();
            if (s.Extra != null) foreach (var kv in s.Extra.OrderBy(k => k.Key, StringComparer.Ordinal)) extra.Set(kv.Key, kv.Value);
            set.Set("extra", extra);
            if (s.Barriers != null)
            {
                var bs = JsonValue.Arr();
                foreach (var r in s.Barriers) bs.Add(Ring(r));
                set.Set("barriers", bs);
                if (s.BarrierKinds != null) { var ks = JsonValue.Arr(); foreach (var k in s.BarrierKinds) ks.Add(k); set.Set("kinds", ks); }
                if (s.BarrierGroups != null) { var gs = JsonValue.Arr(); foreach (var g in s.BarrierGroups) gs.Add(g.ToString(CultureInfo.InvariantCulture)); set.Set("groups", gs); }
                if (s.BarrierHeights != null) { var hts = JsonValue.Arr(); foreach (var h in s.BarrierHeights) hts.Add(h); set.Set("heights", hts); }
            }
            var doc = JsonValue.Obj().Set("houses", hs).Set("settings", set);
            if (ground != null)
            {
                var h = JsonValue.Arr();
                foreach (var v in ground.H) h.Add(v);
                doc.Set("ground", JsonValue.Obj().Set("min", P(ground.Min)).Set("cell", ground.Cell).Set("nx", ground.Nx).Set("ny", ground.Ny).Set("h", h));
            }
            return doc.Write(false);
        }

        public static (List<StitchHouse> Houses, StitchSettings Settings) Read(string json)
        {
            var doc = JsonValue.Parse(json);
            float F(JsonValue v) => (float)v.AsNumber();
            float2 P(JsonValue v) => new float2(F(v.Items[0]), F(v.Items[1]));
            float2[] Ring(JsonValue v) => v.IsArray ? v.Items.Select(P).ToArray() : null;
            long L(JsonValue v) => v.Kind == JsonKind.String ? long.Parse(v.AsString(), CultureInfo.InvariantCulture) : (long)v.AsNumber();
            var houses = new List<StitchHouse>();
            foreach (var o in doc["houses"].Items)
            {
                var h = new StitchHouse { Id = L(o["id"]), Position = P(o["pos"]), Forward = P(o["fwd"]), HalfLot = P(o["half"]) };
                foreach (var l in o["lines"].Items) h.Lines.Add(new StitchLine { Id = L(l["id"]), Name = l["name"].AsString(), A = P(l["a"]), B = P(l["b"]) });
                foreach (var l in o["lawns"].Items) h.Lawns.Add(new StitchLawn { Id = L(l["id"]), Ring = Ring(l["ring"]) });
                foreach (var r in o["hard"].Items) h.Hard.Add(Ring(r));
                h.Footprint = Ring(o["footprint"]);
                foreach (var r in o["props"].Items) h.Props.Add(Ring(r));
                h.Edit = o["edit"].AsBool(true);
                h.Polished = o["polished"].AsBool(true);
                if (o.Has("garden")) h.Garden = Ring(o["garden"]);
                if (o.Has("standing")) h.Standing = o["standing"].Items.Select(x => (P(x.Items[0]), P(x.Items[1]))).ToList();
                if (o.Has("main")) h.MainSurface = o["main"].AsString();
                if (o.Has("zone")) h.Zone = o["zone"].AsString();
                if (o.Has("theme")) h.Theme = o["theme"].AsString();
                if (o.Has("options")) h.Options = o["options"].Properties.ToDictionary(kv => kv.Key, kv => F(kv.Value));
                houses.Add(h);
            }
            var set = doc["settings"];
            var s = new StitchSettings
            {
                MaxFrontGap = F(set["maxFrontGap"]), MaxBackGap = F(set["maxBackGap"]), MaxFrontOffset = F(set["maxFrontOffset"]),
                EdgeTolerance = F(set["edgeTolerance"]), TouchingGap = F(set["touchingGap"]), LawnContact = F(set["lawnContact"]),
                Extra = set["extra"].Properties.ToDictionary(kv => kv.Key, kv => F(kv.Value)),
            };
            if (set.Has("barriers"))
            {
                s.Barriers = set["barriers"].Items.Select(Ring).ToList();
                if (set.Has("kinds")) s.BarrierKinds = set["kinds"].Items.Select(k => (int)k.AsNumber()).ToList();
                if (set.Has("groups")) s.BarrierGroups = set["groups"].Items.Select(L).ToList();
                if (set.Has("heights")) s.BarrierHeights = set["heights"].Items.Select(F).ToList();
            }
            if (doc.Has("ground"))
            {
                var g = doc["ground"];
                var raster = new HeightRaster(P(g["min"]), F(g["cell"]), (int)g["nx"].AsNumber(), (int)g["ny"].AsNumber(), g["h"].Items.Select(F).ToArray());
                s.Ground = raster.Sample;
            }
            return (houses, s);
        }

        /// <summary>
        /// Everything a plan decides (not its notes, which carry timings), in a fixed order with every coordinate to the
        /// millimetre: two plans with the same signature are the same plan.
        /// </summary>
        /// <summary>
        /// What the game writes of a plan: the houses it edits (their lines moved or hidden, lawns, plots, beds, bushes, fence
        /// pieces, paths, new lawns), the gardens over a lot it found, the shared greens and the ground paint. A plan of the
        /// neighbours that are not edited is never written (NeighbourPolishSystem.FinishPlan: the core only), so a planner that
        /// skips them plans the same when this is the same.
        /// </summary>
        public static string EditedSignature(StitchPlan plan, IReadOnlyList<StitchHouse> houses)
        {
            var edited = new HashSet<long>(houses.Where(h => h.Edit).Select(h => h.Id));
            var lineOf = new Dictionary<long, long>();
            var lawnOf = new Dictionary<long, long>();
            foreach (var h in houses)
            {
                foreach (var l in h.Lines) lineOf[l.Id] = h.Id;
                foreach (var l in h.Lawns) lawnOf[l.Id] = h.Id;
            }
            bool Mine(long house) => edited.Contains(house);
            bool MyLine(long line) => lineOf.TryGetValue(line, out var h) && edited.Contains(h);
            var sb = new StringBuilder();
            void N(float v) => sb.Append(Math.Round(v, 3).ToString("0.000", CultureInfo.InvariantCulture)).Append(' ');
            void P(float2 p) { N(p.x); N(p.y); }
            void R(IReadOnlyList<float2> r) { sb.Append('['); if (r != null) foreach (var p in r) P(p); sb.Append("] "); }
            sb.Append("hide ").Append(string.Join(" ", plan.HideLines.Where(MyLine).OrderBy(x => x))).Append('\n');
            sb.Append("move\n");
            foreach (var kv in plan.MoveLines.Where(k => MyLine(k.Key)).OrderBy(k => k.Key)) { sb.Append(kv.Key).Append(' '); P(kv.Value.A); P(kv.Value.B); sb.Append('\n'); }
            sb.Append("lawns\n");
            foreach (var kv in plan.Lawns.Where(k => lawnOf.TryGetValue(k.Key, out var h) && edited.Contains(h)).OrderBy(k => k.Key)) { sb.Append(kv.Key).Append(' '); R(kv.Value); sb.Append('\n'); }
            sb.Append("plots\n");
            foreach (var kv in plan.Plots.Where(k => Mine(k.Key)).OrderBy(k => k.Key)) { sb.Append(kv.Key).Append(' '); R(kv.Value); sb.Append('\n'); }
            sb.Append("clashes ").Append(string.Join(" ", plan.GardenClashes.OrderBy(x => x))).Append('\n');
            sb.Append("newLawns\n");
            foreach (var (house, ring) in plan.NewLawns.Where(x => Mine(x.Item1))) { sb.Append(house).Append(' '); R(ring); sb.Append('\n'); }
            sb.Append("beds\n");
            foreach (var (house, ring) in plan.Beds.Where(x => Mine(x.Item1))) { sb.Append(house).Append(' '); R(ring); sb.Append('\n'); }
            sb.Append("bushes\n");
            foreach (var (house, at, yaw, kind) in plan.Bushes.Where(x => Mine(x.Item1))) { sb.Append(house).Append(' '); P(at); N(yaw); sb.Append(kind).Append('\n'); }
            sb.Append("pieces\n");
            foreach (var (house, at, yaw, name) in plan.FencePieces.Where(x => Mine(x.Item1))) { sb.Append(house).Append(' '); P(at); N(yaw); sb.Append(name).Append('\n'); }
            sb.Append("paths\n");
            foreach (var (house, ring, surface) in plan.Paths.Where(x => Mine(x.Item1))) { sb.Append(house).Append(' ').Append(surface).Append(' '); R(ring); sb.Append('\n'); }
            sb.Append("commons\n");
            foreach (var ring in plan.Commons) { R(ring); sb.Append('\n'); }
            sb.Append("paints\n");
            foreach (var pt in plan.Paints)
            {
                sb.Append(pt.Layer).Append(' '); N(pt.Strength); N(pt.Feather); N(pt.Noise); N(pt.NoiseScale); sb.Append(pt.Seed).Append(' ');
                sb.Append('['); foreach (var q in pt.Ring) { N(q.x); N(q.z); } sb.Append("]\n");
            }
            return sb.ToString();
        }

        public static string Signature(StitchPlan plan)
        {
            var sb = new StringBuilder();
            void N(float v) => sb.Append(Math.Round(v, 3).ToString("0.000", CultureInfo.InvariantCulture)).Append(' ');
            void P(float2 p) { N(p.x); N(p.y); }
            void R(IReadOnlyList<float2> r) { sb.Append('['); if (r != null) foreach (var p in r) P(p); sb.Append("] "); }
            sb.Append("pairs\n");
            foreach (var p in plan.Pairs) { sb.Append($"{p.Left} {p.Right} {p.Rear} {p.KeptLine} {p.Edge} {p.Reverted} "); P(p.Front); P(p.Back); N(p.FrontGap); N(p.BackGap); R(p.Gap); sb.Append(p.Tip.HasValue ? "tip " : ""); sb.Append('\n'); }
            sb.Append("hide ").Append(string.Join(" ", plan.HideLines.OrderBy(x => x))).Append('\n');
            sb.Append("move\n");
            foreach (var kv in plan.MoveLines.OrderBy(k => k.Key)) { sb.Append(kv.Key).Append(' '); P(kv.Value.A); P(kv.Value.B); sb.Append('\n'); }
            sb.Append("lawns\n");
            foreach (var kv in plan.Lawns.OrderBy(k => k.Key)) { sb.Append(kv.Key).Append(' '); R(kv.Value); sb.Append('\n'); }
            sb.Append("plots\n");
            foreach (var kv in plan.Plots.OrderBy(k => k.Key)) { sb.Append(kv.Key).Append(' '); R(kv.Value); sb.Append('\n'); }
            sb.Append("clashes ").Append(string.Join(" ", plan.GardenClashes.OrderBy(x => x))).Append('\n');
            sb.Append("newLawns\n");
            foreach (var (house, ring) in plan.NewLawns) { sb.Append(house).Append(' '); R(ring); sb.Append('\n'); }
            sb.Append("beds\n");
            foreach (var (house, ring) in plan.Beds) { sb.Append(house).Append(' '); R(ring); sb.Append('\n'); }
            sb.Append("bushes\n");
            foreach (var (house, at, yaw, kind) in plan.Bushes) { sb.Append(house).Append(' '); P(at); N(yaw); sb.Append(kind).Append('\n'); }
            sb.Append("pieces\n");
            foreach (var (house, at, yaw, name) in plan.FencePieces) { sb.Append(house).Append(' '); P(at); N(yaw); sb.Append(name).Append('\n'); }
            sb.Append("paths\n");
            foreach (var (house, ring, surface) in plan.Paths) { sb.Append(house).Append(' ').Append(surface).Append(' '); R(ring); sb.Append('\n'); }
            sb.Append("commons\n");
            foreach (var ring in plan.Commons) { R(ring); sb.Append('\n'); }
            sb.Append("paints\n");
            foreach (var pt in plan.Paints)
            {
                sb.Append(pt.Layer).Append(' '); N(pt.Strength); N(pt.Feather); N(pt.Noise); N(pt.NoiseScale); sb.Append(pt.Seed).Append(' ');
                sb.Append('['); foreach (var q in pt.Ring) { N(q.x); N(q.z); } sb.Append("]\n");
            }
            N(plan.LawnAddedM2);
            return sb.ToString();
        }
    }
}
