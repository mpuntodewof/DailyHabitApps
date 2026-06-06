import React, { createContext, useCallback, useContext, useEffect, useState } from 'react';
import api from '../api/axiosInstance';
import { getAccessToken } from '../utils/tokenUtils';

const TagContext = createContext(undefined);

export const useTags = () => {
  const ctx = useContext(TagContext);
  if (!ctx) throw new Error('useTags must be used within TagProvider');
  return ctx;
};

export const TagProvider = ({ children }) => {
  const [tags, setTags] = useState([]);
  const [loading, setLoading] = useState(false);

  const fetchTags = useCallback(async () => {
    if (!getAccessToken()) return;
    setLoading(true);
    try {
      const res = await api.get('/Tag');
      setTags(res.data?.result || []);
    } catch (err) {
      console.error('Failed to load tags:', err.message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { fetchTags(); }, [fetchTags]);

  const createTag = useCallback(async (name, color) => {
    const res = await api.post('/Tag', { name, color });
    const created = res.data?.result;
    if (created) setTags((prev) => [...prev, created].sort((a, b) => a.name.localeCompare(b.name)));
    return created;
  }, []);

  const deleteTag = useCallback(async (tagId) => {
    await api.delete(`/Tag/${tagId}`);
    setTags((prev) => prev.filter((t) => t.id !== tagId));
  }, []);

  const attachTag = useCallback(async (habitId, tagId) => {
    await api.post(`/Tag/habit/${habitId}/${tagId}`);
  }, []);

  const detachTag = useCallback(async (habitId, tagId) => {
    await api.delete(`/Tag/habit/${habitId}/${tagId}`);
  }, []);

  return (
    <TagContext.Provider value={{ tags, loading, fetchTags, createTag, deleteTag, attachTag, detachTag }}>
      {children}
    </TagContext.Provider>
  );
};
