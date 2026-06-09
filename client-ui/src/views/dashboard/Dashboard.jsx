import React, { Suspense, lazy, useEffect, useState } from 'react';
import { Grid, Box, Skeleton } from '@mui/material';
import PageContainer from '../../components/container/PageContainer';
import api from '../../api/axiosInstance';
import { getAccessToken } from '../../utils/tokenUtils';

// components
const TopCards = lazy(() => import('./components/TopCards'));
const HabitCompletionRate = lazy(() => import('./components/habitCompletionRates/HabitCompletionRate'));
const HabitHeatmapCalendar = lazy(() => import('./components/HabitHeatmapCalendar'));
const DashboardInsights = lazy(() => import('./components/DashboardInsights'));

const buildCurrentMonthHeatmap = (cells) => {
  if (!Array.isArray(cells)) return {};

  const now = new Date();
  const year = now.getFullYear();
  const month = now.getMonth() + 1;
  const result = {};

  for (const cell of cells) {
    const d = new Date(cell.date);
    if (d.getFullYear() === year && d.getMonth() + 1 === month) {
      result[d.getDate()] = cell.intensity ?? 0;
    }
  }
  return result;
};

const Dashboard = () => {
  const fallback = <Skeleton variant="rectangular" height={200} animation="wave" />;
  const [heatmapData, setHeatmapData] = useState({});

  useEffect(() => {
    if (!getAccessToken()) return;
    let cancelled = false;
    (async () => {
      try {
        const res = await api.get('/Dashboard/heatmap', { params: { days: 90 } });
        if (cancelled) return;
        setHeatmapData(buildCurrentMonthHeatmap(res.data?.result?.cells));
      } catch (err) {
        console.error('Failed to load heatmap:', err.message);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  return (
    <PageContainer title="Dashboard" description="Your habit overview">
      <Box>
        <Grid container spacing={4}>
          <Grid xs={12}>
            <Suspense fallback={fallback}>
              <TopCards />
            </Suspense>
          </Grid>

          <Grid item xs={12} lg={8} sx={{ width: '100%' }}>
            <Suspense fallback={fallback}>
              <HabitCompletionRate />
            </Suspense>
          </Grid>

          {/* Insights panel — Pro feature (free users see an upgrade prompt). */}
          <Grid item xs={12} lg={4}>
            <Suspense fallback={fallback}>
              <DashboardInsights />
            </Suspense>
          </Grid>

          <Grid item xs={12} sx={{ width: '100%' }}>
            <Suspense fallback={fallback}>
              <HabitHeatmapCalendar data={heatmapData} />
            </Suspense>
          </Grid>
        </Grid>
      </Box>
    </PageContainer>
  );
};

export default Dashboard;
