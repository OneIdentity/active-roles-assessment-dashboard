import { useEffect, useId, useMemo, useRef, useState } from 'react';
import type { SearchKpi } from './api';
import { formatNumber, t } from './strings';

const MAX_RESULTS = 12;

/** Lower-cases and strips diacritics so "uberprufen" matches "Überprüfen". */
export const normalize = (value: string) =>
  value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();

/** Name-contains match on the KPI label; results whose label starts with the query come first. */
export const searchKpis = (index: SearchKpi[], query: string): SearchKpi[] => {
  const q = normalize(query.trim());
  if (!q) return [];
  return index
    .map((item) => ({ item, pos: normalize(item.kpi.label).indexOf(q) }))
    .filter((m) => m.pos >= 0)
    .sort((a, b) => Number(a.pos !== 0) - Number(b.pos !== 0) || a.item.kpi.label.localeCompare(b.item.kpi.label))
    .map((m) => m.item);
};

interface Props {
  index: SearchKpi[];
  onSelect: (item: SearchKpi) => void;
}

/** Global header search across every KPI the caller can see, on all dashboards. */
export function KpiSearch({ index, onSelect }: Props) {
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listId = useId();

  const all = useMemo(() => searchKpis(index, query), [index, query]);
  const results = all.slice(0, MAX_RESULTS);

  useEffect(() => setActive(0), [query]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        inputRef.current?.focus();
        inputRef.current?.select();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const choose = (item: SearchKpi) => {
    setQuery('');
    setOpen(false);
    inputRef.current?.blur();
    onSelect(item);
  };

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown' && results.length) { e.preventDefault(); setOpen(true); setActive((i) => (i + 1) % results.length); }
    else if (e.key === 'ArrowUp' && results.length) { e.preventDefault(); setActive((i) => (i - 1 + results.length) % results.length); }
    else if (e.key === 'Enter' && results[active]) { e.preventDefault(); choose(results[active]); }
    else if (e.key === 'Escape') { setQuery(''); setOpen(false); }
  };

  const showList = open && query.trim().length > 0;
  const optionId = (i: number) => `${listId}-opt-${i}`;

  return (
    <div className="ad-kpi-search">
      <svg className="ad-kpi-search-icon" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
        <circle cx="11" cy="11" r="6.5" fill="none" stroke="currentColor" strokeWidth="1.8" />
        <path d="m16 16 4.5 4.5" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
      </svg>
      <input ref={inputRef} type="search" role="combobox" value={query} placeholder={t.searchKpisPlaceholder}
        aria-label={t.searchKpis} aria-expanded={showList} aria-controls={listId} aria-autocomplete="list"
        aria-activedescendant={showList && results[active] ? optionId(active) : undefined}
        onChange={(e) => { setQuery(e.target.value); setOpen(true); }}
        onFocus={() => setOpen(true)} onBlur={() => window.setTimeout(() => setOpen(false), 150)}
        onKeyDown={onKeyDown} />
      <kbd className="ad-kpi-search-hint" aria-hidden="true">Ctrl K</kbd>
      {showList && (
        <ul id={listId} role="listbox" className="ad-kpi-search-results" aria-label={t.searchKpis}>
          {results.length === 0 && <li className="ad-kpi-search-empty" role="presentation">{t.searchNoResults}</li>}
          {results.map((item, i) => (
            <li key={`${item.dashboardKey}:${item.categoryKey}:${item.kpi.key}`} id={optionId(i)} role="option"
              aria-selected={i === active} className={i === active ? 'is-active' : ''}
              onMouseEnter={() => setActive(i)} onMouseDown={(e) => { e.preventDefault(); choose(item); }}>
              <span className="ad-kpi-search-label">{item.kpi.label}</span>
              <span className="ad-kpi-search-value">{item.kpi.error ? '!' : formatNumber(item.kpi.count)}</span>
              <span className="ad-kpi-search-path">
                {item.dashboardKey === 'Overview' ? t.overview : item.dashboardTitle} › {item.categoryTitle}
              </span>
            </li>
          ))}
          {all.length > MAX_RESULTS && (
            <li className="ad-kpi-search-more" role="presentation">{t.searchMoreResults(all.length - MAX_RESULTS)}</li>
          )}
        </ul>
      )}
    </div>
  );
}
