$URL = "https://dl.filippo.io/mkcert/latest?for=windows/amd64"
$MkCertPath = ".\.tmp\mkcert.exe"
$Folder = ".\.tmp\"
$SslAngularFolder = "..\..\..\enigma-ui-angular\.ssl\"

If(-Not (Test-Path $Folder)){
	New-Item $Folder -ItemType Directory
}

If(-Not (Test-Path $MkCertPath)){
	Invoke-WebRequest -URI $URL -OutFile $MkCertPath
}

Set-Location .\.tmp
.\mkcert -install
.\mkcert -cert-file "cert.pem" -key-file "key.pem" localhost


If(-Not (Test-Path $SSLAngularFolder)){
	New-Item $SSLAngularFolder -ItemType Directory
}

Copy-Item -Path "*.pem" -Destination $SslAngularFolder
Write-Output $SslAngularFolder
Remove-Item -Path ".\*.pem"
Set-Location ..
exit