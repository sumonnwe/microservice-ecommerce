// Helper to read runtime-injected env first, then compile-time process.env
export const getEventBridgeUrl = () => {
  if (typeof window !== "undefined" && window._env && window._env.REACT_APP_EVENTBRIDGE_URL) {
    return window._env.REACT_APP_EVENTBRIDGE_URL;
  }
  return process.env.REACT_APP_EVENTBRIDGE_URL || "http://localhost:5005/eventhub";
};