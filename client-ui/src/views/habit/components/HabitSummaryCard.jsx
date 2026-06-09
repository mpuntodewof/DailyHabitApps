import { Grid, Card, Stack, Box, Typography } from "@mui/material";
import {
    IconFlame,
    IconProgressCheck,
    IconCalendarStats,
    IconChartDonut
} from "@tabler/icons-react";

const HabitSummaryCards = ({ stats }) => {

    // Defensive: the API result may arrive with missing/null sub-objects (or
    // `stats` may be undefined on first render). Read every field through
    // optional chaining with a numeric fallback so a card never crashes the page.
    const today = stats?.todaySummary ?? {};
    const weekly = stats?.weeklySummary ?? {};
    const monthly = stats?.monthlySummary ?? {};
    const n = (v) => v ?? 0;

    // This Card Data is represent the summary statistics for all habits
    const cards = [
        {
            title: "Today's Progress",
            value: `${n(today.todayCompletionRate)}%`,
            subtitle: `${n(today.completedToday)}/${n(today.habitsToday)} habits`,
            icon: IconProgressCheck,
            color: "#6C4ED9"
        },
        {
            title: "Weekly Completion",
            value: `${n(weekly.weeklyCompletionRate)}%`,
            subtitle: `${n(weekly.totalCompletedThisWeek)} sessions this week`,
            icon: IconCalendarStats,
            color: "#5D87FF"
        },
        {
            title: "Monthly Summary",
            value: `${n(monthly.monthlyCompletionRate)}%`,
            subtitle: `${n(monthly.totalMonthlySessions)} total sessions`,
            icon: IconChartDonut,
            color: "#32CD32"
        },
        {
            title: "Habit Health Score",
            value: n(stats?.habitHealthScore),
            subtitle: "Overall habit performance",
            icon: IconFlame,
            color: "#FF6B6B"
        }
    ];

    return (
        <Grid container spacing={3} sx={{ mb: 4, pt: 3 }}
        >
            {cards.map((card, index) => (
                <Grid size={{ xs: 12, sm: 6, md: 3, lg: 3 }} key={index} sx={{ display: 'flex', justifyContent: 'center' }}>
                    <Card
                        sx={{
                            p: 3,
                            height: "120px",       // << FIXED HEIGHT
                            display: "flex",
                            alignItems: "center",
                            justifyContent: "flex-start",
                            boxShadow: 2,
                            borderRadius: "12px",
                            maxWidth: '350px',
                            minWidth: '350px',

                        }}
                    >
                        <Stack direction="row" spacing={2} alignItems="center">
                            {/* Icon */}
                            <Box
                                width={120}
                                height={65}
                                bgcolor={card.color}
                                display="flex"
                                alignItems="center"
                                justifyContent="center"
                                borderRadius="14px"
                                flexShrink={0}
                            >
                                <card.icon size={40} color="#fff" stroke={1.5} />
                            </Box>

                            {/* Text */}
                            <Box>
                                <Typography variant="subtitle2" color="textSecondary" sx={{ fontSize: "14px" }}
                                >
                                    {card.title}
                                </Typography>

                                <Typography variant="h4" sx={{ fontWeight: 700 }}
                                >
                                    {card.value}
                                </Typography>

                                <Typography variant="body2" color="text.secondary"
                                >
                                    {card.subtitle}
                                </Typography>
                            </Box>
                        </Stack>
                    </Card>
                </Grid>
            ))}
        </Grid>
    );
};

export default HabitSummaryCards;
