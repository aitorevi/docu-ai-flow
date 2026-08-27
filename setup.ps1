# Configuracion inicial del Procesador de Facturas
# Ejecutar una sola vez tras clonar el repositorio.
# Requiere PowerShell 5+ (incluido en Windows 10/11).

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "=== Procesador de Facturas - Configuracion inicial ===" -ForegroundColor Cyan
Write-Host ""

# 1. Verificar .NET SDK >= 10
Write-Host "Comprobando .NET SDK..." -NoNewline
$dotnetVersion = $null
try {
    $dotnetVersion = (dotnet --version 2>$null)
} catch {}

if (-not $dotnetVersion -or -not ($dotnetVersion -match "^10\.")) {
    Write-Host " NO ENCONTRADO" -ForegroundColor Red
    Write-Host ""
    Write-Host "Necesitas instalar .NET SDK 10 o superior." -ForegroundColor Yellow
    Write-Host "Descargalo en: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "Pulsa INTRO para cerrar"
    exit 1
}
Write-Host " OK ($dotnetVersion)" -ForegroundColor Green

# 2. Crear carpetas de datos
Write-Host "Creando carpetas de datos..." -NoNewline
$folders = @("data\inbox", "data\archive", "data\failed", "data\output", "data\pending", "data\duplicates")
foreach ($f in $folders) {
    New-Item -ItemType Directory -Path $f -Force | Out-Null
}
Write-Host " OK" -ForegroundColor Green

# 3. Crear .env si no existe
Write-Host "Configurando variables de entorno..." -NoNewline
if (-not (Test-Path ".env")) {
    Copy-Item ".env.example" ".env"
    Write-Host " OK (creado .env desde .env.example)" -ForegroundColor Green
    Write-Host ""
    Write-Host "El .env es OPCIONAL: sin tocarlo, la app usa el extractor local" -ForegroundColor Cyan
    Write-Host "(por plantillas), sin nube y sin credenciales." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Editalo solo si quieres:" -ForegroundColor Yellow
    Write-Host "  Extraction__Provider=DocumentAi        → extraer con Google Document AI" -ForegroundColor Yellow
    Write-Host "  TemplateExtractor__OcrFallback__Enabled → OCR para facturas escaneadas" -ForegroundColor Yellow
    Write-Host "  Resend__ApiKey / FromAddress / AdvisorAddress → enviar el trimestre por correo" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Puedes editar .env con el Bloc de notas:" -ForegroundColor Cyan
    Write-Host "  notepad .env" -ForegroundColor Cyan
} else {
    Write-Host " OK (.env ya existe)" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Configuracion completada ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Para arrancar la aplicacion, haz doble clic en run.bat" -ForegroundColor White
Write-Host ""
Read-Host "Pulsa INTRO para cerrar"
