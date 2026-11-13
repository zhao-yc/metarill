import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const runtimePath = new URL('../common/yzp_runtime/relationReadValues.js', import.meta.url)
const source = await readFile(runtimePath, 'utf8')
const relation = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))

test('多选 JSON 字符串与数组回显均保留 ID 顺序和缺失名称空位', () => {
	for (const ids of [['b', '失效', 'a'], '["b","失效","a"]']) {
		const before = JSON.stringify(ids)
		const rows = relation.selectedRelationRows(ids, '["乙",null,"甲"]', 'displayTitle')
		assert.deepEqual(rows.map(row => row.id), ['b', '失效', 'a'])
		assert.deepEqual(rows.map(row => row.displayTitle), ['乙', null, '甲'])
		assert.equal(relation.relationDisplayText('["乙",null,"甲"]'), '乙、、甲')
		assert.equal(JSON.stringify(ids), before)
	}
	assert.deepEqual(relation.selectedRelationRows('["失效"]', null), [{ id: '失效', name: null }])
	assert.deepEqual(relation.selectedRelationRows('["失效"]', null, 'id'), [{ id: '失效', name: null }])
	assert.deepEqual(relation.relationArray(null), [])
	assert.deepEqual(relation.relationArray('不是数组'), [])
})

test('列表刷新更新选中名称，并保留跨页已选与失效 ID', () => {
	const original = relation.selectedRelationRows(['b', '失效', 'a'], ['旧乙', null, '甲'], 'displayTitle')
	const current = [{ id: 'b', displayTitle: '新乙' }, { id: '新记录', displayTitle: '丙' }]
	const rows = relation.refreshSelectedRelationRows(original, current)
	assert.deepEqual(rows.map(row => row.id), ['b', '失效', 'a'])
	assert.equal(rows[0].displayTitle, '新乙')
	assert.equal(rows[1].displayTitle, null)
	assert.equal(original[0].displayTitle, '旧乙')
	assert.equal(current[0].isSelect, undefined)
})

test('非冗余提交剔除辅助名称、审计姓名和级联祖先，界面值及叶 ID 保持完整', () => {
	const entity = { id: '记录', authorIds: ['b', '失效', 'a'], authorNames: ['乙', null, '甲'],
		insertManagerRealName: '当前姓名', provinceId: '省', provinceName: '浙江', cityId: '市', cityName: '杭州', districtId: '区' }
	const before = JSON.stringify(entity)
	const payload = relation.relationWriteEntity(entity, ['authorNames', 'insertManagerRealName', 'provinceId', 'provinceName', 'cityId', 'cityName'])
	assert.deepEqual(payload, { id: '记录', authorIds: ['b', '失效', 'a'], districtId: '区' })
	assert.equal(JSON.stringify(entity), before)
})
