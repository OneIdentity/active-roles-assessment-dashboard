import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  getPerfTargets, getPerfTrends, runPerfTests,
  type PerfProbe, type PerfStatus, type PerfTarget, type PerfTrendSeries,
} from './api';
import { formatNumber } from './strings';
import { pt } from './perfStrings';

const PALETTE = ['blue', 'teal', 'purple', 'amber', 'pink', 'indigo', 'orange', 'green', 'slate', 'red'];
const seriesColor = (i: number) => `var(--chart-color-${PALETTE[i % PALETTE.length]})`;

const statusClass = (s: PerfStatus) =>
  s === 'Ok' ? 'is-ok' : s === 'Warn' ? 'is-warn' : s === 'Fail' ? 'is-fail' : 'is-skipped';

const testLabel = (k: string) => pt.test[k] ?? k;
const serverLabel = (k: string) => pt.server[k] ?? k;

export function PerformanceTests({ dashboard }: { dashboard: string }) {
  const [targets, setTargets] = useState<PerfTarget[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [testType, setTestType] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [running, setRunning] = useState(false);
  const [probes, setProbes] = useState<PerfProbe[] | null>(null);
  const [runError, setRunError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setTargets(null);
    setLoadError(null);
    setProbes(null);
    getPerfTargets(dashboard)
      .then((r) => {
        if (cancelled) return;
        if (!Array.isArray(r)) { setLoadError(r.error); setTargets([]); return; }
        setTargets(r);
        setSelected(new Set(r.map((x) => x.id)));
      })
      .catch(() => { if (!cancelled) { setLoadError(pt.loadFailed); setTargets([]); } });
    return () => { cancelled = true; };
  }, [dashboard]);

  const testTypes = useMemo(
    () => Array.from(new Set((targets ?? []).flatMap((x) => x.applicableTests))),
    [targets]);

  const visibleTargets = useMemo(
    () => (targets ?? []).filter((x) => !testType || x.applicableTests.includes(testType)),
    [targets, testType]);

  const allSelected = visibleTargets.length > 0 && visibleTargets.every((x) => selected.has(x.id));

  const toggle = (id: string) => setSelected((prev) => {
    const next = new Set(prev);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });

  const toggleAll = () => setSelected(allSelected ? new Set() : new Set(visibleTargets.map((x) => x.id)));

  const run = useCallback(async () => {
    const ids = visibleTargets.filter((x) => selected.has(x.id)).map((x) => x.id);
    if (ids.length === 0) return;
    setRunning(true);
    setRunError(null);
    try {
      const r = await runPerfTests(dashboard, testType || null, ids);
      if (r.error) { setRunError(r.error); setProbes([]); } else setProbes(r.probes);
    } catch {
      setRunError(pt.loadFailed);
    } finally {
      setRunning(false);
    }
  }, [dashboard, testType, selected, visibleTargets]);

  const selectedCount = visibleTargets.filter((x) => selected.has(x.id)).length;

  return (
    <details className="iris-accordion ad-category-accordion perf-tests">
      <summary className="iris-accordion-header">
        <span className="iris-accordion-caret" aria-hidden="true" />
        <span>{pt.title}</span>
      </summary>
      <div className="iris-accordion-content perf-content">
        <p className="muted">{pt.intro}</p>

        {targets === null && <p className="muted" aria-busy="true"><span className="perf-spinner" aria-hidden="true" /></p>}
        {loadError && <p className="perf-error" role="alert">{loadError}</p>}
        {targets && !loadError && targets.length === 0 && <p className="muted">{pt.noTargets}</p>}

        {targets && targets.length > 0 && (
          <>
            <div className="perf-controls">
              <div className="export-field">
                <label htmlFor={`perf-type-${dashboard}`}>{pt.testType}</label>
                <select id={`perf-type-${dashboard}`} value={testType} onChange={(e) => setTestType(e.target.value)}>
                  <option value="">{pt.all}</option>
                  {testTypes.map((x) => <option key={x} value={x}>{testLabel(x)}</option>)}
                </select>
              </div>
              <fieldset className="perf-targets">
                <legend>{pt.targets} <span className="muted">({selectedCount}/{visibleTargets.length})</span></legend>
                <label className="perf-check perf-check-all">
                  <input type="checkbox" checked={allSelected} onChange={toggleAll} />
                  <span>{pt.all}</span>
                </label>
                <div className="perf-target-list">
                  {visibleTargets.map((x) => (
                    <label key={x.id} className="perf-check" title={serverLabel(x.serverType)}>
                      <input type="checkbox" checked={selected.has(x.id)} onChange={() => toggle(x.id)} />
                      <span>{x.name}</span>
                      <span className="perf-chip">{serverLabel(x.serverType)}</span>
                    </label>
                  ))}
                </div>
              </fieldset>
            </div>
            <div className="perf-run-row">
              <button type="button" className="btn-primary" onClick={run} disabled={running || selectedCount === 0}>
                {running && <span className="perf-spinner" aria-hidden="true" />}
                {running ? pt.running : pt.run}
              </button>
              <span className="muted" role="status" aria-live="polite">
                {probes && !running && summarize(probes)}
              </span>
            </div>
          </>
        )}

        {runError && <p className="perf-error" role="alert">{runError}</p>}
        {probes && probes.length === 0 && !runError && <p className="muted">{pt.noResults}</p>}
        {probes && probes.length > 0 && (
          <>
            <ResultsTable probes={probes} />
            <LatencyBars probes={probes} />
          </>
        )}

        <PerfTrend dashboard={dashboard} />
      </div>
    </details>
  );
}

function summarize(probes: PerfProbe[]) {
  const count = (s: PerfStatus) => probes.filter((p) => p.status === s).length;
  return `${pt.status.Ok}: ${count('Ok')} · ${pt.status.Warn}: ${count('Warn')} · ${pt.status.Fail}: ${count('Fail')}`;
}

function ResultsTable({ probes }: { probes: PerfProbe[] }) {
  return (
    <div className="perf-table-wrap">
      <table className="kpi-detail-table perf-table">
        <thead>
          <tr>
            <th>{pt.colTarget}</th>
            <th>{pt.colServerType}</th>
            <th>{pt.colTest}</th>
            <th>{pt.colStatus}</th>
            <th className="num">{pt.colLatency}</th>
            <th>{pt.colDetail}</th>
          </tr>
        </thead>
        <tbody>
          {probes.map((p, i) => (
            <tr key={`${p.targetId}-${p.testType}-${i}`}>
              <td>{p.targetName}</td>
              <td>{serverLabel(p.serverType)}</td>
              <td>{testLabel(p.testType)}</td>
              <td><span className={`perf-status ${statusClass(p.status)}`}>{pt.status[p.status] ?? p.status}</span></td>
              <td className="num">{p.latencyMs != null ? `${formatNumber(p.latencyMs)} ms` : '—'}</td>
              <td className="perf-detail">{p.message}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function LatencyBars({ probes }: { probes: PerfProbe[] }) {
  const rows = probes.filter((p) => p.latencyMs != null);
  if (rows.length === 0) return null;
  const max = Math.max(1, ...rows.map((p) => p.latencyMs ?? 0));
  return (
    <section className="perf-chart" aria-label={pt.chartTitle}>
      <h3 className="category-chart-title">{pt.chartTitle}</h3>
      <ul className="perf-bars">
        {rows.map((p, i) => (
          <li key={`${p.targetId}-${p.testType}-${i}`}>
            <span className="perf-bar-label" title={`${p.targetName} – ${testLabel(p.testType)}`}>
              {p.targetName} <span className="muted">· {testLabel(p.testType)}</span>
            </span>
            <span className="perf-bar-track">
              <span className={`perf-bar-fill ${statusClass(p.status)}`} style={{ width: `${((p.latencyMs ?? 0) / max) * 100}%` }} />
            </span>
            <strong className="perf-bar-value">{formatNumber(p.latencyMs ?? 0)}</strong>
          </li>
        ))}
      </ul>
    </section>
  );
}

const W = 640;
const H = 200;
const PAD = { top: 12, right: 16, bottom: 28, left: 48 };

function PerfTrend({ dashboard }: { dashboard: string }) {
  const [period, setPeriod] = useState(24);
  const [series, setSeries] = useState<PerfTrendSeries[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setFailed(false);
    getPerfTrends(dashboard, period)
      .then((r) => { if (!cancelled) setSeries(r.series.filter((s) => s.samples.some((x) => x.latencyMs != null))); })
      .catch(() => { if (!cancelled) { setFailed(true); setSeries([]); } });
    return () => { cancelled = true; };
  }, [dashboard, period]);

  // Group series by test type so each chart compares targets on a common scale.
  const groups = useMemo(() => {
    const map = new Map<string, PerfTrendSeries[]>();
    for (const s of series ?? []) map.set(s.testType, [...(map.get(s.testType) ?? []), s]);
    return Array.from(map.entries());
  }, [series]);

  return (
    <section className="perf-trend">
      <div className="perf-trend-header">
        <h3 className="category-chart-title">{pt.trendTitle}</h3>
        <div className="export-field perf-period">
          <label htmlFor={`perf-period-${dashboard}`}>{pt.period}</label>
          <select id={`perf-period-${dashboard}`} value={period} onChange={(e) => setPeriod(Number(e.target.value))}>
            <option value={1}>{pt.last1h}</option>
            <option value={6}>{pt.last6h}</option>
            <option value={24}>{pt.last24h}</option>
          </select>
        </div>
      </div>
      <p className="muted">{pt.trendIntro}</p>
      {failed && <p className="perf-error" role="alert">{pt.loadFailed}</p>}
      {series && !failed && groups.length === 0 && <p className="muted">{pt.trendEmpty}</p>}
      <div className="perf-trend-charts">
        {groups.map(([test, list]) => <TrendChart key={test} title={testLabel(test)} series={list} />)}
      </div>
    </section>
  );
}

function TrendChart({ title, series }: { title: string; series: PerfTrendSeries[] }) {
  const times = series.flatMap((s) => s.samples.map((x) => Date.parse(x.timestampUtc)));
  const minT = Math.min(...times);
  const maxT = Math.max(...times);
  const max = Math.max(1, ...series.flatMap((s) => s.samples.map((x) => x.latencyMs ?? 0)));
  const x = (t: number) => PAD.left + (maxT === minT ? (W - PAD.left - PAD.right) / 2 : ((t - minT) * (W - PAD.left - PAD.right)) / (maxT - minT));
  const y = (v: number) => PAD.top + (H - PAD.top - PAD.bottom) * (1 - v / max);
  const ticks = [0, 0.5, 1].map((f) => Math.round(max * f));
  const timeFmt = (t: number) => new Date(t).toLocaleTimeString(document.documentElement.lang || undefined, { hour: '2-digit', minute: '2-digit' });

  return (
    <div className="category-chart perf-trend-chart">
      <h4 className="perf-trend-chart-title">{title} (ms)</h4>
      <svg viewBox={`0 0 ${W} ${H}`} width="100%" role="img" aria-label={title}>
        {ticks.map((v) => (
          <g key={v}>
            <line x1={PAD.left} x2={W - PAD.right} y1={y(v)} y2={y(v)} stroke="var(--oi-border-color-muted)" />
            <text x={PAD.left - 6} y={y(v) + 4} textAnchor="end" fontSize="11" fill="var(--oi-content-color-tertiary)">{formatNumber(v)}</text>
          </g>
        ))}
        {[minT, (minT + maxT) / 2, maxT].map((tt, i) => (
          <text key={i} x={x(tt)} y={H - 8} textAnchor={i === 0 ? 'start' : i === 2 ? 'end' : 'middle'} fontSize="11" fill="var(--oi-content-color-tertiary)">{timeFmt(tt)}</text>
        ))}
        {series.map((s, si) => {
          const pts = s.samples.filter((p) => p.latencyMs != null);
          return (
            <g key={s.targetId + s.testType}>
              <polyline fill="none" stroke={seriesColor(si)} strokeWidth="2"
                points={pts.map((p) => `${x(Date.parse(p.timestampUtc))},${y(p.latencyMs ?? 0)}`).join(' ')} />
              {pts.map((p, i) => (
                <circle key={i} cx={x(Date.parse(p.timestampUtc))} cy={y(p.latencyMs ?? 0)} r="2.5" fill={seriesColor(si)}>
                  <title>{`${s.targetName}: ${formatNumber(p.latencyMs ?? 0)} ms – ${timeFmt(Date.parse(p.timestampUtc))}`}</title>
                </circle>
              ))}
            </g>
          );
        })}
      </svg>
      <ul className="ad-source-legend">
        {series.map((s, si) => {
          const last = [...s.samples].reverse().find((p) => p.latencyMs != null);
          return (
            <li key={s.targetId + s.testType}>
              <span className="ad-source-swatch" style={{ ['--source-color' as string]: seriesColor(si) }} />
              <span>{s.targetName}</span>
              <strong>{last ? `${formatNumber(last.latencyMs ?? 0)} ms` : '—'}</strong>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
