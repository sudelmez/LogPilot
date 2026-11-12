import { Link } from 'react-router-dom';
import {
  Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, Card, CardContent, Chip, LinearProgress,
  Stack, Tooltip, Typography,
} from '@mui/material';
import ExpandMore from '@mui/icons-material/ExpandMore';
import HistoryIcon from '@mui/icons-material/History';
import type { ChatMessage, ConfidenceLevel } from '../api/chatTypes';
import { formatDateTime } from '../lib/format';
import AnswerFeedback from './AnswerFeedback';

const confidenceColor: Record<ConfidenceLevel, 'default' | 'success' | 'info' | 'warning' | 'error'> = {
  NotApplicable: 'default', Insufficient: 'error', Low: 'warning', Medium: 'info', High: 'success',
};

// LinearProgress 'default' rengini desteklemez.
function progressColor(level: ConfidenceLevel) {
  const color = confidenceColor[level];
  return color === 'default' ? 'primary' : color;
}

function logsLink(ids: string[], correlationId?: string): string {
  // range=all: kanıt eski olsa bile Logs Explorer'da görünsün
  return correlationId ? `/logs?correlationId=${correlationId}&range=all` : `/logs?logId=${ids[0]}&range=all`;
}

export default function AssistantAnswerCard({ message }: { message: ChatMessage }) {
  const answer = message.answer;
  if (!answer) return <Typography>{message.content}</Typography>;

  // Görev alanı dışı soru: kanıt/güven bölümleri anlamsız, sadece kibar ret gösterilir.
  if (answer.outOfScope) {
    return <Alert severity={answer.outOfScope === 'OffTopic' ? 'info' : 'warning'} variant="outlined">{answer.summary}</Alert>;
  }

  const showConfidence = answer.confidence !== 'NotApplicable';

  return (
    <Stack spacing={2}>
      <Typography variant="body1" sx={{ fontWeight: 600 }}>{answer.summary}</Typography>

      {answer.clarifyingQuestion && <Alert severity="info" variant="outlined">{answer.clarifyingQuestion}</Alert>}

      {answer.probableCause && (
        <Box>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 0.5 }}>
            <Typography variant="overline" color="text.secondary">Probable cause</Typography>
            {showConfidence && (
              <Tooltip arrow title={
                <Box>
                  <Typography variant="caption" sx={{ fontWeight: 700 }}>
                    Confidence is computed from the evidence ({answer.confidenceScore}/100)
                  </Typography>
                  {answer.confidenceFactors.map((f) => <Typography key={f} variant="caption" sx={{ display: 'block' }}>{f}</Typography>)}
                </Box>}>
                <Chip size="small" color={confidenceColor[answer.confidence]} label={`${answer.confidence} confidence`} />
              </Tooltip>
            )}
          </Stack>
          <Typography variant="body2">{answer.probableCause}</Typography>
          {showConfidence && (
            <LinearProgress variant="determinate" value={answer.confidenceScore} color={progressColor(answer.confidence)}
              sx={{ mt: 1, height: 5, borderRadius: 3, maxWidth: 260 }} />
          )}
        </Box>
      )}

      {!answer.probableCause && showConfidence && answer.confidence === 'Insufficient' && (
        <Chip size="small" color="error" variant="outlined" label="Insufficient evidence" sx={{ alignSelf: 'flex-start' }} />
      )}

      {answer.evidence.length > 0 && (
        <Box>
          <Typography variant="overline" color="text.secondary">Evidence</Typography>
          <Box sx={{ display: 'grid', gap: 1, gridTemplateColumns: 'repeat(auto-fill, minmax(230px, 1fr))', mt: 0.5 }}>
            {answer.evidence.map((e, i) => (
              <Card key={`${e.errorCode}-${e.service}-${i}`}>
                <CardContent sx={{ p: 1.5, '&:last-child': { pb: 1.5 } }}>
                  <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                    <Chip size="small" color="error" variant="outlined" label={e.errorCode} />
                    <Typography variant="caption" color="text.secondary">{e.count}×</Typography>
                  </Stack>
                  <Typography variant="body2" sx={{ mt: 0.5 }}>{e.service}</Typography>
                  <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                    {formatDateTime(e.firstSeen)}{e.lastSeen && e.lastSeen !== e.firstSeen ? ` → ${formatDateTime(e.lastSeen)}` : ''}
                  </Typography>
                  {e.userCodes.length > 0 && (
                    <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>Users: {e.userCodes.join(', ')}</Typography>
                  )}
                  <Button size="small" component={Link} to={logsLink(e.logIds, e.correlationIds[0])} sx={{ mt: 0.5, px: 0 }}>
                    Open related logs
                  </Button>
                </CardContent>
              </Card>
            ))}
          </Box>
        </Box>
      )}

      {answer.affectedServices.length > 0 && (
        <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography variant="overline" color="text.secondary">Affected services</Typography>
          {answer.affectedServices.map((s) => <Chip key={s} size="small" label={s} />)}
        </Stack>
      )}

      {answer.suggestedActions.length > 0 && (
        <Box>
          <Typography variant="overline" color="text.secondary">Suggested troubleshooting steps</Typography>
          <Box component="ol" sx={{ m: 0, pl: 3 }}>
            {answer.suggestedActions.map((a) => <Typography key={a} component="li" variant="body2">{a}</Typography>)}
          </Box>
        </Box>
      )}

      {answer.relatedCases.length > 0 && (
        <Box>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            <HistoryIcon fontSize="small" color="primary" />
            <Typography variant="overline" color="text.secondary">Related past cases</Typography>
          </Stack>
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1 }}>
            Previously verified by people for the same error. Hints only: not evidence for this answer.
          </Typography>
          <Stack spacing={1}>
            {answer.relatedCases.map((c) => (
              <Card key={c.id} variant="outlined">
                <CardContent sx={{ p: 1.5, '&:last-child': { pb: 1.5 } }}>
                  <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                    <Typography variant="body2" sx={{ fontWeight: 600 }}>{c.title}</Typography>
                    {c.errorCodes.map((code) => <Chip key={code} size="small" variant="outlined" label={code} />)}
                  </Stack>
                  <Typography variant="body2" sx={{ mt: 0.5 }}><b>Fixed by:</b> {c.resolution}</Typography>
                  {c.rootCause && <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>Root cause: {c.rootCause}</Typography>}
                  <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                    Recorded {formatDateTime(c.createdAt)} ·{' '}
                    <Box component={Link} to={`/knowledge?id=${c.id}`} sx={{ color: 'primary.main' }}>open in knowledge base</Box>
                  </Typography>
                </CardContent>
              </Card>
            ))}
          </Stack>
        </Box>
      )}

      {answer.dataLimitations.length > 0 && (
        <Alert severity="info" variant="outlined" sx={{ '& .MuiAlert-message': { width: '100%' } }}>
          <Typography variant="caption" sx={{ fontWeight: 700 }}>What the data cannot tell</Typography>
          {answer.dataLimitations.map((l) => <Typography key={l} variant="caption" sx={{ display: 'block' }}>• {l}</Typography>)}
        </Alert>
      )}

      {answer.evidenceLogIds.length > 0 && (
        <Button size="small" variant="outlined" component={Link} to={`/logs?logId=${answer.evidenceLogIds[0]}&range=all`}
          sx={{ alignSelf: 'flex-start' }}>
          Open first evidence log
        </Button>
      )}

      <AnswerFeedback message={message} />

      {message.toolExecutions.length > 0 && (
        <Accordion disableGutters variant="outlined" sx={{ '&:before': { display: 'none' } }}>
          <AccordionSummary expandIcon={<ExpandMore />}>
            <Typography variant="caption" color="text.secondary">
              Investigation: {message.toolExecutions.length} tool call{message.toolExecutions.length === 1 ? '' : 's'}
              {message.provider ? ` · provider: ${message.provider}` : ''}
            </Typography>
          </AccordionSummary>
          <AccordionDetails>
            <Stack spacing={0.5}>
              {message.toolExecutions.map((t, i) => (
                <Typography key={`${t.name}-${i}`} variant="caption" sx={{ fontFamily: 'monospace' }}>
                  {t.name}({t.argumentsJson}) → {t.error ? `error: ${t.error}` : `${t.resultCount} result(s)`}
                  {t.truncated ? ' · truncated' : ''}{t.flaggedContent > 0 ? ` · ${t.flaggedContent} instruction-like text(s) removed` : ''} · {t.durationMs} ms
                </Typography>
              ))}
            </Stack>
          </AccordionDetails>
        </Accordion>
      )}
    </Stack>
  );
}
