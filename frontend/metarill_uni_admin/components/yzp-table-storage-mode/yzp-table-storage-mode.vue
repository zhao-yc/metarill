<template>
	<view class="storage-mode">
		<text v-if="!policy">{{ policyError || '正在读取项目存储配置…' }}</text>
		<checkbox-group v-else-if="!policy.isV8" @change="changeLegacyMode">
			<label><checkbox value="1" :checked="mode === 1" :disabled="readonly" />冗余</label>
		</checkbox-group>
		<text v-else-if="locked">{{ modeText }}</text>
		<uni-data-checkbox v-else :model-value="mode" :localdata="options" @update:modelValue="changeMode" />
		<text v-if="policy && policy.isV8" class="storage-mode-tip">{{ locked ? '表配置已保存，存储模式已固定。' : '保存表配置后，存储模式不能修改。' }}</text>
		<button v-if="policyError" size="mini" @click="loadPolicy">重试</button>
	</view>
</template>

<script>
import yzp from '@/common/yzp_runtime/yzp-runtime'
import { tableStorageMode, isTableStorageModeLocked, changeTableStorageMode } from '@/common/yzp_runtime/tableStorageMode'

export default {
	props: {
		entity: { type: Object, default: () => ({}) },
		recordId: { type: [String, Number], default: '' },
		readonly: { type: Boolean, default: false }
	},
	data() { return { policy: null, policyError: '' } },
	/** 版本及数据库能力由服务端确认，不能从当前页面是 V8 就推断业务项目也是 V8。 */
	created() { this.loadPolicy() },
	computed: {
		locked() { return isTableStorageModeLocked(this.entity, this.recordId, this.readonly, this.policy) },
		mode() { return tableStorageMode(this.entity.isRongyu) },
		modeText() { return this.mode === 1 ? '冗余存储' : this.mode === 0 ? '非冗余存储' : '存储模式无效' },
		/** V7 沿用旧复选框；V8 的 MySQL 普通表才提供新的非冗余选项。 */
		options() {
			const result = [{ value: 1, text: '冗余存储' }]
			if (this.policy && this.policy.supportsNonRedundant && Number(this.entity.isView) !== 1 && Number(this.entity.isFunctionTable) !== 1) {
				result.push({ value: 0, text: '非冗余存储' })
			}
			return result
		}
	},
	methods: {
		/** 读取失败保持只读并允许重试，避免把未知版本误当成 V8 或放宽编辑限制。 */
		loadPolicy() {
			this.policy = null
			this.policyError = ''
			yzp.request({ url: '/admin/custom/config_table/storage_mode_policy', method: 'POST', sendSessionId: true,
				success: res => {
					if (res.code === 200 && res.data && typeof res.data.isV8 === 'boolean') this.policy = res.data
					else this.policyError = res.msg || res.message || '读取项目存储配置失败'
				},
				fail: () => { this.policyError = '读取项目存储配置失败' }
			})
		},
		/** V7 保留旧复选框的 0/1 协议和已保存配置的可编辑行为。 */
		changeLegacyMode(event) { this.changeMode(event.detail.value.includes('1') ? 1 : 0) },
		/** 发出实体副本，元数据及后端仍负责默认值、版本边界和保存校验。 */
		changeMode(value) {
			const next = changeTableStorageMode(this.entity, this.recordId, this.readonly, value, this.policy)
			if (next) this.$emit('receive', next)
		}
	}
}
</script>

<style scoped>
.storage-mode { display: flex; flex-direction: column; gap: 12rpx; }
.storage-mode-tip { color: #737b88; font-size: 24rpx; line-height: 1.5; }
</style>
