// AI builder against the premade types: per type, the type itself (its default style) and the AI given one plain
// request, each on the same made-to-order land, one 45° picture each. Writes suite/scenarios/<name>.json.
//   node tools/suite/ai-vs-types.js <name> <type>=<request> ...
const fs = require("fs");
const [name, ...pairs] = process.argv.slice(2);
const land = { shape: "rect", size: [140, 110], ground: "flat", roads: "around", gap: 10 };
const shot = { do: "shot", name: "45-built", camera: { target: "build", view: "oblique" }, shotMode: "render" };
const scenarios = [];
pairs.forEach((p, i) => {
  const [type, request] = p.split("=");
  scenarios.push({ id: `type-${type}`, land, steps: [{ do: "area", tool: "land" }, { do: "type", layer: type }, { do: "entrances" }, { do: "build" }, shot] });
  const ai = { id: `ai-${type}`, land, steps: [] };
  if (i === 0) ai.steps.push({ do: "setting", name: "AiBuilderOn", value: true }, { do: "setting", name: "AiModel", value: "anthropic/claude-sonnet-5.5" }, { do: "setting", name: "AiThinking", value: 1 });
  ai.steps.push({ do: "area", tool: "land" }, { do: "type", layer: "ai" }, { do: "aiAgent", prompt: request, timeout: 600 }, { do: "build" }, shot);
  if (i === pairs.length - 1) ai.steps.push({ do: "setting", name: "AiBuilderOn", value: false });
  scenarios.push(ai);
});
fs.writeFileSync(`suite/scenarios/${name}.json`, JSON.stringify({ _about: "AI builder against the premade types (tools/suite/ai-vs-types.js): per type its default style, then the AI given one plain request, on the same land.", scenarios }, null, 2) + "\n");
console.log(scenarios.length + " scenarios");
