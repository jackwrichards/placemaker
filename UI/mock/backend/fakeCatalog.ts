// Fallback catalog for the mock: plausible layers covering every control kind, used whenever the
// mock host has no real layer with that id (other workstreams write the real ones in parallel).
// Shapes follow contract.ts plus the proposed `default` on controls and preset `values`.
import { Control, LayerType, RecipeInfo, SettingValue } from "../../src/contract";

export type MockControl = Control & { default?: SettingValue };
export type MockLayer = Omit<LayerType, "controls" | "presets"> & {
    controls: MockControl[];
    presets: { id: string; name: string; icon?: string; values?: Record<string, SettingValue> }[];
    minArea?: number;
    /** Typed marker kinds (StepView.markerKinds) and how many markers Build needs (StepView.markersRequired). */
    markerKinds?: { kind: string; label: string; icon?: string; tooltip?: string; crosswalk?: boolean }[];
    markersRequired?: number;
    real?: boolean;
    /** Built-in features (the mock host sends Core's IHasFeatures; fake layers use FAKE_FEATURES). */
    features?: FeatureDef[];
};

const opt = (value: string, label: string, icon?: string, tooltip?: string) => ({ value, label, icon, tooltip });

export const FAKE_LAYERS: MockLayer[] = [
    {
        id: "park", name: "Placemaker.Park.Name", icon: "park", kind: "fill", accepts: [], emits: ["green", "core"],
        controls: [
            { id: "style", kind: "choice", tier: "essential", group: "", label: "Placemaker.Park.Style", tooltip: "Placemaker.Park.StyleTip",
              options: [opt("formal", "Placemaker.Park.Style.Formal", "plaza"), opt("natural", "Placemaker.Park.Style.Natural", "leaf"), opt("meadow", "Placemaker.Park.Style.Meadow", "tree")], default: "natural" },
            { id: "pathWidth", kind: "slider", tier: "essential", group: "", label: "Placemaker.Park.PathWidth", min: 2, max: 8, step: 0.5, unit: "m", default: 4, cheap: false },
            { id: "trees", kind: "slider", tier: "essential", group: "", label: "Placemaker.Park.TreeDensity", tooltip: "Placemaker.Park.TreeDensityTip", min: 0, max: 100, step: 5, unit: "%", default: 60, cheap: true },
            { id: "furniture", kind: "toggle", tier: "essential", group: "", label: "Placemaker.Park.Furniture", default: true, cheap: true },
            { id: "pathAsset", kind: "asset", tier: "more", group: "paths", label: "Placemaker.Park.PathAsset", assetCategory: "net.pathway", default: "" },
            { id: "treeSpecies", kind: "assetMulti", tier: "more", group: "planting", label: "Placemaker.Park.TreeSpecies", assetCategory: "tree", default: [] },
            { id: "entrances", kind: "markers", tier: "more", group: "paths", label: "Placemaker.Park.Entrances", markerKinds: ["entrance"] },
            { id: "lake", kind: "toggle", tier: "more", group: "water", label: "Placemaker.Park.Lake", tooltip: "Placemaker.Park.LakeTip", default: false },
            { id: "lakeSize", kind: "slider", tier: "more", group: "water", label: "Placemaker.Park.LakeSize", min: 10, max: 40, step: 5, unit: "%", default: 20, visibleWhen: "lake=true" },
            { id: "fence", kind: "toggle", tier: "more", group: "edge", label: "Placemaker.Park.Fence", default: false },
            { id: "plazaShape", kind: "choice", tier: "more", group: "plaza", label: "Placemaker.Park.PlazaShape",
              options: [opt("circle", "Placemaker.Shape.Circle"), opt("square", "Placemaker.Shape.Square"), opt("rectangle", "Placemaker.Shape.Rect")], default: "circle" },
            { id: "plazaAt", kind: "choice", tier: "more", group: "plaza", label: "Placemaker.Park.PlazaAt",
              options: [opt("centre", "Placemaker.At.Centre"), opt("corner", "Placemaker.At.Corner"), opt("edge", "Placemaker.At.Edge"), opt("entrance", "Placemaker.At.Entrance")], default: "centre" },
            { id: "plazaSize", kind: "slider", tier: "more", group: "plaza", label: "Placemaker.Park.PlazaSize", min: 5, max: 40, step: 1, unit: "%", default: 15 },
            { id: "fenceAsset", kind: "asset", tier: "more", group: "edge", label: "Placemaker.Park.FenceAsset", assetCategory: "fence", default: "", visibleWhen: "fence=true" },
            { id: "ground", kind: "asset", tier: "more", group: "ground", label: "Placemaker.Park.Ground", tooltip: "Placemaker.Park.GroundTip", assetCategory: "surface.grass", default: "" },
            { id: "decals", kind: "assetMulti", tier: "more", group: "ground", label: "Placemaker.Decals.Pick", tooltip: "Placemaker.Decals.PickTip", assetCategory: "decal", default: [] },
            { id: "decalDensity", kind: "slider", tier: "more", group: "ground", label: "Placemaker.Decals.Density", min: 0, max: 100, step: 5, unit: "%", default: 30 },
            { id: "decalClustering", kind: "slider", tier: "more", group: "ground", label: "Placemaker.Decals.Clustering", tooltip: "Placemaker.Decals.ClusteringTip", min: 0, max: 100, step: 5, unit: "%", default: 50 },
        ],
        presets: [
            { id: "formal", name: "Placemaker.Park.Preset.Formal", values: { style: "formal", pathWidth: 5, trees: 40, furniture: true } },
            { id: "wild", name: "Placemaker.Park.Preset.Wild", values: { style: "meadow", pathWidth: 2.5, trees: 85, furniture: false } },
            { id: "pond", name: "Placemaker.Park.Preset.Pond", values: { style: "natural", trees: 55, lake: true, lakeSize: 25 } },
        ],
        minArea: 400,
    },
    {
        id: "plaza", name: "Placemaker.Plaza.Name", icon: "plaza", kind: "fill", accepts: [], emits: ["paved", "core", "edge"],
        controls: [
            { id: "pattern", kind: "choice", tier: "essential", group: "", label: "Placemaker.Plaza.Pattern",
              options: [opt("grid", "Placemaker.Plaza.Pattern.Grid", "block"), opt("radial", "Placemaker.Plaza.Pattern.Radial", "fountain"), opt("open", "Placemaker.Plaza.Pattern.Open", "area")], default: "radial" },
            { id: "centerpiece", kind: "asset", tier: "essential", group: "", label: "Placemaker.Plaza.Centerpiece", assetCategory: "centerpiece", default: "" },
            { id: "furniture", kind: "slider", tier: "essential", group: "", label: "Placemaker.Plaza.Furniture", min: 0, max: 100, step: 10, unit: "%", default: 50 },
            { id: "surface", kind: "asset", tier: "more", group: "", label: "Placemaker.Plaza.Surface", assetCategory: "surface.paving", default: "" },
            { id: "arrangement", kind: "arrangement", tier: "more", group: "", label: "Placemaker.Plaza.Arrangement", assetCategory: "bench", default: ["bench-wood", "lamp-classic", "bench-wood", "planter-square"] },
            { id: "edgeTrees", kind: "toggle", tier: "more", group: "", label: "Placemaker.Plaza.EdgeTrees", default: true },
            { id: "decals", kind: "assetMulti", tier: "more", group: "decals", label: "Placemaker.Decals.Pick", tooltip: "Placemaker.Decals.PickTip", assetCategory: "decal", default: [] },
            { id: "decalDensity", kind: "slider", tier: "more", group: "decals", label: "Placemaker.Decals.Density", min: 0, max: 100, step: 5, unit: "%", default: 30 },
            { id: "decalClustering", kind: "slider", tier: "more", group: "decals", label: "Placemaker.Decals.Clustering", tooltip: "Placemaker.Decals.ClusteringTip", min: 0, max: 100, step: 5, unit: "%", default: 50 },
        ],
        presets: [
            { id: "market", name: "Placemaker.Plaza.Preset.Market", values: { pattern: "open", furniture: 30 } },
            { id: "civic", name: "Placemaker.Plaza.Preset.Civic", values: { pattern: "radial", furniture: 60, edgeTrees: true } },
        ],
        minArea: 200,
    },
    {
        id: "parking", name: "Placemaker.Parking.Name", icon: "parking", kind: "fill", accepts: [], emits: ["lot", "edge"],
        controls: [
            { id: "angle", kind: "choice", tier: "essential", group: "", label: "Placemaker.Parking.StallAngle",
              options: [opt("90", "Placemaker.Parking.Angle90"), opt("60", "Placemaker.Parking.Angle60"), opt("45", "Placemaker.Parking.Angle45")], default: "90" },
            { id: "entrances", kind: "markers", tier: "essential", group: "", label: "Placemaker.Parking.Entrances", markerKinds: ["entrance", "entry", "exit", "alley", "footpath"] },
            { id: "surface", kind: "asset", tier: "essential", group: "", label: "Placemaker.Parking.Surface", assetCategory: "surface.asphalt", default: "" },
            { id: "greenery", kind: "toggle", tier: "essential", group: "", label: "Placemaker.Parking.Greenery", default: true },
            { id: "aisle", kind: "slider", tier: "more", group: "layout", label: "Placemaker.Parking.AisleWidth", min: 5, max: 8, step: 0.5, unit: "m", default: 6 },
            { id: "medians", kind: "toggle", tier: "more", group: "layout", label: "Placemaker.Parking.Medians", default: false },
            { id: "lanterns", kind: "toggle", tier: "more", group: "extras", label: "Placemaker.Parking.Lanterns", default: true },
            { id: "perimeterRoad", kind: "toggle", tier: "more", group: "layout", label: "Placemaker.Parking.PerimeterRoad", default: false },
            { id: "zoningEdges", kind: "toggle", tier: "more", group: "zoning", label: "Placemaker.Parking.ZoningEdges", tooltip: "Placemaker.Parking.ZoningEdgesTip", default: false },
            { id: "zone", kind: "choice", tier: "more", group: "zoning", label: "Placemaker.EdgeZoning.Zone", visibleWhen: "zoningEdges=true",
              options: [opt("resLow", "Placemaker.Zone.ResidentialLow"), opt("commercial", "Placemaker.Zone.Commercial"), opt("mixed", "Placemaker.Zone.Mixed")], default: "commercial" },
            { id: "chargers", kind: "slider", tier: "more", group: "extras", label: "Placemaker.Parking.Chargers", min: 0, max: 20, step: 1, unit: "count", default: 0 },
            { id: "fee", kind: "slider", tier: "more", group: "extras", label: "Placemaker.Parking.Fee", min: 0, max: 20, step: 1, unit: "money", default: 5 },
        ],
        presets: [],
        minArea: 600,
        markerKinds: [
            { kind: "entrance", label: "Placemaker.Marker.TwoWay" },
            { kind: "entry", label: "Placemaker.Marker.Entry" },
            { kind: "exit", label: "Placemaker.Marker.Exit" },
            { kind: "alley", label: "Placemaker.Marker.Alley" },
            { kind: "footpath", label: "Placemaker.Marker.Footpath", crosswalk: true },
        ],
        markersRequired: 1,
    },
    {
        id: "roads", name: "Placemaker.Roads.Name", icon: "roads", kind: "fill", accepts: [], emits: ["block", "edge"],
        controls: [
            { id: "pattern", kind: "choice", tier: "essential", group: "", label: "Placemaker.Roads.Pattern",
              options: [opt("grid", "Placemaker.Roads.Pattern.Grid", "roads"), opt("loop", "Placemaker.Roads.Pattern.Loop", "perimeter"), opt("organic", "Placemaker.Roads.Pattern.Organic", "path")], default: "grid" },
            { id: "blockSize", kind: "slider", tier: "essential", group: "", label: "Placemaker.Roads.BlockSize", min: 50, max: 200, step: 5, unit: "m", default: 90 },
            { id: "street", kind: "asset", tier: "essential", group: "", label: "Placemaker.Roads.Street", assetCategory: "net.road", default: "" },
            { id: "angle", kind: "slider", tier: "more", group: "", label: "Placemaker.Roads.Angle", min: -45, max: 45, step: 1, unit: "deg", default: 0 },
            { id: "perimeter", kind: "toggle", tier: "more", group: "", label: "Placemaker.Roads.Perimeter", default: true },
            { id: "culDeSacs", kind: "toggle", tier: "more", group: "", label: "Placemaker.Roads.CulDeSacs", default: false },
        ],
        presets: [
            { id: "dense", name: "Placemaker.Roads.Preset.Dense", values: { blockSize: 70 } },
            { id: "suburb", name: "Placemaker.Roads.Preset.Suburb", values: { blockSize: 140, culDeSacs: true } },
        ],
        minArea: 4000,
    },
    {
        id: "split", hidden: true, name: "Placemaker.Split.Name", icon: "split", kind: "split", accepts: [], emits: ["part"],
        controls: [
            { id: "ratio", kind: "slider", tier: "essential", group: "", label: "Placemaker.Split.Ratio", min: 20, max: 80, step: 5, unit: "%", default: 50 },
            { id: "direction", kind: "choice", tier: "essential", group: "", label: "Placemaker.Split.Direction",
              options: [opt("vertical", "Placemaker.Split.Vertical"), opt("horizontal", "Placemaker.Split.Horizontal")], default: "vertical" },
            { id: "divider", kind: "toggle", tier: "more", group: "", label: "Placemaker.Split.Divider", default: true },
        ],
        presets: [],
    },
    {
        id: "campground", name: "Placemaker.Campground.Name", icon: "campground", kind: "fill", accepts: [], emits: ["green"],
        controls: [
            { id: "pitches", kind: "slider", tier: "essential", group: "", label: "Placemaker.Campground.Pitches", min: 4, max: 40, step: 1, unit: "count", default: 14 },
            { id: "campfires", kind: "toggle", tier: "essential", group: "", label: "Placemaker.Campground.Campfires", default: true },
        ],
        presets: [],
        minArea: 1500,
    },
    {
        id: "playground", name: "Placemaker.Playground.Name", icon: "playground", kind: "addOn", accepts: ["green", "core", "root", "block", "part", "paved"], emits: [],
        controls: [
            { id: "size", kind: "choice", tier: "essential", group: "", label: "Placemaker.Playground.Size",
              options: [opt("s", "Placemaker.Size.Small"), opt("m", "Placemaker.Size.Medium"), opt("l", "Placemaker.Size.Large")], default: "m" },
            { id: "equipment", kind: "assetMulti", tier: "more", group: "", label: "Placemaker.Playground.Equipment", assetCategory: "playground", default: [] },
        ],
        presets: [],
    },
    {
        id: "fountain", name: "Placemaker.Fountain.Name", icon: "fountain", kind: "addOn", accepts: ["paved", "core", "green"], emits: [],
        controls: [{ id: "asset", kind: "asset", tier: "essential", group: "", label: "Placemaker.Fountain.Asset", assetCategory: "centerpiece", default: "" }],
        presets: [],
    },
    {
        id: "restrooms", name: "Placemaker.Restrooms.Name", icon: "restrooms", kind: "addOn", accepts: ["green", "paved", "core"], emits: [],
        controls: [{ id: "count", kind: "slider", tier: "essential", group: "", label: "Placemaker.Restrooms.Count", min: 1, max: 4, step: 1, unit: "count", default: 1 }],
        presets: [],
    },
    {
        id: "bigTrees", name: "Placemaker.BigTrees.Name", icon: "bigTrees", kind: "addOn", accepts: ["green", "edge", "root", "block", "core"], emits: [],
        controls: [
            { id: "count", kind: "slider", tier: "essential", group: "", label: "Placemaker.BigTrees.Count", min: 1, max: 30, step: 1, unit: "count", default: 8 },
            { id: "species", kind: "assetMulti", tier: "essential", group: "", label: "Placemaker.BigTrees.Species", assetCategory: "tree", default: [] },
        ],
        presets: [],
    },
    {
        id: "edgeZoning", name: "Placemaker.EdgeZoning.Name", icon: "edgeZoning", kind: "addOn", accepts: ["edge", "block", "root", "lot", "paved"], emits: [],
        controls: [
            { id: "zone", kind: "choice", tier: "essential", group: "", label: "Placemaker.EdgeZoning.Zone",
              options: [opt("resLow", "Placemaker.Zone.ResidentialLow"), opt("resHigh", "Placemaker.Zone.ResidentialHigh"), opt("commercial", "Placemaker.Zone.Commercial"), opt("mixed", "Placemaker.Zone.Mixed"), opt("office", "Placemaker.Zone.Office"), opt("industry", "Placemaker.Zone.Industry")], default: "mixed" },
            { id: "depth", kind: "slider", tier: "essential", group: "", label: "Placemaker.EdgeZoning.Depth", min: 1, max: 6, step: 1, unit: "count", default: 4 },
        ],
        presets: [],
    },
    {
        id: "pond", hidden: true, name: "Placemaker.Pond.Name", icon: "water", kind: "addOn", accepts: ["green"], emits: [],
        controls: [
            { id: "size", kind: "slider", tier: "essential", group: "", label: "Placemaker.Pond.Size", tooltip: "Placemaker.Pond.SizeTip", min: 5, max: 40, step: 5, unit: "%", default: 15 },
            { id: "shore", kind: "choice", tier: "more", group: "", label: "Placemaker.Pond.Shore",
              options: [opt("grass", "Placemaker.Pond.Shore.Grass"), opt("stone", "Placemaker.Pond.Shore.Stone")], default: "grass" },
        ],
        presets: [],
    },
    {
        id: "kiosk", name: "Placemaker.Kiosk.Name", icon: "building", kind: "addOn", accepts: ["paved", "core"], emits: [],
        controls: [{ id: "count", kind: "slider", tier: "essential", group: "", label: "Placemaker.Kiosk.Count", min: 1, max: 4, step: 1, unit: "count", default: 1 }],
        presets: [],
    },
    {
        id: "picnic", name: "Placemaker.Picnic.Name", icon: "picnic", kind: "addOn", accepts: ["green"], emits: [],
        controls: [{ id: "tables", kind: "slider", tier: "essential", group: "", label: "Placemaker.Picnic.Tables", min: 2, max: 16, step: 1, unit: "count", default: 6 }],
        presets: [],
    },
];

export interface MockRecipe {
    info: RecipeInfo;
    root: RecipeTemplate;
}

export interface RecipeTemplate {
    layer: string;
    target?: string;
    values?: Record<string, SettingValue>;
    children?: RecipeTemplate[];
    overrides?: Record<string, RecipeTemplate>;
}

/** Built-in features of fill layers (mirrors Core.FeatureInfo): a toggle that adds a child step. */
export interface FeatureDef {
    layer: string;
    target?: string;
    defaultOn?: boolean;
    labelKey?: string;
    tooltip?: string;
    /** Ids of the PARENT's controls that belong to the feature (shown under its toggle). */
    options?: string[];
    preset?: Record<string, SettingValue>;
}

/** Playtest 2 decision: refine each type with features instead of free mixing. */
export const FAKE_FEATURES: Record<string, FeatureDef[]> = {
    park: [
        { layer: "plaza", target: "core", labelKey: "Placemaker.Feature.CentralPlaza", tooltip: "Placemaker.Feature.CentralPlazaTip", options: ["plazaShape", "plazaAt", "plazaSize"], preset: { pattern: "radial", furniture: 40 } },
        { layer: "playground" },
        { layer: "pond", labelKey: "Placemaker.Feature.Pond", tooltip: "Placemaker.Pond.SizeTip" },
        { layer: "picnic" },
        { layer: "restrooms" },
        { layer: "fountain" },
        { layer: "bigTrees" },
    ],
    plaza: [
        { layer: "bigTrees", labelKey: "Placemaker.Feature.Trees", defaultOn: true },
        { layer: "playground" },
        { layer: "fountain" },
        { layer: "kiosk" },
    ],
    roads: [{ layer: "edgeZoning", target: "edge", labelKey: "Placemaker.Feature.StreetZoning" }],
};

export const FAKE_RECIPES: MockRecipe[] = [
    { info: { id: "parking", name: "Placemaker.Parking.Name", icon: "parking", custom: false, description: "Placemaker.Recipe.ParkingTip", group: "parking" }, root: { layer: "parking" } },
    { info: { id: "plaza", name: "Placemaker.Plaza.Name", icon: "plaza", custom: false, description: "Placemaker.Recipe.PlazaTip", group: "squares" }, root: { layer: "plaza" } },
    { info: { id: "park", name: "Placemaker.Park.Name", icon: "park", custom: false, description: "Placemaker.Recipe.ParkTip", group: "parks" }, root: { layer: "park" } },
    { info: { id: "roads", name: "Placemaker.Roads.Name", icon: "roads", custom: false, description: "Placemaker.Recipe.RoadsTip", group: "roads" }, root: { layer: "roads" } },
    {
        info: { id: "roads-with-parks", name: "Placemaker.Recipe.RoadsParks", icon: "roadsParks", custom: false, description: "Placemaker.Recipe.RoadsParksTip" },
        root: { layer: "roads", children: [{ layer: "park", target: "block" }] },
    },
    {
        info: { id: "park-plaza", name: "Placemaker.Recipe.ParkPlaza", icon: "parkPlaza", custom: false, description: "Placemaker.Recipe.ParkPlazaTip" },
        root: { layer: "split", children: [{ layer: "park", target: "part:0" }, { layer: "plaza", target: "part:1" }] },
    },
    {
        info: { id: "neighbourhood-park", name: "Placemaker.Recipe.NeighbourhoodPark", icon: "playground", custom: false, description: "Placemaker.Recipe.NeighbourhoodParkTip" },
        root: { layer: "park", children: [{ layer: "playground" }, { layer: "restrooms" }] },
    },
    { info: { id: "campground", name: "Placemaker.Campground.Name", icon: "campground", custom: false, description: "Placemaker.Recipe.CampgroundTip", group: "nature" }, root: { layer: "campground" } },
];

/** English for the fake layers' keys (the real layers ship their own Lang files). */
export const FAKE_STRINGS: Record<string, string> = {
    "Placemaker.Park.Name": "Park", "Placemaker.Park.Style": "Style", "Placemaker.Park.StyleTip": "Overall look: formal lines, natural curves or an open meadow.",
    "Placemaker.Park.Style.Formal": "Formal", "Placemaker.Park.Style.Natural": "Natural", "Placemaker.Park.Style.Meadow": "Meadow",
    "Placemaker.Park.PathWidth": "Path width", "Placemaker.Park.TreeDensity": "Trees", "Placemaker.Park.TreeDensityTip": "How much of the lawn is planted with trees.",
    "Placemaker.Park.Furniture": "Benches & lamps", "Placemaker.Park.PathAsset": "Path type", "Placemaker.Park.TreeSpecies": "Tree species",
    "Placemaker.Park.Entrances": "Entrances", "Placemaker.Park.Lake": "Pond", "Placemaker.Park.LakeTip": "Digs a pond. Terrain changes are permanent.",
    "Placemaker.Park.LakeSize": "Pond size", "Placemaker.Park.Fence": "Fence", "Placemaker.Park.FenceAsset": "Fence type",
    "Placemaker.Park.Preset.Formal": "Formal garden", "Placemaker.Park.Preset.Wild": "Wild meadow", "Placemaker.Park.Preset.Pond": "Pond park",
    "Placemaker.Plaza.Name": "Plaza", "Placemaker.Plaza.Pattern": "Layout", "Placemaker.Plaza.Pattern.Grid": "Grid", "Placemaker.Plaza.Pattern.Radial": "Radial",
    "Placemaker.Plaza.Pattern.Open": "Open", "Placemaker.Plaza.Centerpiece": "Centerpiece", "Placemaker.Plaza.Furniture": "Furniture",
    "Placemaker.Plaza.Surface": "Paving", "Placemaker.Plaza.Arrangement": "Edge sequence", "Placemaker.Plaza.EdgeTrees": "Trees along the edge",
    "Placemaker.Plaza.Preset.Market": "Market square", "Placemaker.Plaza.Preset.Civic": "Civic square",
    "Placemaker.Parking.Name": "Parking", "Placemaker.Parking.StallAngle": "Stall angle", "Placemaker.Parking.Angle90": "90°", "Placemaker.Parking.Angle60": "60°",
    "Placemaker.Parking.Angle45": "45°", "Placemaker.Parking.Entrances": "Entrances", "Placemaker.Parking.Surface": "Surface", "Placemaker.Parking.Greenery": "Greenery",
    "Placemaker.Parking.AisleWidth": "Aisle width", "Placemaker.Parking.Medians": "Planted medians", "Placemaker.Parking.Lanterns": "Lanterns",
    "Placemaker.Parking.Chargers": "EV chargers", "Placemaker.Parking.Fee": "Parking fee", "Placemaker.Parking.NeedsEntrance": "Place at least one entrance on the outline, where cars come in.",
    "Placemaker.Parking.TooSmall": "Too small for a parking lot (at least 600 m²).",
    "Placemaker.Roads.Name": "Roads", "Placemaker.Roads.Pattern": "Pattern", "Placemaker.Roads.Pattern.Grid": "Grid", "Placemaker.Roads.Pattern.Loop": "Loops",
    "Placemaker.Roads.Pattern.Organic": "Organic", "Placemaker.Roads.BlockSize": "Block size", "Placemaker.Roads.Street": "Street type", "Placemaker.Roads.Angle": "Rotation",
    "Placemaker.Roads.Perimeter": "Road along the edge", "Placemaker.Roads.CulDeSacs": "Cul-de-sacs", "Placemaker.Roads.Preset.Dense": "Dense grid", "Placemaker.Roads.Preset.Suburb": "Suburb",
    "Placemaker.Split.Name": "Split", "Placemaker.Split.Ratio": "First part", "Placemaker.Split.Direction": "Direction", "Placemaker.Split.Vertical": "Side by side",
    "Placemaker.Split.Horizontal": "Stacked", "Placemaker.Split.Divider": "Path between parts",
    "Placemaker.Campground.Name": "Campground", "Placemaker.Campground.Pitches": "Pitches", "Placemaker.Campground.Campfires": "Campfires",
    "Placemaker.Playground.Name": "Playground", "Placemaker.Playground.Size": "Size", "Placemaker.Playground.Equipment": "Equipment",
    "Placemaker.Size.Small": "Small", "Placemaker.Size.Medium": "Medium", "Placemaker.Size.Large": "Large",
    "Placemaker.Fountain.Name": "Fountain", "Placemaker.Fountain.Asset": "Fountain",
    "Placemaker.Restrooms.Name": "Restrooms", "Placemaker.Restrooms.Count": "Buildings",
    "Placemaker.BigTrees.Name": "Big trees", "Placemaker.BigTrees.Count": "Trees", "Placemaker.BigTrees.Species": "Species",
    "Placemaker.EdgeZoning.Name": "Edge zoning", "Placemaker.EdgeZoning.Zone": "Zone", "Placemaker.EdgeZoning.Depth": "Depth (cells)",
    "Placemaker.Zone.ResidentialLow": "Low res.", "Placemaker.Zone.ResidentialHigh": "High res.", "Placemaker.Zone.Commercial": "Commercial",
    "Placemaker.Zone.Mixed": "Mixed", "Placemaker.Zone.Office": "Office", "Placemaker.Zone.Industry": "Industry",
    "Placemaker.Picnic.Name": "Picnic area", "Placemaker.Picnic.Tables": "Tables",
    "Placemaker.Pond.Name": "Pond", "Placemaker.Pond.Size": "Size", "Placemaker.Pond.SizeTip": "Share of the park. Digging is permanent.",
    "Placemaker.Pond.Shore": "Shore", "Placemaker.Pond.Shore.Grass": "Grass", "Placemaker.Pond.Shore.Stone": "Stone",
    "Placemaker.Kiosk.Name": "Kiosk", "Placemaker.Kiosk.Count": "Kiosks",
    "Placemaker.Feature.CentralPlaza": "Central plaza", "Placemaker.Feature.Pond": "Pond", "Placemaker.Feature.Trees": "Trees",
    "Placemaker.Feature.StreetZoning": "Zoning along streets",
    "Placemaker.Feature.CentralPlazaTip": "A paved plaza inside the park; pick its shape, place and size.",
    "Placemaker.Park.PlazaShape": "Shape", "Placemaker.Park.PlazaAt": "Position", "Placemaker.Park.PlazaSize": "Size",
    "Placemaker.Shape.Circle": "Circle", "Placemaker.Shape.Square": "Square", "Placemaker.Shape.Rect": "Rect",
    "Placemaker.At.Centre": "Centre", "Placemaker.At.Corner": "Corner", "Placemaker.At.Edge": "Edge", "Placemaker.At.Entrance": "Entrance",
    "Placemaker.Recipe.RoadsParks": "Roads + parks", "Placemaker.Recipe.ParkPlaza": "Park | plaza", "Placemaker.Recipe.NeighbourhoodPark": "Family park",
    "Placemaker.Marker.TwoWay": "Two-way", "Placemaker.Marker.Entry": "Entry only", "Placemaker.Marker.Exit": "Exit only",
    "Placemaker.Marker.Alley": "Alley", "Placemaker.Marker.Footpath": "Footpath", "Placemaker.Reason.NeedEntrance": "Place at least one entrance", "Placemaker.Reason.AddEntrance": "Add an entrance to build",
    "Placemaker.Parking.PerimeterRoad": "Perimeter road", "Placemaker.Parking.ZoningEdges": "Zone the edges", "Placemaker.Parking.ZoningEdgesTip": "Zones a strip along the lot so buildings can grow next to it.",
    "Placemaker.Park.Ground": "Lawn", "Placemaker.Park.GroundTip": "Ground texture under the lawn.",
    "Placemaker.Decals.Pick": "Decals", "Placemaker.Decals.PickTip": "Leaves, cracks, puddles, markings, scattered over the ground.",
    "Placemaker.Decals.Density": "Density", "Placemaker.Decals.Clustering": "Clustering", "Placemaker.Decals.ClusteringTip": "Low: spread evenly. High: in patches.",
    "Placemaker.UI.Group.ground": "Ground", "Placemaker.UI.Group.decals": "Decals",
    "Placemaker.UI.Group.zoning": "Zoning", "Placemaker.UI.Group.layout": "Layout", "Placemaker.UI.Group.extras": "Extras", "Placemaker.UI.Group.paths": "Paths",
    "Placemaker.UI.Group.planting": "Planting", "Placemaker.UI.Group.water": "Water", "Placemaker.UI.Group.edge": "Edge",
    "Placemaker.Recipe.ParkingTip": "A surface parking lot with stalls, aisles and entrances.",
    "Placemaker.Recipe.PlazaTip": "A paved square with a centrepiece, seating and lights.",
    "Placemaker.Recipe.ParkTip": "Paths, lawns, trees and benches.",
    "Placemaker.Recipe.RoadsTip": "A street pattern fitted to the area.",
    "Placemaker.Recipe.RoadsParksTip": "Streets, and every block between them becomes a park.",
    "Placemaker.Recipe.ParkPlazaTip": "Half park, half plaza.",
    "Placemaker.Recipe.NeighbourhoodParkTip": "A park with a playground and restrooms.",
    "Placemaker.Recipe.CampgroundTip": "Tent pitches around a loop path.",
    "Placemaker.Stat.Trees": "trees", "Placemaker.Stat.Benches": "benches", "Placemaker.Stat.Lamps": "lamps", "Placemaker.Stat.Stalls": "stalls",
    "Placemaker.Stat.PathLength": "of paths", "Placemaker.Stat.RoadLength": "of road", "Placemaker.Stat.Blocks": "blocks", "Placemaker.Stat.Playgrounds": "playgrounds",
    "Placemaker.Stat.Fountains": "fountains", "Placemaker.Stat.Restrooms": "restrooms", "Placemaker.Stat.ZoneCells": "zone cells", "Placemaker.Stat.Tents": "tents",
    "Placemaker.Stat.Chargers": "EV chargers", "Placemaker.Stat.PavedArea": "paved",
    "Placemaker.Issue.PlazaLarge": "Large plazas feel empty. Consider Park | plaza, or more furniture.",
    "Placemaker.Issue.TreesSkipped": "{0} trees were left out where they would block a path.",
    "Placemaker.Issue.BlockTooSmall": "Block {0} is too small to fill and stays empty.",
    "Placemaker.Issue.NoRoom": "{0}: no free spot left, skipped.",
    "Placemaker.Marker.entrance.Plural": "entrances",
};

// ───────────── fake assets (fakeAssets.ts) ─────────────

export { fakeAssets, surfaceColor } from "./fakeAssets";

// ───────────── more types (the Make row at scale) ─────────────

/** Types other workstreams are adding: backend.addMoreTypes() puts them in the catalog (Make-row tests). */
export const MORE_TYPES: [string, string, string, string][] = [
    ["botanicalGarden", "Botanical garden", "flower", "gardens"],
    ["sportsPark", "Sports park", "people", "sports"],
    ["dogPark", "Dog park", "park", "parks"],
    ["picnicGrounds", "Picnic grounds", "picnic", "parks"],
    ["natureReserve", "Nature reserve", "tree", "nature"],
    ["formalGarden", "Formal garden", "flower", "gardens"],
    ["communityGarden", "Community garden", "leaf", "gardens"],
    ["marketSquare", "Market square", "plaza", "squares"],
    ["parkingGarage", "Parking garage", "parking", "parking"],
    ["quarry", "Quarry", "area", "industrial"],
    ["restArea", "Rest area", "parking", "transport"],
];
