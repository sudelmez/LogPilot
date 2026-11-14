import { useState } from 'react';
import type { ReactNode } from 'react';
import { Link, Outlet, useLocation } from 'react-router-dom';
import {
  AppBar, Box, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText, Stack, ToggleButton,
  ToggleButtonGroup, Toolbar, Tooltip, Typography,
} from '@mui/material';
import { useColorScheme } from '@mui/material/styles';
import MenuIcon from '@mui/icons-material/Menu';
import DashboardIcon from '@mui/icons-material/SpaceDashboardOutlined';
import AssistantIcon from '@mui/icons-material/ForumOutlined';
import LogsIcon from '@mui/icons-material/ManageSearchOutlined';
import IncidentsIcon from '@mui/icons-material/ReportGmailerrorredOutlined';
import ScenariosIcon from '@mui/icons-material/PlayCircleOutlined';
import KnowledgeIcon from '@mui/icons-material/LibraryBooksOutlined';
import SystemModeIcon from '@mui/icons-material/SettingsBrightnessOutlined';
import LightModeIcon from '@mui/icons-material/LightModeOutlined';
import DarkModeIcon from '@mui/icons-material/DarkModeOutlined';
import { BrandMark } from './ui';

const WIDTH = 216;

// Sıra = kullanım sıklığı: neyin bozuk olduğunu gör, nedenini sor, logda doğrula; sonra yönetim sayfaları.
const NAV: { to: string; label: string; icon: ReactNode; group: 'work' | 'manage' }[] = [
  { to: '/', label: 'Dashboard', icon: <DashboardIcon />, group: 'work' },
  { to: '/assistant', label: 'Assistant', icon: <AssistantIcon />, group: 'work' },
  { to: '/logs', label: 'Logs', icon: <LogsIcon />, group: 'work' },
  { to: '/incidents', label: 'Incidents', icon: <IncidentsIcon />, group: 'work' },
  { to: '/knowledge', label: 'Knowledge', icon: <KnowledgeIcon />, group: 'manage' },
  { to: '/scenarios', label: 'Scenarios', icon: <ScenariosIcon />, group: 'manage' },
];

function ModeSwitch() {
  const { mode, setMode } = useColorScheme();
  if (!mode) return null;   // ilk çizimde (sunucu tarafı yok ama yine de) belirsiz
  return (
    <ToggleButtonGroup size="small" exclusive value={mode} onChange={(_, next) => next && setMode(next)} aria-label="Color theme"
      sx={{ '& .MuiToggleButton-root': { px: 1, py: 0.5, flex: 1 } }} fullWidth>
      <Tooltip title="Follow system"><ToggleButton value="system" aria-label="Follow system"><SystemModeIcon sx={{ fontSize: 17 }} /></ToggleButton></Tooltip>
      <Tooltip title="Light"><ToggleButton value="light" aria-label="Light"><LightModeIcon sx={{ fontSize: 17 }} /></ToggleButton></Tooltip>
      <Tooltip title="Dark"><ToggleButton value="dark" aria-label="Dark"><DarkModeIcon sx={{ fontSize: 17 }} /></ToggleButton></Tooltip>
    </ToggleButtonGroup>
  );
}

export default function Layout() {
  const [open, setOpen] = useState(false);
  const { pathname } = useLocation();

  const item = (n: (typeof NAV)[number]) => {
    const selected = n.to === '/' ? pathname === '/' : pathname.startsWith(n.to);
    return (
      <ListItemButton key={n.to} component={Link} to={n.to} selected={selected} onClick={() => setOpen(false)}
        aria-current={selected ? 'page' : undefined}
        sx={{ minHeight: 34, py: 0.5, px: 1.25, mb: 0.25, color: selected ? 'text.primary' : 'text.secondary',
          '& .MuiListItemIcon-root': { color: selected ? 'primary.main' : 'inherit' } }}>
        <ListItemIcon sx={{ minWidth: 30, '& svg': { fontSize: 19 } }}>{n.icon}</ListItemIcon>
        <ListItemText primary={n.label} slotProps={{ primary: { sx: { fontSize: '0.875rem', fontWeight: selected ? 650 : 550 } } }} />
      </ListItemButton>
    );
  };

  const nav = (
    <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column', bgcolor: 'background.rail' }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', px: 2, height: 56 }}>
        <BrandMark />
        <Typography sx={{ fontWeight: 700, fontSize: '1rem', letterSpacing: '-0.01em' }}>LogPilot</Typography>
      </Stack>
      <List component="nav" aria-label="Main" sx={{ px: 1, py: 0 }}>
        {NAV.filter((n) => n.group === 'work').map(item)}
        <Typography variant="caption" color="text.disabled" component="div" sx={{ px: 1.25, pt: 2, pb: 0.5, fontWeight: 600 }}>
          Manage
        </Typography>
        {NAV.filter((n) => n.group === 'manage').map(item)}
      </List>
      <Box sx={{ flexGrow: 1 }} />
      <Stack spacing={1.25} sx={{ p: 1.5 }}>
        <ModeSwitch />
        <Typography variant="caption" color="text.disabled" sx={{ px: 0.5 }}>Internal tool · synthetic data</Typography>
      </Stack>
    </Box>
  );

  return (
    <Box sx={{ display: 'flex', minHeight: '100%' }}>
      <AppBar position="fixed" color="inherit" elevation={0}
        sx={{ display: { md: 'none' }, borderBottom: 1, borderColor: 'divider', bgcolor: 'background.rail' }}>
        <Toolbar variant="dense" sx={{ gap: 1 }}>
          <IconButton edge="start" onClick={() => setOpen(true)} aria-label="Open navigation"><MenuIcon /></IconButton>
          <BrandMark size={20} />
          <Typography sx={{ fontWeight: 700 }}>LogPilot</Typography>
        </Toolbar>
      </AppBar>

      <Drawer variant="temporary" open={open} onClose={() => setOpen(false)} ModalProps={{ keepMounted: true }}
        sx={{ display: { xs: 'block', md: 'none' }, '& .MuiDrawer-paper': { width: WIDTH, border: 0 } }}>
        {nav}
      </Drawer>
      <Drawer variant="permanent" open
        sx={{ display: { xs: 'none', md: 'block' }, width: WIDTH, flexShrink: 0,
          '& .MuiDrawer-paper': { width: WIDTH, boxSizing: 'border-box', borderRight: 1, borderColor: 'divider' } }}>
        {nav}
      </Drawer>

      <Box component="main" sx={{ flexGrow: 1, minWidth: 0, px: { xs: 2, md: 3 }, pb: 4, pt: { xs: 9, md: 3 } }}>
        <Box sx={{ maxWidth: 1440, mx: 'auto' }}>
          <Outlet />
        </Box>
      </Box>
    </Box>
  );
}
