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
const teamIds = ref([])
const teams = ref([])

const overview = ref({ rows: [] })
const trends = ref({ categories: [], success_rate: [], avg_time_min: [], deposit_volume: [] })
const hourly = ref({ categories: [], series: [] })

const loading = ref(false)
const sortBy = ref('deposit_volume')
const sortDesc = ref(true)
const activeTab = ref(0)

const formatMoney = v => '₺' + Number(v || 0).toLocaleString('tr-TR', { minimumFractionDigits: 0 })
const formatSec = v => {
  if (v === null || v === undefined) return '-'
  if (v < 60) return Math.round(v) + ' sn'
  const m = Math.floor(v / 60); const s = Math.round(v - m * 60)
  return s ? `${m} dk ${s} sn` : `${m} dk`
}

const fetchTeams = async () => {
  const res = await fetch('/api/team-reports/filter-options', { headers })
  if (res.ok) teams.value = await res.json()
}

const fetchAll = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ date_from: dateFrom.value, date_to: dateTo.value })
    if (teamIds.value.length) params.append('team_ids', teamIds.value.join(','))
    const [r1, r2, r3] = await Promise.all([
      fetch(`/api/team-reports/overview?${params}`, { headers }).then(r => r.json()),
      fetch(`/api/team-reports/trends?${params}`, { headers }).then(r => r.json()),
      fetch(`/api/team-reports/hourly?${params}`, { headers }).then(r => r.json()),
    ])
    overview.value = r1
    trends.value = r2
    hourly.value = r3
  } finally {
    loading.value = false
  }
}

onMounted(async () => { await fetchTeams(); await fetchAll() })

const sortedRows = computed(() => {
  const rows = [...(overview.value.rows || [])]
  rows.sort((a, b) => {
    const av = a[sortBy.value]; const bv = b[sortBy.value]
    if (typeof av === 'string') return sortDesc.value ? bv.localeCompare(av) : av.localeCompare(bv)
    return sortDesc.value ? (bv ?? 0) - (av ?? 0) : (av ?? 0) - (bv ?? 0)
  })
  return rows
})

const setSort = key => {
  if (sortBy.value === key) sortDesc.value = !sortDesc.value
  else { sortBy.value = key; sortDesc.value = true }
}

const totals = computed(() => {
  const rows = overview.value.rows || []
  const sum = k => rows.reduce((s, r) => s + (Number(r[k]) || 0), 0)
  const dApp = sum('deposit_approved'); const dRej = sum('deposit_rejected')
  const wApp = sum('withdraw_approved'); const wRej = sum('withdraw_rejected')
  return {
    deposit_approved: dApp, deposit_rejected: dRej,
    withdraw_approved: wApp, withdraw_rejected: wRej,
    deposit_volume: sum('deposit_volume'), withdraw_volume: sum('withdraw_volume'),
    deposit_success_rate: (dApp + dRej) ? Math.round(dApp * 10000 / (dApp + dRej)) / 100 : 0,
    withdraw_success_rate: (wApp + wRej) ? Math.round(wApp * 10000 / (wApp + wRej)) / 100 : 0,
  }
})

const winners = computed(() => {
  const rows = overview.value.rows || []
  if (! rows.length) return null
  const fastest = [...rows].filter(r => r.deposit_avg_seconds).sort((a, b) => a.deposit_avg_seconds - b.deposit_avg_seconds)[0]
  const topVolume = [...rows].sort((a, b) => b.deposit_volume - a.deposit_volume)[0]
  const bestRate = [...rows].filter(r => r.deposit_approved + r.deposit_rejected > 5).sort((a, b) => b.deposit_success_rate - a.deposit_success_rate)[0]
  return { fastest, topVolume, bestRate }
})

// Chart styling
const themeColors = computed(() => vuetifyTheme.current.value.colors)
const themeVars = computed(() => vuetifyTheme.current.value.variables)
const labelColor = computed(() => `rgba(${hexToRgb(themeColors.value['on-surface'])},${themeVars.value['disabled-opacity']})`)
const borderColor = computed(() => `rgba(${hexToRgb(String(themeVars.value['border-color']))},${themeVars.value['border-opacity']})`)
const isDark = computed(() => vuetifyTheme.global.name.value === 'dark')

const chartPalette = computed(() => [
  themeColors.value.primary, themeColors.value.success, themeColors.value.warning,
  themeColors.value.error, themeColors.value.info, themeColors.value.secondary,
  '#9c27b0', '#00bcd4', '#ff5722', '#795548', '#607d8b', '#e91e63',
])

const baseLineOpts = cats => ({
  chart: { type: 'line', toolbar: { show: false }, parentHeightOffset: 0, zoom: { enabled: false } },
  stroke: { width: 2, curve: 'smooth' },
  dataLabels: { enabled: false },
  grid: { borderColor: borderColor.value, strokeDashArray: 4 },
  markers: { size: 3 },
  xaxis: { categories: cats, labels: { style: { colors: labelColor.value, fontSize: '11px' }, rotate: -45, rotateAlways: cats.length > 12 } },
  yaxis: { labels: { style: { colors: labelColor.value, fontSize: '11px' } } },
  legend: { position: 'top', labels: { colors: labelColor.value } },
  tooltip: { theme: isDark.value ? 'dark' : 'light' },
  colors: chartPalette.value,
})

const baseBarOpts = cats => ({
  chart: { type: 'bar', toolbar: { show: false } },
  plotOptions: { bar: { columnWidth: '70%', borderRadius: 3 } },
  dataLabels: { enabled: false },
  grid: { borderColor: borderColor.value, strokeDashArray: 4 },
  xaxis: { categories: cats, labels: { style: { colors: labelColor.value, fontSize: '11px' }, rotate: -45, rotateAlways: cats.length > 12 } },
  yaxis: { labels: { style: { colors: labelColor.value, fontSize: '11px' } } },
  legend: { position: 'top', labels: { colors: labelColor.value } },
  tooltip: { theme: isDark.value ? 'dark' : 'light' },
  colors: chartPalette.value,
})
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
            v-model="teamIds"
            :items="teams.map(team => ({ title: team.name, value: team.id }))"
            label="Takımlar"
            density="compact"
            multiple chips closable-chips clearable
            style="min-width: 280px;"
            :no-data-text="t('common.no_data')"
          />
          <VBtn color="primary" :loading="loading" @click="fetchAll">{{ t('common.filter') }}</VBtn>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Highlights -->
    <VCol v-if="winners" cols="12">
      <VRow>
        <VCol cols="12" md="4">
          <VCard color="success" variant="tonal">
            <VCardText class="d-flex align-center gap-3">
              <VIcon icon="tabler-bolt" size="32" />
              <div>
                <div class="text-caption">En Hızlı Onay</div>
                <div class="text-h6">{{ winners.fastest?.team_name || '-' }}</div>
                <div class="text-body-2">{{ formatSec(winners.fastest?.deposit_avg_seconds) }}</div>
              </div>
            </VCardText>
          </VCard>
        </VCol>
        <VCol cols="12" md="4">
          <VCard color="primary" variant="tonal">
            <VCardText class="d-flex align-center gap-3">
              <VIcon icon="tabler-trophy" size="32" />
              <div>
                <div class="text-caption">En Yüksek Hacim</div>
                <div class="text-h6">{{ winners.topVolume?.team_name || '-' }}</div>
                <div class="text-body-2">{{ formatMoney(winners.topVolume?.deposit_volume) }}</div>
              </div>
            </VCardText>
          </VCard>
        </VCol>
        <VCol cols="12" md="4">
          <VCard color="info" variant="tonal">
            <VCardText class="d-flex align-center gap-3">
              <VIcon icon="tabler-target-arrow" size="32" />
              <div>
                <div class="text-caption">En Yüksek Başarı Oranı</div>
                <div class="text-h6">{{ winners.bestRate?.team_name || '-' }}</div>
                <div class="text-body-2">%{{ winners.bestRate?.deposit_success_rate ?? 0 }}</div>
              </div>
            </VCardText>
          </VCard>
        </VCol>
      </VRow>
    </VCol>

    <!-- Tabs -->
    <VCol cols="12">
      <VCard>
        <VTabs v-model="activeTab">
          <VTab><VIcon icon="tabler-table" size="18" class="me-1" />Karşılaştırma</VTab>
          <VTab><VIcon icon="tabler-chart-line" size="18" class="me-1" />Trendler</VTab>
          <VTab><VIcon icon="tabler-clock-hour-9" size="18" class="me-1" />Saatlik Yoğunluk</VTab>
        </VTabs>
      </VCard>
    </VCol>

    <!-- Tab 0: Comparison Table -->
    <template v-if="activeTab === 0">
      <VCol cols="12">
        <VCard :loading="loading">
          <VCardItem><VCardTitle>Takım Karşılaştırma</VCardTitle></VCardItem>
          <VCardText>
            <VTable density="compact" hover class="text-no-wrap">
              <thead>
                <tr>
                  <th class="cursor-pointer" @click="setSort('team_name')">Takım <VIcon v-if="sortBy === 'team_name'" :icon="sortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                  <th class="text-end cursor-pointer" @click="setSort('deposit_approved')">Yat. Onay <VIcon v-if="sortBy === 'deposit_approved'" :icon="sortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                  <th class="text-end cursor-pointer" @click="setSort('deposit_rejected')">Yat. Red <VIcon v-if="sortBy === 'deposit_rejected'" :icon="sortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                  <th class="text-end cursor-pointer" @click="setSort('deposit_success_rate')">Yat. Onay % <VIcon v-if="sortBy === 'deposit_success_rate'" :icon="sortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                  <th class="text-end cursor-pointer" @click="setSort('deposit_avg_seconds')">Ort. Süre <VIcon v-if="sortBy === 'deposit_avg_seconds'" :icon="sortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                  <th class="text-end cursor-pointer" @click="setSort('deposit_volume')">Yatırım Hacmi <VIcon v-if="sortBy === 'deposit_volume'" :icon="sortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                  <th class="text-end cursor-pointer" @click="setSort('withdraw_approved')">Çek. Onay <VIcon v-if="sortBy === 'withdraw_approved'" :icon="sortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                  <th class="text-end cursor-pointer" @click="setSort('withdraw_rejected')">Çek. Red <VIcon v-if="sortBy === 'withdraw_rejected'" :icon="sortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                  <th class="text-end cursor-pointer" @click="setSort('withdraw_volume')">Çekim Hacmi <VIcon v-if="sortBy === 'withdraw_volume'" :icon="sortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                  <th class="text-end cursor-pointer" @click="setSort('net_volume')">Net <VIcon v-if="sortBy === 'net_volume'" :icon="sortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="row in sortedRows" :key="row.team_id">
                  <td class="font-weight-medium">{{ row.team_name }}</td>
                  <td class="text-end text-success">{{ row.deposit_approved.toLocaleString('tr-TR') }}</td>
                  <td class="text-end text-error">{{ row.deposit_rejected.toLocaleString('tr-TR') }}</td>
                  <td class="text-end">
                    <VChip size="x-small" :color="row.deposit_success_rate >= 80 ? 'success' : row.deposit_success_rate >= 60 ? 'warning' : 'error'" variant="tonal">
                      %{{ row.deposit_success_rate }}
                    </VChip>
                  </td>
                  <td class="text-end">{{ formatSec(row.deposit_avg_seconds) }}</td>
                  <td class="text-end font-weight-medium">{{ formatMoney(row.deposit_volume) }}</td>
                  <td class="text-end text-success">{{ row.withdraw_approved.toLocaleString('tr-TR') }}</td>
                  <td class="text-end text-error">{{ row.withdraw_rejected.toLocaleString('tr-TR') }}</td>
                  <td class="text-end font-weight-medium">{{ formatMoney(row.withdraw_volume) }}</td>
                  <td class="text-end font-weight-medium" :class="row.net_volume >= 0 ? 'text-success' : 'text-error'">{{ formatMoney(row.net_volume) }}</td>
                </tr>
                <tr v-if="!sortedRows.length">
                  <td colspan="10" class="text-center text-medium-emphasis py-4">{{ t('common.no_data') }}</td>
                </tr>
              </tbody>
              <tfoot v-if="sortedRows.length">
                <tr class="font-weight-bold">
                  <td>TOPLAM</td>
                  <td class="text-end">{{ totals.deposit_approved.toLocaleString('tr-TR') }}</td>
                  <td class="text-end">{{ totals.deposit_rejected.toLocaleString('tr-TR') }}</td>
                  <td class="text-end">%{{ totals.deposit_success_rate }}</td>
                  <td class="text-end">-</td>
                  <td class="text-end">{{ formatMoney(totals.deposit_volume) }}</td>
                  <td class="text-end">{{ totals.withdraw_approved.toLocaleString('tr-TR') }}</td>
                  <td class="text-end">{{ totals.withdraw_rejected.toLocaleString('tr-TR') }}</td>
                  <td class="text-end">{{ formatMoney(totals.withdraw_volume) }}</td>
                  <td class="text-end">{{ formatMoney(totals.deposit_volume - totals.withdraw_volume) }}</td>
                </tr>
              </tfoot>
            </VTable>
          </VCardText>
        </VCard>
      </VCol>
    </template>

    <!-- Tab 1: Trends -->
    <template v-if="activeTab === 1">
      <VCol cols="12">
        <VCard :loading="loading">
          <VCardItem><VCardTitle>Onay Oranı Trendi (%)</VCardTitle></VCardItem>
          <VCardText v-if="trends.success_rate?.length">
            <VueApexCharts
              :options="{ ...baseLineOpts(trends.categories), yaxis: { max: 100, labels: { style: { colors: labelColor, fontSize: '11px' }, formatter: val => val + '%' } }, tooltip: { ...baseLineOpts(trends.categories).tooltip, y: { formatter: val => val + '%' } } }"
              :series="trends.success_rate"
              height="320"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="6">
        <VCard :loading="loading">
          <VCardItem><VCardTitle>Ortalama Onay Süresi (dk)</VCardTitle></VCardItem>
          <VCardText v-if="trends.avg_time_min?.length">
            <VueApexCharts
              :options="{ ...baseLineOpts(trends.categories), tooltip: { ...baseLineOpts(trends.categories).tooltip, y: { formatter: val => val + ' dk' } } }"
              :series="trends.avg_time_min"
              height="300"
            />
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="6">
        <VCard :loading="loading">
          <VCardItem><VCardTitle>Günlük Yatırım Hacmi</VCardTitle></VCardItem>
          <VCardText v-if="trends.deposit_volume?.length">
            <VueApexCharts
              :options="{ ...baseLineOpts(trends.categories), yaxis: { labels: { style: { colors: labelColor, fontSize: '11px' }, formatter: val => formatMoney(val) } }, tooltip: { ...baseLineOpts(trends.categories).tooltip, y: { formatter: val => formatMoney(val) } } }"
              :series="trends.deposit_volume"
              height="300"
            />
          </VCardText>
        </VCard>
      </VCol>
    </template>

    <!-- Tab 2: Hourly density -->
    <template v-if="activeTab === 2">
      <VCol cols="12">
        <VCard :loading="loading">
          <VCardItem><VCardTitle>Saatlik Onay Yoğunluğu</VCardTitle></VCardItem>
          <VCardText v-if="hourly.series?.length">
            <VueApexCharts
              :options="{ ...baseBarOpts(hourly.categories), chart: { ...baseBarOpts(hourly.categories).chart, stacked: true } }"
              :series="hourly.series"
              height="380"
            />
          </VCardText>
        </VCard>
      </VCol>
    </template>
  </VRow>
</template>

<style scoped>
.cursor-pointer { cursor: pointer; user-select: none; }
.cursor-pointer:hover { background: rgba(var(--v-theme-on-surface), 0.04); }
</style>
