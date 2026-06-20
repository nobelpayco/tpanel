<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default', roles: [1, 4] } })

const { t, locale } = useI18n()

const loading = ref(false)
const data = ref({ report: [], totals: {} })
const summary = ref({
  merchant_cases: [], total_merchant_case: 0,
  intermediary_cases: [], total_intermediary: 0,
  paylira_net: 0,
  team_balances: [], total_team_balance: 0,
  fund_storages: [], total_fund_storage: 0,
})

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const dateFrom = ref(today)

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const netColor = val => val >= 0 ? 'success' : 'error'

const { headers } = useApi()

const localeMap = { tr: Turkish, en: english, ru: Russian }

const dateConfig = computed(() => ({
  dateFormat: 'Y-m-d',
  altInput: true,
  altFormat: 'd.m.Y',
  locale: localeMap[locale.value] || Turkish,
}))

const fetchAll = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ date_from: dateFrom.value, date_to: dateFrom.value })

    const [summaryRes, reportRes] = await Promise.all([
      fetch(`/api/case-report/summary?${params}`, { headers }),
      fetch(`/api/case-report?${params}`, { headers }),
    ])

    if (summaryRes.ok) summary.value = await summaryRes.json()
    if (reportRes.ok) data.value = await reportRes.json()
  } finally {
    loading.value = false
  }
}

onMounted(fetchAll)

// Fon Transferi
const showTransferDialog = ref(false)
const transferLoading = ref(false)
const transferForm = ref({ from_storage_id: null, to_storage_id: null, amount: 0, commission_rate: 0, description: '', transfer_date: today })

const fromStorage = computed(() => summary.value.fund_storages.find(f => f.id === transferForm.value.from_storage_id))
const toStorage = computed(() => summary.value.fund_storages.find(f => f.id === transferForm.value.to_storage_id))
const isExternalToInternal = computed(() => fromStorage.value?.type === 1 && toStorage.value?.type === 2)
const commissionAmount = computed(() => isExternalToInternal.value ? Math.round((parseFloat(transferForm.value.amount) || 0) * (parseFloat(transferForm.value.commission_rate) || 0) / 100 * 100) / 100 : 0)
const receivedAmount = computed(() => Math.round(((parseFloat(transferForm.value.amount) || 0) - commissionAmount.value) * 100) / 100)

const openTransferDialog = () => {
  transferForm.value = { from_storage_id: null, to_storage_id: null, amount: 0, commission_rate: 0, description: '', transfer_date: today }
  showTransferDialog.value = true
}

const submitTransfer = async () => {
  transferLoading.value = true
  try {
    const res = await fetch('/api/fund-transfers', {
      method: 'POST', headers,
      body: JSON.stringify(transferForm.value),
    })
    const data = await res.json()
    if (res.ok) {
      showTransferDialog.value = false
      snackbar.success(data.message)
      fetchAll()
    } else {
      snackbar.handleError(data)
    }
  } finally { transferLoading.value = false }
}

// Başlangıç bakiyesi
const showInitialDialog = ref(false)
const initialLoading = ref(false)
const initialDate = ref(today)
const initialEntities = ref({ merchants: [], teams: [], intermediaries: [], partners: [], paylira: [] })
const snackbar = useSnackbar()

const openInitialBalance = async () => {
  initialLoading.value = true
  showInitialDialog.value = true
  try {
    const res = await fetch('/api/initial-balance/entities', { headers })
    if (res.ok) initialEntities.value = await res.json()
  } finally { initialLoading.value = false }
}

const saveInitialBalance = async () => {
  const allEntities = [
    ...initialEntities.value.merchants,
    ...initialEntities.value.teams,
    ...initialEntities.value.intermediaries,
    ...initialEntities.value.partners,
    ...initialEntities.value.paylira,
  ]

  const res = await fetch('/api/initial-balance', {
    method: 'POST', headers: { ...headers, 'Content-Type': 'application/json' },
    body: JSON.stringify({ date: initialDate.value, entities: allEntities }),
  })
  const data = await res.json()
  if (res.ok) {
    snackbar.success(data.message)
    showInitialDialog.value = false
    fetchAll()
  } else {
    snackbar.handleError(data)
  }
}

const netStatus = computed(() => {
  return summary.value.total_team_balance
    + summary.value.total_fund_storage
    - summary.value.total_merchant_case
    - summary.value.total_intermediary
    - summary.value.paylira_net
})
</script>

<template>
  <VRow>
    <!-- Tarih filtresi -->
    <VCol cols="12">
      <VCard>
        <VCardText class="d-flex align-center gap-4 flex-wrap">
          <div style="min-width: 200px;">
            <AppDateTimePicker
              v-model="dateFrom"
              :label="t('deposits.date')"
              :config="dateConfig"
              density="compact"
            />
          </div>
          <VBtn
            color="primary"
            :loading="loading"
            @click="fetchAll"
          >
            {{ t('common.filter') }}
          </VBtn>
          <VSpacer />
          <VBtn
            color="warning"
            variant="outlined"
            size="small"
            @click="openInitialBalance"
          >
            <VIcon start icon="tabler-adjustments" />
            {{ t('case_report.initial_balance') }}
          </VBtn>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Özet kartları -->
    <VCol cols="12" sm="6" lg="3">
      <VCard :loading="loading" class="cursor-pointer" @click="$router.push('/merchant-cases')">
        <VCardText>
          <div class="d-flex align-center gap-2 mb-2">
            <VAvatar color="success" variant="tonal" rounded size="40">
              <VIcon icon="tabler-building-store" size="24" />
            </VAvatar>
            <span class="text-body-1">{{ t('case_report.merchant_cases') }}</span>
          </div>
          <h4 class="text-h4 font-weight-bold" :class="`text-${netColor(summary.total_merchant_case)}`">
            {{ formatMoney(summary.total_merchant_case) }}
          </h4>
        </VCardText>
      </VCard>
    </VCol>

    <VCol cols="12" sm="6" lg="3">
      <VCard :loading="loading" class="cursor-pointer" @click="$router.push('/intermediary-cases')">
        <VCardText>
          <div class="d-flex align-center gap-2 mb-2">
            <VAvatar color="info" variant="tonal" rounded size="40">
              <VIcon icon="tabler-users-minus" size="24" />
            </VAvatar>
            <span class="text-body-1">{{ t('case_report.intermediary_total') }}</span>
          </div>
          <h4 class="text-h4 font-weight-bold text-info">
            {{ formatMoney(summary.total_intermediary) }}
          </h4>
        </VCardText>
      </VCard>
    </VCol>

    <VCol cols="12" sm="6" lg="3">
      <VCard :loading="loading" class="cursor-pointer" @click="$router.push('/partner-net')">
        <VCardText>
          <div class="d-flex align-center gap-2 mb-2">
            <VAvatar color="primary" variant="tonal" rounded size="40">
              <VIcon icon="tabler-report-money" size="24" />
            </VAvatar>
            <span class="text-body-1">{{ t('case_report.paylira_net') }}</span>
          </div>
          <h4 class="text-h4 font-weight-bold" :class="`text-${netColor(summary.paylira_net)}`">
            {{ formatMoney(summary.paylira_net) }}
          </h4>
        </VCardText>
      </VCard>
    </VCol>

    <VCol cols="12" sm="6" lg="3">
      <VCard :loading="loading" class="cursor-pointer" @click="$router.push('/team-cases')">
        <VCardText>
          <div class="d-flex align-center gap-2 mb-2">
            <VAvatar color="warning" variant="tonal" rounded size="40">
              <VIcon icon="tabler-users-group" size="24" />
            </VAvatar>
            <span class="text-body-1">{{ t('case_report.team_balances') }}</span>
          </div>
          <h4 class="text-h4 font-weight-bold text-warning">
            {{ formatMoney(summary.total_team_balance) }}
          </h4>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Net Durum -->
    <VCol cols="12">
      <VCard :loading="loading" :color="netStatus === 0 ? 'success' : 'error'" variant="tonal">
        <VCardText class="d-flex align-center justify-space-between">
          <div class="d-flex align-center gap-3">
            <VAvatar :color="netStatus === 0 ? 'success' : 'error'" rounded size="44">
              <VIcon :icon="netStatus === 0 ? 'tabler-check' : 'tabler-alert-triangle'" size="28" color="white" />
            </VAvatar>
            <div>
              <h5 class="text-h5 font-weight-bold">{{ t('case_report.net_status') }}</h5>
              <span class="text-body-2">
                {{ netStatus === 0 ? t('case_report.status_ok') : t('case_report.status_error') }}
              </span>
            </div>
          </div>
          <VTooltip location="top">
            <template #activator="{ props }">
              <h4 v-bind="props" class="text-h4 font-weight-bold cursor-pointer">
                {{ formatMoney(netStatus) }}
              </h4>
            </template>
            <div class="pa-2 text-body-2" style="color: #000;">
              <div class="d-flex justify-space-between gap-4 mb-1">
                <span>{{ t('case_report.team_balances') }}</span>
                <span class="font-weight-bold">{{ formatMoney(summary.total_team_balance) }}</span>
              </div>
              <div class="d-flex justify-space-between gap-4 mb-1">
                <span>+ {{ t('case_report.fund_distribution') }}</span>
                <span class="font-weight-bold">{{ formatMoney(summary.total_fund_storage) }}</span>
              </div>
              <div class="d-flex justify-space-between gap-4 mb-1">
                <span>- {{ t('case_report.merchant_cases') }}</span>
                <span class="font-weight-bold">{{ formatMoney(summary.total_merchant_case) }}</span>
              </div>
              <div class="d-flex justify-space-between gap-4 mb-1">
                <span>- {{ t('case_report.intermediary_total') }}</span>
                <span class="font-weight-bold">{{ formatMoney(summary.total_intermediary) }}</span>
              </div>
              <div class="d-flex justify-space-between gap-4">
                <span>- {{ t('case_report.paylira_net') }}</span>
                <span class="font-weight-bold">{{ formatMoney(summary.paylira_net) }}</span>
              </div>
            </div>
          </VTooltip>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Fonların Dağılımı -->
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle class="d-flex align-center gap-2">
            {{ t('case_report.fund_distribution') }}
            <VChip color="primary" label size="small" class="ms-2">
              {{ formatMoney(summary.total_fund_storage) }}
            </VChip>
          </VCardTitle>
        </VCardItem>
        <VDivider />
        <VCardText>
          <VRow>
            <!-- Takımlarda -->
            <VCol cols="12" md="4">
              <div class="d-flex align-center gap-2 mb-3">
                <VAvatar color="warning" variant="tonal" rounded size="36">
                  <VIcon icon="tabler-users-group" size="20" />
                </VAvatar>
                <div>
                  <div class="text-body-2 text-medium-emphasis">{{ t('case_report.in_teams') }}</div>
                  <div class="text-h6 font-weight-bold">{{ formatMoney(summary.total_team_balance) }}</div>
                </div>
              </div>
              <div v-for="tm in summary.team_balances" :key="tm.name" class="d-flex justify-space-between py-1 px-2">
                <span class="text-body-2">{{ tm.name }}</span>
                <span class="text-body-2 font-weight-medium">{{ formatMoney(tm.value) }}</span>
              </div>
            </VCol>

            <!-- Fon Depoları -->
            <VCol cols="12" md="4">
              <div class="d-flex align-center justify-space-between mb-3">
                <div class="d-flex align-center gap-2">
                  <VAvatar color="info" variant="tonal" rounded size="36">
                    <VIcon icon="tabler-safe" size="20" />
                  </VAvatar>
                  <div>
                    <div class="text-body-2 text-medium-emphasis">{{ t('nav.fund_storages') }}</div>
                    <div class="text-h6 font-weight-bold">{{ formatMoney(summary.total_fund_storage) }}</div>
                  </div>
                </div>
                <VBtn icon size="small" variant="tonal" color="primary" @click="openTransferDialog">
                  <VIcon icon="tabler-arrows-exchange" size="20" />
                  <VTooltip activator="parent" location="top">Fon Transferi</VTooltip>
                </VBtn>
              </div>
              <div v-for="fs in summary.fund_storages" :key="fs.id" class="d-flex justify-space-between align-center py-1 px-2">
                <div class="d-flex align-center gap-2">
                  <VChip :color="fs.type === 1 ? 'warning' : 'info'" label size="x-small">
                    {{ fs.type === 1 ? t('fund_storage.external') : t('fund_storage.internal') }}
                  </VChip>
                  <a
                    class="text-primary text-body-2 cursor-pointer"
                    @click="$router.push(`/fund-storage/${fs.id}`)"
                  >{{ fs.name }}</a>
                  <a
                    v-if="fs.wallet_address"
                    :href="`https://tronscan.org/#/address/${fs.wallet_address}`"
                    target="_blank"
                    class="ms-1"
                  >
                    <VIcon icon="tabler-external-link" size="14" />
                  </a>
                  <VChip
                    v-if="fs.chain_balance !== null && fs.chain_balance !== undefined"
                    color="success"
                    label
                    size="x-small"
                    class="ms-1"
                  >{{ fs.chain_balance }} USDT</VChip>
                </div>
                <span class="text-body-2 font-weight-medium">{{ formatMoney(fs.balance) }}</span>
              </div>
              <div v-if="summary.fund_storages.length === 0" class="text-body-2 text-medium-emphasis px-2">-</div>
            </VCol>

            <!-- Özet -->
            <VCol cols="12" md="4">
              <div class="d-flex align-center gap-2 mb-3">
                <VAvatar color="primary" variant="tonal" rounded size="36">
                  <VIcon icon="tabler-report-analytics" size="20" />
                </VAvatar>
                <div>
                  <div class="text-body-2 text-medium-emphasis">{{ t('case_report.fund_summary') }}</div>
                </div>
              </div>
              <div class="d-flex justify-space-between py-1 px-2">
                <span class="text-body-2">{{ t('case_report.in_teams') }}</span>
                <span class="text-body-2 font-weight-medium text-warning">{{ formatMoney(summary.total_team_balance) }}</span>
              </div>
              <div class="d-flex justify-space-between py-1 px-2">
                <span class="text-body-2">{{ t('nav.fund_storages') }}</span>
                <span class="text-body-2 font-weight-medium text-info">{{ formatMoney(summary.total_fund_storage) }}</span>
              </div>
              <VDivider class="my-2" />
              <div class="d-flex justify-space-between py-1 px-2">
                <span class="text-body-1 font-weight-bold">{{ t('common.total') }}</span>
                <span class="text-body-1 font-weight-bold text-primary">{{ formatMoney(summary.total_team_balance + summary.total_fund_storage) }}</span>
              </div>
            </VCol>
          </VRow>
        </VCardText>
      </VCard>
    </VCol>

  </VRow>

  <!-- Başlangıç Bakiyesi Dialog -->
  <VDialog v-model="showInitialDialog" max-width="700" scrollable>
    <VCard :loading="initialLoading">
      <VCardItem>
        <VCardTitle>{{ t('case_report.initial_balance') }}</VCardTitle>
      </VCardItem>
      <VDivider />
      <VCardText style="max-height: 500px;">
        <div class="mb-4" style="max-width: 200px;">
          <AppDateTimePicker v-model="initialDate" :label="t('deposits.date')" :config="dateConfig" density="compact" />
        </div>

        <!-- Merchantlar -->
        <h6 class="text-h6 mb-2">{{ t('case_report.merchant_cases') }}</h6>
        <VRow dense class="mb-4">
          <VCol v-for="m in initialEntities.merchants" :key="m.id" cols="6" md="4">
            <AppTextField v-model.number="m.amount" :label="m.name" type="number" prefix="₺" density="compact" />
          </VCol>
        </VRow>

        <!-- Takımlar -->
        <h6 class="text-h6 mb-2">{{ t('case_report.team_balances') }}</h6>
        <VRow dense class="mb-4">
          <VCol v-for="tm in initialEntities.teams" :key="tm.id" cols="6" md="4">
            <AppTextField v-model.number="tm.amount" :label="tm.name" type="number" prefix="₺" density="compact" />
          </VCol>
        </VRow>

        <!-- Aracılar -->
        <h6 class="text-h6 mb-2">{{ t('case_report.intermediary_total') }}</h6>
        <VRow dense class="mb-4">
          <VCol v-for="i in initialEntities.intermediaries" :key="i.id" cols="6" md="4">
            <AppTextField v-model.number="i.amount" :label="i.name" type="number" prefix="₺" density="compact" />
          </VCol>
        </VRow>

        <!-- Paylira Net -->
        <h6 class="text-h6 mb-2">{{ t('case_report.paylira_net') }}</h6>
        <VRow dense class="mb-4">
          <VCol v-for="p in initialEntities.paylira" :key="'paylira'" cols="6" md="4">
            <AppTextField v-model.number="p.amount" :label="p.name" type="number" prefix="₺" density="compact" />
          </VCol>
        </VRow>

        <!-- Ortaklar -->
        <h6 class="text-h6 mb-2">{{ t('partner.share') }}</h6>
        <VRow dense>
          <VCol v-for="pt in initialEntities.partners" :key="pt.id" cols="6" md="4">
            <AppTextField v-model.number="pt.amount" :label="pt.name" type="number" prefix="₺" density="compact" />
          </VCol>
        </VRow>
      </VCardText>
      <VDivider />
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showInitialDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="warning" @click="saveInitialBalance">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Fon Transferi Dialog -->
  <VDialog v-model="showTransferDialog" max-width="500">
    <VCard title="Fon Transferi">
      <VCardText>
        <VSelect
          v-model="transferForm.from_storage_id"
          :items="summary.fund_storages.map(f => ({ title: f.name + ' (₺' + Number(f.balance).toLocaleString('tr-TR', { minimumFractionDigits: 2 }) + ')', value: f.id }))"
          label="Kaynak Depo"
          density="compact"
          class="mb-4"
        />
        <VSelect
          v-model="transferForm.to_storage_id"
          :items="summary.fund_storages.filter(f => f.id !== transferForm.from_storage_id).map(f => ({ title: f.name, value: f.id }))"
          label="Hedef Depo"
          density="compact"
          class="mb-4"
        />
        <AppTextField
          v-model="transferForm.amount"
          type="number"
          label="Transfer Tutarı"
          prefix="₺"
          class="mb-4"
        />
        <AppTextField
          v-if="isExternalToInternal"
          v-model="transferForm.commission_rate"
          type="number"
          label="Komisyon Oranı (%)"
          suffix="%"
          class="mb-4"
        />
        <VAlert
          v-if="isExternalToInternal && transferForm.amount > 0"
          type="info"
          variant="tonal"
          density="compact"
          class="mb-4"
        >
          Komisyon: {{ formatMoney(commissionAmount) }} | Hedefe ulaşacak: <strong>{{ formatMoney(receivedAmount) }}</strong>
        </VAlert>
        <AppTextField
          v-model="transferForm.description"
          label="Açıklama"
          class="mb-4"
        />
        <AppDateTimePicker
          v-model="transferForm.transfer_date"
          :label="t('deposits.date')"
          :config="dateConfig"
          density="compact"
        />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showTransferDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" :loading="transferLoading" @click="submitTransfer">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>

