import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { Box, Button, Card, Grid, Stack, Typography } from '@mui/material';
import CustomTextField from '../../../components/forms/theme-elements/CustomTextField.jsx';
import PageContainer from '../../../components/container/PageContainer';
import Logo from '../../../layouts/shared/logo/Logo';
import { useAuth } from '../../../context/AuthContext';
import { useSnackbar } from '../../../context/SnackbarContext';

const ForgotPassword = () => {
  const { requestPasswordReset } = useAuth();
  const { showError, showSuccess } = useSnackbar();
  const [email, setEmail] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [sent, setSent] = useState(false);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      await requestPasswordReset(email);
      setSent(true);
      showSuccess('Reset link sent. Check your inbox (and spam folder).');
    } catch (err) {
      const apiMsg =
        err?.response?.data?.errorMessages?.[0] ||
        err?.response?.data?.message ||
        err?.message ||
        'Failed to send reset email';
      showError(apiMsg);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <PageContainer title="Forgot Password" description="Reset your password">
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
                Forgot your password?
              </Typography>

              <Typography variant="body2" textAlign="center" color="textSecondary" mb={3}>
                Enter the email associated with your account and we'll send you a reset link.
              </Typography>

              <form onSubmit={handleSubmit}>
                <Stack>
                  <Box>
                    <Typography
                      variant="subtitle1"
                      fontWeight={600}
                      component="label"
                      htmlFor="email"
                      mb="5px"
                    >
                      Email
                    </Typography>
                    <CustomTextField
                      id="email"
                      type="email"
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      variant="outlined"
                      fullWidth
                      required
                    />
                  </Box>
                </Stack>
                <Box mt={3}>
                  <Button
                    disabled={submitting}
                    color="primary"
                    variant="contained"
                    size="large"
                    fullWidth
                    type="submit"
                  >
                    {sent ? 'Email Sent' : submitting ? 'Sending…' : 'Send Reset Link'}
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

export default ForgotPassword;
