import { useEffect, useState } from 'react';
import {
    Dialog, DialogTitle, DialogContent, DialogActions,
    Button, Stack, Typography, Box, IconButton, Switch, FormGroup,
    FormControlLabel, Checkbox, TextField, List, ListItem, ListItemText,
    ListItemSecondaryAction, Alert, Divider, CircularProgress
} from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import api from '../../../api/axiosInstance';

const DAY_CODES = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

const parseDays = (csv) => {
    if (!csv) return new Set();
    return new Set(csv.split(',').map((s) => s.trim()).filter(Boolean));
};

const stringifyDays = (set) =>
    DAY_CODES.filter((d) => set.has(d)).join(',');

const HabitRemindersDialog = ({ open, habitId, habitName, onClose }) => {
    const [items, setItems] = useState([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(null);

    const [time, setTime] = useState('09:00');
    const [days, setDays] = useState(() => new Set(['Mon', 'Tue', 'Wed', 'Thu', 'Fri']));
    const [enabled, setEnabled] = useState(true);
    const [creating, setCreating] = useState(false);

    const fetchAll = async () => {
        setLoading(true);
        setError(null);
        try {
            const res = await api.get('/HabitReminder');
            const all = res.data?.result || [];
            setItems(all.filter((r) => r.habitId === habitId));
        } catch (err) {
            setError(err?.response?.data?.errorMessages?.[0] || 'Failed to load reminders');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        if (open && habitId) fetchAll();
    }, [open, habitId]);

    const handleCreate = async () => {
        setCreating(true);
        setError(null);
        try {
            const [hh, mm] = time.split(':').map(Number);
            const reminderTime = `${String(hh).padStart(2, '0')}:${String(mm).padStart(2, '0')}:00`;
            await api.post('/HabitReminder', {
                habitId,
                reminderTime,
                daysOfWeek: stringifyDays(days),
                isEnabled: enabled,
            });
            await fetchAll();
        } catch (err) {
            setError(err?.response?.data?.errorMessages?.[0] || 'Failed to create reminder');
        } finally {
            setCreating(false);
        }
    };

    const handleToggle = async (reminder) => {
        try {
            await api.put(`/HabitReminder/${reminder.id}`, {
                habitId,
                reminderTime: reminder.reminderTime + ':00',
                daysOfWeek: reminder.daysOfWeek,
                isEnabled: !reminder.isEnabled,
            });
            await fetchAll();
        } catch (err) {
            setError(err?.response?.data?.errorMessages?.[0] || 'Failed to update reminder');
        }
    };

    const handleDelete = async (id) => {
        try {
            await api.delete(`/HabitReminder/${id}`);
            await fetchAll();
        } catch (err) {
            setError(err?.response?.data?.errorMessages?.[0] || 'Failed to delete reminder');
        }
    };

    const toggleDay = (code) => {
        const next = new Set(days);
        if (next.has(code)) next.delete(code); else next.add(code);
        setDays(next);
    };

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
            <DialogTitle>Reminders — {habitName || 'Habit'}</DialogTitle>
            <DialogContent>
                {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

                {loading ? (
                    <Box display="flex" justifyContent="center" py={3}><CircularProgress size={28} /></Box>
                ) : items.length === 0 ? (
                    <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                        No reminders yet. Add one below.
                    </Typography>
                ) : (
                    <List dense>
                        {items.map((r) => (
                            <ListItem key={r.id} divider>
                                <ListItemText
                                    primary={`${r.reminderTime} — ${r.daysOfWeek || 'Every day'}`}
                                    secondary={r.lastFiredOn ? `Last fired: ${r.lastFiredOn}` : 'Not fired yet'}
                                />
                                <ListItemSecondaryAction>
                                    <Switch
                                        size="small"
                                        checked={!!r.isEnabled}
                                        onChange={() => handleToggle(r)}
                                    />
                                    <IconButton edge="end" size="small" onClick={() => handleDelete(r.id)} aria-label="delete">
                                        <DeleteIcon fontSize="small" />
                                    </IconButton>
                                </ListItemSecondaryAction>
                            </ListItem>
                        ))}
                    </List>
                )}

                <Divider sx={{ my: 2 }} />
                <Typography variant="subtitle2" sx={{ mb: 1 }}>Add reminder</Typography>

                <Stack spacing={2}>
                    <TextField
                        type="time"
                        label="Time (UTC)"
                        size="small"
                        value={time}
                        onChange={(e) => setTime(e.target.value)}
                        InputLabelProps={{ shrink: true }}
                        sx={{ maxWidth: 180 }}
                    />
                    <FormGroup row>
                        {DAY_CODES.map((d) => (
                            <FormControlLabel
                                key={d}
                                control={<Checkbox size="small" checked={days.has(d)} onChange={() => toggleDay(d)} />}
                                label={d}
                            />
                        ))}
                    </FormGroup>
                    <FormControlLabel
                        control={<Switch checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />}
                        label="Enabled"
                    />
                </Stack>
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose}>Close</Button>
                <Button variant="contained" onClick={handleCreate} disabled={creating}>
                    {creating ? 'Adding…' : 'Add Reminder'}
                </Button>
            </DialogActions>
        </Dialog>
    );
};

export default HabitRemindersDialog;
