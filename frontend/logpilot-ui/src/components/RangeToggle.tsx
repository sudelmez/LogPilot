import { ToggleButton, ToggleButtonGroup, Tooltip } from '@mui/material';
import { RANGES } from '../lib/ranges';
import type { RangeKey } from '../lib/ranges';

/** Zaman aralığı: kısa etiketler (15m, 1h…) tek satırda kalır; tam adı ipucunda. */
export default function RangeToggle({ value, onChange }: { value: RangeKey; onChange: (value: RangeKey) => void }) {
  return (
    <ToggleButtonGroup size="small" exclusive value={value} aria-label="Time range"
      onChange={(_, next: RangeKey | null) => next && onChange(next)} sx={{ flexShrink: 0 }}>
      {RANGES.map((r) => (
        <Tooltip key={r.key} title={`Last ${r.label}`}>
          <ToggleButton value={r.key} aria-label={`Last ${r.label}`} sx={{ px: 1.25, whiteSpace: 'nowrap', fontFamily: 'inherit' }}>{r.key}</ToggleButton>
        </Tooltip>
      ))}
    </ToggleButtonGroup>
  );
}
