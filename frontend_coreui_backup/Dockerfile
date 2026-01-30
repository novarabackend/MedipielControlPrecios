FROM node:20-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build -- --configuration production

FROM nginx:1.25-alpine
COPY --from=build /app/dist/coreui-free-angular-admin-template/ /usr/share/nginx/html
EXPOSE 80
