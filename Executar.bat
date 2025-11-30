@echo off
cd /d "%~dp0"
echo ========================================
echo    PoE2 Item Inspector
echo ========================================
echo.
echo [INFO] Iniciando aplicacao...
echo [INFO] Procure pela janela "PoE2 Inspector"
echo [INFO] Ela pode estar minimizada ou atras de outras janelas
echo.
start "" "bin\Debug\net8.0-windows\PoE2Inspector.exe"
timeout /t 2 /nobreak >nul
echo.
echo [OK] Aplicacao iniciada!
echo.
echo INSTRUCOES:
echo 1. Procure a janela "PoE2 Inspector" (pode estar minimizada)
echo 2. Clique em "Testar Parser" para verificar funcionamento
echo 3. No PoE2: passe o mouse sobre item e pressione Alt+E
echo.
echo Pressione qualquer tecla para fechar este terminal...
pause >nul
