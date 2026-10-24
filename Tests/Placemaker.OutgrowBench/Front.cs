// Outgrow benchmark: fence and hedge pieces, and the houses' own lines, that a plan leaves in front of a house (between it
// and its street: --front <passes dir> [...]). The user, 23 Oct, row houses: "fences sticking out in front of them".

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;

namespace Placemaker.OutgrowBench
{
    internal static class Front
    {
        public static int Run(List<string> dirs)
        {
            int passes = 0, pieces = 0, lines = 0;
            var examples = new List<string>();
            foreach (var dir in dirs)
                foreach (var f in Directory.GetFiles(dir, "pass-*.json").OrderBy(x => x, StringComparer.Ordinal))
                {
                    var (houses, settings) = StitchDump.Read(File.ReadAllText(f));
                    var plan = YardStitch.Plan(houses, settings);
                    passes++;
                    var byId = houses.ToDictionary(h => h.Id);
                    string name = Path.GetFileName(Path.GetDirectoryName(dir.TrimEnd('/', '\\'))) + "/" + Path.GetFileNameWithoutExtension(f);
                    // in front: the front third of its own lot, or past its front edge (toward the street)
                    bool InFront(StitchHouse h, float2 p)
                    {
                        var l = h.ToLocal(p);
                        return math.abs(l.x) < h.HalfLot.x + 0.5f && l.y > h.HalfLot.y * 0.33f;
                    }
                    foreach (var (house, at, yaw, piece) in plan.FencePieces)
                    {
                        if (!byId.TryGetValue(house, out var h) || !h.Edit || !InFront(h, at)) continue;
                        pieces++;
                        if (examples.Count < 40) examples.Add($"{name} {house % 1000} piece {piece} at local {h.ToLocal(at)} (lot half {h.HalfLot})");
                    }
                    foreach (var h in houses.Where(h => h.Edit))
                        foreach (var l in h.Lines)
                        {
                            if (plan.HideLines.Contains(l.Id) || !plan.MoveLines.TryGetValue(l.Id, out var m)) continue;
                            // a line moved so that it now lies across the front (it was not there before)
                            bool before = InFront(h, (l.A + l.B) * 0.5f);
                            bool after = InFront(h, (m.A + m.B) * 0.5f);
                            if (!after || before) continue;
                            lines++;
                            if (examples.Count < 40) examples.Add($"{name} {h.Id % 1000} line {l.Name} moved to local {h.ToLocal(m.A)}..{h.ToLocal(m.B)} (was {h.ToLocal(l.A)}..{h.ToLocal(l.B)}, lot half {h.HalfLot})");
                        }
                }
            Console.WriteLine($"{passes} passes: {pieces} pieces and {lines} moved lines in front of their houses");
            foreach (var e in examples) Console.WriteLine("  " + e);
            return 0;
        }
    }
}
