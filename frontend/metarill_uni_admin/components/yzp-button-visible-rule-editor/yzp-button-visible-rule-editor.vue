<template>
	<view class="rule-editor">
		<view class="rule-editor-header">
			<view>
				<text class="rule-editor-title">动态显示规则</text>
				<text class="rule-editor-desc">只支持当前记录字段，未配置时仅按显示方案控制。</text>
			</view>
			<button size="mini" type="primary" @click="addCondition">新增条件</button>
		</view>

		<view class="rule-editor-logic">
			<text class="rule-editor-label">条件关系</text>
			<uni-data-checkbox v-model="rule.logic" :localdata="logicOptions" />
		</view>

		<view v-if="!rule.conditions.length" class="rule-editor-empty">
			<text>当前没有条件，按钮会直接按显示方案判断。</text>
		</view>

		<view v-for="(condition, index) in rule.conditions" :key="index" class="rule-condition-card">
			<view class="rule-condition-header">
				<text class="rule-condition-title">条件 {{ index + 1 }}</text>
				<button size="mini" type="warn" @click="removeCondition(index)">删除</button>
			</view>

			<view class="rule-condition-row">
				<text class="rule-editor-label">字段名</text>
				<uni-easyinput
					primaryColor="#007aff"
					v-model="condition.field"
					placeholder="例如：status、auditStatus、detail.result"
				/>
			</view>

			<view class="rule-condition-row">
				<text class="rule-editor-label">运算符</text>
				<uni-data-select v-model="condition.operator" :localdata="operatorOptions" />
			</view>

			<view v-if="needsValue(condition.operator)" class="rule-condition-row">
				<text class="rule-editor-label">目标值</text>
				<uni-easyinput
					primaryColor="#007aff"
					v-model="condition.value"
					:type="isListOperator(condition.operator) ? 'textarea' : 'text'"
					:maxlength="-1"
					:placeholder="getValuePlaceholder(condition.operator)"
				/>
			</view>

			<view class="rule-condition-tips">
				<text v-if="isListOperator(condition.operator)">`in/notIn` 一行一个值，也支持逗号分隔。</text>
				<text v-else-if="!needsValue(condition.operator)">该运算符不需要填写目标值。</text>
			</view>
		</view>

		<view v-if="serializedRule" class="rule-editor-preview">
			<text class="rule-editor-label">规则 JSON</text>
			<text selectable class="rule-editor-preview-text">{{ serializedRule }}</text>
		</view>
	</view>
</template>

<script>
import {
	LOGIC_OPTIONS,
	OPERATOR_OPTIONS,
	createEmptyCondition,
	createEmptyRule,
	normalizeRule,
	serializeRule
} from '@/common/yzp_runtime/buttonVisibleRule'

export default {
	props: {
		entity: {
			type: Object,
			default: () => ({})
		}
	},
	data() {
		return {
			logicOptions: LOGIC_OPTIONS,
			operatorOptions: OPERATOR_OPTIONS,
			rule: createEmptyRule(),
			isSyncing: false
		}
	},
	computed: {
		serializedRule() {
			return serializeRule(this.buildSerializableRule())
		}
	},
	watch: {
		'entity.visibleRuleJson': {
			immediate: true,
			handler() {
				this.syncFromEntity()
			}
		},
		rule: {
			deep: true,
			handler() {
				if (this.isSyncing) {
					return
				}
				this.emitEntity()
			}
		}
	},
	methods: {
		syncFromEntity() {
			const normalized = normalizeRule(this.entity && this.entity.visibleRuleJson)
			const nextRule = {
				logic: normalized.logic,
				conditions: (normalized.conditions || []).map((condition) => ({
					field: condition.field || '',
					operator: condition.operator || 'eq',
					value: condition.valueList && condition.valueList.length ? condition.valueList.join('\n') : (condition.value || '')
				})),
				groups: normalized.groups || []
			}
			const nextSerialized = serializeRule({
				logic: nextRule.logic,
				conditions: nextRule.conditions
			})
			const currentSerialized = serializeRule(this.buildSerializableRule())
			if (nextSerialized === currentSerialized) {
				return
			}
			this.isSyncing = true
			this.rule = nextRule
			this.$nextTick(() => {
				this.isSyncing = false
			})
		},
		buildSerializableRule() {
			return {
				logic: this.rule.logic,
				conditions: (this.rule.conditions || []).map((condition) => ({
					field: condition.field,
					operator: condition.operator,
					value: condition.value
				}))
			}
		},
		emitEntity() {
			const newEntity = JSON.parse(JSON.stringify(this.entity || {}))
			newEntity.visibleRuleJson = serializeRule(this.buildSerializableRule())
			this.$emit('receive', newEntity)
		},
		addCondition() {
			this.rule.conditions.push(createEmptyCondition())
		},
		removeCondition(index) {
			this.rule.conditions.splice(index, 1)
		},
		isListOperator(operator) {
			return operator === 'in' || operator === 'notIn'
		},
		needsValue(operator) {
			return operator !== 'isBlank' && operator !== 'isNotBlank'
		},
		getValuePlaceholder(operator) {
			if (this.isListOperator(operator)) {
				return '一行一个值，或使用逗号分隔'
			}
			return '请输入比较值'
		}
	}
}
</script>

<style scoped>
.rule-editor {
	padding: 20rpx;
	border: 1px solid #e5eef8;
	border-radius: 20rpx;
	background: linear-gradient(180deg, #f9fbff 0%, #f3f8ff 100%);
}

.rule-editor-header {
	display: flex;
	align-items: flex-start;
	justify-content: space-between;
	gap: 20rpx;
	margin-bottom: 20rpx;
}

.rule-editor-title {
	display: block;
	font-size: 30rpx;
	font-weight: 600;
	color: #1f2a44;
}

.rule-editor-desc {
	display: block;
	margin-top: 8rpx;
	font-size: 24rpx;
	line-height: 1.6;
	color: #6b7280;
}

.rule-editor-logic,
.rule-condition-row {
	margin-bottom: 18rpx;
}

.rule-editor-label {
	display: block;
	margin-bottom: 10rpx;
	font-size: 24rpx;
	color: #4b5563;
}

.rule-editor-empty {
	padding: 24rpx;
	border-radius: 16rpx;
	background: #ffffff;
	color: #6b7280;
	font-size: 24rpx;
}

.rule-condition-card {
	margin-bottom: 20rpx;
	padding: 20rpx;
	border-radius: 18rpx;
	background: #ffffff;
	box-shadow: 0 8rpx 20rpx rgba(15, 23, 42, 0.05);
}

.rule-condition-header {
	display: flex;
	align-items: center;
	justify-content: space-between;
	margin-bottom: 20rpx;
}

.rule-condition-title {
	font-size: 28rpx;
	font-weight: 600;
	color: #1f2937;
}

.rule-condition-tips {
	font-size: 22rpx;
	color: #6b7280;
	line-height: 1.6;
}

.rule-editor-preview {
	margin-top: 24rpx;
	padding: 20rpx;
	border-radius: 18rpx;
	background: #111827;
}

.rule-editor-preview-text {
	display: block;
	font-size: 22rpx;
	line-height: 1.8;
	color: #f9fafb;
	word-break: break-all;
}
</style>
