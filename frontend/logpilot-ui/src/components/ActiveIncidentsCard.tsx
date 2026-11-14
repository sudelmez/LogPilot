import { Link } from 'react-router-dom';
import { Box } from '@mui/material';
import { useActiveIncidents } from '../api/incidentHooks';
import StatCard from './StatCard';

const RANK = { Low: 0, Medium: 1, High: 2, Critical: 3 } as const;

export default function ActiveIncidentsCard() {
  const query = useActiveIncidents();
  const data = query.data;

  const worst = data?.items.reduce<keyof typeof RANK | null>(
    (acc, i) => (acc === null || RANK[i.severity] > RANK[acc] ? i.severity : acc), null);

  return (
    <Box component={Link} to="/incidents" sx={{ textDecoration: 'none', color: 'inherit', display: 'block', transition: 'background-color 150ms', '&:hover': { bgcolor: 'background.raised' } }}>
      <StatCard
        title="Active incidents"
        value={query.isError ? '—' : String(data?.totalCount ?? 0)}
        tone={worst === 'Critical' || worst === 'High' ? 'error' : worst ? 'warning' : 'success'}
        loading={query.isPending}
        caption={query.isError ? 'Could not load incidents' : worst ? `Highest severity: ${worst}` : 'No unresolved incidents'}
      />
    </Box>
  );
}
