#!/bin/sh
set -e

# Default value if not provided
: "${REACT_APP_EVENTBRIDGE_URL:=http://eventbridge:80/eventhub}"

# Inject runtime env into env.js
if [ -f /usr/share/nginx/html/env.js ]; then
  sed -i "s|%%REACT_APP_EVENTBRIDGE_URL%%|${REACT_APP_EVENTBRIDGE_URL}|g" \
    /usr/share/nginx/html/env.js
fi

# Start nginx
exec nginx -g "daemon off;"