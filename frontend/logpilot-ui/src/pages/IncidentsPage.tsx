import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Box, Card, Chip, MenuItem, Stack, Table, TableBody, TableCell, TableContainer, TableHead, TablePagination,
  TableRow, TextField, Typography,
} from '@mui/material';
import { useIncidents } from '../api/incidentHooks';
import { IncidentSeverityChip, IncidentStatusChip } from '../components/IncidentChips';
import IncidentDrawer from '../components/IncidentDrawer';
import PageHeader from '../components/PageHeader';
import QueryState from '../components/QueryState';
import { formatDateTime, relativeTime } from '../lib/format';

export default function IncidentsPage() {
  const [params, setParams] = useSearchParams();
  const selected = params.get('incident');

  const [status, setStatus] = useState('active');
  const [severity, setSeverity] = useState('');
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(20);
  const query = useIncidents({ status, severity, page: page + 1, pageSize });

  const open = (id: string | null) => {
    const next = new URLSearchParams(params);
    if (id) next.set('incident', id); else next.delete('incident');
    setParams(next);
  };

  return (
    <>
      <PageHeader title="Incidents" subtitle="Groups of related errors, detected by rules (auto-refreshes every 15 s)"
        actions={
          <Stack direction="row" spacing={1.5}>
            <TextField select size="small" label="Status" value={status} sx={{ minWidth: 160 }}
              onChange={(e) => { setStatus(e.target.value); setPage(0); }}>
              <MenuItem value="active">Active (unresolved)</MenuItem>
              <MenuItem value="">All</MenuItem>
              <MenuItem value="Open">Open</MenuItem>
              <MenuItem value="Investigating">Investigating</MenuItem>
              <MenuItem value="Resolved">Resolved</MenuItem>
            </TextField>
            <TextField select size="small" label="Severity" value={severity} sx={{ minWidth: 140 }}
              onChange={(e) => { setSeverity(e.target.value); setPage(0); }}>
              <MenuItem value="">All</MenuItem>
              {['Critical', 'High', 'Medium', 'Low'].map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
            </TextField>
          </Stack>
        } />

      <Card>
        <QueryState query={query} height={260} isEmpty={(d) => d.items.length === 0}
          emptyText="No incidents match. Incidents appear when an error code repeats (3+ logs) within the last 15 minutes. Trigger a scenario to see one.">
          {(data) => (
            <>
              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Incident</TableCell><TableCell>Severity</TableCell><TableCell>Status</TableCell>
                      <TableCell>Affected services</TableCell><TableCell align="right">Users</TableCell>
                      <TableCell>Started</TableCell><TableCell>Last seen</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {data.items.map((i) => (
                      <TableRow key={i.id} hover selected={i.id === selected} sx={{ cursor: 'pointer' }} onClick={() => open(i.id)}>
                        <TableCell sx={{ maxWidth: 360 }}>
                          <Typography variant="body2" sx={{ fontWeight: 600 }} noWrap title={i.title}>{i.title}</Typography>
                          {i.aiSummary && <Typography variant="caption" color="text.secondary" noWrap title={i.aiSummary} sx={{ display: 'block' }}>{i.aiSummary}</Typography>}
                        </TableCell>
                        <TableCell><IncidentSeverityChip severity={i.severity} /></TableCell>
                        <TableCell><IncidentStatusChip status={i.status} /></TableCell>
                        <TableCell>
                          <Box sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap' }}>
                            {i.affectedServices.slice(0, 3).map((s) => <Chip key={s} size="small" label={s} />)}
                            {i.affectedServices.length > 3 && <Chip size="small" variant="outlined" label={`+${i.affectedServices.length - 3}`} />}
                          </Box>
                        </TableCell>
                        <TableCell align="right">{i.affectedUserCodes.length}</TableCell>
                        <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatDateTime(i.firstSeenAt)}</TableCell>
                        <TableCell sx={{ whiteSpace: 'nowrap' }}>{relativeTime(i.lastSeenAt)}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
              <TablePagination component="div" count={data.totalCount} page={page} rowsPerPage={pageSize}
                rowsPerPageOptions={[10, 20, 50]} onPageChange={(_, p) => setPage(p)}
                onRowsPerPageChange={(e) => { setPageSize(Number(e.target.value)); setPage(0); }} />
            </>
          )}
        </QueryState>
      </Card>

      <IncidentDrawer id={selected} onClose={() => open(null)} />
    </>
  );
}
