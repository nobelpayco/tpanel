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
const txType = ref('1')

const data = ref({ overall: { approved: 0, rejected: 0, total: 0, rate: 0, prev_rate: 0, delta_pp: 0 }, daily: { categories: [], rate: [] }, by_team: [], by_merchant: [] })
const loading = ref(false)

const teamSortBy = ref('total'); const teamSortDesc = ref(true)
const merchSortBy = ref('total'); const merchSortDesc = ref(true)

const fmtNum = v => Number(v || 0).toLocaleString('tr-TR')

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ date_from: dateFrom.value, date_to: dateTo.value, type: txType.value })
    const res = await fetch(`/api/conversion-reports?${params}`, { headers })
    if (res.ok) data.value = await res.json()
  } finally {
    loading.value = false
  }
}

onMounted(fetchData)

const setTeamSort = key => { if (teamSortBy.value === key) teamSortDesc.value = !teamSortDesc.value; else { teamSortBy.value = key; teamSortDesc.value = true } }
const setMerchSort = key => { if (merchSortBy.value === key) merchSortDesc.value = !merchSortDesc.value; else { merchSortBy.value = key; merchSortDesc.value = true } }

const sortRows = (rows, key, desc) => {
  return [...rows].sort((a, b) => {
    const av = a[key]; const bv = b[key]
    if (typeof av === 'string') return desc ? bv.localeCompare(av) : av.localeCompare(bv)
    return desc ? (bv ?? 0) - (av ?? 0) : (av ?? 0) - (bv ?? 0)
  })
}

const sortedTeams = computed(() => sortRows(data.value.by_team, teamSortBy.value, teamSortDesc.value))
const sortedMerchants = computed(() => sortRows(data.value.by_merchant, merchSortBy.value, merchSortDesc.value))

const rateColor = r => r >= 80 ? 'success' : r >= 60 ? 'warning' : 'error'

// Chart styling
const themeColors = computed(() => vuetifyTheme.current.value.colors)
const themeVars = computed(() => vuetifyTheme.current.value.variables)
const labelColor = computed(() => `rgba(${hexToRgb(themeColors.value['on-surface'])},${themeVars.value['disabled-opacity']})`)
const borderColor = computed(() => `rgba(${hexToRgb(String(themeVars.value['border-color']))},${themeVars.value['border-opacity']})`)
const isDark = computed(() => vuetifyTheme.global.name.value === 'dark')

const chartOpts = computed(() => ({
  chart: { type: 'area', toolbar: { show: false }, parentHeightOffset: 0, zoom: { enabled: false } },
  stroke: { width: 2, curve: 'smooth' },
  fill: { type: 'gradient', gradient: { shadeIntensity: 0.8, opacityFrom: 0.4, opacityTo: 0.1 } },
  dataLabels: { enabled: false },
  grid: { borderColor: borderColor.value, strokeDashArray: 4 },
  markers: { size: 3 },
  xaxis: { categories: data.value.daily.categories, labels: { style: { colors: labelColor.value, fontSize: '11px' }, rotate: -45, rotateAlways: data.value.daily.categories.length > 12 } },
  yaxis: { min: 0, max: 100, labels: { style: { colors: labelColor.value, fontSize: '11px' }, formatter: v => v + '%' } },
  tooltip: { theme: isDark.value ? 'dark' : 'light', y: { formatter: v => (v === null ? '-' : v + '%') } },
  legend: { show: false },
  colors: [themeColors.value.primary],
}))
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
          <VBtnToggle v-model="txType" mandatory density="compact" color="primary">
            <VBtn value="1">Yatırım</VBtn>
            <VBtn value="2">Çekim</VBtn>
          </VBtnToggle>
          <VBtn color="primary" :loading="loading" @click="fetchData">{{ t('common.filter') }}</VBtn>
        </VCardText>
      </VCard>
    </VCol>

    <!-- KPI Cards -->
    <VCol cols="12" md="4">
      <VCard color="success" variant="tonal">
        <VCardText class="d-flex align-center gap-3">
          <VIcon icon="tabler-circle-check" size="36" />
          <div>
            <div class="text-caption">Toplam Onaylanan</div>
            <div class="text-h5 font-weight-bold">{{ fmtNum(data.overall.approved) }}</div>
          </div>
        </VCardText>
      </VCard>
    </VCol>
    <VCol cols="12" md="4">
      <VCard color="error" variant="tonal">
        <VCardText class="d-flex align-center gap-3">
          <VIcon icon="tabler-circle-x" size="36" />
          <div>
            <div class="text-caption">Toplam Reddedilen</div>
            <div class="text-h5 font-weight-bold">{{ fmtNum(data.overall.rejected) }}</div>
          </div>
        </VCardText>
      </VCard>
    </VCol>
    <VCol cols="12" md="4">
      <VCard :color="rateColor(data.overall.rate)" variant="tonal">
        <VCardText class="d-flex align-center justify-space-between gap-3">
          <div class="d-flex align-center gap-3">
            <VIcon icon="tabler-target-arrow" size="36" />
            <div>
              <div class="text-caption">Genel Başarı Oranı</div>
              <div class="text-h4 font-weight-bold">%{{ data.overall.rate }}</div>
            </div>
          </div>
          <VChip
            v-if="data.overall.prev_rate > 0 || data.overall.delta_pp !== 0"
            size="small"
            :color="data.overall.delta_pp >= 0 ? 'success' : 'error'"
            variant="elevated"
          >
            <VIcon :icon="data.overall.delta_pp >= 0 ? 'tabler-trending-up' : 'tabler-trending-down'" start size="14" />
            {{ data.overall.delta_pp >= 0 ? '+' : '' }}{{ data.overall.delta_pp }} pp
          </VChip>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Daily Trend -->
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem><VCardTitle>Günlük Başarı Oranı Trendi</VCardTitle></VCardItem>
        <VCardText v-if="data.daily.categories.length">
          <VueApexCharts
            :options="chartOpts"
            :series="[{ name: 'Başarı Oranı', data: data.daily.rate }]"
            height="280"
          />
        </VCardText>
        <VCardText v-else class="text-center text-medium-emphasis py-6">{{ t('common.no_data') }}</VCardText>
      </VCard>
    </VCol>

    <!-- Per Team -->
    <VCol cols="12" md="6">
      <VCard :loading="loading">
        <VCardItem><VCardTitle>Takım Bazlı</VCardTitle></VCardItem>
        <VCardText>
          <VTable density="compact" hover class="text-no-wrap">
            <thead>
              <tr>
                <th class="cursor-pointer" @click="setTeamSort('name')">Takım <VIcon v-if="teamSortBy === 'name'" :icon="teamSortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                <th class="text-end cursor-pointer" @click="setTeamSort('approved')">Onay <VIcon v-if="teamSortBy === 'approved'" :icon="teamSortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                <th class="text-end cursor-pointer" @click="setTeamSort('rejected')">Red <VIcon v-if="teamSortBy === 'rejected'" :icon="teamSortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                <th class="text-end cursor-pointer" @click="setTeamSort('total')">Toplam <VIcon v-if="teamSortBy === 'total'" :icon="teamSortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                <th class="text-end cursor-pointer" @click="setTeamSort('rate')">Oran <VIcon v-if="teamSortBy === 'rate'" :icon="teamSortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="r in sortedTeams" :key="r.id">
                <td class="font-weight-medium">{{ r.name }}</td>
                <td class="text-end text-success">{{ fmtNum(r.approved) }}</td>
                <td class="text-end text-error">{{ fmtNum(r.rejected) }}</td>
                <td class="text-end">{{ fmtNum(r.total) }}</td>
                <td class="text-end">
                  <VChip size="x-small" :color="rateColor(r.rate)" variant="tonal">%{{ r.rate }}</VChip>
                </td>
              </tr>
              <tr v-if="!sortedTeams.length">
                <td colspan="5" class="text-center text-medium-emphasis py-3">{{ t('common.no_data') }}</td>
              </tr>
            </tbody>
          </VTable>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Per Merchant -->
    <VCol cols="12" md="6">
      <VCard :loading="loading">
        <VCardItem><VCardTitle>Merchant Bazlı</VCardTitle></VCardItem>
        <VCardText>
          <VTable density="compact" hover class="text-no-wrap">
            <thead>
              <tr>
                <th class="cursor-pointer" @click="setMerchSort('name')">Merchant <VIcon v-if="merchSortBy === 'name'" :icon="merchSortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                <th class="text-end cursor-pointer" @click="setMerchSort('approved')">Onay <VIcon v-if="merchSortBy === 'approved'" :icon="merchSortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                <th class="text-end cursor-pointer" @click="setMerchSort('rejected')">Red <VIcon v-if="merchSortBy === 'rejected'" :icon="merchSortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                <th class="text-end cursor-pointer" @click="setMerchSort('total')">Toplam <VIcon v-if="merchSortBy === 'total'" :icon="merchSortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
                <th class="text-end cursor-pointer" @click="setMerchSort('rate')">Oran <VIcon v-if="merchSortBy === 'rate'" :icon="merchSortDesc ? 'tabler-chevron-down' : 'tabler-chevron-up'" size="14" /></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(r, i) in sortedMerchants" :key="i">
                <td class="font-weight-medium">{{ r.name }}</td>
                <td class="text-end text-success">{{ fmtNum(r.approved) }}</td>
                <td class="text-end text-error">{{ fmtNum(r.rejected) }}</td>
                <td class="text-end">{{ fmtNum(r.total) }}</td>
                <td class="text-end">
                  <VChip size="x-small" :color="rateColor(r.rate)" variant="tonal">%{{ r.rate }}</VChip>
                </td>
              </tr>
              <tr v-if="!sortedMerchants.length">
                <td colspan="5" class="text-center text-medium-emphasis py-3">{{ t('common.no_data') }}</td>
              </tr>
            </tbody>
          </VTable>
        </VCardText>
      </VCard>
    </VCol>
  </VRow>
</template>

<style scoped>
.cursor-pointer { cursor: pointer; user-select: none; }
.cursor-pointer:hover { background: rgba(var(--v-theme-on-surface), 0.04); }
</style>
