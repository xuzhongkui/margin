import { apiRequest } from "./api";

/**
 * 获取所有记事本
 */
export const getAllNotes = async () => {
  const response = await apiRequest("/notes");
  if (!response.ok) {
    throw new Error("Failed to fetch notes");
  }
  return response.json();
};

/**
 * 根据ID获取记事本
 */
export const getNoteById = async (id) => {
  const response = await apiRequest(`/notes/${id}`);
  if (!response.ok) {
    throw new Error("Failed to fetch note");
  }
  return response.json();
};

/**
 * 搜索记事本
 */
export const searchNotes = async (keyword, userId, isPinned) => {
  const params = new URLSearchParams();
  if (keyword) params.append("keyword", keyword);
  if (userId) params.append("userId", userId);
  if (isPinned !== undefined) params.append("isPinned", isPinned);
  
  const queryString = params.toString();
  const response = await apiRequest(`/notes/search${queryString ? `?${queryString}` : ""}`);
  if (!response.ok) {
    throw new Error("Failed to search notes");
  }
  return response.json();
};

/**
 * 创建新记事本
 */
export const createNote = async (noteData) => {
  const response = await apiRequest("/notes", {
    method: "POST",
    body: noteData,
  });
  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(errorText || "Failed to create note");
  }
  return response.json();
};

/**
 * 更新记事本
 */
export const updateNote = async (id, noteData) => {
  const response = await apiRequest(`/notes/${id}`, {
    method: "PUT",
    body: noteData,
  });
  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(errorText || "Failed to update note");
  }
  return response.json();
};

/**
 * 删除记事本
 */
export const deleteNote = async (id) => {
  const response = await apiRequest(`/notes/${id}`, {
    method: "DELETE",
  });
  if (!response.ok) {
    throw new Error("Failed to delete note");
  }
  return response.status === 204 ? null : response.json();
};

/**
 * 切换置顶状态
 */
export const togglePinNote = async (id) => {
  const response = await apiRequest(`/notes/${id}/toggle-pin`, {
    method: "PATCH",
  });
  if (!response.ok) {
    throw new Error("Failed to toggle pin status");
  }
  return response.json();
};
