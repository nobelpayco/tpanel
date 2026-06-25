<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default' } })

const { t, locale } = useI18n()
const { headers } = useApi()

const loading = ref(false)
const deposits = ref([])
const total = ref(0)
const totalAmount = ref(0)
const page = ref(1)
const perPage = ref(50)

const merchants = ref([])
const teams = ref([])
const banks = ref([])

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({ dateFormat: 'Y-m-d', altInput: true, altFormat: 'd.m.Y', locale: localeMap[locale.value] || Turkish }))

const filters = ref({
  id: '', status: 0, merchant: null, team: null, bank: null,
  name: '', player_id: '', order_id: '', u_id: '',
  min_amount: '', max_amount: '', date_from: '', date_to: '',
})

const showFilterDialog = ref(false)
const showDetailDialog = ref(false)
const detailData = ref(null)
const detailLoading = ref(false)

const isAdmin = computed(() => {
  const user = JSON.parse(localStorage.getItem('user') || '{}')
  return user.user_type == 1
})
const isTeamMember = computed(() => {
  const user = JSON.parse(localStorage.getItem('user') || '{}')
  return user.user_type == 2 || user.user_type == 5
})

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const formatDate = val => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}
const formatIban = iban => iban ? iban.replace(/(.{4})/g, '$1 ').trim() : '-'
// Banka kısa adı: yasal ekleri (A.Ş./T.A.Ş./T.A.O.) ve baştaki "Türkiye[ Cumhuriyeti]" kısmını at
const shortBank = (name) => {
  if (!name) return ''
  return name
    .replace(/\s*(T\.A\.Ş\.|A\.Ş\.|T\.A\.O\.|A\.O\.)\s*$/i, '')
    .replace(/^Türkiye Cumhuriyeti\s+/i, '')
    .replace(/^Türkiye\s+/i, '')
    .trim()
}

const statusLabels = { 1: 'Bekleyen', 2: 'İşlemde', 3: 'Onaylandı', 4: 'Reddedildi' }
const statusColors = { 1: 'warning', 2: 'info', 3: 'success', 4: 'error' }

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ page: page.value, per_page: perPage.value, converted_only: 1 })
    Object.entries(filters.value).forEach(([k, v]) => {
      if (v !== '' && v !== null && v !== 0) params.append(k, v)
    })
    const res = await fetch(`/api/deposits/all?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      deposits.value = data.deposits
      total.value = data.total
      totalAmount.value = data.total_amount
    }
  } finally { loading.value = false }
}

const fetchMeta = async () => {
  const res = await fetch('/api/deposits/filter-meta', { headers })
  if (res.ok) {
    const data = await res.json()
    merchants.value = data.merchants
    teams.value = data.teams
    banks.value = data.banks
  }
}

const applyFilters = () => {
  page.value = 1
  showFilterDialog.value = false
  fetchData()
}

const resetFilters = () => {
  filters.value = {
    id: '', status: 0, merchant: null, team: null, bank: null,
    name: '', player_id: '', order_id: '', u_id: '',
    min_amount: '', max_amount: '', date_from: '', date_to: '',
  }
  page.value = 1
  fetchData()
}

const openDetail = async (d) => {
  showDetailDialog.value = true
  detailData.value = null
  detailLoading.value = true
  try {
    const res = await fetch(`/api/deposits/${d.id}/detail`, { headers })
    if (res.ok) detailData.value = await res.json()
  } finally { detailLoading.value = false }
}

const totalPages = computed(() => Math.ceil(total.value / perPage.value))

onMounted(() => {
  fetchData()
  fetchMeta()
})

watch(page, fetchData)

const formatDuration = (dateStr) => {
  if (!dateStr) return '-'
  const diff = Math.floor((Date.now() - new Date(dateStr).getTime()) / 1000)
  if (diff < 60) return diff + ' Saniye'
  const min = Math.floor(diff / 60)
  if (min < 60) return min + ' Dakika ' + (diff % 60) + ' Saniye'
  const hr = Math.floor(min / 60)
  if (hr < 24) return hr + ' Saat ' + (min % 60) + ' Dakika'
  const day = Math.floor(hr / 24)
  return day + ' Gün ' + (hr % 24) + ' Saat'
}

const historyStatusLabel = (h) => statusLabels[h.status] || '-'
const historyStatusColor = (h) => statusColors[h.status] || 'default'
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle class="d-flex align-center gap-2">
            Dönüşen Yatırımlar
            <VChip color="primary" label size="small">{{ total }} kayıt</VChip>
            <VChip color="success" label size="small">{{ formatMoney(totalAmount) }}</VChip>
          </VCardTitle>
          <template #append>
            <div class="d-flex gap-2">
              <VBtn color="info" variant="outlined" prepend-icon="tabler-filter" @click="showFilterDialog = true">
                Filtrele
              </VBtn>
              <VBtn color="secondary" variant="text" icon @click="resetFilters">
                <VIcon icon="tabler-refresh" />
              </VBtn>
            </div>
          </template>
        </VCardItem>
        <VDivider />

        <VTable class="text-no-wrap" density="compact">
          <thead>
            <tr>
              <th>ID</th>
              <th v-if="!isTeamMember">Merchant</th>
              <th>Takım</th>
              <th>Order ID</th>
              <th>Müşteri</th>
              <th class="text-center">Güven</th>
              <th>Banka</th>
              <th class="text-end">Tutar</th>
              <th>Durum</th>
              <th>Tarih</th>
              <th class="text-end"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="d in deposits" :key="d.id" class="cursor-pointer" @click="openDetail(d)">
              <td>{{ d.id }}</td>
              <td v-if="!isTeamMember">{{ d.merchant_name || '-' }}</td>
              <td>{{ d.team_name }}</td>
              <td class="text-caption">{{ d.order_id || '-' }}</td>
              <td>
                <div class="font-weight-medium">{{ d.name || '-' }}</div>
                <div v-if="d.player_id" class="text-caption text-medium-emphasis">{{ d.player_id }}</div>
              </td>
              <td class="text-center">
                <VChip
                  v-if="d.trust_rate !== null"
                  :color="d.trust_rate >= 70 ? 'success' : d.trust_rate >= 50 ? 'warning' : 'error'"
                  label
                  size="x-small"
                >
                  %{{ d.trust_rate }}
                </VChip>
                <span v-else class="text-caption text-medium-emphasis">-</span>
              </td>
              <td>
                <div class="text-body-2">{{ d.account_holder || '-' }}</div>
                <div v-if="d.account_iban" class="text-caption text-medium-emphasis">{{ formatIban(d.account_iban) }}</div>
                <div v-if="d.bank_name" class="text-caption text-disabled">{{ shortBank(d.bank_name) }}</div>
              </td>
              <td class="text-end font-weight-bold">{{ formatMoney(d.amount) }}</td>
              <td>
                <VChip :color="statusColors[d.status]" label size="x-small">
                  {{ statusLabels[d.status] }}
                </VChip>
              </td>
              <td>
                <div class="text-body-2">{{ formatDate(d.created_at) }}</div>
              </td>
              <td class="text-end">
                <VBtn icon size="x-small" variant="text" @click.stop="openDetail(d)">
                  <VIcon icon="tabler-eye" size="18" />
                </VBtn>
              </td>
            </tr>
            <tr v-if="!loading && deposits.length === 0">
              <td :colspan="isTeamMember ? 10 : 11" class="text-center text-medium-emphasis py-4">Kayıt yok</td>
            </tr>
          </tbody>
        </VTable>

        <VDivider />
        <div class="d-flex align-center justify-space-between pa-3">
          <div class="text-caption text-medium-emphasis">
            Sayfa {{ page }} / {{ totalPages || 1 }} ({{ total }} toplam)
          </div>
          <VPagination v-model="page" :length="totalPages || 1" :total-visible="7" />
        </div>
      </VCard>
    </VCol>
  </VRow>

  <!-- Filtre Dialog -->
  <VDialog v-model="showFilterDialog" max-width="650">
    <VCard title="Filtrele">
      <VCardText>
        <VRow>
          <VCol cols="12" md="6">
            <AppTextField v-model="filters.id" type="number" label="ID" density="compact" />
          </VCol>
          <VCol cols="12" md="6">
            <VSelect
              v-model="filters.status"
              :items="[
                { title: 'Hepsi', value: 0 },
                { title: 'Bekliyor', value: 1 },
                { title: 'İşlemde', value: 2 },
                { title: 'Onaylandı', value: 3 },
                { title: 'Reddedildi', value: 4 },
              ]"
              label="Durum"
              density="compact"
            />
          </VCol>

          <VCol v-if="!isTeamMember" cols="12" md="6">
            <VAutocomplete
              v-model="filters.merchant"
              :items="[{ title: 'Hepsi', value: null }, ...merchants.map(m => ({ title: m.name, value: m.id }))]"
              label="Merchant"
              density="compact"
              clearable
            />
          </VCol>
          <VCol v-if="isAdmin" cols="12" md="6">
            <VAutocomplete
              v-model="filters.team"
              :items="[{ title: 'Hepsi', value: null }, ...teams.map(tm => ({ title: tm.name, value: tm.id }))]"
              label="Takım"
              density="compact"
              clearable
            />
          </VCol>

          <VCol cols="12" md="6">
            <VAutocomplete
              v-model="filters.bank"
              :items="[{ title: 'Hepsi', value: null }, ...banks.map(b => ({ title: b.name, value: b.id }))]"
              label="Banka"
              density="compact"
              clearable
            />
          </VCol>
          <VCol cols="12" md="6">
            <AppTextField v-model="filters.name" label="Ad Soyad" density="compact" />
          </VCol>

          <VCol cols="12" md="6">
            <AppTextField v-model="filters.player_id" label="Player ID" density="compact" />
          </VCol>
          <VCol cols="12" md="6">
            <AppTextField v-model="filters.order_id" label="Order ID" density="compact" />
          </VCol>

          <VCol cols="12" md="6">
            <AppTextField v-model="filters.u_id" label="U ID" density="compact" />
          </VCol>
          <VCol cols="6" md="3">
            <AppTextField v-model="filters.min_amount" type="number" label="Min Tutar" prefix="₺" density="compact" />
          </VCol>
          <VCol cols="6" md="3">
            <AppTextField v-model="filters.max_amount" type="number" label="Max Tutar" prefix="₺" density="compact" />
          </VCol>

          <VCol cols="12" md="6">
            <AppDateTimePicker v-model="filters.date_from" label="Başlangıç" :config="dateConfig" density="compact" />
          </VCol>
          <VCol cols="12" md="6">
            <AppDateTimePicker v-model="filters.date_to" label="Bitiş" :config="dateConfig" density="compact" />
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions>
        <VBtn variant="text" color="error" @click="resetFilters(); showFilterDialog = false">Temizle</VBtn>
        <VSpacer />
        <VBtn variant="text" @click="showFilterDialog = false">İptal</VBtn>
        <VBtn color="primary" @click="applyFilters">Uygula</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Detay Dialog -->
  <VDialog v-model="showDetailDialog" max-width="950">
    <VCard v-if="detailData" :loading="detailLoading">
      <VCardItem>
        <VCardTitle class="d-flex align-center justify-space-between">
          <span># ID {{ detailData.deposit.id }} — {{ detailData.deposit.name || '-' }}</span>
          <VBtn icon size="small" variant="text" @click="showDetailDialog = false">
            <VIcon icon="tabler-x" />
          </VBtn>
        </VCardTitle>
      </VCardItem>
      <VDivider />
      <VCardText>
        <VRow>
          <VCol cols="12" md="6">
            <table class="detail-table">
              <tr><th>Ekip</th><td>{{ detailData.deposit.team_name || '-' }}</td></tr>
              <tr>
                <th>Müşteri</th>
                <td>
                  <div class="d-flex align-center gap-2">
                    <div>
                      <div>{{ detailData.deposit.name || '-' }}</div>
                      <div class="text-caption text-medium-emphasis">{{ detailData.deposit.player_id || '-' }}</div>
                    </div>
                    <VChip
                      v-if="detailData.deposit.trust_rate !== null"
                      :color="detailData.deposit.trust_rate >= 70 ? 'success' : detailData.deposit.trust_rate >= 50 ? 'warning' : 'error'"
                      label
                      size="small"
                    >
                      Güven: %{{ detailData.deposit.trust_rate }}
                      <span class="text-caption ms-1">({{ detailData.deposit.trust_count }}/10)</span>
                    </VChip>
                  </div>
                </td>
              </tr>
              <tr><th>Tutar</th><td class="text-h6 font-weight-bold">{{ formatMoney(detailData.deposit.amount) }}</td></tr>
              <tr><th>Order ID</th><td>{{ detailData.deposit.order_id || '-' }}</td></tr>
            </table>
          </VCol>
          <VCol cols="12" md="6">
            <table class="detail-table">
              <tr>
                <th>Durum</th>
                <td>
                  <VChip :color="statusColors[detailData.deposit.status]" label size="small">{{ statusLabels[detailData.deposit.status] }}</VChip>
                </td>
              </tr>
              <tr><th>Oluşturulma</th><td>{{ formatDate(detailData.deposit.created_at) }}</td></tr>
              <tr>
                <th>Yatırılan Hesap</th>
                <td>
                  <div class="font-weight-bold">{{ detailData.deposit.account_holder || '-' }}</div>
                  <div class="text-caption">{{ formatIban(detailData.deposit.account_iban) }}</div>
                </td>
              </tr>
              <tr>
                <th>Banka</th>
                <td>
                  <img v-if="detailData.deposit.bank_logo" :src="detailData.deposit.bank_logo" :alt="detailData.deposit.bank_name" style="max-height: 24px;" />
                  <span v-else>{{ detailData.deposit.bank_name || '-' }}</span>
                </td>
              </tr>
            </table>
          </VCol>
        </VRow>

        <div v-if="detailData.history" class="mt-4">
          <div class="text-body-2 text-medium-emphasis mb-2">Üyenin Son İşlemleri</div>
          <VTable density="compact" class="text-no-wrap">
            <thead>
              <tr>
                <th>Ad Soyad</th>
                <th class="text-end">Tutar</th>
                <th>Tarih</th>
                <th>Durum</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="h in detailData.history" :key="h.id">
                <td>{{ h.name || '-' }}</td>
                <td class="text-end">{{ formatMoney(h.amount) }}</td>
                <td>
                  <div>{{ formatDuration(h.created_at) }}</div>
                  <div class="text-caption text-medium-emphasis">{{ formatDate(h.created_at) }}</div>
                </td>
                <td>
                  <VChip :color="historyStatusColor(h)" label size="x-small">{{ historyStatusLabel(h) }}</VChip>
                </td>
              </tr>
              <tr v-if="detailData.history.length === 0">
                <td colspan="4" class="text-center text-medium-emphasis">Geçmiş işlem yok</td>
              </tr>
            </tbody>
          </VTable>
        </div>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showDetailDialog = false">Kapat</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>

<style scoped>
.detail-table { width: 100%; border-collapse: collapse; }
.detail-table th { text-align: left; padding: 12px 8px; width: 35%; font-weight: 500; color: rgba(var(--v-theme-on-surface), 0.7); border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); }
.detail-table td { padding: 12px 8px; border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); }
</style>
