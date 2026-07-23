<script setup>
import { gallery } from '../data/content'
import { asset } from '../utils/asset'

defineProps({
  locale: { type: String, required: true },
})

const emit = defineEmits(['open-image'])
</script>

<template>
  <section id="showcase" class="section showcase">
    <div class="container">
      <div class="section-head center reveal">
        <p class="eyebrow">{{ locale === 'zh' ? '界面速览' : 'Gallery' }}</p>
        <h2>{{ locale === 'zh' ? '截图展廊' : 'Screenshot gallery' }}</h2>
        <p class="lead">
          {{
            locale === 'zh'
              ? '点击任意图片可全屏预览。更多功能请下载后亲自探索。'
              : 'Click any image for a fullscreen preview. Download to explore the rest.'
          }}
        </p>
      </div>

      <div class="masonry">
        <button
          v-for="(item, i) in gallery"
          :key="item.src"
          class="tile glass reveal"
          :class="[`size-${(i % 5) + 1}`, `reveal-delay-${(i % 3) + 1}`]"
          type="button"
          @click="emit('open-image', { src: item.src, caption: locale === 'zh' ? item.caption : item.captionEn })"
        >
          <img :src="asset(item.src)" :alt="locale === 'zh' ? item.caption : item.captionEn" loading="lazy" />
          <span class="cap">{{ locale === 'zh' ? item.caption : item.captionEn }}</span>
        </button>
      </div>
    </div>
  </section>
</template>

<style scoped>
.masonry {
  display: grid;
  grid-template-columns: repeat(12, 1fr);
  gap: 0.9rem;
}

.tile {
  position: relative;
  padding: 0;
  border-radius: var(--radius);
  overflow: hidden;
  cursor: zoom-in;
  min-height: 180px;
  transition:
    transform 0.25s ease,
    border-color 0.25s ease;
}

.tile:hover {
  transform: translateY(-3px);
  border-color: rgba(122, 162, 255, 0.35);
}

.tile img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: top center;
  min-height: 180px;
}

.cap {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  padding: 1.4rem 0.9rem 0.75rem;
  background: linear-gradient(transparent, rgba(5, 7, 15, 0.88));
  font-family: var(--font-display);
  font-size: 0.88rem;
  font-weight: 600;
  text-align: left;
}

.size-1 {
  grid-column: span 7;
  min-height: 280px;
}

.size-2 {
  grid-column: span 5;
  min-height: 280px;
}

.size-3 {
  grid-column: span 4;
  min-height: 220px;
}

.size-4 {
  grid-column: span 4;
  min-height: 220px;
}

.size-5 {
  grid-column: span 4;
  min-height: 220px;
}

@media (max-width: 800px) {
  .masonry {
    grid-template-columns: 1fr 1fr;
  }

  .size-1,
  .size-2,
  .size-3,
  .size-4,
  .size-5 {
    grid-column: span 1;
    min-height: 180px;
  }
}

@media (max-width: 520px) {
  .masonry {
    grid-template-columns: 1fr;
  }
}
</style>
