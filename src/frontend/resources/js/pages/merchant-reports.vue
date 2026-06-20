<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useTheme } from 'vuetify'
import { hexToRgb } from '@layouts/utils'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', roles: [1] } })

const { t, locale } = useI18n()
const vuetifyTheme = useTheme()
const { headers } = useApi()
const snackbar = useSnackbar()

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const monthStart = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-01` })()
const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({ dateFormat: 'Y-m-d', altInput: true, altFormat: 'd.m.Y', locale: localeMap[locale.value] || Turkish }))

const dateFrom = ref(monthStart)
const dateTo = ref(today)
const activeTab = ref(0)
const merchantId = ref(null)
const merchants = ref([])

const tabData = ref({})
const tabLoading = ref({})
const tabLoaded = ref({})

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 0 })

const tabs = [
  { key: 'volume', endpoint: '/api/merchant-reports/volume-performance', title: 'merchant_report.volume_performance', icon: 'tabler-chart-line' },
  { key: 'player', endpoint: '/api/merchant-reports/player-analysis', title: 'merchant_report.player_analysis', icon: 'tabler-users' },
  { key: 'amount', endpoint: '/api/merchant-reports/amount-analysis', title: 'merchant_report.amount_analysis', icon: 'tabler-coins' },
  { key: 'financial', endpoint: '/api/merchant-reports/financial', title: 'merchant_report.financial', icon: 'tabler-report-money' },
  { key: 'risk', endpoint: '/api/merchant-reports/risk', title: 'merchant_report.risk', icon: 'tabler-alert-triangle' },
]

// Export dialog
const showExportDialog = ref(false)
const exportLoading = ref(false)
const exportForm = ref({
  date_from: monthStart,
  date_to: today,
  type: 'all',
  merchant_id: null,
  status: 'all',
})

const typeOptions = computed(() => [
  { title: t('common.all'), value: 'all' },
  { title: t('dashboard.total_deposits'), value: '1' },
  { title: t('dashboard.total_withdrawals'), value: '2' },
])

const statusOptions = computed(() => [
  { title: t('common.all'), value: 'all' },
  { title: t('status.pending'), value: '1' },
  { title: t('status.processing'), value: '2' },
  { title: t('status.approved'), value: '3' },
  { title: t('status.rejected'), value: '4' },
])

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
      snackbar.success(t('export.queued'))
    } else {
      snackbar.handleError(data)
    }
  } finally {
    exportLoading.value = false
  }
}

const fetchMerchants = async () => {
  const res = await fetch('/api/merchant-reports/filter-options', { headers })
  if (res.ok) {
    merchants.value = await res.json()
  }
}

const fetchTab = async (tabIndex) => {
  const tab = tabs[tabIndex]
  tabLoading.value[tab.key] = true
  try {
    const params = new URLSearchParams({ date_from: dateFrom.value, date_to: dateTo.value })
    if (merchantId.value) params.append('merchant_id', merchantId.value)
    const res = await fetch(`${tab.endpoint}?${params}`, { headers })
    if (res.ok) {
      tabData.value[tab.key] = await res.json()
      tabLoaded.value[tab.key] = true
    }
  } finally {
    tabLoading.value[tab.key] = false
  }
}

const applyFilter = () => {
  tabLoaded.value = {}
  fetchTab(activeTab.value)
}

watch(activeTab, (val) => {
  if (!tabLoaded.value[tabs[val].key]) fetchTab(val)
})

onMounted(() => { fetchMerchants(); fetchTab(0) })

// Chart helpers
const themeColors = computed(() => vuetifyTheme.current.value.colors)
const themeVars = computed(() => vuetifyTheme.current.value.variables)
const labelColor = computed(() => `rgba(${hexToRgb(themeColors.value['on-surface'])},${themeVars.value['disabled-opacity']})`)
const borderColor = computed(() => `rgba(${hexToRgb(String(themeVars.value['border-color']))},${themeVars.value['border-opacity']})`)
const isDark = computed(() => vuetifyTheme.global.name.value === 'dark')

const baseAreaOpts = (cats) => ({
  chart: { type: 'area', toolbar: { show: false }, parentHeightOffset: 0 },
  stroke: { width: 2, curve: 'smooth' },
  fill: { type: 'gradient', gradient: { shadeIntensity: 0.8, opacityFrom: 0.4, opacityTo: 0.1 } },
  dataLabels: { enabled: false },
  grid: { borderColor: borderColor.value },
  xaxis: { categories: cats, labels: { style: { colors: labelColor.value, fontSize: '11px', fontFamily: 'Public Sans' }, rotate: -45, rotateAlways: cats.length > 15 } },
  yaxis: { labels: { style: { colors: labelColor.value, fontSize: '11px', fontFamily: 'Public Sans' } } },
  legend: { position: 'top', labels: { colors: labelColor.value }, fontFamily: 'Public Sans' },
  tooltip: { theme: isDark.value ? 'dark' : 'light' },
})

const baseBarOpts = (cats) => ({
  chart: { type: 'bar', toolbar: { show: false } },
  plotOptions: { bar: { columnWidth: '55%', borderRadius: 4 } },
  dataLabels: { enabled: false },
  grid: { borderColor: borderColor.value },
  xaxis: { categories: cats, labels: { style: { colors: labelColor.value, fontSize: '11px', fontFamily: 'Public Sans' }, rotate: -45, rotateAlways: cats.length > 15 } },
  yaxis: { labels: { style: { colors: labelColor.value, fontSize: '11px', fontFamily: 'Public Sans' } } },
  legend: { position: 'top', labels: { colors: labelColor.value }, fontFamily: 'Public Sans' },
  tooltip: { theme: isDark.value ? 'dark' : 'light' },
})

const donutOpts = (labels) => ({
  chart: { type: 'donut' },
  labels,
  legend: { position: 'bottom', fontFamily: 'Public Sans', labels: { colors: labelColor.value } },
  dataLabels: { enabled: true, formatter: (val) => Math.round(val) + '%' },
  plotOptions: { pie: { donut: { size: '60%' } } },
  tooltip: { theme: isDark.value ? 'dark' : 'light' },
})

const v = computed(() => tabData.value.volume || {})
const p = computed(() => tabData.value.player || {})
const a = computed(() => tabData.value.amount || {})
const f = computed(() => tabData.value.financial || {})
const r = computed(() => tabData.value.risk || {})
</script>

<template>
  <VRow>
    <!-- Filtre -->
    <VCol cols="12">
      <VCard>
        <VCardText class="d-flex align-center gap-4 flex-wrap">
          <div style="min-width: 160px;">
            <AppDateTimePicker v-model="dateFrom" :label="t('common.start_date')" :config="dateConfig" density="compact" />
          </div>
          <div style="min-width: 160px;">
            <AppDateTimePicker v-model="dateTo" :label="t('common.end_date')" :config="dateConfig" density="compact" />
          </div>
          <VAutocomplete
            v-model="merchantId"
            :items="[{ title: t('merchant_report.all_merchants'), value: null }, ...merchants.map(m => ({ title: m.name, value: m.id }))]"
            :label="t('deposits.merchant')"
            density="compact"
            style="min-width: 180px;"
            :no-data-text="t('common.no_data')"
            clearable
          />
          <VBtn color="primary" @click="applyFilter">{{ t('common.filter') }}</VBtn>
          <VBtn color="success" variant="outlined" @click="showExportDialog = true">
            <VIcon start icon="tabler-file-spreadsheet" />
            {{ t('export.excel') }}
          </VBtn>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Tabs -->
    <VCol cols="12">
      <VCard>
        <VTabs v-model="activeTab">
          <VTab v-for="tab in tabs" :key="tab.key">
            <VIcon :icon="tab.icon" size="18" class="me-1" />
            {{ t(tab.title) }}
          </VTab>
        </VTabs>
      </VCard>
    </VCol>

    <!-- Tab 0: Hacim & Performans -->
    <template v-if="activeTab === 0">
      <VCol cols="12">
        <VCard :loading="tabLoading.volume">
          <VCardItem><VCardTitle>{{ t('merchant_report.deposit_volume') }} / {{ t('merchant_report.withdrawal_volume') }}</VCardTitle></VCardItem>
          <VCardText v-if="v.daily_volume?.categories">
            <VRow>
              <VCol cols="12" md="6">
                <div class="text-body-2 text-medium-emphasis mb-2">{{ t('merchant_report.deposit_volume') }}</div>
                <VueApexCharts
                  :options="{ ...baseAreaOpts(v.daily_volume.categories), colors: [themeColors.success, themeColors.info, themeColors.warning, themeColors.primary, themeColors.error, themeColors.secondary], yaxis: { ...baseAreaOpts(v.daily_volume.categories).yaxis, labels: { ...baseAreaOpts(v.daily_volume.categories).yaxis.labels, formatter: val => formatMoney(val) } }, tooltip: { ...baseAreaOpts(v.daily_volume.categories).tooltip, y: { formatter: val => formatMoney(val) } } }"
                  :series="v.daily_volume.deposit_series"
                  height="300"
                />
              </VCol>
              <VCol cols="12" md="6">
                <div class="text-body-2 text-medium-emphasis mb-2">{{ t('merchant_report.withdrawal_volume') }}</div>
                <VueApexCharts
                  :options="{ ...baseAreaOpts(v.daily_volume.categories), colors: [themeColors.error, themeColors.warning, themeColors.info, themeColors.primary], yaxis: { ...baseAreaOpts(v.daily_volume.categories).yaxis, labels: { ...baseAreaOpts(v.daily_volume.categories).yaxis.labels, formatter: val => formatMoney(val) } }, tooltip: { ...baseAreaOpts(v.daily_volume.categories).tooltip, y: { formatter: val => formatMoney(val) } } }"
                  :series="v.daily_volume.withdrawal_series"
                  height="300"
                />
              </VCol>
            </VRow>
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="6">
        <VCard :loading="tabLoading.volume">
          <VCardItem><VCardTitle>{{ t('merchant_report.approval_rate') }}</VCardTitle></VCardItem>
          <VCardText v-if="v.approval_rates?.categories">
            <VueApexCharts
              :options="{ ...baseAreaOpts(v.approval_rates.categories), yaxis: { max: 100, labels: { style: { colors: labelColor, fontSize: '11px' }, formatter: val => val + '%' } } }"
              :series="v.approval_rates.series"
              height="260"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="6">
        <VCard :loading="tabLoading.volume">
          <VCardItem><VCardTitle>{{ t('merchant_report.avg_process_time') }} / {{ t('merchant_report.avg_amount') }}</VCardTitle></VCardItem>
          <VCardText v-if="v.avg_processing_time?.categories">
            <VueApexCharts
              :options="{ ...baseBarOpts(v.avg_processing_time.categories), colors: [themeColors.info] }"
              :series="v.avg_processing_time.series"
              height="260"
            />
          </VCardText>
        </VCard>
      </VCol>
    </template>

    <!-- Tab 1: Oyuncu Analizi -->
    <template v-if="activeTab === 1">
      <VCol cols="12" md="6">
        <VCard :loading="tabLoading.player">
          <VCardItem><VCardTitle>{{ t('merchant_report.active_players') }}</VCardTitle></VCardItem>
          <VCardText v-if="p.active_player_trend?.categories">
            <VueApexCharts
              :options="{ ...baseAreaOpts(p.active_player_trend.categories), colors: [themeColors.primary] }"
              :series="p.active_player_trend.series"
              height="260"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="6">
        <VCard :loading="tabLoading.player">
          <VCardItem><VCardTitle>{{ t('merchant_report.new_vs_returning') }}</VCardTitle></VCardItem>
          <VCardText v-if="p.new_vs_returning?.categories">
            <VueApexCharts
              :options="{ ...baseBarOpts(p.new_vs_returning.categories), chart: { ...baseBarOpts(p.new_vs_returning.categories).chart, stacked: true }, colors: [themeColors.success, themeColors.info] }"
              :series="p.new_vs_returning.series"
              height="260"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="6">
        <VCard :loading="tabLoading.player">
          <VCardItem><VCardTitle>{{ t('merchant_report.avg_tx_per_player') }}</VCardTitle></VCardItem>
          <VCardText v-if="p.avg_tx_per_player?.categories">
            <VueApexCharts
              :options="{ ...baseAreaOpts(p.avg_tx_per_player.categories), colors: [themeColors.warning] }"
              :series="p.avg_tx_per_player.series"
              height="260"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="6">
        <VCard :loading="tabLoading.player">
          <VCardItem><VCardTitle>{{ t('merchant_report.top_players') }}</VCardTitle></VCardItem>
          <VCardText v-if="p.top_players?.length > 0">
            <VTable density="compact" class="text-no-wrap">
              <thead>
                <tr>
                  <th>#</th>
                  <th>{{ t('merchant_report.player_id') }}</th>
                  <th class="text-end">{{ t('merchant_report.tx_count') }}</th>
                  <th class="text-end">{{ t('merchant_report.total_amount') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(pl, i) in p.top_players" :key="i">
                  <td>{{ i + 1 }}</td>
                  <td class="font-weight-medium">{{ pl.player_id }}</td>
                  <td class="text-end">{{ pl.tx_count }}</td>
                  <td class="text-end font-weight-medium">{{ formatMoney(pl.total_amount) }}</td>
                </tr>
              </tbody>
            </VTable>
          </VCardText>
        </VCard>
      </VCol>
    </template>

    <!-- Tab 2: Tutar Analizi -->
    <template v-if="activeTab === 2">
      <VCol cols="12" md="6">
        <VCard :loading="tabLoading.amount">
          <VCardItem><VCardTitle>{{ t('merchant_report.amount_distribution') }}</VCardTitle></VCardItem>
          <VCardText v-if="a.amount_distribution?.categories">
            <VueApexCharts
              :options="{ ...baseBarOpts(a.amount_distribution.categories), chart: { ...baseBarOpts(a.amount_distribution.categories).chart, stacked: true }, colors: [themeColors.success, themeColors.error] }"
              :series="a.amount_distribution.series"
              height="300"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="6">
        <VCard :loading="tabLoading.amount">
          <VCardItem><VCardTitle>{{ t('merchant_report.hourly_density') }}</VCardTitle></VCardItem>
          <VCardText v-if="a.hourly_density?.categories">
            <VueApexCharts
              :options="{ ...baseBarOpts(a.hourly_density.categories), colors: [themeColors.primary], plotOptions: { bar: { columnWidth: '70%', borderRadius: 3 } } }"
              :series="a.hourly_density.series"
              height="300"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12">
        <VCard :loading="tabLoading.amount">
          <VCardItem><VCardTitle>{{ t('merchant_report.daily_min_max') }}</VCardTitle></VCardItem>
          <VCardText v-if="a.daily_min_max?.categories">
            <VueApexCharts
              :options="{ ...baseAreaOpts(a.daily_min_max.categories), colors: [themeColors.error, themeColors.success, themeColors.info], yaxis: { ...baseAreaOpts(a.daily_min_max.categories).yaxis, labels: { ...baseAreaOpts(a.daily_min_max.categories).yaxis.labels, formatter: val => formatMoney(val) } } }"
              :series="a.daily_min_max.series"
              height="280"
            />
          </VCardText>
        </VCard>
      </VCol>
    </template>

    <!-- Tab 3: Finansal -->
    <template v-if="activeTab === 3">
      <VCol cols="12">
        <VCard :loading="tabLoading.financial">
          <VCardItem><VCardTitle>{{ t('merchant_report.commission_revenue') }}</VCardTitle></VCardItem>
          <VCardText v-if="f.commission_trend?.categories">
            <VueApexCharts
              :options="{ ...baseBarOpts(f.commission_trend.categories), chart: { ...baseBarOpts(f.commission_trend.categories).chart, stacked: true }, colors: [themeColors.success, themeColors.warning], yaxis: { ...baseBarOpts(f.commission_trend.categories).yaxis, labels: { ...baseBarOpts(f.commission_trend.categories).yaxis.labels, formatter: val => formatMoney(val) } } }"
              :series="f.commission_trend.series"
              height="280"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="6">
        <VCard :loading="tabLoading.financial">
          <VCardItem><VCardTitle>{{ t('merchant_report.net_case_change') }}</VCardTitle></VCardItem>
          <VCardText v-if="f.net_case_trend?.categories">
            <VueApexCharts
              :options="{ ...baseAreaOpts(f.net_case_trend.categories), colors: [themeColors.primary], yaxis: { ...baseAreaOpts(f.net_case_trend.categories).yaxis, labels: { ...baseAreaOpts(f.net_case_trend.categories).yaxis.labels, formatter: val => formatMoney(val) } } }"
              :series="f.net_case_trend.series"
              height="260"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="6">
        <VCard :loading="tabLoading.financial">
          <VCardItem><VCardTitle>{{ t('merchant_report.merchant_comparison') }}</VCardTitle></VCardItem>
          <VCardText v-if="f.merchant_comparison?.categories">
            <VueApexCharts
              :options="{ ...baseBarOpts(f.merchant_comparison.categories), colors: [themeColors.success, themeColors.error], plotOptions: { bar: { horizontal: true, barHeight: '60%', borderRadius: 4 } }, yaxis: { labels: { style: { colors: labelColor, fontSize: '11px' } } }, tooltip: { ...baseBarOpts(f.merchant_comparison.categories).tooltip, y: { formatter: val => formatMoney(val) } } }"
              :series="f.merchant_comparison.series"
              height="300"
            />
          </VCardText>
        </VCard>
      </VCol>
    </template>

    <!-- Tab 4: Risk -->
    <template v-if="activeTab === 4">
      <VCol cols="12" md="6">
        <VCard :loading="tabLoading.risk">
          <VCardItem><VCardTitle>{{ t('merchant_report.low_amount_ratio') }}</VCardTitle></VCardItem>
          <VCardText v-if="r.low_amount_ratio?.categories">
            <VueApexCharts
              :options="{ ...baseAreaOpts(r.low_amount_ratio.categories), colors: [themeColors.error, themeColors.warning], yaxis: { max: 100, labels: { style: { colors: labelColor, fontSize: '11px' }, formatter: val => val + '%' } } }"
              :series="r.low_amount_ratio.series"
              height="280"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="6">
        <VCard :loading="tabLoading.risk">
          <VCardItem><VCardTitle>{{ t('merchant_report.rejected_trend') }}</VCardTitle></VCardItem>
          <VCardText v-if="r.rejected_trend?.categories">
            <VueApexCharts
              :options="{ ...baseBarOpts(r.rejected_trend.categories), colors: [themeColors.error, themeColors.warning] }"
              :series="r.rejected_trend.series"
              height="280"
            />
          </VCardText>
        </VCard>
      </VCol>
    </template>
  </VRow>

  <!-- Export Dialog -->
  <VDialog v-model="showExportDialog" max-width="500">
    <VCard :title="t('export.excel')">
      <VCardText>
        <VRow>
          <VCol cols="6">
            <AppDateTimePicker v-model="exportForm.date_from" :label="t('common.start_date')" :config="dateConfig" density="compact" />
          </VCol>
          <VCol cols="6">
            <AppDateTimePicker v-model="exportForm.date_to" :label="t('common.end_date')" :config="dateConfig" density="compact" />
          </VCol>
          <VCol cols="6">
            <VSelect v-model="exportForm.type" :items="typeOptions" :label="t('export.tx_type')" density="compact" />
          </VCol>
          <VCol cols="6">
            <VSelect v-model="exportForm.status" :items="statusOptions" :label="t('deposits.status')" density="compact" />
          </VCol>
          <VCol cols="12">
            <VAutocomplete
              v-model="exportForm.merchant_id"
              :items="[{ title: t('merchant_report.all_merchants'), value: null }, ...merchants.map(m => ({ title: m.name, value: m.id }))]"
              :label="t('deposits.merchant')"
              density="compact"
              :no-data-text="t('common.no_data')"
              clearable
            />
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showExportDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="success" :loading="exportLoading" @click="submitExport">
          <VIcon start icon="tabler-file-spreadsheet" />
          {{ t('export.start') }}
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
