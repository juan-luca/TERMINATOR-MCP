@echo off
REM -----------------------------
REM 1) Inicia AgentAPI en nueva ventana
REM -----------------------------
echo Iniciando AgentAPI...
start "AgentAPI" cmd /k "dotnet run --project AgentAPI"

REM -----------------------------
REM 2) Espera para asegurarse de que la API esté corriendo
REM    (ajusta el número de segundos si lo crees necesario)
REM -----------------------------
timeout /t 10 /nobreak >nul

REM -----------------------------
REM 3) Inicia AgentWorker en otra ventana
REM -----------------------------
echo Iniciando AgentWorker...
start "AgentWorker" cmd /k "dotnet run --project AgentWorker"

REM -----------------------------
REM Fin del script
REM -----------------------------
