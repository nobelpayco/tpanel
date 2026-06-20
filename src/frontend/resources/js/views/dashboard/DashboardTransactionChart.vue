<script setup>
import { useTheme } from 'vuetify'
import { hexToRgb } from '@layouts/utils'
import { useI18n } from 'vue-i18n'

const props = defineProps({
  dateFrom: { type: String, required: true },
  dateTo: { type: String, required: true },
})

const { t } = useI18n()
const vuetifyTheme = useTheme()

const loading = ref(true)
const refreshing = ref(false)
const volumeData = ref({ days: [], deposits: [], withdrawals: [] })

const fetchData = async () => {
  try {
    const token = localStorage.getItem('token')
    const params = new URLSearchParams({ date_from: props.dateFrom, date_to: props.dateTo })
    const res = await fetch(`/api/dashboard/yearly-volume?${params}`, {
      headers: { 'Accept': 'application/json', 'Authorization': `Bearer ${token}` },
    })
    if (res.ok) volumeData.value = await res.json()
  } finally {
    loading.value = false
    refreshing.value = false
  }
}

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const isToday = computed(() => props.dateFrom === today && props.dateTo === today)

let interval
onMounted(() => {
  fetchData()
  interval = setInterval(() => {
    if (isToday.value) { refreshing.value = true; fetchData() }
  }, 10000)
})
onUnmounted(() => clearInterval(interval))

const series = computed(() => [
  { name: t('dashboard.total_deposits'), data: volumeData.value.deposits },
  { name: t('dashboard.total_withdrawals'), data: volumeData.value.withdrawals },
])

const chartOptions = computed(() => {
  const currentTheme = vuetifyTheme.current.value.colors
  const variableTheme = vuetifyTheme.current.value.variables
  const borderColor = `rgba(${hexToRgb(String(variableTheme['border-color']))},${variableTheme['border-opacity']})`
  const labelColor = `rgba(${hexToRgb(currentTheme['on-surface'])},${variableTheme['disabled-opacity']})`

  return {
    chart: { parentHeightOffset: 0, type: 'area', toolbar: { show: false } },
    colors: [currentTheme.success, currentTheme.error],
    fill: {
      type: 'gradient',
      gradient: { shadeIntensity: 0.8, opacityFrom: 0.4, opacityTo: 0.1, stops: [0, 95, 100] },
    },
    stroke: { width: 2, curve: 'smooth' },
    dataLabels: { enabled: false },
    legend: {
      position: 'top',
      horizontalAlign: 'left',
      labels: { colors: labelColor },
      fontFamily: 'Public Sans',
      markers: { offsetX: -3 },
      itemMargin: { horizontal: 10 },
    },
    grid: { show: true, borderColor, padding: { top: 0, bottom: -8, left: 10, right: 10 } },
    xaxis: {
      categories: volumeData.value.days,
      axisBorder: { show: false },
      axisTicks: { show: false },
      labels: { style: { colors: labelColor, fontSize: '13px', fontFamily: 'Public Sans' } },
    },
    yaxis: {
      labels: {
        formatter: val => '₺' + Number(val).toLocaleString('tr-TR'),
        style: { colors: labelColor, fontSize: '13px', fontFamily: 'Public Sans' },
      },
    },
    tooltip: {
      shared: true,
      theme: vuetifyTheme.global.name.value === 'dark' ? 'dark' : 'light',
      y: { formatter: val => '₺' + Number(val).toLocaleString('tr-TR') },
    },
  }
})
</script>

<template>
  <VCard :loading="loading || refreshing">
    <VCardItem>
      <VCardTitle>{{ t('dashboard.transaction_volume') }}</VCardTitle>
      <VCardSubtitle>{{ t('dashboard.last_30_days') }}</VCardSubtitle>
    </VCardItem>
    <VCardText>
      <VueApexCharts
        v-if="volumeData.deposits.length"
        :options="chartOptions"
        :series="series"
        height="300"
      />
    </VCardText>
  </VCard>
</template>
