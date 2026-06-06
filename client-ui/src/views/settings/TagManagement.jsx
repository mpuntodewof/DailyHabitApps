import { useState } from 'react';
import {
    Card, CardContent, Typography, Stack, Chip, IconButton,
    TextField, Button, Box, Alert, CircularProgress, Dialog, DialogTitle, DialogContent, DialogActions
} from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import { SketchPicker } from 'react-color';
import { useTags } from '../../context/TagContext';

const TagManagement = () => {
    const { tags, loading, createTag, deleteTag } = useTags();

    const [name, setName] = useState('');
    const [color, setColor] = useState('#2196f3');
    const [colorOpen, setColorOpen] = useState(false);
    const [error, setError] = useState(null);
    const [busy, setBusy] = useState(false);

    const handleCreate = async () => {
        const trimmed = name.trim();
        if (!trimmed) return;
        setBusy(true);
        setError(null);
        try {
            await createTag(trimmed, color);
            setName('');
        } catch (err) {
            setError(err?.response?.data?.errorMessages?.[0] || 'Failed to create tag');
        } finally {
            setBusy(false);
        }
    };

    const handleDelete = async (tagId) => {
        try {
            await deleteTag(tagId);
        } catch (err) {
            setError(err?.response?.data?.errorMessages?.[0] || 'Failed to delete tag');
        }
    };

    return (
        <Card>
            <CardContent>
                <Typography variant="h3" mb={3}>Tags</Typography>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                    Create reusable tags to group your habits. Tags appear as colored chips on each habit card.
                </Typography>

                {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}

                <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', gap: 1, mb: 3 }}>
                    {loading && tags.length === 0 ? (
                        <CircularProgress size={20} />
                    ) : tags.length === 0 ? (
                        <Typography variant="body2" color="text.secondary">No tags yet.</Typography>
                    ) : (
                        tags.map((t) => (
                            <Chip
                                key={t.id}
                                label={t.name}
                                onDelete={() => handleDelete(t.id)}
                                deleteIcon={<DeleteIcon />}
                                sx={{
                                    bgcolor: t.color || undefined,
                                    color: t.color ? '#fff' : undefined,
                                    '& .MuiChip-deleteIcon': { color: t.color ? 'rgba(255,255,255,0.85)' : undefined }
                                }}
                            />
                        ))
                    )}
                </Stack>

                <Stack direction="row" spacing={2} alignItems="center" sx={{ flexWrap: 'wrap', gap: 2 }}>
                    <TextField
                        size="small"
                        label="Tag name"
                        value={name}
                        onChange={(e) => setName(e.target.value)}
                        sx={{ minWidth: 220 }}
                        inputProps={{ maxLength: 40 }}
                    />
                    <Box display="flex" alignItems="center" gap={1}>
                        <Box
                            onClick={() => setColorOpen(true)}
                            sx={{
                                width: 36,
                                height: 36,
                                borderRadius: 1,
                                bgcolor: color,
                                cursor: 'pointer',
                                border: '2px solid',
                                borderColor: 'divider',
                            }}
                        />
                        <Typography variant="body2" color="text.secondary">{color}</Typography>
                    </Box>
                    <Button variant="contained" onClick={handleCreate} disabled={busy || !name.trim()}>
                        Add tag
                    </Button>
                </Stack>

                <Dialog open={colorOpen} onClose={() => setColorOpen(false)} maxWidth="xs">
                    <DialogTitle>Choose tag color</DialogTitle>
                    <DialogContent>
                        <Box sx={{ p: 2 }}>
                            <SketchPicker
                                color={color}
                                onChange={(c) => setColor(c.hex)}
                                width="100%"
                            />
                        </Box>
                    </DialogContent>
                    <DialogActions>
                        <Button onClick={() => setColorOpen(false)}>Done</Button>
                    </DialogActions>
                </Dialog>
            </CardContent>
        </Card>
    );
};

export default TagManagement;
