// s42 — colours and glyphs of the Design picker: every category, type, style and extra gets a coloured tile with
// a dark glyph (lib/icons bakes a "dark" tint for every bundled icon). Categories own the colour; their types and
// styles inherit it, so the three picker rows and the editing breadcrumb read as one family. Extras (features)
// carry their own role colour (water blue, play orange, planting green…).
import { bundledName } from "./icons";

/** Category tile colours (Make-row group id → hex). */
export const CATEGORY_COLOR: Record<string, string> = {
    roads: "#8fe3ff",
    parks: "#6cc56a",
    gardens: "#f08bc8",
    squares: "#e2b47e",
    sports: "#ff8f5a",
    water: "#4fb8e8",
    nature: "#3fc49a",
    ai: "#c58bff",
    farms: "#e8c547",
    events: "#b48cff",
    industrial: "#a9b4bd",
    transport: "#8a9dff",
    parking: "#5f9dff",
    commercial: "#ff7c9c",
    // (core) shelf category ids (Recipes/shelf/<id>.json) that are not a type group
    shops: "#ff7c9c",
    beach: "#4fb8e8",
    industry: "#a9b4bd",
    neighbourhood: "#9fd86b",
    zoning: "#9fd86b",
    other: "#c9d3da",
};

/** Category glyphs. */
export const CATEGORY_ICON: Record<string, string> = {
    roads: "roads",
    parks: "park",
    gardens: "flower",
    squares: "plaza",
    sports: "ball",
    water: "waves",
    nature: "mountain",
    ai: "star",
    farms: "barn",
    events: "ferris",
    industrial: "factory",
    transport: "bus",
    parking: "parking",
    commercial: "shop",
    shops: "shop",
    beach: "waves",
    industry: "factory",
    neighbourhood: "shrub",
    zoning: "zone",
    other: "layers",
};

export const categoryColor = (group: string) => CATEGORY_COLOR[group] ?? CATEGORY_COLOR.other;
export const categoryIcon = (group: string) => CATEGORY_ICON[group] ?? "layers";

/** Type glyphs by type (recipe / fill layer) id. Unknown types: the bundled glyph of their id, else the category's. */
const TYPE_ICON: Record<string, string> = {
    // the Zoning category's neighbourhoods
    zoningSuburb: "flower",
    zoningVillage: "tree",
    zoningTownhouses: "bench",
    zoningMainStreet: "shop",
    zoningTownCentre: "parkPlaza",
    zoningDowntown: "building",
    zoningBusinessPark: "sparkle",
    zoningIndustrial: "factory",
    zoningSeaside: "waves",
    // the Zoning category's looks (hidden)
    polishGarden: "flower",
    polishNatural: "rocks",
    polishTidy: "topiary",
    polishYard: "crate",
    polishMeadow: "leaf",
    polishRustic: "pickaxe",
    polishCoastal: "waves",
    polishManicured: "shrub",
    parking: "parking",
    parkingGarage: "car",
    stripMall: "shop",
    shoppingPlaza: "bag",
    outdoorMall: "footpath",
    shopsPark: "bench",
    mallPark: "park",
    highStreet: "lamp",
    retailPark: "cart",
    mallDistrict: "building",
    plaza: "plaza",
    marketSquare: "awning",
    park: "park",
    campground: "campground",
    botanicalGarden: "flower",
    sportsPark: "ball",
    dogPark: "paw",
    picnicGrounds: "picnic",
    natureReserve: "tree",
    formalGarden: "topiary",
    communityGarden: "sprout",
    cemeteryGrounds: "tombstone",
    greenwayPark: "bike",
    urbanFarm: "carrot",
    beach: "umbrella",
    animalPark: "bird",
    waterfront: "boat",
    constructionSite: "cone",
    quarry: "pickaxe",
    fairground: "ferris",
    amphitheatre: "amphi",
    restArea: "coffee",
    transitPlaza: "bus",
    farmstead: "barn",
    orchardPark: "apple",
    golf: "golf",
    lookoutHill: "binoculars",
    adventurePlayground: "playground",
    memorialPark: "obelisk",
    containerYard: "crate",
    carYard: "car",
    junkYard: "trash",
    industrialWorks: "factory",
    logisticsYard: "crate",
    hikingTrail: "signpost",
    scrapyard: "gear",
    abandonedLot: "rubble",
    urbanRuins: "column",
    landfill: "trash",
    roads: "roads",
    wetland: "reeds",
    zenGarden: "zen",
    polishGardens: "sparkle",
};

export function typeIcon(id: string, group: string): string {
    if (TYPE_ICON[id]) return TYPE_ICON[id];
    if (bundledName(id)) return id;
    const base = (id || "").split(/[-_]/)[0];
    if (base && TYPE_ICON[base]) return TYPE_ICON[base];
    return categoryIcon(group);
}

/**
 * Style (preset) glyph by keyword in its id or English name; falls back to the type's glyph. Order matters:
 * the first matching rule wins ("riverSteps" is water before steps).
 */
const STYLE_RULES: [RegExp, string][] = [
    [/christmas|winter|snow|sled/i, "snow"],
    [/night|moon/i, "moon"],
    [/river|lake|harbou?r|pier|cove|flood|water|lido|splash|shore|fish/i, "waves"],
    [/beach|sand|resort|volley/i, "umbrella"],
    [/forest|wood|wild|tree|arbor|leafy|jungle|shady|olive/i, "tree"],
    [/meadow|flower|wild ?flower|lavender|blossom|cherry/i, "flower"],
    [/garden|green|moss|bamboo|shrub|hedge|lawn/i, "leaf"],
    [/formal|french|italian|knot|victorian|grand|classic|civic|podium/i, "crown"],
    [/fountain|cascade/i, "fountain"],
    [/market|farmer|stall|bazaar|shop|outlet|trade|retail|megastore|mall|lifestyle/i, "awning"],
    [/cafe|tea|food|bbq|truck/i, "coffee"],
    [/amphi|cinema|stage|band|speaker|festival|fair|fun/i, "music"],
    [/sport|stadium|tennis|campus|pitch|kick|agility/i, "ball"],
    [/play|toddler|adventure|family|kids/i, "playground"],
    [/hill|ridge|summit|canyon|terrace|lookout|view|overlook|mound|rocky|rock|kite/i, "mountain"],
    [/sunken|split|level|step/i, "stairs"],
    [/camp|caravan|tent/i, "campground"],
    [/bike|cycle|greenway/i, "bike"],
    [/road|street|grid|block|loop|boulevard|shared|suburb|village|town|downtown|superblock/i, "roads"],
    [/park ?ride|bus|taxi|transit|forecourt|highway|ev\b/i, "bus"],
    [/parking|compact|standard|angled|garage/i, "parking"],
    [/dairy|arable|ranch|hobby|farm|paddock|horse|pett|deer|aviary|vineyard|greenhouse|orchard|apple|citrus/i, "barn"],
    [/memorial|war|reflection|sculpture|monument/i, "obelisk"],
    [/container|pallet|lumber|storage|loading|shipment|yard/i, "crate"],
    [/scrap|salvage|tire|tyre|cars|junk|recycl|compost|capped|active|flytip/i, "gear"],
    [/ruin|rubble|demolish|overgrown|squatter|wasteland|graffiti|parkour|diy/i, "rubble"],
    [/pit|quarry|gravel|dig|earthwork|foundation|roadwork|compound/i, "pickaxe"],
    [/modern|urban|dense|mixed|district|plaza|piazza|square|court/i, "building"],
    [/pocket|neighbou?rhood|community|commons|corner|rear|alley/i, "heart"],
    [/natural|landscape|heath|marsh|reed|bird|habitat|wetland|rain/i, "feather"],
    [/golf|putt|links|driving|disc/i, "golf"],
    [/zen|karesansui|stroll/i, "zen"],
];

export function styleIcon(id: string, name: string, fallback: string): string {
    const text = id + " " + name;
    for (const [re, icon] of STYLE_RULES) if (re.test(text)) return icon;
    return fallback;
}

/** Extras (feature layers): [glyph, tile colour]. */
const FEATURE_LOOK: Record<string, [string, string]> = {
    plaza: ["plaza", "#e2b47e"],
    pond: ["water", "#4fb8e8"],
    lake: ["water", "#4fb8e8"],
    fountain: ["fountain", "#4fb8e8"],
    shore: ["waves", "#4fb8e8"],
    splashPad: ["drops", "#4fb8e8"],
    playground: ["playground", "#ff8f5a"],
    kickabout: ["ball", "#ff8f5a"],
    restrooms: ["restrooms", "#8fd1ff"],
    kiosk: ["awning", "#ff7c9c"],
    shops: ["shop", "#ff7c9c"],
    beachBar: ["coffee", "#ff7c9c"],
    farmStall: ["awning", "#e8c547"],
    farmStand: ["awning", "#e8c547"],
    honeyHives: ["bee", "#e8c547"],
    apiary: ["bee", "#e8c547"],
    picnic: ["picnic", "#6cc56a"],
    bigtrees: ["bigTrees", "#6cc56a"],
    bigTrees: ["bigTrees", "#6cc56a"],
    plazatrees: ["tree", "#6cc56a"],
    flowerbeds: ["flower", "#f08bc8"],
    terrain: ["mountain", "#c99a6b"],
    rocks: ["rocks", "#a9b4bd"],
    ground: ["paint", "#c99a6b"],
    decals: ["decal", "#a9b4bd"],
    spoilHeaps: ["mountain", "#c99a6b"],
    dirtJumps: ["mountain", "#c99a6b"],
    debris: ["rubble", "#a9b4bd"],
    junkPiles: ["gear", "#a9b4bd"],
    skipRow: ["trash", "#a9b4bd"],
    workLights: ["lamp", "#ffd166"],
    lightTowers: ["lamp", "#ffd166"],
    volleyball: ["ball", "#ff8f5a"],
    basketball: ["ball", "#ff8f5a"],
    basketballCourt: ["ball", "#ff8f5a"],
    tableTennis: ["ball", "#ff8f5a"],
    tennisCourts: ["ball", "#ff8f5a"],
    pitch: ["ball", "#ff8f5a"],
    baseballDiamond: ["ball", "#ff8f5a"],
    cricketGround: ["ball", "#ff8f5a"],
    runningTrack: ["track", "#ff8f5a"],
    skateBowl: ["track", "#ff8f5a"],
    skatePark: ["track", "#ff8f5a"],
    fitness: ["dumbbell", "#ff8f5a"],
    chess: ["crown", "#b48cff"],
    bandstand: ["music", "#b48cff"],
    noticeboard: ["board", "#b48cff"],
    dogRun: ["paw", "#6cc56a"],
    bigStatue: ["obelisk", "#b48cff"],
    obelisk: ["obelisk", "#b48cff"],
    triumphalArch: ["arch", "#b48cff"],
    sculptureWalk: ["sparkle", "#b48cff"],
    folly: ["column", "#b48cff"],
    terraceGarden: ["stairs", "#6cc56a"],
    flagCircle: ["golf", "#b48cff"],
    festivalCamping: ["campground", "#3fc49a"],
    trailCamp: ["campground", "#3fc49a"],
    puttingGreen: ["golf", "#6cc56a"],
    viewpoint: ["binoculars", "#3fc49a"],
    yardBreak: ["coffee", "#a9b4bd"],
    wildlifeHabitat: ["bird", "#3fc49a"],
    teaHouse: ["coffee", "#f08bc8"],
    edgeZoning: ["edgeZoning", "#b48cff"],
};

export function featureLook(id: string, declared?: string): { icon: string; color: string } {
    const hit = FEATURE_LOOK[id] ?? FEATURE_LOOK[(id || "").toLowerCase()];
    if (hit) return { icon: hit[0], color: hit[1] };
    return { icon: bundledName(id) ? id : bundledName(declared ?? "") ? declared! : "addon", color: "#ffa94d" };
}

/** Section (control group) glyphs for the Shape rows. */
const SECTION_ICON: [RegExp, string][] = [
    [/^$|basics|layout|style|pattern|shape/i, "layers"],
    [/path|walk|promenade|trail|route/i, "path"],
    [/edge|fence|border|kerb|curb/i, "perimeter"],
    [/plant|tree|green|lawn|hedge/i, "tree"],
    [/asset|object|furnit|bench|amenit/i, "bench"],
    [/paint|dress|ground|surface|texture/i, "paint"],
    [/decal/i, "decal"],
    [/water|pond|lake|shore/i, "water"],
    [/terrain|height|slope|level/i, "mountain"],
    [/road|street|avenue|net|lane/i, "roads"],
    [/park|stall|aisle/i, "parking"],
    [/zon/i, "zone"],
    [/shop|retail|store|commerc/i, "shop"],
    [/light|lamp/i, "lamp"],
    [/entr|access|gate/i, "entrance"],
];

export function sectionIcon(group: string): string {
    for (const [re, icon] of SECTION_ICON) if (re.test(group)) return icon;
    return "layers";
}

/** Gold of the player's own styles. */
export const CUSTOM_COLOR = "#ffb547";
