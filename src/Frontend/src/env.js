export const getEventBridgeUrl = () => {
  if (window._env && window._env.REACT_APP_EVENTBRIDGE_URL) {
    return window._env.REACT_APP_EVENTBRIDGE_URL;
  }
  return process.env.REACT_APP_EVENTBRIDGE_URL;
};
