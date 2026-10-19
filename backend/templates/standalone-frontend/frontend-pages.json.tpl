{
  "easycom": {
    "autoscan": true,
    "custom": {
      "^yzp-(.*)": "@/components/yzp-$1/yzp-$1.vue",
      "^zxz-(.*)": "@/components/zxz-$1/zxz-$1.vue"
    }
  },
  "leftWindow": {
    "path": "pages/main/left.vue",
    "style": {
      "width": "240px"
    }
  },
  "topWindow": {
    "path": "pages/main/top.vue",
    "style": {
      "height": "60px"
    }
  },
  "pages": [
    {
      "path": "pages/login",
      "style": {
        "navigationStyle": "custom",
        "navigationBarTitleText": "登录",
        "topWindow": false,
        "leftWindow": false
      }
    },
    {
      "path": "pages/main/index",
      "style": {
        "navigationBarTitleText": "首页"
      }
    },
    {
      "path": "pages/main/menu",
      "style": {
        "navigationBarTitleText": "菜单"
      }
    },
    {
      "path": "pages/main/my",
      "style": {
        "navigationBarTitleText": "我的"
      }
    },
    {
      "path": "pages/main/rtf/rtf",
      "style": {
        "navigationBarTitleText": "富文本"
      }
    }
  ],
  "tabBar": {
    "color": "#707070",
    "selectedColor": "#0ab3ad",
    "list": [
      {
        "pagePath": "pages/main/index",
        "text": "首页"
      },
      {
        "pagePath": "pages/main/menu",
        "text": "菜单"
      },
      {
        "pagePath": "pages/main/my",
        "text": "我的"
      }
    ]
  },
  "globalStyle": {
    "navigationBarTextStyle": "black",
    "navigationBarTitleText": "Metarill 应用",
    "navigationBarBackgroundColor": "#ffffff",
    "backgroundColor": "#ffffff"
  }
}
