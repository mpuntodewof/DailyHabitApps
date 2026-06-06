export interface CookieOptions {
  path?: string;
  secure?: boolean;
  sameSite?: "strict" | "lax" | "none";
  maxAge?: number;
}

export const setCookie = (name: string, value: string, options: CookieOptions = {}): void => {
  const defaults: CookieOptions = {
    path: "/",
    secure: true,
    sameSite: "strict",
  };

  const opts: CookieOptions = { ...defaults, ...options };
  let cookieString = `${name}=${encodeURIComponent(value)}`;

  if (opts.maxAge) cookieString += `; max-age=${opts.maxAge}`;
  if (opts.path) cookieString += `; path=${opts.path}`;
  if (opts.secure) cookieString += "; secure";
  if (opts.sameSite) cookieString += `; samesite=${opts.sameSite}`;

  document.cookie = cookieString;
};

export const getCookie = (name: string): string | null => {
  const match = document.cookie.match(new RegExp("(^| )" + name + "=([^;]+)"));
  return match ? decodeURIComponent(match[2]) : null;
};

export const deleteCookie = (name: string): void => {
  document.cookie = name + "=; Max-Age=0; path=/";
};
