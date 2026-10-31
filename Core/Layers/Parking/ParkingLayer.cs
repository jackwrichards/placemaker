// Parking lot layer. Layout engine ported from ParkingLotTool (KruemelGames, GPL-3.0); see
// LayoutEngine.cs, SurfaceCells.cs, SpecialStalls.cs, Lanterns.cs, Vegetation.cs.
//
// Pure and reentrant: no static mutable state (PLT's PhaseLog / SonderplatzSpur statics are gone),
// every run builds its own settings snapshot and engine instance. Never throws for bad geometry;
// cancellation is honoured between phases (OperationCanceledException propagates to the composer).

using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Parking
{
    [AreaGenerator(10, Group = "parking")]
    public sealed class ParkingLayer : IAreaGenerator, IHasFeatures
    {
        /// <summary>
        /// Round 4 features. "Shops along the street" for lots is the built-in edge zoning (zoning
        /// controls + zoning patch markers), which already shares the frontage rules with StreetFrontage.
        /// </summary>
        private static readonly FeatureInfo[] FeatureList =
        {
            new FeatureInfo { Layer = "shops", LabelKey = "Placemaker.Feature.shops", TooltipKey = "Placemaker.Feature.shops.Tooltip",
                Preset = Park.ParkLayer.Bag(("count", 2.0), ("where", "edges"), ("seating", false)) },
            new FeatureInfo { Layer = "decals", LabelKey = "Placemaker.Feature.decals", Preset = Park.ParkLayer.Bag(("theme", "cracks"), ("ground", "paved"), ("density", 0.6)) },
            new FeatureInfo { Layer = "terrain", LabelKey = "Placemaker.Feature.flatten", TooltipKey = "Placemaker.Feature.terrain.Tooltip", Preset = Park.ParkLayer.Bag(("shape", "flatten")) },
        };

        public IReadOnlyList<FeatureInfo> Features => FeatureList;
        public const string LayerId = "parking";
        private const string K = "Placemaker.Parking.";

        public string Id => LayerId;
        public string NameKey => K + "Name";
        public string Icon => "Media/Game/Icons/Parking.svg";
        public LayerKind Kind => LayerKind.Fill;
        public string[] Accepts => Array.Empty<string>();
        public string[] Emits => new[] { RegionTags.Lot, RegionTags.Green };
        public int Version => 1;
        public float MinArea => 400f;
        public float MaxArea => 300000f;

        private static readonly IReadOnlyList<ControlSchema> _controls = BuildControls();
        private static readonly IReadOnlyList<PresetInfo> _presets = BuildPresets();

        public IReadOnlyList<ControlSchema> Controls => _controls;
        public IReadOnlyList<PresetInfo> Presets => _presets;

        public SettingsBag Defaults()
        {
            var b = new SettingsBag();
            foreach (var c in _controls)
                if (c.Default != null) b.Values[c.Id] = c.Default is string[] a ? (string[])a.Clone() : c.Default;
            return b;
        }

        // ───────────────────────────── schema ─────────────────────────────

        private static ControlOption Opt(string value, string label, string icon = "") =>
            new ControlOption { Value = value, LabelKey = K + label, TooltipKey = K + label + ".Tip", Icon = icon };

        private static ControlSchema C(string id, ControlKind kind, ControlTier tier, string group, object def)
        {
            string label = char.ToUpperInvariant(id[0]) + id.Substring(1);
            return new ControlSchema
            {
                Id = id, Kind = kind, Tier = tier, Group = group, Default = def,
                LabelKey = K + "Control." + label, TooltipKey = K + "Control." + label + ".Tip",
            };
        }

        private static ControlSchema Slider(string id, ControlTier tier, string group, double def, double min, double max, double step, string unit, bool cheap = false)
        {
            var c = C(id, ControlKind.Slider, tier, group, def);
            c.Min = min; c.Max = max; c.Step = step; c.Unit = unit; c.Cheap = cheap;
            return c;
        }

        /// <summary>
        /// Marker kind → icon. The tool (Core/Tool/MarkerKinds) owns verified vanilla icons for entrance,
        /// entry, exit, alley and footpath; the one-way alleys reuse the alley icon; bus stop and zoning
        /// patch use bundled UI glyphs (UI/src/lib/icons.ts) until the tool adds vanilla ones.
        /// </summary>
        internal static string MarkerIcon(string kind)
        {
            switch (kind)
            {
                case ParkingIds.KindAlleyIn: case ParkingIds.KindAlleyOut: return Placemaker.Core.Tool.MarkerKinds.IconFor(ParkingIds.KindAlley);
                case ParkingIds.KindBusStop: return "people";
                case ParkingIds.KindZoningPatch: return "zone";
                default: return Placemaker.Core.Tool.MarkerKinds.IconFor(kind);
            }
        }

        internal static string TouchName(string kind)
        {
            switch (kind)
            {
                case ParkingIds.TouchAccessible: return "Accessible";
                case ParkingIds.TouchCharger: return "Charger";
                case ParkingIds.TouchPlain: return "Plain";
                case ParkingIds.TouchBlank: return "Blank";
                case ParkingIds.TouchIsland: return "Island";
                case ParkingIds.TouchEdge: return "Edge";
                default: return "Other";
            }
        }

        /// <summary>Bundled UI glyphs (UI/src/assets/icons).</summary>
        internal static string TouchIcon(string kind)
        {
            switch (kind)
            {
                case ParkingIds.TouchAccessible: return "people";
                case ParkingIds.TouchCharger: return "sparkle";
                case ParkingIds.TouchPlain: return "car";
                case ParkingIds.TouchBlank: return "zoneEmpty";
                case ParkingIds.TouchIsland: return "shrub";
                case ParkingIds.TouchEdge: return "angle";
                default: return "";
            }
        }

        // Groups in display order — the UI shows every control, so the most used come first.
        internal static readonly string[] GroupOrder = { "entrances", "layout", "spaces", "planting", "lighting", "surfaces", "zoning", "fee", "decals" };

        private static IReadOnlyList<ControlSchema> BuildControls()
        {
            var l = new List<ControlSchema>();

            // ── Entrances ──
            var ent = C(ParkingIds.Entrances, ControlKind.Markers, ControlTier.Essential, "entrances", null);
            ent.Icon = "entrance";
            ent.MarkerKinds = (string[])ParkingIds.MarkerKinds.Clone();
            foreach (var k in ParkingIds.MarkerKinds)
                ent.Options.Add(new ControlOption { Value = k, LabelKey = K + "Marker." + Placemaker.Core.Tool.MarkerKinds.Pascal(k), TooltipKey = K + "Marker." + Placemaker.Core.Tool.MarkerKinds.Pascal(k) + ".Tip", Icon = MarkerIcon(k) });
            l.Add(ent);

            // ── Layout ──
            var angle = C(ParkingIds.StallAngle, ControlKind.Choice, ControlTier.Essential, "layout", "90");
            angle.Options.Add(Opt("90", "StallAngle.90"));
            angle.Options.Add(Opt("60", "StallAngle.60"));
            angle.Options.Add(Opt("45", "StallAngle.45"));
            l.Add(angle);
            // (P2-1) what the grid lines up with, and its turn against it; a picked edge (hand edit) wins over both
            var mode = C(ParkingIds.RowAngleMode, ControlKind.Choice, ControlTier.Essential, "layout", "edge");
            mode.Options.Add(Opt("edge", "RowAngleMode.Edge"));
            mode.Options.Add(Opt("street", "RowAngleMode.Street"));
            mode.Options.Add(Opt("zones", "RowAngleMode.Zones"));
            mode.Options.Add(Opt("across", "RowAngleMode.Across"));
            mode.Options.Add(Opt("fixed", "RowAngleMode.Fixed"));
            l.Add(mode);
            var turn = C(ParkingIds.GridTurn, ControlKind.Choice, ControlTier.Essential, "layout", "0");
            foreach (var t in new[] { "0", "30", "45", "60", "90" }) turn.Options.Add(Opt(t, "GridTurn." + t));
            turn.Options.Add(Opt("free", "GridTurn.Free"));
            turn.VisibleWhen = ParkingIds.RowAngleMode + "!=fixed";
            l.Add(turn);
            var turnFree = Slider(ParkingIds.GridTurnFree, ControlTier.More, "layout", 15, 1, 179, 1, "deg");
            turnFree.VisibleWhen = ParkingIds.RowAngleMode + "!=fixed&" + ParkingIds.GridTurn + "=free";
            l.Add(turnFree);
            var rowAngle = Slider(ParkingIds.RowAngle, ControlTier.More, "layout", 0, 0, 179, 1, "deg");
            rowAngle.VisibleWhen = ParkingIds.RowAngleMode + "=fixed";
            l.Add(rowAngle);
            l.Add(C(ParkingIds.PerimeterRoad, ControlKind.Toggle, ControlTier.More, "layout", true));
            l.Add(Slider(ParkingIds.CrossEvery, ControlTier.More, "layout", ParkingSettings.DefaultCrossEvery, 0, 30, 1, "count"));
            l.Add(Slider(ParkingIds.AisleWidth, ControlTier.More, "layout", ParkingSettings.DefaultAisle, 4, 10, 0.5, "m"));
            l.Add(Slider(ParkingIds.Setback, ControlTier.More, "layout", ParkingSettings.DefaultSetback, 0, 6, 0.5, "m"));
            l.Add(C(ParkingIds.Grading, ControlKind.Toggle, ControlTier.More, "layout", true));

            // ── Spaces ──
            l.Add(Slider(ParkingIds.StallWidth, ControlTier.More, "spaces", ParkingSettings.DefaultStallWidth, 2.5, 4, 0.1, "m"));
            l.Add(Slider(ParkingIds.StallLength, ControlTier.More, "spaces", ParkingSettings.DefaultStallLength, 4.5, 7, 0.1, "m"));
            l.Add(Slider(ParkingIds.AccessibleShare, ControlTier.More, "spaces", 2, 0, 10, 1, "%"));
            l.Add(Slider(ParkingIds.EvShare, ControlTier.More, "spaces", 4, 0, 30, 1, "%"));
            // (P2-1) hand edits on the stalls and the edge the rows line up with (Core/Touches.cs)
            var edits = C(ParkingIds.HandEdits, ControlKind.Touches, ControlTier.Essential, "spaces", null);
            edits.Icon = "edit";
            foreach (var k in ParkingIds.TouchKinds)
            {
                string name = TouchName(k);
                edits.Options.Add(new ControlOption { Value = k, LabelKey = K + "Touch." + name, TooltipKey = K + "Touch." + name + ".Tip", Icon = TouchIcon(k) });
            }
            l.Add(edits);
            l.Add(C(ParkingIds.Markings, ControlKind.Toggle, ControlTier.More, "spaces", true));
            var ch = C(ParkingIds.ChargerAsset, ControlKind.Asset, ControlTier.More, "spaces", "");
            ch.AssetCategory = AssetCategories.Charger; ch.Cheap = true;
            l.Add(ch);

            // ── Planting ──
            var green = C(ParkingIds.Greenery, ControlKind.Choice, ControlTier.Essential, "planting", "medians");
            green.Options.Add(Opt("none", "Greenery.None"));
            green.Options.Add(Opt("edges", "Greenery.Edges"));
            green.Options.Add(Opt("medians", "Greenery.Medians"));
            l.Add(green);
            var median = Slider(ParkingIds.MedianDepth, ControlTier.More, "planting", ParkingSettings.DefaultMedian, 0.5, 8, 0.5, "m");
            median.VisibleWhen = ParkingIds.Greenery + "=medians";
            l.Add(median);
            l.Add(C(ParkingIds.RowEndPlanting, ControlKind.Toggle, ControlTier.More, "planting", true));
            l.Add(Slider(ParkingIds.PlantingDensity, ControlTier.More, "planting", 50, 0, 100, 5, "%"));
            var trees = C(ParkingIds.Trees, ControlKind.AssetMulti, ControlTier.More, "planting", Array.Empty<string>());
            trees.AssetCategory = AssetCategories.Tree;
            l.Add(trees);
            var bushes = C(ParkingIds.Bushes, ControlKind.AssetMulti, ControlTier.More, "planting", Array.Empty<string>());
            bushes.AssetCategory = AssetCategories.Bush;
            l.Add(bushes);

            // ── Lighting ──
            l.Add(C(ParkingIds.Lanterns, ControlKind.Toggle, ControlTier.More, "lighting", true));
            var la = C(ParkingIds.LanternAsset, ControlKind.Asset, ControlTier.More, "lighting", "");
            la.AssetCategory = AssetCategories.Lamp; la.Cheap = true; la.VisibleWhen = ParkingIds.Lanterns + "=true";
            l.Add(la);
            var ls = Slider(ParkingIds.LanternSpacing, ControlTier.More, "lighting", 30, 20, 40, 1, "m");
            ls.VisibleWhen = ParkingIds.Lanterns + "=true";
            l.Add(ls);

            // ── Surfaces ──
            var asphalt = C(ParkingIds.AsphaltSurface, ControlKind.Asset, ControlTier.Essential, "surfaces", "");
            asphalt.AssetCategory = AssetCategories.SurfaceAsphalt; asphalt.Cheap = true;
            l.Add(asphalt);
            var gs = C(ParkingIds.GreenSurface, ControlKind.Asset, ControlTier.More, "surfaces", "");
            gs.AssetCategory = AssetCategories.SurfaceGrass; gs.Cheap = true;
            l.Add(gs);

            // ── Zoning (buildings along the street) ──
            var ez = C(ParkingIds.EdgeZoning, ControlKind.Choice, ControlTier.More, "zoning", "none");
            ez.Options.Add(Opt("none", "EdgeZoning.None"));
            ez.Options.Add(Opt("oneSide", "EdgeZoning.OneSide"));
            ez.Options.Add(Opt("all", "EdgeZoning.All"));
            l.Add(ez);
            var zt = C(ParkingIds.ZoneType, ControlKind.Asset, ControlTier.More, "zoning", "");
            zt.AssetCategory = AssetCategories.Zone; zt.Cheap = true;
            l.Add(zt);
            l.Add(Slider(ParkingIds.ZoningDepth, ControlTier.More, "zoning", 3, 1, ParkingSettings.MaxZoneDepthCells, 1, "count"));
            // (ZoningPatchWidth has no slider: the building plot marker it sizes is not offered any more, ParkingIds.MarkerKinds.)

            // ── Fee ──
            // (P2-1) up front: the fee was hard to find (a creator turned fees off elsewhere); 0 = free. Changeable later on the click panel.
            l.Add(Slider(ParkingIds.Fee, ControlTier.Essential, "fee", 10, 0, 50, 1, "money", cheap: true));

            // ── Decals (s21): oil in the bays, tyre marks, litter under lot trees — Core/Decals/Dressing ──
            var groups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in l) groups.Add(c.Group);
            l.AddRange(Decals.DressingControls.For(LayerId, groups.Contains));
            return l;
        }

        private static IReadOnlyList<PresetInfo> BuildPresets()
        {
            PresetInfo P(string id, params (string Key, object Value)[] values)
            {
                var p = new PresetInfo { Id = id, LabelKey = K + "Preset." + id };
                foreach (var v in values) p.Values.Values[v.Key] = v.Value;
                p.Values.Values[Decals.DressingControls.StyleId] = Decals.DressingControls.Styles[(int)Decals.DressingProfiles.StyleFor(LayerId, id)];
                return p;
            }
            return new[]
            {
                P("standard", (ParkingIds.StallAngle, "90"), (ParkingIds.Greenery, "medians"), (ParkingIds.PerimeterRoad, true)),
                P("compact", (ParkingIds.StallAngle, "90"), (ParkingIds.Greenery, "edges"), (ParkingIds.PerimeterRoad, true), (ParkingIds.CrossEvery, 0.0)),
                P("green", (ParkingIds.StallAngle, "90"), (ParkingIds.Greenery, "medians"), (ParkingIds.MedianDepth, 4.0), (ParkingIds.PlantingDensity, 80.0), (ParkingIds.CrossEvery, 8.0)),
                P("angled", (ParkingIds.StallAngle, "60"), (ParkingIds.Greenery, "medians"), (ParkingIds.PerimeterRoad, true)),
            };
        }

        public SettingsBag Randomize(SettingsBag current, uint seed, ICollection<string> locked)
        {
            var r = (current ?? Defaults()).Clone();
            var rng = Seeds.Rng(seed == 0 ? 1u : seed, "parking.randomize");
            bool Free(string id) => locked == null || !locked.Contains(id);
            string[] angles = { "90", "90", "60", "45" };
            if (Free(ParkingIds.StallAngle)) r.Values[ParkingIds.StallAngle] = angles[rng.NextInt(angles.Length)];
            string[] greens = { "medians", "medians", "edges", "none" };
            if (Free(ParkingIds.Greenery)) r.Values[ParkingIds.Greenery] = greens[rng.NextInt(greens.Length)];
            if (Free(ParkingIds.MedianDepth)) r.Values[ParkingIds.MedianDepth] = Math.Round(rng.NextDouble(1.5, 4.5) * 2) / 2;
            if (Free(ParkingIds.CrossEvery)) r.Values[ParkingIds.CrossEvery] = (double)rng.NextInt(0, 15);
            if (Free(ParkingIds.PerimeterRoad)) r.Values[ParkingIds.PerimeterRoad] = rng.NextFloat() < 0.75f;
            if (Free(ParkingIds.RowEndPlanting)) r.Values[ParkingIds.RowEndPlanting] = rng.NextFloat() < 0.7f;
            if (Free(ParkingIds.LanternSpacing)) r.Values[ParkingIds.LanternSpacing] = (double)rng.NextInt(20, 41);
            if (Free(ParkingIds.PlantingDensity)) r.Values[ParkingIds.PlantingDensity] = (double)(rng.NextInt(6, 17) * 5);
            return r;
        }

        // ───────────────────────────── readiness & markers ─────────────────────────────

        public Readiness CheckReady(AreaInput input)
        {
            // PLT FehlenderZugang: decided by DIRECTION, not by build type. A two-way entrance or alley
            // is enough; one-way kinds need one in and one out; footpaths never count (people only).
            int two = 0, inbound = 0, outbound = 0, vehicles = 0;
            if (input?.Markers != null)
                foreach (var m in input.Markers)
                {
                    if (!EntranceKinds.TryParse(m.Kind, out var k) || !EntranceKinds.ForCars(k)) continue;
                    vehicles++;
                    if (!EntranceKinds.OneWay(k)) two++;
                    else if (EntranceKinds.Outbound(k)) outbound++;
                    else inbound++;
                }
            // ReasonArgs: [minimum vehicle entrances, vehicle entrances placed] (the schema has no field for it yet)
            var args = new object[] { ParkingIds.MinVehicleEntrances, vehicles };
            if (two > 0 || (inbound > 0 && outbound > 0)) return new Readiness { Ready = true, ReasonArgs = args };
            string reason = inbound > 0 ? K + "Reason.EntryWithoutExit"
                : outbound > 0 ? K + "Reason.ExitWithoutEntry"
                : K + "Reason.NeedsEntrance";
            return new Readiness { Ready = false, NextStepKey = K + "Step.PlaceEntrance", ReasonKey = reason, ReasonArgs = args };
        }

        public List<EdgeMarker> SuggestMarkers(AreaInput input)
        {
            try
            {
                return EntranceSuggest.Suggest(input, new ParkingSettings(input?.Settings));
            }
            catch (Exception)
            {
                return new List<EdgeMarker>();
            }
        }

        // ───────────────────────────── plan ─────────────────────────────

        public AreaPlan Plan(AreaInput input, CancellationToken cancel)
        {
            var plan = new AreaPlan { GeneratorId = LayerId };
            if (input?.Shape?.Ring == null || input.Shape.Ring.Length < 3)
            {
                plan.Issues.Add(new PlanIssue { Severity = IssueSeverity.Error, Key = K + "Issue.BadShape" });
                return plan;
            }
            try
            {
                return PlanBuilder.Build(input, new ParkingSettings(input.Settings), this, cancel);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                plan.Issues.Add(new PlanIssue
                {
                    Severity = IssueSeverity.Error, Key = K + "Issue.LayoutFailed",
                    Args = new object[] { e.GetType().Name + ": " + e.Message },
                });
                return plan;
            }
        }
    }
}
