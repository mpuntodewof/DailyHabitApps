import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  CardContent,
  Typography,
  Button,
  Stack,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  CircularProgress,
} from '@mui/material';
import { IconBulb, IconLock } from '@tabler/icons-react';
import PageContainer from '../../components/container/PageContainer';
import RequirePro from '../../components/RequirePro';
import { useAuth } from '../../context/AuthContext';
import api from '../../api/axiosInstance';

/**
 * Upgrade prompt shown to Free users in place of the insights list.
 * The set-plan call is a manual-testing bridge — Stripe checkout replaces
 * this flow in Plan 7.
 */
const UpgradeCard = ({ onUpgrade }) => {
  const [busy, setBusy] = useState(false);

  const handleClick = async () => {
    setBusy(true);
    try {
      await onUpgrade();
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card sx={{ maxWidth: 480, mx: 'auto', mt: 4 }}>
      <CardContent>
        <Stack spacing={2} alignItems="center" textAlign="center">
          <IconLock size={40} stroke={1.5} />
          <Typography variant="h5">Insights are a Pro feature</Typography>
          <Typography variant="body2" color="text.secondary">
            Unlock pattern analysis of your habits and skips.
          </Typography>
          <Button variant="contained" onClick={handleClick} disabled={busy}>
            {busy ? 'Upgrading…' : 'Upgrade to Pro'}
          </Button>
        </Stack>
      </CardContent>
    </Card>
  );
};

/**
 * Pro-only insights list. Fetches GET /api/Insight on mount and renders each
 * { key, text } entry. Empty array → encouraging empty state.
 */
const InsightsList = () => {
  const [insights, setInsights] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    (async () => {
      try {
        const res = await api.get('/Insight');
        if (active) setInsights(res.data?.result || []);
      } catch (err) {
        console.warn('Failed to load insights:', err?.message);
        if (active) setInsights([]);
      } finally {
        if (active) setLoading(false);
      }
    })();
    return () => { active = false; };
  }, []);

  if (loading) {
    return (
      <Box display="flex" justifyContent="center" sx={{ py: 4 }}>
        <CircularProgress />
      </Box>
    );
  }

  if (insights.length === 0) {
    return (
      <Card sx={{ mt: 2 }}>
        <CardContent>
          <Typography variant="body1" color="text.secondary">
            Keep logging your habits and skips — your insights will appear as
            patterns emerge.
          </Typography>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card sx={{ mt: 2 }}>
      <List>
        {insights.map((insight) => (
          <ListItem key={insight.key} alignItems="flex-start">
            <ListItemIcon sx={{ minWidth: 40, mt: 0.5 }}>
              <IconBulb size={22} stroke={1.5} />
            </ListItemIcon>
            <ListItemText primary={insight.text} />
          </ListItem>
        ))}
      </List>
    </Card>
  );
};

const Insights = () => {
  const { refreshMe } = useAuth();

  const handleUpgrade = async () => {
    // Manual-testing bridge: flips the current user to Pro/Active.
    // Stripe checkout replaces this in Plan 7.
    await api.post('/Subscription/set-plan', { plan: 'Pro' });
    await refreshMe();
  };

  return (
    <PageContainer title="Insights" description="Pattern analysis of your habits and skips">
      <Box display="flex" alignItems="center" gap={1} sx={{ mb: 2 }}>
        <IconBulb size={28} />
        <Typography variant="h4">Insights</Typography>
      </Box>

      <RequirePro fallback={<UpgradeCard onUpgrade={handleUpgrade} />}>
        <InsightsList />
      </RequirePro>
    </PageContainer>
  );
};

export default Insights;
