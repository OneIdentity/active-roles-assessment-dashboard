import type { ChartDataset } from './api';
import { formatNumber, t } from './strings';

/** Matches the UX `_CategoryCharts.cshtml` RingColor mapping. */
export const ringColor = (token: string) =>
  token === 'blue' ? 'var(--chart-ring-blue-400)'
    : token === 'teal' ? 'var(--chart-ring-green-300)'
      : `var(--chart-color-${token})`;

const RADIUS = 48;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

/** Static SVG ring + legend used by Overview categories (120x120, stroke 24). */
export function RingChart({ chart }: { chart: ChartDataset }) {
  const total = chart.values.reduce((sum, v) => sum + v, 0);
  let cursor = 0;
  return (
    <div className="category-chart">
      <h3 className="category-chart-title">{chart.title}</h3>
      <div className="chart-ring-row">
        <div className="chart-ring-wrap">
          <svg className="chart-ring" viewBox="0 0 120 120" width="120" height="120" role="img" aria-label={chart.title}>
            <g transform="rotate(-90 60 60)">
              {chart.values.map((value, i) => {
                const slice = total > 0 ? (value * CIRCUMFERENCE) / total : 0;
                const offset = -cursor;
                cursor += slice;
                return (
                  <circle key={chart.labels[i]} cx="60" cy="60" r={RADIUS} fill="none"
                    stroke={ringColor(chart.colorTokens[i])} strokeWidth="24"
                    strokeDasharray={`${slice} ${CIRCUMFERENCE}`} strokeDashoffset={offset} />
                );
              })}
            </g>
          </svg>
          <div className="chart-ring-center" aria-hidden="true">
            <span className="chart-ring-total">{formatNumber(total)}</span>
            <span className="chart-ring-caption">{t.total}</span>
          </div>
        </div>
        <ul className="chart-ring-legend">
          {chart.labels.map((label, i) => (
            <li key={label}>
              <span className="chart-ring-swatch" style={{ background: ringColor(chart.colorTokens[i]) }} />
              {label} ({total > 0 ? Math.round((chart.values[i] * 100) / total) : 0}%)
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
