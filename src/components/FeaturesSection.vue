<script setup>
import { featureBlocks } from '../data/content'
import { asset } from '../utils/asset'

defineProps({
  locale: { type: String, required: true },
})

const emit = defineEmits(['open-image'])
</script>

<template>
  <section id="features" class="section features">
    <div class="container">
      <div class="section-head reveal">
        <p class="eyebrow">{{ locale === 'zh' ? '功能深潜' : 'Deep dive' }}</p>
        <h2>{{ locale === 'zh' ? '用真实界面讲述能力' : 'Features, shown as they look' }}</h2>
        <p class="lead">
          {{
            locale === 'zh'
              ? '以下截图来自当前 Moonward 客户端，对照说明启动、抽卡、战绩与界面打磨。'
              : 'Screenshots from the current Moonward client — launcher, gacha, records and polish.'
          }}
        </p>
      </div>

      <article
        v-for="block in featureBlocks"
        :key="block.id"
        class="block reveal"
        :class="{ reverse: block.reverse }"
      >
        <div class="copy">
          <p class="eyebrow">{{ block.eyebrow }}</p>
          <h3 class="title">{{ locale === 'zh' ? block.title : block.titleEn }}</h3>
          <p class="desc">{{ locale === 'zh' ? block.desc : block.descEn }}</p>
          <ul>
            <li v-for="(p, idx) in locale === 'zh' ? block.points : block.pointsEn" :key="idx">
              {{ p }}
            </li>
          </ul>
        </div>

        <button
          class="shot glass"
          type="button"
          :aria-label="block.imageAlt"
          @click="emit('open-image', { src: block.image, caption: block.imageAlt })"
        >
          <img :src="asset(block.image)" :alt="block.imageAlt" loading="lazy" />
          <span class="zoom">{{ locale === 'zh' ? '点击放大' : 'Click to enlarge' }}</span>
        </button>
      </article>
    </div>
  </section>
</template>

<style scoped>
.block {
  display: grid;
  grid-template-columns: 1fr 1.15fr;
  gap: 2.5rem;
  align-items: center;
  margin-bottom: 4.5rem;
}

.block:last-child {
  margin-bottom: 0;
}

.block.reverse {
  grid-template-columns: 1.15fr 1fr;
}

.block.reverse .copy {
  order: 2;
}

.block.reverse .shot {
  order: 1;
}

.title {
  font-size: clamp(1.4rem, 2.4vw, 1.85rem);
  margin-bottom: 0.75rem;
}

.desc {
  color: var(--text-muted);
  margin: 0 0 1.1rem;
}

ul {
  margin: 0;
  padding: 0;
  list-style: none;
  display: grid;
  gap: 0.55rem;
}

li {
  position: relative;
  padding-left: 1.25rem;
  color: var(--text);
  font-size: 0.95rem;
}

li::before {
  content: '';
  position: absolute;
  left: 0;
  top: 0.55em;
  width: 0.45rem;
  height: 0.45rem;
  border-radius: 50%;
  background: linear-gradient(135deg, var(--accent), var(--accent-2));
  box-shadow: 0 0 10px rgba(122, 162, 255, 0.5);
}

.shot {
  position: relative;
  display: block;
  width: 100%;
  padding: 0;
  border-radius: var(--radius);
  overflow: hidden;
  text-align: left;
  cursor: zoom-in;
  transition:
    transform 0.3s ease,
    border-color 0.3s ease;
}

.shot:hover {
  transform: translateY(-4px);
  border-color: rgba(122, 162, 255, 0.35);
}

.shot img {
  width: 100%;
  aspect-ratio: 16 / 10;
  object-fit: cover;
  object-position: top center;
}

.zoom {
  position: absolute;
  right: 0.85rem;
  bottom: 0.85rem;
  padding: 0.35rem 0.7rem;
  border-radius: 999px;
  font-size: 0.75rem;
  font-family: var(--font-display);
  font-weight: 600;
  background: rgba(7, 11, 22, 0.72);
  border: 1px solid var(--border-strong);
  backdrop-filter: blur(10px);
  opacity: 0;
  transform: translateY(6px);
  transition:
    opacity 0.2s ease,
    transform 0.2s ease;
}

.shot:hover .zoom {
  opacity: 1;
  transform: none;
}

@media (max-width: 900px) {
  .block,
  .block.reverse {
    grid-template-columns: 1fr;
    gap: 1.25rem;
  }

  .block.reverse .copy,
  .block.reverse .shot {
    order: initial;
  }
}
</style>
