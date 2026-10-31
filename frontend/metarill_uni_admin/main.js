import App from './App'
import yzp from '@/common/yzp'
import { installMenuRuntime } from '@/common/yzp_runtime/menuRuntimeBridge'
import {
	createH5DocumentTitleSync
} from '@/common/yzp_runtime/h5DocumentTitleSync'

const deviceInfo = {
	is_pc: true
}

const listEmptyObj = {
	id: '',
	name: '--'
}

// PC 端图片控件样式，移动端会在 onLaunch 时清空为组件默认值。
const imageStyles = {
	height: 100,
	width: 100
}

const checkboxRange = [{
	value: 0,
	text: '否'
}, {
	value: 1,
	text: '是'
}]

const multiCheckboxRange = [{
	value: 1,
	text: '全选'
}]

function installGlobalProperties(target) {
	target.$yzp = yzp
	target.$deviceInfo = deviceInfo
	target.$listEmptyObj = listEmptyObj
	target.$imageStyles = imageStyles
	target.$checkboxRange = checkboxRange
	target.$multiCheckboxRange = multiCheckboxRange
}

// #ifdef H5
const setupH5DocumentTitleSync = createH5DocumentTitleSync({
	fallbackProjectName: "Metarill"
})
// #endif

App.mpType = 'app'

// #ifndef VUE3
import Vue from 'vue'

Vue.config.productionTip = false
installGlobalProperties(Vue.prototype)
installMenuRuntime(mixin => Vue.mixin(mixin))

// #ifdef H5
setupH5DocumentTitleSync((mixin) => {
	Vue.mixin(mixin)
})
// #endif

const app = new Vue({
	...App
})
app.$mount()
// #endif

// #ifdef VUE3
import {
	createSSRApp
} from 'vue'

export function createApp() {
	const app = createSSRApp(App)
	installGlobalProperties(app.config.globalProperties)
	installMenuRuntime(mixin => app.mixin(mixin))
	// #ifdef H5
	setupH5DocumentTitleSync((mixin) => {
		app.mixin(mixin)
	})
	// #endif
	return {
		app
	}
}
// #endif
