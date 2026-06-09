import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import api from '../api/axiosInstance';
import {
  buildUserProfile,
  clearAccessToken,
  refreshAccessToken,
  setAccessToken,
} from "../utils/tokenUtils";

const AuthContext = createContext(undefined);

export const useAuth = () => {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
};

export const AuthProvider = ({ children }) => {
  const [user, setUser] = useState(null);
  const [permissions, setPermissions] = useState([]);
  const [roles, setRoles] = useState([]);
  const [planTier, setPlanTier] = useState('Free');
  const [isPro, setIsPro] = useState(false);
  const [initializing, setInitializing] = useState(true);

  const clearSessions = useCallback(() => {
    clearAccessToken();
    setUser(null);
    setPermissions([]);
    setRoles([]);
    setPlanTier('Free');
    setIsPro(false);
  }, []);

  const fetchMe = useCallback(async () => {
    try {
      const res = await api.get('/Auth/me');
      const me = res.data?.result;
      if (me) {
        setPermissions(me.permissions || []);
        setRoles(me.roles || []);
        setPlanTier(me.planTier || 'Free');
        setIsPro(me.isPro === true);
      }
    } catch (err) {
      console.warn('Failed to load /Auth/me:', err?.message);
    }
  }, []);

  const restoreSession = useCallback(async () => {
    const newAccessToken = await refreshAccessToken();
    if (newAccessToken) {
      setUser(buildUserProfile(newAccessToken));
      await fetchMe();
      return;
    }
    clearSessions();
  }, [clearSessions, fetchMe]);

  useEffect(() => {
    restoreSession().finally(() => setInitializing(false));
  }, [restoreSession]);

  const login = useCallback(async (email, password) => {
    try {
      const res = await api.post('/Auth/login', { email, password });
      const result = res.data?.result;

      if (result?.requiresTwoFactor) {
        return { requiresTwoFactor: true, twoFactorToken: result.twoFactorToken };
      }

      const accessToken = result?.accessToken;
      if (!accessToken) {
        throw new Error("Login response missing access token");
      }

      setAccessToken(accessToken);
      const profile = buildUserProfile(accessToken);
      setUser(profile);
      await fetchMe();

      return { user: profile };
    } catch (error) {
      console.error('Authentication failed: ' + error);
      throw error;
    }
  }, [fetchMe]);

  const verifyTwoFactor = useCallback(async (twoFactorToken, code, isRecoveryCode = false) => {
    const res = await api.post('/Auth/verify-2fa', { twoFactorToken, code, isRecoveryCode });
    const accessToken = res.data?.result?.accessToken;
    if (!accessToken) {
      throw new Error("2FA verification did not return tokens");
    }
    setAccessToken(accessToken);
    const profile = buildUserProfile(accessToken);
    setUser(profile);
    await fetchMe();
    return profile;
  }, [fetchMe]);

  const hasPermission = useCallback(
    (code) => Array.isArray(permissions) && permissions.includes(code),
    [permissions]
  );

  const hasRole = useCallback(
    (roleName) => Array.isArray(roles) && roles.includes(roleName),
    [roles]
  );

  const register = async (payload) => {
    try {
      const res = await api.post('/Auth/register', payload);
      const accessToken = res.data?.result?.accessToken;
      if (accessToken) {
        setAccessToken(accessToken);
        setUser(buildUserProfile(accessToken));
      }
      return res.data;
    } catch (error) {
      console.error('registration failed: ' + error);
      throw error;
    }
  };

  const requestPasswordReset = async (email) => {
    try {
      await api.post('/Auth/forgot-password', { email });
    } catch (error) {
      console.error('Password reset failed: ' + error);
      throw error;
    }
  };

  const confirmPasswordReset = async ({ email, token, password, confirmPassword }) => {
    try {
      await api.post('/Auth/reset-password', {
        email,
        token,
        password,
        confirmPassword,
      });
    } catch (error) {
      console.error('Confirm password reset failed: ' + error);
      throw error;
    }
  };

  const logout = useCallback(async () => {
    try {
      await api.post('/Auth/logout');
    } catch (err) {
      console.warn('Logout request failed; clearing session anyway:', err?.message);
    } finally {
      clearSessions();
    }
  }, [clearSessions]);

  const value = useMemo(
    () => ({
      user,
      permissions,
      roles,
      planTier,
      isPro,
      hasPermission,
      hasRole,
      initializing,
      login,
      verifyTwoFactor,
      register,
      requestPasswordReset,
      confirmPasswordReset,
      logout,
      refreshMe: fetchMe
    }),
    [user, permissions, roles, planTier, isPro, hasPermission, hasRole, initializing, login, verifyTwoFactor, logout, fetchMe]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};
