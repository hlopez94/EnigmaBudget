FROM node:16-alpine AS build
WORKDIR /usr/local/app
COPY . /usr/local/app/
#Configurar e instalar pnpm
RUN npm install pnpm@8 -g
RUN pnpm i
#Configurar variables de entorno
#Compilar aplicación angular
RUN pnpm run build:docker

FROM nginx:alpine
#Configrar nginx
RUN rm /etc/nginx/conf.d/default.conf
COPY nginx.conf /etc/nginx/conf.d
#Copiar datos 'dist' de aplicación
COPY --from=build /usr/local/app/dist/enigma-ui /usr/share/nginx/html
CMD ["/bin/sh",  "-c",  "envsubst < /usr/share/nginx/html/assets/env.template.js > /usr/share/nginx/html/assets/env.js && exec nginx -g 'daemon off;'"]
EXPOSE 80