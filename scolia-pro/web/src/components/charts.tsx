import { money } from '../lib/format';
import type { SeriesPoint } from '../api/types';

/** Histogramme SVG léger : recettes (barre) et montant attendu (repère). */
export function BarChart({ data, height = 220 }: { data: SeriesPoint[]; height?: number }) {
  const width = 640;
  const pad = { top: 16, right: 12, bottom: 28, left: 12 };
  const max = Math.max(1, ...data.map((d) => Math.max(d.value, d.secondary ?? 0)));
  const step = (width - pad.left - pad.right) / Math.max(1, data.length);
  const bar = Math.min(34, step * 0.56);
  const y = (v: number) => pad.top + (height - pad.top - pad.bottom) * (1 - v / max);
  return (
    <svg className="chart" viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Recettes par mois">
      {[0.25, 0.5, 0.75, 1].map((t) => <line key={t} x1={pad.left} x2={width - pad.right} y1={y(max * t)} y2={y(max * t)} stroke="var(--border)" strokeDasharray="3 4" />)}
      {data.map((d, i) => {
        const x = pad.left + i * step + (step - bar) / 2;
        return (
          <g key={d.label}>
            <title>{`${d.label} : ${money(d.value)} encaissés${d.secondary ? ` sur ${money(d.secondary)} attendus` : ''}`}</title>
            {d.secondary ? <rect x={x - 3} y={y(d.secondary)} width={bar + 6} height={height - pad.bottom - y(d.secondary)} rx={5} fill="var(--table-head)" /> : null}
            <rect x={x} y={y(d.value)} width={bar} height={Math.max(0, height - pad.bottom - y(d.value))} rx={4} fill="var(--blue)" />
            <text x={x + bar / 2} y={height - 9} textAnchor="middle">{d.label}</text>
          </g>
        );
      })}
    </svg>
  );
}

/** Anneau de progression (taux de recouvrement). */
export function Ring({ value, size = 132, label }: { value: number; size?: number; label: string }) {
  const r = size / 2 - 10;
  const c = 2 * Math.PI * r;
  const v = Math.max(0, Math.min(100, value));
  const color = v >= 80 ? 'var(--green)' : v >= 60 ? 'var(--orange)' : 'var(--red)';
  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} role="img" aria-label={`${label} ${v} %`}>
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--table-head)" strokeWidth={12} />
      <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke={color} strokeWidth={12} strokeLinecap="round"
        strokeDasharray={`${(c * v) / 100} ${c}`} transform={`rotate(-90 ${size / 2} ${size / 2})`} />
      <text x="50%" y="48%" textAnchor="middle" fontSize="24" fontWeight="800" fill="var(--text)">{Math.round(v)}%</text>
      <text x="50%" y="64%" textAnchor="middle" fontSize="11" fill="var(--muted)">{label}</text>
    </svg>
  );
}

/** Barres horizontales (balance âgée, répartition). */
export function HBars({ data, format = money }: { data: SeriesPoint[]; format?: (v: number) => string }) {
  const max = Math.max(1, ...data.map((d) => d.value));
  const tones = ['var(--blue)', 'var(--orange)', '#ea580c', 'var(--red)'];
  return (
    <div className="stack" style={{ gap: 12 }}>
      {data.map((d, i) => (
        <div key={d.label}>
          <div className="row" style={{ justifyContent: 'space-between' }}>
            <span className="small" style={{ fontWeight: 600 }}>{d.label}{d.secondary ? <span className="muted"> · {d.secondary} dossier(s)</span> : null}</span>
            <span className="small mono" style={{ fontWeight: 700 }}>{format(d.value)}</span>
          </div>
          <div className="progress" style={{ marginTop: 6 }}><span style={{ width: `${(d.value / max) * 100}%`, background: tones[i % tones.length] }} /></div>
        </div>
      ))}
    </div>
  );
}
