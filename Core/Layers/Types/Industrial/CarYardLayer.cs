// Industrial category: the car yard (car demolition / auto salvage). Wrecks stand in long double rows nose to the aisle,
// one make to a row, the way a pick-your-part yard racks them; the back of the yard is where the work happens:
//   salvage   pick-your-part: double rows of wrecks on every band, customer parking at the office, a row of parts bays
//             against the back fence (tyres, engines, batteries, body panels);
//   crusher   the crusher yard: wrecks queued in rows at the front, the crusher's pad at the back (a heap of crushed
//             metal, the loader, skips), rows of crushed-metal heaps beside it;
//   breakers  a breakers' yard: a row of carports at the front where cars are taken apart, rows of drained wrecks behind,
//             parts sorted into bays at the back.

using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Types.Yards
{
    [AreaGenerator(65, Group = "industrial")]
    public sealed class CarYardLayer : IndustrialYardLayer
    {
        public const string LayerId = "carYard";
        private static readonly string[] StyleIds = { "salvage", "crusher", "breakers" };

        protected override string Area => "CarYard";
        public override string Id => LayerId;
        public override string Icon => "Media/Game/Icons/PropsIndustrial.svg";
        protected override string[] Styles => StyleIds;

        protected override void DefinePresets()
        {
            Preset("salvage", ("style", "salvage"), ("ground", "dirt"), ("edge", "wrecks"), ("aisle", 7.0), ("fill", 90.0), ("weighbridge", false));
            Preset("crusher", ("style", "crusher"), ("ground", "dirt"), ("edge", "wrecks"), ("aisle", 8.0), ("fill", 85.0), ("weighbridge", true));
            Preset("breakers", ("style", "breakers"), ("ground", "gravel"), ("edge", "trees"), ("aisle", 7.0), ("fill", 85.0), ("weighbridge", false));
        }

        // Two wrecks nose to tail (4.7 m each) with a walkway between their tails.
        private const float DoubleRow = 2f * 5.2f + 1.0f + 2f * 0.5f + 0.5f;

        /// <summary>Every car yard is a scrapyard: tracks through a field of wrecks (OrganicYard), not rows in blocks.</summary>
        protected override bool Organic(string style) => true;

        protected override SiteSpec Spec(LayerContext ctx, string style)
        {
            switch (style)
            {
                case "crusher":
                    return new SiteSpec { Edge = 3f, Front = 24f, BandDepth = DoubleRow, LastBand = 26f, MaxBlock = 52f, MinBlock = 9f, BlockGap = 0.5f };
                case "breakers":
                    return new SiteSpec { Edge = 5f, Front = 24f, BandDepth = DoubleRow, LastBand = 12f, MaxBlock = 48f, MinBlock = 9f, BlockGap = 0.5f };
                default:
                    return new SiteSpec { Edge = 3f, Front = 26f, BandDepth = DoubleRow, LastBand = 12f, MaxBlock = 56f, MinBlock = 9f, BlockGap = 0.5f };
            }
        }

        protected override YardFill.FrontSpec Front(LayerContext ctx, string style) => new YardFill.FrontSpec
        {
            Office = style == "breakers" ? IndustrialAssets.Shed : IndustrialAssets.Camper, Offices = 2, Booth = style != "breakers",
            Parking = true, Toilet = true, Flag = style == "salvage", ParkedCar = IndustrialAssets.Car,
        };

        /// <summary>What the parts bays hold, in order along the back wall.</summary>
        private static readonly YardItem[] Parts =
        {
            IndustrialAssets.Tyres,      // tyres (dark rubber)
            IndustrialAssets.Barrels,    // engines and oil
            IndustrialAssets.Crates,     // batteries and small parts
            IndustrialAssets.MetalHeap,  // body panels
            IndustrialAssets.Pallets,    // glass and doors
            IndustrialAssets.Gas,        // drained gas bottles
        };

        protected override void Fill(IndustrialSite s, string style)
        {
            float fill = s.Ctx.F("fill") / 100f;
            if (s.Organic)
            {
                // The work pad first (kept clear), then the wrecks lining the tracks, then the field round them.
                OrganicYard.WorkPad(s, style == "crusher", s.Salt(0x9AD0u));
                OrganicYard.TrackRows(s, IndustrialAssets.Car, fill, s.Salt(0x7A0Bu));
                var zones = style == "crusher"
                    ? new[] { new OrganicYard.Zone(YardFill.CrushedMix, 0.9f, 0.7f), new OrganicYard.Zone(YardFill.MetalMix, 0.85f, 0.2f), new OrganicYard.Zone(YardFill.CarsMix, 0.85f, 0.6f) }
                    : style == "breakers"
                        ? new[] { new OrganicYard.Zone(YardFill.PartsMix, 0.8f, 0.3f), new OrganicYard.Zone(YardFill.CarsMix, 0.8f, 0.35f) }
                        : new[] { new OrganicYard.Zone(YardFill.CarsMix, 0.9f, 0.4f), new OrganicYard.Zone(YardFill.PartsMix, 0.85f, 0.5f), new OrganicYard.Zone(YardFill.CarsMix, 0.9f, 0.65f) };
                OrganicYard.Field(s, zones, fill, s.Salt(0xF1E1u));
                OrganicYard.Spill(s, fill, s.Salt(0x5911u));
                return;
            }
            int last = LastBand(s);
            var car = IndustrialAssets.Car;
            YardBlock pad = null;
            if (style == "crusher")
            {
                // The crusher's pad: the biggest block of the back band (or of the yard).
                pad = Biggest(s, b => b.Band == last && last > 0) ?? Biggest(s);
                if (pad != null && YardFill.Mountain(s, pad, s.Salt(0xC2u))) pad.Use = "crusher";
                else pad = null;
            }
            foreach (var b in Ordered(s))
            {
                if (b == pad) continue;
                uint salt = s.Salt(0xCA00u + (uint)b.Index * 7919u);
                bool back = b.Band == last && last > 0;
                if (style == "breakers" && b.Band == 0 && YardFill.Carports(s, b, fill, salt, car) > 0) { b.Use = "carports"; continue; }
                if (back && style != "crusher" && YardFill.Bays(s, b, 8.6f, fill, salt, (i, c, face, w, d) => PartsBay(s, i, c, face, w, d, salt)) > 0)
                {
                    b.Use = "parts";
                    continue;
                }
                if (back && style == "crusher" && YardFill.Jumble(s, b, YardFill.CrushedMix, fill, salt) > 0) { b.Use = "crushed"; continue; }
                // Wrecks: nose to the aisle, a few degrees off true (they were dropped by a forklift, not parked).
                // Piled higher towards the back: a few on top of each other at the front, most at the back.
                float pile = b.Band < 0 ? 0.15f : math.min(0.8f, 0.35f + 0.2f * b.Band);
                int n = YardFill.RowsAcross(s, b, car, 0.6f, fill, salt, YardFill.Mess.Wrecks(pile));
                if (n > 0)
                {
                    b.Use = "wrecks";
                    s.Wrecks += n;
                    EndStacks(s, b, salt);
                }
            }
        }

        private static void PartsBay(IndustrialSite s, int i, float2 c, float2 face, float w, float d, uint salt)
        {
            var item = Parts[i % Parts.Length];
            uint v = Hashing.Mix(salt, (uint)i);
            if (item.X >= 7f) YardFill.Heap(s, c, face, w, d, item, v);
            else if (item.Category == AssetCategories.ScrapPallet) YardFill.Grid(s, c, face, w, d, item, 0.25f, 0.75f, v);
            else YardFill.Clump(s, c, math.min(w, d) * 0.4f, item, 6 + (int)(v % 5u), v);
        }

        private static readonly YardItem DeadBush = new YardItem(AssetCategories.Bush, 1.8f, 1.8f, "FlowerBushWildDead02");

        /// <summary>
        /// What collects round a row of wrecks: a loose pile of drums, gas bottles and crates at the row's outer end (the
        /// fluids and the small parts taken out), weeds and dead bushes coming up along the walkway between the rows.
        /// </summary>
        private static void EndStacks(IndustrialSite s, YardBlock b, uint salt)
        {
            float uOut = b.UMid >= 0f ? b.U1 - 2.4f : b.U0 + 2.4f;
            var c = s.W(uOut, b.VMid + (Hashing.Unit(salt) - 0.5f) * b.Depth * 0.4f);
            YardFill.Clump(s, c, 2.4f, IndustrialAssets.Drums, 7, Hashing.Mix(salt, 1u));
            YardFill.Clump(s, c + s.V * 1.8f, 1.6f, (salt & 1u) == 0 ? IndustrialAssets.Gas : IndustrialAssets.Crates, 2, Hashing.Mix(salt, 2u));
            // The walkway between back-to-back rows: weeds in it every few metres, some of them dead.
            if (!b.AisleFront || !b.AisleBack) return;
            int i = 0;
            for (float u = b.U0 + 3f; u < b.U1 - 3f; u += 4f + Hashing.Unit(Hashing.Mix(salt, (uint)(i + 40))) * 6f, i++)
            {
                uint v = Hashing.Mix(salt, (uint)(i + 100));
                if (Hashing.Unit(v) < 0.45f) continue;
                var p = s.W(u, b.VMid + (Hashing.Unit(Hashing.Mix(v, 1u)) - 0.5f) * 0.6f);
                if (!s.Occ.Free(p, 0.7f, 0.2f, 0.05f) || !s.ReservedOnly.Free(p, 1.3f, 0f, 0.2f)) continue;
                bool dead = Hashing.Unit(Hashing.Mix(v, 2u)) < 0.4f;
                s.Ctx.Object(p, Hashing.Unit(Hashing.Mix(v, 3u)) * 6.283f, ObjectRole.Bush, dead ? DeadBush.Pick(v) : new AssetRef(""), v, -1, AssetCategories.Bush);
                s.Occ.Take(p, 0.7f, Scrapyard.YardKit.KindStuff);
            }
        }
    }
}
