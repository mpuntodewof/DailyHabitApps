import { useState } from 'react';
import {
    Dialog, DialogTitle, DialogContent, DialogActions,
    Button, Stack, Typography, Chip, Box
} from '@mui/material';
import { useHabits } from '../../../context/HabitContext';
import { useSnackbar } from '../../../context/SnackbarContext';

const REASONS = [
    { value: 'Busy', label: 'Busy' },
    { value: 'Forgot', label: 'Forgot' },
    { value: 'LowEnergy', label: 'Low Energy' },
    { value: 'NoMotivation', label: 'No Motivation' },
    { value: 'ScheduleConflict', label: 'Schedule Conflict' },
    { value: 'Other', label: 'Other' },
];

const SkipHabitDialog = ({ open, onClose, habit }) => {
    const { skipHabit } = useHabits();
    const { showSuccess, showError } = useSnackbar();
    const [selectedReason, setSelectedReason] = useState(null);
    const [submitting, setSubmitting] = useState(false);

    const handleClose = () => {
        setSelectedReason(null);
        onClose();
    };

    const handleRecord = async () => {
        if (!selectedReason || !habit) return;
        setSubmitting(true);
        try {
            await skipHabit(habit.id, selectedReason);
            showSuccess('Skip recorded');
            setSelectedReason(null);
            onClose();
        } catch (err) {
            showError(err?.response?.data?.errorMessages?.[0] || 'Failed to record skip');
        } finally {
            setSubmitting(false);
        }
    };

    return (
        <Dialog open={open} onClose={handleClose} fullWidth maxWidth="xs">
            <DialogTitle>Skip today — {habit?.name || 'Habit'}</DialogTitle>
            <DialogContent>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                    Why are you skipping today? Pick a reason.
                </Typography>
                <Box>
                    <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', gap: 1 }}>
                        {REASONS.map((r) => (
                            <Chip
                                key={r.value}
                                label={r.label}
                                clickable
                                color={selectedReason === r.value ? 'primary' : 'default'}
                                variant={selectedReason === r.value ? 'filled' : 'outlined'}
                                onClick={() => setSelectedReason(r.value)}
                            />
                        ))}
                    </Stack>
                </Box>
            </DialogContent>
            <DialogActions>
                <Button onClick={handleClose} color="inherit">Cancel</Button>
                <Button
                    variant="contained"
                    onClick={handleRecord}
                    disabled={!selectedReason || submitting}
                >
                    {submitting ? 'Recording…' : 'Record skip'}
                </Button>
            </DialogActions>
        </Dialog>
    );
};

export default SkipHabitDialog;
