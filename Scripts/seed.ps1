# Aplica migraciones (si faltan) e inserta datos demo para AMPERA.
# Requisito: cadena SQL en appsettings.json o variable de entorno DefaultConnection.
# Ejecutar desde la raíz del repo o desde esta carpeta:
#   .\Scripts\seed.ps1

$ErrorActionPreference = "Stop"
$projectDir = Join-Path $PSScriptRoot "..\AuditWorkflow"
Set-Location $projectDir
dotnet run -- --seed
