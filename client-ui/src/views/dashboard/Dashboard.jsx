import React, { Suspense, lazy, useEffect, useState } from 'react';
import { Grid, Box, Skeleton } from '@mui/material';
import PageContainer from '../../components/container/PageContainer';
import api from '../../api/axiosInstance';
import { getAccessToken } from '../../utils/tokenUtils';
import { useAuth } from '../../context/AuthContext';

// components
const TopCards = lazy(() => import('./components/TopCards'));
const HabitCompletionRate = lazy(() => import('./components/habitCompletionRates/HabitCompletionRate'));
const ContributionHeatmap = lazy(() => import('./components/ContributionHeatmap'));
const DashboardInsights = lazy(() => import('./components/DashboardInsights'));
const DashboardWeeklyReport = lazy(() => import('./components/DashboardWeeklyReport'));

const Dashboard = () => {
  const fallback = <Skeleton variant="rectangular" height={200} animation="wave" />;
  const [heatmapCells, setHeatmapCells] = useState([]);
  const { refreshMe } = useAuth();

  useEffect(() => {
    if (typeof window !== 'undefined' && window.location.search.includes('checkout=success')) {
      refreshMe?.();
    }
  }, [refreshMe]);

  useEffect(() => {
    if (!getAccessToken()) return;
    let cancelled = false;
    (async () => {
      try {
        // Full trailing year, GitHub-style contribution graph.
        const res = await api.get('/Dashboard/heatmap', { params: { days: 365 } });
        if (cancelled) return;
        setHeatmapCells(res.data?.result?.cells || []);
      } catch (err) {
        console.error('Failed to load heatmap:', err.message);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  return (
    <PageContainer title="Dashboard" description="Your habit overview">
      <Box>
        {/* MUI 7 Grid: items use `size` (no `item`/bare xs/lg props, which v7 ignores). */}
        <Grid container spacing={4}>
          <Grid size={12}>
            <Suspense fallback={fallback}>
              <TopCards />
            </Suspense>
          </Grid>

          <Grid size={12}>
            <Suspense fallback={fallback}>
              <HabitCompletionRate />
            </Suspense>
          </Grid>

          {/* Two Pro narrative cards share a full-width row (each half) so neither
              leaves blank space. Free users see upgrade prompts in both. */}
          <Grid size={{ xs: 12, lg: 6 }}>
            <Suspense fallback={fallback}>
              <DashboardInsights />
            </Suspense>
          </Grid>

          <Grid size={{ xs: 12, lg: 6 }}>
            <Suspense fallback={fallback}>
              <DashboardWeeklyReport />
            </Suspense>
          </Grid>

          <Grid size={12}>
            <Suspense fallback={fallback}>
              <ContributionHeatmap cells={heatmapCells} />
            </Suspense>
          </Grid>
        </Grid>
      </Box>
    </PageContainer>
  );
};

export default Dashboard;
