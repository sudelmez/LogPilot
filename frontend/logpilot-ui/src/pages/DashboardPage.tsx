import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Box, Card, Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography,
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
import { AskButton, Code, Panel } from '../components/ui';
import { formatDateTime, formatDuration, formatNumber, formatPercent, formatTime, relativeTime } from '../lib/format';
import type { RangeKey } from '../lib/ranges';
import { MONO } from '../theme';
import { useChartColors } from '../lib/chartColors';
import { askAboutCode as askCode, askAboutLog as askLog } from '../lib/ask';


export default function DashboardPage() {
  const [range, setRange] = useState<RangeKey>('24h');
  const C = useChartColors();
  const axisTick = { fontSize: 11, fill: C.text, fontFamily: MONO };
  const tooltipStyle = {
    contentStyle: { background: C.raised, border: `1px solid ${C.grid}`, borderRadius: 8, fontSize: 12, color: C.textPrimary },
    labelStyle: { color: C.textPrimary, fontWeight: 600 },
    cursor: { fill: C.raised, stroke: C.grid },
  };

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
      <PageHeader title="Dashboard" subtitle="What is failing right now, across application logs. Refreshes every 15 seconds."
        actions={<RangeToggle value={range} onChange={setRange} />} />

      {summary.isError && (
        <Typography color="error" variant="body2" sx={{ mb: 2 }}>Could not load the summary: {summary.error.message}</Typography>
      )}

      {/* Durum şeridi: altı sayı tek yüzeyde, ince çizgilerle ayrılmış. */}
      <Card sx={{ mb: 2 }}>
        {/* 1px boşluk + çizgi rengi zemin: her sütun sayısında hücreler arasında ince çizgi. */}
        <Box sx={{
          display: 'grid', gap: '1px', bgcolor: 'divider',
          gridTemplateColumns: { xs: 'repeat(2, 1fr)', sm: 'repeat(3, 1fr)', lg: 'repeat(6, 1fr)' },
          '& > *': { bgcolor: 'background.paper' },
        }}>
          <StatCard title="Logs" value={formatNumber(s?.totalLogs)} loading={summary.isPending} />
          <StatCard title="Errors" value={formatNumber(s?.errorCount)} tone={s && s.errorCount > 0 ? 'error' : 'default'} loading={summary.isPending} />
          <StatCard title="Warnings" value={formatNumber(s?.warningCount)} tone={s && s.warningCount > 0 ? 'warning' : 'default'} loading={summary.isPending} />
          <StatCard title="Log error ratio" value={formatPercent(s?.logErrorRatio)} loading={summary.isPending} caption="error logs ÷ all logs" />
          <StatCard title="Request failure rate" value={formatPercent(s?.requests.failureRate)} loading={summary.isPending}
            tone={s && s.requests.failureRate >= 0.1 ? 'error' : 'default'}
            caption={s ? `${formatNumber(s.requests.failed)} of ${formatNumber(s.requests.completed)} requests` : undefined} />
          <ActiveIncidentsCard />
        </Box>
      </Card>

      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', lg: '2fr 1fr' }, mb: 2 }}>
        <Panel title="Error trend" caption="errors and warnings per interval">
          <QueryState query={trend} height={240} isEmpty={(d) => d.every((p) => p.total === 0)}
            emptyText="No errors or warnings in this period. Pick a longer range, or trigger a scenario to generate some.">
            {(points) => (
              <ResponsiveContainer width="100%" height={240}>
                <AreaChart data={points.map((p) => ({ ...p, label: trendLabel(p) }))} margin={{ top: 8, right: 8, left: -8, bottom: 0 }}>
                  <CartesianGrid stroke={C.grid} strokeDasharray="2 4" vertical={false} />
                  <XAxis dataKey="label" minTickGap={40} tick={axisTick} axisLine={{ stroke: C.grid }} tickLine={false} />
                  <YAxis allowDecimals={false} tick={axisTick} width={36} axisLine={false} tickLine={false} />
                  <Tooltip {...tooltipStyle} />
                  <Area isAnimationActive={false} type="monotone" dataKey="warnings" name="Warnings" stroke={C.warning} strokeWidth={1.5} fill={C.warning} fillOpacity={0.12} />
                  <Area isAnimationActive={false} type="monotone" dataKey="errors" name="Errors" stroke={C.error} strokeWidth={1.75} fill={C.error} fillOpacity={0.18} />
                </AreaChart>
              </ResponsiveContainer>
            )}
          </QueryState>
        </Panel>

        <Panel title="Errors by service" caption="error + critical logs">
          <QueryState query={byService} height={240} isEmpty={(d) => d.length === 0} emptyText="No service logged an error in this period.">
            {(items) => (
              <ResponsiveContainer width="100%" height={Math.max(180, items.length * 34)}>
                <BarChart data={items} layout="vertical" margin={{ left: 0, right: 12 }}>
                  <CartesianGrid stroke={C.grid} strokeDasharray="2 4" horizontal={false} />
                  <XAxis type="number" allowDecimals={false} tick={axisTick} axisLine={false} tickLine={false} />
                  <YAxis type="category" dataKey="service" width={150} tick={{ ...axisTick, fontFamily: 'inherit', fontSize: 12 }} axisLine={false} tickLine={false} />
                  <Tooltip {...tooltipStyle} />
                  <Bar isAnimationActive={false} dataKey="errors" name="Errors" fill={C.error} fillOpacity={0.85} radius={[0, 3, 3, 0]} barSize={14} />
                </BarChart>
              </ResponsiveContainer>
            )}
          </QueryState>
        </Panel>
      </Box>

      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, mb: 2 }}>
        <Panel title="Most frequent errors" caption="click a code to see its logs" padding={0}>
          <QueryState query={topCodes} isEmpty={(d) => d.length === 0} emptyText="No error codes in this period.">
            {(items) => (
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell sx={{ pl: 2 }}>Error code</TableCell>
                    <TableCell align="right">Logs</TableCell>
                    <TableCell align="right">Users</TableCell>
                    <TableCell sx={{ width: '22%', display: { xs: 'none', sm: 'table-cell' } }} aria-label="Share" />
                    <TableCell align="right" sx={{ pr: 1.5 }} />
                  </TableRow>
                </TableHead>
                <TableBody>
                  {items.map((item) => (
                    <TableRow key={item.errorCode} hover>
                      <TableCell sx={{ pl: 2 }}>
                        <Code tone="error" to={`/logs?errorCode=${item.errorCode}`} title={`Open logs with ${item.errorCode}`}>{item.errorCode}</Code>
                      </TableCell>
                      <TableCell align="right">{formatNumber(item.count)}</TableCell>
                      <TableCell align="right">{formatNumber(item.distinctUsers)}</TableCell>
                      <TableCell sx={{ display: { xs: 'none', sm: 'table-cell' } }}>
                        <Box sx={{ height: 4, borderRadius: 2, bgcolor: 'background.raised', overflow: 'hidden' }}>
                          <Box sx={{ height: '100%', width: `${(item.count / items[0].count) * 100}%`, bgcolor: 'error.main', opacity: 0.7 }} />
                        </Box>
                      </TableCell>
                      <TableCell align="right" sx={{ pr: 1 }}><AskButton compact question={askCode(item.errorCode)} /></TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </QueryState>
        </Panel>

        <Panel title="Recent errors" caption="latest error and critical logs" padding={0}>
          <QueryState query={recent} isEmpty={(d) => d.length === 0} emptyText="No error logs recently.">
            {(items) => (
              <Box component="ul" sx={{ listStyle: 'none', m: 0, p: 0 }}>
                {items.map((log) => (
                  <Box component="li" key={log.id} sx={{
                    display: 'flex', alignItems: 'center', gap: 1, px: 2, py: 0.75, borderTop: 1, borderColor: 'divider',
                    '&:hover': { bgcolor: 'background.raised' },
                  }}>
                    <Box component={Link} to={`/logs?logId=${log.id}`} sx={{ flex: 1, minWidth: 0, textDecoration: 'none', color: 'inherit' }}>
                      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', minWidth: 0 }}>
                        <Code tone="error">{log.errorCode ?? log.level}</Code>
                        <Typography variant="caption" color="text.secondary" noWrap>
                          {log.service}{log.userCode ? ` · ${log.userCode}` : ''} · <span title={formatDateTime(log.timestamp)}>{relativeTime(log.timestamp)}</span>
                        </Typography>
                      </Stack>
                      <Typography variant="body2" noWrap title={log.message} sx={{ mt: 0.25 }}>{log.message}</Typography>
                    </Box>
                    <AskButton compact question={askLog(log)} />
                  </Box>
                ))}
              </Box>
            )}
          </QueryState>
        </Panel>
      </Box>

      <Panel title="Service health" caption="from the log error ratio; services with very few logs show as Unknown" padding={0}>
        <QueryState query={health} isEmpty={(d) => d.length === 0}>
          {(items) => (
            <Box sx={{ overflowX: 'auto' }}>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell sx={{ pl: 2 }}>Service</TableCell>
                    <TableCell>Status</TableCell>
                    <TableCell align="right">Error ratio</TableCell>
                    <TableCell align="right">Logs</TableCell>
                    <TableCell align="right">Avg duration</TableCell>
                    <TableCell align="right" sx={{ pr: 2 }}>Last error</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {[...items].sort((a, b) => b.logErrorRatio - a.logErrorRatio).map((h) => (
                    <TableRow key={h.service} hover>
                      <TableCell sx={{ pl: 2 }}>
                        <Box component={Link} to={`/logs?service=${h.service}`} sx={{ color: 'text.primary', textDecoration: 'none', fontWeight: 600, '&:hover': { textDecoration: 'underline' } }}>
                          {h.service}
                        </Box>
                      </TableCell>
                      <TableCell><HealthChip status={h.status} /></TableCell>
                      <TableCell align="right">{formatPercent(h.logErrorRatio)}</TableCell>
                      <TableCell align="right">{formatNumber(h.totalLogs)}</TableCell>
                      <TableCell align="right">{formatDuration(h.avgDurationMs)}</TableCell>
                      <TableCell align="right" sx={{ pr: 2, color: 'text.secondary' }}>{relativeTime(h.lastErrorAt)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          )}
        </QueryState>
      </Panel>
    </>
  );
}
