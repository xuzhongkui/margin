import { apiRequest } from "./api";

/**
 * 获取所有COM分配列表
 */
export const getAllComAllocations = async () => {
  const response = await apiRequest("/com-allocations");
  if (!response.ok) {
    throw new Error("Failed to fetch COM allocations");
  }
  return response.json();
};

/**
 * 根据ID获取COM分配
 */
export const getComAllocationById = async (id) => {
  const response = await apiRequest(`/com-allocations/${id}`);
  if (!response.ok) {
    throw new Error("Failed to fetch COM allocation");
  }
  return response.json();
};

/**
 * 创建新的COM分配
 */
export const createComAllocation = async (data) => {
  const response = await apiRequest("/com-allocations", {
    method: "POST",
    body: data,
  });
  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(errorText || "Failed to create COM allocation");
  }
  return response.json();
};

/**
 * 更新COM分配
 */
export const updateComAllocation = async (id, data) => {
  const response = await apiRequest(`/com-allocations/${id}`, {
    method: "PUT",
    body: data,
  });
  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(errorText || "Failed to update COM allocation");
  }
  return response.json();
};

/**
 * 删除COM分配
 */
export const deleteComAllocation = async (id) => {
  const response = await apiRequest(`/com-allocations/${id}`, {
    method: "DELETE",
  });
  if (!response.ok) {
    throw new Error("Failed to delete COM allocation");
  }
  return response.status === 204 ? null : response.json();
};

/**
 * 获取当前登录用户的COM分配
 */
export const getMyComAllocations = async () => {
  const response = await apiRequest("/com-allocations/me");
  if (!response.ok) {
    throw new Error("Failed to fetch my COM allocations");
  }
  return response.json();
};

/**
 * 根据用户ID获取COM分配（管理员）
 */
export const getComAllocationsByUserId = async (userId) => {
  const response = await apiRequest(`/com-allocations/user/${userId}`);
  if (!response.ok) {
    throw new Error("Failed to fetch user COM allocations");
  }
  return response.json();
};
