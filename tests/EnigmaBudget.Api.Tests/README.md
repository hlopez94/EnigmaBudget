# Pruebas de integración de EnigmaBudget.Api

Las pruebas usan PostgreSQL real y crean un esquema temporal por ejecución de fixture. Requieren un servidor local accesible en `127.0.0.1:55432` y una base `enigmabudget` con el usuario de desarrollo `enigma`. La contraseña `local-development-only` solo debe existir en configuración local de pruebas. El fixture elimina su esquema al terminar.

Ejecutar desde la raíz:

```sh
dotnet test EnigmaBudget.sln
```

Para otra instancia, actualizar la cadena de conexión en `ApiFactory` antes de correr el conjunto.
