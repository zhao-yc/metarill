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

// 仅引用 JSON 值位置的数字词元，使规则和表单 JSON 内的数字都不先舍入。
const jsonNumberToken = /"(?:[^"\\]|\\[\s\S])*"|([:\[,]\s*)(-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)(?=\s*[,}\]])/g

function parseJsonWithExactNumbers(text) {
	return JSON.parse(text.replace(jsonNumberToken, (token, separator, number) =>
		separator === undefined ? token : separator + JSON.stringify(number)))
}

function normalizeRule(ruleLike) {
	if (utils.isBlank(ruleLike)) {
		return createEmptyRule()
	}
	let rule = ruleLike
	if (typeof ruleLike === 'string') {
		try {
			rule = parseJsonWithExactNumbers(ruleLike)
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
	const explicitValues = Array.isArray(valueList) ? valueList : singleValue
	if (Array.isArray(explicitValues)) {
		// 显式数组的空项仍是候选。逗号或换行产生的空片段才按原规则忽略。
		return explicitValues.map((item) => item == null ? '' : String(item))
	}
	if (isBlankValue(singleValue)) {
		return []
	}
	return String(singleValue)
		.split(/[\n,]/)
		.filter((item) => !isBlankValue(item))
		.map(trimRuleText)
}

function serializeRule(ruleLike) {
	const rule = normalizeRule(ruleLike)
	if (!rule.conditions.length && !(rule.groups && rule.groups.length)) {
		return ''
	}
	return JSON.stringify(rule)
}

const invalidBooleanOption = Symbol('invalidBooleanOption')

function evaluateRule(ruleLike, entity, jsonObjectFields = [], booleanOptionFields = {}) {
	const rule = normalizeRule(ruleLike)
	if (!rule.conditions.length && !(rule.groups && rule.groups.length)) {
		return true
	}
	return evaluateGroup(rule, entity || {}, jsonObjectFields, booleanOptionFields)
}

function evaluateGroup(group, entity, jsonObjectFields, booleanOptionFields) {
	const results = []
	;(group.conditions || []).forEach((condition) => {
		results.push(evaluateCondition(condition, entity, jsonObjectFields, booleanOptionFields))
	})
	;(group.groups || []).forEach((childGroup) => {
		results.push(evaluateGroup(childGroup, entity, jsonObjectFields, booleanOptionFields))
	})
	if (!results.length) {
		return true
	}
	if (group.logic === 'OR') {
		return results.some(Boolean)
	}
	return results.every(Boolean)
}

function evaluateCondition(condition, entity, jsonObjectFields, booleanOptionFields) {
	const operator = condition.operator || 'eq'
	const actualValue = readFieldValue(entity, condition.field, jsonObjectFields, booleanOptionFields)
	if (actualValue === invalidBooleanOption) return false
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

function readFieldValue(entity, field, jsonObjectFields, booleanOptionFields) {
	if (!entity || isBlankValue(field)) {
		return null
	}
	if (booleanOptionFields && Object.prototype.hasOwnProperty.call(booleanOptionFields, field)) {
		// 仅还原生成页面已证明的布尔选项，表单和请求继续使用原数字。
		const value = entity[booleanOptionFields[field]]
		if (value === true || value === 1 || value === '1') return true
		if (value === false || value === 0 || value === '0') return false
		if (value == null || value === '') return null
		if (utils.isBlank(entity.id) && (value === 'true' || value === 'false')) return value === 'true'
		return invalidBooleanOption
	}
	const segments = String(field).split('.')
	if (segments.length > 1 && Array.isArray(jsonObjectFields) && jsonObjectFields.includes(segments[0])) {
		return readDeclaredJsonMember(entity[segments[0]], segments.slice(1))
	}
	return segments.reduce((current, segment) => {
		if (current == null || typeof current !== 'object') {
			return null
		}
		return current[segment]
	}, entity)
}

// 仅对生成器声明的 JSON 对象解析文本，精确保留标量数字。复合叶值仍沿用原比较输入。
function readDeclaredJsonMember(value, path) {
	if (typeof value !== 'string') return readObjectMember(value, path)
	try {
		const member = readObjectMember(parseJsonWithExactNumbers(value), path)
		return member !== null && typeof member === 'object' ? readObjectMember(JSON.parse(value), path) : member
	} catch {
		return null
	}
}

function readObjectMember(value, path) {
	for (const segment of path) {
		if (['__proto__', 'prototype', 'constructor'].includes(segment) ||
			!value || typeof value !== 'object' || Array.isArray(value) ||
			!Object.prototype.hasOwnProperty.call(value, segment)) {
			return null
		}
		value = value[segment]
	}
	return value
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
	const left = trimRuleText(String(actualValue))
	const right = trimRuleText(String(expectedValue))
	const actual = readRuleDecimal(left)
	const expected = readRuleDecimal(right)
	if (actual && expected) return compareRuleDecimals(actual, expected)
	// 原 Java 按 UTF-16 码元逐位比较，不能由浏览器地区设置合并不同文本。
	return left < right ? -1 : left > right ? 1 : 0
}

function trimRuleText(text) {
	return text.replace(/^[\u0000-\u0020]+|[\u0000-\u0020]+$/g, '')
}

// 原判定按有效数字与数量级比较。指数只用来计算位数，不能展开成大量尾零。
function readRuleDecimal(text) {
	const match = /^([+-]?)([0-9]*)(?:\.([0-9]*))?(?:[eE]([+-]?[0-9]+))?$/.exec(text)
	if (!match) return null
	const [, sign, whole, fraction = '', exponentText = '0'] = match
	if (!whole.length && !fraction.length) return null
	const exponent = Number(exponentText)
	const scale = fraction.length - exponent
	if (!Number.isSafeInteger(exponent) || scale < -2147483648 || scale > 2147483647) return null
	const significant = (whole + fraction).replace(/^0+/, '')
	if (!significant) return { sign: 0, order: 0, digits: '' }
	return { sign: sign === '-' ? -1 : 1, order: significant.length - scale, digits: significant.replace(/0+$/, '') }
}

function compareRuleDecimals(left, right) {
	if (left.sign !== right.sign) return left.sign - right.sign
	if (!left.sign) return 0
	if (left.order !== right.order) return left.sign * (left.order - right.order)
	for (let index = 0; index < Math.max(left.digits.length, right.digits.length); index++) {
		const a = left.digits[index] || '0'
		const b = right.digits[index] || '0'
		if (a !== b) return left.sign * (a < b ? -1 : 1)
	}
	return 0
}

function isBlankValue(value) {
	if (value == null) {
		return true
	}
	if (typeof value === 'string') {
		// Java 的空白判断保留不换行空格，比对用的 trim 则另按 U+0020 边界执行。
		return /^[\u0009-\u000d\u001c-\u0020\u1680\u2000-\u2006\u2008-\u200a\u2028\u2029\u205f\u3000]*$/.test(value)
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
