<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useTheme } from 'vuetify'
import { hexToRgb } from '@layouts/utils'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default', roles: [1] } })

const { t, locale } = useI18n()
const vuetifyTheme = useTheme()
const { headers } = useApi()

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const monthStart = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-01` })()
const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({ dateFormat: 'Y-m-d', altInput: true, altFormat: 'd.m.Y', locale: localeMap[locale.value] || Turkish }))

const dateFrom = ref(monthStart)
const dateTo = ref(today)
const merchantId = ref(null)
const merchants = ref([])
const activeTab = ref(0)

const queueData = ref(null)
const peakData = ref(null)
const slaData = ref(null)
const queueLoading = ref(false)
const peakLoading = ref(false)
const slaLoading = ref(false)
const tabLoaded = ref({})

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 0 })
const formatDuration = (seconds) => {
  if (!seconds) return '-'
  const min = Math.floor(seconds / 60)
  const sec = Math.round(seconds % 60)
  return `${min}dk ${sec}sn`
}

const themeColors = computed(() => vuetifyTheme.current.value.colors)
const themeVars = computed(() => vuetifyTheme.current.value.variables)
const labelColor = computed(() => `rgba(${hexToRgb(themeColors.value['on-surface'])},${themeVars.value['disabled-opacity']})`)
const borderColor = computed(() => `rgba(${hexToRgb(String(themeVars.value['border-color']))},${themeVars.value['border-opacity']})`)
const isDark = computed(() => vuetifyTheme.global.name.value === 'dark')

const baseBarOpts = (cats) => ({
  chart: { type: 'bar', toolbar: { show: false } },
  plotOptions: { bar: { columnWidth: '55%', borderRadius: 4 } },
  dataLabels: { enabled: false },
  grid: { borderColor: borderColor.value },
  xaxis: { categories: cats, labels: { style: { colors: labelColor.value, fontSize: '11px', fontFamily: 'Public Sans' } } },
  yaxis: { labels: { style: { colors: labelColor.value, fontSize: '11px', fontFamily: 'Public Sans' } } },
  legend: { position: 'top', labels: { colors: labelColor.value }, fontFamily: 'Public Sans' },
  tooltip: { theme: isDark.value ? 'dark' : 'light' },
})

const baseAreaOpts = (cats) => ({
  chart: { type: 'area', toolbar: { show: false }, parentHeightOffset: 0 },
  stroke: { width: 2, curve: 'smooth' },
  fill: { type: 'gradient', gradient: { shadeIntensity: 0.8, opacityFrom: 0.4, opacityTo: 0.1 } },
  dataLabels: { enabled: false },
  grid: { borderColor: borderColor.value },
  xaxis: { categories: cats, labels: { style: { colors: labelColor.value, fontSize: '11px', fontFamily: 'Public Sans' } } },
  yaxis: { labels: { style: { colors: labelColor.value, fontSize: '11px', fontFamily: 'Public Sans' } } },
  legend: { position: 'top', labels: { colors: labelColor.value }, fontFamily: 'Public Sans' },
  tooltip: { theme: isDark.value ? 'dark' : 'light' },
})

const fetchMerchants = async () => {
  const res = await fetch('/api/merchant-reports/filter-options', { headers })
  if (res.ok) merchants.value = await res.json()
}

const buildParams = () => {
  const params = new URLSearchParams({ date_from: dateFrom.value, date_to: dateTo.value })
  if (merchantId.value) params.append('merchant_id', merchantId.value)
  return params
}

const fetchQueue = async () => {
  queueLoading.value = true
  try {
    const res = await fetch(`/api/operations/queue-analysis?${buildParams()}`, { headers })
    if (res.ok) { queueData.value = await res.json(); tabLoaded.value[0] = true }
  } finally { queueLoading.value = false }
}

const fetchPeak = async () => {
  peakLoading.value = true
  try {
    const res = await fetch(`/api/operations/peak-hours?${buildParams()}`, { headers })
    if (res.ok) { peakData.value = await res.json(); tabLoaded.value[1] = true }
  } finally { peakLoading.value = false }
}

const fetchSla = async () => {
  slaLoading.value = true
  try {
    const res = await fetch(`/api/operations/sla?${buildParams()}`, { headers })
    if (res.ok) { slaData.value = await res.json(); tabLoaded.value[2] = true }
  } finally { slaLoading.value = false }
}

const applyFilter = () => {
  tabLoaded.value = {}
  if (activeTab.value === 0) fetchQueue()
  else if (activeTab.value === 1) fetchPeak()
  else fetchSla()
}

watch(activeTab, (val) => {
  if (val === 0 && !tabLoaded.value[0]) fetchQueue()
  if (val === 1 && !tabLoaded.value[1]) fetchPeak()
  if (val === 2 && !tabLoaded.value[2]) fetchSla()
})

onMounted(() => { fetchMerchants(); fetchQueue() })

const resolveRateColor = rate => rate >= 90 ? 'success' : rate >= 75 ? 'info' : rate >= 50 ? 'warning' : 'error'
</script>

<template>
  <VRow>
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
        </VCardText>
      </VCard>
    </VCol>

    <VCol cols="12">
      <VCard>
        <VTabs v-model="activeTab">
          <VTab><VIcon icon="tabler-list-check" size="18" class="me-1" />{{ t('operations.queue_analysis') }}</VTab>
          <VTab><VIcon icon="tabler-flame" size="18" class="me-1" />{{ t('operations.peak_hours') }}</VTab>
          <VTab><VIcon icon="tabler-clock-check" size="18" class="me-1" />{{ t('operations.sla_report') }}</VTab>
        </VTabs>
      </VCard>
    </VCol>

    <!-- Tab 0: Kuyruk Analizi -->
    <template v-if="activeTab === 0">
      <VCol v-if="queueData?.queue_status" cols="12">
        <VCard :loading="queueLoading">
          <VCardItem><VCardTitle>{{ t('operations.queue_status') }}</VCardTitle></VCardItem>
          <VCardText>
            <VRow>
              <VCol v-for="item in queueData.queue_status" :key="item.label" cols="6" sm="3">
                <div class="text-center pa-3 rounded" :style="`background: rgba(var(--v-theme-${item.color || 'primary'}), 0.08)`">
                  <div class="text-h5 font-weight-bold">{{ item.count }}</div>
                  <div class="text-caption">{{ item.label }}</div>
                  <div class="text-body-2 font-weight-medium">{{ formatMoney(item.amount) }}</div>
                </div>
              </VCol>
            </VRow>
          </VCardText>
        </VCard>
      </VCol>
      <VCol v-if="queueData?.age_distribution" cols="12" md="6">
        <VCard :loading="queueLoading">
          <VCardItem><VCardTitle>{{ t('operations.age_distribution') }}</VCardTitle></VCardItem>
          <VCardText>
            <VueApexCharts
              :options="{ ...baseBarOpts(queueData.age_distribution.categories), colors: [themeColors.warning, themeColors.error] }"
              :series="queueData.age_distribution.series"
              height="280"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol v-if="queueData?.oldest_pending" cols="12" md="6">
        <VCard :loading="queueLoading">
          <VCardItem><VCardTitle>{{ t('operations.oldest_pending') }}</VCardTitle></VCardItem>
          <VTable density="compact" class="text-no-wrap">
            <thead>
              <tr>
                <th>ID</th>
                <th>{{ t('deposits.amount') }}</th>
                <th>{{ t('operations.avg_wait_time') }}</th>
                <th>{{ t('deposits.date') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="item in queueData.oldest_pending" :key="item.id">
                <td class="text-body-2">#{{ item.id }}</td>
                <td class="font-weight-medium">{{ formatMoney(item.amount) }}</td>
                <td class="text-body-2">{{ formatDuration(item.age_seconds) }}</td>
                <td class="text-body-2 text-medium-emphasis">{{ item.created_at }}</td>
              </tr>
            </tbody>
          </VTable>
        </VCard>
      </VCol>
    </template>

    <!-- Tab 1: Pik Saat -->
    <template v-if="activeTab === 1">
      <VCol v-if="peakData?.heatmap" cols="12">
        <VCard :loading="peakLoading">
          <VCardItem><VCardTitle>{{ t('operations.hourly_heatmap') }}</VCardTitle></VCardItem>
          <VCardText>
            <VueApexCharts
              :options="{
                chart: { type: 'heatmap', toolbar: { show: false } },
                dataLabels: { enabled: false },
                colors: [themeColors.primary],
                xaxis: { labels: { style: { colors: labelColor, fontSize: '10px' } } },
                yaxis: { labels: { style: { colors: labelColor, fontSize: '11px' } } },
                tooltip: { theme: isDark ? 'dark' : 'light' },
              }"
              :series="peakData.heatmap"
              height="280"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol v-if="peakData?.hourly_totals" cols="12" md="6">
        <VCard :loading="peakLoading">
          <VCardItem><VCardTitle>{{ t('operations.peak_hour') }}</VCardTitle></VCardItem>
          <VCardText>
            <VueApexCharts
              :options="{ ...baseBarOpts(peakData.hourly_totals.categories), colors: [themeColors.success, themeColors.error] }"
              :series="peakData.hourly_totals.series"
              height="260"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol v-if="peakData?.daily_totals" cols="12" md="6">
        <VCard :loading="peakLoading">
          <VCardItem><VCardTitle>{{ t('operations.day_of_week') }}</VCardTitle></VCardItem>
          <VCardText>
            <VueApexCharts
              :options="{ ...baseBarOpts(peakData.daily_totals.categories), colors: [themeColors.primary] }"
              :series="peakData.daily_totals.series"
              height="260"
            />
          </VCardText>
        </VCard>
      </VCol>
    </template>

    <!-- Tab 2: SLA Raporu -->
    <template v-if="activeTab === 2">
      <VCol v-if="slaData?.overall" cols="12">
        <VCard :loading="slaLoading">
          <VCardText class="d-flex gap-4 flex-wrap">
            <div class="text-center pa-4 rounded" style="background: rgba(var(--v-theme-primary), 0.08); min-width: 150px;">
              <div class="text-h4 font-weight-bold" :class="`text-${resolveRateColor(slaData.overall.compliance_rate)}`">
                %{{ slaData.overall.compliance_rate }}
              </div>
              <div class="text-body-2">{{ t('operations.sla_compliance') }}</div>
            </div>
            <div class="text-center pa-4 rounded" style="background: rgba(var(--v-theme-success), 0.08); min-width: 120px;">
              <div class="text-h5 font-weight-bold text-success">{{ slaData.overall.within_sla }}</div>
              <div class="text-caption">{{ t('operations.within_sla') }}</div>
            </div>
            <div class="text-center pa-4 rounded" style="background: rgba(var(--v-theme-error), 0.08); min-width: 120px;">
              <div class="text-h5 font-weight-bold text-error">{{ slaData.overall.breached }}</div>
              <div class="text-caption">{{ t('operations.breach') }}</div>
            </div>
            <div class="text-center pa-4 rounded" style="background: rgba(var(--v-theme-info), 0.08); min-width: 120px;">
              <div class="text-h6 font-weight-bold text-info">{{ formatDuration(slaData.overall.avg_time) }}</div>
              <div class="text-caption">{{ t('operations.avg_wait_time') }}</div>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <VCol v-if="slaData?.daily_trend" cols="12" md="6">
        <VCard :loading="slaLoading">
          <VCardItem><VCardTitle>{{ t('operations.sla_trend') }}</VCardTitle></VCardItem>
          <VCardText>
            <VueApexCharts
              :options="{ ...baseAreaOpts(slaData.daily_trend.categories), colors: [themeColors.success], yaxis: { max: 100, labels: { style: { colors: labelColor, fontSize: '11px' }, formatter: val => val + '%' } } }"
              :series="slaData.daily_trend.series"
              height="260"
            />
          </VCardText>
        </VCard>
      </VCol>

      <VCol v-if="slaData?.by_team" cols="12" md="6">
        <VCard :loading="slaLoading">
          <VCardItem><VCardTitle>{{ t('operations.sla_by_team') }}</VCardTitle></VCardItem>
          <VTable density="compact" class="text-no-wrap">
            <thead>
              <tr>
                <th>{{ t('teams.name') }}</th>
                <th class="text-center">{{ t('operations.sla_compliance') }}</th>
                <th class="text-center">{{ t('operations.within_sla') }}</th>
                <th class="text-center">{{ t('operations.breach') }}</th>
                <th class="text-end">{{ t('operations.avg_wait_time') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="team in slaData.by_team" :key="team.name">
                <td class="font-weight-medium">{{ team.name }}</td>
                <td class="text-center">
                  <VChip :color="resolveRateColor(team.compliance_rate)" label size="small">%{{ team.compliance_rate }}</VChip>
                </td>
                <td class="text-center text-success">{{ team.within_sla }}</td>
                <td class="text-center text-error">{{ team.breached }}</td>
                <td class="text-end text-medium-emphasis">{{ formatDuration(team.avg_time) }}</td>
              </tr>
            </tbody>
          </VTable>
        </VCard>
      </VCol>

      <VCol v-if="slaData?.worst_agents" cols="12">
        <VCard :loading="slaLoading">
          <VCardItem><VCardTitle>{{ t('operations.worst_agents') }}</VCardTitle></VCardItem>
          <VTable density="compact" class="text-no-wrap">
            <thead>
              <tr>
                <th>Agent</th>
                <th>{{ t('deposits.team') }}</th>
                <th class="text-center">{{ t('operations.breach') }}</th>
                <th class="text-end">{{ t('operations.avg_wait_time') }}</th>
                <th class="text-center">{{ t('operations.sla_compliance') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="agent in slaData.worst_agents" :key="agent.name">
                <td class="font-weight-medium">{{ agent.name }}</td>
                <td class="text-body-2">{{ agent.team }}</td>
                <td class="text-center text-error font-weight-medium">{{ agent.breached }}</td>
                <td class="text-end text-medium-emphasis">{{ formatDuration(agent.avg_time) }}</td>
                <td class="text-center">
                  <VChip :color="resolveRateColor(agent.compliance_rate)" label size="small">%{{ agent.compliance_rate }}</VChip>
                </td>
              </tr>
            </tbody>
          </VTable>
        </VCard>
      </VCol>
    </template>
  </VRow>
</template>
