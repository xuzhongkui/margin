import { apiRequest } from "./api";

export const getConnectedDevices = async () => {
  const response = await apiRequest("/device/connected", {
    method: "GET",
  });

  if (!response.ok) {
    const txt = await response.text();
    throw new Error(txt || "Failed to load connected devices");
  }

  return response.json();
};

export const triggerComPortScan = async (deviceId) => {
  const response = await apiRequest(
    `/device/scan-com-ports/${encodeURIComponent(deviceId)}`,
    {
      method: "POST",
    }
  );

  if (!response.ok) {
    const txt = await response.text();
    throw new Error(txt || "Failed to trigger scan");
  }

  return response.json();
};

export const getComSnapshot = async (deviceId) => {
  const response = await apiRequest(
    `/device/com-snapshot/${encodeURIComponent(deviceId)}`,
    {
      method: "GET",
    }
  );

  if (!response.ok) {
    const txt = await response.text();
    throw new Error(txt || "Failed to load COM snapshot");
  }

  return response.json();
};

export const upsertComSnapshot = async (deviceId, ports) => {
  const response = await apiRequest(
    `/device/com-snapshot/${encodeURIComponent(deviceId)}`,
    {
      method: "POST",
      body: { Ports: Array.isArray(ports) ? ports : [] },
    }
  );

  if (!response.ok) {
    const txt = await response.text();
    throw new Error(txt || "Failed to save COM snapshot");
  }

  return response.json();
};
