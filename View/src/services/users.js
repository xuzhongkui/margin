import { apiRequest } from "./api";

/**
 * 获取所有用户列表
 */
export const getAllUsers = async () => {
  const response = await apiRequest("/users");
  if (!response.ok) {
    throw new Error("Failed to fetch users");
  }
  return response.json();
};

/**
 * 根据ID获取用户
 */
export const getUserById = async (id) => {
  const response = await apiRequest(`/users/${id}`);
  if (!response.ok) {
    throw new Error("Failed to fetch user");
  }
  return response.json();
};

/**
 * 搜索用户
 */
export const searchUsers = async (keyword) => {
  const queryParam = keyword ? `?keyword=${encodeURIComponent(keyword)}` : "";
  const response = await apiRequest(`/users/search${queryParam}`);
  if (!response.ok) {
    throw new Error("Failed to search users");
  }
  return response.json();
};

/**
 * 创建新用户
 */
export const createUser = async (userData) => {
  const response = await apiRequest("/users", {
    method: "POST",
    body: userData,
  });
  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(errorText || "Failed to create user");
  }
  return response.json();
};

/**
 * 更新用户信息
 */
export const updateUser = async (id, userData) => {
  const response = await apiRequest(`/users/${id}`, {
    method: "PUT",
    body: userData,
  });
  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(errorText || "Failed to update user");
  }
  return response.json();
};

/**
 * 删除用户
 */
export const deleteUser = async (id) => {
  const response = await apiRequest(`/users/${id}`, {
    method: "DELETE",
  });
  if (!response.ok) {
    throw new Error("Failed to delete user");
  }
  return response.status === 204 ? null : response.json();
};
