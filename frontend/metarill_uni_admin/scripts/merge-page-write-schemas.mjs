import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';

/** 校验生成器清单并确定性合并；冲突必须修正生成来源，不能依赖复制顺序覆盖。 */
export function mergePageWriteSchemas(inputs) {
  const pages = new Map();
  const routes = new Map();
  for (const { source, schema } of inputs) {
    if (schema?.schemaVersion !== 1 || !schema.pages || typeof schema.pages !== 'object' || Array.isArray(schema.pages)) throw new Error('写入清单版本或页面集合无效：' + source);
    for (const [id, page] of Object.entries(schema.pages)) {
      if (!/^[0-9]+$/.test(id) || !page || typeof page !== 'object' || !new RegExp('^/admin/auto/[A-Za-z_][A-Za-z0-9_]*/' + id + '$').test(page.route)) throw new Error('写入清单页面路由无效：' + source + ':' + id);
      const normalized = { route: page.route };
      for (const key of ['createFields', 'updateFields']) {
        if (!Array.isArray(page[key]) || page[key].some(field => typeof field !== 'string' || !/^[A-Za-z][A-Za-z0-9]*$/.test(field)) || new Set(page[key]).size !== page[key].length) throw new Error('写入字段集合无效：' + source + ':' + id + ':' + key);
        normalized[key] = [...page[key]].sort();
      }
      const previous = pages.get(id);
      if (previous && JSON.stringify(previous) !== JSON.stringify(normalized)) throw new Error('页面写入清单冲突：' + id);
      if (routes.has(page.route) && routes.get(page.route) !== id) throw new Error('页面写入路由重复：' + page.route);
      routes.set(page.route, id);
      pages.set(id, normalized);
    }
  }
  return { schemaVersion: 1, pages: Object.fromEntries([...pages.entries()].sort(([a], [b]) => a.localeCompare(b, 'en'))) };
}

/** 只读取已生成的 platform/demo 清单，不从手写页面推断可写字段。 */
export async function writeMergedSchema({ sourceRoot, output }) {
  const relative = 'frontend/common/yzp_runtime/page-write-schema.generated.json';
  const inputs = [];
  for (const project of ['platform', 'demo']) {
    const source = resolve(sourceRoot, 'backend/generated', project, relative);
    let schema;
    try { schema = JSON.parse(await readFile(source, 'utf8')); } catch (error) { throw new Error('无法读取生成清单，请先生成 platform 和 demo：' + source + '；' + error.message); }
    inputs.push({ source, schema });
  }
  const merged = mergePageWriteSchemas(inputs);
  const body = JSON.stringify(merged, null, 2) + '\n';
  await mkdir(dirname(output), { recursive: true });
  let previous;
  try { previous = await readFile(output, 'utf8'); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  if (previous !== body) await writeFile(output, body, 'utf8');
  return { pages: Object.keys(merged.pages).length, changed: previous !== body, sha256: createHash('sha256').update(body).digest('hex') };
}

/** 命令行允许显式来源根目录，默认使用当前仓库，输出固定在管理端运行时。 */
async function main() {
  const appRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
  let sourceRoot = resolve(appRoot, '../..');
  let output = resolve(appRoot, 'common/yzp_runtime/page-write-schema.generated.json');
  const args = process.argv.slice(2);
  for (let index = 0; index < args.length; index += 2) {
    if (!['--source-root', '--output'].includes(args[index]) || !args[index + 1]) throw new Error('仅支持 --source-root 来源仓库 与 --output 输出文件');
    if (args[index] === '--source-root') sourceRoot = resolve(args[index + 1]);
    else output = resolve(args[index + 1]);
  }
  console.log('页面写入清单合并完成：' + JSON.stringify(await writeMergedSchema({ sourceRoot, output })));
}
if (process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url) main().catch(error => { console.error(error.message); process.exitCode = 1; });
