# WM Portal — build from the repo root:  docker build -f frontend/portal/Dockerfile .
FROM node:20-alpine AS build
WORKDIR /src

COPY frontend/portal/package*.json ./
RUN npm ci

COPY frontend/portal/ ./
RUN npm run build -- --configuration production

FROM nginx:1.27-alpine AS runtime

# nginx serves the SPA and proxies /api and /hubs to the API container, so the
# browser only ever talks to one origin — no CORS, no baked-in API URL.
COPY frontend/portal/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/portal/browser /usr/share/nginx/html

EXPOSE 8080

# 127.0.0.1 not localhost: nginx binds IPv4 only, and localhost resolves to ::1
# first inside the container, which would fail the check against a healthy server.
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
  CMD wget -qO- http://127.0.0.1:8080/healthz || exit 1
