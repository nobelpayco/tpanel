<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useBrand } from '@/composables/useBrand'
import { useSnackbar } from '@/composables/useSnackbar'
import { useTronLookup } from '@/composables/useTronLookup'
import { useRoute, useRouter } from 'vue-router'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default', roles: [1] } })

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const brand = useBrand()
const partnerId = route.params.id

const loading = ref(true)
const partner = ref({})
const currentCase = ref(0)
const dailyCases = ref([])
const fundStorages = ref([])
const teamsList = ref([])

const pageTab = ref(0)

const showPaymentDialog = ref(false)
const showCapitalDialog = ref(false)
const showTransferDialog = ref(false)
const showDayDetailDialog = ref(false)
const selectedDay = ref(null)
const dayPayments = ref([])
const dayPaymentsLoading = ref(false)
const partnersList = ref([])

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const transferForm = ref({ to_partner_id: null, amount: 0, description: '', payment_date: today })
const monthStart = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-01` })()
const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({ dateFormat: 'Y-m-d', altInput: true, altFormat: 'd.m.Y', locale: localeMap[locale.value] || Turkish }))

const paymentForm = ref({ payment_type: 1, amount: 0, crypto_quantity: null, crypto_rate: null, tx_link: '', fund_storage_id: null, team_id: null, is_capital: false, description: '', payment_date: today })
const capitalForm = ref({ payment_type: 1, amount: 0, crypto_quantity: null, crypto_rate: null, tx_link: '', fund_storage_id: null, description: '', payment_date: today })

// Gün sonu kasaları tarih filtresi
const caseeDateFrom = ref(monthStart)
const caseeDateTo = ref(today)

// Ödemeler tab
const paymentsLoading = ref(false)
const allPayments = ref([])
const allPaymentsTotal = ref(0)
const paymentDateFrom = ref(monthStart)
const paymentDateTo = ref(today)

// Sermaye tab
const capitalsLoading = ref(false)
const allCapitals = ref([])
const allCapitalsTotal = ref(0)
const capitalDateFrom = ref(monthStart)
const capitalDateTo = ref(today)

// Partners list (for partner selector in capital - not needed since we're already on a partner page, but keep for context)
const partners = ref([])

const { headers } = useApi()
const snackbar = useSnackbar()
const { txLoading: payTxLoading, lookupTx: payLookupTx } = useTronLookup(paymentForm)
const { txLoading: capTxLoading, lookupTx: capLookupTx } = useTronLookup(capitalForm)

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const formatDate = val => new Date(val).toLocaleDateString('tr-TR')
const formatDateTime = val => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}
const netColor = val => val >= 0 ? 'success' : 'error'

const dailyCaseTotals = computed(() => {
  const t = { daily_share: 0, capitals: 0, expenses: 0, payments: 0 }
  for (const d of dailyCases.value) {
    t.daily_share += Number(d.daily_share) || 0
    t.capitals += Number(d.capitals) || 0
    t.expenses += Number(d.expenses) || 0
    t.payments += Number(d.payments) || 0
  }
  return t
})

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ date_from: caseeDateFrom.value, date_to: caseeDateTo.value })
    const res = await fetch(`/api/paylira-partners/${partnerId}?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      partner.value = data.partner
      currentCase.value = data.current_case
      dailyCases.value = data.daily_cases
    }
  } finally { loading.value = false }
}

const fetchFundStorages = async () => {
  const res = await fetch('/api/fund-storages', { headers })
  if (res.ok) {
    const data = await res.json()
    fundStorages.value = data.storages || []
  }
}

const fetchAllPayments = async () => {
  paymentsLoading.value = true
  try {
    const params = new URLSearchParams({ date_from: paymentDateFrom.value, date_to: paymentDateTo.value })
    const res = await fetch(`/api/paylira-partners/${partnerId}/payments?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      allPayments.value = data.payments
      allPaymentsTotal.value = data.total
    }
  } finally { paymentsLoading.value = false }
}

const fetchAllCapitals = async () => {
  capitalsLoading.value = true
  try {
    const params = new URLSearchParams({ date_from: capitalDateFrom.value, date_to: capitalDateTo.value })
    const res = await fetch(`/api/paylira-partners/${partnerId}/capitals?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      allCapitals.value = data.capitals
      allCapitalsTotal.value = data.total
    }
  } finally { capitalsLoading.value = false }
}

const fetchTeams = async () => {
  const res = await fetch('/api/teams?status=all', { headers })
  if (res.ok) {
    const data = await res.json()
    teamsList.value = Array.isArray(data) ? data : (data.teams || [])
  }
}

const fetchPartnersList = async () => {
  const res = await fetch('/api/paylira-partners', { headers })
  if (res.ok) {
    const data = await res.json()
    partnersList.value = data.filter(p => String(p.id) !== String(partnerId))
  }
}

const addTransfer = async () => {
  const res = await fetch(`/api/paylira-partners/${partnerId}/transfers`, { method: 'POST', headers, body: JSON.stringify(transferForm.value) })
  const data = await res.json()
  if (res.ok) {
    showTransferDialog.value = false
    transferForm.value = { to_partner_id: null, amount: 0, description: '', payment_date: today }
    snackbar.success(data.message)
    fetchData(); fetchAllPayments()
  } else {
    snackbar.handleError(data)
  }
}

const deleteTransferFromList = async (rawId) => {
  const id = String(rawId).replace(/^pt[oi]_/, '')
  if (!confirm('Bu transferi silmek istediğinize emin misiniz?')) return
  const res = await fetch(`/api/paylira-partners/${partnerId}/transfers/${id}`, { method: 'DELETE', headers })
  const data = await res.json()
  if (res.ok) { snackbar.success(data.message); fetchData(); fetchAllPayments() }
  else { snackbar.handleError(data) }
}

onMounted(() => {
  fetchData()
  fetchFundStorages()
  fetchTeams()
  fetchAllPayments()
  fetchAllCapitals()
  fetchPartnersList()
})

watch(() => [paymentForm.value.crypto_quantity, paymentForm.value.crypto_rate], ([qty, rate]) => {
  if (paymentForm.value.payment_type === 2 && qty && rate) {
    paymentForm.value.amount = parseFloat((qty * rate).toFixed(2))
  }
})

watch(() => [capitalForm.value.crypto_quantity, capitalForm.value.crypto_rate], ([qty, rate]) => {
  if (capitalForm.value.payment_type === 2 && qty && rate) {
    capitalForm.value.amount = parseFloat((qty * rate).toFixed(2))
  }
})

const addPayment = async () => {
  const res = await fetch(`/api/paylira-partners/${partnerId}/payments`, { method: 'POST', headers, body: JSON.stringify(paymentForm.value) })
  const data = await res.json()
  if (res.ok) {
    showPaymentDialog.value = false
    paymentForm.value = { payment_type: 1, amount: 0, crypto_quantity: null, crypto_rate: null, tx_link: '', fund_storage_id: null, team_id: null, is_capital: false, description: '', payment_date: today }
    snackbar.success(data.message)
    fetchData(); fetchAllPayments()
  } else {
    snackbar.handleError(data)
  }
}

const addCapital = async () => {
  const res = await fetch(`/api/paylira-partners/${partnerId}/capitals`, { method: 'POST', headers, body: JSON.stringify(capitalForm.value) })
  const data = await res.json()
  if (res.ok) {
    showCapitalDialog.value = false
    capitalForm.value = { payment_type: 1, amount: 0, crypto_quantity: null, crypto_rate: null, tx_link: '', fund_storage_id: null, description: '', payment_date: today }
    snackbar.success(data.message)
    fetchData(); fetchAllCapitals()
  } else {
    snackbar.handleError(data)
  }
}

const openDayDetail = async (day) => {
  selectedDay.value = day
  showDayDetailDialog.value = true
  dayPaymentsLoading.value = true
  try {
    const res = await fetch(`/api/paylira-partners/${partnerId}/payments?date=${day.date}`, { headers })
    if (res.ok) {
      const data = await res.json()
      dayPayments.value = data.payments
    }
  } finally { dayPaymentsLoading.value = false }
}

const deletePayment = async (paymentId) => {
  if (!confirm('Bu ödemeyi silmek istediğinize emin misiniz?')) return
  const res = await fetch(`/api/paylira-partners/${partnerId}/payments/${paymentId}`, { method: 'DELETE', headers })
  if (res.ok) { showDayDetailDialog.value = false; fetchData(); fetchAllPayments() }
  else { const data = await res.json(); snackbar.handleError(data) }
}

const deletePaymentFromList = async (paymentId) => {
  if (!confirm('Bu ödemeyi silmek istediğinize emin misiniz?')) return
  const res = await fetch(`/api/paylira-partners/${partnerId}/payments/${paymentId}`, { method: 'DELETE', headers })
  if (res.ok) { fetchData(); fetchAllPayments() }
  else { const data = await res.json(); snackbar.handleError(data) }
}

const deleteCapital = async (capitalId) => {
  if (!confirm('Bu sermayeyi silmek istediğinize emin misiniz?')) return
  const res = await fetch(`/api/paylira-partners/${partnerId}/capitals/${capitalId}`, { method: 'DELETE', headers })
  if (res.ok) { fetchData(); fetchAllCapitals() }
  else { const data = await res.json(); snackbar.handleError(data) }
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
            <VBtn icon variant="text" size="small" @click="router.push('/partner-net')">
              <VIcon icon="tabler-arrow-left" />
            </VBtn>
            <div>
              <h4 class="text-h4 font-weight-bold">{{ partner.name }}</h4>
              <span class="text-body-2 text-medium-emphasis">{{ t('partner.share') }}</span>
            </div>
          </div>
          <div class="text-center">
            <div class="text-body-2 text-medium-emphasis">{{ t('merchant_case.current_case') }}</div>
            <h5 class="text-h5 font-weight-bold" :class="`text-${netColor(currentCase)}`">{{ formatMoney(currentCase) }}</h5>
          </div>
          <div class="d-flex gap-2">
            <VBtn color="info" variant="tonal" @click="showTransferDialog = true">
              <VIcon start icon="tabler-arrows-exchange" />
              Partner Transferi
            </VBtn>
            <VBtn color="warning" variant="outlined" @click="showCapitalDialog = true">
              <VIcon start icon="tabler-wallet" />
              {{ t('partner.add_capital') }}
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
              <th class="text-end">{{ t('partner.daily_share') }}</th>
              <th class="text-end">{{ t('partner.add_capital') }}</th>
              <th class="text-end">{{ t('partner.expenses') }}</th>
              <th class="text-end">{{ t('merchant_case.total_payments') }}</th>
              <th class="text-end">{{ t('merchant_case.end_of_day') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="day in dailyCases" :key="day.date" class="cursor-pointer" :style="day.is_today ? 'background: rgba(var(--v-theme-primary), 0.08)' : ''" @click="openDayDetail(day)">
              <td class="font-weight-medium">
                {{ formatDate(day.date) }}
                <VChip v-if="day.is_today" color="primary" label size="x-small" class="ms-1">{{ t('dashboard.today') }}</VChip>
              </td>
              <td class="text-end">{{ formatMoney(day.previous_balance) }}</td>
              <td class="text-end text-success">{{ formatMoney(day.daily_share) }}</td>
              <td class="text-end text-warning">{{ day.capitals > 0 ? formatMoney(day.capitals) : '-' }}</td>
              <td class="text-end text-error">{{ day.expenses > 0 ? formatMoney(day.expenses) : '-' }}</td>
              <td class="text-end text-info">{{ day.payments > 0 ? formatMoney(day.payments) : '-' }}</td>
              <td class="text-end font-weight-bold" :class="`text-${netColor(day.amount)}`">{{ formatMoney(day.amount) }}</td>
            </tr>
            <tr v-if="!loading && dailyCases.length === 0">
              <td colspan="7" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
          <tfoot v-if="dailyCases.length > 0">
            <tr style="background: rgba(var(--v-theme-primary), 0.12); font-weight: 700;">
              <td>TOPLAM</td>
              <td></td>
              <td class="text-end" :class="`text-${netColor(dailyCaseTotals.daily_share)}`">{{ formatMoney(dailyCaseTotals.daily_share) }}</td>
              <td class="text-end text-warning">{{ dailyCaseTotals.capitals > 0 ? formatMoney(dailyCaseTotals.capitals) : '-' }}</td>
              <td class="text-end text-error">{{ dailyCaseTotals.expenses > 0 ? formatMoney(dailyCaseTotals.expenses) : '-' }}</td>
              <td class="text-end text-info">{{ dailyCaseTotals.payments > 0 ? formatMoney(dailyCaseTotals.payments) : '-' }}</td>
              <td></td>
            </tr>
          </tfoot>
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
              <th>Tür</th>
              <th>Kaynak</th>
              <th class="text-end">{{ t('merchant_case.tl_amount') }}</th>
              <th>İşlemi Yapan</th>
              <th>{{ t('merchant_case.description') }}</th>
              <th class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in allPayments" :key="p.id">
              <td class="text-body-2">{{ formatDateTime(p.created_at) }}</td>
              <td>
                <VChip v-if="p.is_expense" color="error" variant="tonal" label size="x-small">{{ brand }} Masraf</VChip>
                <VChip v-else-if="p.source === 'partner_transfer_out'" color="warning" variant="tonal" label size="x-small">Transfer → {{ p.to_partner_name }}</VChip>
                <VChip v-else-if="p.source === 'partner_transfer_in'" color="success" variant="tonal" label size="x-small">Transfer ← {{ p.from_partner_name }}</VChip>
                <VChip v-else-if="p.source === 'capital'" color="success" label size="x-small">Sermaye Girişi</VChip>
                <VChip v-else color="purple" variant="tonal" label size="x-small">
                  Sermaye Düşümü/{{ p.payment_type === 1 ? t('merchant_case.type_tl') : p.payment_type === 2 ? t('merchant_case.type_crypto') : t('deposits.team') }}
                </VChip>
              </td>
              <td>
                <span v-if="p.source_name" class="text-body-2 font-weight-medium">{{ p.source_name }}</span>
                <span v-else class="text-medium-emphasis">-</span>
              </td>
              <td class="text-end font-weight-medium" :class="p.is_expense ? 'text-error' : p.source === 'capital' || p.source === 'partner_transfer_in' ? 'text-success' : ''">
                {{ p.source === 'capital' || p.source === 'partner_transfer_in' ? '+' : '' }}{{ formatMoney(p.amount) }}
              </td>
              <td class="text-body-2">{{ p.created_by_name || '-' }}</td>
              <td class="text-body-2">
                {{ p.description || '-' }}
                <a v-if="p.tx_link" :href="p.tx_link" target="_blank" class="ms-1"><VIcon icon="tabler-external-link" size="14" /></a>
              </td>
              <td class="text-end">
                <VBtn v-if="!p.is_expense && !p.source?.startsWith('partner_transfer') && isPaymentToday(p.created_at)" icon size="x-small" variant="text" color="error" @click="deletePaymentFromList(p.id)">
                  <VIcon icon="tabler-trash" size="18" />
                </VBtn>
                <VBtn v-else-if="p.source === 'partner_transfer_out' && isPaymentToday(p.created_at)" icon size="x-small" variant="text" color="error" @click="deleteTransferFromList(p.id)">
                  <VIcon icon="tabler-trash" size="18" />
                </VBtn>
                <VBtn v-else-if="p.source === 'capital' && isPaymentToday(p.created_at)" icon size="x-small" variant="text" color="error" @click="deleteCapital(String(p.id).replace('cap_',''))">
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

  <!-- Ödeme Ekle -->
  <VDialog v-model="showPaymentDialog" max-width="500">
    <VCard :title="t('merchant_case.add_payment')">
      <VCardText>
        <VRadioGroup v-model="paymentForm.payment_type" inline class="mb-4">
          <VRadio :label="t('merchant_case.type_tl')" :value="1" />
          <VRadio :label="t('merchant_case.type_crypto')" :value="2" />
          <VRadio :label="t('deposits.team')" :value="3" />
        </VRadioGroup>

        <template v-if="paymentForm.payment_type === 2">
          <AppTextField
            v-model="paymentForm.tx_link"
            :label="t('merchant_case.tx_link')"
            placeholder="https://tronscan.org/#/transaction/..."
            class="mb-4"
            :loading="payTxLoading"
            @change="payLookupTx(paymentForm.tx_link)"
          />
          <VRow class="mb-2">
            <VCol cols="4"><AppTextField v-model="paymentForm.crypto_quantity" type="number" :label="t('merchant_case.crypto_quantity')" :loading="payTxLoading" /></VCol>
            <VCol cols="4"><AppTextField v-model="paymentForm.crypto_rate" type="number" :label="t('merchant_case.crypto_rate')" prefix="₺" /></VCol>
            <VCol cols="4"><AppTextField v-model="paymentForm.amount" type="number" :label="t('merchant_case.tl_amount')" prefix="₺" /></VCol>
          </VRow>
          <VSelect v-model="paymentForm.fund_storage_id" :items="fundStorages.map(f => ({ title: f.name, value: f.id }))" :label="t('fund_storage.name')" class="mb-4" />
        </template>

        <AppTextField v-if="paymentForm.payment_type === 1" v-model="paymentForm.amount" type="number" :label="t('merchant_case.tl_amount')" prefix="₺" class="mb-4" />

        <template v-if="paymentForm.payment_type === 3">
          <VAutocomplete v-model="paymentForm.team_id" :items="teamsList.map(item => ({ title: item.name, value: item.id }))" :label="t('deposits.team')" class="mb-4" />
          <AppTextField v-model="paymentForm.amount" type="number" :label="t('merchant_case.tl_amount')" prefix="₺" class="mb-4" />
        </template>

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

  <!-- Partner Transferi -->
  <VDialog v-model="showTransferDialog" max-width="450">
    <VCard title="Partner Transferi">
      <VCardText>
        <VSelect
          v-model="transferForm.to_partner_id"
          :items="partnersList.map(p => ({ title: p.name, value: p.id }))"
          label="Hedef Partner"
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

  <!-- Sermaye Ekle -->
  <VDialog v-model="showCapitalDialog" max-width="500">
    <VCard :title="t('partner.add_capital')">
      <VCardText>
        <VRadioGroup v-model="capitalForm.payment_type" inline class="mb-4">
          <VRadio :label="t('merchant_case.type_tl')" :value="1" />
          <VRadio :label="t('merchant_case.type_crypto')" :value="2" />
        </VRadioGroup>

        <template v-if="capitalForm.payment_type === 2">
          <AppTextField
            v-model="capitalForm.tx_link"
            :label="t('merchant_case.tx_link')"
            placeholder="https://tronscan.org/#/transaction/..."
            class="mb-4"
            :loading="capTxLoading"
            @change="capLookupTx(capitalForm.tx_link)"
          />
          <VRow class="mb-2">
            <VCol cols="4"><AppTextField v-model="capitalForm.crypto_quantity" type="number" :label="t('merchant_case.crypto_quantity')" :loading="capTxLoading" /></VCol>
            <VCol cols="4"><AppTextField v-model="capitalForm.crypto_rate" type="number" :label="t('merchant_case.crypto_rate')" prefix="₺" /></VCol>
            <VCol cols="4"><AppTextField v-model="capitalForm.amount" type="number" :label="t('merchant_case.tl_amount')" prefix="₺" /></VCol>
          </VRow>
        </template>

        <AppTextField v-if="capitalForm.payment_type === 1" v-model="capitalForm.amount" type="number" :label="t('merchant_case.tl_amount')" prefix="₺" class="mb-4" />

        <VSelect
          v-model="capitalForm.fund_storage_id"
          :items="fundStorages.map(f => ({ title: f.name, value: f.id }))"
          :label="t('fund_storage.name')"
          class="mb-4"
        />

        <AppTextField v-model="capitalForm.description" :label="t('merchant_case.description')" class="mb-4" />
        <AppDateTimePicker v-model="capitalForm.payment_date" :label="t('deposits.date')" :config="dateConfig" density="compact" />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showCapitalDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="warning" @click="addCapital">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Gün Detay -->
  <VDialog v-model="showDayDetailDialog" max-width="600">
    <VCard :title="`${partner.name} — ${selectedDay ? formatDate(selectedDay.date) : ''}`" :loading="dayPaymentsLoading">
      <VCardText>
        <div v-if="dayPayments.length > 0">
          <VTable density="compact" class="text-no-wrap">
            <thead>
              <tr>
                <th>{{ t('merchant_case.payment_type_label') }}</th>
                <th class="text-end">{{ t('merchant_case.tl_amount') }}</th>
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
