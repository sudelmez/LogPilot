import { useState } from 'react';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import {
  Box, Card, CardContent, Chip, LinearProgress, Stack, Typography, useTheme,
} from '@mui/material';
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts';
import {
  useErrorsByService, useRecentErrors, useServiceHealth, useSummary, useTopErrorCodes, useTrend,
} from '../api/hooks';
import type { TrendPoint } from '../api/types';
import ActiveIncidentsCard from '../components/ActiveIncidentsCard';
import { HealthChip } from '../components/Chips';
import PageHeader from '../components/PageHeader';
import QueryState from '../components/QueryState';
import RangeToggle from '../components/RangeToggle';
import StatCard from '../components/StatCard';
import { formatDateTime, formatDuration, formatNumber, formatPercent, formatTime, relativeTime } from '../lib/format';
import type { RangeKey } from '../lib/ranges';

function Panel({ title, caption, children }: { title: string; caption?: string; children: ReactNode }) {
  return (
    <Card>
      <CardContent>
        <Typography variant="h6">{title}</Typography>
        {caption && <Typography variant="caption" color="text.secondary">{caption}</Typography>}
        <Box sx={{ mt: 2 }}>{children}</Box>
      </CardContent>
    </Card>
  );
}

export default function DashboardPage() {
  const theme = useTheme();
  const [range, setRange] = useState<RangeKey>('24h');

  const summary = useSummary(range);
  const trend = useTrend(range);
  const byService = useErrorsByService(range);
  const topCodes = useTopErrorCodes(range);
  const health = useServiceHealth(range);
  const recent = useRecentErrors(8);

  const s = summary.data;
  const trendLabel = (p: TrendPoint) =>
    range === '7d' ? new Date(p.bucketStart).toLocaleDateString(undefined, { month: 'short', day: '2-digit', hour: '2-digit' })
                   : formatTime(p.bucketStart);

  return (
    <>
      <PageHeader title="Dashboard" subtitle="Live overview of application logs (auto-refreshes every 15 s)"
        actions={<RangeToggle value={range} onChange={setRange} />} />

      {summary.isError && (
        <Typography color="error" sx={{ mb: 2 }}>Could not load the summary: {summary.error.message}</Typography>
      )}

      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', mb: 2 }}>
        <StatCard title="Total logs" value={formatNumber(s?.totalLogs)} loading={summary.isPending} />
        <StatCard title="Errors" value={formatNumber(s?.errorCount)} tone="error" loading={summary.isPending} />
        <StatCard title="Warnings" value={formatNumber(s?.warningCount)} tone="warning" loading={summary.isPending} />
        <StatCard title="Log error ratio" value={formatPercent(s?.logErrorRatio)} loading={summary.isPending}
          caption="Error logs ÷ all logs" />
        <StatCard title="Request failure rate" value={formatPercent(s?.requests.failureRate)} loading={summary.isPending}
          tone={s && s.requests.failureRate >= 0.1 ? 'error' : 'default'}
          caption={s ? `${formatNumber(s.requests.failed)} of ${formatNumber(s.requests.completed)} requests` : undefined} />
        <ActiveIncidentsCard />
      </Box>

      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', lg: '2fr 1fr' }, mb: 2 }}>
        <Panel title="Error trend" caption="Errors and warnings over time">
          <QueryState query={trend} height={260} isEmpty={(d) => d.every((p) => p.total === 0)}>
            {(points) => (
              <ResponsiveContainer width="100%" height={260}>
                <AreaChart data={points.map((p) => ({ ...p, label: trendLabel(p) }))}>
                  <CartesianGrid strokeDasharray="3 3" vertical={false} />
                  <XAxis dataKey="label" minTickGap={32} tick={{ fontSize: 12 }} />
                  <YAxis allowDecimals={false} tick={{ fontSize: 12 }} width={32} />
                  <Tooltip />
                  <Area type="monotone" dataKey="warnings" stroke={theme.palette.warning.main}
                    fill={theme.palette.warning.light} fillOpacity={0.35} />
                  <Area type="monotone" dataKey="errors" stroke={theme.palette.error.main}
                    fill={theme.palette.error.light} fillOpacity={0.5} />
                </AreaChart>
              </ResponsiveContainer>
            )}
          </QueryState>
        </Panel>

        <Panel title="Errors by service" caption="Error + critical logs per service">
          <QueryState query={byService} height={260} isEmpty={(d) => d.length === 0} emptyText="No errors in this period.">
            {(items) => (
              <ResponsiveContainer width="100%" height={Math.max(180, items.length * 44)}>
                <BarChart data={items} layout="vertical" margin={{ left: 8 }}>
                  <CartesianGrid strokeDasharray="3 3" horizontal={false} />
                  <XAxis type="number" allowDecimals={false} tick={{ fontSize: 12 }} />
                  <YAxis type="category" dataKey="service" width={130} tick={{ fontSize: 12 }} />
                  <Tooltip />
                  <Bar dataKey="errors" fill={theme.palette.error.main} radius={[0, 4, 4, 0]} />
                </BarChart>
              </ResponsiveContainer>
            )}
          </QueryState>
        </Panel>
      </Box>

      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, mb: 2 }}>
        <Panel title="Most frequent errors">
          <QueryState query={topCodes} isEmpty={(d) => d.length === 0} emptyText="No error codes in this period.">
            {(items) => (
              <Stack spacing={1.5}>
                {items.map((item) => (
                  <Box key={item.errorCode} component={Link} to={`/logs?errorCode=${item.errorCode}`}
                    sx={{ textDecoration: 'none', color: 'inherit' }}>
                    <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center' }}>
                      <Typography variant="body2" sx={{ fontWeight: 600 }}>{item.errorCode}</Typography>
                      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                        <Chip size="small" variant="outlined" label={`${item.distinctUsers} user${item.distinctUsers === 1 ? '' : 's'}`} />
                        <Typography variant="body2">{formatNumber(item.count)}</Typography>
                      </Stack>
                    </Stack>
                    <LinearProgress variant="determinate" value={(item.count / items[0].count) * 100}
                      sx={{ mt: 0.5, height: 6, borderRadius: 3 }} />
                  </Box>
                ))}
              </Stack>
            )}
          </QueryState>
        </Panel>

        <Panel title="Recent critical errors" caption="Latest error and critical logs">
          <QueryState query={recent} isEmpty={(d) => d.length === 0} emptyText="No recent errors. 🎉">
            {(items) => (
              <Stack divider={<Box sx={{ borderBottom: 1, borderColor: 'divider' }} />} spacing={1}>
                {items.map((log) => (
                  <Box key={log.id} component={Link} to={`/logs?logId=${log.id}`}
                    sx={{ textDecoration: 'none', color: 'inherit', pb: 1 }}>
                    <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                      <Chip size="small" color="error" variant="outlined" label={log.errorCode ?? log.level} />
                      <Typography variant="caption" color="text.secondary">
                        {log.service} · {relativeTime(log.timestamp)}
                      </Typography>
                    </Stack>
                    <Typography variant="body2" noWrap title={`${formatDateTime(log.timestamp)} — ${log.message}`}>
                      {log.message}
                    </Typography>
                  </Box>
                ))}
              </Stack>
            )}
          </QueryState>
        </Panel>
      </Box>

      <Panel title="Service health" caption="Based on the log error ratio; a service with very few logs is reported as Unknown">
        <QueryState query={health} isEmpty={(d) => d.length === 0}>
          {(items) => (
            <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))' }}>
              {items.map((h) => (
                <Card key={h.service}>
                  <CardContent>
                    <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
                      <Typography variant="subtitle2" noWrap sx={{ fontWeight: 700 }}>{h.service}</Typography>
                      <HealthChip status={h.status} />
                    </Stack>
                    <Typography variant="body2" color="text.secondary">
                      {formatPercent(h.logErrorRatio)} error ratio · {formatNumber(h.totalLogs)} logs
                    </Typography>
                    <Typography variant="body2" color="text.secondary">
                      Avg {formatDuration(h.avgDurationMs)} · last error {relativeTime(h.lastErrorAt)}
                    </Typography>
                  </CardContent>
                </Card>
              ))}
            </Box>
          )}
        </QueryState>
      </Panel>
    </>
  );
}
