<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'
import { useRouter } from 'vue-router'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default', roles: [1] } })

const { t, locale } = useI18n()
const router = useRouter()
const snackbar = useSnackbar()

const loading = ref(true)
const dailyNetData = ref([])
const partners = ref([])
const partnersLoading = ref(true)

const { headers } = useApi()

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const monthStart = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-01` })()
const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({ dateFormat: 'Y-m-d', altInput: true, altFormat: 'd.m.Y', locale: localeMap[locale.value] || Turkish }))

const caseeDateFrom = ref(monthStart)
const caseeDateTo = ref(today)

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const formatDate = val => new Date(val).toLocaleDateString('tr-TR')
const formatDateTime = val => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}
const netColor = val => val >= 0 ? 'success' : 'error'

// Expense dialog
const showExpenseDialog = ref(false)
const expenseLoading = ref(false)
const expenseForm = ref({ amount: 0, description: '', source_type: 'team', team_id: null, fund_storage_id: null, payment_date: today, shares: [] })
const teamsList = ref([])
const fundStorages = ref([])
const manualEdit = ref({}) // track which partner was manually edited

// Expense detail popup
const showExpenseDetailDialog = ref(false)
const showCapitalReductionDialog = ref(false)
const capitalReductionDate = ref('')
const capitalReductionLoading = ref(false)
const capitalReductions = ref([])
const expenseDetailLoading = ref(false)
const expenseDetailDate = ref('')
const expenseDetails = ref([])

const initExpenseShares = () => {
  expenseForm.value.shares = partners.value.map(p => ({
    partner_id: p.id,
    name: p.name,
    share_percent: p.share_percent,
    amount: 0,
  }))
  manualEdit.value = {}
}

const distributeExpense = () => {
  const total = parseFloat(expenseForm.value.amount) || 0
  const shares = expenseForm.value.shares

  // Find manually edited and non-edited partners
  const manualIds = Object.keys(manualEdit.value).map(Number)
  const manualTotal = shares.filter(s => manualIds.includes(s.partner_id)).reduce((sum, s) => sum + (parseFloat(s.amount) || 0), 0)
  const remaining = total - manualTotal
  const autoShares = shares.filter(s => !manualIds.includes(s.partner_id))
  const autoPercentTotal = autoShares.reduce((sum, s) => sum + s.share_percent, 0)

  autoShares.forEach(s => {
    s.amount = autoPercentTotal > 0 ? parseFloat((remaining * s.share_percent / autoPercentTotal).toFixed(2)) : 0
  })
}

const onShareManualEdit = (partnerId) => {
  const total = parseFloat(expenseForm.value.amount) || 0
  const shares = expenseForm.value.shares
  const edited = shares.find(s => s.partner_id === partnerId)

  // Girilen değer toplam tutarı geçemez
  if (edited && parseFloat(edited.amount) > total) {
    edited.amount = total
  }

  manualEdit.value[partnerId] = true

  const manualIds = Object.keys(manualEdit.value).map(Number)
  const manualTotal = shares.filter(s => manualIds.includes(s.partner_id)).reduce((sum, s) => sum + (parseFloat(s.amount) || 0), 0)

  // Manuel toplamı toplam tutarı geçerse düzelt
  if (manualTotal > total && edited) {
    edited.amount = parseFloat(Math.max(0, parseFloat(edited.amount) - (manualTotal - total)).toFixed(2))
  }

  const newManualTotal = shares.filter(s => manualIds.includes(s.partner_id)).reduce((sum, s) => sum + (parseFloat(s.amount) || 0), 0)
  const remaining = Math.max(0, total - newManualTotal)
  const autoShares = shares.filter(s => !manualIds.includes(s.partner_id))
  const autoPercentTotal = autoShares.reduce((sum, s) => sum + s.share_percent, 0)

  autoShares.forEach(s => {
    s.amount = autoPercentTotal > 0 ? parseFloat((remaining * s.share_percent / autoPercentTotal).toFixed(2)) : 0
  })
}

watch(() => expenseForm.value.useTeam, (val) => {
  if (!val) expenseForm.value.team_id = null
})

watch(() => expenseForm.value.amount, () => {
  manualEdit.value = {}
  distributeExpense()
})

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ date_from: caseeDateFrom.value, date_to: caseeDateTo.value })
    const res = await fetch(`/api/merchant-cases/paylira-daily-net?${params}`, { headers })
    if (res.ok) dailyNetData.value = await res.json()
  } finally { loading.value = false }
}

const fetchPartners = async () => {
  partnersLoading.value = true
  try {
    const res = await fetch('/api/paylira-partners', { headers })
    if (res.ok) partners.value = await res.json()
  } finally { partnersLoading.value = false }
}

const openExpenseDialog = async () => {
  if (partners.value.length === 0) await fetchPartners()
  expenseForm.value = { amount: 0, description: '', source_type: 'team', team_id: null, fund_storage_id: null, payment_date: today, shares: [] }
  initExpenseShares()
  showExpenseDialog.value = true
}

const submitExpense = async () => {
  expenseLoading.value = true
  try {
    const res = await fetch('/api/paylira-expenses', {
      method: 'POST', headers,
      body: JSON.stringify({
        amount: expenseForm.value.amount,
        description: expenseForm.value.description,
        team_id: expenseForm.value.source_type === 'team' ? expenseForm.value.team_id : null,
        fund_storage_id: expenseForm.value.source_type === 'storage' ? expenseForm.value.fund_storage_id : null,
        payment_date: expenseForm.value.payment_date,
        shares: expenseForm.value.shares.map(s => ({ partner_id: s.partner_id, amount: s.amount })),
      }),
    })
    const data = await res.json()
    if (res.ok) {
      showExpenseDialog.value = false
      snackbar.success(data.message)
      fetchData()
      fetchPartners()
    } else {
      snackbar.handleError(data)
    }
  } finally { expenseLoading.value = false }
}

const openExpenseDetail = async (date) => {
  expenseDetailDate.value = date
  showExpenseDetailDialog.value = true
  expenseDetailLoading.value = true
  try {
    const params = new URLSearchParams({ date_from: date, date_to: date })
    const res = await fetch(`/api/paylira-expenses?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      expenseDetails.value = data.expenses
    }
  } finally { expenseDetailLoading.value = false }
}

const deleteExpense = async (id) => {
  if (!confirm('Bu masrafı silmek istediğinize emin misiniz?')) return
  const res = await fetch(`/api/paylira-expenses/${id}`, { method: 'DELETE', headers })
  const data = await res.json()
  if (res.ok) {
    snackbar.success(data.message)
    showExpenseDetailDialog.value = false
    fetchData()
    fetchPartners()
  } else {
    snackbar.handleError(data)
  }
}

const isPaymentToday = (createdAt) => createdAt?.substring(0, 10) === today

const openCapitalReductionDetail = async (date) => {
  capitalReductionDate.value = date
  showCapitalReductionDialog.value = true
  capitalReductionLoading.value = true
  try {
    const params = new URLSearchParams({ date_from: date, date_to: date })
    const res = await fetch(`/api/paylira-partner-payments-all?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      capitalReductions.value = data.payments
    }
  } finally { capitalReductionLoading.value = false }
}

// Tüm unique merchant isimleri
const allMerchantNames = computed(() => {
  const names = new Set()
  dailyNetData.value.forEach(row => {
    row.merchants.forEach(m => names.add(m.name))
  })
  return [...names]
})

const getMerchantNet = (row, name) => {
  const found = row.merchants.find(m => m.name === name)
  return found ? found.net : null
}

const getMerchant = (row, name) => row.merchants.find(m => m.name === name) || null

const currentTotal = computed(() => {
  if (dailyNetData.value.length === 0) return 0
  return dailyNetData.value[0]?.cumulative ?? 0
})

const columnTotals = computed(() => {
  const totals = {
    previous_balance: 0,
    merchants: {},
    daily_total: 0,
    expenses: 0,
    partner_payments: 0,
    partner_capitals: 0,
  }
  for (const row of dailyNetData.value) {
    totals.previous_balance += Number(row.previous_balance) || 0
    totals.daily_total += Number(row.daily_total) || 0
    totals.expenses += Number(row.expenses) || 0
    totals.partner_payments += Number(row.partner_payments) || 0
    totals.partner_capitals += Number(row.partner_capitals) || 0
    for (const m of row.merchants) {
      totals.merchants[m.name] = (totals.merchants[m.name] || 0) + (Number(m.net) || 0)
    }
  }
  return totals
})

const fetchTeams = async () => {
  const res = await fetch('/api/teams?status=all', { headers })
  if (res.ok) {
    const data = await res.json()
    teamsList.value = Array.isArray(data) ? data : (data.teams || [])
  }
}

const fetchFundStorages = async () => {
  const res = await fetch('/api/fund-storages', { headers })
  if (res.ok) {
    const data = await res.json()
    fundStorages.value = data.storages || []
  }
}

onMounted(() => {
  fetchData()
  fetchPartners()
  fetchTeams()
  fetchFundStorages()
})
</script>

<template>
  <VRow>
    <!-- Ortaklar -->
    <VCol cols="12">
      <VCard :loading="partnersLoading">
        <VCardItem>
          <VCardTitle class="d-flex align-center gap-2">
            <VBtn icon variant="text" size="small" @click="router.push('/case-report')">
              <VIcon icon="tabler-arrow-left" />
            </VBtn>
            {{ t('case_report.paylira_net') }}
            <VChip :color="netColor(currentTotal)" label size="small" class="ms-2">
              {{ formatMoney(currentTotal) }}
            </VChip>
          </VCardTitle>
          <template #append>
            <VBtn color="error" variant="outlined" @click="openExpenseDialog">
              <VIcon start icon="tabler-minus" />
              {{ t('partner.add_expense') }}
            </VBtn>
          </template>
        </VCardItem>
        <VDivider />
        <VCardText>
          <VRow>
            <VCol
              v-for="p in partners"
              :key="p.id"
              cols="12"
              :sm="12 / partners.length"
            >
              <VCard
                variant="outlined"
                class="cursor-pointer"
                @click="router.push(`/partner/${p.id}`)"
              >
                <VCardText class="d-flex justify-space-between align-center">
                  <div>
                    <div class="text-body-2 text-medium-emphasis">{{ p.name }}</div>
                    <h5 class="text-h5 font-weight-bold" :class="`text-${netColor(p.current_case)}`">
                      {{ formatMoney(p.current_case) }}
                    </h5>
                  </div>
                  <VAvatar color="primary" variant="tonal" rounded size="40">
                    <VIcon icon="tabler-user" size="24" />
                  </VAvatar>
                </VCardText>
              </VCard>
            </VCol>
          </VRow>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Gün sonu tablosu -->
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle>{{ t('merchant_case.daily_cases') }}</VCardTitle>
        </VCardItem>
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
        <div style="max-height: 700px; overflow-y: auto;">
          <VTable class="text-no-wrap" density="compact">
            <thead>
              <tr>
                <th>{{ t('deposits.date') }}</th>
                <th class="text-end">{{ t('merchant_case.carryover') }}</th>
                <th v-for="name in allMerchantNames" :key="name" class="text-end">{{ name }}</th>
                <th class="text-end">{{ t('merchant_case.daily_net') }}</th>
                <th class="text-end">{{ t('partner.expenses') }}</th>
                <th class="text-end">Sermaye Düşümü</th>
                <th class="text-end">Sermaye Girişi</th>
                <th class="text-end">{{ t('merchant_case.cumulative') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in dailyNetData" :key="row.date" :style="row.is_today ? 'background: rgba(var(--v-theme-primary), 0.08)' : ''">
                <td class="font-weight-medium">
                  {{ formatDate(row.date) }}
                  <VChip v-if="row.is_today" color="primary" label size="x-small" class="ms-1">{{ t('dashboard.today') }}</VChip>
                </td>
                <td class="text-end">{{ formatMoney(row.previous_balance) }}</td>
                <td
                  v-for="name in allMerchantNames"
                  :key="name"
                  class="text-end"
                  :class="getMerchantNet(row, name) !== null ? `text-${netColor(getMerchantNet(row, name))}` : 'text-medium-emphasis'"
                >
                  <template v-if="getMerchantNet(row, name) !== null">
                    <VTooltip location="top">
                      <template #activator="{ props }">
                        <span v-bind="props" class="cursor-pointer">{{ formatMoney(getMerchantNet(row, name)) }}</span>
                      </template>
                      <div class="pa-1">
                        <div>Yatırım Karı: {{ formatMoney(getMerchant(row, name)?.deposit_profit ?? 0) }}</div>
                        <div>Çekim Karı: {{ formatMoney(getMerchant(row, name)?.withdraw_profit ?? 0) }}</div>
                        <div>Teslimat Karı: {{ formatMoney(getMerchant(row, name)?.delivery_profit ?? 0) }}</div>
                      </div>
                    </VTooltip>
                  </template>
                  <template v-else>-</template>
                </td>
                <td class="text-end font-weight-bold" :class="`text-${netColor(row.daily_total)}`">
                  {{ formatMoney(row.daily_total) }}
                </td>
                <td class="text-end">
                  <span
                    v-if="row.expenses > 0"
                    class="text-error font-weight-medium cursor-pointer"
                    @click="openExpenseDetail(row.date)"
                  >
                    {{ formatMoney(row.expenses) }}
                  </span>
                  <span v-else class="text-medium-emphasis">-</span>
                </td>
                <td class="text-end">
                  <span
                    v-if="row.partner_payments > 0"
                    class="text-error font-weight-medium cursor-pointer"
                    @click="openCapitalReductionDetail(row.date)"
                  >
                    {{ formatMoney(row.partner_payments) }}
                  </span>
                  <span v-else class="text-medium-emphasis">-</span>
                </td>
                <td class="text-end">
                  <span v-if="row.partner_capitals > 0" class="text-success font-weight-medium">
                    {{ formatMoney(row.partner_capitals) }}
                  </span>
                  <span v-else class="text-medium-emphasis">-</span>
                </td>
                <td class="text-end font-weight-bold" :class="`text-${netColor(row.cumulative)}`">
                  {{ formatMoney(row.cumulative) }}
                </td>
              </tr>
              <tr v-if="!loading && dailyNetData.length === 0">
                <td :colspan="allMerchantNames.length + 7" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
              </tr>
            </tbody>
            <tfoot v-if="dailyNetData.length > 0">
              <tr style="background: rgba(var(--v-theme-primary), 0.12); font-weight: 700;">
                <td>TOPLAM</td>
                <td></td>
                <td
                  v-for="name in allMerchantNames"
                  :key="name"
                  class="text-end"
                  :class="`text-${netColor(columnTotals.merchants[name] ?? 0)}`"
                >
                  {{ formatMoney(columnTotals.merchants[name] ?? 0) }}
                </td>
                <td class="text-end" :class="`text-${netColor(columnTotals.daily_total)}`">{{ formatMoney(columnTotals.daily_total) }}</td>
                <td class="text-end text-error">{{ columnTotals.expenses > 0 ? formatMoney(columnTotals.expenses) : '-' }}</td>
                <td class="text-end text-error">{{ columnTotals.partner_payments > 0 ? formatMoney(columnTotals.partner_payments) : '-' }}</td>
                <td class="text-end text-success">{{ columnTotals.partner_capitals > 0 ? formatMoney(columnTotals.partner_capitals) : '-' }}</td>
                <td></td>
              </tr>
            </tfoot>
          </VTable>
        </div>
      </VCard>
    </VCol>
  </VRow>

  <!-- Masraf Ekle Dialog -->
  <VDialog v-model="showExpenseDialog" max-width="550">
    <VCard :title="t('partner.add_expense')">
      <VCardText>
        <AppTextField
          v-model="expenseForm.amount"
          type="number"
          :label="t('partner.expense_amount')"
          prefix="₺"
          class="mb-4"
        />
        <AppTextField
          v-model="expenseForm.description"
          :label="t('merchant_case.description')"
          class="mb-4"
        />
        <VRadioGroup v-model="expenseForm.source_type" inline class="mb-2">
          <VRadio :label="t('deposits.team')" value="team" />
          <VRadio :label="t('fund_storage.name')" value="storage" />
        </VRadioGroup>
        <VAutocomplete
          v-if="expenseForm.source_type === 'team'"
          v-model="expenseForm.team_id"
          :items="teamsList.map(item => ({ title: item.name, value: item.id }))"
          :label="t('deposits.team')"
          density="compact"
          class="mb-4"
        />
        <VSelect
          v-if="expenseForm.source_type === 'storage'"
          v-model="expenseForm.fund_storage_id"
          :items="fundStorages.map(f => ({ title: f.name, value: f.id }))"
          :label="t('fund_storage.name')"
          density="compact"
          class="mb-4"
        />
        <AppDateTimePicker
          v-model="expenseForm.payment_date"
          :label="t('deposits.date')"
          :config="dateConfig"
          density="compact"
          class="mb-4"
        />

        <template v-if="Number(expenseForm.amount) > 0 && expenseForm.shares.length > 0">
          <VDivider class="mb-4" />
          <div class="text-body-2 text-medium-emphasis mb-3">{{ t('partner.partner_shares') }}</div>

          <VRow v-for="share in expenseForm.shares" :key="share.partner_id" class="mb-2" align="center">
            <VCol cols="4">
              <span class="text-body-2 font-weight-medium">{{ share.name }}</span>
            </VCol>
            <VCol cols="8">
              <AppTextField
                v-model="share.amount"
                type="number"
                prefix="₺"
                density="compact"
                @update:model-value="onShareManualEdit(share.partner_id)"
              />
            </VCol>
          </VRow>
        </template>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showExpenseDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="error" :loading="expenseLoading" @click="submitExpense">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Masraf Detay Popup -->
  <VDialog v-model="showExpenseDetailDialog" max-width="600">
    <VCard :title="`${t('partner.expenses')} — ${formatDate(expenseDetailDate)}`" :loading="expenseDetailLoading">
      <VCardText>
        <div v-if="expenseDetails.length > 0">
          <div v-for="exp in expenseDetails" :key="exp.id" class="mb-4 pa-3 rounded" style="border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));">
            <div class="d-flex justify-space-between align-center mb-2">
              <div>
                <span class="font-weight-bold text-error">{{ formatMoney(exp.amount) }}</span>
                <span v-if="exp.description" class="text-body-2 text-medium-emphasis ms-2">{{ exp.description }}</span>
              </div>
              <VBtn v-if="isPaymentToday(exp.created_at)" icon size="x-small" variant="text" color="error" @click="deleteExpense(exp.id)">
                <VIcon icon="tabler-trash" size="18" />
              </VBtn>
            </div>
            <VTable density="compact" class="text-no-wrap">
              <thead>
                <tr>
                  <th>Partner</th>
                  <th class="text-end">{{ t('deposits.amount') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="s in exp.shares" :key="s.id">
                  <td class="text-body-2">{{ s.partner_name }}</td>
                  <td class="text-end font-weight-medium text-error">{{ formatMoney(s.amount) }}</td>
                </tr>
              </tbody>
            </VTable>
          </div>
        </div>
        <div v-else-if="!expenseDetailLoading" class="text-center text-medium-emphasis py-4">
          {{ t('common.no_data') }}
        </div>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showExpenseDetailDialog = false">{{ t('common.cancel') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Sermaye Düşümü Detay Popup -->
  <VDialog v-model="showCapitalReductionDialog" max-width="700">
    <VCard :title="`Sermaye Düşümleri — ${formatDate(capitalReductionDate)}`" :loading="capitalReductionLoading">
      <VCardText>
        <VTable v-if="capitalReductions.length > 0" density="compact" class="text-no-wrap">
          <thead>
            <tr>
              <th>Partner</th>
              <th>Tür</th>
              <th>Kaynak</th>
              <th class="text-end">Tutar</th>
              <th>Açıklama</th>
              <th>İşlemi Yapan</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in capitalReductions" :key="p.id">
              <td class="font-weight-medium">{{ p.partner_name }}</td>
              <td>
                <VChip :color="p.payment_type === 1 ? 'success' : p.payment_type === 2 ? 'warning' : 'info'" label size="x-small">
                  {{ p.payment_type === 1 ? 'TL' : p.payment_type === 2 ? 'Kripto' : 'Takım' }}
                </VChip>
              </td>
              <td class="text-body-2">{{ p.fund_storage_name || p.team_name || '-' }}</td>
              <td class="text-end font-weight-medium text-error">{{ formatMoney(p.amount) }}</td>
              <td class="text-body-2 text-medium-emphasis">{{ p.description || '-' }}</td>
              <td class="text-body-2">{{ p.created_by_name || '-' }}</td>
            </tr>
          </tbody>
        </VTable>
        <div v-else-if="!capitalReductionLoading" class="text-center text-medium-emphasis py-4">
          {{ t('common.no_data') }}
        </div>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showCapitalReductionDialog = false">{{ t('common.cancel') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
