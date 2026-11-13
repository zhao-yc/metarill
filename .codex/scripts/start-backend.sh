#!/usr/bin/env bash
set -euo pipefail
# Windows入口与VS Code复用同一份进程和端口管理逻辑。
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
exec pwsh -NoProfile -File "$ROOT/scripts/dev.ps1" start
