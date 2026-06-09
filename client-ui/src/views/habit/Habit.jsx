import React, { useEffect, useCallback } from 'react';
import { useState } from 'react';
import { Grid, Box, Card, Typography, Stack, Checkbox, Button, CircularProgress, Chip, TextField, MenuItem, FormControlLabel, Switch, Pagination } from '@mui/material';
import { IconFlame, IconPlus, IconProgressCheck, IconMapPinFilled, IconClockHour1 } from '@tabler/icons-react';
import { useHabits } from '../../context/HabitContext';
import { useAuth } from '../../context/AuthContext';
import { useSnackbar } from '../../context/SnackbarContext';
import { useHabitTracking } from '../../context/HabitTrackingContext';
import { useTags } from '../../context/TagContext';
import api from '../../api/axiosInstance';
import CircleCheckedFilled from '@mui/icons-material/CheckCircle';
import CircleUnchecked from '@mui/icons-material/RadioButtonUnchecked';

import HabitMenuButton from './components/HabitMenuButton';
import HabitTrackingDialog from './components/HabitTrackingDialog';
import HabitSummaryCards from "./components/HabitSummaryCard";
import PageContainer from "../../components/container/PageContainer";
import HabitDialogForm from './components/HabitDialogForm';
import HabitTimeDialog from './components/HabitTimeDialog';
import HabitRemindersDialog from './components/HabitRemindersDialog';
import SkipHabitDialog from './components/SkipHabitDialog';


const Habit = () => {
    const [open, setOpen] = useState(false);
    const { habits, loading, pagination, createHabit, fetchHabits, searchHabits, updateHabit, deleteHabit, archiveHabit, restoreHabit } = useHabits();
    const { submitHabitTracking, getHabitStats, submitDailyHabit } = useHabitTracking();
    const { tags } = useTags();
    const { user } = useAuth();

    const [search, setSearch] = useState('');
    const [tagFilter, setTagFilter] = useState('');
    const [includeArchived, setIncludeArchived] = useState(false);
    const [page, setPage] = useState(1);
    const pageSize = 20;

    const [reminderDialog, setReminderDialog] = useState({ open: false, habitId: null, habitName: '' });
    const [skipTarget, setSkipTarget] = useState(null);

    const handleOpen = () => setOpen(true);
    const [openTrackingDialog, setOpenTrackingDialog] = useState(false);
    const [openTimeDialog, setOpenTimeDialog] = useState(false);
    const [pendingHabitId, setPendingHabitId] = useState(null);
    const { showError, showSuccess } = useSnackbar();
    const [editMode, setEditMode] = useState(false);
    const [selectedHabit, setSelectedHabit] = useState(null);
    const [selectedCardHabit, setSelectedCardHabit] = useState(null);
    const [habitStats, setHabitStats] = useState({});
    const [contributions, setContributions] = useState({}); // habitId -> HabitContributionDto


    useEffect(() => {
        if (!user?.sub) return;
        searchHabits({
            search,
            tagId: tagFilter ? Number(tagFilter) : null,
            includeArchived,
            page,
            pageSize,
        });
    }, [searchHabits, user?.sub, search, tagFilter, includeArchived, page]);

    const fetchAllStats = useCallback(async () => {
        if (!habits?.result || habits.result.length === 0 || !user?.sub) return;
        const statsMap = {};
        for (const habit of habits.result) {
            try {
                const res = await getHabitStats(habit.id, user.sub);
                if (res?.result) {
                    statsMap[habit.id] = res.result;
                }
            } catch (e) {
                console.error("Error fetching stats for habit", habit.id, e);
            }
        }
        setHabitStats(statsMap);
    }, [habits?.result, getHabitStats, user?.sub]);

    useEffect(() => { fetchAllStats(); }, [fetchAllStats]);

    // Fetch the goal/milestone payoff line, only for habits linked to a milestone.
    useEffect(() => {
        if (!habits?.result || habits.result.length === 0) return;
        const linked = habits.result.filter((h) => h.milestoneId);
        if (linked.length === 0) return;

        let cancelled = false;
        const fetchContributions = async () => {
            const map = {};
            for (const habit of linked) {
                try {
                    const res = await api.get(`/Habit/${habit.id}/contribution`);
                    if (res.data?.result) map[habit.id] = res.data.result;
                } catch (e) {
                    console.error('Error fetching contribution for habit', habit.id, e);
                }
            }
            if (!cancelled) setContributions(map);
        };
        fetchContributions();
        return () => { cancelled = true; };
    }, [habits?.result]);

    // Real, server-computed summary. Defaults to zeros so a new user with no
    // habits sees an honest empty state (not fake placeholder numbers).
    const EMPTY_SUMMARY = {
        todaySummary: { habitsToday: 0, completedToday: 0, todayCompletionRate: 0 },
        weeklySummary: { weeklyCompletionRate: 0, totalCompletedThisWeek: 0 },
        monthlySummary: { monthlyCompletionRate: 0, totalMonthlySessions: 0 },
        habitHealthScore: 0,
    };
    const [habitSummaryStats, setHabitSummaryStats] = useState(EMPTY_SUMMARY);

    const fetchSummary = useCallback(async () => {
        if (!user?.sub) return;
        try {
            const res = await api.get(`/Habit/habits-summary/${user.sub}`);
            if (res.data?.result) setHabitSummaryStats(res.data.result);
        } catch (err) {
            console.error('Failed to load habit summary:', err.message);
        }
    }, [user?.sub]);

    // Refresh the summary when the page loads / the habit list changes.
    useEffect(() => { fetchSummary(); }, [fetchSummary, habits?.result]);


    const handleOpenTrackingDialog = (habitId) => {
        setSelectedCardHabit(habitId);
        setOpenTrackingDialog(true);
    };

    const handleCloseTrackingDialog = () => setOpenTrackingDialog(false);

    const handleDeleteHabit = async (habitId) => {
        try {
            await deleteHabit(habitId);
            showSuccess('Habit deleted successfully!');
        } catch (error) {
            console.error('Failed to delete habit:', error);
        }
    }

    const handleArchiveHabit = async (habitId) => {
        try {
            await archiveHabit(habitId);
            showSuccess('Habit archived');
        } catch (error) {
            showError('Failed to archive habit');
        }
    }

    const handleRestoreHabit = async (habitId) => {
        try {
            await restoreHabit(habitId);
            showSuccess('Habit restored');
        } catch (error) {
            showError('Failed to restore habit');
        }
    }

    const handleSubmitHabit = async (habit) => {
        try {
            let res;
            if (editMode) {
                res = await updateHabit(habit.id, habit);
                if (res.status === 200) {
                    showSuccess('Habit updated successfully!');
                    // Optionally update local state
                } else {
                    console.log('Update Habit Response Error: ', res);
                    showError('Failed to update habit.');
                }
            } else {
                res = await createHabit(habit);
                if (res.status === 200) {
                    showSuccess('Habit created successfully!');
                    // Optionally update local state
                } else {
                    console.log('Create Habit Response Error: ', res);
                    showError('Failed to create habit.');
                }
            }
            searchHabits({
                search,
                tagId: tagFilter ? Number(tagFilter) : null,
                includeArchived,
                page,
                pageSize,
            });
            setOpen(false);
            setEditMode(false);
            setSelectedCardHabit(null);
        } catch (error) {
            console.log('Habit creation failed:', error);
            showError("An error occurred while saving the habit.");
        }
    }

    const handleSaveTracking = async (formData) => {
        try {
            const trackingPayload = {
                ...formData,
                userId: user.sub,
            }
            const response = await submitHabitTracking(trackingPayload);
            console.log('Habit tracking response:', response);
            if (response.status == 200) {
                showSuccess('Habit tracking saved successfully!');
                fetchAllStats(); // refresh per-habit stats (checkbox/completion) live
                fetchSummary(); // live-update the summary cards after logging progress
            } else {
                showError(response.data.errorMessages || 'Failed to submit habit tracking.');
            }
        } catch (error) {
            console.error('Failed to save habit tracking:', error.message);
            showError(error.response?.data?.errorMessages || error.message || 'An error occurred while submitting habit tracking.');
        }
    }

    const handleEditHabit = (habit) => {
        setSelectedHabit(habit);
        setEditMode(true);
        setOpen(true);
    };

    // Show time dialog after marking complete
    const handleMarkCompletion = (habitId) => {
        setPendingHabitId(habitId);
        setOpenTimeDialog(true);
    };

    // Called after time entry
    const handleSaveTimeSpent = async (minutes) => {
        setOpenTimeDialog(false);
        if (!pendingHabitId) return;

        try {
            // Submit completion with time spent (use correct DB field: TimeSpentMinutes)
            const res = await submitDailyHabit(pendingHabitId, {}, minutes);
            if (res.status === 200) {
                // Resolve the habit's identity for an "identity-vote" confirmation hit.
                // Additive: only replaces the default toast when an identity exists.
                let identity = null;
                const habitName = habits?.result?.find((h) => h.id === pendingHabitId)?.name;
                try {
                    identity = contributions[pendingHabitId]?.identityTitle ?? null;
                    if (!identity) {
                        const contrib = (await api.get(`/Habit/${pendingHabitId}/contribution`)).data?.result;
                        identity = contrib?.identityTitle ?? null;
                    }
                } catch (e) {
                    console.error('Error fetching contribution for identity-vote toast', e);
                    identity = null;
                }

                if (identity && habitName) {
                    showSuccess(`✓ ${habitName} — A vote for ${identity} 🗳️`);
                } else {
                    showSuccess('Habit marked as complete for today!');
                }
                // Optimistically check the box immediately, then reconcile with the
                // server (so completionRate/streak stay accurate). Previously the
                // checkbox only updated on a full page refresh.
                setHabitStats((prev) => ({
                    ...prev,
                    [pendingHabitId]: { ...(prev[pendingHabitId] || {}), completedToday: true },
                }));
                fetchAllStats();
                fetchSummary(); // live-update the summary cards after completion
            } else {
                showError('Failed to mark habit as complete.');
            }
        } catch (error) {
            console.error('Error marking habit as complete:', error);
            showError('Failed to mark habit as complete.');
        }
        setPendingHabitId(null);
    };

    return (
        <PageContainer title="Habits" description="Habit">
            <Typography variant="h4" gutterBottom>
                Create and Manage Your Habits
            </Typography>
            <Box>               

                <HabitSummaryCards stats={habitSummaryStats} />

                {/* Button Create Habit Section */}
                <Grid container paddingTop={4} display={'block'}>
                    <Grid item xs={12}>
                        <Button onClick={handleOpen} variant="outlined" color="primary" startIcon={<IconPlus />} fullWidth sx={{
                            '&:focus': {
                                outline: 'none',
                                boxShadow: 'none'
                            },
                            '&:focus-visible': {
                                outline: 'none',
                                boxShadow: 'none'
                            }
                        }}>
                            Add New Habit
                        </Button>
                    </Grid>
                </Grid>

                {/* Filter Toolbar */}
                <Box display="flex" flexWrap="wrap" gap={2} alignItems="center" sx={{ mt: 3, mb: 2 }}>
                    <TextField
                        size="small"
                        label="Search"
                        value={search}
                        onChange={(e) => { setSearch(e.target.value); setPage(1); }}
                        sx={{ minWidth: 220 }}
                    />
                    <TextField
                        size="small"
                        select
                        label="Tag"
                        value={tagFilter}
                        onChange={(e) => { setTagFilter(e.target.value); setPage(1); }}
                        sx={{ minWidth: 180 }}
                    >
                        <MenuItem value="">All tags</MenuItem>
                        {tags.map((t) => (
                            <MenuItem key={t.id} value={t.id}>{t.name}</MenuItem>
                        ))}
                    </TextField>
                    <FormControlLabel
                        control={
                            <Switch
                                checked={includeArchived}
                                onChange={(e) => { setIncludeArchived(e.target.checked); setPage(1); }}
                            />
                        }
                        label="Show archived"
                    />
                    {pagination?.total > 0 && (
                        <Typography variant="body2" color="text.secondary" sx={{ ml: 'auto' }}>
                            {pagination.total} {pagination.total === 1 ? 'habit' : 'habits'}
                        </Typography>
                    )}
                </Box>

                {/* Habit Card Section*/}
                {loading ? (
                    <Grid item xs={12} display="flex" justifyContent="center" alignItems="center">
                        <CircularProgress color="primary" />
                    </Grid>
                ) : !habits?.result || habits.result.length === 0 ? (
                    <Grid item xs={12}>
                        <Card sx={{ padding: 3, height: '100%', width: '100%', textAlign: 'center' }}>
                            <Typography variant="h6" color="textSecondary">
                                No habits found. Please add a new habit.
                            </Typography>
                        </Card>
                    </Grid>
                ) : (
                    habits.result.map((habit, index) => {
                        const progress = habitStats[habit.id]?.completionRate || 0;
                        const monthlyGoal = habitStats[habit.id]?.monthlyGoal || 0;
                        console.log("HabitStats: ", habitStats[habit.id], habit);

                        return (
                            <Grid container spacing={3} sx={{ pt: 2 }} key={index}>
                                <Grid item xs={12} sx={{ width: '100%' }}>
                                    <Card sx={{ 
                                            p: 3, height: '100%', width: '100%', boxShadow: 3, cursor: 'pointer', transition: "0.2s",
                                            '&:hover': { boxShadow: 6, transform: "translateY(-2px)" }
                                        }}
                                        onClick={() => handleOpenTrackingDialog(habit.id)}
                                    >
                                        <Box display="flex" flexDirection="row" alignItems="center" sx={{ width: '100%' }}>
                                            {/* Checkbox on the left, large, triggers HabitTrackingDialog */}
                                            <Checkbox
                                                size="large"
                                                icon={<CircleUnchecked fontSize="large" />}
                                                checkedIcon={<CircleCheckedFilled fontSize="large" />}
                                                sx={{ mr: 2 }}
                                                checked={habitStats[habit.id]?.completedToday === true}
                                                disabled={habitStats[habit.id]?.completedToday === true}
                                                onClick={(e) => {
                                                    e.stopPropagation();
                                                    handleMarkCompletion(habit.id);
                                                }}
                                            />
                                            {/* Title & Subtitle */}
                                            <Box display="flex" flexDirection="column" alignItems="flex-start" sx={{ flex: 2 }}>
                                                <Typography variant="h3" color="#1E293B" sx={{ fontWeight: 600 }}>
                                                    {habit.name}
                                                </Typography>
                                                <Typography variant="body1" color="text.secondary" sx={{ mb: 1 }}>
                                                    {habit.description}
                                                </Typography>
                                                {contributions[habit.id] && (
                                                    <Typography variant="body2" color="text.disabled" sx={{ mb: 1, fontStyle: 'italic' }}>
                                                        Contributes to: {contributions[habit.id].goalTitle
                                                            || contributions[habit.id].milestoneTitle
                                                            || contributions[habit.id].visionTitle}
                                                    </Typography>
                                                )}
                                                {Array.isArray(habit.habitTags) && habit.habitTags.length > 0 && (
                                                    <Stack direction="row" spacing={0.5} sx={{ mt: 0.5, flexWrap: 'wrap', gap: 0.5 }} onClick={(e) => e.stopPropagation()}>
                                                        {habit.habitTags.map((ht) => (
                                                            <Chip
                                                                key={ht.tag?.id ?? ht.tagId}
                                                                label={ht.tag?.name ?? ''}
                                                                size="small"
                                                                sx={{
                                                                    bgcolor: ht.tag?.color || 'default',
                                                                    color: ht.tag?.color ? '#fff' : undefined,
                                                                }}
                                                            />
                                                        ))}
                                                    </Stack>
                                                )}
                                            </Box>
                                            {/* Right: Menu Button */}
                                            <Box display="flex" alignItems="center" sx={{ flex: 0 }}>
                                                <HabitMenuButton
                                                    onEdit={() => handleEditHabit(habit)}
                                                    onDelete={() => handleDeleteHabit(habit.id)}
                                                    onArchive={() => handleArchiveHabit(habit.id)}
                                                    onRestore={() => handleRestoreHabit(habit.id)}
                                                    onReminders={() => setReminderDialog({ open: true, habitId: habit.id, habitName: habit.name })}
                                                    onSkip={() => setSkipTarget(habit)}
                                                    isArchived={!!habit.isArchived}
                                                />
                                            </Box>
                                        </Box>

                                        {/* Goal Value, Goal Unit, Goal Frequency with icons */}
                                        <Box display="flex" flexDirection="row" alignItems="center" gap={2} sx={{ mt: 2 }}>
                                            <Box display="flex" alignItems="center" gap={1}>
                                                <IconProgressCheck size={20} color="#6c4ed9" />
                                                <Typography variant="body2" color="text.secondary">Current Progress: {habit.goalValue}</Typography>
                                            </Box>
                                            <Box display="flex" alignItems="center" gap={1}>
                                                <IconMapPinFilled size={20} color="#d94e4e" />
                                                <Typography variant="body2" color="text.secondary">Monthly Goal: {monthlyGoal}</Typography>
                                            </Box>
                                            <Box display="flex" alignItems="center" gap={1}>
                                                <IconClockHour1 size={20} color="#32CD32" />
                                                <Typography variant="body2" color="text.secondary">Goal Frequency: {habit.goalFrequency}</Typography>
                                            </Box>
                                        </Box>

                                        {/* Progress Bar & Percentage */}
                                        <Box display="flex" alignItems="center" gap={2} sx={{ mt: 2 }}>
                                            <Box sx={{ flex: 1 }}>
                                                <Box sx={{ width: '100%', bgcolor: '#f0f0f0', borderRadius: 2, height: 8 }}>
                                                    {progress > 0 ? (
                                                        <Box sx={{ width: `${progress}%`, bgcolor: '#6c4ed9', height: 8, borderRadius: 2 }} />
                                                    ) : (
                                                        <Box sx={{ width: '100%', bgcolor: '#fff', height: 8, borderRadius: 2 }} />
                                                    )}
                                                </Box>
                                            </Box>
                                            <Typography variant="body2" color="text.secondary">{progress}%</Typography>
                                        </Box>
                                    </Card>
                                </Grid>
                            </Grid>
                        )
                    })
                )}

                {/* Habit Dialog Form Section */}
                <HabitDialogForm
                    open={open}
                    onClose={() => { setOpen(false); setEditMode(false); setSelectedHabit(null); }}
                    onSubmit={handleSubmitHabit}
                    habit={selectedHabit}
                    isEditMode={editMode}
                />

                {pagination?.total > pageSize && (
                    <Box display="flex" justifyContent="center" sx={{ mt: 3 }}>
                        <Pagination
                            count={Math.ceil(pagination.total / pageSize)}
                            page={page}
                            onChange={(_, value) => setPage(value)}
                            color="primary"
                        />
                    </Box>
                )}

                {/* <pre>{JSON.stringify(habits, null, 2)}</pre> */}
                {/* <pre>{JSON.stringify(selectedCardHabit, null, 2)}</pre> */}

                <HabitTrackingDialog
                    open={openTrackingDialog}
                    onClose={handleCloseTrackingDialog}
                    habitId={selectedCardHabit}
                    habits={habits?.result || []}
                    onSave={handleSaveTracking}
                />

                {/* Time Entry Dialog for Habit Completion */}
                <HabitTimeDialog
                    open={openTimeDialog}
                    onClose={() => { setOpenTimeDialog(false); setPendingHabitId(null); }}
                    onSave={handleSaveTimeSpent}
                />

                <HabitRemindersDialog
                    open={reminderDialog.open}
                    habitId={reminderDialog.habitId}
                    habitName={reminderDialog.habitName}
                    onClose={() => setReminderDialog({ open: false, habitId: null, habitName: '' })}
                />

                <SkipHabitDialog
                    open={!!skipTarget}
                    habit={skipTarget}
                    onClose={() => setSkipTarget(null)}
                    onRecorded={fetchSummary}
                />

            </Box >
        </PageContainer >
    );
}

export default Habit;