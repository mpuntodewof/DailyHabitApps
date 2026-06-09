import React, { useEffect, useState } from 'react';
import { Card, CardContent, Typography, List, ListItem, ListItemIcon, ListItemText, Box, CircularProgress, Button, Stack } from '@mui/material';
import { IconBulb, IconLock } from '@tabler/icons-react';
import api from '../../../api/axiosInstance';
import { getAccessToken } from '../../../utils/tokenUtils';
import RequirePro from '../../../components/RequirePro';
import { useAuth } from '../../../context/AuthContext';

/**
 * Dashboard "Insights" panel — surfaces the Pro failure-analysis insights
 * (GET /api/Insight) right on the overview page. Free users see an inline
 * upgrade prompt; Pro users see their insight list.
 *
 * Reuses the Plan 5 backend + <RequirePro> gate. The standalone /insights
 * view shares the same endpoint.
 */

// Free-tier fallback: an inline upgrade prompt.
const UpgradePanel = () => {
  const { refreshMe } = useAuth();
  const [busy, setBusy] = useState(false);

  const handleUpgrade = async () => {
    setBusy(true);
    try {
      // Manual plan flip — replaced by Stripe checkout in Plan 7.
      await api.post('/Subscription/set-plan', { plan: 'Pro' });
      await refreshMe?.();
    } catch (err) {
      console.warn('Upgrade failed:', err?.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" spacing={1.5} alignItems="center" sx={{ mb: 1 }}>
          <IconLock size={22} />
          <Typography variant="h5" fontWeight={600}>Insights</Typography>
        </Stack>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Unlock pattern analysis of your habits and skips — a Pro feature.
        </Typography>
        <Button variant="contained" onClick={handleUpgrade} disabled={busy}>
          {busy ? 'Upgrading…' : 'Upgrade to Pro'}
        </Button>
      </CardContent>
    </Card>
  );
};

// Pro-tier content: the insight list.
const InsightsList = () => {
  const [insights, setInsights] = useState([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!getAccessToken()) return;
    let active = true;
    (async () => {
      try {
        const res = await api.get('/Insight');
        if (active) setInsights(res.data?.result || []);
      } catch (err) {
        console.error('Failed to load insights:', err.message);
      } finally {
        if (active) setLoading(false);
      }
    })();
    return () => { active = false; };
  }, []);

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" spacing={1.5} alignItems="center" sx={{ mb: 1.5 }}>
          <IconBulb size={22} />
          <Typography variant="h5" fontWeight={600}>Insights</Typography>
        </Stack>

        {loading ? (
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
            <CircularProgress size={28} />
          </Box>
        ) : insights.length === 0 ? (
          <Typography variant="body2" color="text.secondary">
            Keep logging your habits and skips — your insights will appear here as patterns emerge.
          </Typography>
        ) : (
          <List dense disablePadding>
            {insights.map((insight) => (
              <ListItem key={insight.key} disableGutters alignItems="flex-start">
                <ListItemIcon sx={{ minWidth: 34, mt: 0.5 }}>
                  <IconBulb size={18} />
                </ListItemIcon>
                <ListItemText primary={insight.text} />
              </ListItem>
            ))}
          </List>
        )}
      </CardContent>
    </Card>
  );
};

const DashboardInsights = () => (
  <RequirePro fallback={<UpgradePanel />}>
    <InsightsList />
  </RequirePro>
);

export default DashboardInsights;
