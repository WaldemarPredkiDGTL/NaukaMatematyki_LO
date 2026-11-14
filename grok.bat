@echo off
title Grok - Uruchamianie z PowerShell

:: Konsola Windows-1250 (polskie znaki w cmd).
:: To NIE zmienia kodowania plików, które Grok zapisuje narzędziami edycji.
chcp 1250 >nul
set PYTHONIOENCODING=cp1250

:: Uruchamia PowerShell i od razu wykonuje komendę Grok
powershell.exe -NoExit -Command "[Console]::InputEncoding = [System.Text.Encoding]::GetEncoding(1250); [Console]::OutputEncoding = [System.Text.Encoding]::GetEncoding(1250); $OutputEncoding = [System.Text.Encoding]::GetEncoding(1250); Write-Host 'Uruchamiam Grok (konsola CP1250)...' -ForegroundColor Cyan; & 'grok'"

:: Jeśli używasz np. Pythonowego wrappera Grok:
:: powershell.exe -NoExit -Command "cd C:\ścieżka\do\twojego\projektu; python grok.py"