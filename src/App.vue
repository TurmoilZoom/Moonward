<script setup>
import { ref, watch } from 'vue'
import SiteNav from './components/SiteNav.vue'
import HeroSection from './components/HeroSection.vue'
import HighlightsSection from './components/HighlightsSection.vue'
import FeaturesSection from './components/FeaturesSection.vue'
import ShowcaseSection from './components/ShowcaseSection.vue'
import DownloadSection from './components/DownloadSection.vue'
import SiteFooter from './components/SiteFooter.vue'
import Lightbox from './components/Lightbox.vue'
import { useReveal } from './composables/useReveal'

const locale = ref(localStorage.getItem('moonward-locale') || 'zh')
const lightbox = ref({ open: false, src: '', caption: '' })

useReveal()

watch(locale, (v) => {
  localStorage.setItem('moonward-locale', v)
  document.documentElement.lang = v === 'zh' ? 'zh-CN' : 'en'
})

function toggleLocale() {
  locale.value = locale.value === 'zh' ? 'en' : 'zh'
}

function openImage({ src, caption }) {
  lightbox.value = { open: true, src, caption: caption || '' }
}

function closeLightbox() {
  lightbox.value = { ...lightbox.value, open: false }
}
</script>

<template>
  <div class="app">
    <SiteNav :locale="locale" @toggle-locale="toggleLocale" />
    <main>
      <HeroSection :locale="locale" />
      <HighlightsSection :locale="locale" />
      <FeaturesSection :locale="locale" @open-image="openImage" />
      <ShowcaseSection :locale="locale" @open-image="openImage" />
      <DownloadSection :locale="locale" />
    </main>
    <SiteFooter :locale="locale" />
    <Lightbox
      :open="lightbox.open"
      :src="lightbox.src"
      :caption="lightbox.caption"
      @close="closeLightbox"
    />
  </div>
</template>

<style scoped>
.app {
  min-height: 100vh;
}
</style>
