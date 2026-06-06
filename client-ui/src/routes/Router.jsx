import React, { lazy, Suspense } from 'react';
import { createBrowserRouter, Navigate } from 'react-router-dom';
import { CircularProgress, Box } from '@mui/material';
import ProtectedRoute from '../components/ProtectedRoute';
import PublicRoute from '../components/PublicRoute';


const LoadingFallback = () => (
  <Box
    sx={{
      display: 'flex',
      justifyContent: 'center',
      alignItems: 'center',
      height: '100vh',
      width: '100vw',
    }}
  >
    <CircularProgress />
  </Box>
);

/* ***Layouts**** */
const FullLayout = lazy(() => import('../layouts/FullLayout'));
const BlankLayout = lazy(() => import('../layouts/BlankLayout'));

/* ****Pages***** */
const Dashboard = lazy(() => import('../views/dashboard/Dashboard'));
const Habit = lazy(() => import('../views/habit/Habit'));
const Tables = lazy(() => import('../views/tables/Tables'));
const Stats = lazy(() => import('../views/stats/Stats'));
// const Profile = lazy(() => import('../views/profile/Profile'));
const Settings = lazy(() => import('../views/settings/Settings'));
const AdminUsers = lazy(() => import('../views/admin/AdminUsers'));
const Error = lazy(() => import('../views/authentication/Error'));
const Login = lazy(() => import('../views/authentication/Login'));
const Register = lazy(() => import('../views/authentication/Register'));
const Forgot = lazy(() => import('../views/authentication/auth/ForgotPassword'));
const ResetPassword = lazy(() => import('../views/authentication/auth/ResetPassword'));


const Router = [
  {
    path: '/',
    element: (
      <ProtectedRoute>
        <Suspense fallback={<LoadingFallback />}>
          <FullLayout />
        </Suspense>
      </ProtectedRoute>
    ),
    children: [
      { path: '/', element: <Navigate to="/dashboard" /> },
      { path: '/dashboard', element: <Dashboard /> },
      { path: '/habits', exact: true, element: <Habit /> },
      { path: '/stats', exact: true, element: <Stats /> },
      { path: '/tables', exact: true, element: <Tables /> },
      { path: '/settings', exact: true, element: <Settings /> },
      { path: '/admin/users', exact: true, element: <AdminUsers /> },
      { path: '*', element: <Navigate to="/auth/404" /> },
    ],
  },
  {
    path: '/auth',
    element: (
      <Suspense fallback={<LoadingFallback />}>
        <BlankLayout />
      </Suspense>
    ),
    children: [
      { path: 'login', element: <PublicRoute><Login /></PublicRoute> },
      { path: 'register', element: <PublicRoute><Register /></PublicRoute> },
      { path: 'forgot-password', element: <PublicRoute><Forgot /></PublicRoute> },
      { path: 'reset-password', element: <PublicRoute><ResetPassword /></PublicRoute> },
      { path: '404', element: <Error /> },
      { path: '*', element: <Navigate to="/auth/404" /> },
    ],
  },
  {
    path: '/404',
    element: (
      <Suspense fallback={<LoadingFallback />}>
        <Error />
      </Suspense>
    ),
  },
  {
    path: '*',
    element: <Navigate to="/404" />
  }
];

const router = createBrowserRouter(Router);

export default router;
