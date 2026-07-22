import type { ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
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
import { Card } from '../shared/adminUi';

const COLORS = [
  'var(--chart-1)',
  'var(--chart-2)',
  'var(--chart-3)',
  'var(--chart-4)',
  'var(--chart-5)',
];

const tooltipContentStyle = {
  background: 'var(--bg-elevated)',
  border: '1px solid var(--border)',
  borderRadius: 8,
  color: 'var(--text-primary)',
};

const tooltipLabelStyle = { color: 'var(--text-primary)' };
const tooltipItemStyle = { color: 'var(--text-primary)' };

export function ChartCard({
  title,
  children,
  to,
  height = 220,
}: {
  title: string;
  children: ReactNode;
  to?: string;
  height?: number;
}) {
  const navigate = useNavigate();

  return (
    <Card
      className={`chart-card${to ? ' chart-card--link' : ''}`}
      onClick={to ? () => void navigate(to) : undefined}
    >
      <div className="chart-card-header">
        <h3>{title}</h3>
        {to ? (
          <span className="chart-card-open">
            Open analysis
            <span className="chart-card-open-arrow" aria-hidden="true">
              →
            </span>
          </span>
        ) : null}
      </div>
      <div
        style={{ width: '100%', height }}
        onClick={to ? (event) => event.stopPropagation() : undefined}
      >
        {children}
      </div>
    </Card>
  );
}

type SeriesPoint = Record<string, string | number>;

export function DailyLineChart({
  data,
  xKey,
  yKey,
  yLabel,
  onPointClick,
}: {
  data: SeriesPoint[];
  xKey: string;
  yKey: string;
  yLabel?: string;
  onPointClick?: (point: SeriesPoint) => void;
}) {
  return (
    <ResponsiveContainer>
      <LineChart
        data={data}
        margin={{ top: 8, right: 8, left: 0, bottom: 0 }}
        onClick={(state) => {
          const payload = state?.activePayload?.[0]?.payload as SeriesPoint | undefined;
          if (payload && onPointClick) onPointClick(payload);
        }}
      >
        <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" />
        <XAxis dataKey={xKey} tick={{ fill: 'var(--text-muted)', fontSize: 12 }} />
        <YAxis tick={{ fill: 'var(--text-muted)', fontSize: 12 }} width={48} />
        <Tooltip
          contentStyle={tooltipContentStyle}
          labelStyle={tooltipLabelStyle}
          itemStyle={tooltipItemStyle}
        />
        <Line
          type="monotone"
          dataKey={yKey}
          name={yLabel ?? yKey}
          stroke="var(--chart-1)"
          strokeWidth={2}
          dot={!!onPointClick}
          activeDot={onPointClick ? { r: 5 } : undefined}
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
  onItemClick,
}: {
  data: SeriesPoint[];
  nameKey?: string;
  valueKey: string;
  valueLabel?: string;
  onItemClick?: (name: string) => void;
}) {
  return (
    <ResponsiveContainer>
      <BarChart data={data} margin={{ top: 8, right: 8, left: 0, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" />
        <XAxis dataKey={nameKey} tick={{ fill: 'var(--text-muted)', fontSize: 12 }} />
        <YAxis tick={{ fill: 'var(--text-muted)', fontSize: 12 }} width={48} />
        <Tooltip
          contentStyle={tooltipContentStyle}
          labelStyle={tooltipLabelStyle}
          itemStyle={tooltipItemStyle}
        />
        <Bar
          dataKey={valueKey}
          name={valueLabel ?? valueKey}
          fill="var(--chart-2)"
          radius={[4, 4, 0, 0]}
          cursor={onItemClick ? 'pointer' : undefined}
          onClick={(entry) => {
            const name = String((entry as SeriesPoint)?.[nameKey] ?? '');
            if (name && onItemClick) onItemClick(name);
          }}
        />
      </BarChart>
    </ResponsiveContainer>
  );
}

export function NamedPieChart({
  data,
  nameKey = 'name',
  valueKey,
  onItemClick,
}: {
  data: SeriesPoint[];
  nameKey?: string;
  valueKey: string;
  onItemClick?: (name: string) => void;
}) {
  return (
    <ResponsiveContainer>
      <PieChart>
        <Pie
          data={data}
          dataKey={valueKey}
          nameKey={nameKey}
          innerRadius={48}
          outerRadius={80}
          paddingAngle={2}
          cursor={onItemClick ? 'pointer' : undefined}
          onClick={(_, index) => {
            const name = String(data[index]?.[nameKey] ?? '');
            if (name && onItemClick) onItemClick(name);
          }}
        >
          {data.map((_, index) => (
            <Cell key={String(index)} fill={COLORS[index % COLORS.length]} />
          ))}
        </Pie>
        <Tooltip
          contentStyle={tooltipContentStyle}
          labelStyle={tooltipLabelStyle}
          itemStyle={tooltipItemStyle}
        />
        <Legend />
      </PieChart>
    </ResponsiveContainer>
  );
}
