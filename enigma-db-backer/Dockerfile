FROM alpine:latest
ARG CRON_SCHEDULE

# Instalo paquetes requeridos
RUN apk update && \
    apk add --no-cache bash && \
    apk add --no-cache tzdata && \
    apk add --no-cache dcron && \
    apk add --no-cache gettext && \
    apk add --no-cache tini && \
    apk add --no-cache mysql-client

# Copio archivos crontab y script
ADD crontab /backer-cron.template
ADD backer-script.sh /backer-script.sh

# Substituyo el valor de cron_schedule en template
RUN awk -v schedule="${CRON_SCHEDULE}" '{gsub(/CRON_SCHEDULE/, schedule)}1' /backer-cron.template > /etc/cron.d/backer-cron

# Doy permisos
RUN chmod +x /etc/cron.d/backer-cron  
RUN chmod +x /backer-script.sh

# Creo los archivos de log
RUN mkdir -p /var/cron_logs
RUN touch /var/cron_logs/cron.log

# Inicializo cron.d y configuro para que haga dump de logs en el archivo correspondiente
CMD ["/sbin/tini", "--", "crond", "-f", "-L", "/var/cron_logs/cron.log"]