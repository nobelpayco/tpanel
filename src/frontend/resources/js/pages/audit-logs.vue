<script setup>
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', godMode: true } })

const { headers } = useApi()
const snackbar = useSnackbar()

const loading = ref(false)
const items = ref([])
const total = ref(0)
const page = ref(1)
const perPage = 50

const filters = ref({ search: '', method: 'all', from: '', to: '' })

const methodOptions = [
  { title: 'Tümü', value: 'all' },
  { title: 'POST', value: 'POST' },
  { title: 'PUT', value: 'PUT' },
  { title: 'DELETE', value: 'DELETE' },
  { title: 'PATCH', value: 'PATCH' },
]

const methodColor = (m) => ({ POST: 'success', PUT: 'info', DELETE: 'error', PATCH: 'warning' }[m] || 'secondary')
const statusColor = (s) => (s >= 200 && s < 300 ? 'success' : s >= 400 && s < 500 ? 'warning' : s >= 500 ? 'error' : 'secondary')

const pageCount = computed(() => Math.max(1, Math.ceil(total.value / perPage)))

const formatDate = (val) => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

const showDetail = ref(false)
const detailRow = ref(null)
const openDetail = (r) => { detailRow.value = r; showDetail.value = true }
const prettyMeta = (meta) => { try { return JSON.stringify(JSON.parse(meta), null, 2) } catch { return meta || '' } }

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ page: page.value, per_page: perPage })
    if (filters.value.search) params.append('search', filters.value.search)
    if (filters.value.method !== 'all') params.append('method', filters.value.method)
    if (filters.value.from) params.append('from', filters.value.from)
    if (filters.value.to) params.append('to', filters.value.to)
    const res = await fetch(`/api/audit-logs?${params}`, { headers })
    const data = await res.json()
    if (res.ok) { items.value = data.items || []; total.value = data.total || 0 }
    else snackbar.error(data.message || 'Yüklenemedi.')
  } catch { snackbar.error('Sunucu hatası.') } finally { loading.value = false }
}

const applyFilters = () => { page.value = 1; fetchData() }
const resetFilters = () => { filters.value = { search: '', method: 'all', from: '', to: '' }; page.value = 1; fetchData() }

watch(page, fetchData)
onMounted(fetchData)
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle class="d-flex align-center gap-2">
            <VIcon icon="tabler-history" />
            Denetim İzleri
            <VChip color="primary" label size="small">{{ total }}</VChip>
          </VCardTitle>
        </VCardItem>
        <VDivider />

        <VCardText class="d-flex flex-wrap gap-3 align-end">
          <AppTextField
            v-model="filters.search"
            label="Ara (kullanıcı / yol / aksiyon / açıklama)"
            density="compact"
            style="min-width: 280px;"
            prepend-inner-icon="tabler-search"
            clearable
            @keyup.enter="applyFilters"
          />
          <VSelect v-model="filters.method" :items="methodOptions" label="Yöntem" density="compact" style="max-width: 140px;" />
          <AppDateTimePicker v-model="filters.from" label="Başlangıç" density="compact" style="max-width: 160px;" />
          <AppDateTimePicker v-model="filters.to" label="Bitiş" density="compact" style="max-width: 160px;" />
          <VBtn color="primary" prepend-icon="tabler-filter" @click="applyFilters">Filtrele</VBtn>
          <VBtn variant="tonal" color="secondary" prepend-icon="tabler-x" @click="resetFilters">Temizle</VBtn>
        </VCardText>

        <VDivider />
        <VTable class="text-no-wrap" density="compact">
          <thead>
            <tr>
              <th>Tarih</th>
              <th>Kullanıcı</th>
              <th>Yöntem</th>
              <th>Aksiyon</th>
              <th>Açıklama</th>
              <th>Hedef</th>
              <th>IP</th>
              <th class="text-end">Sonuç</th>
              <th class="text-end">Detay</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="r in items" :key="r.id">
              <td class="text-body-2">{{ formatDate(r.created_at) }}</td>
              <td class="text-body-2">{{ r.user_name || '-' }}<span v-if="r.user_id" class="text-caption text-medium-emphasis"> (#{{ r.user_id }})</span></td>
              <td><VChip :color="methodColor(r.method)" label size="x-small">{{ r.method }}</VChip></td>
              <td class="text-body-2">{{ r.action || '-' }}</td>
              <td class="text-body-2" style="max-width: 320px; white-space: normal;">{{ r.description || '-' }}</td>
              <td class="text-body-2">{{ r.entity_id ? `#${r.entity_id}` : '-' }}</td>
              <td class="text-caption text-medium-emphasis">{{ r.ip || '-' }}</td>
              <td class="text-end"><VChip :color="statusColor(r.status_code)" label size="x-small">{{ r.status_code }}</VChip></td>
              <td class="text-end">
                <VBtn icon size="x-small" variant="text" @click="openDetail(r)"><VIcon icon="tabler-eye" size="18" /></VBtn>
              </td>
            </tr>
            <tr v-if="!loading && items.length === 0">
              <td colspan="9" class="text-center text-medium-emphasis py-4">Kayıt yok</td>
            </tr>
          </tbody>
        </VTable>

        <VDivider />
        <div class="d-flex justify-end pa-3">
          <VPagination v-model="page" :length="pageCount" :total-visible="7" density="compact" />
        </div>
      </VCard>
    </VCol>
  </VRow>

  <!-- Detay dialog (eski/yeni değer) -->
  <VDialog v-model="showDetail" max-width="640">
    <VCard v-if="detailRow">
      <VCardItem>
        <VCardTitle class="d-flex align-center gap-2"><VIcon icon="tabler-history" />Denetim Kaydı #{{ detailRow.id }}</VCardTitle>
        <template #append>
          <VBtn icon size="small" variant="text" @click="showDetail = false"><VIcon icon="tabler-x" /></VBtn>
        </template>
      </VCardItem>
      <VDivider />
      <VCardText>
        <table class="detail-kv">
          <tr><td>Tarih</td><td>{{ formatDate(detailRow.created_at) }}</td></tr>
          <tr><td>Kullanıcı</td><td>{{ detailRow.user_name || '-' }}<span v-if="detailRow.user_id"> (#{{ detailRow.user_id }})</span></td></tr>
          <tr><td>Aksiyon</td><td>{{ detailRow.action || '-' }}</td></tr>
          <tr><td>Açıklama</td><td>{{ detailRow.description || '-' }}</td></tr>
          <tr><td>Yöntem / Yol</td><td><VChip :color="methodColor(detailRow.method)" label size="x-small">{{ detailRow.method }}</VChip> {{ detailRow.path }}</td></tr>
          <tr><td>Hedef</td><td>{{ detailRow.entity_type || '-' }}{{ detailRow.entity_id ? ` #${detailRow.entity_id}` : '' }}</td></tr>
          <tr><td>IP</td><td>{{ detailRow.ip || '-' }}</td></tr>
          <tr><td>Sonuç</td><td><VChip :color="statusColor(detailRow.status_code)" label size="x-small">{{ detailRow.status_code }}</VChip></td></tr>
        </table>
        <div v-if="detailRow.meta" class="mt-4">
          <div class="text-subtitle-2 mb-1">Eski / Yeni Değer</div>
          <pre class="meta-box">{{ prettyMeta(detailRow.meta) }}</pre>
        </div>
        <div v-else class="text-caption text-medium-emphasis mt-4">Bu kayıt için before/after bilgisi yok (jenerik kayıt).</div>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showDetail = false">Kapat</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>

<style scoped>
.detail-kv { width: 100%; border-collapse: collapse; }
.detail-kv td { padding: 6px 8px; border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); vertical-align: top; }
.detail-kv td:first-child { width: 30%; color: rgba(var(--v-theme-on-surface), 0.6); }
.meta-box { background: rgba(var(--v-theme-on-surface), 0.05); padding: 12px; border-radius: 6px; font-size: 12px; white-space: pre-wrap; word-break: break-word; max-height: 300px; overflow: auto; }
</style>
