// OutgrowBench --preview <out.svg> [--knob k=v ...]: the garden picture of Outgrow's editor (Core GardenPreview), drawn as the
// panel draws it, with the look's default values and the knobs given.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Placemaker.Core.Neighbours;
using Unity.Mathematics;

namespace Placemaker.OutgrowBench
{
    public static class Preview
    {
        public static int Run(List<string> args)
        {
            var values = new Dictionary<string, float>(PolishLooks.Defaults);
            for (int i = 1; i < args.Count; i++)
                if (args[i] == "--knob") { var kv = args[++i].Split('='); values[kv[0]] = float.Parse(kv[1], CultureInfo.InvariantCulture); }
            var pic = GardenPreview.Plan(values);
            pic = GardenPreview.Plan(values);   // (the second: warm)
            const float S = 3f;
            float w = (pic.Hi.x - pic.Lo.x) * S, h = (pic.Hi.y - pic.Lo.y) * S;
            string P(float2[] r) => string.Join(" ", r.Select(p => ((p.x - pic.Lo.x) * S).ToString("F1", CultureInfo.InvariantCulture) + "," + ((pic.Hi.y - p.y) * S).ToString("F1", CultureInfo.InvariantCulture)));
            var sb = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w:F0}\" height=\"{h + 30:F0}\"><rect width=\"100%\" height=\"100%\" fill=\"#e9e4d8\"/>");
            foreach (var r in pic.Roads) sb.Append($"<polygon points=\"{P(r)}\" fill=\"#8d8d8d\"/>");
            foreach (var g in pic.Gardens) sb.Append($"<polygon points=\"{P(g)}\" fill=\"#a8d08d\" stroke=\"#3d6b2a\" stroke-width=\"2\"/>");
            foreach (var l in pic.Lots) sb.Append($"<polygon points=\"{P(l)}\" fill=\"none\" stroke=\"#ffffff\" stroke-width=\"1.2\" stroke-dasharray=\"4 3\"/>");
            foreach (var b in pic.Houses) sb.Append($"<polygon points=\"{P(b)}\" fill=\"#c96f4a\"/>");
            sb.Append($"<text x=\"8\" y=\"{h + 22:F0}\" font-family=\"system-ui\" font-size=\"16\">{string.Join("  ", args.Skip(1).Where(a => a != "--knob"))}  ({pic.Ms:F1} ms)</text></svg>");
            File.WriteAllText(args[0], sb.ToString());
            Console.WriteLine($"{args[0]}: {pic.Gardens.Count} gardens, {pic.Ms:F1} ms");
            return 0;
        }
    }
}
