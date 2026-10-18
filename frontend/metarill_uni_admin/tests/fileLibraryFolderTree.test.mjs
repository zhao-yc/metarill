import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const modulePath = new URL('../common/yzp_runtime/fileLibraryFolderTree.js', import.meta.url)

/**
 * 通过 data URL 加载 uni-app ES Module，避免修改当前前端 package.json 模块类型。
 */
async function loadFolderTreeModule() {
	const source = await readFile(modulePath, 'utf8')
	return import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'))
}

const folders = [{
	id: 'a',
	parentId: '',
	name: '合同',
	sortNo: 0
}, {
	id: 'b',
	parentId: 'a',
	name: '扫描件',
	sortNo: 0
}, {
	id: 'c',
	parentId: 'b',
	name: '六月',
	sortNo: 0
}, {
	id: 'd',
	parentId: '',
	name: '图片',
	sortNo: 1
}]

test('根目录直属子目录从第一层开始显示', async () => {
	const { buildVisibleFolderNodes } = await loadFolderTreeModule()

	assert.deepEqual(buildVisibleFolderNodes(folders, ['a', 'b']).map((item) => [item.id, item.level]), [
		['a', 1],
		['b', 2],
		['c', 3],
		['d', 1]
	])
})

test('收起父目录后隐藏后代节点', async () => {
	const { buildVisibleFolderNodes } = await loadFolderTreeModule()

	assert.deepEqual(buildVisibleFolderNodes(folders, ['a']).map((item) => item.id), ['a', 'b', 'd'])
})

test('计算当前目录路径和祖先目录', async () => {
	const { buildFolderPath, findAncestorFolderIds } = await loadFolderTreeModule()

	assert.deepEqual(buildFolderPath(folders, 'c').map((item) => item.name), ['根目录', '合同', '扫描件', '六月'])
	assert.deepEqual(findAncestorFolderIds(folders, 'c'), ['a', 'b'])
})

test('保留文件夹直属文件数量', async () => {
	const { normalizeFolders } = await loadFolderTreeModule()

	assert.deepEqual(normalizeFolders([{
		id: 'a',
		name: '合同',
		fileCount: '3'
	}]).map((item) => item.fileCount), [3])
})

test('父目录和根目录文件数量包含子目录数量', async () => {
	const { aggregateFolderFileCounts } = await loadFolderTreeModule()

	const result = aggregateFolderFileCounts([{
		id: 'a',
		parentId: '',
		name: '合同',
		fileCount: 2
	}, {
		id: 'b',
		parentId: 'a',
		name: '扫描件',
		fileCount: 3
	}, {
		id: 'c',
		parentId: 'b',
		name: '六月',
		fileCount: 5
	}, {
		id: 'd',
		parentId: '',
		name: '图片',
		fileCount: 7
	}], 1)

	assert.equal(result.rootFileCount, 18)
	assert.deepEqual(result.folders.map((item) => [item.id, item.fileCount]), [
		['a', 10],
		['b', 8],
		['c', 5],
		['d', 7]
	])
})

test('当前目录只展示直属子文件夹', async () => {
	const { getDirectChildFolders } = await loadFolderTreeModule()

	assert.deepEqual(getDirectChildFolders(folders, 'a').map((item) => item.id), ['b'])
	assert.deepEqual(getDirectChildFolders(folders, '').map((item) => item.id), ['a', 'd'])
})

test('浏览区条目中文件夹排在文件前面', async () => {
	const { buildFileLibraryBrowserItems } = await loadFolderTreeModule()
	const files = [{
		id: 'file-1',
		originalName: '合同.pdf'
	}]

	assert.deepEqual(buildFileLibraryBrowserItems(folders, files, '', '').map((item) => [item.type, item.id]), [
		['folder', 'a'],
		['folder', 'd'],
		['file', 'file-1']
	])
})

test('搜索文件夹支持有序字符模糊匹配并携带路径', async () => {
	const { searchFoldersByKeyword } = await loadFolderTreeModule()

	assert.deepEqual(searchFoldersByKeyword(folders, '扫件').map((item) => [item.id, item.pathText]), [
		['b', '根目录 / 合同 / 扫描件']
	])
})

test('循环父子关系不会无限递归', async () => {
	const { buildFolderPath, buildVisibleFolderNodes, findAncestorFolderIds } = await loadFolderTreeModule()
	const cyclicFolders = [{
		id: 'a',
		parentId: 'b',
		name: 'A'
	}, {
		id: 'b',
		parentId: 'a',
		name: 'B'
	}]

	assert.deepEqual(buildVisibleFolderNodes(cyclicFolders, ['a', 'b']), [])
	assert.deepEqual(buildFolderPath(cyclicFolders, 'a'), [{
		id: '',
		parentId: '',
		name: '根目录',
		sortNo: 0
	}])
	assert.deepEqual(findAncestorFolderIds(cyclicFolders, 'a'), [])
})
