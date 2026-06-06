import { createContext, useContext, useState, useCallback } from 'react';
import { buildUserProfile, getAccessToken } from '../utils/tokenUtils';
import api from '../api/axiosInstance';

const HabitContext = createContext();

export const useHabits = () => useContext(HabitContext);

export const HabitProvider = ({ children }) => {
  const [habits, setHabits] = useState([]);
  const [loading, setLoading] = useState(true);

  const [pagination, setPagination] = useState({ total: 0, page: 1, pageSize: 20 });

  const fetchHabits = useCallback(async () => {
    setLoading(true);
    try {
      const token = getAccessToken();
      if (!token) {
        setLoading(false);
        return;
      }

      const profile = buildUserProfile(token);
      const res = await api.get(`/Habit/get-habits/${profile.sub}`);
      setHabits(res.data || []);
    } catch (err) {
      console.error('Failed to fetch habits:', err.message);
    } finally {
      setLoading(false);
    }
  }, []);

  const searchHabits = useCallback(async ({ search = '', tagId = null, includeArchived = false, page = 1, pageSize = 20 } = {}) => {
    if (!getAccessToken()) return;
    setLoading(true);
    try {
      const params = { page, pageSize, includeArchived };
      if (search) params.search = search;
      if (tagId) params.tagId = tagId;
      const res = await api.get('/Habit/search', { params });
      const result = res.data?.result;
      setHabits({ result: result?.items || [] });
      setPagination({
        total: result?.total ?? 0,
        page: result?.page ?? page,
        pageSize: result?.pageSize ?? pageSize,
      });
    } catch (err) {
      console.error('Failed to search habits:', err.message);
    } finally {
      setLoading(false);
    }
  }, []);

  const createHabit = async (habit) => {
    try {
      const res = await api.post('/Habit/post-habit', habit);

      const createdData = Array.isArray(res.data) ? res.data[0] : res.data;

      setHabits((prev) =>
        Array.isArray(prev) ? [...prev, createdData] : [createdData]
      );

      return res;
    } catch (err) {
      console.error('Create habit error:', err);
      if (err.code === 'ERR_NETWORK') {
        console.error('Network error: Unable to connect to the server. Please check if the API server is running.');
      } else if (err.response?.status === 0) {
        console.error('CORS error: The server is not allowing cross-origin requests. Please check the server CORS configuration.');
      } else {
        console.error(`Failed to create habit: ${err.response?.data?.message || err.message}`);
      }
      throw err; // Re-throw to let the calling component handle it
    }
  };

  const updateHabit = async (id, updatedHabit) => {
    try {
      const res = await api.put(`/Habit/update-habit/${id}`, updatedHabit);

      const updatedData = Array.isArray(res.data) ? res.data[0] : res.data;

      setHabits((prev) =>
        Array.isArray(prev)
          ? prev.map((habit) => (habit.id === id ? updatedData : habit))
          : [updatedData]
      );

      return res;
    } catch (err) {
      console.error('Failed to update habit' + err.message);
    }
  };

  const archiveHabit = async (id) => {
    try {
      const res = await api.post(`/Habit/archive/${id}`);
      setHabits((prev) =>
        Array.isArray(prev) ? prev.filter((h) => h.id !== id) : prev
      );
      return res;
    } catch (err) {
      console.error('Failed to archive habit: ' + err.message);
      throw err;
    }
  };

  const restoreHabit = async (id) => {
    try {
      const res = await api.post(`/Habit/restore/${id}`);
      await fetchHabits();
      return res;
    } catch (err) {
      console.error('Failed to restore habit: ' + err.message);
      throw err;
    }
  };

  const deleteHabit = async (id) => {
    try {
      const res = await api.delete(`/Habit/delete-habit/${id}`);

      const token = getAccessToken();
      if (token) {
        const profile = buildUserProfile(token);
        const habitsRes = await api.get(`/Habit/get-habits/${profile.sub}`);
        setHabits(habitsRes.data || []);
      }

      return res;
    } catch (err) {
      console.error('Failed to delete habit: ' + err.message);
      throw err;
    }
  }

  return (
    <HabitContext.Provider value={{ habits, loading, pagination, fetchHabits, searchHabits, createHabit, updateHabit, deleteHabit, archiveHabit, restoreHabit }}>
      {children}
    </HabitContext.Provider>
  );
};