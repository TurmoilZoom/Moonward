<script setup>
import { computed, ref, watch } from 'vue'
import {
  featureGroups,
  games,
  intro,
  links,
  requirements,
} from './data/content'
import { asset } from './utils/asset'

const locale = ref(localStorage.getItem('moonward-locale') || 'zh')
const t = computed(() => (keyObj) => keyObj[locale.value] ?? keyObj.zh)

watch(locale, (v) => {
  localStorage.setItem('moonward-locale', v)
  document.documentElement.lang = v === 'zh' ? 'zh-CN' : 'en'
})

function toggleLocale() {
  locale.value = locale.value === 'zh' ? 'en' : 'zh'
}
</script>

<template>
  <div class="page">
    <header class="top">
      <div class="wrap top-inner">
        <a class="brand" href="#">
          <img :src="asset('logo.png')" alt="" width="28" height="28" />
          <span>Moonward</span>
        </a>
        <nav class="nav">
          <a href="#features">{{ locale === 'zh' ? '功能' : 'Features' }}</a>
          <a href="#install">{{ locale === 'zh' ? '安装' : 'Install' }}</a>
          <a :href="links.github" target="_blank" rel="noopener noreferrer">GitHub</a>
          <button type="button" class="lang" @click="toggleLocale">
            {{ locale === 'zh' ? 'EN' : '中文' }}
          </button>
        </nav>
      </div>
    </header>

    <main class="wrap">
      <section class="intro">
        <p class="kicker mono">Windows · WinUI 3 · MIT</p>
        <h1>Moonward</h1>
        <p class="lede">{{ t(intro) }}</p>
        <p class="actions">
          <a class="btn" :href="links.download" target="_blank" rel="noopener noreferrer">
            {{ locale === 'zh' ? '下载' : 'Download' }}
          </a>
          <a class="btn ghost" :href="links.github" target="_blank" rel="noopener noreferrer">
            GitHub
          </a>
          <a class="text-link" :href="links.upstream" target="_blank" rel="noopener noreferrer">
            {{ locale === 'zh' ? '上游 Starward' : 'Upstream Starward' }}
          </a>
        </p>
      </section>

      <section class="block" aria-labelledby="games-heading">
        <h2 id="games-heading">{{ locale === 'zh' ? '支持的游戏' : 'Supported games' }}</h2>
        <ul class="games">
          <li v-for="name in games[locale]" :key="name">{{ name }}</li>
        </ul>
      </section>

      <section id="features" class="block" aria-labelledby="features-heading">
        <h2 id="features-heading">{{ locale === 'zh' ? '功能' : 'Features' }}</h2>
        <div v-for="group in featureGroups" :key="group.id" class="group">
          <h3>{{ t(group.title) }}</h3>
          <dl class="spec">
            <template v-for="item in group.items" :key="item.name.zh">
              <dt>{{ t(item.name) }}</dt>
              <dd>{{ t(item.detail) }}</dd>
            </template>
          </dl>
        </div>
      </section>

      <section id="install" class="block" aria-labelledby="install-heading">
        <h2 id="install-heading">{{ locale === 'zh' ? '安装' : 'Install' }}</h2>
        <p class="note">
          {{
            locale === 'zh'
              ? '从 GitHub Releases 下载对应 CPU 架构的安装包，按提示完成安装。'
              : 'Download the package for your CPU architecture from GitHub Releases and follow the installer.'
          }}
        </p>
        <dl class="spec tight">
          <template v-for="row in requirements" :key="row.label.zh">
            <dt>{{ t(row.label) }}</dt>
            <dd>{{ t(row.value) }}</dd>
          </template>
        </dl>
        <p class="actions end">
          <a class="btn" :href="links.download" target="_blank" rel="noopener noreferrer">
            Releases
          </a>
          <a class="text-link" :href="links.issues" target="_blank" rel="noopener noreferrer">
            Issues
          </a>
        </p>
      </section>
    </main>

    <footer class="foot">
      <div class="wrap">
        <p>
          {{
            locale === 'zh'
              ? 'Moonward / Starward 与 miHoYo / HoYoverse 无关联。游戏内容版权归原权利方。'
              : 'Moonward / Starward is not affiliated with miHoYo / HoYoverse. Game content belongs to their owners.'
          }}
        </p>
        <p class="meta mono">
          <a :href="links.license" target="_blank" rel="noopener noreferrer">MIT</a>
          ·
          <a :href="links.github" target="_blank" rel="noopener noreferrer">TurmoilZoom/Moonward</a>
        </p>
      </div>
    </footer>
  </div>
</template>

<style scoped>
.page {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

.top {
  border-bottom: 1px solid var(--line);
  background: color-mix(in srgb, var(--bg-raised) 88%, transparent);
  backdrop-filter: blur(8px);
  position: sticky;
  top: 0;
  z-index: 10;
}

.top-inner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  min-height: 3.25rem;
}

.brand {
  display: inline-flex;
  align-items: center;
  gap: 0.55rem;
  color: var(--ink);
  text-decoration: none;
  font-weight: 600;
  font-size: 0.95rem;
}

.brand img {
  width: 28px;
  height: 28px;
  border-radius: 6px;
  border: 1px solid var(--line);
}

.nav {
  display: flex;
  align-items: center;
  gap: 0.15rem 1rem;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.nav a {
  color: var(--ink-2);
  text-decoration: none;
  font-size: 0.9rem;
}

.nav a:hover {
  color: var(--ink);
  text-decoration: underline;
}

.lang {
  font-family: var(--mono);
  font-size: 0.75rem;
  padding: 0.2rem 0.45rem;
  border: 1px solid var(--line-strong);
  border-radius: 4px;
  color: var(--muted);
}

.lang:hover {
  color: var(--ink);
  border-color: var(--ink-2);
}

main {
  flex: 1;
  padding: 2.5rem 0 3.5rem;
}

.intro {
  padding-bottom: 2rem;
  border-bottom: 1px solid var(--line);
  margin-bottom: 2rem;
}

.kicker {
  font-size: 0.75rem;
  color: var(--muted);
  margin-bottom: 0.75rem;
}

.lede {
  margin-top: 1rem;
  max-width: 38rem;
  color: var(--ink-2);
}

.actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.65rem 1rem;
  margin-top: 1.35rem;
}

.actions.end {
  margin-top: 1.25rem;
}

.btn {
  display: inline-flex;
  align-items: center;
  padding: 0.45rem 0.9rem;
  border-radius: 4px;
  background: var(--accent);
  color: #f7faf8;
  text-decoration: none;
  font-size: 0.9rem;
  font-weight: 500;
}

.btn:hover {
  color: #fff;
  filter: brightness(1.05);
}

.btn.ghost {
  background: transparent;
  color: var(--ink);
  border: 1px solid var(--line-strong);
}

.btn.ghost:hover {
  border-color: var(--ink-2);
  filter: none;
}

.text-link {
  font-size: 0.9rem;
}

.block {
  margin-bottom: 2.25rem;
}

.games {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem 0.5rem;
}

.games li {
  font-size: 0.9rem;
  padding: 0.25rem 0.6rem;
  border: 1px solid var(--line);
  border-radius: 999px;
  background: var(--bg-raised);
  color: var(--ink);
}

.group {
  margin-bottom: 1.75rem;
}

.group h3 {
  margin: 0 0 0.65rem;
  font-size: 1.05rem;
  font-weight: 600;
  color: var(--ink);
}

.spec {
  display: grid;
  grid-template-columns: 7.5rem 1fr;
  gap: 0.65rem 1rem;
  margin: 0;
  padding: 0.85rem 0 0;
  border-top: 1px solid var(--line);
}

.spec.tight {
  padding-top: 0.85rem;
}

.spec dt {
  margin: 0;
  font-size: 0.88rem;
  font-weight: 600;
  color: var(--ink);
  line-height: 1.5;
}

.spec dd {
  margin: 0;
  font-size: 0.92rem;
  color: var(--ink-2);
}

.note {
  font-size: 0.95rem;
  margin-bottom: 0.75rem;
}

.foot {
  border-top: 1px solid var(--line);
  padding: 1.25rem 0 1.75rem;
  font-size: 0.82rem;
  color: var(--muted);
}

.foot p + p {
  margin-top: 0.4rem;
}

.meta a {
  color: var(--muted);
}

.meta a:hover {
  color: var(--ink);
}

@media (max-width: 560px) {
  .spec {
    grid-template-columns: 1fr;
    gap: 0.2rem;
  }

  .spec dt {
    margin-top: 0.55rem;
  }

  .spec dt:first-child {
    margin-top: 0;
  }

  .spec dd {
    padding-bottom: 0.45rem;
    border-bottom: 1px solid var(--line);
  }

  .spec dd:last-child {
    border-bottom: none;
  }
}
</style>
