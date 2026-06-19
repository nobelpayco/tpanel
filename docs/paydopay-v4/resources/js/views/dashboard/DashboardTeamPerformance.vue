<script setup>
import { useI18n } from 'vue-i18n'
import { useTheme } from 'vuetify'
import { hexToRgb } from '@layouts/utils'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

const props = defineProps({
  dateFrom: { type: String, required: true },
  dateTo: { type: String, required: true },
})

const { t, locale } = useI18n()
const vuetifyTheme = useTheme()

const loading = ref(true)
const refreshing = ref(false)
const teams = ref([])

// Detail dialog
const showDetail = ref(false)
const detailLoading = ref(false)
const detail = ref(null)
const detailDateFrom = ref(props.dateFrom)
const detailDateTo = ref(props.dateTo)
const selectedTeamId = ref(null)

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({ dateFormat: 'Y-m-d', altInput: true, altFormat: 'd.m.Y', locale: localeMap[locale.value] || Turkish }))

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 0 })
const formatMoney2 = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })

const formatDuration = (seconds) => {
  if (!seconds) return '-'
  const min = Math.floor(seconds / 60)
  const sec = seconds % 60
  return `${min}dk ${sec}sn`
}

const formatDate = val => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}

const fetchData = async () => {
  try {
    const token = localStorage.getItem('token')
    const params = new URLSearchParams({ date_from: props.dateFrom, date_to: props.dateTo })
    const res = await fetch(`/api/dashboard/team-performance?${params}`, {
      headers: { 'Accept': 'application/json', 'Authorization': `Bearer ${token}` },
    })
    if (res.ok) teams.value = await res.json()
  } finally {
    loading.value = false
    refreshing.value = false
  }
}

const isToday = computed(() => props.dateFrom === today && props.dateTo === today)

let interval
onMounted(() => {
  fetchData()
  interval = setInterval(() => {
    if (isToday.value) { refreshing.value = true; fetchData() }
  }, 10000)
})
onUnmounted(() => clearInterval(interval))

const caseWarningRowStyle = (team) => {
  const max = Number(team.max_case) || 0
  const cur = Number(team.current_case) || 0
  if (max <= 0) return null
  const distance = max - cur
  if (distance >= 150000) return null
  const ratio = Math.max(0, Math.min(1, (distance - 10000) / 140000))
  const hue = Math.round(30 * ratio)
  const duration = (0.5 + 2.5 * ratio).toFixed(2)
  return `--warn-hue: ${hue}; animation: caseWarningBlink ${duration}s ease-in-out infinite;`
}

const resolveRateColor = rate => {
  if (rate >= 90) return 'success'
  if (rate >= 75) return 'info'
  if (rate >= 50) return 'warning'
  return 'error'
}

const resolveStatusColor = status => {
  const map = { 3: 'success', 4: 'error' }
  return map[status] || 'secondary'
}

const resolveStatusText = status => {
  const map = { 3: 'status.approved', 4: 'status.rejected' }
  return map[status] || 'status.pending'
}

const openDetail = async (team) => {
  selectedTeamId.value = team.id
  detailDateFrom.value = props.dateFrom
  detailDateTo.value = props.dateTo
  showDetail.value = true
  await fetchDetail()
}

const fetchDetail = async () => {
  detailLoading.value = true
  detail.value = null
  try {
    const token = localStorage.getItem('token')
    const params = new URLSearchParams({ date_from: detailDateFrom.value, date_to: detailDateTo.value })
    const res = await fetch(`/api/dashboard/team-detail/${selectedTeamId.value}?${params}`, {
      headers: { 'Accept': 'application/json', 'Authorization': `Bearer ${token}` },
    })
    if (res.ok) detail.value = await res.json()
  } finally {
    detailLoading.value = false
  }
}

// Chart helpers
const themeColors = computed(() => vuetifyTheme.current.value.colors)
const themeVars = computed(() => vuetifyTheme.current.value.variables)
const labelColor = computed(() => `rgba(${hexToRgb(themeColors.value['on-surface'])},${themeVars.value['disabled-opacity']})`)
const borderColor = computed(() => `rgba(${hexToRgb(String(themeVars.value['border-color']))},${themeVars.value['border-opacity']})`)
const isDark = computed(() => vuetifyTheme.global.name.value === 'dark')

const hourlyChartOpts = computed(() => {
  if (!detail.value) return {}
  return {
    chart: { type: 'bar', toolbar: { show: false } },
    plotOptions: { bar: { columnWidth: '65%', borderRadius: 3 } },
    colors: [themeColors.value.primary],
    dataLabels: { enabled: false },
    grid: { borderColor: borderColor.value },
    xaxis: { categories: detail.value.hourly.labels, labels: { style: { colors: labelColor.value, fontSize: '10px', fontFamily: 'Public Sans' } } },
    yaxis: { labels: { style: { colors: labelColor.value, fontSize: '11px' } } },
    tooltip: { theme: isDark.value ? 'dark' : 'light' },
  }
})

const amountDistOpts = computed(() => {
  if (!detail.value) return {}
  return {
    chart: { type: 'donut' },
    labels: detail.value.amount_dist.map(d => d.range_label),
    legend: { position: 'bottom', fontFamily: 'Public Sans' },
    dataLabels: { enabled: true, formatter: (val) => Math.round(val) + '%' },
    plotOptions: { pie: { donut: { size: '60%' } } },
    tooltip: { theme: isDark.value ? 'dark' : 'light' },
  }
})
</script>

<template>
  <VCard :loading="loading || refreshing">
    <VCardItem>
      <VCardTitle>{{ t('dashboard.team_performance') }}</VCardTitle>
      <VCardSubtitle>{{ t('dashboard.today') }}</VCardSubtitle>
    </VCardItem>
    <VDivider />
    <VTable class="text-no-wrap">
      <thead>
        <tr>
          <th>{{ t('teams.name') }}</th>
          <th>{{ t('dashboard.approved') }}</th>
          <th>{{ t('dashboard.rejected') }}</th>
          <th>{{ t('dashboard.approval_rate') }}</th>
          <th>{{ t('dashboard.avg_time') }}</th>
          <th class="text-end">Anlık Kasa</th>
          <th>Son Çekim</th>
          <th class="text-end">{{ t('common.total') }}</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="team in teams"
          :key="team.name"
          class="cursor-pointer"
          :style="caseWarningRowStyle(team)"
          @click="openDetail(team)"
        >
          <td class="font-weight-medium">
            {{ team.name }}
          </td>
          <td>
            <span class="text-success font-weight-medium">{{ team.approved }}</span>
          </td>
          <td>
            <span class="text-error font-weight-medium">{{ team.rejected }}</span>
          </td>
          <td>
            <VChip
              :color="resolveRateColor(team.rate)"
              label
              size="small"
            >
              %{{ team.rate }}
            </VChip>
          </td>
          <td>
            <VTooltip location="top">
              <template #activator="{ props }">
                <span
                  v-bind="props"
                  class="cursor-pointer"
                >
                  <span class="text-success font-weight-medium">{{ formatDuration(team.avg_approved_sec) }}</span>
                  <span class="text-medium-emphasis mx-1">/</span>
                  <span class="text-error font-weight-medium">{{ formatDuration(team.avg_rejected_sec) }}</span>
                  <span class="text-medium-emphasis mx-1">/</span>
                  <span class="text-warning font-weight-medium">{{ formatDuration(team.avg_process_sec) }}</span>
                </span>
              </template>
              <div class="pa-1">
                <div><span class="text-success">{{ t('dashboard.avg_approved') }}:</span> {{ formatDuration(team.avg_approved_sec) }}</div>
                <div><span class="text-error">{{ t('dashboard.avg_rejected') }}:</span> {{ formatDuration(team.avg_rejected_sec) }}</div>
                <div><span class="text-warning">{{ t('dashboard.avg_process') }}:</span> {{ formatDuration(team.avg_process_sec) }}</div>
              </div>
            </VTooltip>
          </td>
          <td class="text-end font-weight-bold" :class="`text-${team.current_case >= 0 ? 'success' : 'error'}`">
            <VTooltip v-if="team.max_case > 0" location="top">
              <template #activator="{ props }">
                <span v-bind="props">{{ formatMoney(team.current_case) }}</span>
              </template>
              <div>Max: {{ formatMoney(team.max_case) }}</div>
              <div>Kalan: {{ formatMoney(team.max_case - team.current_case) }}</div>
            </VTooltip>
            <span v-else>{{ formatMoney(team.current_case) }}</span>
          </td>
          <td class="text-body-2 text-medium-emphasis">
            {{ team.last_withdraw ? formatDate(team.last_withdraw) : '-' }}
          </td>
          <td class="font-weight-medium">
            {{ formatMoney(team.total) }}
          </td>
        </tr>
        <tr v-if="!loading && teams.length === 0">
          <td
            colspan="8"
            class="text-center text-medium-emphasis"
          >
            {{ t('common.no_data') }}
          </td>
        </tr>
      </tbody>
    </VTable>
  </VCard>

  <!-- Team Detail Dialog -->
  <VDialog v-model="showDetail" max-width="950">
    <VCard :loading="detailLoading">
      <VCardItem v-if="detail">
        <VCardTitle class="d-flex align-center gap-2">
          <VIcon icon="tabler-users-group" size="24" />
          {{ detail.team.name }}
          <VChip color="primary" label size="small" class="ms-2">%{{ detail.team.commission }} {{ t('teams.commission') }}</VChip>
        </VCardTitle>
      </VCardItem>

      <!-- Tarih filtresi -->
      <VCardText class="d-flex align-center gap-3 flex-wrap pb-0">
        <div style="min-width: 140px;">
          <AppDateTimePicker v-model="detailDateFrom" :label="t('common.start_date')" :config="dateConfig" density="compact" />
        </div>
        <div style="min-width: 140px;">
          <AppDateTimePicker v-model="detailDateTo" :label="t('common.end_date')" :config="dateConfig" density="compact" />
        </div>
        <VBtn color="primary" size="small" @click="fetchDetail">{{ t('common.filter') }}</VBtn>
      </VCardText>

      <VDivider class="mt-3" />

      <VCardText v-if="detail">
        <!-- Özet kartları -->
        <VRow class="mb-4">
          <VCol cols="6" sm="3">
            <div class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-success), 0.08)">
              <div class="text-h5 font-weight-bold text-success">{{ detail.summary.approved }}</div>
              <div class="text-caption text-medium-emphasis">{{ t('dashboard.approved') }}</div>
              <div class="text-body-2 font-weight-medium text-success">{{ formatMoney2(detail.summary.approved_amount) }}</div>
            </div>
          </VCol>
          <VCol cols="6" sm="3">
            <div class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-error), 0.08)">
              <div class="text-h5 font-weight-bold text-error">{{ detail.summary.rejected }}</div>
              <div class="text-caption text-medium-emphasis">{{ t('dashboard.rejected') }}</div>
              <div class="text-body-2 font-weight-medium text-error">{{ formatMoney2(detail.summary.rejected_amount) }}</div>
            </div>
          </VCol>
          <VCol cols="6" sm="3">
            <div class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-primary), 0.08)">
              <div class="text-h5 font-weight-bold" :class="`text-${resolveRateColor(detail.summary.approval_rate)}`">%{{ detail.summary.approval_rate }}</div>
              <div class="text-caption text-medium-emphasis">{{ t('dashboard.approval_rate') }}</div>
            </div>
          </VCol>
          <VCol cols="6" sm="3">
            <div class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-info), 0.08)">
              <div class="text-h6 font-weight-bold text-info">{{ formatMoney2(detail.summary.avg_amount) }}</div>
              <div class="text-caption text-medium-emphasis">{{ t('merchant_report.avg_amount') }}</div>
              <div class="text-caption text-medium-emphasis mt-1">
                <span class="text-success">{{ formatDuration(detail.summary.avg_approve_time) }}</span>
                /
                <span class="text-error">{{ formatDuration(detail.summary.avg_reject_time) }}</span>
              </div>
            </div>
          </VCol>
        </VRow>

        <VRow class="mb-4">
          <!-- Agent performansı -->
          <VCol cols="12" md="6">
            <div class="text-body-2 text-medium-emphasis mb-2">Agent Performansı</div>
            <VTable density="compact" class="text-no-wrap">
              <thead>
                <tr>
                  <th>Agent</th>
                  <th class="text-center">{{ t('dashboard.approved') }}</th>
                  <th class="text-center">{{ t('dashboard.rejected') }}</th>
                  <th class="text-center">%</th>
                  <th class="text-end">{{ t('dashboard.avg_time') }}</th>
                  <th class="text-end">{{ t('common.total') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="ag in detail.agents" :key="ag.name">
                  <td class="font-weight-medium">{{ ag.name }}</td>
                  <td class="text-center text-success">{{ ag.approved }}</td>
                  <td class="text-center text-error">{{ ag.rejected }}</td>
                  <td class="text-center">
                    <VChip :color="resolveRateColor(ag.rate)" label size="x-small">%{{ ag.rate }}</VChip>
                  </td>
                  <td class="text-end text-medium-emphasis">{{ formatDuration(ag.avg_time) }}</td>
                  <td class="text-end font-weight-medium">{{ formatMoney(ag.total) }}</td>
                </tr>
              </tbody>
            </VTable>
          </VCol>

          <!-- Tutar dağılımı -->
          <VCol cols="12" md="6">
            <div class="text-body-2 text-medium-emphasis mb-2">{{ t('merchant_report.amount_distribution') }}</div>
            <VueApexCharts
              v-if="detail.amount_dist.length > 0"
              :options="amountDistOpts"
              :series="detail.amount_dist.map(d => d.cnt)"
              height="220"
            />
          </VCol>
        </VRow>

        <!-- Saatlik yoğunluk -->
        <div class="mb-4">
          <div class="text-body-2 text-medium-emphasis mb-2">{{ t('merchant_report.hourly_density') }}</div>
          <VueApexCharts
            v-if="detail.hourly.counts.some(c => c > 0)"
            :options="hourlyChartOpts"
            :series="[{ name: t('merchant_report.tx_count'), data: detail.hourly.counts }]"
            height="180"
          />
        </div>

        <!-- Son işlemler -->
        <div class="text-body-2 text-medium-emphasis mb-2">{{ t('dashboard.recent_transactions') }}</div>
        <VTable density="compact" class="text-no-wrap">
          <thead>
            <tr>
              <th>ID</th>
              <th>{{ t('deposits.sender') }}</th>
              <th class="text-end">{{ t('deposits.amount') }}</th>
              <th>{{ t('deposits.status') }}</th>
              <th>Agent</th>
              <th>{{ t('dashboard.avg_time') }}</th>
              <th>{{ t('deposits.date') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="tx in detail.recent" :key="tx.id">
              <td class="text-body-2">#{{ tx.id }}</td>
              <td class="text-body-2">{{ tx.name || '-' }}</td>
              <td class="text-end font-weight-medium">{{ formatMoney(tx.amount) }}</td>
              <td>
                <VChip :color="resolveStatusColor(tx.status)" label size="x-small">
                  {{ t(resolveStatusText(tx.status)) }}
                </VChip>
              </td>
              <td class="text-body-2">{{ tx.agent_name || '-' }}</td>
              <td class="text-body-2 text-medium-emphasis">{{ formatDuration(tx.duration) }}</td>
              <td class="text-body-2 text-medium-emphasis">{{ formatDate(tx.date) }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCardText>

      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showDetail = false">{{ t('common.cancel') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>

<style>
@keyframes caseWarningBlink {
  0%, 100% { background-color: hsla(var(--warn-hue), 85%, 50%, 0.05); }
  50% { background-color: hsla(var(--warn-hue), 85%, 50%, 0.45); }
}
</style>
