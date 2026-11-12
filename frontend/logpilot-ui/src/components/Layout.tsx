import { useState } from 'react';
import { Link, Outlet, useLocation } from 'react-router-dom';
import {
  AppBar, Box, Divider, Drawer, IconButton, List, ListItemButton, ListItemIcon, ListItemText, Toolbar, Typography,
} from '@mui/material';
import MenuIcon from '@mui/icons-material/Menu';
import DashboardIcon from '@mui/icons-material/DashboardOutlined';
import AssistantIcon from '@mui/icons-material/SmartToyOutlined';
import LogsIcon from '@mui/icons-material/ManageSearchOutlined';
import IncidentsIcon from '@mui/icons-material/ReportProblemOutlined';
import ScenariosIcon from '@mui/icons-material/PlayCircleOutlined';
import KnowledgeIcon from '@mui/icons-material/MenuBookOutlined';
import type { ReactNode } from 'react';

const WIDTH = 240;

const NAV: { to: string; label: string; icon: ReactNode }[] = [
  { to: '/', label: 'Dashboard', icon: <DashboardIcon /> },
  { to: '/assistant', label: 'AI Assistant', icon: <AssistantIcon /> },
  { to: '/logs', label: 'Logs Explorer', icon: <LogsIcon /> },
  { to: '/incidents', label: 'Incidents', icon: <IncidentsIcon /> },
  { to: '/knowledge', label: 'Knowledge', icon: <KnowledgeIcon /> },
  { to: '/scenarios', label: 'Scenarios', icon: <ScenariosIcon /> },
];

export default function Layout() {
  const [open, setOpen] = useState(false);
  const { pathname } = useLocation();

  const nav = (
    <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
      <Toolbar>
        <Typography variant="h6" sx={{ fontWeight: 800, letterSpacing: -0.5 }}>
          Log<Box component="span" sx={{ color: 'primary.main' }}>Pilot</Box>
        </Typography>
      </Toolbar>
      <Divider />
      <List sx={{ px: 1, pt: 1.5 }}>
        {NAV.map((item) => {
          const selected = item.to === '/' ? pathname === '/' : pathname.startsWith(item.to);
          return (
            <ListItemButton
              key={item.to}
              component={Link}
              to={item.to}
              selected={selected}
              onClick={() => setOpen(false)}
              sx={{ borderRadius: 2, mb: 0.5 }}
            >
              <ListItemIcon sx={{ minWidth: 38 }}>{item.icon}</ListItemIcon>
              <ListItemText primary={item.label} slotProps={{ primary: { sx: { fontWeight: 600 } } }} />
            </ListItemButton>
          );
        })}
      </List>
      <Box sx={{ flexGrow: 1 }} />
      <Typography variant="caption" color="text.secondary" sx={{ p: 2 }}>
        Synthetic data only
      </Typography>
    </Box>
  );

  return (
    <Box sx={{ display: 'flex', minHeight: '100%' }}>
      <AppBar position="fixed" color="inherit" elevation={0}
        sx={{ display: { md: 'none' }, borderBottom: 1, borderColor: 'divider' }}>
        <Toolbar>
          <IconButton edge="start" onClick={() => setOpen(true)} aria-label="Open navigation"><MenuIcon /></IconButton>
          <Typography variant="h6" sx={{ ml: 1, fontWeight: 800 }}>LogPilot</Typography>
        </Toolbar>
      </AppBar>

      <Drawer variant="temporary" open={open} onClose={() => setOpen(false)} ModalProps={{ keepMounted: true }}
        sx={{ display: { xs: 'block', md: 'none' }, '& .MuiDrawer-paper': { width: WIDTH } }}>
        {nav}
      </Drawer>
      <Drawer variant="permanent" open
        sx={{ display: { xs: 'none', md: 'block' }, width: WIDTH, flexShrink: 0,
          '& .MuiDrawer-paper': { width: WIDTH, boxSizing: 'border-box' } }}>
        {nav}
      </Drawer>

      <Box component="main" sx={{ flexGrow: 1, minWidth: 0, p: { xs: 2, md: 3 }, pt: { xs: 9, md: 3 } }}>
        <Outlet />
      </Box>
    </Box>
  );
}
