import React, { useState } from 'react';
import { Box, Typography, Button } from '@mui/material';
import { Link, useNavigate } from 'react-router-dom';

import CustomTextField from '../../../components/forms/theme-elements/CustomTextField.jsx';
import PasswordField from '../../../components/forms/PasswordField.jsx';
import { Stack } from '@mui/system';
import { useAuth } from '../../../context/AuthContext';
import { useSnackbar } from '../../../context/SnackbarContext';

const AuthRegister = ({ title, subtitle, subtext }) => {
    const navigate = useNavigate();
    const { register } = useAuth();
    const { showError, showSuccess } = useSnackbar();
    const [username, setUsername] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [submitting, setSubmitting] = useState(false);

    const handleSubmit = async (e) => {
        e.preventDefault();
        setSubmitting(true);
        try {
            await register({ username, email, password, role: "User" });
            showSuccess('Registration successful! Please login with your new account.');
            navigate('/auth/login', { replace: true });
        } catch (err) {
            const errorMessage = err?.response?.data?.message || 'Registration failed. Please try again.';
            showError(errorMessage);
        } finally {
            setSubmitting(false);
        }
    };

    return (
        <form onSubmit={handleSubmit}>
            {title ? (
                <Typography fontWeight="700" variant="h2" mb={1}>
                    {title}
                </Typography>
            ) : null}

            {subtext}

            <Box>
                <Stack mb={3}>
                    <Typography variant="subtitle1"
                        fontWeight={600} component="label" htmlFor='name' mb="5px">Name</Typography>
                    <CustomTextField id="name" value={username} onChange={(e) => setUsername(e.target.value)} variant="outlined" fullWidth required />

                    <Typography variant="subtitle1"
                        fontWeight={600} component="label" htmlFor='email' mb="5px" mt="25px">Email Address</Typography>
                    <CustomTextField id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} variant="outlined" fullWidth required />

                    <Typography variant="subtitle1"
                        fontWeight={600} component="label" htmlFor='password' mb="5px" mt="25px">Password</Typography>
                    <PasswordField id="password" value={password} onChange={(e) => setPassword(e.target.value)} variant="outlined" fullWidth required />
                </Stack>
                <Button disabled={submitting} color="primary" variant="contained" size="large" fullWidth type="submit">
                    Sign Up
                </Button>
            </Box>
            {subtitle}
        </form>
    );
};

export default AuthRegister;
