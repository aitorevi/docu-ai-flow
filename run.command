#!/usr/bin/env bash
cd "$(dirname "$0")"
dotnet run --project src/InvoiceProcessor.Worker/InvoiceProcessor.Worker.csproj
