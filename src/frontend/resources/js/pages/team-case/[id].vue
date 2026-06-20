<script setup>
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useSnackbar } from '@/composables/useSnackbar'
import { useTronLookup } from '@/composables/useTronLookup'
import { useApi } from '@/composables/useApi'
import { useBrand } from '@/composables/useBrand'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default', roles: [1, 4] } })

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const brand = useBrand()
const teamId = route.params.id

const loading = ref(true)
const team = ref({})
const currentCase = ref(0)
const dailyCases = ref([])
const fundStorages = ref([])

const pageTab = ref(0)

const showPaymentDialog = ref(false)
const showTransferDialog = ref(false)
const showSyncDialog = ref(false)
const showDayDetailDialog = ref(false)
const selectedDay = ref(null)
const dayPayments = ref([])
const dayPaymentsLoading = ref(false)

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const transferForm = ref({ to_team_id: null, amount: 0, description: '', payment_date: today })
const syncForm = ref({ amount: 0, description: '', payment_date: today })
const teamsList = ref([])
const monthStart = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-01` })()
const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({ dateFormat: 'Y-m-d', altInput: true, altFormat: 'd.m.Y', locale: localeMap[locale.value] || Turkish }))

const paymentForm = ref({ payment_type: 1, amount: 0, crypto_quantity: null, crypto_rate: null, tx_link: '', fund_storage_id: null, description: '', payment_date: today })

// Gün sonu kasaları tarih filtresi
const caseeDateFrom = ref(monthStart)
const caseeDateTo = ref(today)

// Ödemeler tab
const paymentsLoading = ref(false)
const allPayments = ref([])
const allPaymentsTotal = ref(0)
const paymentDateFrom = ref(monthStart)
const paymentDateTo = ref(today)

const { headers } = useApi()
const snackbar = useSnackbar()
const { txLoading, lookupTx } = useTronLookup(paymentForm)

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const formatDate = val => new Date(val).toLocaleDateString('tr-TR')
const formatDateTime = val => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}
const netColor = val => val >= 0 ? 'success' : 'error'

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ date_from: caseeDateFrom.value, date_to: caseeDateTo.value })
    const res = await fetch(`/api/team-cases/${teamId}?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      team.value = data.team
      currentCase.value = data.current_case
      dailyCases.value = data.daily_cases
      fundStorages.value = data.fund_storages
    }
  } finally { loading.value = false }
}

const fetchAllPayments = async () => {
  paymentsLoading.value = true
  try {
    const params = new URLSearchParams({ date_from: paymentDateFrom.value, date_to: paymentDateTo.value })
    const res = await fetch(`/api/team-cases/${teamId}/payments?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      allPayments.value = data.payments
      allPaymentsTotal.value = data.total
    }
  } finally { paymentsLoading.value = false }
}

const fetchTeamsList = async () => {
  const res = await fetch('/api/teams?status=all', { headers })
  if (res.ok) {
    const data = await res.json()
    const list = Array.isArray(data) ? data : (data.teams || [])
    teamsList.value = list.filter(item => String(item.id) !== String(teamId))
  }
}

const addTransfer = async () => {
  const res = await fetch(`/api/team-cases/${teamId}/transfers`, { method: 'POST', headers, body: JSON.stringify(transferForm.value) })
  const data = await res.json()
  if (res.ok) {
    showTransferDialog.value = false
    transferForm.value = { to_team_id: null, amount: 0, description: '', payment_date: today }
    snackbar.success(data.message)
    fetchData(); fetchAllPayments()
  } else {
    snackbar.handleError(data)
  }
}

const deleteTransferFromList = async (rawId) => {
  const id = String(rawId).replace(/^tt[oi]_/, '')
  if (!confirm(t('common.confirm') + '?')) return
  const res = await fetch(`/api/team-cases/${teamId}/transfers/${id}`, { method: 'DELETE', headers })
  const data = await res.json()
  if (res.ok) { snackbar.success(data.message); fetchData(); fetchAllPayments() }
  else { snackbar.handleError(data) }
}

const addSync = async () => {
  if (!syncForm.value.description || !syncForm.value.description.trim()) {
    snackbar.error('Açıklama zorunludur')
    return
  }
  const res = await fetch(`/api/team-cases/${teamId}/syncs`, { method: 'POST', headers, body: JSON.stringify(syncForm.value) })
  const data = await res.json()
  if (res.ok) {
    showSyncDialog.value = false
    syncForm.value = { amount: 0, description: '', payment_date: today }
    snackbar.success(data.message)
    fetchData(); fetchAllPayments()
  } else {
    snackbar.handleError(data)
  }
}

const deleteSyncFromList = async (rawId) => {
  const id = String(rawId).replace(/^sync_/, '')
  if (!confirm('Bu senkronu silmek istediğinize emin misiniz?')) return
  const res = await fetch(`/api/team-cases/${teamId}/syncs/${id}`, { method: 'DELETE', headers })
  const data = await res.json()
  if (res.ok) { snackbar.success(data.message); fetchData(); fetchAllPayments() }
  else { snackbar.handleError(data) }
}

onMounted(() => { fetchData(); fetchAllPayments(); fetchTeamsList() })

watch(() => [paymentForm.value.crypto_quantity, paymentForm.value.crypto_rate], ([qty, rate]) => {
  if (paymentForm.value.payment_type === 2 && qty && rate) {
    paymentForm.value.amount = parseFloat((qty * rate).toFixed(2))
  }
})

const addPayment = async () => {
  const res = await fetch(`/api/team-cases/${teamId}/payments`, { method: 'POST', headers, body: JSON.stringify(paymentForm.value) })
  const data = await res.json()
  if (res.ok) {
    showPaymentDialog.value = false
    paymentForm.value = { payment_type: 1, amount: 0, crypto_quantity: null, crypto_rate: null, tx_link: '', fund_storage_id: null, description: '', payment_date: today }
    snackbar.success(data.message)
    fetchData(); fetchAllPayments()
  } else {
    snackbar.handleError(data)
  }
}

const copyDayToClipboard = async (day) => {
  const fmt = (v) => Number(v || 0).toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' TL'
  const text = `**${team.value.name} - ${formatDate(day.date)}**

**Devir:** ${fmt(day.previous_balance)}
**Yatırım:** ${fmt(day.deposits)}
**Çekim:** ${fmt(day.withdrawals)}
**Komisyon:** ${fmt(day.team_commission)}
**Manuel Ödeme:** ${fmt(day.payments)}
**Toplam Takviye:** ${fmt(day.transfers_in)}

**Gün Sonu:** ${fmt(day.amount)}`
  try {
    await navigator.clipboard.writeText(text)
    snackbar.success('Kopyalandı')
  } catch (e) {
    snackbar.error('Kopyalanamadı')
  }
}

const dayDetailMode = ref('payments') // 'payments' | 'transfers'

const openDayDetail = async (day, mode = 'payments') => {
  selectedDay.value = day
  dayDetailMode.value = mode
  showDayDetailDialog.value = true
  dayPaymentsLoading.value = true
  try {
    const res = await fetch(`/api/team-cases/${teamId}/payments?date=${day.date}`, { headers })
    if (res.ok) {
      const data = await res.json()
      if (mode === 'transfers') {
        dayPayments.value = data.payments.filter(p => p.source === 'team_transfer_in')
      } else {
        dayPayments.value = data.payments.filter(p => p.source !== 'team_transfer_in')
      }
    }
  } finally { dayPaymentsLoading.value = false }
}

const deletePayment = async (paymentId) => {
  if (!confirm(t('common.confirm') + '?')) return
  const res = await fetch(`/api/team-cases/${teamId}/payments/${paymentId}`, { method: 'DELETE', headers })
  const data = await res.json()
  if (res.ok) { showDayDetailDialog.value = false; snackbar.success(data.message); fetchData(); fetchAllPayments() }
  else { snackbar.error(data.message) }
}

const deletePaymentFromList = async (paymentId) => {
  if (!confirm(t('common.confirm') + '?')) return
  const res = await fetch(`/api/team-cases/${teamId}/payments/${paymentId}`, { method: 'DELETE', headers })
  const data = await res.json()
  if (res.ok) { snackbar.success(data.message); fetchData(); fetchAllPayments() }
  else { snackbar.error(data.message) }
}

const isPaymentToday = (createdAt) => {
  if (!createdAt) return false
  return createdAt.substring(0, 10) === today
}
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardText class="d-flex align-center justify-space-between flex-wrap gap-4">
          <div class="d-flex align-center gap-3">
            <VBtn icon variant="text" size="small" @click="router.push('/team-cases')">
              <VIcon icon="tabler-arrow-left" />
            </VBtn>
            <div>
              <h4 class="text-h4 font-weight-bold">{{ team.name }}</h4>
              <span class="text-body-2 text-medium-emphasis">{{ t('teams.commission') }}: %{{ team.commission }}</span>
            </div>
          </div>
          <div class="text-center">
            <div class="text-body-2 text-medium-emphasis">{{ t('merchant_case.current_case') }}</div>
            <h5 class="text-h5 font-weight-bold" :class="`text-${netColor(currentCase)}`">{{ formatMoney(currentCase) }}</h5>
          </div>
          <div class="d-flex gap-2">
            <VBtn color="warning" variant="tonal" @click="showSyncDialog = true">
              <VIcon start icon="tabler-refresh" />
              Senkron
            </VBtn>
            <VBtn color="info" variant="tonal" @click="showTransferDialog = true">
              <VIcon start icon="tabler-arrows-exchange" />
              Takım Transferi
            </VBtn>
            <VBtn color="primary" @click="showPaymentDialog = true">
              <VIcon start icon="tabler-plus" />
              {{ t('merchant_case.add_payment') }}
            </VBtn>
          </div>
        </VCardText>
      </VCard>
    </VCol>

    <VCol cols="12">
      <VCard>
        <VTabs v-model="pageTab">
          <VTab>{{ t('merchant_case.daily_cases') }}</VTab>
          <VTab>{{ t('merchant_case.payment_history') }}</VTab>
        </VTabs>
      </VCard>
    </VCol>

    <!-- Tab 0: Gün sonu kasaları -->
    <VCol v-if="pageTab === 0" cols="12">
      <VCard :loading="loading">
        <VCardText class="d-flex align-center gap-4 flex-wrap">
          <div style="min-width: 160px;">
            <AppDateTimePicker v-model="caseeDateFrom" :label="t('common.start_date')" :config="dateConfig" density="compact" />
          </div>
          <div style="min-width: 160px;">
            <AppDateTimePicker v-model="caseeDateTo" :label="t('common.end_date')" :config="dateConfig" density="compact" />
          </div>
          <VBtn color="primary" @click="fetchData">{{ t('common.filter') }}</VBtn>
        </VCardText>
        <VDivider />
        <VTable class="text-no-wrap">
          <thead>
            <tr>
              <th>{{ t('deposits.date') }}</th>
              <th class="text-end">{{ t('merchant_case.carryover') }}</th>
              <th class="text-end">{{ t('dashboard.total_deposits') }}</th>
              <th class="text-end">{{ t('dashboard.total_withdrawals') }}</th>
              <th class="text-end">{{ t('case_report.team_commission') }}</th>
              <th class="text-end">{{ t('merchant_case.total_payments') }}</th>
              <th class="text-end">Toplam Takviye</th>
              <th class="text-end">{{ t('merchant_case.end_of_day') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="day in dailyCases" :key="day.date" class="cursor-pointer" :style="day.is_today ? 'background: rgba(var(--v-theme-primary), 0.08)' : ''" @click="copyDayToClipboard(day)">
              <td class="font-weight-medium">
                {{ formatDate(day.date) }}
                <VChip v-if="day.is_today" color="primary" label size="x-small" class="ms-1">{{ t('dashboard.today') }}</VChip>
              </td>
              <td class="text-end">{{ formatMoney(day.previous_balance) }}</td>
              <td class="text-end text-success">{{ formatMoney(day.deposits) }}</td>
              <td class="text-end text-error">{{ formatMoney(day.withdrawals) }}</td>
              <td class="text-end text-warning">{{ formatMoney(day.team_commission) }}</td>
              <td class="text-end text-info" @click.stop="openDayDetail(day, 'payments')">{{ day.payments > 0 ? formatMoney(day.payments) : '-' }}</td>
              <td class="text-end text-success" @click.stop="openDayDetail(day, 'transfers')">{{ day.transfers_in > 0 ? formatMoney(day.transfers_in) : '-' }}</td>
              <td class="text-end font-weight-bold" :class="`text-${netColor(day.amount)}`">{{ formatMoney(day.amount) }}</td>
            </tr>
            <tr v-if="!loading && dailyCases.length === 0">
              <td colspan="8" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>

    <!-- Tab 1: Ödemeler -->
    <VCol v-if="pageTab === 1" cols="12">
      <VCard :loading="paymentsLoading">
        <VCardText class="d-flex align-center gap-4 flex-wrap">
          <div style="min-width: 160px;">
            <AppDateTimePicker v-model="paymentDateFrom" :label="t('common.start_date')" :config="dateConfig" density="compact" />
          </div>
          <div style="min-width: 160px;">
            <AppDateTimePicker v-model="paymentDateTo" :label="t('common.end_date')" :config="dateConfig" density="compact" />
          </div>
          <VBtn color="primary" @click="fetchAllPayments">{{ t('common.filter') }}</VBtn>
          <VSpacer />
          <div class="text-end">
            <div class="text-body-2 text-medium-emphasis">{{ t('common.total') }}</div>
            <h5 class="text-h5 font-weight-bold">{{ formatMoney(allPaymentsTotal) }}</h5>
          </div>
        </VCardText>
        <VDivider />
        <VTable class="text-no-wrap" density="compact">
          <thead>
            <tr>
              <th>{{ t('deposits.date') }}</th>
              <th>Kaynak</th>
              <th>{{ t('merchant_case.payment_type_label') }}</th>
              <th class="text-end">{{ t('merchant_case.tl_amount') }}</th>
              <th>{{ t('fund_storage.name') }}</th>
              <th>{{ t('merchant_case.description') }}</th>
              <th class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in allPayments" :key="p.id">
              <td class="text-body-2">{{ formatDateTime(p.created_at) }}</td>
              <td>
                <VChip v-if="p.source === 'expense'" color="error" variant="tonal" label size="x-small">{{ brand }} Masraf</VChip>
                <VChip v-else-if="p.source === 'partner_payment'" color="info" variant="tonal" label size="x-small">Partner: {{ p.partner_name }}</VChip>
                <VChip v-else-if="p.source === 'intermediary_offset'" color="warning" variant="tonal" label size="x-small">Aracı Mahsup: {{ p.intermediary_name }}</VChip>
                <VChip v-else-if="p.source === 'team_transfer_out'" color="purple" variant="tonal" label size="x-small">Takım Transferi → {{ p.to_team_name }}</VChip>
                <VChip v-else-if="p.source === 'team_transfer_in'" color="success" variant="tonal" label size="x-small">Takım Transferi ← {{ p.from_team_name }}</VChip>
                <template v-else-if="p.source === 'team_sync'">
                  <VChip v-if="Number(p.amount) < 0" color="success" variant="tonal" label size="x-small">Takviye</VChip>
                  <VChip v-else color="warning" variant="tonal" label size="x-small">Senkron</VChip>
                </template>
                <VChip v-else color="primary" variant="tonal" label size="x-small">Takım Ödemesi</VChip>
              </td>
              <td>
                <VChip v-if="p.source === 'team_payment'" :color="p.payment_type === 1 ? 'success' : 'warning'" label size="x-small">
                  {{ p.payment_type === 1 ? t('merchant_case.type_tl') : t('merchant_case.type_crypto') }}
                </VChip>
                <span v-else class="text-medium-emphasis">-</span>
              </td>
              <td class="text-end font-weight-medium" :class="(p.source === 'team_sync' && Number(p.amount) < 0) ? 'text-success' : (p.source && p.source !== 'team_payment' ? 'text-error' : '')">{{ formatMoney(p.amount) }}</td>
              <td class="text-body-2">{{ p.fund_storage_name || '-' }}</td>
              <td class="text-body-2">
                {{ p.description || '-' }}
                <a v-if="p.tx_link" :href="p.tx_link" target="_blank" class="ms-1"><VIcon icon="tabler-external-link" size="14" /></a>
              </td>
              <td class="text-end">
                <VBtn v-if="p.source === 'team_payment' && isPaymentToday(p.created_at)" icon size="x-small" variant="text" color="error" @click="deletePaymentFromList(p.id)">
                  <VIcon icon="tabler-trash" size="18" />
                </VBtn>
                <VBtn v-else-if="p.source === 'team_transfer_out' && isPaymentToday(p.created_at)" icon size="x-small" variant="text" color="error" @click="deleteTransferFromList(p.id)">
                  <VIcon icon="tabler-trash" size="18" />
                </VBtn>
                <VBtn v-else-if="p.source === 'team_sync' && isPaymentToday(p.created_at)" icon size="x-small" variant="text" color="error" @click="deleteSyncFromList(p.id)">
                  <VIcon icon="tabler-trash" size="18" />
                </VBtn>
              </td>
            </tr>
            <tr v-if="!paymentsLoading && allPayments.length === 0">
              <td colspan="7" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>
  </VRow>

  <!-- Ödeme Al -->
  <VDialog v-model="showPaymentDialog" max-width="500">
    <VCard :title="t('merchant_case.add_payment')">
      <VCardText>
        <VRadioGroup v-model="paymentForm.payment_type" inline class="mb-4">
          <VRadio :label="t('merchant_case.type_tl')" :value="1" />
          <VRadio :label="t('merchant_case.type_crypto')" :value="2" />
        </VRadioGroup>

        <template v-if="paymentForm.payment_type === 2">
          <AppTextField
            v-model="paymentForm.tx_link"
            :label="t('merchant_case.tx_link')"
            placeholder="https://tronscan.org/#/transaction/..."
            class="mb-4"
            :loading="txLoading"
            @change="lookupTx(paymentForm.tx_link)"
          />
          <VRow class="mb-2">
            <VCol cols="4"><AppTextField v-model="paymentForm.crypto_quantity" type="number" :label="t('merchant_case.crypto_quantity')" :loading="txLoading" /></VCol>
            <VCol cols="4"><AppTextField v-model="paymentForm.crypto_rate" type="number" :label="t('merchant_case.crypto_rate')" prefix="₺" /></VCol>
            <VCol cols="4"><AppTextField v-model="paymentForm.amount" type="number" :label="t('merchant_case.tl_amount')" prefix="₺" /></VCol>
          </VRow>
        </template>

        <AppTextField v-if="paymentForm.payment_type === 1" v-model="paymentForm.amount" type="number" :label="t('merchant_case.tl_amount')" prefix="₺" class="mb-4" />
        <VSelect v-model="paymentForm.fund_storage_id" :items="fundStorages.map(f => ({ title: f.name, value: f.id }))" :label="t('fund_storage.name')" class="mb-4" />
        <AppTextField v-model="paymentForm.description" :label="t('merchant_case.description')" class="mb-4" />
        <AppDateTimePicker v-model="paymentForm.payment_date" :label="t('deposits.date')" :config="dateConfig" density="compact" />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showPaymentDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="addPayment">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Senkron -->
  <VDialog v-model="showSyncDialog" max-width="500">
    <VCard title="Senkron (Kasa Düşümü)">
      <VCardText>
        <AppTextField v-model="syncForm.amount" type="number" :label="t('merchant_case.tl_amount')" prefix="₺" class="mb-4" />
        <AppTextField v-model="syncForm.description" label="Açıklama (zorunlu)" :rules="[v => !!v || 'Açıklama zorunludur']" class="mb-4" />
        <AppDateTimePicker v-model="syncForm.payment_date" :label="t('deposits.date')" :config="dateConfig" density="compact" />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showSyncDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="warning" @click="addSync">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Takım Transferi -->
  <VDialog v-model="showTransferDialog" max-width="500">
    <VCard title="Takım Transferi">
      <VCardText>
        <VAutocomplete
          v-model="transferForm.to_team_id"
          :items="teamsList.map(item => ({ title: item.name, value: item.id }))"
          label="Hedef Takım"
          class="mb-4"
        />
        <AppTextField v-model="transferForm.amount" type="number" :label="t('merchant_case.tl_amount')" prefix="₺" class="mb-4" />
        <AppTextField v-model="transferForm.description" :label="t('merchant_case.description')" class="mb-4" />
        <AppDateTimePicker v-model="transferForm.payment_date" :label="t('deposits.date')" :config="dateConfig" density="compact" />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showTransferDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="addTransfer">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Gün Detay -->
  <VDialog v-model="showDayDetailDialog" max-width="600">
    <VCard :title="`${team.name} — ${selectedDay ? formatDate(selectedDay.date) : ''} (${dayDetailMode === 'transfers' ? 'Takviyeler' : 'Ödemeler'})`" :loading="dayPaymentsLoading">
      <VCardText>
        <div v-if="dayPayments.length > 0">
          <VTable density="compact" class="text-no-wrap">
            <thead>
              <tr>
                <th>{{ t('merchant_case.payment_type_label') }}</th>
                <th class="text-end">{{ t('merchant_case.tl_amount') }}</th>
                <th>{{ t('fund_storage.name') }}</th>
                <th>{{ t('merchant_case.description') }}</th>
                <th>{{ t('deposits.date') }}</th>
                <th v-if="selectedDay?.is_today" class="text-end">{{ t('common.actions') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="p in dayPayments" :key="p.id">
                <td>
                  <VChip :color="p.payment_type === 1 ? 'success' : 'warning'" label size="x-small">
                    {{ p.payment_type === 1 ? t('merchant_case.type_tl') : t('merchant_case.type_crypto') }}
                  </VChip>
                </td>
                <td class="text-end font-weight-medium">{{ formatMoney(p.amount) }}</td>
                <td class="text-body-2">{{ p.fund_storage_name || '-' }}</td>
                <td class="text-body-2">
                  {{ p.description || '-' }}
                  <a v-if="p.tx_link" :href="p.tx_link" target="_blank" class="ms-1"><VIcon icon="tabler-external-link" size="14" /></a>
                </td>
                <td class="text-body-2 text-medium-emphasis">{{ new Date(p.created_at).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }) }}</td>
                <td v-if="selectedDay?.is_today" class="text-end">
                  <VBtn icon size="x-small" variant="text" color="error" @click="deletePayment(p.id)"><VIcon icon="tabler-trash" size="18" /></VBtn>
                </td>
              </tr>
            </tbody>
          </VTable>
        </div>
        <div v-else-if="!dayPaymentsLoading" class="text-center text-medium-emphasis py-4">{{ t('merchant_case.no_payments') }}</div>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showDayDetailDialog = false">{{ t('common.cancel') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
