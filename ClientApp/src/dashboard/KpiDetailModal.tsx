import { useEffect, useMemo, useRef, useState } from 'react';
import { getKpiDetail, withBase, type KpiDetail, type ViewKpi } from './api';
import { breakableDn, classifyColumns } from './kpiDetailColumns';
import { formatNumber, t } from './strings';

const MAX_ROWS = 500;

const treeHref = (dn: string) => withBase(`/GroupTree?Group=${encodeURIComponent(dn)}&returnPage=App`);

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

  useEffect(() => {
    let alive = true;
    setDetail(null);
    setFailed(false);
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

  const rows = detail?.rows.slice(0, MAX_ROWS) ?? [];
  const rowDns = detail?.rowGroupTreeDns?.some((d) => d) ? detail.rowGroupTreeDns : null;
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
                {detail.rows.length > MAX_ROWS && <p className="kpi-detail-meta">{t.rowsShown(MAX_ROWS, detail.rows.length)}</p>}
                <table className="kpi-detail-table">
                  <thead><tr>{detail.columns.map((c, j) => <th key={c} scope="col" className={colClass(j)}>{j === excludedCol ? t.excluded : c}</th>)}{rowDns && <th scope="col" className="kpi-detail-action"><span className="visually-hidden">{t.viewNestedMembership}</span></th>}</tr></thead>
                  <tbody>
                    {rows.map((row, i) => (
                      <tr key={i} className={isExcluded(row) ? 'is-excluded' : undefined}>
                        {row.map((cell, j) => <td key={j} className={colClass(j)}>{j === excludedCol ? (isExcluded(row) ? <span className="kpi-excluded-badge" title={t.excludedHint}>{t.excluded}</span> : null) : columnKinds[j] === 'wrap' ? breakableDn(cell) : cell}</td>)}
                        {rowDns && (
                          <td className="kpi-detail-action">
                            {rowDns[i] && (
                              <a className="iris-kpi-modal-close" href={treeHref(rowDns[i]!)} title={t.viewNestedMembership} aria-label={`${t.viewNestedMembership}: ${row[0]}`}>
                                <TreeIcon />
                              </a>
                            )}
                          </td>
                        )}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </div>
      </section>
    </div>
  );
}
