// Panel width → content layout. The panel is resizable (Panel.tsx); content reads this context to lay
// itself out in more columns when there is room (playtest: "more vertically space conscious, grids, a
// little wider, multiple columns when it would look decent").
import { createContext, useContext } from "react";

/** Default panel widths in rem; the wide default applies on screens ≥ WIDE_SCREEN_PX. */
export const NARROW_REM = 450;
export const WIDE_DEFAULT_REM = 450;
export const WIDE_SCREEN_PX = 1600;
export const MIN_REM = 340;
/** Largest share of the screen width the panel may take. */
export const MAX_SHARE = 0.45;
/** From this width on, property rows / groups / features go into two columns. */
export const TWO_COL_REM = 520;
/** From this width on, feature toggles go into three columns. */
export const THREE_COL_REM = 760;
/** Horizontal padding of the panel body (keep in sync with .body in panel.module.scss). */
export const BODY_PAD_REM = 20;

export type PanelLayout = {
    /** Panel width in rem. */
    width: number;
    /** Two-column layout for property rows and folded groups. */
    wide: boolean;
    /** Columns of the feature grid (1–3). */
    featureCols: number;
};

export function layoutFor(width: number): PanelLayout {
    const wide = width >= TWO_COL_REM;
    return { width, wide, featureCols: width >= THREE_COL_REM ? 3 : wide ? 2 : 1 };
}

export const PanelLayoutContext = createContext<PanelLayout>(layoutFor(NARROW_REM));

export const usePanelLayout = () => useContext(PanelLayoutContext);

/**
 * Columns for n tiles of at least `minTile` rem in `inner` rem: as many as fit, then balanced so the
 * last row is not a lonely straggler (5 tiles in room for 4 → 3 + 2).
 */
export function tileColumns(n: number, inner: number, minTile: number, max = 8): number {
    if (n <= 0) return 1;
    const fit = Math.max(1, Math.min(max, Math.floor(inner / minTile)));
    if (n <= fit) return n;
    const rows = Math.ceil(n / fit);
    return Math.ceil(n / rows);
}

/**
 * Two-column packing of a list: each item is "half" or "full"; a half without a half neighbour to pair
 * with becomes full, so nothing sits alone in half a row. Returns true for the items laid out as halves.
 */
export function pairHalves(halves: boolean[]): boolean[] {
    const out = halves.map(() => false);
    for (let i = 0; i < halves.length; i++) {
        if (halves[i] && i + 1 < halves.length && halves[i + 1]) {
            out[i] = out[i + 1] = true;
            i++;
        }
    }
    return out;
}
