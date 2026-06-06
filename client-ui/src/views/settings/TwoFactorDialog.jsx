import { useEffect, useState } from 'react';
import {
    Dialog, DialogTitle, DialogContent, DialogActions,
    Button, Typography, TextField, Box, Stack, Alert, CircularProgress
} from '@mui/material';
import { QRCodeSVG } from 'qrcode.react';
import api from '../../api/axiosInstance';

const TwoFactorDialog = ({ open, mode, onClose, onChanged }) => {
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(null);
    const [secret, setSecret] = useState(null);
    const [otpauthUri, setOtpauthUri] = useState(null);
    const [code, setCode] = useState('');

    useEffect(() => {
        if (!open) return;
        setError(null);
        setCode('');
        if (mode === 'enable') {
            (async () => {
                setLoading(true);
                try {
                    const res = await api.post('/TwoFactor/enable-init');
                    setSecret(res.data?.result?.secret || null);
                    setOtpauthUri(res.data?.result?.otpauthUri || null);
                } catch (err) {
                    setError(err?.response?.data?.errorMessages?.[0] || 'Failed to start 2FA enrollment');
                } finally {
                    setLoading(false);
                }
            })();
        } else {
            setSecret(null);
            setOtpauthUri(null);
        }
    }, [open, mode]);

    const handleConfirm = async () => {
        setLoading(true);
        setError(null);
        try {
            if (mode === 'enable') {
                await api.post('/TwoFactor/enable-confirm', { code: code.trim() });
            } else {
                await api.post('/TwoFactor/disable', { code: code.trim() });
            }
            onChanged?.();
            onClose();
        } catch (err) {
            setError(err?.response?.data?.errorMessages?.[0] || 'Invalid code');
        } finally {
            setLoading(false);
        }
    };

    return (
        <Dialog open={open} onClose={onClose} fullWidth maxWidth="xs">
            <DialogTitle>
                {mode === 'enable' ? 'Enable Two-Factor Authentication' : 'Disable Two-Factor Authentication'}
            </DialogTitle>
            <DialogContent>
                {loading && (
                    <Box display="flex" justifyContent="center" py={3}>
                        <CircularProgress size={28} />
                    </Box>
                )}

                {!loading && mode === 'enable' && otpauthUri && (
                    <Stack spacing={2} alignItems="center">
                        <Typography variant="body2">
                            Scan this QR code with an authenticator app (Google Authenticator, 1Password, Authy, …) and then enter the 6-digit code.
                        </Typography>
                        <Box sx={{ p: 2, bgcolor: '#fff' }}>
                            <QRCodeSVG value={otpauthUri} size={192} />
                        </Box>
                        <Typography variant="caption" sx={{ wordBreak: 'break-all', textAlign: 'center' }}>
                            Or enter this secret manually: <strong>{secret}</strong>
                        </Typography>
                    </Stack>
                )}

                {!loading && mode === 'disable' && (
                    <Typography variant="body2" sx={{ mb: 2 }}>
                        Enter a current 6-digit code from your authenticator app to confirm disabling.
                    </Typography>
                )}

                <TextField
                    label="6-digit code"
                    value={code}
                    onChange={(e) => setCode(e.target.value)}
                    inputProps={{ inputMode: 'numeric', pattern: '[0-9]*', maxLength: 6 }}
                    fullWidth
                    sx={{ mt: 2 }}
                />

                {error && <Alert severity="error" sx={{ mt: 2 }}>{error}</Alert>}
            </DialogContent>
            <DialogActions>
                <Button onClick={onClose} disabled={loading}>Cancel</Button>
                <Button
                    variant="contained"
                    onClick={handleConfirm}
                    disabled={loading || code.length < 6}
                    color={mode === 'enable' ? 'primary' : 'error'}
                >
                    {mode === 'enable' ? 'Enable' : 'Disable'}
                </Button>
            </DialogActions>
        </Dialog>
    );
};

export default TwoFactorDialog;
