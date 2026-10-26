# Pruebas de integración de EnigmaBudget.Api

Las pruebas usan PostgreSQL real y crean esquemas temporales por fixture. Definí `PGUSER` y `PGPASSWORD` para tu instancia local. El fixture crea y elimina únicamente sus propios esquemas; no limpia tablas compartidas ni necesita credenciales guardadas en el repositorio.

Ejecutar desde la raíz:

```sh
ENIGMABUDGET_TEST_CONNECTION="Host=127.0.0.1;Port=55432;Database=enigmabudget_test;Username=$PGUSER;Password=$PGPASSWORD" dotnet test EnigmaBudget.sln
```

Para usar otra instancia o base, cambiá la variable de entorno. No apuntes a una base con datos de usuario. Playwright requiere su propia `ENIGMABUDGET_E2E_CONNECTION` y una base vacía, independiente de las pruebas de integración.
