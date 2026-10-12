@echo off
rem ============================================================
rem  deploy.local.example.bat  -> copy to deploy.local.bat and fill in values
rem  deploy.local.bat is gitignored, do NOT commit.
rem ============================================================

rem 206 remote machine
set "REMOTE_HOST=10.253.6.206"
set "REMOTE_USER=admin"
set "REMOTE_PWD=在这里填 admin 密码"

rem 206 share root (maps to C:\ProgramCustom)
set "REMOTE_SHARE=\\%REMOTE_HOST%\ProgramCustom"
set "STAGING_DIR=%REMOTE_SHARE%\Server\.staging"
