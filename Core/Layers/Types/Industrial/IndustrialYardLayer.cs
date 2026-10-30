// Industrial category (2026-10-19): the base of the handcrafted yards (container yard, car yard, junk yard, industrial
// works). The plan is always the same five steps, and only the middle one belongs to the type:
//
//   1. level the yard (blended back to the ground at the outline) and lay the ground: one surface for the yard, asphalt for
//      the aisles, concrete pads under whatever stands on them;
//   2. the layout (IndustrialSite): frame square to the street, front yard, spine and aisles, storage blocks;
//   3. the type decides what each block is (Assign) and fills it (Fill) with YardFill's exact rows, stacks and bays;
//   4. the front yard (gate, office row, parking), the tidy edge (trees / block wall / container wall), floodlight masts and
//      the fence with its gate gap;
//   5. stats.
//
// No grime scatter, no strays, no "random junk by the fence": the user asked for yards that look designed.

using System.Collections.Generic;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Layers.Leisure;
using Placemaker.Core.Layers.Types.Scrapyard;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    public abstract class IndustrialYardLayer : LeisureLayer
    {
        /// <summary>A vehicle site: plain entrances, never blocks Build.</summary>
        protected override bool TypedEntrances => false;
        protected override bool HostsStreetShops => false;
        public override string[] Emits => new[] { RegionTags.Core };
        public override float MinArea => 1200f;
        protected override int WantedEntrances(LayerRegion region) => 1;

        /// <summary>The type's styles (preset ids double as the style choice).</summary>
        protected abstract string[] Styles { get; }

        protected override List<FeatureInfo> BuildFeatures() => new List<FeatureInfo>
        {
            Feature("decals", ("theme", "dirt"), ("density", 0.6), ("ground", "paved")),
        };

        protected override string[] DefaultFeatures => System.Array.Empty<string>();
        public static bool DebugBlocks;

        protected override void DefineControls(ControlSet c)
        {
            c.Choice("style", Styles[0], ControlTier.Essential, "yard", Styles).Tip();
            c.Slider("fill", 85, 30, 100, 5, "%", ControlTier.Essential, "yard").Tip();
            c.Slider("aisle", 8, 6, 14, 0.5, "m", ControlTier.Essential, "yard").Tip();

            c.Toggle("office", true, ControlTier.More, "front").Tip();
            c.Toggle("parking", true, ControlTier.More, "front").Tip();
            c.Toggle("weighbridge", false, ControlTier.More, "front").Tip();
            c.Toggle("floodlights", true, ControlTier.More, "front");

            c.Choice("edge", "blocks", ControlTier.More, "edge", "trees", "blocks", "containers", "wrecks", "boulders", "plain").Tip();
            c.Choice("fence", "fence", ControlTier.More, "edge", "none", "fence").Tip();
            c.Asset("fenceAsset", AssetCategories.Fence, ControlTier.More, "edge").When("fence=fence");
            c.AssetMulti("treeSpecies", AssetCategories.Tree, ControlTier.More, "edge").When("edge=trees");

            c.Choice("ground", "gravel", ControlTier.More, "ground", "gravel", "concrete", "dirt").Tip();
            c.Toggle("paint", true, ControlTier.More, "ground").Tip();
            c.Toggle("spoil", true, ControlTier.More, "ground").Tip();
            c.Toggle("banks", true, ControlTier.More, "ground").Tip();
            c.Slider("stones", 60, 0, 100, 10, "%", ControlTier.More, "ground").Tip();
            c.Slider("rubble", 50, 0, 100, 10, "%", ControlTier.More, "ground").Tip();
            c.Toggle("level", true, ControlTier.More, "ground").Tip();
            EntranceMarkers(c);

            c.RandomRange("fill", 60, 100);
            c.Fixed("level");
        }

        /// <summary>The style is a scrapyard laid out organically (tracks and a scrap field, OrganicYard), not in blocks.</summary>
        protected virtual bool Organic(string style) => false;

        /// <summary>Layout numbers for a style.</summary>
        protected abstract SiteSpec Spec(LayerContext ctx, string style);
        /// <summary>What stands in the front yard for a style.</summary>
        protected abstract YardFill.FrontSpec Front(LayerContext ctx, string style);
        /// <summary>Decides each block's use (YardBlock.Use) and fills it.</summary>
        protected abstract void Fill(IndustrialSite s, string style);

        protected override void PlanCore(LayerContext ctx)
        {
            var region = ctx.LayerRegion;
            var gates = Gates(ctx);
            string style = ctx.S("style");
            if (System.Array.IndexOf(Styles, style) < 0) style = Styles[0];


            var front = Front(ctx, style);
            front.Offices = ctx.B("office") ? front.Offices : 0;
            front.Booth = front.Booth && ctx.B("office");
            front.Parking = front.Parking && ctx.B("parking") && front.ParkedCar.Options != null;
            front.Weighbridge = ctx.B("weighbridge");

            var spec = Spec(ctx, style);
            spec.Aisle = math.max(6f, ctx.F("aisle"));
            // A big plot gets longer blocks (a 20 ha yard of 60 m blocks is a car park).
            spec.MaxBlock *= 1f + 0.7f * math.saturate((math.sqrt(region.Area) - 160f) / 240f);
            // A small plot (the user: "What about really tiny areas?") gets a compact yard: a thin edge, a short front with one
            // office, no parking under 2000 m², short blocks, a narrower aisle.
            if (region.Area < 4000f)
            {
                float k = math.saturate((region.Area - 1000f) / 3000f);
                spec.Edge = math.min(spec.Edge, math.lerp(1.2f, spec.Edge, k));
                spec.Front = math.min(spec.Front, math.lerp(11f, spec.Front, k));
                spec.MinBlock = math.min(spec.MinBlock, math.lerp(6f, spec.MinBlock, k));
                spec.Aisle = math.min(spec.Aisle, math.lerp(6f, spec.Aisle, k));
                front.Offices = math.min(front.Offices, 1);
                if (region.Area < 2000f) front.Parking = false;
            }
            // A bund of 5 m boulders needs a band that deep inside the fence.
            if (ctx.S("edge") == "boulders" && region.Area >= 4000f) spec.Edge = math.max(spec.Edge, 6.4f);
            // The office row or the parking row, whichever reaches further from the spine, plus a walkway.
            float officeZone = front.Offices > 0 ? 3.2f + front.Office.Z + 0.6f : front.Booth ? 4f : 0f;
            float parkingZone = front.Parking ? 6.8f : 0f;
            spec.FrontZone = spec.Aisle * 0.5f + math.max(officeZone, parkingZone) + 2.5f;
            if (Organic(style)) spec.Aisle = OrganicYard.TrackWidth;
            var s = Organic(style) ? IndustrialSite.PlanOrganic(ctx, gates, spec, Hashing.Mix(ctx.Seed, 0x7A4Cu)) : IndustrialSite.Plan(ctx, gates, spec);
            ctx.Check();

            Ground(s);
            if (ctx.S("fence") != "none")
                YardKit.Fence(ctx, gates, "fence", AssetMix.One(ctx.S("fenceAsset")), s.Salt(0xFE7Cu));

            // Masts first: the edge walls leave them their gap.
            if (ctx.B("floodlights")) YardFill.Lights(s, s.Salt(0x11A5u));
            YardFill.FrontYard(s, front, s.Salt(0xF407u));
            if (front.Offices > 0 || front.Booth) YardEarth.BurnBarrel(s, s.Salt(0xB0B1u));
            ctx.Check();

            // Dirt banks along the back and side fence (YardEarth): an organic yard's claim their ground before the scrap field
            // fills it, a block yard's go where its blocks left room.
            int bankMax = ctx.B("banks") ? math.clamp((int)(region.Area / 9000f), 1, 4) : 0;
            // The boulder bund is laid before the fill too: a 5 m boulder reaches past the edge band (YardFill.Edge skips it).
            if (ctx.S("edge") == "boulders") YardEarth.Bund(s, s.Salt(0xED6Eu));
            if (s.Organic) YardEarth.Banks(s, bankMax, s.Salt(0xBA4Bu), ctx.S("edge") == "boulders" ? 1.6f : 0f);
            Fill(s, style);
            if (!s.Organic) YardEarth.Banks(s, bankMax, s.Salt(0xBA4Bu), ctx.S("edge") == "boulders" ? 1.6f : 0f);
            YardEarth.Crushed(s, s.Salt(0xC0C0u));
            if (Id == "containerYard") LorriesOnLanes(s, s.Salt(0x10A1u));
            ctx.Check();

            YardFill.Edge(s, ctx.S("edge"), ctx.L("treeSpecies"), s.Salt(0xED6Eu));
            // Stones spilled round the heaps, the pad and the banks, and along the dirt roads.
            // Fewer on a concrete yard: stones on a slab read as a mess, not as ground.
            YardEarth.Scree(s, ctx.F("stones") / 100f * (ctx.S("ground") == "concrete" ? 0.35f : 1f), s.Salt(0x5C2Eu));
            // Rubble worked into the earth (none on a concrete yard, half in a block yard: the scrap field is where it lies).
            YardEarth.Strewn(s, ctx.F("rubble") / 100f * (ctx.S("ground") == "concrete" ? 0f : s.Organic ? 1f : 0.5f), s.Salt(0x57E1u));
            ctx.Check();

            // The ground art (YardGround): spoil mounds in the leftover corners, the painted ground, the wear decals.
            var mounds = new List<Mound>(s.Mounds);
            if (ctx.B("spoil")) mounds.AddRange(YardGround.Spoil(s, math.clamp((int)(region.Area / 6000f), 1, 6), s.Salt(0x5B0Au)));
            if (ctx.B("spoil")) YardGround.DeadCorners(s, s.Salt(0xDEADu));
            if (ctx.B("paint"))
            {
                YardGround.Paint(s, ctx.S("ground"), s.Salt(0x9A17u));
                YardGround.Slabs(s, ctx.S("ground"), s.Salt(0x51ABu));
            }
            YardGround.Rocks(s, s.Salt(0x20C4u));
            // No wear decals: every decal of the kind (puddles, oil, litter) comes from an add-on pack, and the yards use the
            // game's own pieces only (the user, 2026-10-19). YardGround.Decals stays for a later vanilla set.
            int decals = 0;
            ctx.Check();

            // Whatever is still automatic (pads, aisles, the fence, decals) gets the curated industrial look.
            GritAssets.Apply(ctx.Plan, ctx.Seed, null, null, SecurityFence);
            GritAssets.CapDecals(ctx.Plan, region.Area, 110f);
            var terrain = Shape(ctx, region, mounds);
            YardKit.Settle(ctx, terrain);
            // The marks of work (YardWear): ruts, oil, rust, dust, weeds, cracks, puddles, each where the yard gives it a reason.
            if (ctx.B("paint")) YardWear.Lay(ctx, YardWear.Of(s, Id, style, ctx.S("ground")));
            EmitGreenRegions(ctx, s.Occ, 200f);

            ctx.Stat(Keys.StatArea, System.Math.Round(region.Area), Keys.UnitM2);
            ctx.Stat("Placemaker.Stat.Containers", s.Containers, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Wrecks", s.Wrecks, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Heaps", s.Heaps, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Bays", s.Bays, Keys.UnitCount);
            ctx.Stat("Placemaker.Stat.Machines", s.Machines, Keys.UnitCount);
            ctx.Stat(Keys.StatLamps, s.Lights, Keys.UnitCount);
            int used = 0;
            foreach (var b in s.Blocks) if (b.Use.Length > 0) used++;
            ctx.Plan.Facts["blocks"] = s.Blocks.Count;
            ctx.Plan.Facts["blocksUsed"] = used;
            ctx.Plan.Facts["aisles"] = s.Aisles.Count;
            ctx.Plan.Facts["offices"] = s.Offices;
            ctx.Plan.Facts["parked"] = s.Parked;
            ctx.Plan.Facts["items"] = s.Items;
            ctx.Plan.Facts["mounds"] = mounds.Count;
            ctx.Plan.Facts["decals"] = decals;
            ctx.Plan.Facts["paints"] = ctx.Plan.Paint.Count;
            ctx.Plan.Facts["banks"] = s.Banks;
            ctx.Plan.Facts["stones"] = s.Stones;
            ctx.Plan.Facts["strewn"] = s.Strewn;
            int sunk = 0, boulders = 0;
            foreach (var o in ctx.Plan.Objects) { if (o.Elevation < -0.01f) sunk++; if (o.Category == AssetCategories.Rock && (o.Asset.Id ?? "").StartsWith("Boulder")) boulders++; }
            ctx.Plan.Facts["sunk"] = sunk;
            ctx.Plan.Facts["boulders"] = boulders;
            if (DebugBlocks) { ctx.Plan.Facts["dbg.Ox"] = s.O.x; ctx.Plan.Facts["dbg.Oy"] = s.O.y; ctx.Plan.Facts["dbg.Vx"] = s.V.x; ctx.Plan.Facts["dbg.Vy"] = s.V.y; ctx.Plan.Facts["dbg.umin"] = s.UMin; ctx.Plan.Facts["dbg.umax"] = s.UMax; ctx.Plan.Facts["dbg.vmin"] = s.VMin; ctx.Plan.Facts["dbg.vmax"] = s.VMax; ctx.Plan.Facts["dbg.front"] = s.Spec.Front; ctx.Plan.Facts["dbg.gates"] = s.Gates.Count; }
            if (DebugBlocks) foreach (var b in s.Blocks) { ctx.Plan.Facts[$"blk{b.Index}.band"] = b.Band; ctx.Plan.Facts[$"blk{b.Index}.u0"] = b.U0; ctx.Plan.Facts[$"blk{b.Index}.u1"] = b.U1; ctx.Plan.Facts[$"blk{b.Index}.v0"] = b.V0; ctx.Plan.Facts[$"blk{b.Index}.v1"] = b.V1; }
        }

        /// <summary>A yard is fenced high (2.3 m chain-link), the low industrial fence only where no high one exists.</summary>
        private static readonly GritAssets.Set SecurityFence = new GritAssets.Set(true, "FenceIndustrialHigh01|FenceIndustrialHigh02|FenceIndustrialLow02|FenceIndustrialLow01");

        // ───────────── ground ─────────────

        private static readonly AssetRef Gravel = new AssetRef("Sand Surface 02", "Sand Surface 01");
        private static readonly AssetRef Dirt = new AssetRef("Oil Surface 01");
        /// <summary>Aisles: weathered asphalt from the surface packs, else the dark vanilla pavement (concrete would vanish on a concrete yard).</summary>
        private static readonly AssetRef AisleSurface = new AssetRef("Pavement Surface 01");
        private static readonly AssetRef WornLane = new AssetRef("Oil Surface 01", "Pavement Surface 01");
        private static readonly AssetRef Concrete = new AssetRef("Concrete Surface 01", "Pavement Surface 02", "Concrete Surface 02");

        /// <summary>The yard surface, then the aisles on top (asphalt, so the lanes read from above).</summary>
        private static void Ground(IndustrialSite s)
        {
            var ctx = s.Ctx;
            // Concrete: one slab over the yard inside the edge band. Gravel and dirt are painted into the terrain itself
            // (YardGround.Paint); without paint they fall back to a surface so the yard is never bare grass.
            // Without paint the yard keeps one surface so it is never bare grass; with paint it is a patchwork of slabs per
            // block over painted earth (YardGround.Slabs, after the blocks are filled).
            if (!ctx.B("paint"))
                switch (ctx.S("ground"))
                {
                    case "concrete": ctx.Surface(s.Inner.Ring, SurfaceRole.Paving, Concrete, AssetCategories.SurfacePaving); break;
                    case "dirt": ctx.Surface(s.Inner.Ring, SurfaceRole.Ground, Dirt, AssetCategories.SurfaceDirt); break;
                    default: ctx.Surface(s.Inner.Ring, SurfaceRole.Ground, Gravel, AssetCategories.SurfaceNatural); break;
                }
            // Concrete yards drive on dark pavement lanes; dirt and gravel yards on their own trodden ground (the paint's
            // mud tracks), the way a real scrap yard does: no white roads through the mud.
            if (s.Organic) { OrganicYard.TrackSurfaces(s); OrganicYard.FieldGround(s, Hashing.Mix(ctx.Seed, 0x6F0Du)); return; }
            float hw = s.Spec.Aisle * 0.5f;
            // Container yards (the makeover, 2026-10-30: uniform light lanes read clean and new from 45 degrees): the middle of
            // every lane, where the carriers and lorries run, is worn dark and oily; the shoulders stay pavement.
            bool worn = ctx.LayerRegion != null && s.Ctx.Plan.GeneratorId == "containerYard";
            foreach (var (a, b) in s.Aisles)
            {
                if (!worn) { YardKit.Strip(ctx, a, b, hw, SurfaceRole.Asphalt, AisleSurface, AssetCategories.SurfaceAsphalt); continue; }
                var d = math.normalizesafe(b - a); var n = new float2(-d.y, d.x);
                float mid = hw * 0.45f, side = (hw - mid) * 0.5f;
                YardKit.Strip(ctx, a, b, mid, SurfaceRole.Asphalt, WornLane, AssetCategories.SurfaceAsphalt);
                YardKit.Strip(ctx, a + n * (mid + side), b + n * (mid + side), side, SurfaceRole.Asphalt, AisleSurface, AssetCategories.SurfaceAsphalt);
                YardKit.Strip(ctx, a - n * (mid + side), b - n * (mid + side), side, SurfaceRole.Asphalt, AisleSurface, AssetCategories.SurfaceAsphalt);
            }
        }

        /// <summary>The yard levelled to one pad (blended to the ground at the outline) with the mounds on it: one sculpt.</summary>
        private static TerrainResult Shape(LayerContext ctx, LayerRegion region, List<Mound> mounds)
        {
            bool level = ctx.B("level");
            if (!level && mounds.Count == 0) return new TerrainResult { HeightAfter = ctx.H };
            float mean = level ? TerrainShaper.MeanHeight(ctx, region) : 0f;
            return YardKit.Sculpt(ctx, region, (p, h0) =>
            {
                float o = level ? mean - h0 : 0f;
                float m = 0f;
                for (int i = 0; i < mounds.Count; i++) m = math.max(m, mounds[i].Offset(p));
                return o + m;
            }, 45f, 12f, 4f);
        }

        /// <summary>Lorries waiting on the lanes for their box, parked on the lane's shoulder (one lane in three).</summary>
        private static void LorriesOnLanes(IndustrialSite s, uint salt)
        {
            int i = 0;
            foreach (var (a, b) in s.Aisles)
            {
                i++;
                float len = math.distance(a, b);
                if (len < 30f || Hashing.Unit(Hashing.Mix(salt, (uint)i)) > 0.4f) continue;
                var d = (b - a) / len; var n = new float2(-d.y, d.x);
                float t = len * (0.3f + 0.4f * Hashing.Unit(Hashing.Mix(salt, (uint)i + 99u)));
                float sideK = Hashing.Unit(Hashing.Mix(salt, (uint)i + 7u)) < 0.5f ? 1f : -1f;
                var p = a + d * t + n * sideK * (s.Spec.Aisle * 0.5f - 1.6f);
                var item = IndustrialAssets.Truck;
                s.Put(item, item.Pick(Hashing.Mix(salt, (uint)i + 3u)), p, d * (Hashing.Unit(Hashing.Mix(salt, (uint)i + 5u)) < 0.5f ? 1f : -1f), Hashing.Mix(salt, (uint)i + 11u), 0.05f, false);
            }
        }

        // ───────────── shared helpers for the types ─────────────

        /// <summary>Blocks ordered from the spine and the street outward (the order a yard fills up).</summary>
        protected static List<YardBlock> Ordered(IndustrialSite s)
        {
            var list = new List<YardBlock>(s.Blocks);
            list.Sort((a, b) => a.Band != b.Band ? a.Band.CompareTo(b.Band) : a.Near.CompareTo(b.Near));
            return list;
        }

        /// <summary>The biggest block (by area) — the one that gets the yard's centrepiece.</summary>
        protected static YardBlock Biggest(IndustrialSite s, System.Func<YardBlock, bool> ok = null)
        {
            YardBlock best = null;
            foreach (var b in s.Blocks)
                if ((ok == null || ok(b)) && (best == null || b.Length * b.Depth > best.Length * best.Depth)) best = b;
            return best;
        }

        protected static int LastBand(IndustrialSite s)
        {
            int n = -1;
            foreach (var b in s.Blocks) n = math.max(n, b.Band);
            return n;
        }
    }
}
