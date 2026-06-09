import React, { useState, useEffect } from 'react';
import {
  Box,
  Card,
  Typography,
  Button,
  Chip,
  Stack,
  IconButton,
  Accordion,
  AccordionSummary,
  AccordionDetails,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  TextField,
  MenuItem,
  CircularProgress,
  Divider,
} from '@mui/material';
import {
  IconChevronDown,
  IconPlus,
  IconEdit,
  IconTrash,
  IconTargetArrow,
} from '@tabler/icons-react';
import PageContainer from '../../components/container/PageContainer';
import { useGoals } from '../../context/GoalContext';
import { useHabits } from '../../context/HabitContext';
import { useSnackbar } from '../../context/SnackbarContext';
import HabitDialogForm from '../habit/components/HabitDialogForm';

const GOAL_STATUSES = ['Active', 'Completed', 'Paused', 'Abandoned'];
const MILESTONE_STATUSES = ['Active', 'Completed'];

const statusColor = (status) => {
  switch (status) {
    case 'Completed': return 'success';
    case 'Paused': return 'warning';
    case 'Abandoned': return 'default';
    default: return 'primary';
  }
};

const Goals = () => {
  const {
    visions, goals, loading,
    createVision, updateVision, deleteVision,
    createGoal, updateGoal, deleteGoal,
    listMilestones, createMilestone, updateMilestone, deleteMilestone,
  } = useGoals();
  const { habits, fetchHabits, createHabit } = useHabits();
  const { showSuccess, showError } = useSnackbar();

  // milestones cache keyed by goalId; null = not yet loaded
  const [milestonesByGoal, setMilestonesByGoal] = useState({});
  const [loadingMilestones, setLoadingMilestones] = useState({});

  // dialog state: { type: 'vision'|'goal'|'milestone', mode: 'create'|'edit', data }
  const [dialog, setDialog] = useState(null);
  const [form, setForm] = useState({});

  // "Add a habit toward this" dialog state: holds { milestone, goalTitle } or null
  const [addHabitTo, setAddHabitTo] = useState(null);

  // Load the habit list once so we can show habits linked to each milestone.
  useEffect(() => {
    fetchHabits?.();
  }, [fetchHabits]);

  // habits may be an array (fetchHabits) or { result: [...] } (searchHabits)
  const habitList = Array.isArray(habits) ? habits : (habits?.result || []);

  const habitsForMilestone = (milestoneId) =>
    habitList.filter((h) => h.milestoneId === milestoneId);

  const handleAddHabit = async (payload) => {
    try {
      await createHabit(payload);
      showSuccess('Habit added');
      setAddHabitTo(null);
      await fetchHabits?.();
    } catch (err) {
      showError('Failed to add habit');
    }
  };

  const loadMilestones = async (goalId) => {
    if (milestonesByGoal[goalId] !== undefined) return;
    setLoadingMilestones((p) => ({ ...p, [goalId]: true }));
    try {
      const ms = await listMilestones(goalId);
      setMilestonesByGoal((p) => ({ ...p, [goalId]: ms }));
    } catch (err) {
      showError('Failed to load milestones');
    } finally {
      setLoadingMilestones((p) => ({ ...p, [goalId]: false }));
    }
  };

  const refreshMilestones = async (goalId) => {
    try {
      const ms = await listMilestones(goalId);
      setMilestonesByGoal((p) => ({ ...p, [goalId]: ms }));
    } catch (err) {
      showError('Failed to refresh milestones');
    }
  };

  // ----- dialog openers -----
  const openVisionDialog = (mode, data) => {
    setForm(mode === 'edit'
      ? { title: data.title || '', description: data.description || '' }
      : { title: '', description: '' });
    setDialog({ type: 'vision', mode, data });
  };
  const openGoalDialog = (mode, data, visionId) => {
    setForm(mode === 'edit'
      ? { title: data.title || '', status: data.status || 'Active', visionId: data.visionId ?? '', targetDate: data.targetDate ? data.targetDate.slice(0, 10) : '' }
      : { title: '', status: 'Active', visionId: visionId ?? '', targetDate: '' });
    setDialog({ type: 'goal', mode, data });
  };
  const openMilestoneDialog = (mode, data, goalId) => {
    setForm(mode === 'edit'
      ? { title: data.title || '', status: data.status || 'Active', orderIndex: data.orderIndex ?? 0, goalId: data.goalId }
      : { title: '', status: 'Active', orderIndex: 0, goalId });
    setDialog({ type: 'milestone', mode, data });
  };

  const closeDialog = () => { setDialog(null); setForm({}); };

  const handleSave = async () => {
    if (!form.title || !form.title.trim()) {
      showError('Title is required');
      return;
    }
    try {
      if (dialog.type === 'vision') {
        const dto = { title: form.title.trim(), description: form.description?.trim() || null };
        if (dialog.mode === 'create') { await createVision(dto); showSuccess('Vision created'); }
        else { await updateVision(dialog.data.id, dto); showSuccess('Vision updated'); }
      } else if (dialog.type === 'goal') {
        const dto = {
          title: form.title.trim(),
          status: form.status || 'Active',
          visionId: form.visionId === '' ? null : Number(form.visionId),
          targetDate: form.targetDate ? new Date(form.targetDate).toISOString() : null,
        };
        if (dialog.mode === 'create') { await createGoal(dto); showSuccess('Goal created'); }
        else { await updateGoal(dialog.data.id, dto); showSuccess('Goal updated'); }
      } else if (dialog.type === 'milestone') {
        const goalId = form.goalId;
        const dto = {
          goalId,
          title: form.title.trim(),
          status: form.status || 'Active',
          orderIndex: Number(form.orderIndex) || 0,
        };
        if (dialog.mode === 'create') { await createMilestone(dto); showSuccess('Milestone created'); }
        else { await updateMilestone(dialog.data.id, dto); showSuccess('Milestone updated'); }
        await refreshMilestones(goalId);
      }
      closeDialog();
    } catch (err) {
      showError(err.response?.data?.errorMessages?.[0] || 'Save failed');
    }
  };

  const handleDeleteVision = async (id) => {
    try { await deleteVision(id); showSuccess('Vision deleted'); }
    catch { showError('Failed to delete vision'); }
  };
  const handleDeleteGoal = async (id) => {
    try { await deleteGoal(id); showSuccess('Goal deleted'); }
    catch { showError('Failed to delete goal'); }
  };
  const handleDeleteMilestone = async (m) => {
    try { await deleteMilestone(m.id); await refreshMilestones(m.goalId); showSuccess('Milestone deleted'); }
    catch { showError('Failed to delete milestone'); }
  };

  // ----- grouping -----
  const goalsByVision = (visionId) => goals.filter((g) => g.visionId === visionId);
  const unassignedGoals = goals.filter((g) => g.visionId == null);

  const renderGoal = (goal) => {
    const ms = milestonesByGoal[goal.id];
    return (
      <Accordion
        key={goal.id}
        disableGutters
        sx={{ bgcolor: 'background.default', '&:before': { display: 'none' }, mb: 1 }}
        onChange={(_, expanded) => { if (expanded) loadMilestones(goal.id); }}
      >
        <AccordionSummary expandIcon={<IconChevronDown size={18} />}>
          <Box display="flex" alignItems="center" gap={1} sx={{ width: '100%' }}>
            <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>{goal.title}</Typography>
            <Chip label={goal.status} size="small" color={statusColor(goal.status)} />
            <Box sx={{ ml: 'auto' }} onClick={(e) => e.stopPropagation()}>
              <IconButton size="small" onClick={() => openGoalDialog('edit', goal)}><IconEdit size={16} /></IconButton>
              <IconButton size="small" color="error" onClick={() => handleDeleteGoal(goal.id)}><IconTrash size={16} /></IconButton>
            </Box>
          </Box>
        </AccordionSummary>
        <AccordionDetails>
          <Box display="flex" justifyContent="space-between" alignItems="center" sx={{ mb: 1 }}>
            <Typography variant="body2" color="text.secondary">Milestones</Typography>
            <Button size="small" startIcon={<IconPlus size={14} />} onClick={() => openMilestoneDialog('create', null, goal.id)}>
              Add Milestone
            </Button>
          </Box>
          {loadingMilestones[goal.id] ? (
            <Box display="flex" justifyContent="center" sx={{ py: 2 }}><CircularProgress size={20} /></Box>
          ) : !ms || ms.length === 0 ? (
            <Typography variant="body2" color="text.secondary" sx={{ pl: 1 }}>No milestones yet.</Typography>
          ) : (
            <Stack spacing={1}>
              {ms.map((m) => {
                const linkedHabits = habitsForMilestone(m.id);
                return (
                  <Box key={m.id} sx={{ pl: 1 }}>
                    <Box display="flex" alignItems="center" gap={1}>
                      <Typography variant="body2">{m.title}</Typography>
                      <Chip label={m.status} size="small" color={statusColor(m.status)} variant="outlined" />
                      <Box sx={{ ml: 'auto' }}>
                        <IconButton size="small" onClick={() => openMilestoneDialog('edit', m, m.goalId)}><IconEdit size={14} /></IconButton>
                        <IconButton size="small" color="error" onClick={() => handleDeleteMilestone(m)}><IconTrash size={14} /></IconButton>
                      </Box>
                    </Box>
                    {linkedHabits.length > 0 ? (
                      <Stack direction="row" spacing={0.5} sx={{ pl: 1, mt: 0.5, flexWrap: 'wrap', gap: 0.5 }}>
                        {linkedHabits.map((h) => (
                          <Chip key={h.id} label={h.name} size="small" variant="outlined" />
                        ))}
                      </Stack>
                    ) : (
                      <Typography variant="caption" color="text.disabled" sx={{ pl: 1 }}>No habits yet.</Typography>
                    )}
                    <Button
                      size="small"
                      startIcon={<IconPlus size={14} />}
                      sx={{ ml: 0.5, mt: 0.5 }}
                      onClick={() => setAddHabitTo({ milestone: m, goalTitle: goal.title })}
                    >
                      Add a habit toward this
                    </Button>
                  </Box>
                );
              })}
            </Stack>
          )}
        </AccordionDetails>
      </Accordion>
    );
  };

  return (
    <PageContainer title="Goals" description="Vision, goals and milestones">
      <Box display="flex" alignItems="center" gap={1} sx={{ mb: 2 }}>
        <IconTargetArrow size={28} />
        <Typography variant="h4">Goals & Vision</Typography>
      </Box>

      <Stack direction="row" spacing={1} sx={{ mb: 3 }}>
        <Button variant="outlined" startIcon={<IconPlus />} onClick={() => openVisionDialog('create')}>
          Add Vision
        </Button>
        <Button variant="outlined" startIcon={<IconPlus />} onClick={() => openGoalDialog('create', null, '')}>
          Add Goal
        </Button>
      </Stack>

      {loading ? (
        <Box display="flex" justifyContent="center" sx={{ py: 4 }}><CircularProgress /></Box>
      ) : (
        <Stack spacing={2}>
          {visions.map((vision) => (
            <Card key={vision.id} sx={{ p: 2 }}>
              <Accordion disableGutters defaultExpanded sx={{ boxShadow: 'none', '&:before': { display: 'none' } }}>
                <AccordionSummary expandIcon={<IconChevronDown />}>
                  <Box display="flex" alignItems="center" gap={1} sx={{ width: '100%' }}>
                    <Typography variant="h6">{vision.title}</Typography>
                    <Box sx={{ ml: 'auto' }} onClick={(e) => e.stopPropagation()}>
                      <Button size="small" startIcon={<IconPlus size={14} />} onClick={() => openGoalDialog('create', null, vision.id)}>
                        Goal
                      </Button>
                      <IconButton size="small" onClick={() => openVisionDialog('edit', vision)}><IconEdit size={16} /></IconButton>
                      <IconButton size="small" color="error" onClick={() => handleDeleteVision(vision.id)}><IconTrash size={16} /></IconButton>
                    </Box>
                  </Box>
                </AccordionSummary>
                <AccordionDetails>
                  {vision.description && (
                    <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>{vision.description}</Typography>
                  )}
                  {goalsByVision(vision.id).length === 0 ? (
                    <Typography variant="body2" color="text.secondary">No goals under this vision.</Typography>
                  ) : (
                    goalsByVision(vision.id).map(renderGoal)
                  )}
                </AccordionDetails>
              </Accordion>
            </Card>
          ))}

          {/* Unassigned goals */}
          <Card sx={{ p: 2 }}>
            <Typography variant="h6" sx={{ mb: 1 }}>Unassigned</Typography>
            <Divider sx={{ mb: 2 }} />
            {unassignedGoals.length === 0 ? (
              <Typography variant="body2" color="text.secondary">No unassigned goals.</Typography>
            ) : (
              unassignedGoals.map(renderGoal)
            )}
          </Card>
        </Stack>
      )}

      {/* Shared dialog for vision / goal / milestone */}
      <Dialog open={!!dialog} onClose={closeDialog} maxWidth="sm" fullWidth>
        <DialogTitle>
          {dialog ? `${dialog.mode === 'create' ? 'Create' : 'Edit'} ${dialog.type.charAt(0).toUpperCase() + dialog.type.slice(1)}` : ''}
        </DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField
              label="Title"
              fullWidth
              value={form.title || ''}
              onChange={(e) => setForm((f) => ({ ...f, title: e.target.value }))}
            />
            {dialog?.type === 'vision' && (
              <TextField
                label="Description"
                fullWidth
                multiline
                rows={3}
                value={form.description || ''}
                onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
              />
            )}
            {dialog?.type === 'goal' && (
              <>
                <TextField
                  select
                  label="Vision"
                  fullWidth
                  value={form.visionId ?? ''}
                  onChange={(e) => setForm((f) => ({ ...f, visionId: e.target.value }))}
                >
                  <MenuItem value="">Unassigned</MenuItem>
                  {visions.map((v) => (
                    <MenuItem key={v.id} value={v.id}>{v.title}</MenuItem>
                  ))}
                </TextField>
                <TextField
                  select
                  label="Status"
                  fullWidth
                  value={form.status || 'Active'}
                  onChange={(e) => setForm((f) => ({ ...f, status: e.target.value }))}
                >
                  {GOAL_STATUSES.map((s) => (<MenuItem key={s} value={s}>{s}</MenuItem>))}
                </TextField>
                <TextField
                  label="Target Date"
                  type="date"
                  fullWidth
                  InputLabelProps={{ shrink: true }}
                  value={form.targetDate || ''}
                  onChange={(e) => setForm((f) => ({ ...f, targetDate: e.target.value }))}
                />
              </>
            )}
            {dialog?.type === 'milestone' && (
              <>
                <TextField
                  select
                  label="Status"
                  fullWidth
                  value={form.status || 'Active'}
                  onChange={(e) => setForm((f) => ({ ...f, status: e.target.value }))}
                >
                  {MILESTONE_STATUSES.map((s) => (<MenuItem key={s} value={s}>{s}</MenuItem>))}
                </TextField>
                <TextField
                  label="Order"
                  type="number"
                  fullWidth
                  value={form.orderIndex ?? 0}
                  onChange={(e) => setForm((f) => ({ ...f, orderIndex: e.target.value }))}
                />
              </>
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={closeDialog} color="inherit">Cancel</Button>
          <Button onClick={handleSave} variant="contained">Save</Button>
        </DialogActions>
      </Dialog>

      {/* Add-a-habit-toward-this-milestone dialog (pre-linked, milestone locked) */}
      <HabitDialogForm
        open={!!addHabitTo}
        onClose={() => setAddHabitTo(null)}
        onSubmit={handleAddHabit}
        isEditMode={false}
        lockedMilestoneId={addHabitTo?.milestone?.id ?? null}
        lockedMilestoneLabel={addHabitTo ? `${addHabitTo.goalTitle} › ${addHabitTo.milestone.title}` : ''}
      />
    </PageContainer>
  );
};

export default Goals;
