import React, { useEffect, useState } from 'react';
import {
    Box,
    Typography,
    FormGroup,
    FormControlLabel,
    Button,
    Stack,
    Checkbox
} from '@mui/material';
import { Link, useNavigate, useLocation } from 'react-router-dom';

import CustomTextField from '../../../components/forms/theme-elements/CustomTextField.jsx';
import PasswordField from '../../../components/forms/PasswordField.jsx';
import { useAuth } from '../../../context/AuthContext';
import { useSnackbar } from '../../../context/SnackbarContext';

const AuthLogin = ({ title, subtitle, subtext }) => {
    const navigate = useNavigate();
    const location = useLocation();
    const { login, verifyTwoFactor, user, initializing } = useAuth();
    const { showError, showSuccess } = useSnackbar();
    const [usernameOrEmail, setUsernameOrEmail] = useState('');
    const [password, setPassword] = useState('');
    const [submitting, setSubmitting] = useState(false);

    const [twoFactorToken, setTwoFactorToken] = useState(null);
    const [twoFactorCode, setTwoFactorCode] = useState('');
    const [useRecoveryCode, setUseRecoveryCode] = useState(false);

    useEffect(() => {
        if (!initializing && user) {
            const from = location.state?.from?.pathname || '/dashboard';
            navigate(from, { replace: true });
        }
        // `location` is a new object on every render — depending on it loops.
        // Read location.state at effect time instead. eslint-disable-next-line react-hooks/exhaustive-deps
    }, [user, initializing, navigate]);

    const handleSubmit = async (e) => {
        e.preventDefault();
        setSubmitting(true);
        try {
            const result = await login(usernameOrEmail, password);
            if (result?.requiresTwoFactor) {
                setTwoFactorToken(result.twoFactorToken);
                showSuccess('Enter the 6-digit code from your authenticator app');
                return;
            }
            showSuccess('Login successful!');
            navigate('/dashboard', { replace: true, state: {} });
        } catch (err) {
            const errorMessage = err?.response?.data?.message || 'Invalid credentials. Please try again.';
            showError(errorMessage);
        } finally {
            setSubmitting(false);
        }
    };

    const handleVerifyTwoFactor = async (e) => {
        e.preventDefault();
        setSubmitting(true);
        try {
            await verifyTwoFactor(twoFactorToken, twoFactorCode.trim(), useRecoveryCode);
            showSuccess('Login successful!');
            navigate('/dashboard', { replace: true, state: {} });
        } catch (err) {
            const errorMessage = err?.response?.data?.errorMessages?.[0] || 'Invalid code. Try again.';
            showError(errorMessage);
        } finally {
            setSubmitting(false);
        }
    };

    if (twoFactorToken) {
        const minLen = useRecoveryCode ? 12 : 6; // recovery codes are XXXX-XXXX-XXXX
        return (
            <form onSubmit={handleVerifyTwoFactor}>
                {title ? (
                    <Typography fontWeight="700" variant="h2" mb={1}>
                        Two-Factor Authentication
                    </Typography>
                ) : null}
                <Typography variant="body1" mb={2}>
                    {useRecoveryCode
                        ? 'Enter one of your recovery codes.'
                        : 'Enter the 6-digit code from your authenticator app.'}
                </Typography>
                <Stack spacing={2}>
                    <CustomTextField
                        id="twofa-code"
                        variant="outlined"
                        fullWidth
                        autoFocus
                        inputProps={useRecoveryCode
                            ? { maxLength: 14 }
                            : { inputMode: 'numeric', pattern: '[0-9]*', maxLength: 6 }}
                        placeholder={useRecoveryCode ? 'XXXX-XXXX-XXXX' : ''}
                        value={twoFactorCode}
                        onChange={(e) => setTwoFactorCode(e.target.value)}
                    />
                    <Button
                        color="primary"
                        variant="contained"
                        size="large"
                        fullWidth
                        type="submit"
                        disabled={submitting || twoFactorCode.trim().length < minLen}
                    >
                        Verify
                    </Button>
                    <Button
                        color="inherit"
                        size="small"
                        onClick={() => { setUseRecoveryCode((v) => !v); setTwoFactorCode(''); }}
                        disabled={submitting}
                    >
                        {useRecoveryCode ? 'Use an authenticator code instead' : 'Use a recovery code instead'}
                    </Button>
                    <Button
                        color="inherit"
                        size="small"
                        onClick={() => { setTwoFactorToken(null); setTwoFactorCode(''); setUseRecoveryCode(false); }}
                        disabled={submitting}
                    >
                        Cancel
                    </Button>
                </Stack>
            </form>
        );
    }

    return (
        <form onSubmit={handleSubmit}>
            {title ? (
                <Typography fontWeight="700" variant="h2" mb={1}>
                    {title}
                </Typography>
            ) : null}

            {subtext}

            <Stack>
                <Box>
                    <Typography variant="subtitle1"
                        fontWeight={600} component="label" htmlFor='username' mb="5px">Username or Email</Typography>
                    <CustomTextField id="username" value={usernameOrEmail} onChange={(e) => setUsernameOrEmail(e.target.value)} variant="outlined" fullWidth required />
                </Box>
                <Box mt="25px">
                    <Typography variant="subtitle1"
                        fontWeight={600} component="label" htmlFor='password' mb="5px" >Password</Typography>
                    <PasswordField id="password" value={password} onChange={(e) => setPassword(e.target.value)} variant="outlined" fullWidth required />
                </Box>
                <Stack justifyContent="space-between" direction="row" alignItems="center" my={2}>
                    <FormGroup>
                        <FormControlLabel
                            control={<Checkbox defaultChecked />}
                            label="Remember this Device"
                        />
                    </FormGroup>
                    <Typography
                        component={Link}
                        to="/auth/forgot-password"
                        fontWeight="500"
                        sx={{
                            textDecoration: 'none',
                            color: 'primary.main',
                        }}
                    >
                        Forgot Password ?
                    </Typography>
                </Stack>
            </Stack>
            <Box>
                <Button
                    disabled={submitting}
                    color="primary"
                    variant="contained"
                    size="large"
                    fullWidth
                    type="submit"
                >
                    Sign In
                </Button>
            </Box>
            {subtitle}
        </form>
    )
};

export default AuthLogin;
