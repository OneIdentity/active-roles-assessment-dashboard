import type { ChartDataset, LicensingSegment, LicensingView } from './api';
import { RingChart, ringColor } from './RingChart';
import { formatNumber, t } from './strings';

const PALETTE = ['blue', 'teal', 'purple', 'amber', 'pink', 'indigo', 'orange', 'green', 'slate', 'red'];
const seriesColor = (i: number) => `var(--chart-color-${PALETTE[i % PALETTE.length]})`;

const segmentLabel = (s: LicensingSegment) => {
  const suffix = t.licKind[s.kind];
  return suffix ? `${s.name} \u2014 ${suffix}` : s.name;
};

const toRing = (key: string, title: string, segments: LicensingSegment[]): ChartDataset => ({
  key,
  title,
  type: 'doughnut',
  disableTypeToggle: true,
  sliceOffset: 0,
  labels: segments.map(segmentLabel),
  values: segments.map((s) => s.value),
  colors: [],
  colorTokens: segments.map((_, i) => PALETTE[i % PALETTE.length]),
});

const W = 640;
const H = 240;
const PAD = { top: 12, right: 16, bottom: 28, left: 48 };

function TrendChart({ view }: { view: LicensingView }) {
  const n = view.trendLabels.length;
  if (n === 0 || view.trend.length === 0) return <p className="muted">{t.noData}</p>;
  const max = Math.max(1, ...view.trend.flatMap((s) => s.values));
  const x = (i: number) => PAD.left + (n === 1 ? (W - PAD.left - PAD.right) / 2 : (i * (W - PAD.left - PAD.right)) / (n - 1));
  const y = (v: number) => PAD.top + (H - PAD.top - PAD.bottom) * (1 - v / max);
  const ticks = [0, 0.5, 1].map((f) => Math.round(max * f));
  const labelStep = Math.max(1, Math.ceil(n / 6));

  return (
    <div className="licensing-trend">
      <svg viewBox={`0 0 ${W} ${H}`} width="100%" role="img" aria-label={t.managedObjectsTrend}>
        {ticks.map((v) => (
          <g key={v}>
            <line x1={PAD.left} x2={W - PAD.right} y1={y(v)} y2={y(v)} stroke="var(--oi-border-color-muted)" />
            <text x={PAD.left - 6} y={y(v) + 4} textAnchor="end" fontSize="11" fill="var(--oi-content-color-tertiary)">{formatNumber(v)}</text>
          </g>
        ))}
        {view.trendLabels.map((label, i) => (i % labelStep === 0 || i === n - 1) && (
          <text key={label + i} x={x(i)} y={H - 8} textAnchor="middle" fontSize="11" fill="var(--oi-content-color-tertiary)">{label.slice(0, 10)}</text>
        ))}
        {view.trend.map((s, si) => (
          <g key={s.category + s.name}>
            <polyline fill="none" stroke={seriesColor(si)} strokeWidth="2"
              points={s.values.map((v, i) => `${x(i)},${y(v)}`).join(' ')} />
            {s.values.map((v, i) => <circle key={i} cx={x(i)} cy={y(v)} r="3" fill={seriesColor(si)}><title>{`${s.name}: ${formatNumber(v)}`}</title></circle>)}
          </g>
        ))}
      </svg>
      <ul className="ad-source-legend">
        {view.trend.map((s, si) => (
          <li key={s.category + s.name}>
            <span className="ad-source-swatch" style={{ ['--source-color' as string]: seriesColor(si) }} />
            <span>{s.name}</span>
            <strong>{formatNumber(s.values[s.values.length - 1] ?? 0)}</strong>
          </li>
        ))}
      </ul>
    </div>
  );
}

function ThresholdChart({ view }: { view: LicensingView }) {
  return (
    <ul className="licensing-thresholds">
      {view.thresholds.map((row) => {
        const scale = Math.max(1, row.total, row.threshold);
        const over = row.threshold > 0 && row.total > row.threshold;
        return (
          <li key={row.key}>
            <div className="licensing-threshold-head">
              <span>{t.licCat[row.key] ?? row.key}</span>
              <strong>
                {formatNumber(row.total)}
                {' / '}
                {row.threshold > 0 ? formatNumber(row.threshold) : t.notConfigured}
              </strong>
            </div>
            <div className="ad-capacity-track licensing-threshold-track" aria-hidden="true">
              <span style={{ width: `${(row.total * 100) / scale}%`, background: over ? 'var(--oi-base-color-error)' : undefined }} />
              {row.threshold > 0 && (
                <i className="licensing-threshold-marker" style={{ left: `${(row.threshold * 100) / scale}%` }} title={t.entitlement} />
              )}
            </div>
          </li>
        );
      })}
    </ul>
  );
}

function DonutWithLegend({ chartKey, title, segments }: { chartKey: string; title: string; segments: LicensingSegment[] }) {
  if (segments.length === 0) {
    return <div className="category-chart"><h3 className="category-chart-title">{title}</h3><p className="muted">{t.noData}</p></div>;
  }
  const chart = toRing(chartKey, title, segments);
  return (
    <div className="licensing-donut">
      <RingChart chart={chart} />
      <ul className="ad-source-legend licensing-donut-legend" aria-label={title}>
        {segments.map((s, i) => (
          <li key={`${s.kind}-${s.name}`}>
            <span className="ad-source-swatch" style={{ ['--source-color' as string]: ringColor(chart.colorTokens[i]) }} />
            <span>{segmentLabel(s)}</span>
            <strong>{formatNumber(s.value)}</strong>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** Composition + excluded donuts (port of the Razor Licensing page). */
export function LicensingDonuts({ view }: { view: LicensingView }) {
  if (view.error) {
    return <section className="ad-source-card"><p className="muted">{view.error}</p></section>;
  }
  return (
    <section className="ad-source-card" aria-label={`${t.licensingComposition} / ${t.licensingExcluded}`}>
      <div className="category-charts-wrap">
        <div className="category-charts">
          <DonutWithLegend chartKey="licComposition" title={t.licensingComposition} segments={view.composition} />
          <DonutWithLegend chartKey="licExcluded" title={t.licensingExcluded} segments={view.excluded} />
        </div>
      </div>
      <p className="muted licensing-intro">{t.licensingCompositionIntro} {t.licensingExcludedIntro}</p>
    </section>
  );
}

export function LicensingThresholds({ view }: { view: LicensingView }) {
  if (view.error || !view.thresholds.some((r) => r.threshold > 0)) return null;
  return (
    <section className="ad-source-card" aria-labelledby="lic-threshold-title">
      <div className="ad-section-heading">
        <h2 id="lic-threshold-title">
          {t.licensingThresholds}
          {view.overThreshold && <span className="ad-risk-badge licensing-over-badge">{t.overEntitlement}</span>}
        </h2>
      </div>
      <ThresholdChart view={view} />
    </section>
  );
}

export function LicensingTrend({ view }: { view: LicensingView }) {
  if (view.error) return null;
  return (
    <section className="ad-source-card" aria-labelledby="lic-trend-title">
      <div className="ad-section-heading">
        <h2 id="lic-trend-title">{t.managedObjectsTrend}</h2>
      </div>
      <TrendChart view={view} />
    </section>
  );
}
