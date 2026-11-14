import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Box, Button, Card, CardContent, MenuItem, Stack, Table, TableBody, TableCell, TableContainer,
  TableHead, TablePagination, TableRow, TableSortLabel, TextField, Typography,
} from '@mui/material';
import { useLogs } from '../api/hooks';
import type { LogFilters } from '../api/hooks';
import { SeverityChip } from '../components/Chips';
import LogDetailDrawer from '../components/LogDetailDrawer';
import PageHeader from '../components/PageHeader';
import QueryState from '../components/QueryState';
import { formatDateTime } from '../lib/format';
import { AskButton, Code } from '../components/ui';
import { MONO } from '../theme';
import { askAboutLog } from '../lib/ask';
import { RANGES } from '../lib/ranges';
import type { RangeKey } from '../lib/ranges';

const LEVELS = ['', 'Information', 'Warning', 'Error', 'Critical'];
const TEXT_FILTERS = ['application', 'service', 'userCode', 'errorCode', 'correlationId', 'transactionId'] as const;
type TextFilter = (typeof TEXT_FILTERS)[number];

/** Filtreler URL'de yaşar: sayfa yenilense veya başka sayfadan link verilse (ör. AI kanıt kartı) aynı görünüm açılır. */
function readFilters(params: URLSearchParams): LogFilters {
  const range = params.get('range') ?? '24h';
  return {
    level: params.get('level') ?? '',
    application: params.get('application') ?? '',
    service: params.get('service') ?? '',
    userCode: params.get('userCode') ?? '',
    errorCode: params.get('errorCode') ?? '',
    correlationId: params.get('correlationId') ?? '',
    transactionId: params.get('transactionId') ?? '',
    range: range === 'all' || RANGES.some((r) => r.key === range) ? (range as RangeKey | 'all') : '24h',
    page: Math.max(1, Number(params.get('page') ?? 1) || 1),
    pageSize: [25, 50, 100].includes(Number(params.get('pageSize'))) ? Number(params.get('pageSize')) : 50,
    ascending: params.get('asc') === '1',
  };
}

export default function LogsPage() {
  const [params, setParams] = useSearchParams();
  const applied = readFilters(params);
  const logId = params.get('logId');

  const [draft, setDraft] = useState(applied);
  const paramString = params.toString();
  useEffect(() => { setDraft(readFilters(params)); }, [paramString]); // eslint-disable-line react-hooks/exhaustive-deps

  const query = useLogs(applied);

  const update = (changes: Record<string, string | null>) => {
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '') next.delete(key);
      else next.set(key, value);
    }
    setParams(next);
  };

  const apply = () => {
    const changes: Record<string, string | null> = { level: draft.level, range: draft.range, page: null };
    for (const key of TEXT_FILTERS) changes[key] = draft[key].trim();
    update(changes);
  };

  const reset = () => setParams(new URLSearchParams());

  return (
    <>
      <PageHeader title="Logs" subtitle="Application logs. Filter, open a row for the raw document, or follow its correlation id. Filters live in the URL, so a view can be shared." />

      <Card sx={{ mb: 2 }}>
        <CardContent sx={{ p: 1.5, '&:last-child': { pb: 1.5 } }}>
          <Box component="form" onSubmit={(e) => { e.preventDefault(); apply(); }}
            sx={{ display: 'grid', gap: 1.25, gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))' }}>
            <TextField select size="small" label="Level" value={draft.level}
              onChange={(e) => setDraft({ ...draft, level: e.target.value })}>
              {LEVELS.map((l) => <MenuItem key={l} value={l}>{l || 'All levels'}</MenuItem>)}
            </TextField>
            <TextField select size="small" label="Time range" value={draft.range}
              onChange={(e) => setDraft({ ...draft, range: e.target.value as RangeKey | 'all' })}>
              {RANGES.map((r) => <MenuItem key={r.key} value={r.key}>Last {r.label}</MenuItem>)}
              <MenuItem value="all">All time</MenuItem>
            </TextField>
            {TEXT_FILTERS.map((key: TextFilter) => (
              <TextField key={key} size="small" label={key} value={draft[key]}
                slotProps={{ htmlInput: { style: { fontFamily: MONO, fontSize: 13 } } }}
                onChange={(e) => setDraft({ ...draft, [key]: e.target.value })} />
            ))}
            <Stack direction="row" spacing={1}>
              <Button type="submit" variant="contained">Apply</Button>
              <Button onClick={reset}>Reset</Button>
            </Stack>
          </Box>
        </CardContent>
      </Card>

      <Card>
        <QueryState query={query} height={320} isEmpty={(d) => d.items.length === 0} emptyText="No logs match these filters.">
          {(data) => (
            <>
              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell sortDirection={applied.ascending ? 'asc' : 'desc'}>
                        <TableSortLabel active direction={applied.ascending ? 'asc' : 'desc'}
                          onClick={() => update({ asc: applied.ascending ? null : '1', page: null })}>
                          Timestamp
                        </TableSortLabel>
                      </TableCell>
                      <TableCell>Level</TableCell>
                      <TableCell sx={{ display: { xs: 'none', xl: 'table-cell' } }}>Application</TableCell>
                      <TableCell>Service</TableCell>
                      <TableCell>User</TableCell>
                      <TableCell>Error code</TableCell>
                      <TableCell>Message</TableCell>
                      <TableCell aria-label="Actions" />
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {data.items.map((log) => (
                      <TableRow key={log.id} hover selected={log.id === logId} sx={{ cursor: 'pointer' }}
                        onClick={() => update({ logId: log.id })}>
                        <TableCell sx={{ whiteSpace: 'nowrap', fontFamily: MONO, fontSize: 12, color: 'text.secondary' }}>{formatDateTime(log.timestamp)}</TableCell>
                        <TableCell><SeverityChip level={log.level} /></TableCell>
                        <TableCell sx={{ display: { xs: 'none', xl: 'table-cell' } }}>{log.application}</TableCell>
                        <TableCell>{log.service}</TableCell>
                        <TableCell sx={{ fontFamily: MONO, fontSize: 12 }}>{log.userCode ?? '—'}</TableCell>
                        <TableCell>{log.errorCode ? <Code tone={log.level === 'Warning' ? 'warning' : 'error'}>{log.errorCode}</Code> : '—'}</TableCell>
                        {/* Mesaj kalan genişliği alır ve kırpılır: tablo ekrana sığar, eylem sütunu görünür kalır. */}
                        <TableCell sx={{ width: '100%', maxWidth: 0, minWidth: 160 }}>
                          <Typography variant="body2" noWrap title={log.message}>{log.message}</Typography>
                        </TableCell>
                        <TableCell align="right" sx={{ py: 0, pr: 1 }}>
                          {(log.level === 'Error' || log.level === 'Critical') && <AskButton compact question={askAboutLog(log)} />}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
              <TablePagination component="div" count={data.totalCount} page={applied.page - 1}
                rowsPerPage={applied.pageSize} rowsPerPageOptions={[25, 50, 100]}
                onPageChange={(_, page) => update({ page: String(page + 1) })}
                onRowsPerPageChange={(e) => update({ pageSize: e.target.value, page: null })} />
            </>
          )}
        </QueryState>
      </Card>

      <LogDetailDrawer logId={logId} onClose={() => update({ logId: null })}
        onFilter={(key, value) => update({ [key]: value, logId: null, page: null, range: 'all' })} />
    </>
  );
}
