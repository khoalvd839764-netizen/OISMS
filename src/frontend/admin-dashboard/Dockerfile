# Build stage
FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
# Vite nhúng URL API lúc build; không truyền thì code dùng mặc định http://localhost:5000
ARG VITE_API_BASE_URL
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
# Địa chỉ app giả lập đơn sàn cho nút "Mở app giả lập"; để trống thì ẩn nút
ARG VITE_SIMULATOR_URL
ENV VITE_SIMULATOR_URL=$VITE_SIMULATOR_URL
ARG VITE_POS_URL=http://localhost:5174
ENV VITE_POS_URL=$VITE_POS_URL
RUN npm run build

# Production stage with NGINX
FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
