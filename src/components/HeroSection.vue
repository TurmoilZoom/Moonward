<script setup>
import { links, games } from '../data/content'
import { asset } from '../utils/asset'

defineProps({
  locale: { type: String, required: true },
})
</script>

<template>
  <section id="top" class="hero section">
    <div class="container hero-grid">
      <div class="hero-copy reveal">
        <p class="eyebrow">{{ locale === 'zh' ? '开源 · WinUI 3 · 米哈游启动器' : 'Open Source · WinUI 3 · miHoYo Launcher' }}</p>
        <h1>
          <span class="grad">Moonward</span>
          <br />
          {{ locale === 'zh' ? '愿此行，终抵群星' : 'May This Journey Lead Us Moonward' }}
        </h1>
        <p class="lead">
          <template v-if="locale === 'zh'">
            基于 Starward 打造的第三方米哈游 PC 启动器。启动游戏、管理账号、抽卡统计与每日签到，用更现代的桌面体验覆盖原神、星穹铁道、绝区零与崩坏3。
          </template>
          <template v-else>
            A third-party miHoYo PC launcher based on Starward. Launch games, manage accounts, track gacha and check in daily — for Genshin, Star Rail, ZZZ and Honkai Impact 3rd.
          </template>
        </p>

        <div class="hero-actions">
          <a class="btn btn-primary" :href="links.download" target="_blank" rel="noopener noreferrer">
            <span aria-hidden="true">↓</span>
            {{ locale === 'zh' ? '下载最新版' : 'Download latest' }}
          </a>
          <a class="btn btn-ghost" :href="links.github" target="_blank" rel="noopener noreferrer">
            {{ locale === 'zh' ? '查看源码' : 'View source' }}
          </a>
        </div>

        <ul class="game-pills" aria-label="Supported games">
          <li v-for="g in games" :key="g.id" :style="{ '--tone': g.tone }">
            <span class="dot" />
            {{ locale === 'zh' ? g.name : g.nameEn }}
          </li>
        </ul>
      </div>

      <div class="hero-visual reveal reveal-delay-2">
        <div class="frame glow">
          <div class="frame-chrome">
            <span /><span /><span />
            <em>Moonward.exe</em>
          </div>
          <img
            :src="asset('screenshots/launcher-detail.png')"
            alt="Moonward launcher screenshot"
            loading="eager"
          />
        </div>
        <div class="float-card glass card-a">
          <strong>{{ locale === 'zh' ? '每日签到' : 'Daily check-in' }}</strong>
          <span>{{ locale === 'zh' ? '自动 · 启动顺带签' : 'Auto · on launch' }}</span>
        </div>
        <div class="float-card glass card-b">
          <strong>{{ locale === 'zh' ? '抽卡统计' : 'Gacha stats' }}</strong>
          <span>{{ locale === 'zh' ? '垫数 · 保底 · 分享图' : 'Pity · guarantee · share' }}</span>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.hero {
  padding-top: 2.5rem;
  padding-bottom: 4rem;
  overflow: hidden;
}

.hero-grid {
  display: grid;
  grid-template-columns: 1.05fr 1fr;
  gap: 3rem;
  align-items: center;
}

.grad {
  background: linear-gradient(120deg, #eef2ff 10%, #7aa2ff 45%, #b388ff 75%, #5eead4 100%);
  -webkit-background-clip: text;
  background-clip: text;
  color: transparent;
}

.hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-top: 1.75rem;
}

.game-pills {
  list-style: none;
  display: flex;
  flex-wrap: wrap;
  gap: 0.55rem;
  padding: 0;
  margin: 2rem 0 0;
}

.game-pills li {
  display: inline-flex;
  align-items: center;
  gap: 0.45rem;
  padding: 0.4rem 0.75rem;
  border-radius: 999px;
  border: 1px solid color-mix(in srgb, var(--tone) 35%, transparent);
  background: color-mix(in srgb, var(--tone) 10%, transparent);
  color: var(--text-muted);
  font-size: 0.82rem;
  font-weight: 500;
}

.dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--tone);
  box-shadow: 0 0 10px var(--tone);
}

.hero-visual {
  position: relative;
  min-height: 420px;
}

.frame {
  position: relative;
  border-radius: 18px;
  overflow: hidden;
  border: 1px solid var(--border-strong);
  background: #0b1020;
  box-shadow: var(--shadow);
  transform: perspective(1200px) rotateY(-6deg) rotateX(4deg);
  transition: transform 0.45s ease;
}

.frame:hover {
  transform: perspective(1200px) rotateY(-2deg) rotateX(1deg) translateY(-4px);
}

.frame.glow::before {
  content: '';
  position: absolute;
  inset: -30% -10% auto;
  height: 60%;
  background: radial-gradient(circle, rgba(122, 162, 255, 0.35), transparent 65%);
  pointer-events: none;
  z-index: 0;
}

.frame-chrome {
  position: relative;
  z-index: 1;
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.7rem 0.9rem;
  background: rgba(255, 255, 255, 0.03);
  border-bottom: 1px solid var(--border);
}

.frame-chrome span {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: #ff5f57;
}

.frame-chrome span:nth-child(2) {
  background: #febc2e;
}

.frame-chrome span:nth-child(3) {
  background: #28c840;
}

.frame-chrome em {
  margin-left: 0.5rem;
  font-style: normal;
  font-size: 0.75rem;
  color: var(--text-dim);
  font-family: var(--font-display);
}

.frame img {
  position: relative;
  z-index: 1;
  width: 100%;
  aspect-ratio: 16 / 10;
  object-fit: cover;
  object-position: top center;
}

.float-card {
  position: absolute;
  padding: 0.85rem 1rem;
  border-radius: 14px;
  display: grid;
  gap: 0.15rem;
  min-width: 150px;
  animation: float 5.5s ease-in-out infinite;
}

.float-card strong {
  font-family: var(--font-display);
  font-size: 0.92rem;
}

.float-card span {
  color: var(--text-muted);
  font-size: 0.78rem;
}

.card-a {
  left: -0.5rem;
  bottom: 18%;
  animation-delay: 0s;
}

.card-b {
  right: -0.25rem;
  top: 16%;
  animation-delay: 1.2s;
}

@keyframes float {
  0%,
  100% {
    transform: translateY(0);
  }
  50% {
    transform: translateY(-8px);
  }
}

@media (max-width: 960px) {
  .hero-grid {
    grid-template-columns: 1fr;
    gap: 2.5rem;
  }

  .hero-visual {
    min-height: auto;
    max-width: 640px;
    margin-inline: auto;
  }

  .frame {
    transform: none;
  }

  .frame:hover {
    transform: translateY(-4px);
  }

  .card-a {
    left: 0.5rem;
    bottom: 8%;
  }

  .card-b {
    right: 0.5rem;
    top: 12%;
  }
}

@media (max-width: 560px) {
  .float-card {
    display: none;
  }
}

@media (prefers-reduced-motion: reduce) {
  .float-card {
    animation: none;
  }
}
</style>
