// Writes a game-runner scenario for the beach styles on real shores (docs/runbooks/beach.md, p2-beaches).
// node tools/suite/beach-scenario.js <out.json> <styles|all> [lands] [views]
//   styles: comma list of beach preset ids, or "all" (the shelf's visible ones)
//   lands:  comma list of lake | lakein | cliff | steep | lagoon | small (default lake)
//           lakein: the lake land, the outline drawn 45 m out over the water (islands and water rocks need it)
//   views:  comma list of 45 | top | close | joints | low | terrain (default 45,top,close,joints,terrain)
// Every build runs the simulation 30 s afterwards so the water flows into carved bays, creeks and round headlands.
// "joints" frames every place a deck (jetty, boardwalk, footbridge) meets a ground path: camera target "joint", index "all".
const fs = require("fs");
const [, , out, stylesArg = "all", landsArg = "lake", viewsArg = "45,top,close,joints,terrain"] = process.argv;
const visible = ["sandy", "resort", "cove", "wild", "shingle", "mulch", "granite", "ravine", "marsh", "harbour"];
const styles = stylesArg === "all" ? visible : stylesArg.split(",");
const lands = {
  lake: { size: [200, 110], water: { side: "north", width: 90 } },
  cliff: { size: [180, 100], water: { side: "north", freeboard: 7, bank: 120 } },
  steep: { size: [180, 100], water: { side: "north", freeboard: 3, bank: 35 } },
  lagoon: { size: [240, 160], ground: "slope", grade: 4, toward: "north", water: { side: "north", width: 120 } },
  small: { size: [90, 70], water: { side: "north", width: 70 } },
  lakein: { size: [200, 110], water: { side: "north", width: 90 }, area: { do: "area", tool: "points", points: [[-100, -55], [100, -55], [100, 100], [-100, 100]] } },
};
const views = viewsArg.split(",");
const shot = (v) => {
  switch (v) {
    case "45": return [{ do: "camera", target: "build", view: "oblique", pitch: 45, shot: "45", shotMode: "render" }];
    case "top": return [{ do: "camera", target: "build", view: "top", shot: "top", shotMode: "render" }];
    case "close": return [{ do: "camera", target: "build", view: "closeup", shot: "close", shotMode: "render" }];
    case "low": return [{ do: "camera", target: "build", view: "low", shot: "low", shotMode: "render" }];
    case "joints": return [{ do: "camera", target: "joint", index: "all", view: "closeup", distance: 24, pitch: 32, shot: "joint", shotMode: "render" }];
    case "terrain": return [{ do: "terrain" }];
    default: throw new Error("unknown view " + v);
  }
};
const scenarios = [];
for (const land of landsArg.split(","))
  for (const style of styles) {
    const { area, ...landDef } = lands[land];
    const l = Object.assign({ gap: 10, roads: "around" }, landDef);
    scenarios.push({
      id: `beach-${style}-${land}`, site: "flat", pace: "settled", land: l,
      steps: [
        area || { do: "area", tool: "land" }, { do: "type", layer: "beach", preset: style }, { do: "entrances" }, { do: "preview" },
        { do: "build" }, { do: "grow", seconds: 30 }, ...views.flatMap(shot),
      ],
    });
  }
fs.writeFileSync(out, JSON.stringify({ _about: "Beach styles on real shores (tools/suite/beach-scenario.js)", scenarios }, null, 2));
console.log(out + ": " + scenarios.length + " scenarios");
