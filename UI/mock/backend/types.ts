import { Issue, LegendEntry, LocText, NetLegendEntry, ResolvedAsset, SettingValue, Stat } from "../../src/contract";
import { V2 } from "./geometry";

/** A recipe step as the mock backend stores it (mirrors Core.RecipeStep). */
export interface StepModel {
    layer: string;
    target: string;
    enabled: boolean;
    values: Record<string, SettingValue>;
    locked: string[];
    seed: number;
    markers: { kind: string; p: V2 }[];
    children: StepModel[];
    /** Set when this child is a built-in feature of its parent (FeatureDef.layer). */
    feature?: string;
    overrides: Record<string, StepModel>;
}

export interface PlanRegion { key: string; tags: string[]; ring: V2[]; areaM2: number }
/** roleColor (s16): the road role colour for roads of the roads layer (same as the in-game preview). */
/** lg (s22): the preview legend layer of the element (host: Core PreviewLegend.Of; fake: mock/backend/legend.ts). */
export interface PlanNet { role: string; pts: V2[]; width: number; roleColor?: string | null; lg?: string }
export interface PlanObject { role: string; p: V2; yaw: number; r: number; lg?: string }

/** One executed step × region; same JSON as the mock host's /api/compose. */
export interface PlanLayer {
    layer: string;
    stepPath: string;
    regions: PlanRegion[];
    surfaces: { role: string; ring: V2[]; lg?: string }[];
    areas: { role: string; ring: V2[] }[];
    nets: PlanNet[];
    objects: PlanObject[];
    zoning: { zone: string; quad: V2[] }[];
    terraform: { kind: string; permanent: boolean; ring: V2[] }[];
    issues: Issue[];
    stats?: Stat[];
}

export interface ComposeResult {
    layers: PlanLayer[];
    issues: Issue[];
    stats: Stat[];
    perLayer: { stepPath: string; layer: string; regionKey: string; stats: Stat[]; cost: number; upkeep: number }[];
    cost: number;
    upkeep: number;
    permanent: LocText[];
    ms: number;
    source: "core" | "fake";
    /** s16: per UI step path, what Automatic became and the roads by role. */
    steps?: Record<string, { resolvedAssets?: Record<string, ResolvedAsset>; netLegend?: NetLegendEntry[] }>;
    /** s17: park stats from the real Core estimator (host only). */
    parkStats?: import("../../src/contract").ParkStatsView | null;
    /** s45: cost by friendly category (host: Core CostBuckets with the stand-in prices). */
    costBuckets?: { id: string; cost: number; upkeep: number }[];
    /** s22: the preview legend (host: Core UiModels.Legend). */
    legend?: LegendEntry[];
    /** (sketch study) the top view from the host (Core SitePlan.ToUi); absent with the fake planner. */
    sitePlan?: unknown;
}
