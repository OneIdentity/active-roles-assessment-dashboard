import type { DashboardView, ViewCategory, ViewKpi } from './api';
import { LicensingDonuts, LicensingThresholds, LicensingTrend } from './LicensingCharts';
import { RingChart } from './RingChart';
import { formatNumber, t } from './strings';

const SUMMARY_KEYS: Record<string, { icon: string; capacity: string }> = {
  ADUserAccounts: { icon: 'users', capacity: '28.4%' },
  AdOverviewUsers: { icon: 'users', capacity: '28.4%' },
  ADGroups: { icon: 'groups', capacity: '84.1%' },
  AdOverviewGroups: { icon: 'groups', capacity: '84.1%' },
  Computers: { icon: 'computers', capacity: '68.2%' },
  AdOverviewComputers: { icon: 'computers', capacity: '68.2%' },
};

const RISK_GROUPS = [
  ['EmptyGroups', 'NoGroupOwner', 'NoManagerServiceAccount', 'NoManagerUser', 'SpnUserAccounts', 'UserAccountLockedOut'],
  ['CircularGroupNesting', 'ExpiredUsers', 'NeverLoggedIn', 'ReversibleEncryption', 'StaleUsers', 'UnconstrainedComputers'],
];

const SOURCE_COLOR: Record<string, string> = {
  'Active Directory': 'var(--chart-ring-blue-400)',
  'Entra ID': 'var(--chart-ring-green-300)',
};

const kpiValue = (kpi: ViewKpi) => kpi.error ?? formatNumber(kpi.count);
const hasRisk = (kpi: ViewKpi) => kpi.error != null || kpi.count > 0;

function SummaryMetrics({ category }: { category: ViewCategory }) {
  const kpis = category.kpis.filter((k) => SUMMARY_KEYS[k.key]);
  if (kpis.length === 0) return null;
  return (
    <section className="ad-overview" aria-label={category.title}>
      <div className="ad-summary-metrics">
        {kpis.map((kpi) => {
          const meta = SUMMARY_KEYS[kpi.key];
          return (
            <div className="ad-summary-metric" key={kpi.key}>
              <div className="ad-summary-label">
                <span className={`ad-summary-icon ad-summary-icon-${meta.icon}`} aria-hidden="true" />
                <span>{kpi.label}</span>
              </div>
              <div className="ad-summary-value">{kpiValue(kpi)}</div>
              <div className="ad-capacity-track" aria-hidden="true"><span style={{ width: meta.capacity }} /></div>
            </div>
          );
        })}
      </div>
    </section>
  );
}

function SourceDistribution({ category, view }: { category: ViewCategory; view: DashboardView }) {
  return (
    <section className="ad-source-card" aria-labelledby="ad-source-title">
      <div className="ad-section-heading">
        <h2 id="ad-source-title">{t.sourceDistribution}</h2>
        <p>{t.sourceDistributionHint}</p>
      </div>
      {category.charts.length > 0 && (
        <div className="category-charts-wrap">
          <div className="category-charts-toolbar" />
          <div className="category-charts">
            {category.charts.map((chart) => <RingChart key={chart.key} chart={chart} />)}
          </div>
        </div>
      )}
      <ul className="ad-source-legend" aria-label={t.dataSources}>
        {view.sourceSplit.map((s) => (
          <li key={s.source}>
            <span className="ad-source-swatch" style={{ ['--source-color' as string]: SOURCE_COLOR[s.source] ?? 'var(--chart-ring-blue-400)' }} />
            <span>{s.source}</span>
            <strong>{formatNumber(s.count)}</strong>
          </li>
        ))}
        {view.managedObjects && (
          <li className="ad-source-ars">
            <span className="ad-source-swatch" aria-hidden="true" />
            <span>{t.arsManagedObjects}</span>
            <strong>{kpiValue(view.managedObjects)}</strong>
          </li>
        )}
      </ul>
    </section>
  );
}

function Governance({ category, onKpi }: { category: ViewCategory; onKpi: (kpi: ViewKpi) => void }) {
  const byKey = new Map(category.kpis.map((k) => [k.key, k]));
  const known = new Set(RISK_GROUPS.flat());
  // KPIs without a fixed UX slot are appended to the shorter column so nothing is dropped.
  const extra = category.kpis.filter((k) => !known.has(k.key));
  const groups = RISK_GROUPS.map((g) => g.map((key) => byKey.get(key)).filter((k): k is ViewKpi => !!k));
  extra.forEach((k) => (groups[0].length <= groups[1].length ? groups[0] : groups[1]).push(k));
  const actionRequired = category.kpis.filter(hasRisk).length;

  return (
    <details className="iris-accordion ad-governance-card" open>
      <summary className="iris-accordion-header ad-governance-header" aria-labelledby={`gov-${category.key}`}>
        <span className="iris-accordion-caret" aria-hidden="true" />
        <span className="ad-governance-heading">
          <h2 id={`gov-${category.key}`}>{category.title}</h2>
          <span className={`ad-risk-badge ${actionRequired === 0 ? 'clear' : ''}`}>
            {actionRequired === 0 ? t.noActionRequired : t.actionRequired}
          </span>
        </span>
      </summary>
      <div className="iris-accordion-content ad-governance-content">
        <div className="ad-governance-surface">
          <div className="ad-risk-groups">
            {groups.map((group, i) => (
              <div className="ad-risk-group" key={i}>
                {group.map((kpi) => {
                  const risk = hasRisk(kpi);
                  return (
                    <button type="button" key={kpi.key} className={`kpi ad-risk-row ${risk ? 'has-risk' : 'is-clear'}`}
                      data-section={kpi.sectionId} aria-label={`${kpi.label}: ${kpiValue(kpi)}. ${t.viewDetails}`}
                      onClick={() => onKpi(kpi)}>
                      <span className={`ad-risk-icon ${risk ? 'has-risk' : ''}`} aria-hidden="true" />
                      <span className="ad-risk-copy">
                        <span className="ad-risk-title">{kpi.label}</span>
                        <span className="ad-risk-description">{t.risk[kpi.key] ?? ''}</span>
                      </span>
                      <span className="ad-risk-value">{kpiValue(kpi)}</span>
                    </button>
                  );
                })}
              </div>
            ))}
          </div>
        </div>
      </div>
    </details>
  );
}

function CategoryCharts({ category }: { category: ViewCategory }) {
  if (category.charts.length === 0) return null;
  return (
    <div className="category-charts-wrap">
      <div className="category-charts-toolbar" />
      <div className="category-charts">
        {category.charts.map((chart) => <RingChart key={chart.key} chart={chart} />)}
      </div>
    </div>
  );
}

function AdditionalMetrics({ categories, onKpi }: { categories: ViewCategory[]; onKpi: (kpi: ViewKpi) => void }) {
  if (categories.length === 0) return null;
  return (
    <details className="iris-accordion ad-additional-metrics">
      <summary className="iris-accordion-header">
        <span className="iris-accordion-caret" aria-hidden="true" />
        <span>{t.additionalMetrics}</span>
      </summary>
      <div className="iris-accordion-content ad-additional-metrics-content">
        {categories.map((category) => (
          <section className="ad-additional-category" key={category.key}>
            <h3>{category.title}</h3>
            <div className="kpi-grid">
              {category.kpis.map((kpi) => (
                <button type="button" key={kpi.key} data-section={kpi.sectionId} onClick={() => onKpi(kpi)}
                  className={`kpi ${kpi.colorToken} iris-kpi-row ${kpi.isRisk ? 'is-risk' : ''} ${kpi.isRisk && hasRisk(kpi) ? 'has-risk' : ''}`}>
                  <span className="lbl">{kpi.label}</span>
                  <span className="val">{kpiValue(kpi)}</span>
                </button>
              ))}
            </div>
            <CategoryCharts category={category} />
          </section>
        ))}
      </div>
    </details>
  );
}

interface Props {
  view: DashboardView;
  onKpi: (kpi: ViewKpi) => void;
}

/** Port of the `.ad-sections` body of the UX `_AdDashboardFrame.cshtml`. */
export function DashboardContent({ view, onKpi }: Props) {
  const overview = view.categories.find((c) => c.isOverview);
  const risk = view.categories.filter((c) => c.isRisk);
  const additional = view.categories.filter((c) => !c.isOverview && !c.isRisk);

  return (
    <div className="ad-sections">
      {overview && <SummaryMetrics category={overview} />}
      {overview && <SourceDistribution category={overview} view={view} />}
      {risk.map((c) => <Governance key={c.key} category={c} onKpi={onKpi} />)}
      {view.licensing && <LicensingDonuts view={view.licensing} />}
      {view.licensing && <LicensingThresholds view={view.licensing} />}
      <AdditionalMetrics categories={additional} onKpi={onKpi} />
      {view.licensing && <LicensingTrend view={view.licensing} />}
    </div>
  );
}
