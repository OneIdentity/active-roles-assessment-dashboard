import { useEffect, useRef, useState } from 'react';
import type { SegmentFilterView } from './api';
import { t } from './strings';

type Dimension = 'Domain' | 'Tenant';

interface DimensionProps {
  dimension: Dimension;
  label: string;
  available: string[];
  selected: string[];
  onApply: (dimension: Dimension, segments: string[]) => void;
}

function SegmentDimension({ dimension, label, available, selected, onApply }: DimensionProps) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<Set<string>>(() => new Set(selected));
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => { setDraft(new Set(selected)); }, [selected]);

  const close = (apply: boolean) => {
    setOpen(false);
    if (!apply) return;
    const next = available.filter((s) => draft.has(s));
    const unchanged = next.length === selected.length && next.every((s) => selected.includes(s));
    if (!unchanged) onApply(dimension, next);
  };

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) close(true); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { setDraft(new Set(selected)); setOpen(false); } };
    document.addEventListener('mousedown', onDoc);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', onDoc); document.removeEventListener('keydown', onKey); };
  });

  const count = selected.length;
  const summary = count === 0 ? t.segNone(label) : count >= available.length ? t.segAll(label) : t.segSome(count, available.length, label);

  const toggle = (s: string) => setDraft((d) => {
    const n = new Set(d);
    if (n.has(s)) n.delete(s); else n.add(s);
    return n;
  });

  return (
    <div className="segment-filter-dimension" ref={ref}>
      <button type="button" className="segment-filter-toggle" aria-haspopup="true" aria-expanded={open}
        onClick={() => (open ? close(true) : setOpen(true))}>
        <span className="segment-filter-label">{label}:</span>
        <span className="segment-filter-summary">{summary}</span>
        <span className="segment-filter-caret">&#9662;</span>
      </button>
      {open && (
        <div className="segment-filter-menu">
          <div className="segment-filter-actions">
            <button type="button" className="segment-filter-link" onClick={() => setDraft(new Set(available))}>{t.selectAll}</button>
            <button type="button" className="segment-filter-link" onClick={() => setDraft(new Set())}>{t.clear}</button>
            <button type="button" className="segment-filter-link" onClick={() => close(true)}>{t.apply}</button>
          </div>
          <ul className="segment-filter-list">
            {available.map((s) => (
              <li className="segment-filter-item" key={s}>
                <label>
                  <input type="checkbox" checked={draft.has(s)} onChange={() => toggle(s)} />
                  <span>{s}</span>
                </label>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

/** React port of `Pages/Shared/_SegmentFilter.cshtml`. */
export function SegmentFilter({ filter, onApply }: { filter: SegmentFilterView; onApply: DimensionProps['onApply'] }) {
  if (!filter.enabled) return null;
  return (
    <div className="segment-filter react-segment-bar">
      {filter.availableDomains.length > 0 && (
        <SegmentDimension dimension="Domain" label={t.domains} available={filter.availableDomains}
          selected={filter.selectedDomains} onApply={onApply} />
      )}
      {filter.availableTenants.length > 0 && (
        <SegmentDimension dimension="Tenant" label={t.tenants} available={filter.availableTenants}
          selected={filter.selectedTenants} onApply={onApply} />
      )}
    </div>
  );
}
