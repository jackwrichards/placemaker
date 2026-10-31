// Writes a game-runner scenario for Biome mix checks (docs/runbooks/biomes.md).
// node tools/suite/biome-scenario.js <out.json> <name>:<style>:<mix|->[:<land>[:<WxD>]] ...
//   type: a name of the form <type>/<style> as the style picks another type (botanicalGarden/flowerShow); water and wetland
//   mixes run the simulation 45 s after the build so the water fills
//   mix: "rocky=100" or "lawn=40,forest=30,rocky=30" (every other offered biome 0), "-" = the style's own; land: flat|hills|mountains|slope
const fs = require("fs");
const ids = ["lawn", "grass", "field", "meadow", "flowers", "mulch", "glade", "forest", "rocky", "heath", "wetland", "water"];
const cap = (s) => "biome" + s[0].toUpperCase() + s.slice(1);
const [, , out, ...specs] = process.argv;
const scenarios = specs.map((spec) => {
  let [name, styleSpec, mix, land = "flat", size = "220x200"] = spec.split(":");
  const [layer, style] = styleSpec.includes("/") ? styleSpec.split("/") : ["park", styleSpec];
  const wet = /water|wetland|riverside/.test(spec) || layer !== "park";
  const [w, d] = size.split("x").map(Number);
  const values = [];
  // the panel keeps edits on a style (like a file): a natural build after a custom mix on natural would inherit it, so the
  // natural style gets its own mix written out (ParkLayer natural preset)
  if ((!mix || mix === "-") && layer === "park" && style === "natural") mix = "lawn=40,meadow=16,glade=10,forest=18,rocky=10,wetland=6,scale=74";
  if (mix && mix !== "-") {
    const m = Object.fromEntries(mix.split(",").map((x) => x.split("=")).map(([k, v]) => [k, Number(v)]));
    for (const id of ids) if (id !== "scale") values.push({ do: "value", id: cap(id), json: String(m[id] ?? 0) });
    if (m.scale !== undefined) values.push({ do: "value", id: "biomeScale", json: String(m.scale) });
  }
  const l = { size: [w, d], gap: 10 };
  if (land !== "flat") { l.ground = land; if (land === "slope") l.grade = 0.08; }
  return {
    id: name, site: "flat", pace: "settled", land: l,
    steps: [
      { do: "area", tool: "land" }, { do: "type", layer, preset: style }, ...values,
      { do: "entrances" }, { do: "build" }, ...(wet ? [{ do: "grow", seconds: 45 }] : []),
      { do: "camera", target: "build", view: "oblique", pitch: 45, shot: "45", shotMode: "render" },
      { do: "camera", target: "build", view: "top", shot: "top", shotMode: "render" },
      { do: "camera", target: "build", view: "closeup", shot: "close", shotMode: "render" },
      { do: "terrain" },
    ],
  };
});
fs.writeFileSync(out, JSON.stringify({ _about: "Biome mix check (tools/suite/biome-scenario.js)", scenarios }, null, 2));
console.log(out + ": " + scenarios.length + " scenarios");
