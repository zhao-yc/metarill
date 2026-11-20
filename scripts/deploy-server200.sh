#!/usr/bin/env bash
set -euo pipefail

fail() { printf '%s\n' "$*" >&2; exit 1; }

# 所有 Docker 操作均在该 SSH 会话核对主机名、网卡地址和架构后执行。
[[ $(hostname -s) == Server-200 && $(uname -m) == x86_64 ]] &&
    ip -o -4 addr show scope global | grep -Eq '[[:space:]]192\.168\.31\.200/' || fail '目标主机身份不符。'

action=$1 instance=$2 port=$3 part=$4
shift 4
[[ $instance =~ ^metarill(-dev-check-[0-9a-f]{8})?$ && $port =~ ^[0-9]{4,5}$ ]] || fail '部署目标无效。'
[[ $part == frontend || $part == backend || $part == all ]] || fail '更新范围无效。'
root=/opt/yinzon/apps/$instance
url=http://192.168.31.200:$port
export METARILL_DEPLOY_PORT=$port

if [[ $action == cleanup || $action == apply ]]; then
    run=$1
    [[ $run =~ ^[0-9a-f]{32}$ ]] || fail '上传编号无效。'
    archive=/tmp/metarill-upload-$run.tar.gz
    if [[ $action == cleanup ]]; then rm -f -- "$archive"; exit 0; fi
    trap 'rm -f -- "$archive"' EXIT
fi

check_target() {
    [[ ! -L $root ]] || fail '目标目录为符号链接。'
    if [[ -d $root && ! -f $root/.metarill-deploy ]]; then
        # 首次启动前允许管理员只放置本环境私有配置，拒绝接管其它应用目录。
        [[ -z $(find "$root" -mindepth 1 -maxdepth 1 ! -name private -print -quit) ]] || fail '目标目录已有其他用途。'
    fi
    if [[ $part != frontend ]]; then
        [[ -f $root/private/settings.json && ! -L $root/private && ! -L $root/private/settings.json ]] ||
            fail "缺少开发配置 $root/private/settings.json，请按 README 配置专用开发库与 Redis。"
    fi
}

check_target
docker compose version --short >/dev/null
if [[ $action == check ]]; then
    printf '已核对 Server-200，目标 %s，更新范围 %s，访问地址 %s\n' "$root" "$part" "$url"
    exit 0
fi
[[ $action == apply ]] || fail '未知部署操作。'
checksum=$2 revision=$3
[[ $checksum =~ ^[0-9a-f]{64}$ && $revision =~ ^[0-9a-f]{40}$ ]] || fail '上传摘要或源码提交无效。'
printf '%s  %s\n' "$checksum" "$archive" | sha256sum --check --status || fail '上传文件的 SHA-256 不符。'

exec 9>"/run/lock/$instance-deploy.lock"
flock -n 9 || fail '同一实例已有更新在进行，请稍后重试。'
check_target
umask 022
mkdir -p -- "$root" "$root/backend" "$root/web"
cd -- "$root"
touch .metarill-deploy
stage=$(mktemp -d "$root/.deploy-$run-XXXXXXXX")
trap 'rm -rf -- "$stage"; rm -f -- "$archive"' EXIT
tar --extract --gzip --file "$archive" --directory "$stage" --no-same-owner --no-same-permissions

if [[ $part != frontend ]]; then
    test -f "$stage/backend/host/Metarill.Api.dll" || fail '上传包缺少 API 程序。'
    tar --extract --file "$stage/backend/source.tar" --directory "$stage/backend" --no-same-owner --no-same-permissions
    rm -- "$stage/backend/source.tar"
    test -f "$stage/backend/global.json" || fail '上传包缺少在线生成源码。'
    # Docker 在只读源码目录下挂载持久状态时，挂载点必须已经存在。
    mkdir -- "$stage/backend/.local"
    install -d -m 0750 -o 1654 -g 1654 state
fi
if [[ $part != backend ]]; then
    test -s "$stage/web/index.html" || fail '上传包缺少 Web 首页。'
fi

# 仅在 Server-200 专用端口提供同源 H5 与 API。发布目录只读，上传文件和运行状态单独挂载。
cat >"$stage/compose.yaml" <<'YAML'
services:
  api:
    image: mcr.microsoft.com/dotnet/aspnet:10.0
    user: "1654:1654"
    working_dir: /app
    command: ["dotnet", "host/Metarill.Api.dll", "--urls", "http://0.0.0.0:8080"]
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      METARILL_SETTINGS: /config/settings.json
    volumes:
      - ./backend/current:/app:ro
      - ./state:/app/.local
      - ./private/settings.json:/config/settings.json:ro
    restart: unless-stopped
  web:
    image: caddy:2-alpine
    ports:
      - "192.168.31.200:${METARILL_DEPLOY_PORT}:8080"
    volumes:
      - ./web:/srv:ro
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
    restart: unless-stopped
YAML
cat >"$stage/Caddyfile" <<'CADDY'
http://:8080 {
    @api path /api /api/*
    handle @api {
        reverse_proxy api:8080 {
            header_up X-Forwarded-For {http.request.remote.host}
        }
    }
    handle {
        root * /srv/current
        file_server
    }
}
CADDY
# 网关文件首次部署时写入。以后沿用该网关，代码更新时无需重新创建容器。
if [[ ! -f compose.yaml ]]; then install -m 0644 "$stage/compose.yaml" compose.yaml; fi
if [[ ! -f Caddyfile ]]; then install -m 0644 "$stage/Caddyfile" Caddyfile; fi
compose=(docker compose --progress quiet --project-name "$instance" --file compose.yaml)
"${compose[@]}" up -d --no-deps web

activate() {
    local component=$1
    # 版本目录和链接均在同一文件系统。Caddy 重新读取链接，API 下方按新挂载重建。
    printf '%s\n' "$revision" >"$stage/$component/.revision"
    mv -T -- "$stage/$component" "$component/$run"
    ln -s -- "$run" "$stage/current"
    mv -Tf -- "$stage/current" "$component/current"
}

prune_versions() {
    local component=$1 previous
    for previous in "$component"/*; do
        [[ ${previous##*/} =~ ^[0-9a-f]{32}$ && $previous != "$component/$run" && -f $previous/.revision ]] || continue
        rm -rf -- "$previous"
    done
}

check_api() {
    local response
    response=$(curl -fs --max-time 3 "$url/api/runtime/health/check") || return 1
    grep -Eq '"code"[[:space:]]*:[[:space:]]*200([,}])' <<<"$response" &&
        grep -Eq '"application"[[:space:]]*:[[:space:]]*"Metarill"' <<<"$response"
}

check_web() {
    curl -fs --max-time 3 "$url/" -o "$stage/index.response" &&
        cmp -s "$stage/index.response" web/current/index.html
}

wait_for() {
    local attempt
    for ((attempt=0; attempt<20; attempt++)); do
        if "$1"; then return 0; fi
        sleep 1
    done
    fail "健康检查 $1 失败，请在 $root 查看 $instance 的 Compose 服务日志。"
}

if [[ $part != frontend ]]; then
    activate backend
    "${compose[@]}" up -d --no-deps --force-recreate api
    wait_for check_api
    prune_versions backend
fi
if [[ $part != backend ]]; then
    activate web
    wait_for check_web
    prune_versions web
fi
printf 'Server-200 更新成功，范围 %s，Git 提交 %s，访问地址 %s\n' "$part" "$revision" "$url"
