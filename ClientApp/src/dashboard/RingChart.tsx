import { useState } from 'react';
import type { ChartDataset } from './api';
import { formatNumber, t } from './strings';

/** Matches the UX `_CategoryCharts.cshtml` RingColor mapping. */
export const ringColor = (token: string) =>
  token === 'blue' ? 'var(--chart-ring-blue-400)'
    : token === 'teal' ? 'var(--chart-ring-green-300)'
      : `var(--chart-color-${token})`;

const RADIUS = 48;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

/** Static SVG ring + legend used by Overview categories (120x120, stroke 24). Bar charts render as horizontal bars and can be toggled unless disableTypeToggle is set. */
export function RingChart({ chart }: { chart: ChartDataset }) {
  const [asBar, setAsBar] = useState(chart.type === 'bar');
  const total = chart.values.reduce((sum, v) => sum + v, 0);
  const max = Math.max(1, ...chart.values);
  let cursor = 0;
  return (
    <div className="category-chart">
      <div className="category-chart-header">
        <h3 className="category-chart-title">{chart.title}</h3>
        {!chart.disableTypeToggle && (
          <button type="button" className="btn-chart-toggle" data-mode={asBar ? 'bar3d' : 'pie'}
            title={asBar ? t.showAsDonut : t.showAsBar} aria-label={asBar ? t.showAsDonut : t.showAsBar}
            onClick={() => setAsBar((v) => !v)}>
            {asBar
              ? <svg className="chart-toggle-icon" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><circle cx="12" cy="12" r="8" fill="none" stroke="currentColor" strokeWidth="4" /></svg>
              : <svg className="chart-toggle-icon" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path d="M4 20V10M10 20V4M16 20v-7M22 20H2" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" /></svg>}
          </button>
        )}
      </div>
      {asBar ? (
        <ul className="chart-bar-list">
          {chart.labels.map((label, i) => (
            <li key={label}>
              <span className="chart-bar-label">{label}</span>
              <span className="chart-bar-track">
                <span className="chart-bar-fill" style={{ width: `${(chart.values[i] * 100) / max}%`, background: ringColor(chart.colorTokens[i]) }} />
              </span>
              <span className="chart-bar-value">{formatNumber(chart.values[i])}</span>
            </li>
          ))}
        </ul>
      ) : (
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
      )}
    </div>
  );
}
