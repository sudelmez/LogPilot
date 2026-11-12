import { Card, CardContent, Skeleton, Typography } from '@mui/material';

interface Props {
  title: string;
  value: string;
  caption?: string;
  tone?: 'default' | 'error' | 'warning' | 'success';
  loading?: boolean;
}

export default function StatCard({ title, value, caption, tone = 'default', loading }: Props) {
  const color = tone === 'default' ? 'text.primary' : `${tone}.main`;
  return (
    <Card>
      <CardContent>
        <Typography variant="overline" color="text.secondary">{title}</Typography>
        {loading ? (
          <Skeleton width={90} height={44} />
        ) : (
          <Typography variant="h4" sx={{ fontWeight: 700, color }}>{value}</Typography>
        )}
        {caption && <Typography variant="caption" color="text.secondary">{caption}</Typography>}
      </CardContent>
    </Card>
  );
}
