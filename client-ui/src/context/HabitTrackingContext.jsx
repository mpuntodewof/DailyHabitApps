import React, { useState } from 'react'
import { useCallback } from 'react';
import { getAccessToken } from '../utils/tokenUtils';
import api from '../api/axiosInstance';

const HabitTrackingContext = React.createContext();

export const useHabitTracking = () => React.useContext(HabitTrackingContext);

export const HabitTrackingProvider = ({ children }) => {
    const [habitTrackings, setHabitTrackings] = React.useState([]);
    const [habitStats, setHabiStats] = React.useState([]);
    const [loading, setLoading] = useState(true);
    const [habitOverview, setHabitOverview] = useState({});

    const getHabitTrackingDates = useCallback(async (habitId, userId) => {
        setLoading(true);
        if (!getAccessToken()) {
            setLoading(false);
            return;
        }

        try {
            const res = await api.get(`/HabitTracking/habit-tracking-dates?habitId=${habitId}&userId=${userId}`);
            setHabitTrackings(res.data || []);
            return res.data;
        } catch (err) {
            console.error('Failed to fetch habit tracking dates:', err.message);
            throw err;
        } finally {
            setLoading(false);
        }
    }, [])

    const getHabitStats = useCallback(async (habitId, userId) => {
        setLoading(true);
        if (!getAccessToken()) {
            setLoading(false);
            return;
        }

        try {
            const res = await api.get(`/HabitTracking/get-habit-stats/${habitId}/${userId}`);
            setHabiStats(res.data || []);
            return res.data;
        } catch (err) {
            console.error('Failed to fetch habit stats:', err.message);
        } finally {
            setLoading(false);
        }
    }, []);

    const getHabitOverview = useCallback(async (userId) => {
        setLoading(true);
        if (!getAccessToken()) {
            setLoading(false);
            return;
        }

        try {
            const res = await api.get(`/Dashboard/habit-card-overviews?userId=${userId}`);
            setHabitOverview(res.data || {});
            return res.data;
        } catch (err) {
            console.error('Failed to fetch habit overview:', err.message);
        } finally {
            setLoading(false);
        }

    }, []);

    const submitHabitTracking = async (trackingData) => {
        try {
            const res = await api.post('/HabitTracking/submit-habit-progress', trackingData);
            return res;
        } catch (err) {
            console.error('Failed to submit habit tracking' + err.message);
            throw err;
        }
    };

    const submitDailyHabit = async (habitId, payload = {}, minutes) => {
        try {
            const res = await api.post(`/HabitTracking/${habitId}/submit-daily?minutes=${minutes}`, payload);
            return res;
        } catch (err) {
            console.error('Failed to submit habit tracking' + err.message);
            throw err;
        }
    }

    // #region Distribution Methods
    const getWeeklyDistribution = async (payload) => {
        setLoading(true);
        if (!getAccessToken()) {
            setLoading(false);
            return;
        }

        try {
            const res = await api.get('/HabitTracking/get-weekly', { params: payload });
            return res;
        }
        catch (err) {
            console.error('Failed to get weekly distribution' + err.message);
            throw err;
        }
    }

    const getMonthlyDistribution = async (payload) => {
        setLoading(true);
        if (!getAccessToken()) {
            setLoading(false);
            return;
        }

        try {
            const res = await api.get('/HabitTracking/get-monthly', { params: payload });
            return res;

        } catch (err) {
            console.error('Failed to get monthly distribution' + err.message);
            throw err;
        }
    }

    const getYearlyDistribution = async (payload) => {
        setLoading(true);
        if (!getAccessToken()) {
            setLoading(false);
            return;
        }

        try {
            const res = await api.get('/HabitTracking/get-yearly', { params: payload });
            return res;
        } catch (err) {
            console.error('Failed to get yearly distribution' + err.message);
            throw err;
        }
    }

    //#endregion

    return (
        <HabitTrackingContext.Provider value={{ habitTrackings, loading, habitStats, habitOverview, getHabitTrackingDates, submitHabitTracking, getHabitStats, submitDailyHabit, getWeeklyDistribution, getMonthlyDistribution, getYearlyDistribution, getHabitOverview }}>
            {children}
        </HabitTrackingContext.Provider>
    );
}
