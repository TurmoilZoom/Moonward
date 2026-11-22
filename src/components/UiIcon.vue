<script setup>
import { computed } from 'vue'

/**
 * 站内线性图标：24×24、描边 1.75、圆头，和顶栏、截图区现有的手绘 SVG 同一套笔触。
 * 每个图标是若干条 path；`content.js` 里卡片的 `icon` 字段写这里的键名。
 * 取代原来的 Unicode 符号——那些符号靠系统字体回退，粗细、基线各不相同。
 */
const ICONS = {
  /** 多启动配置：三条滑杆 */
  sliders: [
    'M4 6h8M16 6h4M12 6a2 2 0 1 0 4 0a2 2 0 1 0-4 0',
    'M4 12h2M10 12h10M6 12a2 2 0 1 0 4 0a2 2 0 1 0-4 0',
    'M4 18h10M18 18h2M14 18a2 2 0 1 0 4 0a2 2 0 1 0-4 0',
  ],
  /** 桌面快捷方式：方框 + 斜向箭头 */
  shortcut: [
    'M6 4h12a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z',
    'M9.5 14.5l5-5M10.5 9.5h4v4',
  ],
  /** 免 UAC：盾牌 + 对勾 */
  shield: ['M12 3l7 3v5c0 4.5-3 8-7 10c-4-2-7-5.5-7-10V6z', 'M9 12l2 2 4-4'],
  /** URL 协议：横向链环 */
  link: ['M9 8H7a4 4 0 0 0 0 8h2', 'M15 8h2a4 4 0 0 1 0 8h-2', 'M8.5 12h7'],
  /** 云游戏 */
  cloud: ['M7 18h10a4 4 0 0 0 0-8A5.2 5.2 0 0 0 7 10.5a3.75 3.75 0 0 0 0 7.5z'],
  /** 签到：日历 + 对勾 */
  calendar: [
    'M6 5h12a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z',
    'M8 3v4M16 3v4M4 10h16',
    'M9 15l2 2 4-4',
  ],
  /** 抽卡：四角星 + 小十字闪光 */
  sparkle: ['M11 5Q12 12 19 13Q12 14 11 21Q10 14 3 13Q10 12 11 5z', 'M19 2.5v5M16.5 5h5'],
  /** 战绩 / 月报：柱状图 */
  chart: ['M4 20h16', 'M7 17v-5M12 17V6M17 17v-8'],
  /** 同步：上下两道反向箭头 */
  swap: ['M4 8h14M15 5l3 3-3 3', 'M20 16H6M9 13l-3 3 3 3'],
  /** 自定义背景：画框 + 远山 + 太阳 */
  image: [
    'M6 4h12a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z',
    'M4.5 17l4.5-5 4 4.5 2.5-2.5 4 4',
    'M14 9a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0',
  ],
  /** 静默更新：下载进托盘 */
  download: ['M12 4v10M8 10l4 4 4-4', 'M5 15v3a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-3'],
  /** 兑换码：票券 + 撕线 */
  ticket: [
    'M4 8a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v2a2 2 0 0 0 0 4v2a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-2a2 2 0 0 0 0-4z',
    'M14 7.5v1.5M14 11.25v1.5M14 15v1.5',
  ],
  /** 命令行参数：终端提示符 */
  terminal: [
    'M6 4h12a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z',
    'M8 9.5l2.5 2.5L8 14.5M13 14.5h3',
  ],
  /** 紧凑视图：四宫格 */
  grid: [
    'M5 4h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z',
    'M15 4h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z',
    'M5 14h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1z',
    'M15 14h4a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-4a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1z',
  ],
}

const props = defineProps({
  /** ICONS 的键名；未知键名不画任何东西 */
  name: { type: String, required: true },
})

const paths = computed(() => ICONS[props.name] || [])
</script>

<template>
  <svg
    class="ui-icon"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.75"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
    focusable="false"
  >
    <path v-for="(d, i) in paths" :key="i" :d="d" />
  </svg>
</template>

<style scoped>
.ui-icon {
  display: block;
  width: 1em;
  height: 1em;
  flex-shrink: 0;
}
</style>
