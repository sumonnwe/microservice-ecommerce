FROM node:18 AS build
WORKDIR /app

# package.json is at the root of the build context
COPY package.json ./
RUN npm install

# copy the rest of the frontend code
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/build /usr/share/nginx/html
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
