<script setup>
import { highlights } from '../data/content'

defineProps({
  locale: { type: String, required: true },
})
</script>

<template>
  <section class="section highlights">
    <div class="container">
      <div class="section-head center reveal">
        <p class="eyebrow">{{ locale === 'zh' ? '核心能力' : 'Highlights' }}</p>
        <h2>{{ locale === 'zh' ? '不只是启动游戏' : 'More than a play button' }}</h2>
        <p class="lead">
          {{
            locale === 'zh'
              ? '把启动、账号、统计与签到收进同一个亚克力窗口，少几个常驻托盘，多一点掌控感。'
              : 'Launch, accounts, stats and check-in in one acrylic window — fewer trays, more control.'
          }}
        </p>
      </div>

      <div class="grid">
        <article
          v-for="(item, i) in highlights"
          :key="item.title"
          class="card glass reveal"
          :class="`reveal-delay-${(i % 3) + 1}`"
        >
          <div class="icon" aria-hidden="true">{{ item.icon }}</div>
          <h3>{{ locale === 'zh' ? item.title : item.titleEn }}</h3>
          <p>{{ locale === 'zh' ? item.desc : item.descEn }}</p>
        </article>
      </div>
    </div>
  </section>
</template>

<style scoped>
.grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 1rem;
}

.card {
  padding: 1.4rem 1.35rem 1.5rem;
  border-radius: var(--radius);
  transition:
    transform 0.25s ease,
    border-color 0.25s ease,
    background 0.25s ease;
}

.card:hover {
  transform: translateY(-4px);
  border-color: rgba(122, 162, 255, 0.28);
  background: rgba(255, 255, 255, 0.055);
}

.icon {
  width: 2.5rem;
  height: 2.5rem;
  display: grid;
  place-items: center;
  border-radius: 12px;
  background: linear-gradient(145deg, rgba(122, 162, 255, 0.18), rgba(179, 136, 255, 0.12));
  border: 1px solid var(--border);
  margin-bottom: 1rem;
  font-size: 1.15rem;
}

h3 {
  margin-bottom: 0.45rem;
}

p {
  margin: 0;
  color: var(--text-muted);
  font-size: 0.92rem;
}

@media (max-width: 900px) {
  .grid {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (max-width: 560px) {
  .grid {
    grid-template-columns: 1fr;
  }
}
</style>
