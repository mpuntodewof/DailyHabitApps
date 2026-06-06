# syntax=docker/dockerfile:1.7
# Multi-stage build for the Atomic Habits SPA. Final image: nginx serving the dist/ output.

# 1) Build
FROM node:20-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

ARG VITE_API_URL=/api
ENV VITE_API_URL=${VITE_API_URL}

RUN npm run build

# 2) Runtime — nginx serves the static dist/
FROM nginx:1.27-alpine AS runtime

# SPA-aware nginx config: try_files falls back to index.html for client-side routes.
RUN rm /etc/nginx/conf.d/default.conf
COPY nginx.conf /etc/nginx/conf.d/default.conf

COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
