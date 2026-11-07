# Metarill 默认头像来源

2026-10-20，用户明确要求“你重新生成一个替换吧”，替换原工程带“图片天下”水印的默认头像。原库存头像未复制、未去水印。

新头像使用内置 imagegen 工具全新生成，无参考图片；生成后只复制原PNG字节，没有裁剪、调色或改变原页面布局。资源位置为 `frontend/metarill_uni_admin/static/images/avatar.png`，原头像引用、容器尺寸和显示交互保持不变。

- PNG尺寸：1254 × 1254。
- SHA-256：e4829fa7be2628c6a103b246bfa2a4205b6fbdb7479cb890bf55f11f85eef96c。
- 项目用途：默认个人头像，随项目源码分发；不含第三方品牌、水印或文字。

## 完整生成提示词

```text
Use case: illustration-story. Asset type: a single default profile avatar PNG for Metarill, an open source business application, to replace an unlicensed stock avatar. Generate a completely new original illustration, with no reference image. Square canvas, centered friendly gender-neutral adult head and shoulders, short dark charcoal hair, simple warm beige face with very minimal subtle facial features, plain cream collared shirt with a dark teal overshirt, no tie. Flat clean editorial vector-like raster illustration, crisp broad shapes, easy to read at 48 pixels. Solid very pale aqua background, with a centered soft teal circular field behind the bust, harmonious with primary color #0ab3ad. Front-facing, shoulders occupy lower half, comfortable margin around the head; no shadow outside the circle. Full canvas opaque. No text, no watermark, no logo, no border, no stock-photo markings. This is only the avatar asset; do not generate an interface or screen mockup.
```
