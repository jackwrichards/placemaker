// Phase 1 (2026-10-30): every control the panel shows must change what gets built.
//
// The user: "in the parameters I see pads is off, but I'm seeing pads on the ground ... I changed the small pads from 0 to 8.
// I don't see any difference ... it's a problem across basically everything." This test plans every place type of the shelf
// (its Standard, as picking it gives it) on one site, then changes each shown control (a switch flipped, another choice, a
// slider to its other end) and plans again. A control whose change leaves the plan the same is dead: fix it, or hide it
// (Core/Tool/ControlsShown), or grey it out with a reason where it only works in some places (Core/Tool/Availability).
// The few that only act on land this flat test site does not have are listed in Conditional, with why.
// The full list of what was found goes to .forensics/controls/dead-controls.txt.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Placemaker.Core;
using Placemaker.Core.Tool;
using Unity.Mathematics;
using Xunit;
using Xunit.Abstractions;

namespace Placemaker.Tests
{
    public class ControlEffectTests
    {
        private readonly ITestOutputHelper _out;
        public ControlEffectTests(ITestOutputHelper output) { _out = output; }

        private static readonly DesignEditor Editor = new DesignEditor(GeneratorRegistry.Get);
        /// <summary>The test land: a flat rectangle with a street along its bottom edge, sized to the type (big types need room).</summary>
        public readonly struct Land
        {
            public readonly float W, H;
            /// <summary>Rich land: L-shaped, rising to the north with rolling hills, open water along its north edge.</summary>
            public readonly bool Rich;
            public Land(float w, float h, bool rich = false) { W = w; H = h; Rich = rich; }
        }

        public static Land LandFor(IAreaGenerator g)
        {
            switch (GeneratorGroups.Of(g))
            {
                case "parking": case "squares": case "transport": return new Land(150f, 110f);
                default: return new Land(220f, 160f);
            }
        }

        public static readonly Land Rich = new Land(300f, 300f, true);

        private static float3[] Ring(Land l) => l.Rich ? TestKit.LShape(l.W) : TestKit.Rect(l.W, l.H);

        /// <summary>
        /// Controls that act only where this flat, dry, empty test site gives them nothing to act on ("type.control" → why). Each
        /// must be shown greyed out with that reason in the game (Availability) or be truly situational.
        /// </summary>
        private static readonly Dictionary<string, string> Conditional = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // street shops snap their depth to the city's zone cells; the test land has no zone grid
            ["park.shopDepth"] = "zone grid", ["sportsPark.shopDepth"] = "zone grid", ["greenwayPark.shopDepth"] = "zone grid",
            ["amphitheatre.shopDepth"] = "zone grid", ["urbanRuins.shopDepth"] = "zone grid", ["parking.zoningDepth"] = "zone grid",
            // (P2-4) multiplies the facility counts only on a site bigger than a 4.5 ha block; the test land is smaller
            ["sportsPark.growWithSite"] = "big sites only",
            // the bike route starts at a bike entrance the player places (the suggested entrances have none)
            ["park.bikePath"] = "needs a bike entrance",
            // the fee is charged by the game on the built lot's parking lanes (ParkingEconomySystem), not drawn
            ["parking.fee"] = "a game setting of the built lot",
            // district-scale road layouts: their spacing and loops need more land than either test land
            ["roads.connectTo"] = "district scale", ["roads.loopAlignTerrain"] = "district scale", ["roads.loopAngle"] = "district scale",
            ["roads.loopCulDeSacDepth"] = "district scale", ["roads.loopCulDeSacRatio"] = "district scale", ["roads.mixedAvenues"] = "district scale",
            ["roads.mixedCoreRadius"] = "district scale", ["roads.organicLoops"] = "district scale", ["roads.superblockAlignTerrain"] = "district scale",
            ["roads.terraceLinkSpacing"] = "district scale", ["roads.treeBranchSpacing"] = "district scale", ["roads.treeCulDeSacLength"] = "district scale",
            ["roads.treeCulDeSacSpacing"] = "district scale",
            // water types: they need a long shore of deep water (the rich land's lake edge is short and shallow)
            ["beach.bay"] = "long deep shore", ["beach.benchSpacing"] = "long deep shore", ["waterfront.bay"] = "long deep shore",
            ["waterfront.maxWall"] = "long deep shore", ["waterfront.moorings"] = "long deep shore", ["waterfront.nautical"] = "long deep shore",
            ["waterfront.railings"] = "long deep shore", ["waterfront.steps"] = "long deep shore",
            ["hikingTrail.bridges"] = "a stream across the trail",
        };

        [Fact]
        public void HiddenControlsStillExist()
        {
            foreach (var (layer, control) in ControlsShown.HiddenIds)
            {
                if (layer == "*") continue;
                var g = GeneratorRegistry.Get(layer);
                Assert.True(g != null, "no type " + layer);
                Assert.True(g.Controls.Any(c => c.Id == control), layer + "." + control + " is hidden but no longer exists");
            }
        }

        /// <summary>The place types of the product's shelf (not hidden there), as the Make row lists them.</summary>
        public static List<string> PlaceTypes()
        {
            var files = new List<KeyValuePair<string, string>>();
            var dir = Path.Combine(TestKit.RepoRoot(), "Recipes", Shelf.FolderName);
            foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                files.Add(new KeyValuePair<string, string>(Path.GetFileName(f), File.ReadAllText(f)));
            var shelf = Shelf.Parse(files);
            var list = new List<string>();
            foreach (var g in GeneratorRegistry.All)
            {
                if (g == null || g.Kind != LayerKind.Fill || GeneratorRegistry.IsHidden(g) || g.Id == "ai") continue;
                string group = GeneratorGroups.Of(g);
                if (group == "zoning" || g.Id.StartsWith("polish", StringComparison.Ordinal)) continue;   // zoning draws its own panel
                if (shelf.IsTypeShelved(g.Id, group)) continue;
                list.Add(g.Id);
            }
            return list;
        }

        private static SiteContext Site(Land l)
        {
            var site = TestKit.RoadAlongBottom(l.W);
            if (!l.Rich) return site;
            float w = l.W;
            site.HeightAt = p => 0.06f * p.y + 2.5f * math.sin(p.x / 37f) * math.cos(p.y / 29f);
            site.WaterDepthAt = p => p.y > w - 40f ? math.min(4f, (p.y - (w - 40f)) * 0.2f) : 0f;
            return site;
        }

        /// <summary>The type as picking it gives it (<paramref name="preset"/> null: its Standard), with its entrances placed.</summary>
        private static Recipe Fresh(IAreaGenerator g, PresetInfo preset, Land land)
        {
            var r = Editor.SingleLayerRecipe(g, 7);
            uint seed = 9;
            if (preset == null) Editor.AddDefaultFeatures(r, () => seed++);
            else
            {
                foreach (var kv in preset.Values.Values) r.Root.Settings.Values[kv.Key] = kv.Value is string[] a ? (string[])a.Clone() : kv.Value;
                foreach (var f in Features.Of(g))
                    if (preset.FeatureOn(f)) r.Root.Children.Add(Editor.FeatureStep(f, seed++, DesignEditor.PresetFeatureValues(preset, f.Layer)));
            }
            return TestKit.Entered(r, Ring(land), Site(land));
        }

        /// <summary>The styles of a type the picker shows (shelved ones left out), Standard first (null).</summary>
        public static List<PresetInfo> Styles(IAreaGenerator g, Shelf shelf)
        {
            var list = new List<PresetInfo> { null };
            foreach (var p in g.Presets ?? Array.Empty<PresetInfo>())
                if (p != null && !shelf.IsStyleShelved(g.Id, GeneratorGroups.Of(g), p.Id)) list.Add(p);
            return list;
        }

        public static Shelf ProductShelf()
        {
            var files = new List<KeyValuePair<string, string>>();
            var dir = Path.Combine(TestKit.RepoRoot(), "Recipes", Shelf.FolderName);
            foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                files.Add(new KeyValuePair<string, string>(Path.GetFileName(f), File.ReadAllText(f)));
            return Shelf.Parse(files);
        }

        private static ComposedPlan Compose(Recipe r, Land land) =>
            Composer.Compose(r, new AreaShape { Ring = Ring(land), Source = AreaSource.Drawn }, Site(land), CancellationToken.None);

        private static string R(float v) => Math.Round(v, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>What the player can see of a plan: every object, path, surface, lot, terraform, paint stroke, zone and decal.</summary>
        public static string Fingerprint(ComposedPlan plan)
        {
            var sb = new StringBuilder();
            foreach (var l in plan.Layers)
            {
                foreach (var o in l.Objects) sb.Append("O").Append((int)o.Role).Append(':').Append(R(o.Position.x)).Append(',').Append(R(o.Position.z)).Append(',').Append(R(o.Yaw))
                    .Append(',').Append(o.Asset.Id).Append('/').Append(o.Asset.Fallbacks == null ? "" : string.Join("|", o.Asset.Fallbacks)).Append('/').Append(o.Category)
                    .Append('/').Append(R(o.Scale)).Append('/').Append(R(o.Elevation)).Append('/').Append(R(o.Pitch)).Append('/').Append(R(o.Roll)).Append('/').Append(o.Age).Append(';');
                foreach (var n in l.Nets) sb.Append("N").Append((int)n.Role).Append(':').Append(R(n.A.x)).Append(',').Append(R(n.A.z)).Append(',').Append(R(n.B.x)).Append(',').Append(R(n.B.z)).Append(',').Append(R(n.D.x)).Append(',').Append(R(n.D.z))
                    .Append(',').Append(R(n.A.y)).Append(',').Append(R(n.D.y)).Append(',').Append(n.FollowTerrain).Append(',').Append(n.Category).Append(',').Append(string.Join("+", n.Upgrades ?? Array.Empty<string>()))
                    .Append(',').Append(n.Asset.Id).Append('/').Append(n.Asset.Fallbacks == null ? "" : string.Join("|", n.Asset.Fallbacks)).Append(';');
                foreach (var s in l.Surfaces) { sb.Append("S").Append((int)s.Role).Append(s.Asset.Id).Append('/').Append(s.Asset.Fallbacks == null ? "" : string.Join("|", s.Asset.Fallbacks)); foreach (var p in s.Ring) sb.Append(R(p.x)).Append(',').Append(R(p.z)).Append(' '); }
                foreach (var a in l.Areas) { sb.Append("A").Append((int)a.Role).Append(a.Asset.Id); foreach (var p in a.Ring) sb.Append(R(p.x)).Append(',').Append(R(p.z)).Append(' '); }
                foreach (var t in l.Terraform) { sb.Append("T").Append((int)t.Kind).Append(':').Append(R(t.TargetHeight)); foreach (var p in t.Ring) sb.Append(R(p.x)).Append(',').Append(R(p.z)).Append(' '); }
                foreach (var p in l.Paint) { sb.Append("P").Append(p.Layer).Append(':').Append(R(p.Strength)); if (p.Ring != null) foreach (var q in p.Ring) sb.Append(R(q.x)).Append(',').Append(R(q.z)).Append(' '); }
                foreach (var z in l.Zoning) sb.Append("Z").Append(z.ZoneId).Append(':').Append(R(z.A.x)).Append(',').Append(R(z.A.z)).Append(',').Append(R(z.C.x)).Append(',').Append(R(z.C.z)).Append(';');
                foreach (var d in l.Decals) sb.Append("D").Append(d.Kind).Append(':').Append(R(d.Center.x)).Append(',').Append(R(d.Center.y)).Append(';');
                sb.Append("I").Append(l.DecalInstances.Count).Append(';');
            }
            if (plan.Weathering != null) sb.Append("W").Append(plan.Weathering.Instances);
            sb.Append("Q").Append(plan.ZoneQuads.Count).Append('/').Append(plan.ZoneCells.Count).Append('/').Append(plan.ZoneBlockers.Count);
            return sb.ToString();
        }

        private static bool Holds(string cond, SettingsBag values)
        {
            if (string.IsNullOrEmpty(cond)) return true;
            foreach (var raw in cond.Split(new[] { "&&", "&", "," }, StringSplitOptions.RemoveEmptyEntries))
            {
                string clause = raw.Trim();
                bool neg = clause.Contains("!=");
                var parts = clause.Split(new[] { neg ? "!=" : "=" }, StringSplitOptions.None);
                string id = parts[0].Trim(), rhs = parts.Length > 1 ? parts[1].Trim() : "true";
                values.Values.TryGetValue(id, out var v);
                string actual = v is string[] a ? string.Join("|", a) : v is bool b ? (b ? "true" : "false") : v is double d ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : v?.ToString() ?? "";
                bool match = rhs.Split('|').Any(alt => alt == actual);
                if (neg == match) return false;
            }
            return true;
        }

        /// <summary>Other values to try for a control, the most telling first.</summary>
        private static IEnumerable<object> Variants(ControlSchema c, object current)
        {
            switch (c.Kind)
            {
                case ControlKind.Toggle:
                    yield return !(current is bool b && b);
                    break;
                case ControlKind.Choice:
                    foreach (var o in c.Options) if (o.Value != (current as string)) yield return o.Value;
                    break;
                case ControlKind.Slider:
                {
                    double v = current is double d ? d : c.Default is double dd ? dd : c.Min;
                    var tries = new List<double> { c.Max, c.Min, (c.Min + c.Max) / 2 };
                    foreach (var t in tries) if (Math.Abs(t - v) > 1e-6) yield return t;
                    break;
                }
            }
        }

        /// <summary>"type.control\tstyle" of every control that did change its plan (the report's second file).</summary>
        public static readonly ConcurrentBag<string> Live = new ConcurrentBag<string>();
        /// <summary>"type.control\tstyle" shown greyed out because the plan never read it.</summary>
        public static readonly ConcurrentBag<string> Greyed = new ConcurrentBag<string>();

        public static readonly ConcurrentDictionary<string, string> Contents = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        public sealed class Finding { public string Type = "", Style = "", Control = "", Kind = "", Group = "", Value = ""; public int Tried; }

        /// <summary>The dead controls of one type (its Standard, on the test site).</summary>
        private static readonly object LibLock = new object();
        private static bool _libLoaded;

        /// <summary>The game's dynamic decal library (Recipes/decals), as the game loads it: without it no decal is laid.</summary>
        public static void LoadDecals()
        {
            lock (LibLock)
            {
                if (_libLoaded) return;
                Placemaker.Core.Decals.Dynamic.DecalLibrary.Current = Placemaker.Core.Decals.Dynamic.DecalLibrary.LoadFolder(
                    Path.Combine(TestKit.RepoRoot(), "Recipes", Placemaker.Core.Decals.Dynamic.DecalLibrary.FolderName));
                _libLoaded = true;
            }
        }

        /// <summary>The dead controls of one style of a type (<paramref name="preset"/> null: its Standard), on the test site.</summary>
        public static List<Finding> DeadControls(string typeId, PresetInfo preset, out string note, Land? landOverride = null, HashSet<string> only = null)
        {
            LoadDecals();
            note = "";
            var found = new List<Finding>();
            var g = GeneratorRegistry.Get(typeId);
            var land = landOverride ?? LandFor(g);
            var recipe = Fresh(g, preset, land);
            string style = preset?.Id ?? "(standard)";
            var first = Compose(recipe, land);
            string a = Fingerprint(first), b = Fingerprint(Compose(recipe, land));
            if (a != b) { note = "plans differ run to run"; return found; }
            var errors = first.AllIssues().Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Key).Distinct().ToList();
            if (errors.Count > 0) { note = "plans with errors on the test site: " + string.Join(", ", errors); return found; }
            if (preset == null) Contents[typeId] = string.Join(" ", first.Layers.Select(l => $"{l.GeneratorId}:o{l.Objects.Count}/n{l.Nets.Count}/s{l.Surfaces.Count}/t{l.Terraform.Count}/p{l.Paint.Count}/d{l.Decals.Count}/i{l.DecalInstances.Count}"))
                + " w" + (first.Weathering?.Instances ?? -1) + " issues: " + string.Join(",", first.AllIssues().Select(i => i.Key).Distinct());
            var values = g.Defaults().Clone();
            foreach (var kv in recipe.Root.Settings.Values) values.Values[kv.Key] = kv.Value;
            Dictionary<string, LocText> unavailable = null;
            try { Availability.For(recipe, new AreaShape { Ring = Ring(land), Source = AreaSource.Drawn }, Site(land), GeneratorRegistry.Get)?.TryGetValue("0", out unavailable); }
            catch (Exception) { unavailable = null; }
            // An add-on's own options show under it only while it is on (the panel's Extras): tested with it on.
            var optionOf = new Dictionary<string, FeatureInfo>(StringComparer.Ordinal);
            foreach (var f in Features.Of(g)) foreach (var o in f.Options ?? Array.Empty<string>()) optionOf[o] = f;
            // A control the plan never read is shown greyed out ("Not used by this style here", ControlsShown.Unused): not tested.
            var firstUnused = new HashSet<string>(ControlsShown.Unused(g, first, "0"), StringComparer.Ordinal);
            var bases = new Dictionary<string, (Recipe recipe, string print, HashSet<string> unused)>(StringComparer.Ordinal);
            (Recipe recipe, string print, HashSet<string> unused) BaseFor(ControlSchema c)
            {
                if (!optionOf.TryGetValue(c.Id, out var f)) return (recipe, a, firstUnused);
                if (Features.ChildIndex(recipe.Root, f) >= 0) return (recipe, a, firstUnused);
                if (bases.TryGetValue(f.Layer, out var hit)) return hit;
                var r = new Recipe { Id = recipe.Id, Root = recipe.Root.Clone() };
                Editor.SetFeature(r, "0", f.Layer, true, 31);
                var plan = Compose(r, land);
                return bases[f.Layer] = (r, Fingerprint(plan), new HashSet<string>(ControlsShown.Unused(g, plan, "0"), StringComparer.Ordinal));
            }
            foreach (var c in g.Controls)
            {
                if (c == null || !ControlsShown.IsShown(g, c)) continue;
                if (only != null && !only.Contains(c.Id)) continue;
                if (c.Kind != ControlKind.Slider && c.Kind != ControlKind.Toggle && c.Kind != ControlKind.Choice) continue;
                if (!Holds(c.VisibleWhen, values) || !Holds(c.EnabledWhen, values)) continue;
                if (unavailable != null && unavailable.ContainsKey(c.Id)) continue;
                values.Values.TryGetValue(c.Id, out var current);
                var (baseRecipe, basePrint, unused) = BaseFor(c);
                if (unused.Contains(c.Id)) { Greyed.Add(typeId + "." + c.Id + "\t" + style); continue; }
                int tried = 0;
                bool changed = false;
                foreach (var v in Variants(c, current).Take(4))
                {
                    var r = new Recipe { Id = baseRecipe.Id, Root = baseRecipe.Root.Clone() };
                    if (!Editor.SetValue(r, "0", c.Id, v, true)) continue;
                    tried++;
                    if (Fingerprint(Compose(r, land)) != basePrint) { changed = true; break; }
                }
                if (changed) Live.Add(typeId + "." + c.Id + "\t" + style);
                if (!changed && tried > 0)
                    found.Add(new Finding { Type = typeId, Style = style, Control = c.Id, Kind = c.Kind.ToString(), Group = c.Group, Value = current?.ToString() ?? "", Tried = tried });
            }
            return found;
        }

        [Fact]
        public void EveryShownControlChangesThePlan()
        {
            var types = PlaceTypes();
            var shelf = ProductShelf();
            var jobs = types.SelectMany(id => Styles(GeneratorRegistry.Get(id), shelf).Select(p => (id, p))).ToList();
            var dead = new ConcurrentBag<Finding>();
            var notes = new ConcurrentBag<string>();
            Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1) }, job =>
            {
                string name = job.id + "/" + (job.p?.Id ?? "standard");
                try
                {
                    var found = DeadControls(job.id, job.p, out var note);
                    foreach (var f in found) dead.Add(f);
                    if (note.Length > 0) notes.Add(name + ": " + note);
                }
                catch (Exception e) { notes.Add(name + ": " + e.GetType().Name + " " + e.Message); }
            });
            var all = dead.OrderBy(f => f.Type, StringComparer.Ordinal).ThenBy(f => f.Control, StringComparer.Ordinal).ThenBy(f => f.Style, StringComparer.Ordinal).ToList();
            // Each dead one again on the rich land (sloped, water along one edge, bigger, L-shaped): a control that acts only
            // there (shore, bridges, grading, terraces) does change the plan where the land asks for it.
            var rich = new ConcurrentBag<string>();
            Parallel.ForEach(all.GroupBy(f => (f.Type, f.Style)).ToList(), new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1) }, grp =>
            {
                var g = GeneratorRegistry.Get(grp.Key.Type);
                var preset = grp.Key.Style == "(standard)" ? null : g.Presets.FirstOrDefault(x => x.Id == grp.Key.Style);
                var ids = new HashSet<string>(grp.Select(f => f.Control), StringComparer.Ordinal);
                try
                {
                    var still = new HashSet<string>(DeadControls(grp.Key.Type, preset, out _, Rich, ids).Select(f => f.Control), StringComparer.Ordinal);
                    foreach (var id in ids) if (!still.Contains(id)) rich.Add(grp.Key.Type + "." + id + "|" + grp.Key.Style);
                }
                catch (Exception e) { notes.Add(grp.Key.Type + "/" + grp.Key.Style + " on the rich land: " + e.GetType().Name + " " + e.Message); }
            });
            var richAlive = new HashSet<string>(rich, StringComparer.Ordinal);
            var sorted = all.Where(f => !richAlive.Contains(f.Type + "." + f.Control + "|" + f.Style)).ToList();
            // Dead in every style it shows in (read by the plan, yet nothing changes on either land): must be fixed or hidden.
            var liveKeys = new HashSet<string>(Live.Select(x => x.Split('\t')[0]).Concat(rich.Select(x => x.Split('|')[0])), StringComparer.Ordinal);
            var everywhere = sorted.Where(f => !liveKeys.Contains(f.Type + "." + f.Control)).ToList();
            var report = new StringBuilder();
            report.AppendLine($"# Shown controls that change nothing: {everywhere.Select(f => f.Type + "." + f.Control).Distinct().Count()} in every style, "
                + $"{sorted.Count - everywhere.Count} (control, style) pairs in some styles only; {jobs.Count} styles of {types.Count} place types, {DateTime.Now:yyyy-MM-dd HH:mm}");
            foreach (var f in sorted)
            {
                string k = f.Type + "." + f.Control;
                string state = Conditional.TryGetValue(k, out var why) ? "conditional: " + why : liveKeys.Contains(k) ? "some styles" : "DEAD";
                report.AppendLine($"{k}\t{f.Style}\t{f.Kind}\t[{f.Group}]\tnow {f.Value}\t{state}");
            }
            foreach (var n in notes.OrderBy(x => x, StringComparer.Ordinal)) report.AppendLine("note " + n);
            foreach (var kv in Contents.OrderBy(x => x.Key, StringComparer.Ordinal)) report.AppendLine("plan " + kv.Key + " " + kv.Value);
            try
            {
                var dir = Path.Combine(TestKit.RepoRoot(), ".forensics", "controls");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "dead-controls.txt"), report.ToString());
                File.WriteAllLines(Path.Combine(dir, "live-controls.txt"), Live.OrderBy(x => x, StringComparer.Ordinal));
                File.WriteAllLines(Path.Combine(dir, "greyed-controls.txt"), Greyed.OrderBy(x => x, StringComparer.Ordinal));
                File.WriteAllLines(Path.Combine(dir, "rich-alive.txt"), rich.OrderBy(x => x, StringComparer.Ordinal));
            }
            catch (Exception) { /* the report is a convenience */ }
            _out.WriteLine(report.ToString());
            var unexpected = everywhere.Where(f => !Conditional.ContainsKey(f.Type + "." + f.Control))
                .Select(f => f.Type + "." + f.Control + " (" + f.Kind + ")").Distinct().ToList();
            Assert.True(unexpected.Count == 0, unexpected.Count + " shown controls change nothing in any style:\n" + string.Join("\n", unexpected));
            Assert.True(notes.IsEmpty, string.Join("\n", notes));
        }
    }
}
