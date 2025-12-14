# Use a Debian-based Node image to avoid native-build issues common on alpine
FROM node:18-bullseye-slim AS build
WORKDIR /app

# Copy package files first to leverage Docker cache for deps layer
COPY package*.json ./

# Install dependencies:
# - Use npm ci if a lockfile exists for reproducible installs
# - Fallback to npm install otherwise
# - Use --legacy-peer-deps to avoid peer-dep failures in CI images
RUN if [ -f package-lock.json ]; then \
      npm ci --silent --legacy-peer-deps; \
    else \
      npm install --silent --legacy-peer-deps; \
    fi

# Copy app sources and build
COPY . .
RUN npm run build

# Serve with nginx
FROM nginx:stable-alpine
COPY --from=build /app/build /usr/share/nginx/html
# optional runtime env placeholder
COPY public/env.js /usr/share/nginx/html/env.js
COPY docker-entrypoint.sh /docker-entrypoint.sh
RUN sed -i 's/\r$//' /docker-entrypoint.sh && chmod +x /docker-entrypoint.sh

EXPOSE 80
ENTRYPOINT ["/docker-entrypoint.sh"]