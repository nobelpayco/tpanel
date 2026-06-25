<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'
import { useTronLookup } from '@/composables/useTronLookup'
import { useRoute, useRouter } from 'vue-router'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default', roles: [1, 3, 4] } })

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const merchantId = route.params.id
const isGroup = route.query.type === 'group'

const isMerchant = computed(() => {
  const user = JSON.parse(localStorage.getItem('user') || '{}')
  return user.user_type == 3
})

const loading = ref(true)
const merchant = ref({})
const currentCase = ref(0)
const dailyCases = ref([])
const tabs = ref([])
const fundStorages = ref([])
const activeTab = ref(0)

// Main page tab: 0 = gün sonu kasaları, 1 = ödemeler
const pageTab = ref(0)

// Dialogs
const showPaymentDialog = ref(false)
const showDayDetailDialog = ref(false)
const selectedDay = ref(null)
const dayPayments = ref([])
const dayPaymentsLoading = ref(false)

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const monthStart = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-01` })()

const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({
  dateFormat: 'Y-m-d',
  altInput: true,
  altFormat: 'd.m.Y',
  locale: localeMap[locale.value] || Turkish,
}))

// Payment form
const paymentForm = ref({
  payment_type: 1,
  amount: 0,
  crypto_quantity: null,
  crypto_rate: null,
  paid_amount: 0,
  tx_link: '',
  fund_storage_id: null,
  description: '',
  payment_date: today,
  apply_commission: true,
})

// Gün sonu kasaları tarih filtresi
const caseeDateFrom = ref(monthStart)
const caseeDateTo = ref(today)

// Ödemeler tab state
const paymentsLoading = ref(false)
const allPayments = ref([])
const allPaymentsTotal = ref(0)
const paymentDateFrom = ref(monthStart)
const paymentDateTo = ref(today)

const { headers } = useApi()
const snackbar = useSnackbar()
const { txLoading, lookupTx } = useTronLookup(paymentForm)

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const formatDate = val => {
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR')
}
const formatDateTime = val => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}

const fetchFundStorages = async () => {
  // Merchant kullanıcı ödeme ekleyemediği için fon depolarına ihtiyacı yok
  if (isMerchant.value) return
  const res = await fetch('/api/fund-storages', { headers })
  if (res.ok) {
    const data = await res.json()
    fundStorages.value = data.storages || []
  }
}

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ date_from: caseeDateFrom.value, date_to: caseeDateTo.value })
    if (isGroup) params.append('type', 'group')
    const res = await fetch(`/api/merchant-cases/${merchantId}?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      merchant.value = data.merchant
      currentCase.value = data.current_case
      dailyCases.value = data.daily_cases
      tabs.value = data.tabs || []
    }
  } finally {
    loading.value = false
  }
}

const fetchAllPayments = async () => {
  paymentsLoading.value = true
  try {
    const params = new URLSearchParams({ date_from: paymentDateFrom.value, date_to: paymentDateTo.value })
    if (isGroup) params.append('type', 'group')
    const res = await fetch(`/api/merchant-cases/${merchantId}/payments?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      allPayments.value = data.payments
      allPaymentsTotal.value = data.total
    }
  } finally {
    paymentsLoading.value = false
  }
}

onMounted(() => {
  fetchData()
  fetchAllPayments()
  fetchFundStorages()
})

// Teslimat Karı = TL Tutar × merchant.deliveryCommission%
const deliveryProfit = computed(() => {
  const amt = parseFloat(paymentForm.value.amount) || 0
  const rate = parseFloat(merchant.value?.deliveryCommission) || 0
  return parseFloat((amt * rate / 100).toFixed(2))
})

const addPayment = async () => {
  const body = {
    ...paymentForm.value,
    is_group: isGroup,
    target_merchant_id: isGroup && activeTab.value > 0 ? tabs.value[activeTab.value - 1]?.id : null,
  }
  const res = await fetch(`/api/merchant-cases/${merchantId}/payments`, {
    method: 'POST', headers,
    body: JSON.stringify(body),
  })
  const data = await res.json()
  if (res.ok) {
    showPaymentDialog.value = false
    paymentForm.value = { payment_type: 1, amount: 0, crypto_quantity: null, crypto_rate: null, paid_amount: 0, tx_link: '', fund_storage_id: null, description: '', payment_date: today, apply_commission: true }
    snackbar.success(data.message)
    fetchData()
    fetchAllPayments()
  } else {
    snackbar.handleError(data)
  }
}

const openDayDetail = async (day) => {
  selectedDay.value = day
  showDayDetailDialog.value = true
  dayPaymentsLoading.value = true
  try {
    const res = await fetch(`/api/merchant-cases/${merchantId}/payments?date=${day.date}`, { headers })
    if (res.ok) {
      const data = await res.json()
      dayPayments.value = data.payments
    }
  } finally {
    dayPaymentsLoading.value = false
  }
}

const netColor = val => val >= 0 ? 'success' : 'error'

const deletePayment = async (paymentId) => {
  if (!confirm('Bu ödemeyi silmek istediğinize emin misiniz?')) return
  const res = await fetch(`/api/merchant-cases/${merchantId}/payments/${paymentId}`, { method: 'DELETE', headers })
  if (res.ok) {
    showDayDetailDialog.value = false
    fetchData()
    fetchAllPayments()
  } else {
    const data = await res.json()
    snackbar.handleError(data)
  }
}

const deletePaymentFromList = async (paymentId) => {
  if (!confirm('Bu ödemeyi silmek istediğinize emin misiniz?')) return
  const res = await fetch(`/api/merchant-cases/${merchantId}/payments/${paymentId}`, { method: 'DELETE', headers })
  if (res.ok) {
    fetchData()
    fetchAllPayments()
  } else {
    const data = await res.json()
    snackbar.handleError(data)
  }
}

const calcTotalCommission = (day) => {
  return (day.deposit_commission_amount || 0) + (day.withdraw_commission_amount || 0) + (day.payment_commissions || 0)
}

const isPaymentToday = (createdAt) => {
  if (!createdAt) return false
  return createdAt.substring(0, 10) === today
}
</script>

<template>
  <VRow>
    <!-- Üst özet -->
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardText class="d-flex align-center justify-space-between flex-wrap gap-4">
          <div class="d-flex align-center gap-3">
            <VBtn icon variant="text" size="small" @click="router.push('/case-report')">
              <VIcon icon="tabler-arrow-left" />
            </VBtn>
            <div>
              <h4 class="text-h4 font-weight-bold">{{ merchant.name }}</h4>
              <span class="text-body-2 text-medium-emphasis">
                {{ t('merchant_case.commission_rate') }}: %{{ merchant.commission }} ({{ t('deposits.title') }}) / %{{ merchant.withdrawCommission }} ({{ t('withdrawals.title') }}) / %{{ merchant.deliveryCommission }} ({{ t('merchant_case.delivery') }})
              </span>
            </div>
          </div>
          <div class="d-flex gap-6">
            <div class="text-center">
              <div class="text-body-2 text-medium-emphasis">{{ t('merchant_case.current_case') }}</div>
              <h5 class="text-h5 font-weight-bold" :class="`text-${netColor(currentCase)}`">{{ formatMoney(currentCase) }}</h5>
            </div>
          </div>
          <VBtn v-if="!isMerchant" color="primary" @click="showPaymentDialog = true">
            <VIcon start icon="tabler-plus" />
            {{ t('merchant_case.add_payment') }}
          </VBtn>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Grup Tab'ları -->
    <VCol v-if="tabs.length > 0" cols="12">
      <VCard>
        <VTabs v-model="activeTab">
          <VTab>{{ t('common.total') }}</VTab>
          <VTab v-for="tab in tabs" :key="tab.id">{{ tab.name }}</VTab>
        </VTabs>
        <VDivider />
        <VCardText v-if="activeTab > 0">
          <div class="d-flex gap-6">
            <div>
              <span class="text-body-2 text-medium-emphasis">{{ t('merchants.commission') }}</span>
              <div class="font-weight-medium">%{{ tabs[activeTab - 1].commission }}</div>
            </div>
            <div>
              <span class="text-body-2 text-medium-emphasis">{{ t('merchants.withdraw_commission') }}</span>
              <div class="font-weight-medium">%{{ tabs[activeTab - 1].withdrawCommission }}</div>
            </div>
            <div>
              <span class="text-body-2 text-medium-emphasis">{{ t('merchant_case.delivery_commission') }}</span>
              <div class="font-weight-medium">%{{ tabs[activeTab - 1].deliveryCommission }}</div>
            </div>
          </div>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Ana içerik tabları -->
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
              <th class="text-end">{{ t('merchant_case.total_payments') }}</th>
              <th class="text-end">{{ t('merchant_case.total_commission') }}</th>
              <th class="text-end">{{ t('merchant_case.end_of_day') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="day in dailyCases"
              :key="day.date"
              class="cursor-pointer"
              :style="day.is_today ? 'background: rgba(var(--v-theme-primary), 0.08)' : ''"
              @click="openDayDetail(day)"
            >
              <td class="font-weight-medium">
                {{ formatDate(day.date) }}
                <VChip v-if="day.is_today" color="primary" label size="x-small" class="ms-1">{{ t('dashboard.today') }}</VChip>
              </td>
              <td class="text-end">{{ formatMoney(day.previous_balance) }}</td>
              <td class="text-end text-success">{{ formatMoney(day.deposits) }}</td>
              <td class="text-end text-error">{{ formatMoney(day.withdrawals) }}</td>
              <td class="text-end text-info">{{ day.payments > 0 ? formatMoney(day.payments) : '-' }}</td>
              <td class="text-end text-warning">
                <VTooltip location="top">
                  <template #activator="{ props }">
                    <span v-bind="props" class="cursor-pointer">
                      {{ formatMoney(calcTotalCommission(day)) }}
                    </span>
                  </template>
                  <div class="pa-1">
                    <div v-if="day.deposit_commission_amount > 0">{{ t('deposits.title') }}: {{ formatMoney(day.deposit_commission_amount) }}</div>
                    <div v-if="day.withdraw_commission_amount > 0">{{ t('withdrawals.title') }}: {{ formatMoney(day.withdraw_commission_amount) }}</div>
                    <div v-if="day.payment_commissions > 0">{{ t('merchant_case.delivery') }}: {{ formatMoney(day.payment_commissions) }}</div>
                  </div>
                </VTooltip>
              </td>
              <td class="text-end font-weight-bold" :class="`text-${netColor(day.amount)}`">
                {{ formatMoney(day.amount) }}
              </td>
            </tr>
            <tr v-if="!loading && dailyCases.length === 0">
              <td colspan="7" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
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
            <AppDateTimePicker
              v-model="paymentDateFrom"
              :label="t('common.start_date')"
              :config="dateConfig"
              density="compact"
            />
          </div>
          <div style="min-width: 160px;">
            <AppDateTimePicker
              v-model="paymentDateTo"
              :label="t('common.end_date')"
              :config="dateConfig"
              density="compact"
            />
          </div>
          <VBtn color="primary" @click="fetchAllPayments">
            {{ t('common.filter') }}
          </VBtn>
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
              <th v-if="isGroup">{{ t('deposits.merchant') }}</th>
              <th>{{ t('merchant_case.payment_type_label') }}</th>
              <th class="text-end">{{ t('merchant_case.tl_amount') }}</th>
              <th class="text-end">{{ t('merchant_case.delivery_commission') }}</th>
              <th>{{ t('merchant_case.description') }}</th>
              <th class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in allPayments" :key="p.id">
              <td class="text-body-2">{{ formatDateTime(p.created_at) }}</td>
              <td v-if="isGroup" class="text-body-2">{{ p.merchant_name }}</td>
              <td>
                <VChip :color="p.payment_type === 1 ? 'success' : 'warning'" label size="x-small">
                  {{ p.payment_type === 1 ? t('merchant_case.type_tl') : t('merchant_case.type_crypto') }}
                </VChip>
              </td>
              <td class="text-end font-weight-medium">{{ formatMoney(p.amount) }}</td>
              <td class="text-end text-medium-emphasis">
                <span v-if="p.delivery_commission_amount > 0">
                  {{ formatMoney(p.delivery_commission_amount) }}
                  <span class="text-caption">(%{{ p.delivery_commission_rate }})</span>
                </span>
                <span v-else>-</span>
              </td>
              <td class="text-body-2">
                {{ p.description || '-' }}
                <a v-if="p.tx_link" :href="p.tx_link" target="_blank" class="ms-1">
                  <VIcon icon="tabler-external-link" size="14" />
                </a>
              </td>
              <td class="text-end">
                <VBtn v-if="!isMerchant && isPaymentToday(p.created_at)" icon size="x-small" variant="text" color="error" @click="deletePaymentFromList(p.id)">
                  <VIcon icon="tabler-trash" size="18" />
                </VBtn>
              </td>
            </tr>
            <tr v-if="!paymentsLoading && allPayments.length === 0">
              <td :colspan="isGroup ? 7 : 6" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>
  </VRow>

  <!-- Ödeme Ekle Dialog -->
  <VDialog v-model="showPaymentDialog" max-width="500">
    <VCard :title="t('merchant_case.add_payment')">
      <VCardText>
        <VRadioGroup v-model="paymentForm.payment_type" inline class="mb-4">
          <VRadio :label="t('merchant_case.type_tl')" :value="1" />
          <VRadio :label="t('merchant_case.type_crypto')" :value="2" />
        </VRadioGroup>

        <!-- Kripto alanları -->
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
            <VCol cols="6">
              <AppTextField
                v-model="paymentForm.crypto_quantity"
                type="number"
                :label="t('merchant_case.crypto_quantity')"
                :loading="txLoading"
              />
            </VCol>
            <VCol cols="6">
              <AppTextField
                v-model="paymentForm.crypto_rate"
                type="number"
                :label="t('merchant_case.crypto_rate')"
                prefix="₺"
              />
            </VCol>
            <VCol cols="6">
              <AppTextField
                v-model="paymentForm.amount"
                type="number"
                :label="t('merchant_case.tl_amount')"
                prefix="₺"
              />
            </VCol>
            <VCol cols="6">
              <AppTextField
                :model-value="deliveryProfit"
                type="number"
                label="Teslimat Karı"
                prefix="₺"
                readonly
                :class="deliveryProfit >= 0 ? 'text-success' : 'text-error'"
              />
            </VCol>
          </VRow>
          <VSelect
            v-model="paymentForm.fund_storage_id"
            :items="fundStorages.map(f => ({ title: f.name, value: f.id }))"
            :label="t('fund_storage.name')"
            class="mb-4"
          />
        </template>

        <!-- TL tutarı (sadece TL seçiliyse) -->
        <AppTextField
          v-if="paymentForm.payment_type === 1"
          v-model="paymentForm.amount"
          type="number"
          :label="t('merchant_case.tl_amount')"
          prefix="₺"
          class="mb-4"
        />

        <AppTextField
          v-model="paymentForm.description"
          :label="t('merchant_case.description')"
          class="mb-4"
        />

        <AppDateTimePicker
          v-model="paymentForm.payment_date"
          :label="t('deposits.date')"
          :config="dateConfig"
          density="compact"
        />

        <!-- TL'de ödeme komisyonu aç/kapa (sitenin komisyonu varsa) -->
        <VCheckbox
          v-if="paymentForm.payment_type === 1 && merchant.deliveryCommission > 0"
          v-model="paymentForm.apply_commission"
          :label="t('merchant_case.apply_commission')"
          density="compact"
          hide-details
          class="mt-3"
        />

        <VAlert
          v-if="merchant.deliveryCommission > 0 && paymentForm.amount > 0 && (paymentForm.payment_type !== 1 || paymentForm.apply_commission)"
          type="info"
          variant="tonal"
          density="compact"
          class="mt-4"
        >
          {{ t('merchant_case.delivery_commission') }}: %{{ merchant.deliveryCommission }} = {{ formatMoney(paymentForm.amount * merchant.deliveryCommission / 100) }}
        </VAlert>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showPaymentDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="addPayment">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Gün Detay Dialog -->
  <VDialog v-model="showDayDetailDialog" max-width="600">
    <VCard :title="`${merchant.name} — ${selectedDay ? formatDate(selectedDay.date) : ''}`" :loading="dayPaymentsLoading">
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
                  <a v-if="p.tx_link" :href="p.tx_link" target="_blank" class="ms-1">
                    <VIcon icon="tabler-external-link" size="14" />
                  </a>
                </td>
                <td class="text-body-2 text-medium-emphasis">{{ new Date(p.created_at).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }) }}</td>
                <td v-if="selectedDay?.is_today && !isMerchant" class="text-end">
                  <VBtn icon size="x-small" variant="text" color="error" @click="deletePayment(p.id)">
                    <VIcon icon="tabler-trash" size="18" />
                  </VBtn>
                </td>
              </tr>
            </tbody>
          </VTable>
        </div>
        <div v-else-if="!dayPaymentsLoading" class="text-center text-medium-emphasis py-4">
          {{ t('merchant_case.no_payments') }}
        </div>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showDayDetailDialog = false">{{ t('common.cancel') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
