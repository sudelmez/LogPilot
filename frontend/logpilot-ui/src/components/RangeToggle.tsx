import { ToggleButton, ToggleButtonGroup } from '@mui/material';
import { RANGES } from '../lib/ranges';
import type { RangeKey } from '../lib/ranges';

export default function RangeToggle({ value, onChange }: { value: RangeKey; onChange: (value: RangeKey) => void }) {
  return (
    <ToggleButtonGroup size="small" exclusive value={value} onChange={(_, next: RangeKey | null) => next && onChange(next)}>
      {RANGES.map((r) => (
        <ToggleButton key={r.key} value={r.key} sx={{ px: 1.5, textTransform: 'none' }}>{r.label}</ToggleButton>
      ))}
    </ToggleButtonGroup>
  );
}
