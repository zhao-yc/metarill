import { mkdir, readFile, writeFile, rename, chmod, lstat, unlink, open, rm } from 'node:fs/promises';
import { join, dirname, resolve } from 'node:path';
import { homedir } from 'node:os';
import { randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { CliError } from './errors.mjs';

const delay = (ms) => new Promise((done) => setTimeout(done, ms));
let userSid;

/** 仅限制新建的专用目录，不能因自定义配置路径而改动已有宿主目录 ACL。 */
async function ensureDirectory(directory) {
  const created = await mkdir(directory, { recursive: true, mode: 0o700 });
  const stat = await lstat(directory);
  if (!stat.isDirectory() || stat.isSymbolicLink()) throw new CliError('配置目录必须是普通目录。', 'invalid_config');
  if (created) await restrictPermissions(directory, true);
}

/** POSIX 使用 0600/0700；Windows 移除继承权限并只给当前用户授权。 */
export async function restrictPermissions(path, directory = false) {
  if (process.platform !== 'win32') {
    await chmod(path, directory ? 0o700 : 0o600);
    return;
  }
  try {
    userSid ??= execFileSync('whoami.exe', ['/user', '/fo', 'csv', '/nh'], { encoding: 'utf8', windowsHide: true })
      .match(/S-1-5-[0-9-]+/)?.[0];
    if (!userSid) throw new Error('缺少用户 SID');
    execFileSync('icacls.exe', [path, '/inheritance:r', '/grant:r', `*${userSid}:${directory ? '(OI)(CI)(F)' : '(F)'}`],
      { stdio: 'pipe', windowsHide: true });
  } catch {
    throw new CliError('无法限制配置文件权限，请检查当前用户的文件访问权限。', 'config_permissions');
  }
}

/** 项目 origin 与完整 API 基础路径共同构成凭据边界。 */
export function normalizeBaseUrl(value) {
  let url;
  try { url = new URL(value); } catch { throw new CliError('项目地址必须是包含 API 基础路径的绝对 URL。', 'invalid_url'); }
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.search || url.hash) {
    throw new CliError('项目地址只允许 HTTP/HTTPS，不能包含账号、查询参数或片段。', 'invalid_url');
  }
  if (/%2f|%5c|\\/i.test(url.pathname)) throw new CliError('项目 API 基础路径包含非法分隔符。', 'invalid_url');
  url.pathname = url.pathname.replace(/\/+$/, '');
  if (!url.pathname || url.pathname === '/') throw new CliError('项目地址必须包含 API 基础路径，例如 https://项目域名/api。', 'invalid_url');
  return url.href.replace(/\/$/, '');
}

/** 配置以原子替换写入；轮转刷新在同一锁内重新读取，避免并发使用旧刷新令牌。 */
export class ConfigStore {
  constructor(configPath = process.env.METARILL_AGENT_CONFIG ?? join(homedir(), '.metarill-agent', 'config.json')) {
    this.path = resolve(configPath);
  }

  async read() {
    try {
      const stat = await lstat(this.path);
      if (!stat.isFile() || stat.isSymbolicLink()) throw new CliError('配置路径必须是普通文件。', 'invalid_config');
      const value = JSON.parse(await readFile(this.path, 'utf8'));
      if (value.version !== 1 || !value.profiles || Array.isArray(value.profiles) || typeof value.profiles !== 'object') {
        throw new CliError('配置版本或内容无效。', 'invalid_config');
      }
      return value;
    } catch (error) {
      if (error.code === 'ENOENT') return { version: 1, activeProfile: null, profiles: {} };
      if (error instanceof CliError) throw error;
      throw new CliError('无法读取配置文件，请检查 JSON 与文件权限。', 'invalid_config');
    }
  }

  async write(config) {
    const directory = dirname(this.path);
    await ensureDirectory(directory);
    const temp = `${this.path}.${randomUUID()}.tmp`;
    try {
      await writeFile(temp, `${JSON.stringify(config, null, 2)}\n`, { mode: 0o600, flag: 'wx' });
      await restrictPermissions(temp);
      await rename(temp, this.path);
    } finally {
      await rm(temp, { force: true });
    }
  }

  async locked(callback) {
    const directory = dirname(this.path);
    await ensureDirectory(directory);
    const lockPath = `${this.path}.lock`;
    let lock;
    const deadline = Date.now() + 15_000;
    while (!lock) {
      try {
        lock = await open(lockPath, 'wx', 0o600);
        try { await restrictPermissions(lockPath); }
        catch (error) { await lock.close(); await unlink(lockPath); throw error; }
      } catch (error) {
        if (error.code !== 'EEXIST') throw error;
        if (Date.now() > deadline) throw new CliError('另一 CLI 正在更新配置；若进程已退出，请删除配置目录中的 .lock 文件。', 'config_locked');
        await delay(100);
      }
    }
    try { return await callback(await this.read()); }
    finally { await lock.close(); await unlink(lockPath); }
  }
}

/** 只输出账号、项目和授权状态，绝不输出保存的 token。 */
export function publicProfile(name, profile, active = false) {
  return { name, url: profile.url, active, projectId: profile.projectId ?? null,
    projectName: profile.projectName ?? null, authorized: !!profile.credentials,
    accessExpiresAt: profile.credentials?.expiresAt ? new Date(profile.credentials.expiresAt).toISOString() : null };
}

export function assertProfileName(name) {
  if (!/^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$/.test(name ?? '')) throw new CliError('配置名称限 1–64 位字母、数字、点、下划线或短横线。', 'invalid_profile');
}
