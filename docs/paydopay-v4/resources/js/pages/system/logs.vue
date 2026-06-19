<script setup>
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', roles: [1] } })

const { t } = useI18n()
const { headers } = useApi()
const snackbar = useSnackbar()

const loading = ref(false)
const logs = ref([])
const total = ref(0)
const page = ref(1)
const pages = ref(1)
const filters = ref({ direction: '', type: '', q: '' })

const detail = ref(null)
const detailOpen = ref(false)
const detailLoading = ref(false)

const typeOptions = [
  { title: 'Hepsi', value: '' },
  { title: 'Inbound API (başarılı)', value: 'inbound_api' },
  { title: 'Inbound API (hata)', value: 'inbound_api_error' },
  { title: 'Success (Onay)', value: 'success' },
  { title: 'Fail (Red)', value: 'fail' },
  { title: 'Expire (Süre Dolumu)', value: 'expire' },
  { title: 'Manuel Yeniden Gönderim', value: 'manual_resend' },
]

const directionOptions = [
  { title: 'Hepsi', value: '' },
  { title: 'Giden (Callback)', value: 'out' },
  { title: 'Gelen (API Request)', value: 'in' },
]

const fetchLogs = async () => {
  loading.value = true
  try {
    const p = new URLSearchParams({ page: String(page.value) })
    if (filters.value.direction) p.append('direction', filters.value.direction)
    if (filters.value.type) p.append('type', filters.value.type)
    if (filters.value.q) p.append('q', filters.value.q)
    const res = await fetch(`/api/settings/logs?${p}`, { headers })
    if (res.ok) {
      const data = await res.json()
      logs.value = data.items
      total.value = data.total
      pages.value = data.pages
    } else {
      snackbar.error('Loglar yüklenemedi.')
    }
  } finally {
    loading.value = false
  }
}

const openDetail = async (id) => {
  detailOpen.value = true
  detail.value = null
  detailLoading.value = true
  try {
    const res = await fetch(`/api/settings/logs/${id}`, { headers })
    if (res.ok) detail.value = await res.json()
  } finally {
    detailLoading.value = false
  }
}

const applyFilters = () => { page.value = 1; fetchLogs() }
const resetFilters = () => { filters.value = { direction: '', type: '', q: '' }; page.value = 1; fetchLogs() }

const prettyJson = (s) => {
  if (!s) return ''
  try { return JSON.stringify(JSON.parse(s), null, 2) } catch { return s }
}

const formatDate = (val) => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

const statusColor = (status) => {
  if (status === null || status === undefined) return 'grey'
  if (status >= 200 && status < 300) return 'success'
  if (status >= 400 && status < 500) return 'warning'
  return 'error'
}

const typeColor = (type) => {
  return {
    success: 'success',
    fail: 'error',
    expire: 'warning',
    manual_resend: 'info',
    inbound_api: 'secondary',
    inbound_api_error: 'error',
  }[type] || 'default'
}

const directionLabel = (d) => d === 'out' ? 'Giden' : 'Gelen'

watch(page, fetchLogs)
onMounted(fetchLogs)
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle>API & Callback Logları</VCardTitle>
          <template #append>
            <VBtn variant="text" prepend-icon="tabler-refresh" @click="fetchLogs">Yenile</VBtn>
          </template>
        </VCardItem>
        <VDivider />

        <!-- Filters -->
        <VCardText class="d-flex gap-3 flex-wrap align-center">
          <VSelect
            v-model="filters.direction"
            :items="directionOptions"
            label="Yön"
            density="compact"
            style="max-width: 180px"
            hide-details
            @update:model-value="applyFilters"
          />
          <VSelect
            v-model="filters.type"
            :items="typeOptions"
            label="Tip"
            density="compact"
            style="max-width: 220px"
            hide-details
            @update:model-value="applyFilters"
          />
          <AppTextField
            v-model="filters.q"
            placeholder="Order ID / URL / Merchant ara"
            density="compact"
            style="max-width: 280px"
            hide-details
            @keyup.enter="applyFilters"
          />
          <VBtn size="small" color="primary" variant="tonal" @click="applyFilters">
            <VIcon start icon="tabler-filter" />Uygula
          </VBtn>
          <VBtn size="small" variant="text" @click="resetFilters">Temizle</VBtn>
        </VCardText>

        <VTable density="compact" class="text-no-wrap">
          <thead>
            <tr>
              <th>Zaman</th>
              <th>Yön</th>
              <th>Tip</th>
              <th>HTTP</th>
              <th>Süre</th>
              <th>URL / Order</th>
              <th>Merchant</th>
              <th>Tetikleyen</th>
              <th class="text-end">Detay</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="l in logs" :key="l.id">
              <td><span class="text-caption">{{ formatDate(l.created_at) }}</span></td>
              <td>
                <VChip size="x-small" label :color="l.direction === 'out' ? 'primary' : 'secondary'">
                  {{ directionLabel(l.direction) }}
                </VChip>
              </td>
              <td>
                <VChip size="x-small" label :color="typeColor(l.type)">{{ l.type }}</VChip>
              </td>
              <td>
                <VChip v-if="l.response_status" size="x-small" label :color="statusColor(l.response_status)">
                  {{ l.response_status }}
                </VChip>
                <span v-else-if="l.error" class="text-error text-caption" :title="l.error">err</span>
                <span v-else>-</span>
              </td>
              <td><span class="text-caption text-medium-emphasis">{{ l.duration_ms ?? '-' }} ms</span></td>
              <td>
                <div class="text-body-2" style="max-width: 320px; overflow: hidden; text-overflow: ellipsis;">{{ l.url || '-' }}</div>
                <div v-if="l.invest_order_id" class="text-caption text-medium-emphasis">{{ l.invest_order_id }}</div>
              </td>
              <td><span class="text-body-2">{{ l.merchant_name || '-' }}</span></td>
              <td><span class="text-body-2">{{ l.triggered_by_user || '-' }}</span></td>
              <td class="text-end">
                <VBtn icon size="x-small" variant="text" @click="openDetail(l.id)">
                  <VIcon icon="tabler-eye" size="18" />
                </VBtn>
              </td>
            </tr>
            <tr v-if="!loading && logs.length === 0">
              <td colspan="9" class="text-center text-medium-emphasis py-4">Log bulunamadı</td>
            </tr>
          </tbody>
        </VTable>

        <VDivider />
        <div class="d-flex align-center justify-space-between pa-3">
          <div class="text-caption text-medium-emphasis">
            Sayfa {{ page }} / {{ pages || 1 }} ({{ total }} toplam)
          </div>
          <VPagination v-model="page" :length="pages || 1" :total-visible="7" />
        </div>
      </VCard>
    </VCol>
  </VRow>

  <!-- Detay Dialog -->
  <VDialog v-model="detailOpen" max-width="900" scrollable>
    <VCard>
      <VCardItem class="pa-5">
        <div class="d-flex align-center">
          <div>
            <div class="text-h6 font-weight-bold">Log Detayı</div>
            <div v-if="detail" class="text-caption text-medium-emphasis">
              #{{ detail.id }} · {{ formatDate(detail.created_at) }}
            </div>
          </div>
          <VSpacer />
          <VBtn icon size="small" variant="text" @click="detailOpen = false">
            <VIcon icon="tabler-x" />
          </VBtn>
        </div>
      </VCardItem>
      <VDivider />
      <VCardText class="pa-5">
        <div v-if="detailLoading" class="text-center py-4">
          <VProgressCircular indeterminate />
        </div>
        <div v-else-if="detail">
          <div class="mb-3">
            <strong>URL:</strong>
            <code class="ms-1 text-caption">{{ detail.url }}</code>
          </div>
          <div class="d-flex gap-2 flex-wrap mb-3">
            <VChip size="small" label :color="detail.direction === 'out' ? 'primary' : 'secondary'">
              {{ directionLabel(detail.direction) }}
            </VChip>
            <VChip size="small" label :color="typeColor(detail.type)">{{ detail.type }}</VChip>
            <VChip v-if="detail.response_status" size="small" label :color="statusColor(detail.response_status)">
              HTTP {{ detail.response_status }}
            </VChip>
            <VChip v-if="detail.duration_ms !== null" size="small" label color="grey">
              {{ detail.duration_ms }} ms
            </VChip>
          </div>

          <div v-if="detail.error" class="mb-3">
            <strong class="text-error">Hata:</strong>
            <div class="text-caption">{{ detail.error }}</div>
          </div>

          <VRow>
            <VCol cols="12" md="6">
              <div class="text-body-2 font-weight-medium mb-1">İstek Body</div>
              <pre class="log-pre">{{ prettyJson(detail.request_payload) }}</pre>
            </VCol>
            <VCol cols="12" md="6">
              <div class="text-body-2 font-weight-medium mb-1">Yanıt Body</div>
              <pre class="log-pre">{{ prettyJson(detail.response_body) || '—' }}</pre>
            </VCol>
          </VRow>
        </div>
      </VCardText>
    </VCard>
  </VDialog>
</template>

<style scoped>
.log-pre {
  background: rgba(var(--v-theme-on-surface), 0.04);
  padding: 10px;
  border-radius: 6px;
  font-size: 11px;
  max-height: 360px;
  overflow: auto;
  white-space: pre-wrap;
  word-break: break-all;
}
</style>
