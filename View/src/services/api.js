import router from "../router";
import { API_BASE_URL } from "../config/app.config";
import { notifyUnauthorized } from "./unauthorized-events";
import {
  clearAuthTokens,
  getAccessToken,
  getRefreshToken,
  setAuthTokens,
} from "./auth";

const API_BASE = API_BASE_URL;

const refreshAccessToken = async () => {
  const refreshToken = getRefreshToken();
  if (!refreshToken) {
    return "";
  }

  const response = await fetch(`${API_BASE}/users/refresh`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ refreshToken }),
  });

  if (!response.ok) {
    return "";
  }

  const data = await response.json();
  setAuthTokens({
    accessToken: data.accessToken,
    refreshToken: data.refreshToken,
    user: data.user,
  });

  return data.accessToken || "";
};

export const apiRequest = async (path, options = {}) => {
  const {
    method = "GET",
    body,
    headers = {},
    skipAuth = false,
    skipRefresh = false,
  } = options;

  const requestHeaders = { ...headers };
  if (body !== undefined) {
    requestHeaders["Content-Type"] = "application/json";
  }

  const accessToken = skipAuth ? "" : getAccessToken();
  if (accessToken) {
    requestHeaders.Authorization = `Bearer ${accessToken}`;
  }

  const response = await fetch(`${API_BASE}${path}`, {
    method,
    headers: requestHeaders,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (response.status !== 401 || skipRefresh) {
    return response;
  }

  const newToken = await refreshAccessToken();
  if (!newToken) {
    clearAuthTokens();
    notifyUnauthorized();
    const currentPath = router.currentRoute.value.path;
    if (currentPath !== "/login") {
      router.push({
        path: "/login",
        query: { redirect: router.currentRoute.value.fullPath || "/" },
      });
    }
    return response;
  }

  return fetch(`${API_BASE}${path}`, {
    method,
    headers: {
      ...requestHeaders,
      Authorization: `Bearer ${newToken}`,
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
};

export const login = async (userName, password) => {
  const response = await apiRequest("/users/login", {
    method: "POST",
    body: { userName, password },
    skipAuth: true,
    skipRefresh: true,
  });

  if (!response.ok) {
    throw new Error("Login failed");
  }

  const data = await response.json();
  setAuthTokens({
    accessToken: data.accessToken,
    refreshToken: data.refreshToken,
    user: data.user,
  });

  return data;
};
