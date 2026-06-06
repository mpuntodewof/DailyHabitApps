import axios from "axios";
import { getAccessToken, isTokenExpired, refreshAccessToken } from "../utils/tokenUtils";

const baseURL = import.meta.env.VITE_API_URL || "/api";

const ANONYMOUS_ENDPOINTS = [
  "/Auth/login",
  "/Auth/register",
  "/Auth/forgot-password",
  "/Auth/reset-password",
  "/Auth/refresh-token",
];

const axiosInstance = axios.create({
  baseURL: baseURL,
  headers: {
    "Content-Type": "application/json",
    Accept: "application/json",
  },
  withCredentials: true,
  timeout: 10000,
});

let isRefreshing = false;
let failedQueue = [];

const processQueue = (error, token = null) => {
  failedQueue.forEach((p) => (error ? p.reject(error) : p.resolve(token)));
  failedQueue = [];
};

axiosInstance.interceptors.request.use(
  (config) => {
    try {
      const isAnonymous = ANONYMOUS_ENDPOINTS.some((endpoint) =>
        config.url?.includes(endpoint),
      );

      if (!isAnonymous) {
        const token = getAccessToken();
        if (token && !isTokenExpired(token)) {
          config.headers.Authorization = `Bearer ${token}`;
        }
      }

      return config;
    } catch (e) {
      console.error("Failed to attach access token:", e.message);
    }

    return config;
  },
  (error) => Promise.reject(error),
);

axiosInstance.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    // Don't try to refresh tokens for the refresh / login / register endpoints themselves —
    // a 401 there means the cookie is missing or invalid; recursing produces an endless loop.
    const isAuthEndpoint = ANONYMOUS_ENDPOINTS.some((endpoint) =>
      originalRequest?.url?.includes(endpoint),
    );

    if (error.response?.status === 401 && !originalRequest._retry && !isAuthEndpoint) {
      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          failedQueue.push({ resolve, reject });
        }).then((token) => {
          originalRequest.headers.Authorization = `Bearer ${token}`;
          return axiosInstance(originalRequest);
        });
      }

      originalRequest._retry = true;
      isRefreshing = true;

      const newToken = await refreshAccessToken();
      if (newToken) {
        processQueue(null, newToken);
        originalRequest.headers["Authorization"] = `Bearer ${newToken}`;
        isRefreshing = false;
        return axiosInstance(originalRequest);
      }

      processQueue(new Error("Refresh token failed"), null);
      isRefreshing = false;
    }

    return Promise.reject(error);
  },
);

export default axiosInstance;
