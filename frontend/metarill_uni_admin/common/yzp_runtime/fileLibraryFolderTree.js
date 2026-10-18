const ROOT_FOLDER = {
	id: '',
	parentId: '',
	name: '根目录',
	sortNo: 0
}

/**
 * 统一文件夹节点结构，并过滤空 ID 和重复 ID。
 */
export function normalizeFolders(folders) {
	const seenIds = new Set()
	return (Array.isArray(folders) ? folders : []).map((folder) => {
		const current = folder || {}
		return {
			id: String(current.id || '').trim(),
			parentId: String(current.parentId || '').trim(),
			name: String(current.name || current.id || '').trim(),
			sortNo: Number(current.sortNo || 0),
			fileCount: normalizeCount(current.fileCount),
			directFileCount: normalizeCount(
				Object.prototype.hasOwnProperty.call(current, 'directFileCount') ? current.directFileCount : current.fileCount
			)
		}
	}).filter((folder) => {
		if (!folder.id || seenIds.has(folder.id)) {
			return false
		}
		seenIds.add(folder.id)
		return true
	})
}

/**
 * 按父目录分组并排序，供目录树和节点状态复用。
 */
export function buildFolderChildrenMap(folders) {
	const childrenMap = {}
	normalizeFolders(folders).forEach((folder) => {
		if (!childrenMap[folder.parentId]) {
			childrenMap[folder.parentId] = []
		}
		childrenMap[folder.parentId].push(folder)
	})
	Object.keys(childrenMap).forEach((parentId) => {
		childrenMap[parentId].sort(compareFolders)
	})
	return childrenMap
}

/**
 * 只输出根目录下当前展开分支中的可见节点。
 */
export function buildVisibleFolderNodes(folders, expandedFolderIds) {
	const childrenMap = buildFolderChildrenMap(folders)
	const expandedIds = new Set((Array.isArray(expandedFolderIds) ? expandedFolderIds : []).map(String))
	const result = []

	function walk(parentId, level, ancestorIds) {
		;(childrenMap[parentId] || []).forEach((folder) => {
			if (ancestorIds.has(folder.id)) {
				return
			}
			const childFolders = childrenMap[folder.id] || []
			result.push({
				...folder,
				level,
				hasChildren: childFolders.length > 0,
				expanded: expandedIds.has(folder.id)
			})
			if (childFolders.length && expandedIds.has(folder.id)) {
				const nextAncestorIds = new Set(ancestorIds)
				nextAncestorIds.add(folder.id)
				walk(folder.id, level + 1, nextAncestorIds)
			}
		})
	}

	// 根目录是虚拟第 0 层，直属目录从第 1 层开始，避免视觉上与根目录并列。
	walk('', 1, new Set())
	return result
}

/**
 * 把每个目录的直属文件数汇总成包含后代目录的累计文件数。
 */
export function aggregateFolderFileCounts(folders, rootDirectFileCount = 0) {
	const normalizedFolders = normalizeFolders(folders)
	const childrenMap = buildFolderChildrenMap(normalizedFolders)
	const folderMap = normalizedFolders.reduce((result, folder) => {
		result[folder.id] = folder
		return result
	}, {})
	const totalCache = {}

	function countFolder(folderId, ancestorIds = new Set()) {
		if (Object.prototype.hasOwnProperty.call(totalCache, folderId)) {
			return totalCache[folderId]
		}
		if (ancestorIds.has(folderId)) {
			return 0
		}
		const folder = folderMap[folderId]
		if (!folder) {
			return 0
		}
		const nextAncestorIds = new Set(ancestorIds)
		nextAncestorIds.add(folderId)
		const total = normalizeCount(folder.directFileCount) + (childrenMap[folderId] || []).reduce((sum, child) => {
			return sum + countFolder(child.id, nextAncestorIds)
		}, 0)
		totalCache[folderId] = total
		return total
	}

	const aggregatedFolders = normalizedFolders.map((folder) => ({
		...folder,
		fileCount: countFolder(folder.id)
	}))
	const rootFileCount = normalizeCount(rootDirectFileCount) + (childrenMap[''] || []).reduce((sum, folder) => {
		return sum + countFolder(folder.id)
	}, 0)
	return {
		folders: aggregatedFolders,
		rootFileCount
	}
}

/**
 * 从当前目录向上回溯生成完整路径；孤儿或循环目录回退到根目录。
 */
export function buildFolderPath(folders, currentFolderId) {
	const folderId = String(currentFolderId || '').trim()
	if (!folderId) {
		return [{ ...ROOT_FOLDER }]
	}
	const folderMap = buildFolderMap(folders)
	const reversedPath = []
	const visitedIds = new Set()
	let nextId = folderId
	while (nextId) {
		if (visitedIds.has(nextId)) {
			return [{ ...ROOT_FOLDER }]
		}
		visitedIds.add(nextId)
		const folder = folderMap[nextId]
		if (!folder) {
			return [{ ...ROOT_FOLDER }]
		}
		reversedPath.push(folder)
		nextId = folder.parentId
	}
	return [{ ...ROOT_FOLDER }].concat(reversedPath.reverse())
}

/**
 * 返回当前目录的祖先 ID，顺序从一级目录到直接父目录。
 */
export function findAncestorFolderIds(folders, currentFolderId) {
	const path = buildFolderPath(folders, currentFolderId)
	return path.slice(1, -1).map((folder) => folder.id)
}

/**
 * 返回当前目录下一级文件夹，文件列表主区域用它把文件夹当作普通条目展示。
 */
export function getDirectChildFolders(folders, currentFolderId) {
	const childrenMap = buildFolderChildrenMap(folders)
	return childrenMap[String(currentFolderId || '').trim()] || []
}

/**
 * 文件夹搜索和后端文件名搜索保持同口径：每段关键词按字符顺序模糊匹配。
 */
export function searchFoldersByKeyword(folders, keyword) {
	const normalizedFolders = normalizeFolders(folders)
	if (!String(keyword || '').trim()) {
		return []
	}
	return normalizedFolders.filter((folder) => fuzzyMatchText(folder.name, keyword)).map((folder) => ({
		...folder,
		path: buildFolderPath(normalizedFolders, folder.id),
		pathText: buildFolderPath(normalizedFolders, folder.id).map((pathFolder) => pathFolder.name).join(' / ')
	}))
}

/**
 * 构造文件浏览区条目，文件夹永远排在文件前面。
 */
export function buildFileLibraryBrowserItems(folders, files, currentFolderId, keyword) {
	const searching = String(keyword || '').trim().length > 0
	const folderItems = (searching ? searchFoldersByKeyword(folders, keyword) : getDirectChildFolders(folders, currentFolderId))
		.map((folder) => ({
			type: 'folder',
			id: folder.id,
			name: folder.name,
			fileCount: normalizeCount(folder.fileCount),
			path: folder.path || buildFolderPath(folders, folder.id),
			pathText: folder.pathText || buildFolderPath(folders, folder.id).map((pathFolder) => pathFolder.name).join(' / '),
			folder
		}))
	const fileItems = (Array.isArray(files) ? files : []).map((file) => {
		const current = file || {}
		const fileId = String(current.fileId || current.id || '').trim()
		return {
			type: 'file',
			id: fileId,
			name: current.originalName || current.name || fileId,
			path: buildFolderPath(folders, current.folderId),
			pathText: buildFolderPath(folders, current.folderId).map((pathFolder) => pathFolder.name).join(' / '),
			file: current
		}
	})
	return folderItems.concat(fileItems)
}

function buildFolderMap(folders) {
	return normalizeFolders(folders).reduce((result, folder) => {
		result[folder.id] = folder
		return result
	}, {})
}

function normalizeCount(value) {
	const count = Number(value || 0)
	return Number.isFinite(count) && count > 0 ? count : 0
}

function compareFolders(left, right) {
	if (left.sortNo !== right.sortNo) {
		return left.sortNo - right.sortNo
	}
	return left.name.localeCompare(right.name, 'zh-Hans-CN')
}

function fuzzyMatchText(text, keyword) {
	const target = String(text || '').toLowerCase()
	const parts = String(keyword || '').trim().toLowerCase().split(/\s+/).filter(Boolean)
	return parts.length > 0 && parts.every((part) => fuzzyMatchPart(target, part))
}

function fuzzyMatchPart(target, part) {
	let startIndex = 0
	for (const character of Array.from(part)) {
		const matchIndex = target.indexOf(character, startIndex)
		if (matchIndex < 0) {
			return false
		}
		startIndex = matchIndex + character.length
	}
	return true
}

export default {
	normalizeFolders,
	aggregateFolderFileCounts,
	buildFolderChildrenMap,
	buildVisibleFolderNodes,
	buildFolderPath,
	findAncestorFolderIds,
	getDirectChildFolders,
	searchFoldersByKeyword,
	buildFileLibraryBrowserItems
}
