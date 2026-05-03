import { BarChart, Bar, XAxis, YAxis, Tooltip, Cell, ResponsiveContainer, LabelList } from 'recharts'
import type { SplitDelta } from '@/lib/chartUtils'
import { formatElapsed } from '@/lib/chartUtils'
import { useTheme } from '@/context/ThemeContext'

interface Props {
  deltas: SplitDelta[]
}

export function SplitChart({ deltas }: Props) {
  useTheme() // re-render when theme changes so CSS vars are re-read

  const style = getComputedStyle(document.documentElement)
  const tickColor = style.getPropertyValue('--color-secondary').trim()
  const tooltipBg = style.getPropertyValue('--color-surface').trim()
  const labelColor = style.getPropertyValue('--color-muted').trim()

  if (deltas.length === 0) {
    return (
      <p data-testid="split-chart-empty" className="text-xs text-muted py-8 text-center">
        No split data for this event
      </p>
    )
  }

  const data = deltas.map(d => ({
    label: d.label,
    secs: d.secs,
    faster: d.faster,
    deltaLabel: d.delta === 0 ? '—' : `${d.delta > 0 ? '+' : ''}${formatElapsed(Math.abs(d.delta))}`,
  }))

  return (
    <div data-testid="split-chart">
      <ResponsiveContainer width="100%" height={160}>
        <BarChart data={data} margin={{ top: 16, right: 8, bottom: 4, left: 40 }}>
          <XAxis dataKey="label" tick={{ fontSize: 10, fill: tickColor }} tickLine={false} />
          <YAxis
            tickFormatter={formatElapsed}
            tick={{ fontSize: 10, fill: tickColor }}
            tickLine={false}
            axisLine={false}
            domain={['auto', 'auto']}
          />
          <Tooltip
            formatter={(value) => [formatElapsed(typeof value === 'number' ? value : 0), 'Split time']}
            contentStyle={{ background: tooltipBg, border: 'none', fontSize: 12 }}
          />
          <Bar dataKey="secs" radius={[3, 3, 0, 0]}>
            {data.map((entry, index) => (
              <Cell key={`cell-${index}`} fill={entry.faster ? '#4ade80' : '#f87171'} />
            ))}
            <LabelList
              dataKey="deltaLabel"
              position="top"
              style={{ fontSize: 10, fill: labelColor }}
            />
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}
