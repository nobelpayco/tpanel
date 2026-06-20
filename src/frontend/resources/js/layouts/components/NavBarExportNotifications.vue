<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'

const { t } = useI18n()
const { headers } = useApi()

const exports_ = ref([])
const showMenu = ref(false)

const unreadCount = computed(() => exports_.value.filter(e => e.status === 'completed' && !e.seen).length)
const pendingCount = computed(() => exports_.value.filter(e => e.status === 'pending' || e.status === 'processing').length)

const fetchExports = async () => {
  try {
    const res = await fetch('/api/exports', { headers })
    if (res.ok) exports_.value = await res.json()
  } catch {}
}

const clearAll = async () => {
  try {
    const res = await fetch('/api/exports/clear', { method: 'DELETE', headers })
    if (res.ok) exports_.value = []
  } catch {}
}

const download = (exp) => {
  exp.seen = true
  const token = localStorage.getItem('token')
  window.open(`/api/exports/${exp.id}/download?token=${token}`, '_blank')
}

const statusIcon = (status) => {
  const map = { pending: 'tabler-clock', processing: 'tabler-loader', completed: 'tabler-check', failed: 'tabler-x' }
  return map[status] || 'tabler-clock'
}

const statusColor = (status) => {
  const map = { pending: 'warning', processing: 'info', completed: 'success', failed: 'error' }
  return map[status] || 'secondary'
}

const formatDate = (val) => {
  if (!val) return ''
  const d = new Date(val)
  return d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}

// Poll every 10 seconds
let interval
onMounted(() => {
  fetchExports()
  interval = setInterval(fetchExports, 10000)
})
onUnmounted(() => clearInterval(interval))
</script>

<template>
  <VBadge
    :model-value="unreadCount > 0 || pendingCount > 0"
    :content="unreadCount || pendingCount"
    :color="pendingCount > 0 ? 'warning' : 'success'"
    overlap
  >
    <VBtn
      icon
      variant="text"
      size="small"
      @click="showMenu = !showMenu"
    >
      <VIcon icon="tabler-file-download" size="22" />
    </VBtn>
  </VBadge>

  <VMenu
    v-model="showMenu"
    :close-on-content-click="false"
    offset="14px"
    location="bottom end"
  >
    <template #activator="{ props }">
      <span v-bind="props" />
    </template>

    <VCard min-width="350" max-width="400">
      <VCardItem>
        <VCardTitle class="text-body-1">{{ t('export.title') }}</VCardTitle>
      </VCardItem>
      <VDivider />

      <VList v-if="exports_.length > 0" density="compact" class="py-0">
        <VListItem
          v-for="exp in exports_"
          :key="exp.id"
          :class="exp.status === 'completed' && !exp.seen ? 'bg-primary-lighten-5' : ''"
          @click="exp.status === 'completed' ? download(exp) : null"
        >
          <template #prepend>
            <VIcon :icon="statusIcon(exp.status)" :color="statusColor(exp.status)" size="20" />
          </template>
          <VListItemTitle class="text-body-2">
            {{ exp.status === 'completed' ? t('export.ready') : exp.status === 'processing' ? t('export.processing') : exp.status === 'failed' ? t('export.failed') : t('export.queued') }}
          </VListItemTitle>
          <VListItemSubtitle class="text-caption">
            {{ formatDate(exp.created_at) }}
            <span v-if="exp.filters" class="ms-1">
              ({{ exp.filters.date_from }} - {{ exp.filters.date_to }})
            </span>
          </VListItemSubtitle>
          <template #append>
            <VIcon v-if="exp.status === 'completed'" icon="tabler-download" size="18" color="success" />
            <VProgressCircular v-else-if="exp.status === 'processing' || exp.status === 'pending'" indeterminate size="16" width="2" color="warning" />
          </template>
        </VListItem>
      </VList>

      <VDivider v-if="exports_.length > 0" />
      <VCardActions v-if="exports_.length > 0">
        <VBtn variant="text" color="error" size="small" block @click="clearAll">
          {{ t('export.clear_all') }}
        </VBtn>
      </VCardActions>

      <VCardText v-else class="text-center text-medium-emphasis py-4">
        {{ t('export.no_exports') }}
      </VCardText>
    </VCard>
  </VMenu>
</template>
