export const getEventBridgeUrl = () => {
  if (window._env && window._env.REACT_APP_EVENTBRIDGE_URL) {
    return window._env.REACT_APP_EVENTBRIDGE_URL;
  }
  const host = window.location.hostname; // localhost or server domain
  return `http://${host}:5005/eventhub`;
};
