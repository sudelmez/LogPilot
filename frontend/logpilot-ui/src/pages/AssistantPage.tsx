import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Alert, Box, Button, Card, Chip, CircularProgress, IconButton, LinearProgress, List, ListItemButton, ListItemText,
  Paper, Skeleton, Stack, TextField, Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import SendIcon from '@mui/icons-material/Send';
import { useChatMessages, useChatSessions, useCreateSession, useSendMessage } from '../api/chatHooks';
import AssistantAnswerCard from '../components/AssistantAnswerCard';
import PageHeader from '../components/PageHeader';
import RangeToggle from '../components/RangeToggle';
import { relativeTime } from '../lib/format';
import type { RangeKey } from '../lib/ranges';

const EXAMPLES = [
  "Why can't TCLI log in to the mobile application?",
  'Which service has the highest error rate today?',
  'Are multiple users affected by the same error?',
  'What errors occurred in the last 15 minutes?',
  'Is there an active MongoDB-related incident?',
  'Why are proposal creation requests failing?',
  'BOB mobil uygulamadan kasko teklifi alamıyor, neden?',
  'Kasko tekliflerinde neden bazı sigorta şirketlerinin teklifi gelmiyor?',
  'ELIF neden teklif alamadı?',
  "LENA'ya poliçe SMS'i ulaştı mı?",
];

export default function AssistantPage() {
  const [params, setParams] = useSearchParams();
  const sessionId = params.get('session');

  const sessions = useChatSessions();
  const messages = useChatMessages(sessionId);
  const create = useCreateSession();
  const send = useSendMessage();

  const [text, setText] = useState('');
  const [range, setRange] = useState<RangeKey>('24h');
  const bottomRef = useRef<HTMLDivElement>(null);

  const pendingText = send.isPending && send.variables?.sessionId === sessionId ? send.variables.content : null;

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages.data?.length, pendingText]);

  const submit = async (content: string) => {
    const trimmed = content.trim();
    if (!trimmed || send.isPending || create.isPending) return;

    let id = sessionId;
    if (!id) {
      const session = await create.mutateAsync();
      id = session.id;
      setParams({ session: id });
    }
    setText('');
    send.mutate({ sessionId: id, content: trimmed, range });
  };

  const thread = messages.data ?? [];
  const empty = thread.length === 0 && !pendingText;

  return (
    <>
      <PageHeader title="AI Assistant" subtitle="Ask about incidents in plain language. Answers are built from real log evidence." />

      <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '260px 1fr' }, alignItems: 'start' }}>
        <Card sx={{ p: 1 }}>
          <Button fullWidth variant="contained" startIcon={<AddIcon />} onClick={() => setParams({})} sx={{ mb: 1 }}>
            New conversation
          </Button>
          {sessions.isPending && <Skeleton variant="rounded" height={120} />}
          {sessions.isError && <Alert severity="error">{sessions.error.message}</Alert>}
          {sessions.data && sessions.data.length === 0 && (
            <Typography variant="caption" color="text.secondary" sx={{ p: 1, display: 'block' }}>No conversations yet.</Typography>
          )}
          <List dense disablePadding sx={{ maxHeight: { md: '60vh' }, overflow: 'auto' }}>
            {sessions.data?.map((s) => (
              <ListItemButton key={s.id} selected={s.id === sessionId} onClick={() => setParams({ session: s.id })} sx={{ borderRadius: 2 }}>
                <ListItemText primary={s.title} secondary={relativeTime(s.updatedAt)}
                  slotProps={{ primary: { noWrap: true, sx: { fontWeight: 600 } } }} />
              </ListItemButton>
            ))}
          </List>
        </Card>

        <Card sx={{ display: 'flex', flexDirection: 'column', minHeight: '60vh' }}>
          <Box sx={{ flexGrow: 1, p: 2, overflow: 'auto' }}>
            {messages.isPending && sessionId && <Skeleton variant="rounded" height={160} />}
            {messages.isError && <Alert severity="error">{messages.error.message}</Alert>}

            {empty && !messages.isPending && (
              <Stack spacing={2} sx={{ alignItems: 'center', py: 5, textAlign: 'center' }}>
                <Typography variant="h6">What would you like to investigate?</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ maxWidth: 480 }}>
                  The assistant queries the logs with controlled tools, shows its evidence, and tells you when the data is not enough.
                </Typography>
                <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', justifyContent: 'center' }}>
                  {EXAMPLES.map((q) => <Chip key={q} label={q} onClick={() => void submit(q)} variant="outlined" />)}
                </Stack>
              </Stack>
            )}

            <Stack spacing={2}>
              {thread.map((m) =>
                m.role === 'User' ? (
                  <Paper key={m.id} sx={{ alignSelf: 'flex-end', maxWidth: '80%', px: 2, py: 1, bgcolor: 'primary.main', color: 'primary.contrastText' }}>
                    <Typography variant="body2">{m.content}</Typography>
                  </Paper>
                ) : (
                  <Paper key={m.id} variant="outlined" sx={{ p: 2, maxWidth: '95%' }}>
                    <AssistantAnswerCard message={m} />
                  </Paper>
                ),
              )}

              {pendingText && (
                <>
                  <Paper sx={{ alignSelf: 'flex-end', maxWidth: '80%', px: 2, py: 1, bgcolor: 'primary.main', color: 'primary.contrastText' }}>
                    <Typography variant="body2">{pendingText}</Typography>
                  </Paper>
                  <Paper variant="outlined" sx={{ p: 2, maxWidth: 420 }}>
                    <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center' }}>
                      <CircularProgress size={18} />
                      <Typography variant="body2">Investigating the logs with search and analysis tools…</Typography>
                    </Stack>
                    <LinearProgress sx={{ mt: 1.5, borderRadius: 2 }} />
                  </Paper>
                </>
              )}

              {send.isError && <Alert severity="error">{send.error.message}</Alert>}
              <div ref={bottomRef} />
            </Stack>
          </Box>

          <Box sx={{ p: 2, borderTop: 1, borderColor: 'divider' }}>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' }, mb: 1 }}>
              <Typography variant="caption" color="text.secondary">Time range for the investigation:</Typography>
              <RangeToggle value={range} onChange={setRange} />
            </Stack>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-end' }}>
              <TextField fullWidth multiline maxRows={4} size="small" placeholder="Ask about a user, service, error code or time range…"
                value={text} onChange={(e) => setText(e.target.value)} slotProps={{ htmlInput: { maxLength: 2000 } }}
                onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void submit(text); } }} />
              <IconButton color="primary" disabled={!text.trim() || send.isPending} onClick={() => void submit(text)} aria-label="Send">
                <SendIcon />
              </IconButton>
            </Stack>
          </Box>
        </Card>
      </Box>
    </>
  );
}
