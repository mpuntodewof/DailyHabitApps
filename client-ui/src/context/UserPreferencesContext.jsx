import React, { createContext, useCallback, useContext, useEffect, useState } from 'react';
import api from '../api/axiosInstance';
import { getAccessToken } from '../utils/tokenUtils';

const defaultPrefs = {
  notifications: true,
  darkMode: false,
  emailUpdates: true,
  deviceSync: true,
  primaryColor: '#2196f3',
  fontFamily: 'Inter',
  borderRadius: 8,
  spacing: 8,
};

const UserPreferencesContext = createContext(undefined);

export const useUserPreferences = () => {
  const ctx = useContext(UserPreferencesContext);
  if (!ctx) throw new Error('useUserPreferences must be used within UserPreferencesProvider');
  return ctx;
};

export const UserPreferencesProvider = ({ children }) => {
  const [prefs, setPrefs] = useState(defaultPrefs);
  const [loading, setLoading] = useState(false);

  const fetchPrefs = useCallback(async () => {
    if (!getAccessToken()) return;
    setLoading(true);
    try {
      const res = await api.get('/UserPreferences');
      const data = res.data?.result;
      if (data) setPrefs({ ...defaultPrefs, ...data });
    } catch (err) {
      console.error('Failed to load preferences:', err.message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchPrefs();
  }, [fetchPrefs]);

  const savePrefs = useCallback(async (next) => {
    const merged = { ...prefs, ...next };
    setPrefs(merged);
    try {
      const res = await api.put('/UserPreferences', merged);
      const data = res.data?.result;
      if (data) setPrefs({ ...defaultPrefs, ...data });
      return true;
    } catch (err) {
      console.error('Failed to save preferences:', err.message);
      return false;
    }
  }, [prefs]);

  return (
    <UserPreferencesContext.Provider value={{ prefs, loading, fetchPrefs, savePrefs }}>
      {children}
    </UserPreferencesContext.Provider>
  );
};
