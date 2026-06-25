<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default' } })

const { t, locale } = useI18n()
const { headers } = useApi()
const snackbar = useSnackbar()

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
  min_amount: '', max_amount: '', date_from: '', date_to: '', added_type: 0,
})

const showFilterDialog = ref(false)
const showDetailDialog = ref(false)
const detailData = ref(null)
const detailLoading = ref(false)

const monthStart = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-01` })()
const showExportDialog = ref(false)
const exportLoading = ref(false)
const exportForm = ref({
  date_from: monthStart,
  date_to: today,
  type: '1',
  status: 'all',
  merchant_id: null,
  team_id: null,
})
const exportStatusOptions = [
  { title: 'Hepsi', value: 'all' },
  { title: 'Bekliyor', value: '1' },
  { title: 'İşlemde', value: '2' },
  { title: 'Onaylandı', value: '3' },
  { title: 'Reddedildi', value: '4' },
]

const submitExport = async () => {
  exportLoading.value = true
  try {
    const res = await fetch('/api/exports', {
      method: 'POST', headers,
      body: JSON.stringify(exportForm.value),
    })
    const data = await res.json()
    if (res.ok) {
      showExportDialog.value = false
      snackbar.success(data.message || 'Export başlatıldı. Bildirimlerden indirebilirsiniz.')
    } else {
      snackbar.error(data.message || 'Export başlatılamadı.')
    }
  } catch {
    snackbar.error('Sunucu hatası.')
  } finally {
    exportLoading.value = false
  }
}

const isAdmin = computed(() => {
  const user = JSON.parse(localStorage.getItem('user') || '{}')
  return user.user_type == 1
})
const isTeamMember = computed(() => {
  const user = JSON.parse(localStorage.getItem('user') || '{}')
  return user.user_type == 2 || user.user_type == 5
})
const isMerchant = computed(() => {
  const user = JSON.parse(localStorage.getItem('user') || '{}')
  return user.user_type == 3
})
// Manuel Yatırım yalnızca Super Admin (1) ve Sub Admin (4)
const canManualDeposit = computed(() => {
  const user = JSON.parse(localStorage.getItem('user') || '{}')
  return user.user_type == 1 || user.user_type == 4
})

const resendingId = ref(null)
const resendCallback = async (id) => {
  if (!confirm('Bu işlem için callback yeniden gönderilsin mi?')) return
  resendingId.value = id
  try {
    const res = await fetch(`/api/deposits/${id}/resend-callback`, { method: 'POST', headers })
    const data = await res.json()
    if (res.ok) snackbar.success(data.message || 'Callback gönderildi.')
    else snackbar.error(data.message || 'Gönderilemedi.')
  } catch {
    snackbar.error('Sunucu hatası.')
  } finally {
    resendingId.value = null
  }
}

// --- Manuel Yatırım Ekle ---
const showManualDialog = ref(false)
const manualSaving = ref(false)
const manualMetaLoading = ref(false)
const manualTeamLoading = ref(false)
const manualMerchants = ref([])
const manualTeams = ref([])
const manualBanks = ref([])
const manualAgents = ref([])
const manualForm = ref({ merchant_id: null, team_id: null, bank_id: null, agent_id: null, name: '', amount: '' })

const openManualDialog = async () => {
  manualForm.value = { merchant_id: null, team_id: null, bank_id: null, agent_id: null, name: '', amount: '' }
  manualBanks.value = []
  manualAgents.value = []
  showManualDialog.value = true
  manualMetaLoading.value = true
  try {
    const res = await fetch('/api/deposits/manual/meta', { headers })
    if (res.ok) {
      const data = await res.json()
      manualMerchants.value = data.merchants || []
      manualTeams.value = data.teams || []
      if (manualTeams.value.length === 1) manualForm.value.team_id = manualTeams.value[0].id
    } else {
      snackbar.error('Liste yüklenemedi.')
    }
  } catch {
    snackbar.error('Sunucu hatası.')
  } finally {
    manualMetaLoading.value = false
  }
}

watch(() => manualForm.value.team_id, async (teamId) => {
  manualForm.value.bank_id = null
  manualForm.value.agent_id = null
  manualBanks.value = []
  manualAgents.value = []
  if (!teamId) return
  manualTeamLoading.value = true
  try {
    const res = await fetch(`/api/deposits/manual/team/${teamId}`, { headers })
    if (res.ok) {
      const data = await res.json()
      manualBanks.value = data.banks || []
      manualAgents.value = data.agents || []
    }
  } catch {
    snackbar.error('Banka/agent listesi yüklenemedi.')
  } finally {
    manualTeamLoading.value = false
  }
})

const submitManual = async () => {
  if (!manualForm.value.merchant_id) { snackbar.error('Merchant seçin.'); return }
  if (!manualForm.value.team_id) { snackbar.error('Takım seçin.'); return }
  if (!manualForm.value.name?.trim()) { snackbar.error('Müşteri adı girin.'); return }
  if (!manualForm.value.amount || Number(manualForm.value.amount) <= 0) { snackbar.error('Geçerli bir tutar girin.'); return }
  manualSaving.value = true
  try {
    const res = await fetch('/api/deposits/manual', {
      method: 'POST', headers,
      body: JSON.stringify({
        merchant_id: manualForm.value.merchant_id,
        team_id: manualForm.value.team_id,
        bank_id: manualForm.value.bank_id,
        agent_id: manualForm.value.agent_id,
        name: manualForm.value.name.trim(),
        amount: Number(manualForm.value.amount),
      }),
    })
    const data = await res.json()
    if (res.ok) {
      showManualDialog.value = false
      snackbar.success(data.message || 'Manuel yatırım eklendi.')
      page.value = 1
      fetchData()
    } else {
      snackbar.error(data.message || 'Eklenemedi.')
    }
  } catch {
    snackbar.error('Sunucu hatası.')
  } finally {
    manualSaving.value = false
  }
}

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

const approvalSec = (d) => {
  if (Number(d.status) !== 3 || !d.finalize_date || !d.created_at) return null
  return Math.max(0, Math.floor((new Date(d.finalize_date) - new Date(d.created_at)) / 1000))
}
const formatApprovalTime = (sec) => {
  if (sec === null || sec === undefined) return ''
  if (sec < 60) return `${sec}sn`
  const m = Math.floor(sec / 60), s = sec % 60
  return s === 0 ? `${m}dk` : `${m}dk ${s}sn`
}

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ page: page.value, per_page: perPage.value })
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
    min_amount: '', max_amount: '', date_from: '', date_to: '', added_type: 0,
  }
  page.value = 1
  fetchData()
}

const openReceipt = async (depositId) => {
  try {
    const res = await fetch(`/api/deposits/${depositId}/receipt`, { headers })
    if (!res.ok) { snackbar.error('Dekont yüklenemedi.'); return }
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const win = window.open(url, '_blank')
    if (!win) snackbar.error('Yeni sekme açılamadı, popup engelleyici olabilir.')
    setTimeout(() => URL.revokeObjectURL(url), 60_000)
  } catch (e) {
    snackbar.error('Dekont yüklenemedi.')
  }
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
            Tüm Yatırımlar
            <VChip color="primary" label size="small">{{ total }} kayıt</VChip>
            <VChip color="success" label size="small">{{ formatMoney(totalAmount) }}</VChip>
          </VCardTitle>
          <template #append>
            <div class="d-flex gap-2">
              <VBtn v-if="canManualDeposit" color="primary" prepend-icon="tabler-plus" @click="openManualDialog">
                Manuel Yatırım
              </VBtn>
              <VBtn color="info" variant="outlined" prepend-icon="tabler-filter" @click="showFilterDialog = true">
                Filtrele
              </VBtn>
              <VBtn color="success" variant="outlined" prepend-icon="tabler-file-spreadsheet" @click="showExportDialog = true">
                Excel
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
              <td class="text-end">
                <div class="font-weight-bold">{{ formatMoney(d.amount) }}</div>
                <div
                  v-if="d.original_amount !== null && d.original_amount !== undefined && Number(d.original_amount) !== Number(d.amount)"
                  class="text-caption d-flex align-center justify-end gap-1 mt-1"
                  :class="Number(d.amount) >= Number(d.original_amount) ? 'text-success' : 'text-warning'"
                >
                  <VIcon
                    :icon="Number(d.amount) >= Number(d.original_amount) ? 'tabler-arrow-up-right' : 'tabler-arrow-down-right'"
                    size="14"
                  />
                  <span>Talep: {{ formatMoney(d.original_amount) }}</span>
                </div>
              </td>
              <td>
                <VChip :color="statusColors[d.status]" label size="x-small">
                  {{ statusLabels[d.status] }}
                </VChip>
                <div v-if="d.agent_name" class="text-caption text-medium-emphasis">{{ d.agent_name }}</div>
              </td>
              <td>
                <div class="text-body-2">{{ formatDate(d.created_at) }}</div>
                <VChip
                  v-if="approvalSec(d) !== null"
                  :color="approvalSec(d) > 180 ? 'error' : 'success'"
                  :prepend-icon="approvalSec(d) > 180 ? 'tabler-alert-triangle' : 'tabler-clock-check'"
                  size="x-small"
                  label
                  class="mt-1"
                  :title="approvalSec(d) > 180 ? '3 dakikayı aşan onay süresi' : 'Onay süresi'"
                >
                  Onay: {{ formatApprovalTime(approvalSec(d)) }}
                </VChip>
              </td>
              <td class="text-end">
                <VBtn icon size="x-small" variant="text" @click.stop="openDetail(d)">
                  <VIcon icon="tabler-eye" size="18" />
                </VBtn>
                <VBtn
                  v-if="isAdmin && [3, 4].includes(Number(d.status))"
                  icon size="x-small" variant="text" color="info"
                  :loading="resendingId === d.id"
                  title="Callback tekrar gönder"
                  @click.stop="resendCallback(d.id)"
                >
                  <VIcon icon="tabler-refresh-dot" size="18" />
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

          <VCol cols="12" md="6">
            <VSelect
              v-model="filters.added_type"
              :items="[
                { title: 'Tümü', value: 0 },
                { title: 'Otomatik', value: 1 },
                { title: 'Manuel', value: 2 },
              ]"
              label="Kaynak"
              density="compact"
            />
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

  <!-- Excel Export Dialog -->
  <VDialog v-model="showExportDialog" max-width="500">
    <VCard title="Excel İndir (Yatırımlar)">
      <VCardText>
        <VRow>
          <VCol cols="6">
            <AppDateTimePicker v-model="exportForm.date_from" label="Başlangıç" :config="dateConfig" density="compact" />
          </VCol>
          <VCol cols="6">
            <AppDateTimePicker v-model="exportForm.date_to" label="Bitiş" :config="dateConfig" density="compact" />
          </VCol>
          <VCol cols="12">
            <VSelect v-model="exportForm.status" :items="exportStatusOptions" label="Durum" density="compact" />
          </VCol>
          <VCol v-if="!isTeamMember && !isMerchant" cols="12">
            <VAutocomplete
              v-model="exportForm.merchant_id"
              :items="[{ title: 'Hepsi', value: null }, ...merchants.map(m => ({ title: m.name, value: m.id }))]"
              label="Merchant"
              density="compact"
              clearable
            />
          </VCol>
          <VCol v-if="!isTeamMember && !isMerchant" cols="12">
            <VAutocomplete
              v-model="exportForm.team_id"
              :items="[{ title: 'Hepsi', value: null }, ...teams.map(t => ({ title: t.name, value: t.id }))]"
              label="Takım"
              density="compact"
              clearable
            />
          </VCol>
        </VRow>
        <div class="text-caption text-medium-emphasis mt-2">
          Export hazır olunca üst menüdeki bildirim çanından indirebilirsiniz. Tarih filtresi sonuçlanma tarihine (pending ise oluşturma tarihine) göre çalışır.
        </div>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showExportDialog = false">İptal</VBtn>
        <VBtn color="success" :loading="exportLoading" prepend-icon="tabler-file-spreadsheet" @click="submitExport">
          Başlat
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Manuel Yatırım Dialog -->
  <VDialog v-model="showManualDialog" max-width="640">
    <VCard :loading="manualMetaLoading">
      <VCardItem>
        <template #prepend>
          <VAvatar color="primary" variant="tonal" rounded>
            <VIcon icon="tabler-cash-banknote" />
          </VAvatar>
        </template>
        <VCardTitle>Manuel Yatırım Ekle</VCardTitle>
        <VCardSubtitle>Onaylanmış (status=3) olarak kaydedilir, callback gönderilmez</VCardSubtitle>
        <template #append>
          <VBtn icon size="small" variant="text" @click="showManualDialog = false">
            <VIcon icon="tabler-x" />
          </VBtn>
        </template>
      </VCardItem>
      <VDivider />
      <VCardText>
        <VRow>
          <VCol cols="12">
            <VAutocomplete
              v-model="manualForm.merchant_id"
              :items="manualMerchants.map(m => ({ title: m.name, value: m.id }))"
              label="Site (Merchant)"
              prepend-inner-icon="tabler-building-store"
              density="compact"
            />
          </VCol>
          <VCol cols="12">
            <VAutocomplete
              v-model="manualForm.team_id"
              :items="manualTeams.map(tm => ({ title: tm.name, value: tm.id }))"
              label="Takım"
              prepend-inner-icon="tabler-users-group"
              density="compact"
            />
          </VCol>
          <VCol cols="12">
            <VAutocomplete
              v-model="manualForm.bank_id"
              :items="manualBanks.map(b => ({ title: b.name, value: b.id }))"
              label="Banka Hesabı"
              prepend-inner-icon="tabler-building-bank"
              density="compact"
              :disabled="!manualForm.team_id"
              :loading="manualTeamLoading"
              clearable
            />
          </VCol>
          <VCol cols="12">
            <VAutocomplete
              v-model="manualForm.agent_id"
              :items="manualAgents.map(a => ({ title: a.name, value: a.id }))"
              label="Takım Agent"
              prepend-inner-icon="tabler-user"
              density="compact"
              :disabled="!manualForm.team_id"
              :loading="manualTeamLoading"
              clearable
            />
          </VCol>
          <VCol cols="12" md="6">
            <AppTextField v-model="manualForm.name" label="Müşteri Adı" prepend-inner-icon="tabler-user" density="compact" />
          </VCol>
          <VCol cols="12" md="6">
            <AppTextField v-model="manualForm.amount" type="number" label="Tutar" prefix="₺" density="compact" />
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showManualDialog = false">İptal</VBtn>
        <VBtn color="primary" prepend-icon="tabler-device-floppy" :loading="manualSaving" @click="submitManual">
          Kaydet
        </VBtn>
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
              <tr v-if="detailData.deposit.receipt_path">
                <th>Dekont</th>
                <td>
                  <a href="#" @click.prevent="openReceipt(detailData.deposit.id)" class="receipt-link">
                    <VIcon icon="tabler-file-text" size="18" class="me-1" />
                    Dekontu Görüntüle
                    <VIcon icon="tabler-external-link" size="14" class="ms-1" />
                  </a>
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
.receipt-link { display: inline-flex; align-items: center; color: rgb(var(--v-theme-primary)); text-decoration: none; font-weight: 600; }
.receipt-link:hover { text-decoration: underline; }
</style>
