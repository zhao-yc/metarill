import test from 'node:test';import assert from 'node:assert/strict';import fs from 'node:fs';import { createHash } from 'node:crypto';
import { normalizeDeviceProjectPath,deviceProjectPathFingerprint } from '../lib/device-project-path.mjs';
const vectors=JSON.parse(fs.readFileSync(new URL('./device-path-vectors.json',import.meta.url),'utf8'));
for(const [index,row] of vectors.entries())test('原设备目录共享向量 '+index,()=>{
 if(row.normalized===null){assert.throws(()=>deviceProjectPathFingerprint(row.path));return}
 assert.equal(normalizeDeviceProjectPath(row.path),row.normalized);
 assert.equal(deviceProjectPathFingerprint(row.path),createHash('sha256').update('metarill-device-path-v1'+String.fromCharCode(10)+row.normalized).digest('hex'));
});
test('目录大小写与Unicode不能误合并为另一POSIX目录',()=>{
 assert.equal(deviceProjectPathFingerprint('C:/Work/Repo'),deviceProjectPathFingerprint('c:/work/repo/'));
 for(const [a,b] of [['/Work/Repo','/work/Repo'],['/root/Ä','/root/ä'],['/root/é','/root/e'+String.fromCharCode(0x301)]])assert.notEqual(deviceProjectPathFingerprint(a),deviceProjectPathFingerprint(b));
});
