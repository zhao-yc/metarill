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
        "leftWindow": false,
        "enablePullDownRefresh": false
      }
    },
    {
      "path": "pages/main/index",
      "style": {
        "navigationBarTitleText": "首页",
        "navigationStyle": "custom"
      }
    },
    {
      "path": "pages/main/change_password",
      "style": {
        "navigationBarTitleText": "修改密码"
      }
    },
    {
      "path": "pages/main/left",
      "style": {
        "navigationBarTitleText": ""
      }
    },
    {
      "path": "pages/main/top",
      "style": {
        "navigationBarTitleText": ""
      }
    },
    {
      "path": "pages/main/menu",
      "style": {
        "navigationBarTitleText": "菜单",
        "navigationStyle": "custom",
        "enablePullDownRefresh": false
      }
    },
    {
      "path": "pages/main/my",
      "style": {
        "navigationBarTitleText": "我的",
        "navigationStyle": "custom",
        "enablePullDownRefresh": false
      }
    },
    {
      "path": "pages/main/rtf/rtf",
      "style": {
        "navigationBarTitleText": "编辑富文本",
        "enablePullDownRefresh": false
      }
    }
  ],
  "tabBar": {
    "color": "#aaa",
    "selectedColor": "#0ab3ad",
    "list": [
      {
        "pagePath": "pages/main/index",
        "text": "首页",
        "iconPath": "static/images/home.png",
        "selectedIconPath": "static/images/home-selected.png"
      },
      {
        "pagePath": "pages/main/menu",
        "text": "菜单",
        "iconPath": "static/images/menu.png",
        "selectedIconPath": "static/images/menu-selected.png"
      },
      {
        "pagePath": "pages/main/my",
        "text": "我的",
        "iconPath": "static/images/my.png",
        "selectedIconPath": "static/images/my-selected.png"
      }
    ],
    "borderStyle": "black",
    "backgroundColor": "#ffffff"
  },
  "globalStyle": {
    "navigationBarTextStyle": "black",
    "navigationBarTitleText": "Metarill 应用",
    "navigationBarBackgroundColor": "#FFF",
    "backgroundColor": "#F8F8F8",
    "app-plus": {
      "background": "#efeff4"
    }
  }
}
