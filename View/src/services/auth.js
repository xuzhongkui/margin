const ACCESS_TOKEN_KEY = "sms_access_token";
const REFRESH_TOKEN_KEY = "sms_refresh_token";
const USER_KEY = "sms_user";

export const getAccessToken = () => localStorage.getItem(ACCESS_TOKEN_KEY) || "";
export const getRefreshToken = () => localStorage.getItem(REFRESH_TOKEN_KEY) || "";

export const setAuthTokens = ({ accessToken, refreshToken, user }) => {
  if (accessToken) {
    localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
  }
  if (refreshToken) {
    localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
  }
  if (user) {
    localStorage.setItem(USER_KEY, JSON.stringify(user));
  }
};

export const clearAuthTokens = () => {
  localStorage.removeItem(ACCESS_TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
  localStorage.removeItem(USER_KEY);
};

export const isAuthenticated = () => Boolean(getAccessToken());
