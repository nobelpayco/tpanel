<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'
import { useRoute, useRouter } from 'vue-router'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default', roles: [1] } })

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const storageId = route.params.id
const snackbar = useSnackbar()

const { headers } = useApi()

const loading = ref(true)
const data = ref(null)
const currentUser = ref(null)

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const monthStart = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-01` })()
const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({ dateFormat: 'Y-m-d', altInput: true, altFormat: 'd.m.Y', locale: localeMap[locale.value] || Turkish }))

const dateFrom = ref(monthStart)
const dateTo = ref(today)

const showSyncDialog = ref(false)
const syncForm = ref({ amount: 0, description: '', sync_date: today })

const isHeav = computed(() => [1, 4].includes(Number(currentUser.value?.user_type)))

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const formatDateTime = val => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}
const netColor = val => val >= 0 ? 'success' : 'error'

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ date_from: dateFrom.value, date_to: dateTo.value })
    const res = await fetch(`/api/fund-storages/${storageId}?${params}`, { headers })
    if (res.ok) data.value = await res.json()
  } finally { loading.value = false }
}

const fetchCurrentUser = async () => {
  const res = await fetch('/api/auth/me', { headers })
  if (res.ok) {
    const d = await res.json()
    currentUser.value = d.user || d
  }
}

const addSync = async () => {
  const res = await fetch('/api/fund-storage-syncs', {
    method: 'POST', headers,
    body: JSON.stringify({ fund_storage_id: parseInt(storageId), ...syncForm.value }),
  })
  const resData = await res.json()
  if (res.ok) {
    showSyncDialog.value = false
    syncForm.value = { amount: 0, description: '', sync_date: today }
    snackbar.success(resData.message)
    fetchData()
  } else {
    snackbar.handleError(resData)
  }
}

const deleteSync = async (rawId) => {
  const id = String(rawId).replace('sync_', '')
  if (!confirm('Bu senrkon kaydını silmek istediğinize emin misiniz?')) return
  const res = await fetch(`/api/fund-storage-syncs/${id}`, { method: 'DELETE', headers })
  const resData = await res.json()
  if (res.ok) { snackbar.success(resData.message); fetchData() }
  else { snackbar.handleError(resData) }
}

onMounted(() => { fetchData(); fetchCurrentUser() })
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardText class="d-flex align-center justify-space-between flex-wrap gap-4">
          <div class="d-flex align-center gap-3">
            <VBtn icon variant="text" size="small" @click="router.push('/case-report')">
              <VIcon icon="tabler-arrow-left" />
            </VBtn>
            <div>
              <h4 class="text-h4 font-weight-bold">{{ data?.storage?.name }}</h4>
              <VChip :color="data?.storage?.type === 1 ? 'warning' : 'info'" label size="x-small">
                {{ data?.storage?.type === 1 ? t('fund_storage.external') : t('fund_storage.internal') }}
              </VChip>
            </div>
          </div>
          <div class="text-center">
            <div class="text-body-2 text-medium-emphasis">Anlık Bakiye</div>
            <h5 class="text-h5 font-weight-bold text-info">{{ formatMoney(data?.storage?.balance || 0) }}</h5>
          </div>
          <VBtn v-if="isHeav" color="primary" @click="showSyncDialog = true">
            <VIcon start icon="tabler-refresh" />
            Senkron
          </VBtn>
        </VCardText>
      </VCard>
    </VCol>

    <VCol v-if="data?.summary" cols="12">
      <VCard :loading="loading">
        <VCardText class="d-flex gap-4 flex-wrap">
          <div class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-success), 0.08); min-width: 150px;">
            <div class="text-body-2 text-medium-emphasis">Toplam Giriş</div>
            <h5 class="text-h5 font-weight-bold text-success">{{ formatMoney(data.summary.total_in) }}</h5>
          </div>
          <div class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-error), 0.08); min-width: 150px;">
            <div class="text-body-2 text-medium-emphasis">Toplam Çıkış</div>
            <h5 class="text-h5 font-weight-bold text-error">{{ formatMoney(data.summary.total_out) }}</h5>
          </div>
          <div class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-primary), 0.08); min-width: 150px;">
            <div class="text-body-2 text-medium-emphasis">Net</div>
            <h5 class="text-h5 font-weight-bold" :class="`text-${netColor(data.summary.net)}`">{{ formatMoney(data.summary.net) }}</h5>
          </div>
        </VCardText>
      </VCard>
    </VCol>

    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle>Hareket Geçmişi</VCardTitle>
        </VCardItem>
        <VCardText class="d-flex align-center gap-4 flex-wrap">
          <div style="min-width: 160px;">
            <AppDateTimePicker v-model="dateFrom" :label="t('common.start_date')" :config="dateConfig" density="compact" />
          </div>
          <div style="min-width: 160px;">
            <AppDateTimePicker v-model="dateTo" :label="t('common.end_date')" :config="dateConfig" density="compact" />
          </div>
          <VBtn color="primary" @click="fetchData">{{ t('common.filter') }}</VBtn>
        </VCardText>
        <VDivider />
        <VTable class="text-no-wrap" density="compact">
          <thead>
            <tr>
              <th>{{ t('deposits.date') }}</th>
              <th>Yön</th>
              <th>Kaynak</th>
              <th>Hedef</th>
              <th class="text-end">{{ t('deposits.amount') }}</th>
              <th class="text-end">Önceki Bakiye</th>
              <th class="text-end">Sonraki Bakiye</th>
              <th>İşlemi Yapan</th>
              <th>{{ t('merchant_case.description') }}</th>
              <th v-if="isHeav" class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="m in data?.movements" :key="m.id">
              <td class="text-body-2">{{ formatDateTime(m.created_at) }}</td>
              <td>
                <VChip :color="m.direction === 'in' ? 'success' : 'error'" label size="x-small">
                  <VIcon :icon="m.direction === 'in' ? 'tabler-arrow-down' : 'tabler-arrow-up'" size="14" class="me-1" />
                  {{ m.direction === 'in' ? 'Giriş' : 'Çıkış' }}
                </VChip>
              </td>
              <td class="text-body-2">{{ m.source }}</td>
              <td class="text-body-2 font-weight-medium">{{ m.target || '-' }}</td>
              <td class="text-end font-weight-medium" :class="m.direction === 'in' ? 'text-success' : 'text-error'">
                {{ m.direction === 'in' ? '+' : '-' }}{{ formatMoney(m.amount) }}
              </td>
              <td class="text-end text-medium-emphasis">{{ formatMoney(m.balance_before ?? 0) }}</td>
              <td class="text-end font-weight-medium">{{ formatMoney(m.balance_after ?? 0) }}</td>
              <td class="text-body-2">{{ m.created_by || '-' }}</td>
              <td class="text-body-2 text-medium-emphasis">{{ m.description || '-' }}</td>
              <td v-if="isHeav" class="text-end">
                <VBtn v-if="String(m.id).startsWith('sync_')" icon size="x-small" variant="text" color="error" @click="deleteSync(m.id)">
                  <VIcon icon="tabler-trash" size="18" />
                </VBtn>
              </td>
            </tr>
            <tr v-if="!loading && (!data?.movements || data.movements.length === 0)">
              <td :colspan="isHeav ? 10 : 9" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>
  </VRow>

  <!-- Senkron Ekle -->
  <VDialog v-model="showSyncDialog" max-width="450">
    <VCard title="Senkron Ekle">
      <VCardText>
        <AppTextField v-model="syncForm.amount" type="number" label="Tutar" prefix="₺" class="mb-4" />
        <AppTextField v-model="syncForm.description" :label="t('merchant_case.description')" class="mb-4" />
        <AppDateTimePicker v-model="syncForm.sync_date" :label="t('deposits.date')" :config="dateConfig" density="compact" />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showSyncDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="addSync">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
