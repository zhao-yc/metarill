# Web 同源反向代理配置

平台管理端和完整业务应用的登录限流、登录失败记录及 Agent 公开接口使用客户端 IP。Web 前端以部署域名访问同源 `/api` 时，由反向代理将该路径原样转到 ASP.NET Core 宿主。若沿用默认配置，宿主采用 TCP 连接对端的 IP，代理后的客户端会共用同一限流来源。

同一台主机上的代理可以连接只监听回环的后端。分主机部署时，后端应只允许指定代理到达。将下例合并到平台私有设置或完整独立包的 `appsettings.local.json`，地址必须与宿主观察到的直接代理 TCP 对端一致。生产服务通过环境配置时，等价键为 `Metarill__ReverseProxy__TrustedIps__0`，后续地址顺延编号。

```json
{
  "Metarill": {
    "ReverseProxy": {
      "TrustedIps": ["127.0.0.1"]
    }
  }
}
```

在直连互联网客户端的 Nginx 网关上，用 `proxy_set_header X-Forwarded-For $remote_addr;` 覆盖来自客户端的转发头，并保留 `/api` 路径。其它网关同样应写入自己核实的客户端地址。如果网关前还有负载均衡器，应先在网关核实并还原客户端地址，再传给应用。Metarill 只读取直接代理提供的最后一项 `X-Forwarded-For`，只接受明确 IP。请求的 Host 和 Scheme 沿用原处理。ASP.NET Core 全局 `FORWARDEDHEADERS_ENABLED` 开关须关闭。 [ASP.NET Core 反向代理文档](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)

未配置时，限流继续按连接对端计算。已有完整应用先用受管运行时更新同步宿主及依赖，再按本页补私有配置并重启。独立包的 AppID 和业务手写扩展按现有更新流程保留。部署验收应从两个实际客户端各自访问原登录入口，同时确认后端监听端口只对受信代理开放。

### Caddy 与后端在同一主机

下面的站点块适用于 Caddy 直接接收客户端连接，后端监听同一主机的 `127.0.0.1:8088`。将示例域名、后端端口和静态目录换成发行环境对应值。API 匹配包含 `/api` 和其下路径，静态首页回退仅在其余路径执行。转发头由 Caddy 连接的直接对端地址覆盖。 [Caddy reverse_proxy](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy)

```caddy
metarill.example.com {
    @api path /api /api/*
    handle @api {
        reverse_proxy 127.0.0.1:8088 {
            header_up X-Forwarded-For {http.request.remote.host}
        }
    }

    handle {
        root * /srv/metarill-web
        try_files {path} /index.html
        file_server
    }
}
```

`TrustedIps` 按上方示例登记 `127.0.0.1`。静态目录只挂原 H5 构建产物。私有设置由后端运行账户读取，发布物可只读挂载，运行目录和临时目录按所用 UID 单独赋权。实际回环验收、登录审计与重启结果见 [V627](testing/current-caddy-release-v627.md)。Caddy HTTPS、证书正反例及原桌面浏览器见 [V628](testing/current-caddy-https-v628.md)。独立 Full 同源 HTTPS、私有工作目录和原移动客户 CRUD 见 [V629](testing/current-full-caddy-mobile-v629.md)。
