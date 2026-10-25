// Placemaker "Zoning" category — the looks of an area's buildings as Placemaker types (GPL-3.0).
//
// Each look (Garden, Natural, Tidy, Yard, Meadow, Rustic, Coastal, Manicured; Core/Neighbours/PolishLooks) is a type in the
// Make row's Zoning category: its option rows are every setting the neighbourhood polish reads per house (reach and
// joining, fences, lawns and paint, ground, planting and features, paths and shops; PolishLooks.Knobs), its styles are
// Compact, Standard and Sprawling (and the player's own, as for every type). Planning counts the buildings in the area;
// Build (Game/Tool/PlacemakerToolSystem.Polish) polishes them with the look and the values the rows have, saved on each
// building (NeighbourPolishSystem).
// Two tools in one: Zoning (zoneArea: zone the empty land, or only edit the buildings there) and Outgrow (shapeLots: lots
// grow past their borders). Up to three zones (A, B, C) to zone, each a zone type (or an exact zone prefab: any, mods' too)
// with its share of the land (the rest stays empty), its lots' width and depth (cells) and the road widths it goes on.
// Outgrow: the look's settings (the type's default) and, per zone type, its own switch and changes (zoneOutgrow: JSON
// {"<zone key>": {"on": bool, "v": {knob: value}}}, the key a zone's name without EU / NA, lower case), any number of them. The neighbourhood types (ZoningTypes.cs: Suburb,
// Village, Main street…) give their own zones and styles; the looks as types (Garden, Natural…) are hidden on the shelf.
// Zoning: the blocks in the area are zoned as lots that start at their road: along each block, lot after lot, each a zone
// picked by the shares among the zones allowed on that block's road whose smallest lot fits there (empty by the land's empty
// share), its width and depth in that zone's ranges: a lot is a full rectangle, never smaller than its zone's smallest (a
// leftover too narrow for any zone joins the lot before it, or that lot gives up enough for one; else it stays empty). A cell is only
// zoned when every cell between it and the road is (a zone behind another zone can never grow anything). Lots are mixed at
// random, in clumps (neighbouring lots alike) or spread evenly. The zoning is built as every build's (never over cells
// already zoned); with Outgrow on, the buildings that grow there outgrow their lots with their zone's settings.
// Level the lots (levelLots, LotLevel.cs): on sloped land each lot becomes a level pad at the height it meets its road, banks
// blending into the land around; a lot needing more earth than levelEarth is made shallower or left empty.

using System;
using System.Collections.Generic;
using System.Linq;
using Placemaker.Core.Build;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.AddOns;
using Placemaker.Core.Layers.Common;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Zoning
{
    public abstract class PolishLookLayer : LayerBase
    {
        public const string Prefix = "polish";
        public const string StatBuildings = "Placemaker.Polish.Stat.Buildings";

        /// <summary>The look this type is (PolishLooks.BuiltIn id).</summary>
        public abstract string LookId { get; }

        protected override string Area => "Polish";
        public override string Id => Prefix + char.ToUpperInvariant(LookId[0]) + LookId.Substring(1);
        public override string NameKey => "Placemaker.UI.Painter.Theme." + LookId;
        public override string Icon => "Media/Game/Icons/Zones.svg";
        public override LayerKind Kind => LayerKind.Fill;
        public override float MinArea => 20f;

        public const string StatCells = "Placemaker.Polish.Stat.Cells";
        public const string StatZone = "Placemaker.Polish.Stat.Zone.", StatExact = "Placemaker.Polish.Stat.ZoneExact";
        public static readonly string[] Patterns = { "mixed", "clumps", "even" };
        public const int MaxDepth = 6, MaxWidth = 10;

        /// <summary>The zone slots: A's settings have plain ids, B's and C's Outgrow settings end in _B and _C.</summary>
        public static readonly string[] SlotNames = { "A", "B", "C" };
        public const double MaxRoad = 48;

        /// <summary>A zone slot's id of an Outgrow setting (slot 0: the plain id).</summary>
        public static string KnobId(string knob, int slot) => slot == 0 ? knob : knob + "_" + SlotNames[slot];

        /// <summary>The Zoning tool's settings (the panel's Zoning section); the rest are Outgrow's.</summary>
        public static readonly string[] ZoningIds = BuildZoningIds();

        private static string[] BuildZoningIds()
        {
            var ids = new List<string> { "zoneArea", "zonePattern", "zoneGrain", "zoneOutgrow" };
            foreach (var sl in SlotNames)
                foreach (var id in new[] { "zone", "zoneAsset", "share", "widthMin", "widthMax", "depthMin", "depthMax", "roadMin", "roadMax" }) ids.Add(id + sl);
            return ids.ToArray();
        }

        /// <summary>A lot's usual size by zone type (cells): width along the road, depth from it.</summary>
        public static (int W0, int W1, int D0, int D1) LotSize(string kind)
        {
            switch (kind)
            {
                case ZoneKinds.ResidentialLow: case ZoneKinds.LowRent: return (2, 3, 2, 4);
                case ZoneKinds.ResidentialRow: return (2, 2, 3, 4);
                case ZoneKinds.CommercialLow: return (2, 4, 2, 4);
                case ZoneKinds.Industrial: return (3, 6, 4, 6);
                default: return (2, 4, 3, 6);
            }
        }

        /// <summary>The zone a look zones by default (Yard: industry; every other look: low density homes).</summary>
        protected virtual string DefaultZone => LookId == "yard" ? ZoneKinds.Industrial : ZoneKinds.ResidentialLow;

        /// <summary>One of the three zones of a type: its zone type, its share of the land (%), its lots (cells) and the road
        /// widths it goes on (m; 0 to MaxRoad: any).</summary>
        public readonly struct ZoneSlot
        {
            public readonly string Kind;
            public readonly double Share;
            public readonly int W0, W1, D0, D1;
            public readonly double R0, R1;
            public ZoneSlot(string kind, double share, int w0, int w1, int d0, int d1, double r0 = 0, double r1 = MaxRoad)
            { Kind = kind; Share = share; W0 = w0; W1 = w1; D0 = d0; D1 = d1; R0 = r0; R1 = r1; }
            public ZoneSlot(string kind, double share) : this(kind, share, LotSize(kind).W0, LotSize(kind).W1, LotSize(kind).D0, LotSize(kind).D1) { }
        }

        /// <summary>The type's three zones (B and C with share 0: not mixed in).</summary>
        protected virtual ZoneSlot[] Slots => new[] { new ZoneSlot(DefaultZone, 100), new ZoneSlot(ZoneKinds.CommercialLow, 0), new ZoneSlot(ZoneKinds.OfficeLow, 0) };
        protected virtual string DefaultPattern => "mixed";
        protected virtual double DefaultGrain => 3;

        /// <summary>A style's zones: every setting of the three slots (a slot not given: share 0).</summary>
        protected static (string, object)[] Mix(params ZoneSlot[] zones)
        {
            var list = new List<(string, object)>();
            string[] names = { "A", "B", "C" };
            for (int i = 0; i < 3; i++)
            {
                if (i >= zones.Length) { list.Add(("share" + names[i], 0.0)); continue; }
                var z = zones[i];
                list.Add(("zone" + names[i], z.Kind));
                list.Add(("zoneAsset" + names[i], ""));
                list.Add(("share" + names[i], z.Share));
                list.Add(("widthMin" + names[i], (double)z.W0));
                list.Add(("widthMax" + names[i], (double)z.W1));
                list.Add(("depthMin" + names[i], (double)z.D0));
                list.Add(("depthMax" + names[i], (double)z.D1));
                list.Add(("roadMin" + names[i], z.R0));
                list.Add(("roadMax" + names[i], z.R1));
            }
            return list.ToArray();
        }

        /// <summary>Outgrow settings for every zone's copy (A, _B, _C).</summary>
        protected static (string, object)[] AllZones(params (string, object)[] values) => values;

        /// <summary>Lots that keep to themselves (small reach, no open land, lawn everywhere).</summary>
        protected static readonly (string, object)[] CompactReach =
        {
            ("gardenWidth", 15.0), ("gardenDepth", 35.0), ("sideReach", 30.0), ("maxFrontGap", 40.0), ("maxBackGap", 60.0), ("maxRearGap", 20.0), ("maxFacingGap", 16.0),
            ("maxDeepen", 16.0), ("hugReach", 16.0), ("openReach", 0.0), ("lawnReach", 1000.0),
        };

        /// <summary>Roomy lots: they reach well out, but stop short of filling whole fields.</summary>
        protected static readonly (string, object)[] WideReach =
        {
            ("gardenWidth", 70.0), ("gardenDepth", 80.0), ("sideReach", 150.0), ("maxFrontGap", 120.0), ("maxBackGap", 180.0), ("maxRearGap", 80.0), ("maxFacingGap", 60.0),
            ("maxDeepen", 60.0), ("hugReach", 100.0), ("openReach", 40.0),
        };

        /// <summary>Lots that take every bit of land around them.</summary>
        protected static readonly (string, object)[] SprawlReach =
        {
            ("gardenWidth", 120.0), ("gardenDepth", 140.0), ("sideReach", 250.0), ("maxFrontGap", 200.0), ("maxBackGap", 300.0), ("maxRearGap", 150.0), ("maxFacingGap", 120.0),
            ("maxDeepen", 150.0), ("hugReach", 150.0), ("openReach", 80.0),
        };

        protected static (string, object)[] With(params (string, object)[][] parts) => parts.SelectMany(p => p).ToArray();

        /// <summary>The type of a layer id, or null.</summary>
        public static PolishLookLayer Of(string layerId) => GeneratorRegistry.Get(layerId) as PolishLookLayer;

        protected override void DefineControls(ControlSet c)
        {
            // Zoning: zone the empty land (true) or only edit the buildings there (false)
            c.Toggle("zoneArea", true, ControlTier.Essential, "zone").Tip();
            var slots = Slots;
            for (int i = 0; i < 3; i++)
            {
                string slot = SlotNames[i];
                var zs = i < slots.Length ? slots[i] : new ZoneSlot(ZoneKinds.CommercialLow, 0);
                var z = EdgeZoningAddOn.ZoneKindControl("zone" + slot, "zone", zs.Kind);
                z.LabelKey = c.Key("zone" + slot);
                z.TooltipKey = z.LabelKey + ".Tooltip";
                z.Tier = slot == "A" ? ControlTier.Essential : ControlTier.More;
                c.List.Add(z);
                c.Asset("zoneAsset" + slot, AssetCategories.Zone, ControlTier.More, "zone").Tip();
                c.Slider("share" + slot, zs.Share, 0, 100, 1, "%", slot == "A" ? ControlTier.Essential : ControlTier.More, "zone").Tip().When("zoneArea=true");
                c.Slider("widthMin" + slot, zs.W0, 1, MaxWidth, 1, "count", ControlTier.More, "zone").Tip().When("zoneArea=true");
                c.Slider("widthMax" + slot, zs.W1, 1, MaxWidth, 1, "count", ControlTier.More, "zone").Tip().When("zoneArea=true");
                c.Slider("depthMin" + slot, zs.D0, 1, MaxDepth, 1, "count", ControlTier.More, "zone").Tip().When("zoneArea=true");
                c.Slider("depthMax" + slot, zs.D1, 1, MaxDepth, 1, "count", ControlTier.More, "zone").Tip().When("zoneArea=true");
                c.Slider("roadMin" + slot, zs.R0, 0, MaxRoad, 1, "m", ControlTier.More, "zone").Tip().When("zoneArea=true");
                c.Slider("roadMax" + slot, zs.R1, 0, MaxRoad, 1, "m", ControlTier.More, "zone").Tip().When("zoneArea=true");
                foreach (var id in new[] { "zone", "zoneAsset", "share", "widthMin", "widthMax", "depthMin", "depthMax", "roadMin", "roadMax" }) c.Fixed(id + slot);
            }
            c.Choice("zonePattern", DefaultPattern, ControlTier.Essential, "zone", Patterns).Tip().When("zoneArea=true");
            c.Toggle("levelLots", true, ControlTier.Essential, "zone").Tip().When("zoneArea=true");
            c.Slider("levelEarth", 4, 1, 8, 0.5, "m", ControlTier.More, "zone").Tip().When("levelLots=true");
            c.Slider("levelBank", 40, 20, 50, 5, "%", ControlTier.More, "zone").Tip().When("levelLots=true");
            c.Toggle("levelWalls", false, ControlTier.More, "zone").Tip().When("levelLots=true");
            foreach (var id in new[] { "levelLots", "levelEarth", "levelBank", "levelWalls" }) c.Fixed(id);
            c.Slider("zoneGrain", DefaultGrain, 1, 12, 1, "count", ControlTier.More, "zone").Tip().When("zonePattern=clumps");
            // each zone type's own Outgrow switch and changes (the panel writes it; JSON, see the file's header)
            c.Asset("zoneOutgrow", "", ControlTier.More, "zone").Tip();
            foreach (var id in new[] { "zoneArea", "zonePattern", "zoneGrain", "zoneOutgrow" }) c.Fixed(id);
            // Outgrow: the lots grow past their borders, each zone's lots by its own copy of the look's settings
            c.Toggle("shapeLots", true, ControlTier.Essential, "shape").Tip();
            c.Fixed("shapeLots");
            var look = (PolishLooks.BuiltIn.FirstOrDefault(l => l.Id == LookId) ?? PolishLooks.BuiltIn[0]).Values();
            for (int slot = 0; slot < 1; slot++)
                foreach (var k in PolishLooks.Knobs)
                {
                    float def = look.TryGetValue(k.Id, out var v) ? v : 0f;
                    string id = KnobId(k.Id, slot);
                    ControlSchema cs;
                    switch (k.Kind)
                    {
                        case PolishLooks.KnobKind.Toggle: cs = c.Toggle(id, def > 0.5f, k.Tier, k.Group); break;
                        case PolishLooks.KnobKind.Choice: cs = c.Choice(id, ((int)math.round(def)).ToString(), k.Tier, k.Group, k.Options); break;
                        default: cs = c.Slider(id, def, k.Min, k.Max, k.Step, k.Unit, k.Tier, k.Group); break;
                    }
                    cs.Tip();
                    if (slot > 0)
                    {
                        // the same row as A's: its label, tooltip and choice labels
                        cs.LabelKey = c.Key(k.Id);
                        cs.TooltipKey = cs.LabelKey + ".Tooltip";
                        foreach (var o in cs.Options) { o.LabelKey = c.Key(k.Id) + "." + o.Value; }
                    }
                    c.Fixed(id);
                }
        }

        protected override void DefinePresets()
        {
            Preset("standard");
            Preset("compact", AllZones(CompactReach));
            Preset("sprawling", AllZones(SprawlReach));
        }

        protected override void PlanCore(LayerContext ctx)
        {
            // the buildings standing in the area (their lot's middle): what Build polishes
            int buildings = 0;
            foreach (var o in ctx.Input.Site.Obstacles)
            {
                if (o.Kind != ObstacleKind.Building) continue;
                var mid = (o.A + o.B + o.C + o.D) * 0.25f;
                if (Poly.Contains(ctx.LayerRegion.Ring, mid)) buildings++;
            }
            if (ctx.B("shapeLots"))
            {
                ctx.Plan.Stats.Add(new PlanStat(StatBuildings, buildings, "count"));
                ctx.Plan.Facts["polishBuildings"] = buildings;
            }
            if (ctx.B("zoneArea")) PlanZoning(ctx);
        }

        private sealed class LotZone
        {
            public string Id, Stat;
            public float Share;
            public int W0, W1, D0, D1;
            public float R0, R1;
        }

        /// <summary>
        /// The blocks in the area zoned as lots that start at their road (see the file's header): per block, how deep each
        /// column can be zoned (open, inside the area, back from the road with no gap) and the zones its road allows, then
        /// lot after lot along the block.
        /// </summary>
        private void PlanZoning(LayerContext ctx)
        {
            var site = ctx.Input.Site;
            var ring = ctx.LayerRegion.Ring;
            var clock = new PlanClock(ctx.Plan.Timings);
            var zones = new List<LotZone>();
            foreach (var slot in SlotNames)
            {
                float share = ctx.F("share" + slot);
                if (share <= 0f) continue;
                string asset = ctx.S("zoneAsset" + slot);
                string kind = ctx.S("zone" + slot);
                int w0 = math.clamp(ctx.I("widthMin" + slot), 1, MaxWidth), d0 = math.clamp(ctx.I("depthMin" + slot), 1, MaxDepth);
                float r0 = math.max(0f, ctx.F("roadMin" + slot));
                zones.Add(new LotZone
                {
                    Id = !string.IsNullOrEmpty(asset) ? asset : ZoneKinds.PrefabId(kind),
                    Stat = !string.IsNullOrEmpty(asset) ? StatExact : StatZone + kind,   // its line in Review
                    Share = share,
                    W0 = w0, W1 = math.clamp(ctx.I("widthMax" + slot), w0, MaxWidth),
                    D0 = d0, D1 = math.clamp(ctx.I("depthMax" + slot), d0, MaxDepth),
                    R0 = r0, R1 = math.max(r0, ctx.F("roadMax" + slot)),
                });
            }
            if (zones.Count == 0) return;
            // the shares are of the land: what they leave is empty (more than 100 together: shared out, nothing empty)
            float sum = zones.Sum(z => z.Share);
            float fill = math.clamp(sum / 100f, 0.01f, 1f);
            string pattern = ctx.S("zonePattern");
            float clump = math.max(1, ctx.I("zoneGrain")) * 3f * ZoneGuard.Cell;   // about this many lots of 3 cells alike
            uint seed = ctx.Input.Seed * 2654435761u + 0x2D0Eu;
            // the roads near the area as sampled centre lines, for each block's road width
            var roads = new List<(float2[] Line, float Width)>();
            var roads3 = new List<(float3[] Line, float Width)>();
            foreach (var net in site.NearbyNets)
            {
                if (net.Category != NetCategory.Road) continue;
                var line = new float2[17];
                var line3 = new float3[17];
                for (int k = 0; k <= 16; k++) { line3[k] = Bezier3(net, k / 16f); line[k] = line3[k].xz; }
                roads.Add((line, net.Width));
                roads3.Add((line3, net.Width));
            }
            // Level the lots: each lot a level pad at its road's height (a lot needing too much earth: shallower or empty)
            bool levelLots = ctx.B("levelLots") && site.HeightAt != null;
            float maxEarth = math.clamp(ctx.F("levelEarth"), 0.5f, 12f);
            var lots = new List<LotLevel.Lot>();
            int tooSteep = 0;
            Poly.Bounds(ring, out var rmin, out var rmax);
            var open = ZoneGuard.CellOpener(site.NearbyNets, rmin - 40f, rmax + 40f);
            clock.Lap("roads");
            var count = new Dictionary<string, (int Cells, string Stat)>();
            var done = new HashSet<long>();
            int lotIndex = 0;
            foreach (var g in site.ZoneGrids)
            {
                int sx = g.Size.x, sy = g.Size.y;
                if (sx <= 0 || sy <= 0) continue;
                // how deep each column can be zoned: from the road back, while open, inside the area and not taken by a block
                // laid out before (corners where blocks overlap)
                long KeyOf(float2 c) => ((long)math.round(c.x * 2f) << 32) ^ (uint)(int)math.round(c.y * 2f);
                var reach = new int[sx];
                bool any = false;
                for (int x = 0; x < sx; x++)
                {
                    int y = 0;
                    for (; y < sy; y++)
                    {
                        var c = ZoneGuard.CellCentre(g, x, y);
                        if (c.x < rmin.x || c.y < rmin.y || c.x > rmax.x || c.y > rmax.y || !Poly.Contains(ring, c) || !open(g, x, y) || done.Contains(KeyOf(c))) break;
                    }
                    reach[x] = y;
                    any |= y > 0;
                }
                if (!any) continue;
                var fronts = levelLots ? LotLevel.Fronts(g, site.HeightAt) : null;
                // the block's road (the nearest to its front middle) and the zones allowed on it (unknown road: all)
                float roadWidth = -1f;
                var mid = ZoneGuard.CellCentre(g, sx / 2, 0);
                float best = float.MaxValue;
                foreach (var (line, width) in roads)
                {
                    float dist = DistanceToLine(line, mid);
                    if (dist <= width * 0.5f + ZoneGuard.Cell && dist < best) { best = dist; roadWidth = width; }
                }
                var allowed = roadWidth < 0f ? zones : zones.Where(z => roadWidth >= z.R0 - 0.01f && roadWidth <= z.R1 + 0.01f).ToList();
                int MinReach(int from, int count)
                {
                    int m = int.MaxValue;
                    for (int x = from; x < from + count && x < sx; x++) m = math.min(m, reach[x]);
                    return m == int.MaxValue ? 0 : m;
                }
                int x0 = 0;
                while (x0 < sx)
                {
                    if (reach[x0] == 0) { x0++; continue; }
                    // the run of columns that can be zoned from here (to a gap or the block's end): a lot never crosses it
                    int run = 0;
                    while (x0 + run < sx && reach[x0 + run] > 0) run++;
                    // the zones whose smallest lot fits here (wide enough, and deep enough over its width)
                    var fitting = allowed.Where(z => z.W0 <= run && MinReach(x0, z.W0) >= z.D0).ToList();
                    var front = ZoneGuard.CellCentre(g, x0, 0);
                    uint lotSeed = math.hash(new uint3((uint)(int)math.round(front.x * 4f), (uint)(int)math.round(front.y * 4f), seed));
                    // one value picks both: below the zoned share a zone (among those that fit), above it an empty lot (so clumps
                    // and even spreads hold for the empty lots too)
                    float u = PatternValue(pattern, front, lotIndex, clump, seed, lotSeed);
                    bool empty = u >= fill || fitting.Count == 0;
                    var sizeFrom = fitting.Count > 0 ? fitting : allowed.Count > 0 ? allowed : zones;
                    float fromSum = sizeFrom.Sum(z => z.Share);
                    float v = (u >= fill ? (u - fill) / math.max(1e-4f, 1f - fill) : u / fill) * fromSum;
                    var zone = sizeFrom[sizeFrom.Count - 1];
                    foreach (var cand in sizeFrom) { if (v < cand.Share) { zone = cand; break; } v -= cand.Share; }
                    int w = math.min(run, zone.W0 + (int)(((lotSeed >> 4) & 0xFFFF) % (uint)(zone.W1 - zone.W0 + 1)));
                    // a lot keeps its depth over its whole width: narrower where the land behind it ends sooner
                    if (!empty)
                    {
                        while (w > zone.W0 && MinReach(x0, w) < zone.D0) w--;
                        if (MinReach(x0, w) < zone.D0) empty = true;
                    }
                    // what it leaves before the next gap: too narrow for any zone's smallest lot, it joins this lot (or, past this
                    // lot's widest, this lot gives up enough for one)
                    int left = run - w;
                    int minW = allowed.Count > 0 ? allowed.Min(z => z.W0) : 1;
                    if (left > 0 && left < minW)
                    {
                        if (w + left <= zone.W1 && (empty || MinReach(x0, w + left) >= zone.D0)) w += left;
                        else if (w - (minW - left) >= zone.W0) w -= minW - left;
                    }
                    w = math.max(1, w);
                    if (!empty)
                    {
                        int d = zone.D0 + (int)(((lotSeed >> 20) & 0xFFF) % (uint)(zone.D1 - zone.D0 + 1));
                        d = math.min(d, MinReach(x0, w));
                        LotLevel.Lot lot = null;
                        if (levelLots)
                        {
                            // as deep as it can be levelled within the earthwork limit (never under its zone's smallest: then empty)
                            d = LotLevel.Depth(g, fronts, x0, w, d, zone.D0, site.HeightAt, maxEarth, out bool needs);
                            if (d == 0) tooSteep++;
                            else
                            {
                                var lotFront = new float[w];
                                Array.Copy(fronts, x0, lotFront, 0, w);
                                lot = new LotLevel.Lot { G = g, X0 = x0, W = w, D = d, MinD = zone.D0, MinW = zone.W0, Front = lotFront, Level = needs, ZoneId = zone.Id, Stat = zone.Stat };
                                lots.Add(lot);
                            }
                        }
                        for (int x = x0; x < x0 + w; x++)
                            for (int y = 0; y < d; y++)
                            {
                                var c = ZoneGuard.CellCentre(g, x, y);
                                if (!done.Add(KeyOf(c))) continue;
                                var sq = ZoneGuard.CellSquare(g, x, y);
                                // levelled lots: zoned once the ground is laid (the rows a bank took off are not)
                                if (lot != null) { lot.Cells.Add((x, y, sq)); continue; }
                                ctx.Plan.Zoning.Add(new PlannedZoning { A = P(ctx, sq[0]), B = P(ctx, sq[1]), C = P(ctx, sq[2]), D = P(ctx, sq[3]), ZoneId = zone.Id });
                                count[zone.Id] = (count.TryGetValue(zone.Id, out var n) ? n.Cells + 1 : 1, zone.Stat);
                            }
                    }
                    x0 += w;
                    lotIndex++;
                }
            }
            clock.Lap("lots");
            if (levelLots)
            {
                var opt = new LotLevel.Options { MaxEarth = maxEarth, BankSlope = ctx.F("levelBank") / 100f, Walls = ctx.B("levelWalls"), Seed = ctx.Input.Seed == 0 ? 1u : ctx.Input.Seed };
                var levelTimes = new List<KeyValuePair<string, double>>();
                var after = LotLevel.Build(ctx, lots, roads3, opt, ctx.Cancel, levelTimes);
                clock.Lap("level", levelTimes);
                if (after != null) ctx.Plan.HeightAfter = after;
                var h = after ?? site.HeightAt;
                foreach (var lot in lots)
                {
                    if (lot.D == 0) { tooSteep++; continue; }
                    foreach (var (x, y, sq) in lot.Cells)
                    {
                        if (y >= lot.D || x < lot.X0 || x >= lot.X0 + lot.W) continue;
                        ctx.Plan.Zoning.Add(new PlannedZoning { A = At(h, sq[0]), B = At(h, sq[1]), C = At(h, sq[2]), D = At(h, sq[3]), ZoneId = lot.ZoneId });
                        count[lot.ZoneId] = (count.TryGetValue(lot.ZoneId, out var n) ? n.Cells + 1 : 1, lot.Stat);
                    }
                }
                ctx.Plan.Facts["polishLevelTooSteep"] = tooSteep;
                clock.Lap("zone levelled lots");
            }
            int cells = count.Values.Sum(vv => vv.Cells);
            ctx.Plan.Stats.Add(new PlanStat(StatCells, cells, "count"));
            if (count.Count > 1)
                foreach (var kv in count.OrderByDescending(kv => kv.Value.Cells))
                    ctx.Plan.Stats.Add(new PlanStat(kv.Value.Stat, kv.Value.Cells, "count"));
            ctx.Plan.Facts["polishZoneCells"] = cells;
        }

        private static float3 At(System.Func<float2, float> h, float2 p) => new float3(p.x, h(p), p.y);

        private static float3 P(LayerContext ctx, float2 p) => new float3(p.x, ctx.Input.Site.HeightAt != null ? ctx.Input.Site.HeightAt(p) : 0f, p.y);

        private static float3 Bezier3(NearbyNet n, float t)
        {
            float u = 1f - t;
            return u * u * u * n.A + 3f * u * u * t * n.B + 3f * u * t * t * n.C + t * t * t * n.D;
        }

        private static float2 Bezier(NearbyNet n, float t)
        {
            float u = 1f - t;
            var p = u * u * u * n.A + 3f * u * u * t * n.B + 3f * u * t * t * n.C + t * t * t * n.D;
            return p.xz;
        }

        private static float DistanceToLine(float2[] line, float2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                var a = line[i];
                var e = line[i + 1] - a;
                float t = math.saturate(math.dot(p - a, e) / math.max(1e-6f, math.dot(e, e)));
                best = math.min(best, math.distance(p, a + e * t));
            }
            return best;
        }

        /// <summary>A lot's value in [0, 1) that picks its zone by the shares: random per lot (mixed), the same for lots near
        /// each other (clumps), or spread evenly along the lots in turn (even: a golden-ratio sequence).</summary>
        private static float PatternValue(string pattern, float2 front, int lotIndex, float clump, uint seed, uint lotSeed)
        {
            switch (pattern)
            {
                case "clumps": return Rand(Cell(front, clump), seed);
                case "even": return math.frac(lotIndex * 0.6180339887f + (seed & 0xFFFF) / 65536f) * 0.9999f;
                default: return (lotSeed & 0xFFFFFF) / (float)0x1000000;   // mixed
            }
        }

        private static int2 Cell(float2 p, float size) => (int2)math.floor(p / size);

        private static float Rand(int2 cell, uint seed)
        {
            uint h = math.hash(new uint3((uint)cell.x, (uint)cell.y, seed));
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    [AreaGenerator(10, Group = "zoning", Loose = true)] public sealed class PolishGardenLayer : PolishLookLayer { public override string LookId => "garden"; }
    [AreaGenerator(11, Group = "zoning", Loose = true)] public sealed class PolishNaturalLayer : PolishLookLayer { public override string LookId => "natural"; }
    [AreaGenerator(12, Group = "zoning", Loose = true)] public sealed class PolishTidyLayer : PolishLookLayer { public override string LookId => "tidy"; }
    [AreaGenerator(13, Group = "zoning", Loose = true)] public sealed class PolishYardLayer : PolishLookLayer { public override string LookId => "yard"; }
    [AreaGenerator(14, Group = "zoning", Loose = true)] public sealed class PolishMeadowLayer : PolishLookLayer { public override string LookId => "meadow"; }
    [AreaGenerator(15, Group = "zoning", Loose = true)] public sealed class PolishRusticLayer : PolishLookLayer { public override string LookId => "rustic"; }
    [AreaGenerator(16, Group = "zoning", Loose = true)] public sealed class PolishCoastalLayer : PolishLookLayer { public override string LookId => "coastal"; }
    [AreaGenerator(17, Group = "zoning", Loose = true)] public sealed class PolishManicuredLayer : PolishLookLayer { public override string LookId => "manicured"; }
}
