$URL = "https://dl.filippo.io/mkcert/latest?for=windows/amd64"
$Path = ".\.tmp\mkcert.exe"
$Folder= ".\.tmp\"

If(-Not (Test-Path $Folder)){
	New-Item $Folder -ItemType Directory
}

Invoke-WebRequest -URI $URL -OutFile $Path

cd .tmp
.\mkcert -install
.\mkcert -cert-file "cert.pem" -key-file "key.pem" localhost
Copy-Item -Path ".\*.pem" -Destination "..\..\..\enigma-ui-angular\.ssl\"
Remove-Item -Path ".\*.pem"

cd ..