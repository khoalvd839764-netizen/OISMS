# Build stage
FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
# Vite nhúng URL API lúc build; không truyền thì code dùng mặc định http://localhost:5000
ARG VITE_API_BASE_URL
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
ARG VITE_ADMIN_URL=http://localhost:5173
ENV VITE_ADMIN_URL=$VITE_ADMIN_URL
RUN npm run build

# Production stage with NGINX
FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
