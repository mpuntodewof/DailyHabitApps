import { jwtDecode, type JwtPayload } from "jwt-decode";
import api from "../api/axiosInstance";

export interface AppJwtPayload extends JwtPayload {
  sub?: string;
  email?: string;
  username?: string;
  role?: string | string[];
}

let _accessToken: string | null = null;

export const getAccessToken = (): string | null => _accessToken;

export const setAccessToken = (t: string | null | undefined): void => {
  _accessToken = t || null;
};

export const clearAccessToken = (): void => {
  _accessToken = null;
};

export const isTokenExpired = (token: string): boolean => {
  try {
    const { exp } = jwtDecode<AppJwtPayload>(token);
    if (!exp) return true;
    return Date.now() >= exp * 1000;
  } catch {
    return true;
  }
};

export const buildUserProfile = (accessToken: string): AppJwtPayload | null => {
  try {
    const decoded = jwtDecode<AppJwtPayload>(accessToken);
    return decoded || null;
  } catch {
    return null;
  }
};

export const refreshAccessToken = async (): Promise<string | null> => {
  try {
    const res = await api.post("/Auth/refresh-token", null);
    const newAccess: string | undefined = res.data?.result?.accessToken;
    if (!newAccess) {
      clearAccessToken();
      return null;
    }
    setAccessToken(newAccess);
    return newAccess;
  } catch (err: unknown) {
    // 401 here is expected on first visit (no refresh cookie yet) — don't log it as an error.
    const status =
      typeof err === "object" && err !== null && "response" in err
        ? (err as { response?: { status?: number } }).response?.status
        : undefined;
    if (status !== 401) {
      console.error("Failed to refresh token:", err);
    }
    clearAccessToken();
    return null;
  }
};
