#!/bin/bash

# Seteo numero de backups a mantener
MAX_BACKUPS=$MAX_DB_BACKUPS

# Obtengo checksum del estado actual de la base de datos
CURRENT_CHECKSUM=$(mysqldump -h "$PRIMARY_DB_HOST" -u "$PRIMARY_DB_USER" -p"$PRIMARY_DB_PASSWORD" "$PRIMARY_DB_NAME" | head -n -2 | md5sum | awk '{print $1}')


# Obtengo checksum del backup previo (si existe)
PREVIOUS_CHECKSUM=""
if [ -f backups/latest_checksum.txt ]; then
  PREVIOUS_CHECKSUM=$(cat backups/latest_checksum.txt)
fi

# Comparo checksum actual con el previo
if [ "$CURRENT_CHECKSUM" != "$PREVIOUS_CHECKSUM" ]; then
  echo "$(date +'%Y-%m-%d %H:%M:%S'): Cambios encontrados. Generando nuevo backup..."
  
  # Descargo datos de Base de Datos
  mysqldump -h "$PRIMARY_DB_HOST" -u "$PRIMARY_DB_USER" -p"$PRIMARY_DB_PASSWORD" "$PRIMARY_DB_NAME" > backup.sql
  
  # Roto backups
  if [ -d backups ]; then
    # Elimino el backup más antigo si se excede el límite de backups
    if [ $(ls backups/ -1 | wc -l) -ge $MAX_BACKUPS ]; then
      BACKUP_ANTERIOR=$(ls backups/ -1t | tail -1)
      rm backups/$BACKUP_ANTERIOR
    fi
  else
    mkdir backups
  fi
  
  # Muevo el backup actual al directorio de backups
  BACKUP_FILENAME="backup_$(date +"%Y%m%d%H%M%S").sql"
  mv backup.sql backups/$BACKUP_FILENAME
  
  # Se guarda el checksum del backup actual para contrastar a futuro
  echo "$CURRENT_CHECKSUM" > backups/latest_checksum.txt
  
  # Importo backup en base de datos de backup
  mysql -h "$BACKUP_DB_HOST" -u "$BACKUP_DB_USER" -p"$BACKUP_DB_PASSWORD" "$BACKUP_DB_NAME" < backups/$BACKUP_FILENAME
  
else
  echo "$(date +'%Y-%m-%d %H:%M:%S'): Sin cambios detectados. No se realizará backup."
fi

  echo "$(date +'%Y-%m-%d %H:%M:%S'): Proceso de backup finalizado."