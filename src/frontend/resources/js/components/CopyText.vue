<script setup>
import { useSnackbar } from '@/composables/useSnackbar'

// value  : panoya kopyalanacak ham değer (IBAN için boşluklar atılır)
// display : ekranda gösterilecek (biçimli) metin; verilmezse value gösterilir
const props = defineProps({
  value: { type: [String, Number, null], default: '' },
  display: { type: [String, Number, null], default: '' },
})

const snackbar = useSnackbar()
const copied = ref(false)

const copyText = async () => {
  const raw = (props.value ?? '').toString().replace(/\s+/g, '')
  if (!raw) return
  try {
    await navigator.clipboard.writeText(raw)
    copied.value = true
    snackbar.success('Kopyalandı')
    setTimeout(() => { copied.value = false }, 1200)
  } catch {
    snackbar.error('Kopyalanamadı')
  }
}
</script>

<template>
  <span class="copy-text d-inline-flex align-center gap-1">
    <span>{{ (display ?? '') !== '' ? display : (value || '-') }}</span>
    <VBtn
      v-if="value"
      icon
      size="x-small"
      variant="text"
      density="comfortable"
      class="copy-text__btn"
      :title="copied ? 'Kopyalandı' : 'Kopyala'"
      @click.stop="copyText"
    >
      <VIcon :icon="copied ? 'tabler-check' : 'tabler-copy'" :color="copied ? 'success' : undefined" size="14" />
    </VBtn>
  </span>
</template>

<style scoped>
.copy-text__btn { inline-size: 20px; block-size: 20px; }
</style>
