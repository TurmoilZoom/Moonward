<script setup>
import { links } from '../data/content'
import { asset } from '../utils/asset'

defineProps({
  locale: { type: String, required: true },
})
</script>

<template>
  <footer class="footer">
    <div class="container footer-inner">
      <div class="brand-line">
        <img :src="asset('logo.png')" alt="" width="28" height="28" />
        <div>
          <strong>Moonward</strong>
          <p>
            {{
              locale === 'zh'
                ? '基于 Starward 的开源米哈游启动器 · MIT License'
                : 'Open-source miHoYo launcher based on Starward · MIT'
            }}
          </p>
        </div>
      </div>

      <div class="links">
        <a :href="links.github" target="_blank" rel="noopener noreferrer">GitHub</a>
        <a :href="links.download" target="_blank" rel="noopener noreferrer">Releases</a>
        <a :href="links.issues" target="_blank" rel="noopener noreferrer">Issues</a>
        <a :href="links.upstream" target="_blank" rel="noopener noreferrer">Starward</a>
      </div>
    </div>
    <div class="container note">
      <p>
        {{
          locale === 'zh'
            ? 'Moonward / Starward 与 miHoYo / HoYoverse 无官方关联。游戏内容版权归原权利方所有。'
            : 'Moonward / Starward is not affiliated with miHoYo / HoYoverse. Game assets belong to their respective owners.'
        }}
      </p>
    </div>
  </footer>
</template>

<style scoped>
.footer {
  padding: 2.5rem 0 2rem;
  border-top: 1px solid var(--border);
  background: rgba(0, 0, 0, 0.2);
}

.footer-inner {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 1.25rem;
}

.brand-line {
  display: flex;
  gap: 0.75rem;
  align-items: center;
}

.brand-line img {
  border-radius: 8px;
}

.brand-line strong {
  font-family: var(--font-display);
  display: block;
}

.brand-line p {
  margin: 0.15rem 0 0;
  color: var(--text-dim);
  font-size: 0.85rem;
}

.links {
  display: flex;
  flex-wrap: wrap;
  gap: 0.85rem 1.25rem;
}

.links a {
  color: var(--text-muted);
  font-size: 0.9rem;
  transition: color 0.2s ease;
}

.links a:hover {
  color: var(--text);
}

.note {
  margin-top: 1.5rem;
}

.note p {
  margin: 0;
  color: var(--text-dim);
  font-size: 0.8rem;
  max-width: 720px;
}
</style>
