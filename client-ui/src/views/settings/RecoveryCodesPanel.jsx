import { useState } from 'react';
import { Box, Typography, Stack, Button, FormControlLabel, Checkbox, Paper } from '@mui/material';

// Shows a one-time list of recovery codes with copy/download and an acknowledgement
// checkbox that gates the Done button. Used after enabling 2FA and after regenerating.
const RecoveryCodesPanel = ({ codes, onDone }) => {
    const [acknowledged, setAcknowledged] = useState(false);

    const asText = (codes || []).join('\n');

    const handleCopy = async () => {
        try {
            await navigator.clipboard.writeText(asText);
        } catch {
            // Clipboard may be unavailable (non-secure context) — the codes are visible regardless.
        }
    };

    const handleDownload = () => {
        const blob = new Blob([asText + '\n'], { type: 'text/plain' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'atomic-habits-recovery-codes.txt';
        a.click();
        URL.revokeObjectURL(url);
    };

    return (
        <Stack spacing={2}>
            <Typography variant="body2">
                Save these recovery codes somewhere safe. Each can be used once to sign in if you
                lose your authenticator. They will not be shown again.
            </Typography>
            <Paper variant="outlined" sx={{ p: 2 }}>
                <Box
                    sx={{
                        display: 'grid',
                        gridTemplateColumns: '1fr 1fr',
                        gap: 1,
                        fontFamily: 'monospace',
                        fontSize: 14,
                    }}
                >
                    {(codes || []).map((c) => (
                        <span key={c}>{c}</span>
                    ))}
                </Box>
            </Paper>
            <Stack direction="row" spacing={1}>
                <Button size="small" variant="outlined" onClick={handleCopy}>Copy</Button>
                <Button size="small" variant="outlined" onClick={handleDownload}>Download .txt</Button>
            </Stack>
            <FormControlLabel
                control={<Checkbox checked={acknowledged} onChange={(e) => setAcknowledged(e.target.checked)} />}
                label="I have saved these codes"
            />
            <Button
                variant="contained"
                disabled={!acknowledged}
                onClick={onDone}
            >
                Done
            </Button>
        </Stack>
    );
};

export default RecoveryCodesPanel;
