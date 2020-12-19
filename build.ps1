Param(
    [switch] $IsRelease = $false,
    [string] $rid = 'win10-x64')

if($IsRelease) {
    dotnet publish src/Migrondi -c Release -r "linux-x64" --self-contained true -p:PublishSingleFile=true -o "dist/linux-x64"
    dotnet publish src/Migrondi -c Release -r "linux-arm64" --self-contained true -p:PublishSingleFile=true -o "dist/linux-arm64"
    dotnet publish src/Migrondi -c Release -r "win10-x64" --self-contained true -p:PublishSingleFile=true -o "dist/win10-x64"
    dotnet pack src/Migrondi -c Release 
} else {
    dotnet publish src/Migrondi -c Release -r $rid --self-contained true -p:PublishSingleFile=true -o "dist/$rid"
}


