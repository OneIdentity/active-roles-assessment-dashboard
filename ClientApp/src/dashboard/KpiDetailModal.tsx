import { useEffect, useRef, useState } from 'react';
import { getKpiDetail, type KpiDetail, type ViewKpi } from './api';
import { formatNumber, t } from './strings';

const MAX_ROWS = 500;

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

  return (
    <div className="iris-kpi-modal-backdrop" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <section className="iris-kpi-modal" role="dialog" aria-modal="true" aria-labelledby="kpiDetailModalTitle" tabIndex={-1} ref={dialogRef}>
        <header className="iris-kpi-modal-header">
          <h2 id="kpiDetailModalTitle">{kpi.label} ({kpi.error ?? formatNumber(kpi.count)})</h2>
          <div className="iris-kpi-modal-actions">
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
          {detail && detail.error && <p className="muted">{detail.error}</p>}
          {detail && !detail.error && detail.rows.length === 0 && <p className="muted">{t.noDetails}</p>}
          {detail && detail.rows.length > 0 && (
            <div className="panel">
              <div className="panel-content">
                {detail.rows.length > MAX_ROWS && <p className="kpi-detail-meta">{t.rowsShown(MAX_ROWS, detail.rows.length)}</p>}
                <table className="kpi-detail-table">
                  <thead><tr>{detail.columns.map((c) => <th key={c} scope="col">{c}</th>)}</tr></thead>
                  <tbody>
                    {rows.map((row, i) => <tr key={i}>{row.map((cell, j) => <td key={j}>{cell}</td>)}</tr>)}
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
