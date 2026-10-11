import { useEffect, useState } from 'react';
import { exportReport, type DashboardTab, type DashboardView, type ExportRequest } from './api';
import { t } from './strings';

type Scope = ExportRequest['scope'];
type Format = ExportRequest['format'];

interface Props {
  view: DashboardView;
  tabs: DashboardTab[];
  /** Pre-selected KPI (when opened from the KPI detail modal). */
  initialKpi?: string;
  onClose: () => void;
  onResult: (ok: boolean) => void;
}

const OVERVIEW = 'Overview';

/** React port of the UX `_ExportDialog.cshtml`, posting to the existing `/Export` endpoint. */
export function ExportDialog({ view, tabs, initialKpi, onClose, onResult }: Props) {
  const isOverview = view.key === OVERVIEW;
  const subDashboards = isOverview ? tabs.filter((tab) => tab.key !== OVERVIEW) : [];
  const categories = view.categories;
  const kpis = categories.flatMap((c) => c.kpis.map((k) => ({ ...k, group: c.title })));

  const [scope, setScope] = useState<Scope>(initialKpi ? 'Kpi' : 'Dashboard');
  const [subKey, setSubKey] = useState(subDashboards[0]?.key ?? '');
  const [categoryKey, setCategoryKey] = useState(categories[0]?.key ?? '');
  const [kpiKey, setKpiKey] = useState(initialKpi ?? kpis[0]?.key ?? '');
  const [format, setFormat] = useState<Format>('Pdf');
  const [includeDetails, setIncludeDetails] = useState(true);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && !busy) onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [busy, onClose]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    try {
      await exportReport({
        scope,
        format,
        dashboardKey: isOverview ? 'Main' : view.key,
        subDashboardKey: scope === 'SubDashboard' ? subKey : undefined,
        categoryKey: scope === 'Category' ? categoryKey : undefined,
        kpiKey: scope === 'Kpi' ? kpiKey : undefined,
        includeDetails,
      });
      onResult(true);
      onClose();
    } catch {
      onResult(false);
      setBusy(false);
    }
  };

  const groups = [...new Set(kpis.map((k) => k.group))];

  return (
    <div className="export-modal-overlay" onMouseDown={(e) => e.target === e.currentTarget && !busy && onClose()}>
      <div className="export-modal" role="dialog" aria-modal="true" aria-labelledby="exportModalTitle">
        <div className="export-modal-header">
          <h2 id="exportModalTitle">{t.exportTitle}</h2>
          <button type="button" className="export-modal-close" aria-label={t.close} onClick={onClose} disabled={busy}>&times;</button>
        </div>
        <form onSubmit={submit}>
          <div className="export-field">
            <label htmlFor="exportScope">{t.whatToExport}</label>
            <select id="exportScope" value={scope} onChange={(e) => setScope(e.target.value as Scope)}>
              <option value="Dashboard">{t.entireDashboard}</option>
              {subDashboards.length > 0 && <option value="SubDashboard">{t.aDashboard}</option>}
              {categories.length > 0 && <option value="Category">{t.aCategory}</option>}
              {kpis.length > 0 && <option value="Kpi">{t.aSingleKpi}</option>}
            </select>
          </div>

          {scope === 'SubDashboard' && (
            <div className="export-field">
              <label htmlFor="exportSubDashboard">{t.dashboardField}</label>
              <select id="exportSubDashboard" value={subKey} onChange={(e) => setSubKey(e.target.value)}>
                {subDashboards.map((d) => <option key={d.key} value={d.key}>{d.title}</option>)}
              </select>
            </div>
          )}

          {scope === 'Category' && (
            <div className="export-field">
              <label htmlFor="exportCategory">{t.categoryField}</label>
              <select id="exportCategory" value={categoryKey} onChange={(e) => setCategoryKey(e.target.value)}>
                {categories.map((c) => <option key={c.key} value={c.key}>{c.title}</option>)}
              </select>
            </div>
          )}

          {scope === 'Kpi' && (
            <div className="export-field">
              <label htmlFor="exportKpi">{t.kpiField}</label>
              <select id="exportKpi" value={kpiKey} onChange={(e) => setKpiKey(e.target.value)}>
                {groups.map((g) => (
                  <optgroup key={g} label={g}>
                    {kpis.filter((k) => k.group === g).map((k) => <option key={k.key} value={k.key}>{k.label}</option>)}
                  </optgroup>
                ))}
              </select>
            </div>
          )}

          <div className="export-field">
            <label htmlFor="exportFormat">{t.formatField}</label>
            <select id="exportFormat" value={format} onChange={(e) => setFormat(e.target.value as Format)}>
              <option value="Pdf">PDF</option>
              <option value="Word">Word</option>
              <option value="Excel">Excel</option>
            </select>
          </div>

          <div className="export-field export-check">
            <label>
              <input type="checkbox" checked={includeDetails} onChange={(e) => setIncludeDetails(e.target.checked)} />
              {t.includeDetails}
            </label>
          </div>

          <div className="export-modal-actions">
            <button type="button" className="btn-secondary" onClick={onClose} disabled={busy}>{t.cancel}</button>
            <button type="submit" className="btn-primary" disabled={busy}>{busy ? t.exporting : t.exportTitle}</button>
          </div>
        </form>
      </div>
    </div>
  );
}
