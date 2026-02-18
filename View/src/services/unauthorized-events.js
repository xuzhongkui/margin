const listeners = new Set();

export const onUnauthorized = (callback) => {
  listeners.add(callback);
  return () => listeners.delete(callback);
};

export const notifyUnauthorized = () => {
  listeners.forEach((callback) => callback());
};
