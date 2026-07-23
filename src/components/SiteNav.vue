<script setup>
import { onMounted, onUnmounted, ref } from 'vue'
import { links } from '../data/content'
import { asset } from '../utils/asset'

defineProps({
  locale: { type: String, required: true },
})

const emit = defineEmits(['toggle-locale'])

const scrolled = ref(false)
const open = ref(false)

function onScroll() {
  scrolled.value = window.scrollY > 16
}

function close() {
  open.value = false
}

onMounted(() => {
  onScroll()
  window.addEventListener('scroll', onScroll, { passive: true })
})

onUnmounted(() => {
  window.removeEventListener('scroll', onScroll)
})
</script>

<template>
  <header class="nav" :class="{ scrolled, open }">
    <div class="container nav-inner">
      <a class="brand" href="#top" @click="close">
        <img :src="asset('logo.png')" alt="Moonward" width="36" height="36" />
        <span>Moonward</span>
      </a>

      <button
        class="menu-toggle"
        type="button"
        :aria-expanded="open"
        aria-label="Menu"
        @click="open = !open"
      >
        <span />
        <span />
      </button>

      <nav class="nav-links" :aria-hidden="!open && undefined">
        <a href="#features" @click="close">{{ locale === 'zh' ? '功能' : 'Features' }}</a>
        <a href="#showcase" @click="close">{{ locale === 'zh' ? '截图' : 'Showcase' }}</a>
        <a href="#download" @click="close">{{ locale === 'zh' ? '下载' : 'Download' }}</a>
        <a :href="links.github" target="_blank" rel="noopener noreferrer" @click="close">
          GitHub
        </a>
        <button class="lang" type="button" @click="emit('toggle-locale')">
          {{ locale === 'zh' ? 'EN' : '中文' }}
        </button>
        <a class="btn btn-primary nav-cta" :href="links.download" target="_blank" rel="noopener noreferrer">
          {{ locale === 'zh' ? '获取 Moonward' : 'Get Moonward' }}
        </a>
      </nav>
    </div>
  </header>
</template>

<style scoped>
.nav {
  position: sticky;
  top: 0;
  z-index: 50;
  height: var(--nav-h);
  transition:
    background 0.25s ease,
    border-color 0.25s ease,
    backdrop-filter 0.25s ease;
  border-bottom: 1px solid transparent;
}

.nav.scrolled {
  background: rgba(7, 11, 22, 0.72);
  border-bottom-color: var(--border);
  backdrop-filter: blur(18px) saturate(140%);
}

.nav-inner {
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.brand {
  display: inline-flex;
  align-items: center;
  gap: 0.7rem;
  font-family: var(--font-display);
  font-weight: 700;
  font-size: 1.1rem;
  letter-spacing: -0.02em;
  z-index: 2;
}

.brand img {
  width: 36px;
  height: 36px;
  border-radius: 10px;
  box-shadow: 0 8px 20px rgba(122, 162, 255, 0.25);
}

.nav-links {
  display: flex;
  align-items: center;
  gap: 0.35rem 1.15rem;
}

.nav-links a:not(.btn) {
  color: var(--text-muted);
  font-size: 0.92rem;
  font-weight: 500;
  transition: color 0.2s ease;
}

.nav-links a:not(.btn):hover {
  color: var(--text);
}

.lang {
  min-width: 2.6rem;
  height: 2.2rem;
  border-radius: 999px;
  border: 1px solid var(--border-strong);
  color: var(--text-muted);
  font-family: var(--font-display);
  font-size: 0.8rem;
  font-weight: 600;
  transition:
    color 0.2s ease,
    border-color 0.2s ease,
    background 0.2s ease;
}

.lang:hover {
  color: var(--text);
  background: rgba(255, 255, 255, 0.05);
}

.nav-cta {
  padding: 0.65rem 1.1rem;
  font-size: 0.88rem;
}

.menu-toggle {
  display: none;
  width: 42px;
  height: 42px;
  border-radius: 12px;
  border: 1px solid var(--border);
  place-items: center;
  gap: 5px;
  z-index: 2;
}

.menu-toggle span {
  display: block;
  width: 16px;
  height: 1.5px;
  background: var(--text);
  transition: transform 0.2s ease, opacity 0.2s ease;
}

@media (max-width: 860px) {
  .menu-toggle {
    display: grid;
  }

  .nav-links {
    position: fixed;
    inset: 0;
    padding: calc(var(--nav-h) + 1rem) 1.5rem 2rem;
    flex-direction: column;
    align-items: stretch;
    gap: 0.35rem;
    background: rgba(5, 7, 15, 0.94);
    backdrop-filter: blur(20px);
    transform: translateY(-110%);
    opacity: 0;
    pointer-events: none;
    transition:
      transform 0.3s ease,
      opacity 0.3s ease;
  }

  .nav.open .nav-links {
    transform: none;
    opacity: 1;
    pointer-events: auto;
  }

  .nav-links a:not(.btn),
  .lang {
    padding: 0.9rem 0.4rem;
    font-size: 1.05rem;
  }

  .nav-cta {
    margin-top: 0.75rem;
    width: 100%;
  }

  .nav.open .menu-toggle span:first-child {
    transform: translateY(3.25px) rotate(45deg);
  }

  .nav.open .menu-toggle span:last-child {
    transform: translateY(-3.25px) rotate(-45deg);
  }
}
</style>
