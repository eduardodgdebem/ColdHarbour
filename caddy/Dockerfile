FROM node:22-alpine AS build
WORKDIR /app
COPY ColdHarbourFrontend/package*.json ./
RUN npm ci
COPY ColdHarbourFrontend/ ./
RUN npm run build -- --configuration production

FROM caddy:alpine
COPY --from=build /app/dist/cold-harbour-front-end/browser /srv
COPY caddy/Caddyfile /etc/caddy/Caddyfile
EXPOSE 80
