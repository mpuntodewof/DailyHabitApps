import React, { useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { Box, Button, Card, Grid, Stack, Typography, Alert, IconButton, InputAdornment } from '@mui/material';
import VisibilityIcon from '@mui/icons-material/Visibility';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import CustomTextField from '../../../components/forms/theme-elements/CustomTextField.jsx';
import PageContainer from '../../../components/container/PageContainer';
import Logo from '../../../layouts/shared/logo/Logo';
import { useAuth } from '../../../context/AuthContext';
import { useSnackbar } from '../../../context/SnackbarContext';

const ResetPassword = () => {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { confirmPasswordReset } = useAuth();
  const { showError, showSuccess } = useSnackbar();

  const token = searchParams.get('token') || '';
  const email = searchParams.get('email') || '';

  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirm, setShowConfirm] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [success, setSuccess] = useState(false);

  const linkValid = useMemo(() => Boolean(token && email), [token, email]);

  useEffect(() => {
    if (!linkValid) {
      showError('Reset link is missing token or email. Request a new link.');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [linkValid]);

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!linkValid) return;

    if (password.length < 6) {
      showError('Password must be at least 6 characters.');
      return;
    }
    if (password !== confirm) {
      showError('Passwords do not match.');
      return;
    }

    setSubmitting(true);
    try {
      await confirmPasswordReset({ email, token, password, confirmPassword: confirm });
      setSuccess(true);
      showSuccess('Password reset. You can sign in with your new password.');
      setTimeout(() => navigate('/auth/login', { replace: true }), 1200);
    } catch (err) {
      const apiMsg =
        err?.response?.data?.errorMessages?.[0] ||
        err?.response?.data?.message ||
        err?.message ||
        'Failed to reset password';
      showError(apiMsg);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <PageContainer title="Reset Password" description="Set a new password">
      <Box
        sx={{
          position: 'relative',
          '&:before': {
            content: '""',
            background: 'radial-gradient(#d2f1df, #d3d7fa, #bad8f4)',
            backgroundSize: '400% 400%',
            animation: 'gradient 15s ease infinite',
            position: 'absolute',
            height: '100%',
            width: '100%',
            opacity: '0.3',
          },
        }}
      >
        <Grid container spacing={0} justifyContent="center" sx={{ height: '100vh' }}>
          <Grid
            display="flex"
            justifyContent="center"
            alignItems="center"
            size={{
              xs: 12,
              sm: 12,
              lg: 4,
              xl: 3,
            }}
          >
            <Card elevation={9} sx={{ p: 4, zIndex: 1, width: '100%', maxWidth: '500px' }}>
              <Box display="flex" alignItems="center" justifyContent="center">
                <Logo />
              </Box>

              <Typography variant="subtitle1" textAlign="center" color="textSecondary" mb={1}>
                Choose a new password
              </Typography>

              {email ? (
                <Typography variant="body2" textAlign="center" color="textSecondary" mb={3}>
                  Resetting password for <strong>{email}</strong>
                </Typography>
              ) : (
                <Typography variant="body2" textAlign="center" color="textSecondary" mb={3}>
                  Enter the new password for your account.
                </Typography>
              )}

              {!linkValid && (
                <Alert severity="error" sx={{ mb: 2 }}>
                  This reset link is invalid or incomplete.
                </Alert>
              )}

              <form onSubmit={handleSubmit}>
                <Stack spacing={2}>
                  <Box>
                    <Typography
                      variant="subtitle1"
                      fontWeight={600}
                      component="label"
                      htmlFor="password"
                      mb="5px"
                    >
                      New password
                    </Typography>
                    <CustomTextField
                      id="password"
                      type={showPassword ? 'text' : 'password'}
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      variant="outlined"
                      fullWidth
                      required
                      disabled={!linkValid || success}
                      InputProps={{
                        endAdornment: (
                          <InputAdornment position="end">
                            <IconButton
                              aria-label={showPassword ? 'Hide password' : 'Show password'}
                              onClick={() => setShowPassword((v) => !v)}
                              onMouseDown={(e) => e.preventDefault()}
                              edge="end"
                              size="small"
                              tabIndex={-1}
                            >
                              {showPassword ? <VisibilityOffIcon /> : <VisibilityIcon />}
                            </IconButton>
                          </InputAdornment>
                        ),
                      }}
                    />
                  </Box>

                  <Box>
                    <Typography
                      variant="subtitle1"
                      fontWeight={600}
                      component="label"
                      htmlFor="confirm"
                      mb="5px"
                    >
                      Confirm new password
                    </Typography>
                    <CustomTextField
                      id="confirm"
                      type={showConfirm ? 'text' : 'password'}
                      value={confirm}
                      onChange={(e) => setConfirm(e.target.value)}
                      variant="outlined"
                      fullWidth
                      required
                      disabled={!linkValid || success}
                      InputProps={{
                        endAdornment: (
                          <InputAdornment position="end">
                            <IconButton
                              aria-label={showConfirm ? 'Hide password' : 'Show password'}
                              onClick={() => setShowConfirm((v) => !v)}
                              onMouseDown={(e) => e.preventDefault()}
                              edge="end"
                              size="small"
                              tabIndex={-1}
                            >
                              {showConfirm ? <VisibilityOffIcon /> : <VisibilityIcon />}
                            </IconButton>
                          </InputAdornment>
                        ),
                      }}
                    />
                  </Box>
                </Stack>

                <Box mt={3}>
                  <Button
                    color="primary"
                    variant="contained"
                    size="large"
                    fullWidth
                    type="submit"
                    disabled={!linkValid || submitting || success}
                  >
                    {success ? 'Password Updated' : submitting ? 'Updating…' : 'Reset Password'}
                  </Button>
                </Box>
              </form>

              <Stack direction="row" spacing={1} justifyContent="center" mt={3}>
                <Typography color="textSecondary" variant="h6" fontWeight="500">
                  Remembered your password?
                </Typography>
                <Typography
                  component={Link}
                  to="/auth/login"
                  fontWeight="500"
                  sx={{
                    textDecoration: 'none',
                    color: 'primary.main',
                  }}
                >
                  Back to login
                </Typography>
              </Stack>
            </Card>
          </Grid>
        </Grid>
      </Box>
    </PageContainer>
  );
};

export default ResetPassword;
