import { useEffect, useMemo, useRef, useState } from 'react';
import { getKpiDetail, withBase, type KpiDetail, type ViewKpi } from './api';
import { breakableDn, classifyColumns } from './kpiDetailColumns';
import { DetailColumnFilter } from './DetailColumnFilter';
import { formatNumber, t } from './strings';

const MAX_ROWS = 500;

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });
const compareText = (a: string, b: string) => collator.compare(a.trim(), b.trim());

const treeHref = (dn: string) => withBase(`/GroupTree?Group=${encodeURIComponent(dn)}&returnPage=App`);
const webHref = (baseUrl: string, dn: string) => `${baseUrl.replace(/\/+$/, '')}/redirect.ashx?dn=${encodeURIComponent(dn)}`;

function ExternalLinkIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6" /><path d="M15 3h6v6" /><path d="M10 14 21 3" />
    </svg>
  );
}

function TreeIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87" /><path d="M16 3.13a4 4 0 0 1 0 7.75" />
    </svg>
  );
}

interface Props {
  kpi: ViewKpi;
  canExport: boolean;
  onExport: (kpi: ViewKpi) => void;
  onClose: () => void;
}

/** React port of the UX `_KpiDetailModal.cshtml`. */
export function KpiDetailModal({ kpi, canExport, onExport, onClose }: Props) {
  const [detail, setDetail] = useState<KpiDetail | null>(null);
  const [failed, setFailed] = useState(false);
  const dialogRef = useRef<HTMLElement>(null);
  const [sort, setSort] = useState<{ col: number; dir: 'asc' | 'desc' } | null>(null);
  const [filters, setFilters] = useState<Record<number, Set<string>>>({});

  useEffect(() => {
    let alive = true;
    setDetail(null);
    setFailed(false);
    setSort(null);
    setFilters({});
    getKpiDetail(kpi.key).then((d) => alive && setDetail(d)).catch(() => alive && setFailed(true));
    return () => { alive = false; };
  }, [kpi.key]);

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialogRef.current?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('keydown', onKey); previous?.focus(); };
  }, [onClose]);

  const rowDns = detail?.rowGroupTreeDns?.some((d) => d) ? detail.rowGroupTreeDns : null;
  const columnValues = useMemo(
    () => detail?.columns.map((_, j) => Array.from(new Set(detail.rows.map((r) => (r[j] ?? '').trim()))).sort(compareText)) ?? [],
    [detail],
  );
  const filtered = useMemo(() => {
    if (!detail) return [] as { row: string[]; index: number }[];
    const active = Object.entries(filters).map(([k, v]) => [Number(k), v] as const);
    const list = detail.rows
      .map((row, index) => ({ row, index }))
      .filter(({ row }) => active.every(([j, set]) => set.has((row[j] ?? '').trim())));
    if (sort) {
      const sign = sort.dir === 'asc' ? 1 : -1;
      list.sort((a, b) => sign * compareText(a.row[sort.col] ?? '', b.row[sort.col] ?? '') || a.index - b.index);
    }
    return list;
  }, [detail, filters, sort]);
  const visibleRows = filtered.slice(0, MAX_ROWS);
  const filterActive = Object.keys(filters).length > 0;
  const toggleSort = (j: number) => setSort((s) => (s?.col === j && s.dir === 'asc' ? { col: j, dir: 'desc' } : { col: j, dir: 'asc' }));
  const setColumnFilter = (j: number, next: Set<string> | undefined) => setFilters((f) => {
    const copy = { ...f };
    if (next) copy[j] = next; else delete copy[j];
    return copy;
  });
  // Privileged-group details list members with a Membership column; when any member is indirect,
  // the group itself has a nested tree. Prefer the server-supplied DN, else fall back to the group
  // name (GroupTree resolves names as well as DNs).
  const membershipCol = detail ? detail.columns.indexOf('Membership') : -1;
  const hasIndirect = membershipCol >= 0 && !!detail?.rows.some((r) => /^indirect$/i.test(r[membershipCol] ?? ''));
  const groupTreeTarget = detail?.groupTreeDn || (hasIndirect ? detail?.label || kpi.label : null);
  // The server appends an "Excluded" column (True/False) when exclusion patterns match rows.
  const excludedCol = detail ? detail.columns.indexOf('Excluded') : -1;
  const isExcluded = (row: string[]) => excludedCol >= 0 && /^true$/i.test(row[excludedCol] ?? '');
  const columnKinds = useMemo(() => (detail ? classifyColumns(detail.columns, detail.rows) : []), [detail]);
  const colClass = (j: number) => `kpi-col-${columnKinds[j] ?? 'wrap'}`;
  const dnCol = detail ? detail.columns.indexOf('Distinguished Name') : -1;
  const webUrl = detail?.webInterfaceUrl?.trim() ?? '';
  const hasActions = !!rowDns || dnCol >= 0;

  return (
    <div className="iris-kpi-modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <section className="iris-kpi-modal" role="dialog" aria-modal="true" aria-labelledby="kpiDetailModalTitle" tabIndex={-1} ref={dialogRef}>
        <header className="iris-kpi-modal-header">
          <h2 id="kpiDetailModalTitle">{kpi.label} ({kpi.error ?? formatNumber(kpi.count)})</h2>
          <div className="iris-kpi-modal-actions">
            {groupTreeTarget && (
              <a className="iris-kpi-modal-export" href={treeHref(groupTreeTarget)}>
                <TreeIcon />
                <span>{t.viewNestedMembership}</span>
              </a>
            )}
            {canExport && (
              <button type="button" className="iris-kpi-modal-export" onClick={() => onExport(kpi)}>
                <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M12 3v12m0 0 4-4m-4 4-4-4M5 16v4h14v-4" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" /></svg>
                <span>{t.export}</span>
              </button>
            )}
            <button type="button" className="iris-kpi-modal-close" aria-label={t.close} title={t.close} onClick={onClose}>
              <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="m6 6 12 12M18 6 6 18" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" /></svg>
            </button>
          </div>
        </header>
        <div className="iris-kpi-modal-body" aria-busy={!detail && !failed}>
          {!detail && !failed && <div className="spinner" aria-label={t.loading} />}
          {failed && <p role="alert">{t.loadFailed}</p>}
          {detail && detail.exclusionsNotApplied && <p className="kpi-exclusion-na-note" role="note">{t.exclusionsNotAppliedDetail}</p>}
          {detail && detail.error && <p className="muted">{detail.error}</p>}
          {detail && !detail.error && detail.rows.length === 0 && <p className="muted">{t.noDetails}</p>}
          {detail && detail.rows.length > 0 && (
            <div className="panel">
              <div className="panel-content">
                {filtered.length > MAX_ROWS && <p className="kpi-detail-meta">{t.rowsShown(MAX_ROWS, filtered.length)}</p>}
                {filterActive && (
                  <p className="kpi-detail-meta" role="status">
                    {t.rowsFiltered(filtered.length, detail.rows.length)}{' '}
                    <button type="button" className="kpi-clear-filters" onClick={() => setFilters({})}>{t.clearFilters}</button>
                  </p>
                )}
                <div className="kpi-detail-scroll" tabIndex={0} role="region" aria-label={t.detailTableRegion(kpi.label)}>
                <table className="kpi-detail-table">
                  <thead><tr>{detail.columns.map((c, j) => {
                                      const label = j === excludedCol ? t.excluded : c;
                                      const dir = sort?.col === j ? sort.dir : null;
                                      return (
                                        <th key={c} scope="col" className={colClass(j)} aria-sort={dir === 'asc' ? 'ascending' : dir === 'desc' ? 'descending' : 'none'}>
                                          <div className="kpi-th-inner">
                                            <button type="button" className={`kpi-sort-btn${dir ? ` sort-${dir}` : ''}`} onClick={() => toggleSort(j)} title={dir === 'asc' ? t.sortDescending : t.sortAscending}>
                                              <span>{label}</span><span className="kpi-sort-indicator" aria-hidden="true">{dir === 'asc' ? '\u25B2' : dir === 'desc' ? '\u25BC' : '\u21C5'}</span>
                                            </button>
                                            <DetailColumnFilter column={label} values={columnValues[j] ?? []} selected={filters[j]} onChange={(next) => setColumnFilter(j, next)} />
                                          </div>
                                        </th>
                                      );
                                    })}{hasActions && <th scope="col" className="kpi-detail-action"><span className="visually-hidden">{t.actions}</span></th>}</tr></thead>
                  <tbody>
                    {visibleRows.length === 0 && (
                      <tr><td colSpan={detail.columns.length + (hasActions ? 1 : 0)} className="empty-row">{t.noFilterMatches}</td></tr>
                    )}
                    {visibleRows.map(({ row, index: i }) => (
                      <tr key={i} className={isExcluded(row) ? 'is-excluded' : undefined}>
                        {row.map((cell, j) => <td key={j} className={colClass(j)}>{j === excludedCol ? (isExcluded(row) ? <span className="kpi-excluded-badge" title={t.excludedHint}>{t.excluded}</span> : null) : columnKinds[j] === 'wrap' ? breakableDn(cell) : cell}</td>)}
                        {hasActions && (
                          <td className="kpi-detail-action">
                            {rowDns?.[i] && (
                              <a className="iris-kpi-modal-close" href={treeHref(rowDns[i]!)} title={t.viewNestedMembership} aria-label={`${t.viewNestedMembership}: ${row[0]}`}>
                                <TreeIcon />
                              </a>
                            )}
                            {dnCol >= 0 && row[dnCol] && (webUrl ? (
                              <a className="iris-kpi-modal-close" href={webHref(webUrl, row[dnCol])} target="_blank" rel="noopener noreferrer" title={t.openInWebInterface} aria-label={`${t.openInWebInterface}: ${row[0]}`}>
                                <ExternalLinkIcon />
                              </a>
                            ) : (
                              <span className="iris-kpi-modal-close is-disabled" role="img" title={t.configureWebInterface} aria-label={t.configureWebInterface}>
                                <ExternalLinkIcon />
                              </span>
                            ))}
                          </td>
                        )}
                      </tr>
                    ))}
                  </tbody>
                </table>
                </div>
              </div>
            </div>
          )}
        </div>
      </section>
    </div>
  );
}
