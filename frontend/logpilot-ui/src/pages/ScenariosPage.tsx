import { useState } from 'react';
import {
  Alert, Box, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle,
  FormControlLabel, MenuItem, Slider, Snackbar, Stack, Switch, Table, TableBody, TableCell, TableContainer,
  TableHead, TablePagination, TableRow, TextField, Typography,
} from '@mui/material';
import PlayArrow from '@mui/icons-material/PlayArrow';
import TuneIcon from '@mui/icons-material/Tune';
import { useExecutions, useScenarios, useTriggerScenario, useUpdateScenarioSettings } from '../api/hooks';
import type { ScenarioSettings, ScenarioSummary } from '../api/types';
import { ExecutionChip } from '../components/Chips';
import { Code, StatusDot } from '../components/ui';
import PageHeader from '../components/PageHeader';
import QueryState from '../components/QueryState';
import { formatDateTime, formatDuration, formatNumber } from '../lib/format';

function describeSchedule(s: ScenarioSummary): string {
  if (s.kind === 'Normal') return `weight ${s.settings.weight}`;
  if (s.settings.intervalSeconds <= 0) return 'manual only';
  const minutes = s.settings.intervalSeconds / 60;
  const every = minutes >= 1 ? `${Number.isInteger(minutes) ? minutes : minutes.toFixed(1)} min` : `${s.settings.intervalSeconds} s`;
  return `every ${every} · ${Math.round(s.settings.probability * 100)}%`;
}

function ScenarioCard({ scenario, onTrigger, onEdit, busy }: {
  scenario: ScenarioSummary; onTrigger: () => void; onEdit: () => void; busy: boolean;
}) {
  return (
    <Card sx={{ display: 'flex', flexDirection: 'column' }}>
      <CardContent sx={{ flexGrow: 1 }}>
        <Stack direction="row" spacing={1} sx={{ justifyContent: 'space-between', alignItems: 'flex-start' }}>
          <Typography variant="subtitle1" component="h2">{scenario.name}</Typography>
          <StatusDot tone={scenario.settings.enabled ? 'success' : 'neutral'} label={scenario.settings.enabled ? 'Enabled' : 'Disabled'} />
        </Stack>
        <Typography variant="body2" color="text.secondary" sx={{ my: 1, minHeight: 60, display: '-webkit-box', WebkitLineClamp: 4, WebkitBoxOrient: 'vertical', overflow: 'hidden' }} title={scenario.description}>{scenario.description}</Typography>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
          <Code>{describeSchedule(scenario)}</Code>
          {/* Satın alma senaryoları adım değil, üç tabloya yazan bir akış üretir. */}
          <Code>{scenario.stepCount > 0 ? `${scenario.stepCount} steps` : 'multi-table flow'}</Code>
        </Stack>
      </CardContent>
      <Stack direction="row" spacing={1} sx={{ p: 2, pt: 0 }}>
        <Button size="small" variant="contained" startIcon={<PlayArrow />} disabled={busy} onClick={onTrigger}>Trigger</Button>
        <Button size="small" startIcon={<TuneIcon />} onClick={onEdit}>Settings</Button>
      </Stack>
    </Card>
  );
}

function SettingsDialog({ scenario, onClose, onSaved }: {
  scenario: ScenarioSummary; onClose: () => void; onSaved: (message: string) => void;
}) {
  const [settings, setSettings] = useState<ScenarioSettings>(scenario.settings);
  const mutation = useUpdateScenarioSettings();
  const isIncident = scenario.kind === 'Incident';

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="xs">
      <DialogTitle>{scenario.name} — settings</DialogTitle>
      <DialogContent>
        <Stack spacing={2.5} sx={{ pt: 1 }}>
          <FormControlLabel label="Enabled" control={
            <Switch checked={settings.enabled} onChange={(e) => setSettings({ ...settings, enabled: e.target.checked })} />} />
          {isIncident ? (
            <>
              <TextField label="Interval (seconds, 0 = manual only)" type="number" size="small" value={settings.intervalSeconds}
                onChange={(e) => setSettings({ ...settings, intervalSeconds: Number(e.target.value) })}
                helperText="Average time between scheduled runs" />
              <Box>
                <Typography variant="caption" color="text.secondary">
                  Probability per interval: {Math.round(settings.probability * 100)}%
                </Typography>
                <Slider min={0} max={1} step={0.05} value={settings.probability}
                  onChange={(_, v) => setSettings({ ...settings, probability: v as number })} />
              </Box>
            </>
          ) : (
            <TextField label="Weight" type="number" size="small" value={settings.weight}
              onChange={(e) => setSettings({ ...settings, weight: Number(e.target.value) })}
              helperText="Higher weight = picked more often in background traffic" />
          )}
          {mutation.isError && <Alert severity="error">{mutation.error.message}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={mutation.isPending}
          onClick={() => mutation.mutate({ id: scenario.id, settings }, {
            onSuccess: () => { onSaved(`Settings saved for ${scenario.name}.`); onClose(); },
          })}>
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}

export default function ScenariosPage() {
  const scenarios = useScenarios();
  const trigger = useTriggerScenario();
  const [editing, setEditing] = useState<ScenarioSummary | null>(null);
  const [toast, setToast] = useState<{ severity: 'success' | 'error' | 'info'; text: string } | null>(null);

  const [filter, setFilter] = useState('');
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(10);
  const executions = useExecutions(filter, page + 1, pageSize);

  const runTrigger = (s: ScenarioSummary) =>
    trigger.mutate(s.id, {
      onSuccess: () => setToast({ severity: 'success', text: `${s.name} queued. The generator will pick it up in a moment.` }),
      onError: (e) => setToast({ severity: e instanceof Error && e.message.includes('already') ? 'info' : 'error', text: e.message }),
    });

  return (
    <>
      <PageHeader title="Scenarios" subtitle="Synthetic incidents and background traffic produced by the log generator" />

      <QueryState query={scenarios} isEmpty={(d) => d.length === 0} height={220}
        emptyText="No scenarios yet. Start the LogGenerator (or run scripts/seed-data.sh) to load the catalog.">
        {(items) => {
          const incidents = items.filter((s) => s.kind === 'Incident');
          const normal = items.filter((s) => s.kind === 'Normal');
          const grid = { display: 'grid', gap: 2, gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))' };
          const render = (s: ScenarioSummary) => (
            <ScenarioCard key={s.id} scenario={s} busy={trigger.isPending && trigger.variables === s.id}
              onTrigger={() => runTrigger(s)} onEdit={() => setEditing(s)} />
          );
          return (
            <>
              <Typography variant="h6" sx={{ mb: 1.5 }}>Incident scenarios</Typography>
              <Box sx={{ ...grid, mb: 3 }}>{incidents.map(render)}</Box>
              <Typography variant="h6" sx={{ mb: 1.5 }}>Background traffic</Typography>
              <Box sx={{ ...grid, mb: 3 }}>{normal.map(render)}</Box>
            </>
          );
        }}
      </QueryState>

      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1.5 }}>
        <Typography variant="h6">Execution history</Typography>
        <TextField select size="small" label="Scenario" value={filter} sx={{ minWidth: 220 }}
          onChange={(e) => { setFilter(e.target.value); setPage(0); }}>
          <MenuItem value="">All scenarios</MenuItem>
          {(scenarios.data ?? []).map((s) => <MenuItem key={s.id} value={s.id}>{s.name}</MenuItem>)}
        </TextField>
      </Stack>

      <Card>
        <QueryState query={executions} height={200} isEmpty={(d) => d.items.length === 0}
          emptyText="No executions yet. Trigger a scenario to see it here.">
          {(data) => (
            <>
              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell>Requested</TableCell><TableCell>Scenario</TableCell><TableCell>Trigger</TableCell>
                      <TableCell>Status</TableCell><TableCell align="right">Logs</TableCell>
                      <TableCell align="right">Duration</TableCell><TableCell>Details</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {data.items.map((e) => (
                      <TableRow key={e.id} hover>
                        <TableCell sx={{ whiteSpace: 'nowrap' }}>{formatDateTime(e.requestedAt)}</TableCell>
                        <TableCell>{e.scenarioId}</TableCell>
                        <TableCell>{e.triggeredBy}</TableCell>
                        <TableCell><ExecutionChip status={e.status} /></TableCell>
                        <TableCell align="right">{formatNumber(e.generatedLogCount)}</TableCell>
                        <TableCell align="right">
                          {e.startedAt && e.finishedAt
                            ? formatDuration(new Date(e.finishedAt).getTime() - new Date(e.startedAt).getTime())
                            : '—'}
                        </TableCell>
                        <TableCell sx={{ maxWidth: 260 }}>
                          <Typography variant="body2" noWrap color={e.error ? 'error' : 'text.secondary'}
                            title={e.error ?? e.correlationIds.join(', ')}>
                            {e.error ?? `${e.correlationIds.length} flow(s)${e.seed !== undefined ? ` · seed ${e.seed}` : ''}`}
                          </Typography>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
              <TablePagination component="div" count={data.totalCount} page={page} rowsPerPage={pageSize}
                rowsPerPageOptions={[10, 25, 50]} onPageChange={(_, p) => setPage(p)}
                onRowsPerPageChange={(e) => { setPageSize(Number(e.target.value)); setPage(0); }} />
            </>
          )}
        </QueryState>
      </Card>

      {editing && (
        <SettingsDialog scenario={editing} onClose={() => setEditing(null)}
          onSaved={(text) => setToast({ severity: 'success', text })} />
      )}

      <Snackbar open={toast !== null} autoHideDuration={4000} onClose={() => setToast(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
        {toast ? <Alert severity={toast.severity} onClose={() => setToast(null)}>{toast.text}</Alert> : undefined}
      </Snackbar>
    </>
  );
}
