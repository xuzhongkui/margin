import * as signalR from "@microsoft/signalr";

const getHubUrl = () => {
  // 默认同源：WebApi 通常与前端同域部署时最稳。
  const base = import.meta.env.VITE_SIGNALR_BASE_URL || window.location.origin;
  return `${base}/hubs/device`;
};

export const createDeviceHubConnection = () => {
  return new signalR.HubConnectionBuilder()
    .withUrl(getHubUrl())
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Information)
    .build();
};
