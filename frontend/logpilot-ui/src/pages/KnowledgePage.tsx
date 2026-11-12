import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Alert, Box, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle,
  IconButton, InputAdornment, Pagination, Skeleton, Stack, TextField, Tooltip, Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DeleteOutline from '@mui/icons-material/DeleteOutlined';
import EditOutlined from '@mui/icons-material/EditOutlined';
import SearchIcon from '@mui/icons-material/Search';
import { useDeleteKnowledgeCase, useKnowledge, useKnowledgeCase, useSaveKnowledgeCase } from '../api/knowledgeHooks';
import type { KnowledgeCase, KnowledgeCaseInput, KnowledgeSource } from '../api/knowledgeTypes';
import EmptyState from '../components/EmptyState';
import PageHeader from '../components/PageHeader';
import { formatDateTime, relativeTime } from '../lib/format';

const SOURCE_LABEL: Record<KnowledgeSource, string> = {
  ChatFeedback: 'From assistant feedback',
  IncidentResolution: 'From incident resolution',
  Manual: 'Added manually',
};

const PAGE_SIZE = 10;

export default function KnowledgePage() {
  const [params, setParams] = useSearchParams();
  const highlightId = params.get('id');
  const [q, setQ] = useState('');
  const [errorCode, setErrorCode] = useState('');
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<KnowledgeCase | 'new' | null>(null);
  const [deleting, setDeleting] = useState<KnowledgeCase | null>(null);

  const list = useKnowledge({ q: q.trim(), errorCode: errorCode.trim(), page, pageSize: PAGE_SIZE });
  const highlighted = useKnowledgeCase(highlightId);
  const remove = useDeleteKnowledgeCase();

  useEffect(() => setPage(1), [q, errorCode]);

  const items = list.data?.items ?? [];
  const pinned = highlighted.data && !items.some((c) => c.id === highlighted.data.id) ? [highlighted.data] : [];

  return (
    <>
      <PageHeader title="Knowledge base"
        subtitle="Past problems and what fixed them, confirmed by people. The assistant suggests these when the same error appears again."
        actions={<Button variant="contained" startIcon={<AddIcon />} sx={{ flexShrink: 0, whiteSpace: "nowrap" }} onClick={() => setEditing("new")}>Add case</Button>} />

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ mb: 2 }}>
        <TextField size="small" placeholder="Search title, fix, root cause…" value={q} onChange={(e) => setQ(e.target.value)}
          sx={{ flexGrow: 1 }} slotProps={{ htmlInput: { maxLength: 100 }, input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }} />
        <TextField size="small" label="Error code" placeholder="e.g. TOKEN_EXPIRED" value={errorCode}
          onChange={(e) => setErrorCode(e.target.value.toUpperCase())} sx={{ minWidth: 220 }} />
      </Stack>

      {list.isError && <Alert severity="error" sx={{ mb: 2 }}>{list.error.message}</Alert>}
      {remove.isError && <Alert severity="error" sx={{ mb: 2 }}>{remove.error.message}</Alert>}
      {list.isPending && <Stack spacing={1.5}>{[0, 1, 2].map((i) => <Skeleton key={i} variant="rounded" height={110} />)}</Stack>}

      {list.data && items.length === 0 && pinned.length === 0 && (
        <Card><CardContent>
          <EmptyState text={q || errorCode
            ? 'No cases match this search.'
            : 'No cases yet. Mark an assistant answer helpful and write what fixed it, or add a resolution note when resolving an incident.'} />
        </CardContent></Card>
      )}

      <Stack spacing={1.5}>
        {[...pinned, ...items].map((c) => (
          <CaseCard key={c.id} item={c} highlighted={c.id === highlightId}
            onEdit={() => setEditing(c)} onDelete={() => setDeleting(c)} />
        ))}
      </Stack>

      {list.data && list.data.totalPages > 1 && (
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 2 }}>
          <Pagination count={list.data.totalPages} page={page} onChange={(_e, p) => setPage(p)} />
        </Box>
      )}

      {editing && (
        <CaseDialog item={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />
      )}

      <Dialog open={deleting !== null} onClose={() => setDeleting(null)}>
        <DialogTitle>Delete this case?</DialogTitle>
        <DialogContent>
          <DialogContentText>
            “{deleting?.title}” will be removed from the knowledge base permanently and will no longer be suggested. This cannot be undone.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDeleting(null)}>Cancel</Button>
          <Button color="error" variant="contained" disabled={remove.isPending}
            onClick={() => deleting && remove.mutate(deleting.id, {
              onSuccess: () => {
                if (deleting.id === highlightId) setParams({});
                setDeleting(null);
              },
            })}>
            Delete
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

function CaseCard({ item, highlighted, onEdit, onDelete }: {
  item: KnowledgeCase; highlighted: boolean; onEdit: () => void; onDelete: () => void;
}) {
  return (
    <Card variant="outlined" sx={highlighted ? { borderColor: 'primary.main', borderWidth: 2 } : undefined}>
      <CardContent>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-start', justifyContent: 'space-between' }}>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>{item.title}</Typography>
            <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mt: 0.5 }}>
              {item.errorCodes.map((c) => <Chip key={c} size="small" color="error" variant="outlined" label={c} />)}
              {item.services.map((s) => <Chip key={s} size="small" label={s} />)}
            </Stack>
          </Box>
          <Stack direction="row">
            <Tooltip title="Edit"><IconButton size="small" aria-label="Edit" onClick={onEdit}><EditOutlined fontSize="small" /></IconButton></Tooltip>
            <Tooltip title="Delete"><IconButton size="small" aria-label="Delete" onClick={onDelete}><DeleteOutline fontSize="small" /></IconButton></Tooltip>
          </Stack>
        </Stack>

        <Typography variant="body2" sx={{ mt: 1.5 }}><b>Fixed by:</b> {item.resolution}</Typography>
        {item.rootCause && <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}><b>Root cause:</b> {item.rootCause}</Typography>}
        {item.symptom && <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}><b>Symptom:</b> {item.symptom}</Typography>}

        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
          {SOURCE_LABEL[item.source]} · updated {formatDateTime(item.updatedAt)}
          {item.timesSuggested > 0
            ? ` · suggested ${item.timesSuggested}× (last ${relativeTime(item.lastSuggestedAt ?? item.updatedAt)})`
            : ' · not suggested yet'}
        </Typography>
      </CardContent>
    </Card>
  );
}

const splitList = (text: string) =>
  text.split(/[,\s]+/).map((s) => s.trim()).filter((s) => s.length > 0);

function CaseDialog({ item, onClose }: { item: KnowledgeCase | null; onClose: () => void }) {
  const save = useSaveKnowledgeCase();
  const [title, setTitle] = useState(item?.title ?? '');
  const [resolution, setResolution] = useState(item?.resolution ?? '');
  const [rootCause, setRootCause] = useState(item?.rootCause ?? '');
  const [symptom, setSymptom] = useState(item?.symptom ?? '');
  const [codes, setCodes] = useState(item?.errorCodes.join(', ') ?? '');
  const [services, setServices] = useState(item?.services.join(', ') ?? '');

  const submit = () => {
    const data: KnowledgeCaseInput = {
      title: title.trim(), resolution: resolution.trim(),
      rootCause: rootCause.trim() || undefined, symptom: symptom.trim() || undefined,
      errorCodes: splitList(codes.toUpperCase()), services: splitList(services),
    };
    save.mutate({ id: item?.id, data }, { onSuccess: onClose });
  };

  return (
    <Dialog open onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{item ? 'Edit case' : 'Add case'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label="Title" required fullWidth value={title} onChange={(e) => setTitle(e.target.value)}
            slotProps={{ htmlInput: { maxLength: 200 } }} placeholder="e.g. Mobile login fails because the access token expired" />
          <TextField label="Error codes" fullWidth value={codes} onChange={(e) => setCodes(e.target.value)}
            helperText="Comma-separated. The assistant matches past cases by error code." placeholder="TOKEN_EXPIRED, LOGIN_FAILED" />
          <TextField label="Services" fullWidth value={services} onChange={(e) => setServices(e.target.value)}
            placeholder="AuthenticationService, MobileGateway" />
          <TextField label="Resolution (what fixed it)" required multiline minRows={2} fullWidth value={resolution}
            onChange={(e) => setResolution(e.target.value)} slotProps={{ htmlInput: { maxLength: 2000 } }} />
          <TextField label="Root cause" multiline fullWidth value={rootCause}
            onChange={(e) => setRootCause(e.target.value)} slotProps={{ htmlInput: { maxLength: 2000 } }} />
          <TextField label="Symptom" multiline fullWidth value={symptom}
            onChange={(e) => setSymptom(e.target.value)} slotProps={{ htmlInput: { maxLength: 2000 } }} />
        </Stack>
        {save.isError && <Alert severity="error" sx={{ mt: 2 }}>{save.error.message}</Alert>}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={save.isPending || title.trim() === '' || resolution.trim() === ''} onClick={submit}>
          Save
        </Button>
      </DialogActions>
    </Dialog>
  );
}
