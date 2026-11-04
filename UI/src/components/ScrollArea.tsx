// A scrolling box with a scrollbar you can see (Gameface draws none): a thin bar at the right edge, only while there is
// more to see than fits, its thumb as long as the share that shows. Drag the thumb, or click the bar to page towards the
// click. The box itself keeps scrolling as before (wheel, scrollToTop), the bar follows it.
import React, { ReactNode, useCallback, useEffect, useRef, useState } from "react";
import classNames from "classnames";
import s from "./scrollArea.module.scss";

type Bar = { top: number; size: number };

export const ScrollArea = ({ className, outerClassName, children, testId, onMouseLeave }: { className: string; outerClassName?: string; children: ReactNode; testId?: string; onMouseLeave?: () => void }) => {
    const ref = useRef<HTMLDivElement>(null);
    const [bar, setBar] = useState<Bar | null>(null);
    const [dragging, setDragging] = useState(false);

    // where the thumb is, from the box (also a few times a second: content that grows or shrinks does not scroll)
    const measure = useCallback(() => {
        const el = ref.current;
        if (!el) return;
        const h = el.clientHeight, sh = el.scrollHeight;
        if (h <= 0 || sh <= h + 1) {
            setBar((b) => (b ? null : b));
            return;
        }
        const size = Math.max(24, (h * h) / sh);
        const top = (el.scrollTop / (sh - h)) * (h - size);
        setBar((b) => (b && Math.abs(b.top - top) < 0.5 && Math.abs(b.size - size) < 0.5 ? b : { top, size }));
    }, []);
    useEffect(() => {
        measure();
        const id = window.setInterval(measure, 300);
        return () => window.clearInterval(id);
    }, [measure]);

    const onThumbDown = (e: React.MouseEvent) => {
        const el = ref.current;
        if (e.button !== 0 || !el || !bar) return;
        e.preventDefault();
        e.stopPropagation();
        const startY = e.clientY, startTop = el.scrollTop;
        const ratio = (el.scrollHeight - el.clientHeight) / Math.max(1, el.clientHeight - bar.size);
        setDragging(true);
        const move = (ev: MouseEvent) => {
            el.scrollTop = startTop + (ev.clientY - startY) * ratio;
            measure();
        };
        const up = () => {
            window.removeEventListener("mousemove", move);
            window.removeEventListener("mouseup", up);
            setDragging(false);
        };
        window.addEventListener("mousemove", move);
        window.addEventListener("mouseup", up);
    };

    // a click on the bar beside the thumb: a page towards it
    const onTrackDown = (e: React.MouseEvent<HTMLDivElement>) => {
        const el = ref.current;
        if (e.button !== 0 || !el || !bar) return;
        e.preventDefault();
        e.stopPropagation();
        const y = e.clientY - e.currentTarget.getBoundingClientRect().top;
        el.scrollTop += (y < bar.top ? -1 : 1) * el.clientHeight * 0.9;
        measure();
    };

    return (
        <div className={classNames(s.wrap, outerClassName)}>
            <div ref={ref} className={className} data-testid={testId} onScroll={measure} onMouseLeave={onMouseLeave}>
                {children}
            </div>
            {bar ? (
                <div className={s.track} data-testid={testId ? testId + "-scrollbar" : undefined} onMouseDown={onTrackDown}>
                    <div className={classNames(s.thumb, { [s.thumbOn]: dragging })} style={{ top: bar.top + "px", height: bar.size + "px" }} onMouseDown={onThumbDown} />
                </div>
            ) : null}
        </div>
    );
};
