# Herramientas

## Crear Certificado SSL Autofirmado
Se incluye un pequeño script 

### Windows
Para Windows (64bit) se incluye un script PowerShell 

⚠️ Recordar habilitar la ejecución de scripts sin firmar con el siguiente script

```powershell
Set-ExecutionPolicy -ExecutionPolicy Unrestricted -Scope CurrentUser
```

Para ejecutar el script 

```powershell
cd create-self-signed-ssl-cert
.\win64-create-cert.ps1
```

- El mismo descargará mkcert, e instalará el certificado en el sistema

- Presioná Instalar para guardar el certificado y sea aceptado por tu navegador.

- El script copiará los certificados a la carpeta de Angular para que pueda inicializar con SSL a fin de probar las características que lo requieran.

### Linux
> 🕜 Pendiente


## Créditos
- [MkCert - GitHub](https://github.com/FiloSottile/mkcert): 
A simple zero-config tool to make locally trusted development certificates with any names you'd like.
