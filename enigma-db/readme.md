# Scripts para despliegue en contenedores
Esta sección contendrá todo lo relacionado a la estructura de la basa de datos, con scripts que apuntan a brindar la posibilidad de tener un seguimiento de los cambios sobre el modelo de datos y además la posibilidad de desplegar cambios a producción de manera automatizada a fin de automatizar el mantenimiento del modelo de la base de datos.

# DDL - Modelo de Datos
En la carpeta `./ddl/tables`, se encuentra la definición de cada tabla, y sus declaraciones relacionadas incluídos:
- Triggers
- Stored Procedures y Funciones
- Constraints
- Carga de datos fijos -para el caso de enums, y tipos-)

# Nivelado automático de la base de datos
Al iniciar el container, los servicios se ejecutan con las siguientes dependencias:
- db_seeder => db
- api => db
- ui => api

El servicio `db_seeder` ejecutará en el servidor el archivo [deploy.sh](./deploy-script/deploy.sh) en cada inicio del container, el mismo se encargará de esperar a que la conexión a la Base de Datos esté disponible (es decir, se haya inicializado), y comenzará a ejecutar en orden los scripts SQL incluídos en [deploy-scripts](./deploy-scripts/), los mismos deberán estar basados en el archivo modelo [deploy_script_modelo.sql](.deploy_script_modelo.sql).

Con cada nueva versión con modificaciones en la estructura mergeada a una de las ramas con tag `release` se sugiere incluir un script con nueva versión, siguiendo siempre el formato `x.y.z`