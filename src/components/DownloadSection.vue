<script setup>
import { links, requirements } from '../data/content'

defineProps({
  locale: { type: String, required: true },
})
</script>

<template>
  <section id="download" class="section download">
    <div class="container">
      <div class="panel glass reveal">
        <div class="copy">
          <p class="eyebrow">{{ locale === 'zh' ? '开始使用' : 'Get started' }}</p>
          <h2>{{ locale === 'zh' ? '下载 Moonward，启程群星' : 'Download Moonward' }}</h2>
          <p class="lead">
            {{
              locale === 'zh'
                ? '从 GitHub Releases 获取对应 CPU 架构的安装包。项目基于 MIT 开源，欢迎 Star 与 Issue。'
                : 'Grab the package for your CPU architecture from GitHub Releases. MIT licensed — stars and issues welcome.'
            }}
          </p>
          <div class="actions">
            <a class="btn btn-primary" :href="links.download" target="_blank" rel="noopener noreferrer">
              {{ locale === 'zh' ? '前往 Releases' : 'Open Releases' }}
            </a>
            <a class="btn btn-ghost" :href="links.github" target="_blank" rel="noopener noreferrer">
              GitHub
            </a>
            <a class="btn btn-ghost" :href="links.upstream" target="_blank" rel="noopener noreferrer">
              {{ locale === 'zh' ? '上游 Starward' : 'Upstream Starward' }}
            </a>
          </div>
        </div>

        <div class="reqs">
          <h3>{{ locale === 'zh' ? '运行要求' : 'Requirements' }}</h3>
          <dl>
            <div v-for="item in requirements" :key="item.label">
              <dt>{{ locale === 'zh' ? item.label : item.labelEn }}</dt>
              <dd>{{ item.value }}</dd>
            </div>
          </dl>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.panel {
  display: grid;
  grid-template-columns: 1.4fr 1fr;
  gap: 2rem;
  padding: clamp(1.5rem, 4vw, 2.75rem);
  border-radius: calc(var(--radius) + 4px);
  position: relative;
  overflow: hidden;
}

.panel::before {
  content: '';
  position: absolute;
  inset: auto -20% -40% 30%;
  height: 70%;
  background: radial-gradient(circle, rgba(122, 162, 255, 0.22), transparent 65%);
  pointer-events: none;
}

.copy,
.reqs {
  position: relative;
  z-index: 1;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.7rem;
  margin-top: 1.5rem;
}

.reqs {
  padding: 1.25rem;
  border-radius: var(--radius);
  background: rgba(0, 0, 0, 0.22);
  border: 1px solid var(--border);
}

.reqs h3 {
  margin-bottom: 1rem;
  font-size: 1rem;
  color: var(--text-muted);
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-weight: 600;
}

dl {
  margin: 0;
  display: grid;
  gap: 0.9rem;
}

dl > div {
  display: grid;
  gap: 0.15rem;
}

dt {
  font-size: 0.78rem;
  color: var(--text-dim);
  font-family: var(--font-display);
  letter-spacing: 0.04em;
}

dd {
  margin: 0;
  font-weight: 600;
  font-size: 0.98rem;
}

@media (max-width: 800px) {
  .panel {
    grid-template-columns: 1fr;
  }
}
</style>
