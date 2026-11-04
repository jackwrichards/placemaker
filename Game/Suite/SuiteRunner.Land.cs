// Game runner: land made to order for a scenario that arrives in a running queue session (inbox). At launch every
// scenario's land is laid out with the fixture; a scenario sent later gets its land the same way when it starts: east
// of the last plot, the map's trees and props cleared off it, its ground written into the heightmap (only beyond what
// is already laid out, so standing builds keep their ground), then its ring road. The game queue (tools/suite/
// Game-Dispatcher.ps1) relies on this to feed land requests into an open game instead of relaunching.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Placemaker.Core.Suite;
using Unity.Mathematics;
using UnityEngine;

namespace Placemaker.Game.Suite
{
    public sealed partial class SuiteRunner
    {
        /// <summary>Lays out <paramref name="s"/>'s land now; leaves s.Block null (with the reason in SiteNote) when it does not fit.</summary>
        private IEnumerator LayOutPlotNow(Scenario s)
        {
            var plot = s.Plot;
            var L = _fixture.Layout;
            float2 keepMin = L.FixtureMin, keepMax = L.FixtureMax;
            var block = _fixture.AddPlot(plot);
            if (!_fixture.PlotFits(plot))
            {
                _fixture.RemovePlot(plot, block);
                s.SiteNote = $"no room left on the map for land {plot.Size.x:F0} x {plot.Size.y:F0} m east of the {L.Plots.Count} plot(s) this session laid out (a fresh launch has room)";
                Say("land: " + s.SiteNote);
                yield break;
            }
            float t0 = Time.realtimeSinceStartup;
            string phase = _phase;
            _phase = "land";
            yield return Guard(CloseTool(), e => Say("close tool: " + e.Message));
            string cleared = "";
            yield return ApplyWithFixtureTool(t => _fixture.EmitClear(t, out cleared, plot.OuterMin, plot.OuterMax), $"clear land {plot.Name}");
            int texels = 0;
            string err = _cfg.noTerrain ? "skipped (noTerrain)" : _fixture.GrowHeightmap(keepMin, keepMax, out texels);
            yield return Frames(40);
            if (plot.RoadsAround)
            {
                bool Inside(float2 p) => p.x >= plot.OuterMin.x && p.x <= plot.OuterMax.x && p.y >= plot.OuterMin.y && p.y <= plot.OuterMax.y;
                yield return ApplyWithFixtureTool(t => _fixture.EmitRoads(t, seg => Inside(seg.A) && Inside(seg.B)), $"land {plot.Name} ring road");
                yield return Frames(30);
            }
            var extra = plot.NetSegments();
            if (extra.Count > 0) { yield return ApplyWithFixtureTool(t => _fixture.EmitNets(t, extra), $"land {plot.Name} nets"); yield return Frames(30); }
            if (plot.HasWater) yield return FillWater(new[] { plot });
            yield return Settle("land " + plot.Name);
            _phase = phase;
            s.Block = _fixture.PlotBlock(plot);
            _summary["plots"] = new JArray(L.Plots.Select(p => $"{p.Name}: {p.Shape} {p.Size.x:F0} x {p.Size.y:F0} m, ground {p.Ground}, roads {(p.RoadsAround ? "around" : "none")}"));
            var rec = _summary["landLaidOutLater"] as JArray ?? new JArray();
            rec.Add(new JObject { ["plot"] = plot.Name, ["scenario"] = s.Id, ["sec"] = Math.Round(Time.realtimeSinceStartup - t0, 1), ["texels"] = texels, ["heightmap"] = err ?? "patched", ["cleared"] = cleared });
            _summary["landLaidOutLater"] = rec;
            Say($"land {plot.Name} laid out in the running session for {s.Id}: {plot.Shape} {plot.Size.x:F0} x {plot.Size.y:F0} m, ground {plot.Ground}, {texels} texels, cleared {cleared}, {Time.realtimeSinceStartup - t0:F1} s");
        }

        /// <summary>
        /// (rf-beach) Puts the plots' lakes in: their water sources, then the simulation runs (speed as for growing, no fast
        /// spawn) until the water next to each shore is about as deep as the lake bed asks for (at most 90 s), and pauses.
        /// </summary>
        private IEnumerator FillWater(IEnumerable<SuitePlot> plots)
        {
            var wet = plots.Where(p => p != null && p.HasWater).ToList();
            if (wet.Count == 0) yield break;
            int sources = 0;
            foreach (var p in wet) sources += _fixture.AddWater(p);
            yield return Frames(10);
            var sim = _world.GetOrCreateSystemManaged<global::Game.Simulation.SimulationSystem>();
            sim.selectedSpeed = _cfg.speed > 0 ? _cfg.speed : 4f;
            float t0 = Time.realtimeSinceStartup, full = -1f;
            string seen = "";
            while (Time.realtimeSinceStartup - t0 < 90f)
            {
                yield return Seconds(1f);
                var probes = wet.SelectMany(p => _fixture.WaterProbe(p)).ToList();
                seen = string.Join(", ", probes.Select(q => $"{q.have:F2}/{q.want:F1}"));
                bool ok = probes.Count > 0 && probes.All(q => q.have >= q.want * 0.8f);
                if (ok && full < 0f) full = Time.realtimeSinceStartup;
                if (full > 0f && Time.realtimeSinceStartup - full > 4f) break;
            }
            sim.selectedSpeed = 0f;
            yield return Frames(10);
            string what = $"water for {string.Join(", ", wet.Select(p => p.Name))}: {sources} source(s), "
                + (full > 0f ? $"full after {full - t0:F0} s" : "NOT full after 90 s") + $"; depth/expected at the shore: {seen}";
            var rec = _summary["water"] as JArray ?? new JArray();
            rec.Add(what);
            _summary["water"] = rec;
            Say(what);
        }
    }
}
