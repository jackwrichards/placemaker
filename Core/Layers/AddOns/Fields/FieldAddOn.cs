// Sports fields (grand park wave, g1): the base of every field add-on and the size tables.
//
// Sizes are real (FIFA / FIBA / ITF / World Athletics / MLB / Little League / MCC numbers) plus the
// run-off and the strip round the field that holds benches, stands and floodlights. Each field has a
// ladder of formats, biggest first (a full pitch → 9-a-side → 7-a-side → 5-a-side…):
//
//  * In a park the field is an amenity place (Amenities.All): the park reserves the first rung of the
//    field's park ladder at the position the player chose, or a smaller rung when it does not fit
//    between the paths ("scaled to fit"), joins it to the paths ("Path to it") and keeps planting out.
//  * Anywhere else (a sports park feature, a recipe) the field finds its own spot with the same solver,
//    from the format the player chose downwards, at the player's position ("at"), optionally turned
//    north–south / east–west, and joins the nearest path with a short spur ("Path to it").
//
// The field then levels its pad (blended apron), lays its surfaces, paints its lines (marking decals)
// and dresses itself (goals, nets, fences, backstops, stands, benches, floodlights).

using System;
using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.AddOns.Fields
{
    /// <summary>One size of a field: id and place size (long side × short side, metres, run-off and dressing included).</summary>
    public sealed class FieldFormat
    {
        public readonly string Id;
        public readonly float Length, Width;
        public FieldFormat(string id, float length, float width) { Id = id; Length = length; Width = width; }
    }

    public static class FieldSpecs
    {
        // 400 m track (84.39 m straights, 36.5 m bends) with 8 or 6 lanes; 300 m school track; 200 m mini track.
        public static readonly FieldFormat[] Track =
        {
            new FieldFormat("standard", 190f, 106f), new FieldFormat("sixLane", 184f, 100f),
            new FieldFormat("school", 143f, 83f), new FieldFormat("mini", 97f, 57f),
        };
        // Boundary semi-axes ≈ 70×63 / 55×49 / 46×41 / 36×33 m round a 20.12 m pitch.
        public static readonly FieldFormat[] Cricket =
        {
            new FieldFormat("county", 154f, 140f), new FieldFormat("club", 124f, 112f),
            new FieldFormat("village", 104f, 94f), new FieldFormat("school", 82f, 76f),
        };
        // Football: 105×68 / 73×46 / 55×37 / 37×27 m playing areas.
        public static readonly FieldFormat[] Pitch =
        {
            new FieldFormat("full", 121f, 86f), new FieldFormat("nine", 87f, 62f),
            new FieldFormat("seven", 67f, 51f), new FieldFormat("five", 47f, 39f),
        };
        // Square places: home plate in one corner, foul lines along two sides, fence at 99 / 80 / 67 / 61 / 46 m.
        public static readonly FieldFormat[] Baseball =
        {
            new FieldFormat("major", 111f, 111f), new FieldFormat("community", 91f, 91f), new FieldFormat("softball", 77f, 77f),
            new FieldFormat("littleLeague", 70f, 70f), new FieldFormat("teeBall", 54f, 54f),
        };
        // Doubles courts 23.77×10.97 m, 6.4 m behind the baselines, 3.66 m at the sides, 4.27 m between courts.
        public static readonly FieldFormat[] Tennis =
        {
            new FieldFormat("four", 66f, 38.6f), new FieldFormat("three", 50.8f, 38.6f),
            new FieldFormat("two", 38.6f, 35.5f), new FieldFormat("one", 38.6f, 20.3f),
        };
        // FIBA 28×15 m, a compact 26×14 m school court.
        public static readonly FieldFormat[] Basketball = { new FieldFormat("full", 34f, 25f), new FieldFormat("compact", 30f, 21f) };
        public static readonly FieldFormat[] Skate = { new FieldFormat("large", 34f, 26f), new FieldFormat("small", 26f, 20f) };

        /// <summary>The fields as park amenities: place = the first rung of the park ladder, smaller rungs when it does not fit.</summary>
        public static readonly AmenitySpec[] All =
        {
            Spec("runningTrack", "centre", Track, 1),
            Spec("cricketGround", "centre", Cricket, 1),
            Spec("pitch", "edge", Pitch, 0),
            Spec("baseballDiamond", "corner", Baseball, 1),
            Spec("tennisCourts", "corner", Tennis, 2),
            Spec("basketballCourt", "edge", Basketball, 0),
            Spec("skateBowl", "edge", Skate, 0),
        };

        private static AmenitySpec Spec(string layer, string at, FieldFormat[] ladder, int first)
        {
            var smaller = new List<float2>();
            for (int i = first + 1; i < ladder.Length; i++) smaller.Add(new float2(ladder[i].Length, ladder[i].Width));
            return new AmenitySpec { Layer = layer, Length = ladder[first].Length, Width = ladder[first].Width, DefaultAt = at, Smaller = smaller.ToArray() };
        }

        public static readonly string[] Ids = { "pitch", "baseballDiamond", "runningTrack", "tennisCourts", "basketballCourt", "cricketGround", "skateBowl" };

        /// <summary>
        /// The fields as features of a host without places for them (sports park): each field finds its own
        /// spot in what the host left free, at its own "Position", with its own "Path to it".
        /// </summary>
        public static List<FeatureInfo> WithFields(List<FeatureInfo> own)
        {
            foreach (var id in Ids)
                own.Add(new FeatureInfo { Layer = id, LabelKey = "Placemaker.Feature." + id, TooltipKey = "Placemaker.Feature." + id + ".Tooltip" });
            return own;
        }
    }

    public abstract class FieldAddOn : AmenityAddOn
    {
        public override string Icon => "Media/Game/Icons/SportsParks.svg";

        public const string SmallerKey = "Placemaker.Field.Issue.Smaller";
        public const string OrientationKey = "Placemaker.Field.Issue.Orientation";
        /// <summary>
        /// Region tag a host adds to a place it sized itself (the sports park: "how many, how big" are its options):
        /// the field then takes the biggest format that fits the place, whatever its own format setting says.
        /// </summary>
        public const string HostSizedTag = "hostSized";

        /// <summary>
        /// (2026-10-29) Region tag a host adds to a place whose spectators it seats itself (a stadium bowl, a terrace of
        /// grandstands, an amphitheatre): the field leaves out its own stands and floodlights.
        /// </summary>
        public const string HostStandsTag = "hostStands";

        /// <summary>(2026-10-29) Region tag of a place the host lights itself (masts round a stadium bowl): no floodlights of the field's own.</summary>
        public const string HostLightsTag = "hostLights";

        /// <summary>(rf-sports) Region tag prefix a host adds to choose the court surface ("courtSurface:clay"); the field's own choice otherwise.</summary>
        public const string SurfaceTagPrefix = "courtSurface:";

        /// <summary>(rf-sports) The surface a host's place asks for, else <paramref name="own"/>.</summary>
        public static string HostSurface(AreaInput input, string own)
        {
            if (input?.RegionTags != null)
                foreach (var t in input.RegionTags)
                    if (t != null && t.StartsWith(SurfaceTagPrefix, StringComparison.Ordinal)) return t.Substring(SurfaceTagPrefix.Length);
            return own;
        }

        /// <summary>The formats of this field (biggest first).</summary>
        protected abstract FieldFormat[] Formats(LayerContext ctx);

        /// <summary>Index of the format the player asked for (0 = the biggest; "auto" = the biggest that fits).</summary>
        protected virtual int Wanted(LayerContext ctx, FieldFormat[] formats)
        {
            string s = ctx.S("format");
            for (int i = 0; i < formats.Length; i++) if (formats[i].Id == s) return i;
            return 0;
        }

        protected virtual bool Auto(LayerContext ctx) => ctx.S("format") == "auto" || ctx.S("format") == "";

        /// <summary>Lays the field out (surfaces, lines into job.Lines, equipment, dressing).</summary>
        protected abstract void Lay(FieldJob job);

        /// <summary>Size of the levelled pad's blend apron for this place.</summary>
        protected virtual float Apron(OrientedRect place) => math.clamp(math.min(place.HalfLength, place.HalfWidth) * 0.12f, 2.5f, 7f);

        // ───────────── shared controls ─────────────

        protected static string[] WithAuto(FieldFormat[] f)
        {
            var r = new string[f.Length + 1];
            r[0] = "auto";
            for (int i = 0; i < f.Length; i++) r[i + 1] = f[i].Id;
            return r;
        }

        /// <summary>Lines group: painted lines and their decal spacing.</summary>
        protected static void LineControls(ControlSet c)
        {
            c.Toggle("lines", true, ControlTier.More, "lines").Tip();
            c.Slider("lineSpacing", 1.5, 0.5, 5, 0.5, "m", ControlTier.More, "lines").When("lines=true").Tip();
        }

        /// <summary>Ground group: levelled pad.</summary>
        protected static void GroundControls(ControlSet c)
        {
            c.Toggle("level", true, ControlTier.More, "ground").Tip();
        }

        /// <summary>Placement group (used when the field finds its own spot, i.e. not in a park's place).</summary>
        protected void PlacementControls(ControlSet c)
        {
            string at = Amenities.Spec(Id)?.DefaultAt ?? "edge";
            c.Choice("at", at, ControlTier.More, "placement", Amenities.Positions).Tip();
            c.Fixed("at");
            c.Choice("orientation", "auto", ControlTier.More, "placement", "auto", "northSouth", "eastWest").Tip();
            c.Fixed("orientation");
            c.Toggle("pathTo", true, ControlTier.More, "placement").Tip();
            c.Fixed("pathTo");
        }

        protected static void LightControls(ControlSet c, string group, bool choice, string def)
        {
            if (choice) c.Choice("floodlights", def, ControlTier.More, group, "none", "four", "six").Tip();
            else c.Toggle("floodlights", def == "true", ControlTier.More, group);
            c.Asset("lightAsset", AssetCategories.FloodLight, ControlTier.More, group).When(choice ? "" : "floodlights=true");
        }

        // ───────────── planning ─────────────

        private bool Hosted(AreaInput input) => input.RegionTags != null && Array.IndexOf(input.RegionTags, Amenities.Place(Id)) >= 0;
        private bool HostSized(AreaInput input) => Hosted(input) && Array.IndexOf(input.RegionTags, HostSizedTag) >= 0;
        private bool HostStands(AreaInput input) => Hosted(input) && Array.IndexOf(input.RegionTags, HostStandsTag) >= 0;
        private bool HostLights(AreaInput input) => Hosted(input) && Array.IndexOf(input.RegionTags, HostLightsTag) >= 0;

        /// <summary>
        /// Where people arrive from, for a field in a host's place: the nearest walk of the whole recipe. The place's own
        /// occupancy only sees what overlaps the place, so the touchline walk beside it would be missed.
        /// </summary>
        private static bool NearestReservedPath(ReservedSpace reserved, float2 p, out float2 onPath)
        {
            onPath = p; float best = float.MaxValue;
            if (reserved == null) return false;
            foreach (var c in reserved.Corridors)
            {
                if (c.Tag != nameof(NetRole.Pathway) && c.Tag != nameof(NetRole.PathwayWide)) continue;
                var q = SegmentIndex.ClosestPoint(p, c.A, c.B);
                float d = math.distancesq(q, p);
                if (d < best) { best = d; onPath = q; }
            }
            return best < 300f * 300f;
        }

        protected override void PlanCore(LayerContext ctx)
        {
            var occ = Occupancy(ctx);
            var region = ctx.LayerRegion;
            OrientedRect site = default;
            if (Hosted(ctx.Input)) site = Amenities.RectOf(region);
            else
            {
                var formats = Formats(ctx);
                int want = Wanted(ctx, formats);
                string at = ctx.S("at");
                if (Array.IndexOf(Amenities.Positions, at) < 0) at = Spec?.DefaultAt ?? "edge";
                var gates = new List<float2>();
                foreach (var m in ctx.Input.Markers) gates.Add(m.Position.xz);
                float room = FieldKit.MaxClearance(region, occ, formats[want].Width * 0.5f + 4f);
                bool found = false;
                for (int k = want; k < formats.Length; k++)
                {
                    var f = formats[k];
                    if (!FieldKit.MayFit(room, f.Length, f.Width)) continue;
                    ctx.Check();
                    if (Amenities.TrySolve(region, occ, f.Length, f.Width, at, gates, Hashing.Mix(ctx.Seed, 0xF1E1u), null, out site, out _, 1f, false))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) { NoSpace(ctx, NameKey); return; }
                string orient = ctx.S("orientation");
                if (orient == "northSouth" || orient == "eastWest")
                {
                    var axis = orient == "northSouth" ? new float2(0, 1) : new float2(1, 0);
                    if (math.abs(math.dot(site.Axis, axis)) < 0.985f)
                    {
                        if (FieldKit.FixedAxis(region, occ, site.Center, axis, site.HalfLength * 2f, site.HalfWidth * 2f, out var turned)) site = turned;
                        else ctx.Issue(IssueSeverity.Info, OrientationKey);
                    }
                }
                // The spur first, so the field's own furniture keeps clear of it.
                if (ctx.B("pathTo")) FieldKit.Spur(ctx, occ, site, AssetMix.One(""));
            }
            var toward = Hosted(ctx.Input) && NearestReservedPath(ctx.Input.Reserved, site.Center, out var onPath) ? onPath : Toward(occ, site);
            Build(ctx, occ, site, toward);
            ctx.Plan.Facts["amenityLength"] = site.HalfLength * 2f;
            ctx.Plan.Facts["amenityWidth"] = site.HalfWidth * 2f;
        }

        protected sealed override void Build(LayerContext ctx, Occupancy occ, OrientedRect site, float2 toward)
        {
            var formats = Formats(ctx);
            bool sized = HostSized(ctx.Input);
            int want = sized ? 0 : Wanted(ctx, formats);
            int k = -1;
            for (int i = want; i < formats.Length; i++)
                if (formats[i].Length <= site.HalfLength * 2f + 0.6f && formats[i].Width <= site.HalfWidth * 2f + 0.6f) { k = i; break; }
            // (rf-sports) A host that sized the place below the smallest format (the sports park's compact track)
            // gets the smallest format laid out to the place: the add-on shrinks to it (a track shrinks its bends).
            if (k < 0 && sized && formats.Length > 0) k = formats.Length - 1;
            if (k < 0) { NoSpace(ctx, NameKey); return; }
            if (k > want && !sized && !Auto(ctx)) ctx.Issue(IssueSeverity.Info, SmallerKey, NameKey);
            var f = formats[k];
            var place = new OrientedRect
            {
                Center = site.Center, Axis = site.Axis,
                HalfLength = math.min(site.HalfLength, f.Length * 0.5f), HalfWidth = math.min(site.HalfWidth, f.Width * 0.5f),
            };
            float apron = Apron(place);
            var pad = new OrientedRect { Center = place.Center, Axis = place.Axis, HalfLength = place.HalfLength - apron, HalfWidth = place.HalfWidth - apron };
            var outer = new OrientedRect { Center = place.Center, Axis = place.Axis, HalfLength = place.HalfLength - 0.3f, HalfWidth = place.HalfWidth - 0.3f };
            var plan = ctx.Plan;
            int o0 = plan.Objects.Count, s0 = plan.Surfaces.Count, n0 = plan.Nets.Count;
            var job = new FieldJob
            {
                Ctx = ctx, Occ = occ, Site = site, Place = place, Toward = toward, Format = f.Id, Seed = Hashing.Mix(ctx.Seed, Seeds.Hash(Id)),
                Ground = FieldGround.Level(ctx, pad, outer, ctx.B("level")),
                HostSized = sized,
                HostStands = HostStands(ctx.Input),
                HostLights = HostLights(ctx.Input),
            };
            Lay(job);
            ctx.Check();
            int decals = ctx.B("lines") ? job.Lines.Emit(ctx, ctx.F("lineSpacing"), job.Seed) : 0;

            // Everything the field placed stands on the levelled ground.
            if (job.Ground.Levelled)
            {
                var g = job.Ground;
                for (int i = o0; i < plan.Objects.Count; i++)
                {
                    var o = plan.Objects[i];
                    o.Position = new float3(o.Position.x, g.HeightAfter(o.Position.xz), o.Position.z);
                }
                for (int i = s0; i < plan.Surfaces.Count; i++)
                {
                    var ring = plan.Surfaces[i].Ring;
                    for (int v = 0; v < ring.Length; v++) ring[v].y = g.HeightAfter(ring[v].xz);
                }
                for (int i = n0; i < plan.Nets.Count; i++)
                {
                    var n = plan.Nets[i];
                    n.A.y = g.HeightAfter(n.A.xz); n.B.y = g.HeightAfter(n.B.xz); n.C.y = g.HeightAfter(n.C.xz); n.D.y = g.HeightAfter(n.D.xz);
                }
                ctx.Stat("Placemaker.Stat.TerrainChange", Math.Round(job.Ground.MaxChange, 1), Keys.UnitM);
            }
            ctx.Stat("Placemaker.Stat.Fields", 1, Keys.UnitCount);
            if (job.Equipment > 0) ctx.Stat("Placemaker.Stat.Equipment", job.Equipment, Keys.UnitCount);
            if (job.Lights > 0) ctx.Stat(Keys.StatLamps, job.Lights, Keys.UnitCount);
            if (job.Stands + job.Benches > 0) ctx.Stat("Placemaker.Stat.Seats", job.Stands + job.Benches, Keys.UnitCount);
            if (decals > 0) ctx.Stat("Placemaker.Stat.Decals", decals, Keys.UnitCount);
            plan.Facts["fieldFormat"] = k;
            plan.Facts["fieldLength"] = place.HalfLength * 2f;
            plan.Facts["fieldWidth"] = place.HalfWidth * 2f;
            plan.Facts["fieldAxisX"] = place.Axis.x;
            plan.Facts["fieldAxisZ"] = place.Axis.y;
            plan.Facts["lineDecals"] = decals;
            plan.Facts["levelPieces"] = job.Ground.Pieces;
            plan.Facts["equipment"] = job.Equipment;
            plan.Facts["floodlights"] = job.Lights;
            plan.Facts["stands"] = job.Stands;
        }
    }
}
