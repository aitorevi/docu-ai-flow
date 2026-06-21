@echo off
cd /d "%~dp0"
dotnet run --project src\InvoiceProcessor.Worker\InvoiceProcessor.Worker.csproj
pause
