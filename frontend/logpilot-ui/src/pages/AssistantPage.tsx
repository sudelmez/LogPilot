import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  Alert, Box, Button, Card, IconButton, LinearProgress, List, ListItemButton, ListItemText, MenuItem, Skeleton, Stack, TextField, Typography,
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import { useChatMessages, useChatSessions, useCreateSession, useSendMessage } from '../api/chatHooks';
import AssistantAnswerCard from '../components/AssistantAnswerCard';
import RangeToggle from '../components/RangeToggle';
import { relativeTime } from '../lib/format';
import type { RangeKey } from '../lib/ranges';

// Örnekler, asistanın bildiği araştırma türlerine göre gruplu: boş ekran aynı zamanda ne sorulabileceğini öğretir.
const EXAMPLES: { group: string; tables: string; questions: string[] }[] = [
  { group: 'Mobile app & login', tables: 'logs', questions: ["Why can't TCLI log in to the mobile application?", 'OLDU neden giriş yapamıyor?'] },
  { group: 'Insurance quotes', tables: 'users → quote_logs → logs', questions: ['BOB mobil uygulamadan kasko teklifi alamıyor, neden?', 'Kasko tekliflerinde neden bazı sigorta şirketlerinin teklifi gelmiyor?'] },
  { group: 'Policy SMS', tables: 'quote_logs → users → sms_logs', questions: ["LENA'ya poliçe SMS'i ulaştı mı?", "Poliçe SMS'leri neden ulaşmıyor?"] },
  { group: 'Policy purchase', tables: 'proposal_step_logs → winsure → payment', questions: ['NINA kurumsal siteden poliçe satın alırken hata aldı, hata poliçe onayında mı ödemede mi?', 'Bugün poliçe satın almalarında en çok hangi aşamada hata var?'] },
  { group: 'System', tables: 'logs · incidents', questions: ['Which service has the highest error rate today?', 'What errors occurred in the last 15 minutes?'] },
];

export default function AssistantPage() {
  const [params, setParams] = useSearchParams();
  const sessionId = params.get('session');
  const asked = params.get('ask');

  const sessions = useChatSessions();
  const messages = useChatMessages(sessionId);
  const create = useCreateSession();
  const send = useSendMessage();

  const [text, setText] = useState('');
  const [range, setRange] = useState<RangeKey>('24h');
  const bottomRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);

  // Başka bir sayfadaki "Ask assistant" düğmesinden gelindi: soru yeni bir konuşmada hazır, gönderilmeden önce düzenlenebilir.
  useEffect(() => {
    if (!asked) return;
    setText(asked);
    setParams({}, { replace: true });
    setTimeout(() => inputRef.current?.focus(), 0);
  }, [asked]); // eslint-disable-line react-hooks/exhaustive-deps

  const pendingText = send.isPending && send.variables?.sessionId === sessionId ? send.variables.content : null;

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
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
  const busy = send.isPending || create.isPending;

  return (
    <Box sx={{
      display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '232px minmax(0, 1fr)' },
      height: { md: 'calc(100vh - 56px)' }, minHeight: { xs: 'calc(100vh - 104px)', md: 0 },
    }}>
      {/* Konuşmalar */}
      <Card sx={{ display: { xs: 'none', md: 'flex' }, flexDirection: 'column', minHeight: 0 }}>
        <Box sx={{ p: 1 }}>
          <Button fullWidth variant="outlined" startIcon={<AddIcon />} onClick={() => { setParams({}); setText(''); }}>
            New conversation
          </Button>
        </Box>
        <Typography variant="caption" color="text.secondary" sx={{ px: 2, pb: 0.5, fontWeight: 600 }}>Recent</Typography>
        <Box sx={{ overflow: 'auto', px: 1, pb: 1, flex: 1 }}>
          {sessions.isPending && <Skeleton variant="rounded" height={120} />}
          {sessions.isError && <Alert severity="error">{sessions.error.message}</Alert>}
          {sessions.data && sessions.data.length === 0 && (
            <Typography variant="caption" color="text.secondary" sx={{ px: 1, display: 'block' }}>Your questions will be listed here.</Typography>
          )}
          <List dense disablePadding>
            {sessions.data?.map((s) => (
              <ListItemButton key={s.id} selected={s.id === sessionId} onClick={() => setParams({ session: s.id })} sx={{ py: 0.5, mb: 0.25 }}>
                <ListItemText primary={s.title} secondary={relativeTime(s.updatedAt)}
                  slotProps={{ primary: { noWrap: true, sx: { fontSize: '0.8125rem', fontWeight: 550 } }, secondary: { sx: { fontSize: '0.6875rem' } } }} />
              </ListItemButton>
            ))}
          </List>
        </Box>
      </Card>

      {/* Konuşma */}
      <Card sx={{ display: 'flex', flexDirection: 'column', minHeight: 0 }}>
        <Stack direction="row" spacing={1} sx={{ px: 2, py: 1.25, borderBottom: 1, borderColor: 'divider', alignItems: 'center' }}>
          <Typography variant="subtitle1" component="h1" sx={{ flex: 1 }} noWrap>
            {sessionId ? sessions.data?.find((s) => s.id === sessionId)?.title ?? 'Assistant' : 'Assistant'}
          </Typography>
          {/* Telefonda konuşma listesi yok: önceki konuşmalara bu seçiciden dönülür. */}
          {sessions.data && sessions.data.length > 0 && (
            <TextField select size="small" value={sessionId ?? ''} label="History" sx={{ display: { md: 'none' }, width: 132 }}
              onChange={(e) => setParams(e.target.value ? { session: e.target.value } : {})}>
              <MenuItem value=""><em>New conversation</em></MenuItem>
              {sessions.data.map((s) => (
                <MenuItem key={s.id} value={s.id} sx={{ maxWidth: 300 }}>
                  <Typography variant="body2" noWrap>{s.title}</Typography>
                </MenuItem>
              ))}
            </TextField>
          )}
          <Button size="small" startIcon={<AddIcon />} onClick={() => { setParams({}); setText(''); }} sx={{ display: { md: 'none' } }}>New</Button>
        </Stack>

        <Box sx={{ flex: 1, overflow: 'auto', px: { xs: 1.5, sm: 3 }, py: 2.5 }}>
          <Box sx={{ maxWidth: 920, mx: 'auto' }}>
            {messages.isPending && sessionId && <Skeleton variant="rounded" height={160} />}
            {messages.isError && <Alert severity="error">{messages.error.message}</Alert>}

            {/* Konuşma seçili değilken mesaj sorgusu kapalıdır ("pending" görünür): örnekler yine gösterilmeli. */}
            {empty && !(sessionId && messages.isPending) && (
              <Box sx={{ py: { xs: 1, md: 3 } }}>
                <Typography variant="h5" component="h2">What do you want to find out?</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5, mb: 3, maxWidth: '64ch' }}>
                  Ask about a user, an error code, a quote or policy number, or a time window. The assistant only reads the logs,
                  shows the path it took through the tables and the evidence it used, and says when the data is not enough.
                </Typography>
                <Box sx={{ display: 'grid', gap: 2.5, gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' } }}>
                  {EXAMPLES.map((g) => (
                    <Box key={g.group}>
                      <Stack direction="row" spacing={1} sx={{ alignItems: 'baseline', mb: 0.5 }}>
                        <Typography variant="subtitle2">{g.group}</Typography>
                        <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'ui-monospace, Menlo, monospace' }}>{g.tables}</Typography>
                      </Stack>
                      {g.questions.map((q) => (
                        <Box key={q} component="button" type="button" onClick={() => void submit(q)} disabled={busy} sx={{
                          display: 'block', width: '100%', textAlign: 'left', font: 'inherit', fontSize: '0.8125rem', color: 'text.primary',
                          bgcolor: 'transparent', border: 1, borderColor: 'divider', borderRadius: 1.5, px: 1.25, py: 0.75, mb: 0.75,
                          cursor: 'pointer', transition: 'border-color 150ms, background-color 150ms',
                          '&:hover': { borderColor: 'primary.main', bgcolor: 'background.raised' },
                        }}>{q}</Box>
                      ))}
                    </Box>
                  ))}
                </Box>
              </Box>
            )}

            <Stack spacing={3}>
              {thread.map((m) =>
                m.role === 'User' ? (
                  <Box key={m.id} sx={{ alignSelf: 'flex-end', maxWidth: '78%', px: 1.75, py: 1, borderRadius: 2, bgcolor: 'background.raised', border: 1, borderColor: 'divider' }}>
                    <Typography variant="body2">{m.content}</Typography>
                  </Box>
                ) : (
                  <Box key={m.id}><AssistantAnswerCard message={m} /></Box>
                ),
              )}

              {pendingText && (
                <>
                  <Box sx={{ alignSelf: 'flex-end', maxWidth: '78%', px: 1.75, py: 1, borderRadius: 2, bgcolor: 'background.raised', border: 1, borderColor: 'divider' }}>
                    <Typography variant="body2">{pendingText}</Typography>
                  </Box>
                  <Box sx={{ maxWidth: 480 }}>
                    <Typography variant="body2" sx={{ fontWeight: 600 }}>Investigating the logs…</Typography>
                    <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 1 }}>
                      Picking the tables, following keys between them and checking the evidence. With the local model this takes 1–3 minutes.
                    </Typography>
                    <LinearProgress />
                  </Box>
                </>
              )}

              {send.isError && <Alert severity="error">{send.error.message}</Alert>}
              <div ref={bottomRef} />
            </Stack>
          </Box>
        </Box>

        {/* Soru kutusu */}
        <Box sx={{ borderTop: 1, borderColor: 'divider', px: { xs: 1.5, sm: 3 }, py: 1.5 }}>
          <Box sx={{ maxWidth: 920, mx: 'auto' }}>
            <Box sx={{
              border: 1, borderColor: 'divider', borderRadius: 2, bgcolor: 'background.paper', px: 1.5, pt: 1, pb: 0.75,
              transition: 'border-color 150ms', '&:focus-within': { borderColor: 'primary.main' },
            }}>
              <TextField fullWidth multiline maxRows={6} variant="standard" inputRef={inputRef}
                placeholder="Ask about a user, error code, quote, policy or time window…"
                value={text} onChange={(e) => setText(e.target.value)}
                slotProps={{ htmlInput: { maxLength: 2000, 'aria-label': 'Question' }, input: { disableUnderline: true, sx: { fontSize: '0.9375rem' } } }}
                onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void submit(text); } }} />
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 0.75 }}>
                <Typography variant="caption" color="text.secondary" sx={{ display: { xs: 'none', sm: 'block' }, whiteSpace: 'nowrap' }}>Look back</Typography>
                <RangeToggle value={range} onChange={setRange} />
                <Box sx={{ flex: 1 }} />
                <Typography variant="caption" color="text.disabled" noWrap sx={{ display: { xs: 'none', lg: 'block' } }}>Enter to send · Shift+Enter for a new line</Typography>
                <IconButton color="primary" disabled={!text.trim() || busy} onClick={() => void submit(text)} aria-label="Send"
                  sx={{ bgcolor: 'primary.main', color: 'primary.contrastText', width: 32, height: 32,
                    '&:hover': { bgcolor: 'primary.main', filter: 'brightness(1.08)' },
                    '&.Mui-disabled': { bgcolor: 'background.raised', color: 'text.disabled' } }}>
                  <ArrowUpwardIcon sx={{ fontSize: 18 }} />
                </IconButton>
              </Stack>
            </Box>
          </Box>
        </Box>
      </Card>
    </Box>
  );
}
