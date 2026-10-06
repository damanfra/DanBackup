# Gera um único DanBackup.exe (não precisa de .NET instalado) em .\publish
dotnet publish src/DanBackup.App -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -o publish
