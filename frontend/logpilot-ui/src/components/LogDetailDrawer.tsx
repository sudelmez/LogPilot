import { Alert, Box, Button, Divider, Drawer, IconButton, Skeleton, Stack, Typography } from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import { useLog } from '../api/hooks';
import { formatDateTime } from '../lib/format';
import { SeverityChip } from './Chips';
import { AskButton, Code } from './ui';
import { askAboutLog } from '../lib/ask';
import { MONO } from '../theme';

interface Props {
  logId: string | null;
  onClose: () => void;
  onFilter: (key: 'correlationId' | 'transactionId' | 'userCode', value: string) => void;
}

export default function LogDetailDrawer({ logId, onClose, onFilter }: Props) {
  const query = useLog(logId);
  const log = query.data;

  return (
    <Drawer anchor="right" open={logId !== null} onClose={onClose} slotProps={{ paper: { sx: { width: { xs: '100%', sm: 520 } } } }}>
      <Box sx={{ p: 2.5 }}>
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
          <Typography variant="h6">Log details</Typography>
          <IconButton onClick={onClose} aria-label="Close"><CloseIcon /></IconButton>
        </Stack>

        {query.isPending && <Skeleton variant="rounded" height={260} />}
        {query.isError && <Alert severity="error">{query.error.message}</Alert>}

        {log && (
          <Stack spacing={2}>
            <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
              <SeverityChip level={log.level} />
              {log.errorCode && <Code tone={log.level === 'Warning' ? 'warning' : 'error'}>{log.errorCode}</Code>}
              <Typography variant="body2" color="text.secondary" sx={{ fontFamily: MONO, fontSize: 12 }}>{formatDateTime(log.timestamp)}</Typography>
              <Box sx={{ flex: 1 }} />
              {log.level !== 'Information' && log.level !== 'Debug' && <AskButton question={askAboutLog(log)} />}
            </Stack>
            <Typography variant="caption" color="text.secondary">{log.application} · {log.service}{log.userCode ? ` · ${log.userCode}` : ''}</Typography>

            <Typography variant="body1">{log.message}</Typography>

            <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
              {log.correlationId && (
                <Button size="small" variant="outlined" onClick={() => onFilter('correlationId', log.correlationId!)}>
                  Same correlation ID
                </Button>
              )}
              {log.transactionId && (
                <Button size="small" variant="outlined" onClick={() => onFilter('transactionId', log.transactionId!)}>
                  Same transaction
                </Button>
              )}
              {log.userCode && (
                <Button size="small" variant="outlined" onClick={() => onFilter('userCode', log.userCode!)}>
                  Same user
                </Button>
              )}
            </Stack>

            <Divider />
            <Typography variant="subtitle2" color="text.secondary">Raw document</Typography>
            <Box
              component="pre"
              sx={{
                m: 0, p: 1.5, borderRadius: 2, bgcolor: 'background.raised', border: 1, borderColor: 'divider', fontSize: 12, lineHeight: 1.55,
                overflow: 'auto', maxHeight: '55vh', whiteSpace: 'pre-wrap', wordBreak: 'break-word',
              }}
            >
              {JSON.stringify(log, null, 2)}
            </Box>
          </Stack>
        )}
      </Box>
    </Drawer>
  );
}
