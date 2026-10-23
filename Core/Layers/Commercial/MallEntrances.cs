// rf-shops3 — entrances for the shops types, as in ParkingLotTool and Placemaker's parking type. The user: "I want to be able to
// set entrances and exits almost exactly like I can in the parking lot tool. And it wouldn't do anything unless I did."
//
// The player places them on the outline; nothing is suggested and Build waits for a way in for cars (a two-way entrance or
// driveway, or one way in plus one way out; a footpath alone is for people). The layout is planned from them:
//   entrance / entry / exit   a drive from the kerb (the street is not changed: PLT's Zufahrt) into the lot it faces or the
//                             nearest lane of the site (one-way kinds get arrows);
//   alley / alleyIn / alleyOut a real junction (the street is split): a lane from the street to the site's nearest lane
//                             (the service way, also through a row of shops: an alley);
//   footpath                  a walk from the sidewalk to the nearest lane, plaza or spine (the crosswalk at the lane).
// The marker kinds are the parking type's (same ids, icons and words), so a player who knows the parking tool knows these.

using System.Collections.Generic;
using Placemaker.Core.Geometry;
using Placemaker.Core.Layers.Common;
using Unity.Mathematics;

namespace Placemaker.Core.Layers.Commercial
{
    public static class ShopEntrances
    {
        public const string ControlId = "entrances";
        public const string TwoWay = "entrance", Entry = "entry", Exit = "exit", Alley = "alley", AlleyIn = "alleyIn", AlleyOut = "alleyOut", Footpath = "footpath", BusStop = "busStop";
        /// <summary>The marker kinds the panel offers, in display order. Bus stop is not offered (as for parking, ParkingIds.MarkerKinds); designs that have one still build it.</summary>
        public static readonly string[] Kinds = { TwoWay, Entry, Exit, Alley, AlleyIn, AlleyOut, Footpath };
        private const string K = "Placemaker.Commercial.";

        public static bool IsKind(string k) => k == BusStop || System.Array.IndexOf(Kinds, k) >= 0;
        public static bool ForCars(string k) => IsKind(k) && k != Footpath && k != BusStop;
        public static bool IsAlley(string k) => k == Alley || k == AlleyIn || k == AlleyOut;
        public static bool OneWay(string k) => k == Entry || k == Exit || k == AlleyIn || k == AlleyOut;
        /// <summary>Cars only drive out here.</summary>
        public static bool Outbound(string k) => k == Exit || k == AlleyOut;

        /// <summary>The "entrances" Markers control (essential, first group): the parking type's kinds, words and icons.</summary>
        public static ControlSchema Control(ControlSet c)
        {
            var m = c.Markers(ControlId, ControlTier.Essential, Kinds);
            m.Icon = "entrance";
            m.LabelKey = K + "entrances";
            m.TooltipKey = K + "entrances.Tooltip";
            foreach (var k in Kinds)
            {
                string key = "Placemaker.Parking.Marker." + Tool.MarkerKinds.Pascal(k);
                m.Options.Add(new ControlOption { Value = k, LabelKey = key, TooltipKey = key + ".Tip", Icon = Tool.MarkerKinds.IconFor(k == AlleyIn || k == AlleyOut ? Alley : k) });
            }
            return m;
        }

        /// <summary>
        /// PLT FehlenderZugang: a two-way entrance or driveway is enough; one-way kinds need one in and one out; footpaths never
        /// count (people only). Until then Build waits and the panel asks for an entrance.
        /// </summary>
        public static Readiness Ready(AreaInput input)
        {
            int two = 0, inbound = 0, outbound = 0, cars = 0;
            if (input?.Markers != null)
                foreach (var m in input.Markers)
                {
                    if (!ForCars(m.Kind)) continue;
                    cars++;
                    if (!OneWay(m.Kind)) two++;
                    else if (Outbound(m.Kind)) outbound++;
                    else inbound++;
                }
            var args = new object[] { 1, cars };
            if (two > 0 || (inbound > 0 && outbound > 0)) return new Readiness { Ready = true, ReasonArgs = args };
            string reason = inbound > 0 ? "Placemaker.Parking.Reason.EntryWithoutExit"
                : outbound > 0 ? "Placemaker.Parking.Reason.ExitWithoutEntry"
                : K + "Reason.NeedsEntrance";
            return new Readiness { Ready = false, NextStepKey = "Placemaker.Parking.Step.PlaceEntrance", ReasonKey = reason, ReasonArgs = args };
        }

        /// <summary>One placed entrance, on the outline, with the street it faces (when one is within reach).</summary>
        public sealed class Gate
        {
            public string Kind;
            public int Index;
            /// <summary>On the outline, and the inward normal there.</summary>
            public float2 At, In;
            public int Segment;
            public float T;
            /// <summary>The street it joins (a street within reach outside it), its id, curve parameter and the point on its centre line.</summary>
            public bool HasStreet;
            public long StreetId = -1;
            public float StreetT;
            public float2 StreetPoint;
            /// <summary>What the layout made of it ("lot", "lane", "walk", "none"), for the plan facts and the tests.</summary>
            public string Landed = "none";
            public bool Cars => ForCars(Kind);
        }

        /// <summary>The player's entrances of the shops kinds, snapped onto the outline (in marker order).</summary>
        public static List<Gate> Resolve(LayerContext ctx)
        {
            var list = new List<Gate>();
            var region = ctx.LayerRegion;
            if (region == null || ctx.Input?.Markers == null) return list;
            int i = 0;
            foreach (var m in ctx.Input.Markers)
            {
                if (!IsKind(m.Kind)) continue;
                var p = region.ClosestOnBoundary(m.Position.xz, out int seg, out float t);
                var inward = region.InwardNormal(seg);
                var e = new Gate { Kind = m.Kind, Index = i++, At = p, In = inward, Segment = seg, T = t };
                if (StreetShops.StreetEnd(ctx.Input, p, -inward, out var hit, out long id, out float st))
                {
                    e.HasStreet = true; e.StreetId = id; e.StreetT = st; e.StreetPoint = hit;
                }
                list.Add(e);
            }
            return list;
        }
    }
}
