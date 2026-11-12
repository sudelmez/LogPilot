export type RangeKey = '15m' | '1h' | '6h' | '24h' | '7d';

export const RANGES: { key: RangeKey; label: string; ms: number }[] = [
  { key: '15m', label: '15 min', ms: 15 * 60_000 },
  { key: '1h', label: '1 hour', ms: 60 * 60_000 },
  { key: '6h', label: '6 hours', ms: 6 * 60 * 60_000 },
  { key: '24h', label: '24 hours', ms: 24 * 60 * 60_000 },
  { key: '7d', label: '7 days', ms: 7 * 24 * 60 * 60_000 },
];

/** from/to fetch anında hesaplanır; query key'e girmediği için sürekli yeniden istek atılmaz. */
export function rangeParams(key: RangeKey): { from: string; to: string } {
  const range = RANGES.find((r) => r.key === key) ?? RANGES[3];
  const to = new Date();
  return { from: new Date(to.getTime() - range.ms).toISOString(), to: to.toISOString() };
}
