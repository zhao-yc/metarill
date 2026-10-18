var utils = {
	getFirstAndLastDayOfMonth: function(format) {
		// 获取当前日期
		let now = new Date();

		// 获取本月的第一天
		let firstDay = new Date(now.getFullYear(), now.getMonth(), 1);

		// 获取本月的最后一天
		let nextMonthFirstDay = new Date(now.getFullYear(), now.getMonth() + 1, 1);
		let lastDay = new Date(nextMonthFirstDay - 1);

		// 根据指定的格式进行格式化
		function formatDate(date, format) {
			let year = date.getFullYear();
			let month = (1 + date.getMonth()).toString().padStart(2, '0');
			let day = date.getDate().toString().padStart(2, '0');

			if (format === 'YYYY-MM-DD') {
				return `${year}-${month}-${day}`;
			}
			// 这里可以添加更多的格式选项
			return date.toLocaleDateString(); // 默认格式
		}

		return {
			firstDay: formatDate(firstDay, format),
			lastDay: formatDate(lastDay, format)
		};
	},
	isBlank: function(val) {
		if (val == null || val == undefined || val == '') {
			return true;
		} else {
			return false;
		}
	},
	openUrl: function(url) {
		// #ifdef H5
		window.open(url);
		// #endif
		// #ifndef H5
		plus.runtime.openURL(url);
		// #endif
	},
	//获取今天开始和结束的时间戳
	getTodayTimestampAry: function() {
		var result = [];
		const now = new Date();
		now.setHours(0, 0, 0, 0);
		const timestamp = now.getTime();
		result[0] = timestamp;
		result[1] = timestamp + 86399 * 1000;

		return result;
	},
	//获取今天开始和结束的时间字符串
	getTodayDateAry: function() {
		var result = [];
		const now = new Date();


		// 获取今天的年份、月份和日期部分
		const year = now.getFullYear();
		// 获取月份（注意：JavaScript中的月份是从0开始的，所以我们需要加1）
		const month = now.getMonth() + 1;
		const date = now.getDate();

		result[0] = `${year}-${month}-${date}`;

		// 设置时间为今天的最后一秒，即23:59:59
		result[1] = `${year}-${month}-${date} 23:59:59`;
		return result;
	}
};

export default utils;