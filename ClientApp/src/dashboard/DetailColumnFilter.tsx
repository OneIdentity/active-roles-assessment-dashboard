import { useEffect, useMemo, useRef, useState } from 'react';
import { t } from './strings';

interface Props {
  column: string;
  values: string[];
  selected: Set<string> | undefined;
  onChange: (next: Set<string> | undefined) => void;
}

const display = (v: string) => (v === '' ? t.filterBlank : v);

/** Excel-like per-column value filter for KPI drill-down tables (React port of the legacy dashboard.js filter). */
export function DetailColumnFilter({ column, values, selected, onChange }: Props) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const btnRef = useRef<HTMLButtonElement>(null);
  const active = selected !== undefined;
  const toggleOpen = () => {
    if (!open && btnRef.current) {
      const r = btnRef.current.getBoundingClientRect();
      setPos({ top: r.bottom + 4, left: Math.max(8, Math.min(r.left, window.innerWidth - 268)) });
    }
    setOpen((o) => !o);
  };

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(false); };
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { e.stopPropagation(); setOpen(false); } };
    const onScroll = (e: Event) => { if (!rootRef.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey, true);
    window.addEventListener('scroll', onScroll, true);
    window.addEventListener('resize', onScroll);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey, true);
      window.removeEventListener('scroll', onScroll, true);
      window.removeEventListener('resize', onScroll);
    };
  }, [open]);

  const visible = useMemo(() => {
    const q = search.trim().toLowerCase();
    return q ? values.filter((v) => display(v).toLowerCase().includes(q)) : values;
  }, [values, search]);

  const isChecked = (v: string) => !selected || selected.has(v);
  const commit = (next: Set<string>) => onChange(next.size === values.length ? undefined : next);
  const toggle = (v: string) => {
    const next = new Set(selected ?? values);
    if (next.has(v)) next.delete(v); else next.add(v);
    commit(next);
  };
  const selectAll = () => { const next = new Set(selected ?? values); visible.forEach((v) => next.add(v)); commit(next); };
  const clearAll = () => { const next = new Set(selected ?? values); visible.forEach((v) => next.delete(v)); commit(next); };

  return (
    <div className="kpi-col-filter" ref={rootRef}>
      <button
        ref={btnRef}
        type="button"
        className={`kpi-col-filter-btn${active ? ' is-active' : ''}`}
        aria-label={t.filterColumn(column)}
        title={t.filterColumn(column)}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={toggleOpen}
      >
        <svg viewBox="0 0 24 24" width="14" height="14" aria-hidden="true"><path d="M3 5h18l-7 8v6l-4-2v-4z" fill={active ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round" /></svg>
      </button>
      {open && pos && (
        <div className="kpi-col-filter-menu" style={{ top: pos.top, left: pos.left }} role="dialog" aria-label={t.filterColumn(column)}>
          <input type="search" className="kpi-col-filter-search" placeholder={t.filterSearch} aria-label={t.filterSearch} value={search} onChange={(e) => setSearch(e.target.value)} autoFocus />
          <div className="kpi-col-filter-actions">
            <button type="button" onClick={selectAll}>{t.selectAll}</button>
            <button type="button" onClick={clearAll}>{t.clear}</button>
          </div>
          <ul className="kpi-col-filter-list">
            {visible.map((v) => (
              <li key={v}>
                <label><input type="checkbox" checked={isChecked(v)} onChange={() => toggle(v)} /> <span>{display(v)}</span></label>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
