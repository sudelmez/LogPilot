import { useState } from 'react';
import { Link } from 'react-router-dom';
import {
  Alert, Button, Chip, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, Stack, TextField, Tooltip, Typography,
} from '@mui/material';
import ThumbUpOutlined from '@mui/icons-material/ThumbUpOutlined';
import ThumbDownOutlined from '@mui/icons-material/ThumbDownOutlined';
import { useSubmitFeedback } from '../api/chatHooks';
import type { ChatMessage } from '../api/chatTypes';

/**
 * 👍/👎 geri bildirimi. 👍 ile açılan pencerede "gerçek çözüm" yazılırsa cevap doğrulanmış vaka olarak
 * hafızaya girer ve gelecekteki benzer sorularda önerilir. Çözüm yazılmazsa yalnızca geri bildirim kaydedilir.
 */
export default function AnswerFeedback({ message }: { message: ChatMessage }) {
  const submit = useSubmitFeedback();
  const [open, setOpen] = useState(false);
  const [resolution, setResolution] = useState('');
  const [rootCause, setRootCause] = useState(message.answer?.probableCause ?? '');
  const feedback = message.feedback;

  const send = (helpful: boolean, withResolution: boolean) =>
    submit.mutate(
      {
        messageId: message.id,
        sessionId: message.sessionId,
        helpful,
        resolution: withResolution ? resolution.trim() || undefined : undefined,
        rootCause: withResolution ? rootCause.trim() || undefined : undefined,
      },
      { onSuccess: () => setOpen(false) },
    );

  return (
    <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
      {feedback ? (
        <>
          <Chip size="small" variant="outlined" color={feedback.helpful ? 'success' : 'default'}
            icon={feedback.helpful ? <ThumbUpOutlined /> : <ThumbDownOutlined />}
            label={feedback.helpful ? 'Marked helpful' : 'Marked not helpful'} />
          {feedback.knowledgeCaseId ? (
            <Chip size="small" color="primary" variant="outlined" label="Saved to knowledge base"
              component={Link} to={`/knowledge?id=${feedback.knowledgeCaseId}`} clickable />
          ) : feedback.helpful && (
            <Button size="small" onClick={() => setOpen(true)}>Add what fixed it</Button>
          )}
        </>
      ) : (
        <>
          <Typography variant="caption" color="text.secondary">Was this helpful?</Typography>
          <Tooltip title="Yes, and optionally save what fixed it to the knowledge base">
            <IconButton size="small" aria-label="Helpful" onClick={() => setOpen(true)}><ThumbUpOutlined fontSize="small" /></IconButton>
          </Tooltip>
          <Tooltip title="Not helpful">
            <IconButton size="small" aria-label="Not helpful" disabled={submit.isPending} onClick={() => send(false, false)}>
              <ThumbDownOutlined fontSize="small" />
            </IconButton>
          </Tooltip>
        </>
      )}
      {submit.isError && !open && <Alert severity="error" sx={{ py: 0 }}>{submit.error.message}</Alert>}

      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>What actually fixed it?</DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            If you write the real fix, this case is saved to the knowledge base and suggested the next time the same error
            appears. Only cases you confirm are remembered. Leave it empty to just mark the answer helpful.
          </Typography>
          <Stack spacing={2}>
            <TextField label="Resolution (what fixed it)" multiline minRows={2} fullWidth autoFocus
              value={resolution} onChange={(e) => setResolution(e.target.value)} slotProps={{ htmlInput: { maxLength: 2000 } }}
              placeholder="e.g. Increased the token lifetime in AuthenticationService and asked the user to sign in again." />
            <TextField label="Root cause (optional)" multiline minRows={1} fullWidth
              value={rootCause} onChange={(e) => setRootCause(e.target.value)} slotProps={{ htmlInput: { maxLength: 2000 } }} />
          </Stack>
          {submit.isError && <Alert severity="error" sx={{ mt: 2 }}>{submit.error.message}</Alert>}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>Cancel</Button>
          <Button disabled={submit.isPending} onClick={() => send(true, false)}>Just mark helpful</Button>
          <Button variant="contained" disabled={submit.isPending || resolution.trim() === ''} onClick={() => send(true, true)}>
            Save to knowledge base
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  );
}
