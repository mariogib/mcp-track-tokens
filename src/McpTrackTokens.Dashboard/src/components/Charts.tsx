import type { ReactNode } from 'react';
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';

const COLORS = [
  'var(--chart-1)',
  'var(--chart-2)',
  'var(--chart-3)',
  'var(--chart-4)',
  'var(--chart-5)',
];

export function ChartCard({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="chart-card" aria-label={title}>
      <h3>{title}</h3>
      <div style={{ width: '100%', height: 220 }}>{children}</div>
    </section>
  );
}

type SeriesPoint = Record<string, string | number>;

export function DailyLineChart({
  data,
  xKey,
  yKey,
  yLabel,
}: {
  data: SeriesPoint[];
  xKey: string;
  yKey: string;
  yLabel?: string;
}) {
  return (
    <ResponsiveContainer>
      <LineChart data={data} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" />
        <XAxis dataKey={xKey} tick={{ fill: 'var(--text-muted)', fontSize: 12 }} />
        <YAxis tick={{ fill: 'var(--text-muted)', fontSize: 12 }} width={48} />
        <Tooltip
          contentStyle={{
            background: 'var(--bg-elevated)',
            border: '1px solid var(--border)',
            borderRadius: 8,
          }}
          labelStyle={{ color: 'var(--text-primary)' }}
        />
        <Line
          type="monotone"
          dataKey={yKey}
          name={yLabel ?? yKey}
          stroke="var(--chart-1)"
          strokeWidth={2}
          dot={false}
        />
      </LineChart>
    </ResponsiveContainer>
  );
}

export function NamedBarChart({
  data,
  nameKey = 'name',
  valueKey,
  valueLabel,
}: {
  data: SeriesPoint[];
  nameKey?: string;
  valueKey: string;
  valueLabel?: string;
}) {
  return (
    <ResponsiveContainer>
      <BarChart data={data} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" />
        <XAxis dataKey={nameKey} tick={{ fill: 'var(--text-muted)', fontSize: 12 }} />
        <YAxis tick={{ fill: 'var(--text-muted)', fontSize: 12 }} width={48} />
        <Tooltip
          contentStyle={{
            background: 'var(--bg-elevated)',
            border: '1px solid var(--border)',
            borderRadius: 8,
          }}
        />
        <Bar dataKey={valueKey} name={valueLabel ?? valueKey} fill="var(--chart-2)" radius={[4, 4, 0, 0]} />
      </BarChart>
    </ResponsiveContainer>
  );
}

export function NamedPieChart({
  data,
  nameKey = 'name',
  valueKey,
}: {
  data: SeriesPoint[];
  nameKey?: string;
  valueKey: string;
}) {
  return (
    <ResponsiveContainer>
      <PieChart>
        <Pie data={data} dataKey={valueKey} nameKey={nameKey} innerRadius={48} outerRadius={80} paddingAngle={2}>
          {data.map((_, index) => (
            <Cell key={String(index)} fill={COLORS[index % COLORS.length]} />
          ))}
        </Pie>
        <Tooltip
          contentStyle={{
            background: 'var(--bg-elevated)',
            border: '1px solid var(--border)',
            borderRadius: 8,
          }}
        />
        <Legend />
      </PieChart>
    </ResponsiveContainer>
  );
}
