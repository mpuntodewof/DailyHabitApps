import React, { createContext, useCallback, useContext, useEffect, useState } from 'react';
import api from '../api/axiosInstance';
import { getAccessToken } from '../utils/tokenUtils';
import { useAuth } from './AuthContext';

const GoalContext = createContext(undefined);

export const useGoals = () => {
  const ctx = useContext(GoalContext);
  if (!ctx) throw new Error('useGoals must be used within GoalProvider');
  return ctx;
};

export const GoalProvider = ({ children }) => {
  const { user } = useAuth();
  const [visions, setVisions] = useState([]);
  const [goals, setGoals] = useState([]);
  const [loading, setLoading] = useState(false);

  const fetchAll = useCallback(async () => {
    if (!getAccessToken()) return;
    setLoading(true);
    try {
      const [v, g] = await Promise.all([api.get('/Vision'), api.get('/Goal')]);
      setVisions(v.data?.result || []);
      setGoals(g.data?.result || []);
    } catch (err) {
      console.error('Failed to load goals:', err.message);
    } finally {
      setLoading(false);
    }
  }, []);

  // Load once the user is authenticated. On a hard refresh the in-memory access
  // token is restored asynchronously by AuthContext.restoreSession, so we key the
  // fetch on `user` (not provider mount) — otherwise fetchAll runs before the
  // token exists, bails early, and the page shows empty until the next navigation.
  useEffect(() => {
    if (user) fetchAll();
    else { setVisions([]); setGoals([]); }
  }, [user, fetchAll]);

  const createVision = useCallback(async (dto) => {
    const res = await api.post('/Vision', dto);
    const created = res.data?.result;
    if (created) setVisions((p) => [created, ...p]);
    return created;
  }, []);
  const updateVision = useCallback(async (id, dto) => {
    const res = await api.put(`/Vision/${id}`, dto);
    const u = res.data?.result;
    if (u) setVisions((p) => p.map((x) => (x.id === id ? u : x)));
    return u;
  }, []);
  const deleteVision = useCallback(async (id) => {
    await api.delete(`/Vision/${id}`);
    setVisions((p) => p.filter((x) => x.id !== id));
    // goals under it lose their visionId server-side; refresh goals
    const g = await api.get('/Goal');
    setGoals(g.data?.result || []);
  }, []);

  const createGoal = useCallback(async (dto) => {
    const res = await api.post('/Goal', dto);
    const created = res.data?.result;
    if (created) setGoals((p) => [created, ...p]);
    return created;
  }, []);
  const updateGoal = useCallback(async (id, dto) => {
    const res = await api.put(`/Goal/${id}`, dto);
    const u = res.data?.result;
    if (u) setGoals((p) => p.map((x) => (x.id === id ? u : x)));
    return u;
  }, []);
  const deleteGoal = useCallback(async (id) => {
    await api.delete(`/Goal/${id}`);
    setGoals((p) => p.filter((x) => x.id !== id));
  }, []);

  const listMilestones = useCallback(async (goalId) => {
    const res = await api.get(`/Milestone/goal/${goalId}`);
    return res.data?.result || [];
  }, []);
  const createMilestone = useCallback(async (dto) => {
    const res = await api.post('/Milestone', dto);
    return res.data?.result;
  }, []);
  const updateMilestone = useCallback(async (id, dto) => {
    const res = await api.put(`/Milestone/${id}`, dto);
    return res.data?.result;
  }, []);
  const deleteMilestone = useCallback(async (id) => {
    await api.delete(`/Milestone/${id}`);
  }, []);

  return (
    <GoalContext.Provider value={{
      visions, goals, loading, fetchAll,
      createVision, updateVision, deleteVision,
      createGoal, updateGoal, deleteGoal,
      listMilestones, createMilestone, updateMilestone, deleteMilestone,
    }}>
      {children}
    </GoalContext.Provider>
  );
};
