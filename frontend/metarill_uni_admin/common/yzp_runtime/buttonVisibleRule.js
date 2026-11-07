import utils from '../utils'

const LOGIC_OPTIONS = [{
	value: 'AND',
	text: '且'
}, {
	value: 'OR',
	text: '或'
}]

const OPERATOR_OPTIONS = [{
	value: 'eq',
	text: '等于'
}, {
	value: 'ne',
	text: '不等于'
}, {
	value: 'in',
	text: '包含于'
}, {
	value: 'notIn',
	text: '不包含于'
}, {
	value: 'gt',
	text: '大于'
}, {
	value: 'gte',
	text: '大于等于'
}, {
	value: 'lt',
	text: '小于'
}, {
	value: 'lte',
	text: '小于等于'
}, {
	value: 'isBlank',
	text: '为空'
}, {
	value: 'isNotBlank',
	text: '不为空'
}]

function createEmptyCondition() {
	return {
		field: '',
		operator: 'eq',
		value: '',
		valueList: []
	}
}

function createEmptyRule() {
	return {
		logic: 'AND',
		conditions: []
	}
}

function normalizeRule(ruleLike) {
	if (utils.isBlank(ruleLike)) {
		return createEmptyRule()
	}
	let rule = ruleLike
	if (typeof ruleLike === 'string') {
		try {
			rule = JSON.parse(ruleLike)
		} catch (error) {
			return createEmptyRule()
		}
	}
	if (!rule || typeof rule !== 'object') {
		return createEmptyRule()
	}
	return {
		logic: normalizeLogic(rule.logic),
		conditions: normalizeConditions(rule.conditions),
		groups: normalizeGroups(rule.groups)
	}
}

function normalizeLogic(logic) {
	return String(logic || 'AND').toUpperCase() === 'OR' ? 'OR' : 'AND'
}

function normalizeConditions(conditions) {
	if (!Array.isArray(conditions)) {
		return []
	}
	return conditions.map((condition) => {
		const current = condition || {}
		const operator = current.operator || 'eq'
		return {
			field: current.field || '',
			operator,
			value: current.value == null ? '' : String(current.value),
			valueList: normalizeValueList(current.valueList, current.value)
		}
	})
}

function normalizeGroups(groups) {
	if (!Array.isArray(groups)) {
		return []
	}
	return groups.map((group) => normalizeRule(group))
}

function normalizeValueList(valueList, singleValue) {
	if (Array.isArray(valueList)) {
		return valueList.map((item) => String(item)).filter((item) => !utils.isBlank(item))
	}
	if (Array.isArray(singleValue)) {
		return singleValue.map((item) => String(item)).filter((item) => !utils.isBlank(item))
	}
	if (utils.isBlank(singleValue)) {
		return []
	}
	return String(singleValue)
		.split(/[\n,]/)
		.map((item) => item.trim())
		.filter((item) => !utils.isBlank(item))
}

function serializeRule(ruleLike) {
	const rule = normalizeRule(ruleLike)
	if (!rule.conditions.length && !(rule.groups && rule.groups.length)) {
		return ''
	}
	return JSON.stringify(rule)
}

function evaluateRule(ruleLike, entity) {
	const rule = normalizeRule(ruleLike)
	if (!rule.conditions.length && !(rule.groups && rule.groups.length)) {
		return true
	}
	return evaluateGroup(rule, entity || {})
}

function evaluateGroup(group, entity) {
	const results = []
	;(group.conditions || []).forEach((condition) => {
		results.push(evaluateCondition(condition, entity))
	})
	;(group.groups || []).forEach((childGroup) => {
		results.push(evaluateGroup(childGroup, entity))
	})
	if (!results.length) {
		return true
	}
	if (group.logic === 'OR') {
		return results.some(Boolean)
	}
	return results.every(Boolean)
}

function evaluateCondition(condition, entity) {
	const operator = condition.operator || 'eq'
	const actualValue = readFieldValue(entity, condition.field)
	switch (operator) {
		case 'eq':
			return compareValues(actualValue, condition.value) === 0
		case 'ne':
			return compareValues(actualValue, condition.value) !== 0
		case 'gt':
			return compareValues(actualValue, condition.value) > 0
		case 'gte':
			return compareValues(actualValue, condition.value) >= 0
		case 'lt':
			return compareValues(actualValue, condition.value) < 0
		case 'lte':
			return compareValues(actualValue, condition.value) <= 0
		case 'in':
			return condition.valueList.some((item) => compareValues(actualValue, item) === 0)
		case 'notIn':
			return !condition.valueList.some((item) => compareValues(actualValue, item) === 0)
		case 'isBlank':
			return isBlankValue(actualValue)
		case 'isNotBlank':
			return !isBlankValue(actualValue)
		default:
			return false
	}
}

function readFieldValue(entity, field) {
	if (!entity || utils.isBlank(field)) {
		return null
	}
	return String(field).split('.').reduce((current, segment) => {
		if (current == null || typeof current !== 'object') {
			return null
		}
		return current[segment]
	}, entity)
}

function compareValues(actualValue, expectedValue) {
	if (isBlankValue(actualValue) && isBlankValue(expectedValue)) {
		return 0
	}
	if (isBlankValue(actualValue)) {
		return -1
	}
	if (isBlankValue(expectedValue)) {
		return 1
	}
	if (isNumeric(actualValue) && isNumeric(expectedValue)) {
		return Number(actualValue) - Number(expectedValue)
	}
	return String(actualValue).trim().localeCompare(String(expectedValue).trim())
}

function isNumeric(value) {
	if (utils.isBlank(value)) {
		return false
	}
	return !Number.isNaN(Number(String(value).trim()))
}

function isBlankValue(value) {
	if (value == null) {
		return true
	}
	if (typeof value === 'string') {
		return value.trim() === ''
	}
	if (Array.isArray(value)) {
		return value.length === 0
	}
	return false
}

export {
	LOGIC_OPTIONS,
	OPERATOR_OPTIONS,
	createEmptyCondition,
	createEmptyRule,
	normalizeRule,
	serializeRule,
	evaluateRule
}
