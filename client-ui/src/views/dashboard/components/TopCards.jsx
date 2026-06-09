import React, { useEffect } from 'react';
import { Grid, Box, Card, Typography, Stack } from '@mui/material';
import { IconChecklist, IconFlame, IconCalendarWeek, IconTrendingUp, IconArrowDownRight, IconArrowUpLeft } from '@tabler/icons-react';
import { useHabitTracking } from '../../../context/HabitTrackingContext';
import { useAuth } from '../../../context/AuthContext';

const TopCards = () => {
  const { habitOverview, getHabitOverview } = useHabitTracking();
  const { user } = useAuth();

  useEffect(() => {
    if (!user?.sub) return;

    getHabitOverview(user?.sub);
  }, [getHabitOverview, user?.sub]);

  const overview = habitOverview?.result;
  
  const monthlyAvg = overview?.monthlyTrend?.length
    ? Math.round(
      overview.monthlyTrend.reduce((sum, m) => sum + m.completionRate, 0) /
      overview.monthlyTrend.length
    ) : 0;

  const stats = [
    {
      title: 'Today Completed',
      amount: `${overview?.todayCard.completed}/${overview?.todayCard.total}`,
      percent: `${overview?.todayCard.total > 0
        ? Math.round((overview.todayCard.completed / overview.todayCard.total) * 100)
        : 0}%`,
      icon: IconChecklist,
      color: '#5D87FF',
      increase: overview?.todayCard.completed > 0
    },
    {
      title: 'Current Streak',
      amount: `${overview?.streakCard.currentStreak} days`,
      percent: `Best: ${overview?.streakCard.longestStreak}`,
      icon: IconFlame,
      color: '#FA896B',
      increase: overview?.streakCard.currentStreak > 0
    },
    {
      title: 'Weekly Consistency',
      amount: `${overview?.weeklyCard.completionRate}%`,
      percent: overview?.weeklyCard.completionRate >= 50 ? 'On track' : 'Needs work',
      icon: IconCalendarWeek,
      color: '#49BEFF',
      increase: overview?.weeklyCard.completionRate >= 50
    },
    {
      title: 'Monthly Average',
      amount: `${monthlyAvg}%`,
      percent: overview?.monthlyTrend?.at(-1)?.completionRate >= monthlyAvg
        ? 'Improving'
        : 'Declining',
      icon: IconTrendingUp,
      color: '#13DEB9',
      increase: overview?.monthlyTrend?.at(-1)?.completionRate >= monthlyAvg
    }
  ];

  return (
    <Grid container spacing={3} alignItems="stretch">
      {stats.map((stat, index) => (
        <Grid size={{ xs: 12, sm: 6, lg: 3 }} key={index}>
          <Card sx={{
            padding: 3,
            height: '170px',
            width: '100%',
            maxWidth: '200px',
            minWidth: '200px',
            display: 'flex', 
            alignItems: 'center',
            justifyContent: 'center',
            borderRadius: '12px',
          }}>
            <Stack spacing={2}>
              <Box
                width={70}
                height={45}
                bgcolor={stat.color}
                display="flex"
                alignItems="center"
                justifyContent="center"
                borderRadius="12px"
              >
                {React.createElement(stat.icon, {
                  size: 24,
                  color: '#fff',
                  stroke: 1.5
                })}
              </Box>
              <Stack spacing={1}>
                <Typography variant="subtitle2" color="textSecondary">
                  {stat.title}
                </Typography>
                <Stack direction="row" alignItems="center" spacing={1}>
                  <Typography variant="h5">{stat.amount}</Typography>
                  <Stack
                    direction="row"
                    alignItems="center"
                    spacing={0.5}
                    sx={{
                      padding: '1px 8px',
                      backgroundColor: stat.increase ? 'success.light' : 'error.light',
                      borderRadius: '4px',
                    }}
                  >
                    {stat.increase ? (
                      <IconArrowUpLeft size={16} color="#13DEB9" />
                    ) : (
                      <IconArrowDownRight size={16} color="#FA896B" />
                    )}
                    <Typography
                      variant="subtitle2"
                      color={stat.increase ? 'success.main' : 'error.main'}
                      fontSize="12px"
                      fontWeight="600"
                    >
                      {stat.percent}
                    </Typography>
                  </Stack>
                </Stack>
              </Stack>
            </Stack>
          </Card>
        </Grid>
      ))}
    </Grid>
  );
};

export default TopCards; 