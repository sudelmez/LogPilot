import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Alert, Box, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Divider, Drawer, IconButton, MenuItem,
  Skeleton, Stack, Table, TableBody, TableCell, TableHead, TableRow, TextField, Typography,
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import AutoAwesome from '@mui/icons-material/AutoAwesome';
import { useIncident, useIncidentLogs, useRegenerateSummary, useUpdateIncidentStatus } from '../api/incidentHooks';
import type { IncidentStatus } from '../api/incidentTypes';
import { formatDateTime, relativeTime } from '../lib/format';
import { SeverityChip } from './Chips';
import { IncidentSeverityChip, IncidentStatusChip } from './IncidentChips';

const STATUSES: IncidentStatus[] = ['Open', 'Investigating', 'Resolved'];

export default function IncidentDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const incident = useIncident(id);
  const logs = useIncidentLogs(id);
  const setStatus = useUpdateIncidentStatus();
  const regenerate = useRegenerateSummary();
  const [resolveOpen, setResolveOpen] = useState(false);
  const data = incident.data;

  return (
    <Drawer anchor="right" open={id !== null} onClose={onClose} slotProps={{ paper: { sx: { width: { xs: '100%', sm: 640 } } } }}>
      <Box sx={{ p: 2.5 }}>
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
          <Typography variant="h6">Incident</Typography>
          <IconButton onClick={onClose} aria-label="Close"><CloseIcon /></IconButton>
        </Stack>

        {incident.isPending && <Skeleton variant="rounded" height={300} />}
        {incident.isError && <Alert severity="error">{incident.error.message}</Alert>}

        {data && (
          <Stack spacing={2.5}>
            <Box>
              <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>{data.title}</Typography>
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', mt: 1 }} useFlexGap>
                <IncidentSeverityChip severity={data.severity} />
                <IncidentStatusChip status={data.status} />
                <Chip size="small" variant="outlined" label={data.errorCode} />
              </Stack>
            </Box>

            <Stack direction="row" spacing={2} sx={{ alignItems: 'center' }}>
              <TextField select size="small" label="Status" value={data.status} sx={{ minWidth: 170 }}
                onChange={(e) => {
                  const next = e.target.value as IncidentStatus;
                  // Çözülürken ne düzelttiğini sor: not vaka hafızasına girer (boş bırakılabilir).
                  if (next === 'Resolved') setResolveOpen(true);
                  else setStatus.mutate({ id: data.id, status: next });
                }}>
                {STATUSES.map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
              </TextField>
              {data.status !== 'Resolved' && (
                <Typography variant="caption" color="text.secondary">
                  Last error {relativeTime(data.lastSeenAt)}. Incidents are never closed automatically: resolve it yourself once you have confirmed the fix.
                </Typography>
              )}
            </Stack>
            {setStatus.isError && <Alert severity="error">{setStatus.error.message}</Alert>}
            {data.status === 'Resolved' && data.resolutionNote && (
              <Alert severity="success" variant="outlined">
                <Typography variant="caption" sx={{ fontWeight: 700, display: 'block' }}>Resolution (saved to the knowledge base)</Typography>
                <Typography variant="body2">{data.resolutionNote}</Typography>
              </Alert>
            )}

            <ResolveDialog open={resolveOpen} pending={setStatus.isPending}
              error={setStatus.isError ? setStatus.error.message : undefined}
              onCancel={() => setResolveOpen(false)}
              onResolve={(resolutionNote, rootCause) =>
                setStatus.mutate({ id: data.id, status: 'Resolved', resolutionNote, rootCause },
                  { onSuccess: () => setResolveOpen(false) })} />

            <Box sx={{ display: 'grid', gap: 1.5, gridTemplateColumns: '1fr 1fr' }}>
              <div><Typography variant="overline" color="text.secondary">First seen</Typography><Typography variant="body2">{formatDateTime(data.firstSeenAt)}</Typography></div>
              <div><Typography variant="overline" color="text.secondary">Last seen</Typography><Typography variant="body2">{formatDateTime(data.lastSeenAt)}</Typography></div>
              <div><Typography variant="overline" color="text.secondary">Error logs</Typography><Typography variant="body2">{data.logCount.toLocaleString()}</Typography></div>
              <div><Typography variant="overline" color="text.secondary">Affected users</Typography><Typography variant="body2">{data.affectedUserCodes.length}</Typography></div>
            </Box>

            <Box>
              <Typography variant="overline" color="text.secondary">Affected services</Typography>
              <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
                {data.affectedServices.map((s) => <Chip key={s} size="small" label={s} />)}
              </Stack>
            </Box>

            <Box>
              <Typography variant="overline" color="text.secondary">Affected users</Typography>
              <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
                {data.affectedUserCodes.map((u) => (
                  <Chip key={u} size="small" variant="outlined" label={u} component={Link} to={`/logs?userCode=${u}&range=all`} clickable />
                ))}
              </Stack>
            </Box>

            <Alert icon={<AutoAwesome fontSize="small" />} severity="info" variant="outlined"
              action={<Button size="small" disabled={regenerate.isPending} onClick={() => regenerate.mutate(data.id)}>Regenerate</Button>}>
              <Typography variant="caption" sx={{ fontWeight: 700 }}>AI summary</Typography>
              <Typography variant="body2">{data.aiSummary ?? 'No summary has been generated yet.'}</Typography>
              <Typography variant="caption" color="text.secondary">
                Generated from log patterns only; it cannot confirm that an incident is resolved.
                {data.summaryGeneratedAt ? ` Updated ${relativeTime(data.summaryGeneratedAt)}.` : ''}
              </Typography>
            </Alert>
            {regenerate.isError && <Alert severity="error">{regenerate.error.message}</Alert>}

            <Divider />

            <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center' }}>
              <Typography variant="overline" color="text.secondary">Related logs (latest {data.relatedLogIds.length})</Typography>
              <Button size="small" component={Link} to={`/logs?errorCode=${data.errorCode}&range=all`}>Open in Logs Explorer</Button>
            </Stack>

            {logs.isPending && <Skeleton variant="rounded" height={140} />}
            {logs.isError && <Alert severity="error">{logs.error.message}</Alert>}
            {logs.data && (
              <Box sx={{ overflow: 'auto' }}>
                <Table size="small">
                  <TableHead><TableRow><TableCell>Time</TableCell><TableCell>Level</TableCell><TableCell>Service</TableCell><TableCell>User</TableCell><TableCell>Message</TableCell></TableRow></TableHead>
                  <TableBody>
                    {logs.data.slice().reverse().map((l) => (
                      <TableRow key={l.id} hover component={Link} to={`/logs?logId=${l.id}&range=all`} sx={{ textDecoration: 'none' }}>
                        <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatDateTime(l.timestamp)}</TableCell>
                        <TableCell><SeverityChip level={l.level} /></TableCell>
                        <TableCell>{l.service}</TableCell>
                        <TableCell>{l.userCode ?? '—'}</TableCell>
                        <TableCell sx={{ maxWidth: 220 }}><Typography variant="body2" noWrap title={l.message}>{l.message}</Typography></TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Box>
            )}
          </Stack>
        )}
      </Box>
    </Drawer>
  );
}

/** "Resolved" seçilince: ne düzeltti? Yazılan not doğrulanmış vaka olarak hafızaya girer; boş bırakılabilir. */
function ResolveDialog({ open, pending, error, onCancel, onResolve }: {
  open: boolean; pending: boolean; error?: string;
  onCancel: () => void; onResolve: (resolutionNote?: string, rootCause?: string) => void;
}) {
  const [note, setNote] = useState('');
  const [rootCause, setRootCause] = useState('');

  return (
    <Dialog open={open} onClose={onCancel} fullWidth maxWidth="sm">
      <DialogTitle>Resolve incident</DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          What fixed it? The note is saved to the knowledge base, and the assistant suggests it the next time this error appears.
        </Typography>
        <Stack spacing={2}>
          <TextField label="Resolution note" multiline minRows={2} fullWidth autoFocus value={note}
            onChange={(e) => setNote(e.target.value)} slotProps={{ htmlInput: { maxLength: 2000 } }}
            placeholder="e.g. Restarted the MongoDB primary and raised the connection pool limit." />
          <TextField label="Root cause (optional)" fullWidth value={rootCause}
            onChange={(e) => setRootCause(e.target.value)} slotProps={{ htmlInput: { maxLength: 2000 } }} />
        </Stack>
        {error && <Alert severity="error" sx={{ mt: 2 }}>{error}</Alert>}
      </DialogContent>
      <DialogActions>
        <Button onClick={onCancel}>Cancel</Button>
        <Button disabled={pending} onClick={() => onResolve(undefined, undefined)}>Resolve without note</Button>
        <Button variant="contained" disabled={pending || note.trim() === ''}
          onClick={() => onResolve(note.trim(), rootCause.trim() || undefined)}>
          Resolve and save
        </Button>
      </DialogActions>
    </Dialog>
  );
}
