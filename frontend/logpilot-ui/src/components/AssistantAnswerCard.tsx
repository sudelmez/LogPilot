import { Link } from 'react-router-dom';
import {
  Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, LinearProgress, Stack, Tooltip, Typography,
} from '@mui/material';
import ExpandMore from '@mui/icons-material/ExpandMore';
import type { ChatMessage, ConfidenceLevel } from '../api/chatTypes';
import { formatDateTime } from '../lib/format';
import { MONO } from '../theme';
import AnswerFeedback from './AnswerFeedback';
import { Code, RouteLine, SectionLabel, StatusDot } from './ui';
import type { RouteStop, Tone } from './ui';

const confidenceTone: Record<ConfidenceLevel, Tone> = {
  NotApplicable: 'neutral', Insufficient: 'error', Low: 'warning', Medium: 'info', High: 'success',
};

function logsLink(ids: string[], correlationId?: string): string {
  // range=all: kanıt eski olsa bile Logs ekranında görünsün
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
  // İmza öğesi: zincirleme araçların gezdiği tablolar (sonuncu zincir yeterli; çoğu cevapta tektir).
  const route: RouteStop[] = [...message.toolExecutions].reverse().find((t) => t.path && t.path.length > 0)?.path ?? [];

  return (
    <Stack spacing={2.25}>
      <Typography variant="body1" sx={{ fontWeight: 550, maxWidth: '78ch' }}>{answer.summary}</Typography>

      {answer.clarifyingQuestion && <Alert severity="info" variant="outlined">{answer.clarifyingQuestion}</Alert>}

      {route.length > 0 && (
        <Box>
          <SectionLabel>Path through the logs</SectionLabel>
          <RouteLine stops={route} />
        </Box>
      )}

      {answer.probableCause && (
        <Box sx={{ maxWidth: '78ch' }}>
          <Stack direction="row" spacing={1.5} sx={{ alignItems: 'baseline', mb: 0.5 }}>
            <SectionLabel>Probable cause</SectionLabel>
            {showConfidence && (
              <Tooltip arrow title={
                <Box>
                  <Typography variant="caption" sx={{ fontWeight: 700 }} component="div">
                    Computed from the evidence: {answer.confidenceScore}/100
                  </Typography>
                  {answer.confidenceFactors.map((f) => <Typography key={f} variant="caption" component="div">{f}</Typography>)}
                </Box>}>
                <Box sx={{ display: 'inline-flex', cursor: 'help' }}>
                  <StatusDot tone={confidenceTone[answer.confidence]} label={`${answer.confidence} confidence`} />
                </Box>
              </Tooltip>
            )}
          </Stack>
          <Typography variant="body2">{answer.probableCause}</Typography>
          {showConfidence && (
            <LinearProgress variant="determinate" value={answer.confidenceScore}
              color={confidenceTone[answer.confidence] === 'neutral' ? 'primary' : confidenceTone[answer.confidence] as 'error' | 'warning' | 'info' | 'success'}
              sx={{ mt: 1, maxWidth: 220, bgcolor: 'background.raised' }} aria-label="Confidence" />
          )}
        </Box>
      )}

      {!answer.probableCause && answer.confidence === 'Insufficient' && (
        <StatusDot tone="error" label="Insufficient evidence" />
      )}

      {answer.evidence.length > 0 && (
        <Box>
          <SectionLabel>Evidence</SectionLabel>
          <Box sx={{ border: 1, borderColor: 'divider', borderRadius: 2, overflow: 'hidden' }}>
            {answer.evidence.map((e, i) => (
              <Box key={`${e.errorCode}-${e.service}-${i}`} sx={{
                display: 'grid', gap: 1, alignItems: 'center', px: 1.5, py: 1,
                gridTemplateColumns: { xs: '1fr', sm: 'minmax(0, 1.3fr) minmax(0, 1fr) auto' },
                borderTop: i === 0 ? 0 : 1, borderColor: 'divider',
              }}>
                <Box sx={{ minWidth: 0 }}>
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <Code tone="error">{e.errorCode}</Code>
                    <Typography variant="caption" color="text.secondary" sx={{ fontFamily: MONO }}>×{e.count}</Typography>
                  </Stack>
                  <Typography variant="body2" sx={{ mt: 0.25, fontWeight: 600 }}>{e.service}</Typography>
                </Box>
                <Box sx={{ minWidth: 0 }}>
                  <Typography variant="caption" color="text.secondary" component="div" sx={{ fontFamily: MONO }}>
                    {formatDateTime(e.firstSeen)}{e.lastSeen && e.lastSeen !== e.firstSeen ? ` → ${formatDateTime(e.lastSeen)}` : ''}
                  </Typography>
                  {e.userCodes.length > 0 && (
                    <Typography variant="caption" color="text.secondary" component="div" noWrap title={e.userCodes.join(', ')}>
                      Users: {e.userCodes.join(', ')}
                    </Typography>
                  )}
                </Box>
                <Button size="small" component={Link} to={logsLink(e.logIds, e.correlationIds[0])} sx={{ justifySelf: { sm: 'end' } }}>
                  Open logs
                </Button>
              </Box>
            ))}
          </Box>
        </Box>
      )}

      {answer.affectedServices.length > 0 && (
        <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'baseline', flexWrap: 'wrap' }}>
          <SectionLabel>Affected services</SectionLabel>
          {answer.affectedServices.map((s) => <Code key={s}>{s}</Code>)}
        </Stack>
      )}

      {answer.suggestedActions.length > 0 && (
        <Box sx={{ maxWidth: '78ch' }}>
          <SectionLabel>Suggested next steps</SectionLabel>
          <Box component="ol" sx={{ m: 0, pl: 2.5, '& li': { mb: 0.5 } }}>
            {answer.suggestedActions.map((a) => <Typography key={a} component="li" variant="body2">{a}</Typography>)}
          </Box>
        </Box>
      )}

      {answer.relatedCases.length > 0 && (
        <Box sx={{ maxWidth: '78ch' }}>
          <SectionLabel>Solved before</SectionLabel>
          <Typography variant="caption" color="text.secondary" component="div" sx={{ mb: 1 }}>
            Fixes people verified for the same error. A hint, not evidence for this answer.
          </Typography>
          <Stack spacing={1}>
            {answer.relatedCases.map((c) => (
              <Box key={c.id} sx={{ border: 1, borderColor: 'divider', borderRadius: 2, px: 1.5, py: 1 }}>
                <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>{c.title}</Typography>
                  {c.errorCodes.map((code) => <Code key={code}>{code}</Code>)}
                </Stack>
                <Typography variant="body2" sx={{ mt: 0.5 }}><Box component="span" sx={{ fontWeight: 600 }}>Fixed by:</Box> {c.resolution}</Typography>
                {c.rootCause && <Typography variant="caption" color="text.secondary" component="div">Root cause: {c.rootCause}</Typography>}
                <Typography variant="caption" color="text.secondary" component="div">
                  Recorded {formatDateTime(c.createdAt)} · <Link to={`/knowledge?id=${c.id}`}>open in knowledge base</Link>
                </Typography>
              </Box>
            ))}
          </Stack>
        </Box>
      )}

      {answer.dataLimitations.length > 0 && (
        <Box sx={{ maxWidth: '78ch' }}>
          <SectionLabel>What the data cannot tell</SectionLabel>
          <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
            {answer.dataLimitations.map((l) => <Typography key={l} component="li" variant="caption" color="text.secondary">{l}</Typography>)}
          </Box>
        </Box>
      )}

      <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', pt: 0.5, borderTop: 1, borderColor: 'divider' }}>
        {answer.evidenceLogIds.length > 0 && (
          <Button size="small" variant="outlined" component={Link} to={`/logs?logId=${answer.evidenceLogIds[0]}&range=all`}>
            Open first evidence log
          </Button>
        )}
        <Box sx={{ flexGrow: 1 }} />
        <AnswerFeedback message={message} />
      </Stack>

      {message.toolExecutions.length > 0 && (
        <Accordion disableGutters elevation={0} sx={{ bgcolor: 'transparent', border: 1, borderColor: 'divider', borderRadius: '8px !important', '&:before': { display: 'none' } }}>
          <AccordionSummary expandIcon={<ExpandMore />} sx={{ minHeight: 36, '& .MuiAccordionSummary-content': { my: 0.75 } }}>
            <Typography variant="caption" color="text.secondary">
              {message.toolExecutions.length} tool call{message.toolExecutions.length === 1 ? '' : 's'}
              {message.provider ? ` · ${message.provider}` : ''}
            </Typography>
          </AccordionSummary>
          <AccordionDetails sx={{ pt: 0 }}>
            <Stack spacing={0.75}>
              {message.toolExecutions.map((t, i) => (
                <Box key={`${t.name}-${i}`} sx={{ fontFamily: MONO, fontSize: '0.75rem', overflowWrap: 'anywhere' }}>
                  <Box component="span" sx={{ fontWeight: 650 }}>{t.name}</Box>
                  <Box component="span" sx={{ color: 'text.secondary' }}>({t.argumentsJson})</Box>
                  {' → '}
                  {t.error
                    ? <Box component="span" sx={{ color: 'error.main' }}>error: {t.error}</Box>
                    : <span>{t.resultCount} result{t.resultCount === 1 ? '' : 's'}</span>}
                  <Box component="span" sx={{ color: 'text.secondary' }}>
                    {t.truncated ? ' · truncated' : ''}{t.flaggedContent > 0 ? ` · ${t.flaggedContent} instruction-like text removed` : ''} · {t.durationMs} ms
                  </Box>
                </Box>
              ))}
            </Stack>
          </AccordionDetails>
        </Accordion>
      )}
    </Stack>
  );
}
