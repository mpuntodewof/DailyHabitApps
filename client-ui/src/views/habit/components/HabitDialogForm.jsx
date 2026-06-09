import {
    Dialog,
    DialogTitle,
    DialogContent,
    DialogActions,
    TextField,
    Button,
    Select,
    MenuItem,
    FormControl,
    InputLabel,
    Grid,
    Box,
    Typography,
    IconButton,
    Divider
} from '@mui/material';
import { DatePicker } from '@mui/x-date-pickers/DatePicker';
import { LocalizationProvider } from '@mui/x-date-pickers/LocalizationProvider';
import { AdapterDayjs } from '@mui/x-date-pickers/AdapterDayjs';
import { IconX } from '@tabler/icons-react';
import { useCallback, useEffect, useState } from 'react';
import dayjs from 'dayjs';
import { useAuth } from '../../../context/AuthContext';
import { useGoals } from '../../../context/GoalContext';
import { useNavigate } from 'react-router';


const HabitDialogForm = ({ open, onClose, onSubmit, habit, isEditMode, lockedMilestoneId = null, lockedMilestoneLabel = '' }) => {
    const { user } = useAuth(); // Get user from AuthContext
    const { goals, listMilestones } = useGoals();
    const [selectedColor, setSelectedColor] = useState('');
    const [milestoneOptions, setMilestoneOptions] = useState([]); // [{ id, label }]
    const navigate = useNavigate();
    const [formData, setFormData] = useState({
        userId: 0,
        name: '',
        color: '',
        description: '',
        frequency: '',
        goalValue: 1,
        goalUnit: 'Times',
        goalFrequency: 'Per Day',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
    });

    useEffect(() => {
        if (habit && isEditMode) {
            setFormData({
                ...habit,
                userId: habit.userId || user?.userId || 0,
                createdAt: habit.createdAt || new Date().toISOString(),
                updatedAt: habit.updatedAt || new Date().toISOString()
            });
            setSelectedColor(habit.color || '#5D87FF');
        } else {
            setFormData({
                userId: user?.userId || 0,
                name: '',
                color: '#5D87FF',
                description: '',
                frequency: 'Daily',
                goalValue: 1,
                goalUnit: 'Times',
                goalFrequency: 'Per Day',
                milestoneId: lockedMilestoneId ?? undefined
            });
            setSelectedColor('#5D87FF');
        }
    }, [habit, open]);

    // Build the flattened "Goal › Milestone" option list when the form opens.
    useEffect(() => {
        if (!open) return;
        let cancelled = false;
        const buildOptions = async () => {
            try {
                const lists = await Promise.all(
                    (goals || []).map(async (g) => {
                        const ms = await listMilestones(g.id);
                        return ms.map((m) => ({ id: m.id, label: `${g.title} › ${m.title}` }));
                    })
                );
                if (!cancelled) setMilestoneOptions(lists.flat());
            } catch (err) {
                if (!cancelled) setMilestoneOptions([]);
            }
        };
        buildOptions();
        return () => { cancelled = true; };
    }, [open, goals, listMilestones]);

    const handleChange = (field, value) => {
        setFormData(prev => ({ ...prev, [field]: value }));
    };

    const handleSave = () => {
        const dateTimeNow = dayjs().toISOString();

        const payload = {
            ...formData,
            color: selectedColor,
            milestoneId: formData.milestoneId ? Number(formData.milestoneId) : null,
            createdAt: isEditMode ? dayjs(formData.createdAt).toISOString() : dateTimeNow,
            updatedAt: dateTimeNow
        }

        onSubmit(payload);
        // When opened from the Goals page (milestone locked), stay on the page
        // so the new habit can refresh in-place under its milestone.
        if (!lockedMilestoneId) {
            navigate('/habits');
        }
    }

    const colorOptions = [
        { value: '#A7C7FF', label: 'Sky Blue' },
        { value: '#A0E7E5', label: 'Mint Teal' },
        { value: '#C8B6FF', label: 'Lavender' },
        { value: '#FFD580', label: 'Light Orange' },
        { value: "#5D87FF", label: "Blue" },
        { value: "#9FFFAF", label: "Light Green" },
        { value: "#FA896B", label: "Red" },
    ];

    const useColorUtils = () => {
        const getContrastColor = useCallback((hexColor) => {
            const r = parseInt(hexColor.slice(1, 3), 16);
            const g = parseInt(hexColor.slice(3, 5), 16);
            const b = parseInt(hexColor.slice(5, 7), 16);
            const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
            return luminance > 0.5 ? "#000000" : "#FFFFFF";
        }, []);

        return { getContrastColor };
    };
    const { getContrastColor } = useColorUtils();


    return (
        <LocalizationProvider dateAdapter={AdapterDayjs}>
            <Dialog
                open={open}
                onClose={onClose}
                maxWidth="sm"
                fullWidth
            >
                <DialogTitle sx={{
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    pb: 1
                }}>
                    <Typography variant="h5" component="span" fontWeight="bold">
                        {isEditMode ? 'Edit Habit' : 'Create New Habit'}
                    </Typography>
                    <IconButton onClick={onClose} size="small" sx={{
                        textTransform: 'none',
                        fontWeight: 'medium',
                        '&:focus': {
                            outline: 'none',
                        },
                        '&:focus-visible': {
                            outline: 'none',
                        },
                    }}>
                        <IconX />
                    </IconButton>
                </DialogTitle>

                <Divider />

                <DialogContent sx={{ pt: 3 }}>
                    <Grid container spacing={3}>
                        {/* Name Field */}
                        <Grid item xs={12} width="100%">
                            <Box>
                                <Typography variant="body2" color="text.secondary" mb={1} fontWeight="medium">
                                    NAME
                                </Typography>
                                <Box display="flex" alignItems="center" gap={1}>
                                    <TextField
                                        fullWidth
                                        variant="outlined"
                                        size="medium"
                                        value={formData.name}
                                        onChange={e => handleChange('name', e.target.value)}
                                        sx={{
                                            '& .MuiOutlinedInput-root': {
                                                backgroundColor: '#f8f9fa'
                                            }
                                        }}
                                    />
                                </Box>
                            </Box>
                        </Grid>

                        {/* Goal and Repeat Row */}
                        <Grid item xs={6}>
                            <Typography variant="body2" color="text.secondary" mb={1} fontWeight="medium">
                                GOAL
                            </Typography>
                            <Box display="flex" gap={1}>
                                <FormControl size="medium" sx={{ minWidth: 100 }}>
                                    <InputLabel id="demo-simple-select-label">Reps</InputLabel>
                                    <Select
                                        label="Reps"
                                        sx={{ backgroundColor: '#f8f9fa' }}
                                        value={formData.goalValue}
                                        onChange={e => handleChange('goalValue', e.target.value)}
                                    >
                                        {[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map(num => (
                                            <MenuItem key={num} value={num}>{num}</MenuItem>
                                        ))}
                                    </Select>
                                </FormControl>
                                <FormControl size="medium" sx={{ minWidth: 100 }}>
                                    <InputLabel id="demo-simple-select-label">Times</InputLabel>
                                    <Select
                                        label="Times"
                                        sx={{ backgroundColor: '#f8f9fa' }}
                                        value={formData.goalUnit}
                                        onChange={e => handleChange('goalUnit', e.target.value)}
                                    >
                                        <MenuItem value="Times">Times</MenuItem>
                                        <MenuItem value="Minutes">Minutes</MenuItem>
                                        <MenuItem value="Hours">Hours</MenuItem>
                                    </Select>
                                </FormControl>
                                <FormControl size="medium" sx={{ minWidth: 100 }}>
                                    <InputLabel id="demo-simple-select-label">Per Day</InputLabel>
                                    <Select
                                        label="Per Day"
                                        sx={{ backgroundColor: '#f8f9fa' }}
                                        value={formData.goalFrequency}
                                        onChange={e => handleChange('goalFrequency', e.target.value)}
                                    >
                                        <MenuItem value="Per Day">Per Day</MenuItem>
                                        <MenuItem value="Per Week">Per Week</MenuItem>
                                        <MenuItem value="Per Month">Per Month</MenuItem>
                                    </Select>
                                </FormControl>
                            </Box>
                        </Grid>

                        {/* Frequency Field */}
                        <Grid item xs={6} minWidth={190}>
                            <Typography variant="body2" color="text.secondary" mb={1} fontWeight="medium">
                                REPEAT
                            </Typography>
                            <FormControl fullWidth size="medium">
                                <InputLabel id="demo-simple-select-label">Daily</InputLabel>
                                <Select
                                    sx={{ backgroundColor: '#f8f9fa' }}
                                    label="Daily"
                                    value={formData.frequency}
                                    onChange={e => handleChange('frequency', e.target.value)}
                                >
                                    <MenuItem value="Daily">Daily</MenuItem>
                                    <MenuItem value="Weekly">Weekly</MenuItem>
                                    <MenuItem value="Monthly">Monthly</MenuItem>
                                    <MenuItem value="Custom">Custom</MenuItem>
                                </Select>
                            </FormControl>
                        </Grid>

                        {/* Contributes To (milestone link) */}
                        <Grid item xs={12} width="100%">
                            <Typography variant="body2" color="text.secondary" mb={1} fontWeight="medium">
                                CONTRIBUTES TO
                            </Typography>
                            {lockedMilestoneId ? (
                                <TextField
                                    fullWidth
                                    size="medium"
                                    variant="outlined"
                                    disabled
                                    value={lockedMilestoneLabel || 'Linked milestone'}
                                    sx={{
                                        '& .MuiOutlinedInput-root': {
                                            backgroundColor: '#f8f9fa'
                                        }
                                    }}
                                />
                            ) : (
                                <FormControl fullWidth size="medium">
                                    <InputLabel id="contributes-to-label">Contributes to</InputLabel>
                                    <Select
                                        labelId="contributes-to-label"
                                        label="Contributes to"
                                        sx={{ backgroundColor: '#f8f9fa' }}
                                        value={formData.milestoneId ?? ''}
                                        onChange={e => handleChange('milestoneId', e.target.value)}
                                    >
                                        <MenuItem value="">None</MenuItem>
                                        {milestoneOptions.map((opt) => (
                                            <MenuItem key={opt.id} value={opt.id}>{opt.label}</MenuItem>
                                        ))}
                                    </Select>
                                </FormControl>
                            )}
                        </Grid>

                        {/* Start Date and Time of Day Fields */}
                        <Grid item xs={12} width="-webkit-fill-available">
                            <Box display="flex" gap={2}>
                                <Box flex={1}>
                                    <Typography variant="body2" color="text.secondary" mb={1} fontWeight="medium">
                                        START DATE
                                    </Typography>
                                    <DatePicker
                                        slotProps={{
                                            textField: {
                                                fullWidth: true,
                                                size: 'medium',
                                                sx: {
                                                    '& .MuiOutlinedInput-root': {
                                                        backgroundColor: '#f8f9fa'
                                                    }
                                                }
                                            }
                                        }}
                                    />
                                </Box>

                                <Box flex={1}>
                                    <Typography variant="body2" color="text.secondary" mb={1} fontWeight="medium">
                                        TIME OF DAY
                                    </Typography>
                                    <FormControl fullWidth size="medium">
                                        <Select sx={{ backgroundColor: '#f8f9fa' }} >
                                            <MenuItem value="Morning">Morning</MenuItem>
                                            <MenuItem value="Afternoon">Afternoon</MenuItem>
                                            <MenuItem value="Evening">Evening</MenuItem>
                                            <MenuItem value="Night">Night</MenuItem>
                                            <MenuItem value="Anytime">Anytime</MenuItem>
                                        </Select>
                                    </FormControl>
                                </Box>

                            </Box>
                        </Grid>

                        {/* Reminders Field */}
                        <Grid item xs={12} width="100%">
                            <Typography variant="body2" color="text.secondary" mb={1} fontWeight="medium">
                                NOTES
                            </Typography>
                            <TextField
                                fullWidth
                                multiline
                                rows={3}
                                column={6}
                                variant="outlined"
                                placeholder="Add reminder notes..."
                                sx={{
                                    '& .MuiOutlinedInput-root': {
                                        backgroundColor: '#f8f9fa'
                                    }
                                }}
                                value={formData.description}
                                onChange={e => handleChange('description', e.target.value)}
                            />
                        </Grid>

                        {/* Color Picker */}
                        <Grid item xs={12} width="100%">
                            <Typography variant="body2" color="text.secondary" mb={1} fontWeight="medium">
                                COLOR
                            </Typography>
                            <Box display="flex" flexDirection="row" gap={2} alignItems="center" mt={1}>
                                {colorOptions.map((color) => (
                                    <div
                                        key={color.value}
                                        title={color.name}
                                        style={{
                                            width: 32,
                                            height: 32,
                                            borderRadius: "50%",
                                            backgroundColor: color.value,
                                            border:
                                                selectedColor === color.value
                                                    ? "3px solid black"
                                                    : "2px solid gray",
                                            cursor: "pointer",
                                        }}
                                        onClick={() => setSelectedColor(color.value)}
                                    />
                                ))}
                                <span
                                    style={{
                                        marginLeft: "16px",
                                        padding: "6px 12px",
                                        background: selectedColor,
                                        color: getContrastColor(selectedColor),
                                        borderRadius: "12px",
                                    }}
                                >
                                    {selectedColor}
                                </span>
                            </Box>
                        </Grid>
                    </Grid>
                </DialogContent>

                <Divider />

                {/* Button Action Section */}
                <DialogActions sx={{ justifyContent: 'space-between', p: 3, pt: 2 }}>
                    <Box display="flex" gap={1}>
                        <Button
                            color="error"
                            variant="outlined"
                            sx={{
                                textTransform: 'none',
                                fontWeight: 'medium',
                                '&:focus': {
                                    outline: 'none',
                                },
                                '&:focus-visible': {
                                    outline: 'none',
                                },
                            }}
                        >
                            Delete
                        </Button>
                    </Box>
                    <Box display="flex" gap={1}>
                        <Button
                            onClick={onClose}
                            color="inherit"
                            variant="outlined"
                            sx={{
                                textTransform: 'none',
                                fontWeight: 'medium',
                                '&:focus': {
                                    outline: 'none',
                                },
                                '&:focus-visible': {
                                    outline: 'none',
                                },
                            }}
                        >
                            Cancel
                        </Button>
                        <Button
                            color="primary"
                            variant="contained"
                            onClick={handleSave}
                            sx={{
                                textTransform: 'none',
                                fontWeight: 'medium',
                                '&:focus': {
                                    outline: 'none',
                                },
                                '&:focus-visible': {
                                    outline: 'none',
                                },
                            }}
                        >
                            {isEditMode ? 'Update' : 'Save'}
                        </Button>
                    </Box>
                </DialogActions>
            </Dialog>
        </LocalizationProvider>
    );
};

export default HabitDialogForm;