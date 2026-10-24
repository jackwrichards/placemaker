// Outgrow benchmark: the neighbours a plan joined (--pairs pass.json [ids...]): side pairs and back to back pairs with their gaps,
// for the houses whose ids end in the given digits (all when none).

using System;
using System.IO;
using System.Linq;
using Placemaker.Core.Neighbours;

namespace Placemaker.OutgrowBench
{
    internal static class Pairs
    {
        public static int Run(string file, string[] only, string knobs)
        {
            var (houses, settings) = StitchDump.Read(File.ReadAllText(file));
            Score.Apply(houses, settings, Score.ParseKnobs(knobs));
            var plan = YardStitch.Plan(houses, settings);
            bool Wanted(long id) => only.Length == 0 || only.Any(o => (id % 1000).ToString() == o);
            foreach (var p in plan.Pairs)
                if (Wanted(p.Left) || Wanted(p.Right))
                {
                    var a = houses.First(h => h.Id == p.Left);
                    var b = houses.First(h => h.Id == p.Right);
                    float dot = Unity.Mathematics.math.dot(a.Forward, b.Forward);
                    Console.WriteLine($"{p.Left % 1000} {(p.Rear ? "behind/facing" : "beside")} {p.Right % 1000}: gaps {p.FrontGap:F1} / {p.BackGap:F1}, facing {Unity.Mathematics.math.degrees(Unity.Mathematics.math.acos(Unity.Mathematics.math.clamp(dot, -1f, 1f))):F0}° apart, edited {a.Edit}/{b.Edit}");
                }
            return 0;
        }
    }
}
