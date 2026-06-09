import React, { useEffect, useState } from 'react';
import { Card, CardContent, Typography, Box, CircularProgress, Button, Stack, Divider } from '@mui/material';
import { IconTrophy, IconLock, IconArrowUpRight, IconArrowDownRight } from '@tabler/icons-react';
import api from '../../../api/axiosInstance';
import { getAccessToken } from '../../../utils/tokenUtils';
import RequirePro from '../../../components/RequirePro';
import { useAuth } from '../../../context/AuthContext';

const UpgradePanel = () => {
  const { refreshMe } = useAuth();
  const [busy, setBusy] = useState(false);
  const handleUpgrade = async () => {
    setBusy(true);
    try {
      // Manual plan flip — replaced by Stripe checkout in Plan 7.
      await api.post('/Subscription/set-plan', { plan: 'Pro' });
      await refreshMe?.();
    } catch (err) { console.warn('Upgrade failed:', err?.message); }
    finally { setBusy(false); }
  };
  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" spacing={1.5} alignItems="center" sx={{ mb: 1 }}>
          <IconLock size={22} />
          <Typography variant="h5" fontWeight={600}>This Week</Typography>
        </Stack>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Your weekly performance report — a Pro feature.
        </Typography>
        <Button variant="contained" onClick={handleUpgrade} disabled={busy}>
          {busy ? 'Upgrading…' : 'Upgrade to Pro'}
        </Button>
      </CardContent>
    </Card>
  );
};

const ReportContent = () => {
  const [report, setReport] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!getAccessToken()) return;
    let active = true;
    (async () => {
      try {
        const res = await api.get('/Report/weekly');
        if (active) setReport(res.data?.result || null);
      } catch (err) { console.error('Failed to load weekly report:', err.message); }
      finally { if (active) setLoading(false); }
    })();
    return () => { active = false; };
  }, []);

  if (loading) {
    return (
      <Card sx={{ height: '100%' }}><CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}><CircularProgress size={28} /></Box>
      </CardContent></Card>
    );
  }

  const r = report;
  const hasData = r && (r.bestHabit || r.topMissReason || r.performanceScore > 0);
  const delta = r?.consistencyDelta ?? 0;

  return (
    <Card sx={{ height: '100%' }}>
      <CardContent>
        <Stack direction="row" justifyContent="space-between" alignItems="baseline" sx={{ mb: 1 }}>
          <Stack direction="row" spacing={1.5} alignItems="center">
            <IconTrophy size={22} />
            <Typography variant="h5" fontWeight={600}>This Week</Typography>
          </Stack>
          {r?.weekStart && <Typography variant="caption" color="text.secondary">from {r.weekStart}</Typography>}
        </Stack>

        {!hasData ? (
          <Typography variant="body2" color="text.secondary">
            Your first weekly report builds as you log this week — check back as the week fills in.
          </Typography>
        ) : (
          <>
            {/* hero score */}
            <Box sx={{ textAlign: 'center', py: 1 }}>
              <Typography variant="h2" fontWeight={700} lineHeight={1}>{r.performanceScore}</Typography>
              <Typography variant="subtitle2" color="text.secondary">Performance score · {r.scoreBand}</Typography>
            </Box>
            <Divider sx={{ my: 1.5 }} />
            <Stack spacing={1}>
              {r.bestHabit && <Typography variant="body2">🏆 Best: <b>{r.bestHabit}</b></Typography>}
              {r.worstHabit && <Typography variant="body2">🎯 Needs work: <b>{r.worstHabit}</b></Typography>}
              <Stack direction="row" spacing={0.5} alignItems="center">
                {delta >= 0 ? <IconArrowUpRight size={16} color="#13DEB9" /> : <IconArrowDownRight size={16} color="#FA896B" />}
                <Typography variant="body2" sx={{ color: delta >= 0 ? 'success.main' : 'error.main' }}>
                  {delta >= 0 ? '+' : ''}{delta}% vs last week
                </Typography>
              </Stack>
              {r.topMissReason && <Typography variant="body2" color="text.secondary">Top miss reason: {r.topMissReason}</Typography>}
              {r.focusNextWeek && <Typography variant="body2" sx={{ mt: 0.5, fontStyle: 'italic' }}>{r.focusNextWeek}</Typography>}
            </Stack>
          </>
        )}
      </CardContent>
    </Card>
  );
};

const DashboardWeeklyReport = () => (
  <RequirePro fallback={<UpgradePanel />}>
    <ReportContent />
  </RequirePro>
);

export default DashboardWeeklyReport;
