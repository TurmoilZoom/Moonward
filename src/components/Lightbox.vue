<script setup>
import { computed, onMounted, onUnmounted, watch } from 'vue'
import { asset } from '../utils/asset'

const props = defineProps({
  open: { type: Boolean, default: false },
  src: { type: String, default: '' },
  caption: { type: String, default: '' },
})

const emit = defineEmits(['close'])

const resolvedSrc = computed(() => {
  if (!props.src) return ''
  if (/^https?:\/\//i.test(props.src)) return props.src
  return asset(props.src)
})

function onKey(e) {
  if (e.key === 'Escape') emit('close')
}

watch(
  () => props.open,
  (v) => {
    document.body.style.overflow = v ? 'hidden' : ''
  },
)

onMounted(() => window.addEventListener('keydown', onKey))
onUnmounted(() => {
  window.removeEventListener('keydown', onKey)
  document.body.style.overflow = ''
})
</script>

<template>
  <Teleport to="body">
    <Transition name="fade">
      <div v-if="open" class="overlay" role="dialog" aria-modal="true" @click.self="emit('close')">
        <button class="close" type="button" aria-label="Close" @click="emit('close')">×</button>
        <figure>
          <img :src="resolvedSrc" :alt="caption" />
          <figcaption v-if="caption">{{ caption }}</figcaption>
        </figure>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  z-index: 100;
  display: grid;
  place-items: center;
  padding: 2rem;
  background: rgba(3, 5, 12, 0.86);
  backdrop-filter: blur(10px);
}

figure {
  margin: 0;
  max-width: min(1200px, 100%);
  max-height: 100%;
}

img {
  max-height: min(80vh, 900px);
  width: auto;
  max-width: 100%;
  margin-inline: auto;
  border-radius: 14px;
  border: 1px solid var(--border-strong);
  box-shadow: var(--shadow);
}

figcaption {
  text-align: center;
  margin-top: 0.85rem;
  color: var(--text-muted);
  font-family: var(--font-display);
}

.close {
  position: absolute;
  top: 1rem;
  right: 1rem;
  width: 44px;
  height: 44px;
  border-radius: 50%;
  border: 1px solid var(--border-strong);
  background: rgba(255, 255, 255, 0.06);
  font-size: 1.6rem;
  line-height: 1;
  color: var(--text);
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.22s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
