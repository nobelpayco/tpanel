<script setup>
import { useI18n } from 'vue-i18n'
import { useTheme } from 'vuetify'
import { hexToRgb } from '@layouts/utils'

const props = defineProps({
  dateFrom: { type: String, required: true },
  dateTo: { type: String, required: true },
})

const { t } = useI18n()
const vuetifyTheme = useTheme()

const loading = ref(true)
const refreshing = ref(false)
const transactions = ref([])

// Player stats dialog
const showPlayerDialog = ref(false)
const playerLoading = ref(false)
const playerData = ref(null)

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 0 })
const formatMoney2 = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })

const fetchData = async () => {
  try {
    const token = localStorage.getItem('token')
    const params = new URLSearchParams({ date_from: props.dateFrom, date_to: props.dateTo })
    const res = await fetch(`/api/dashboard/recent-transactions?${params}`, {
      headers: { 'Accept': 'application/json', 'Authorization': `Bearer ${token}` },
    })
    if (res.ok) transactions.value = await res.json()
  } finally {
    loading.value = false
    refreshing.value = false
  }
}

const countdown = ref(10)
let interval
let countdownInterval

onMounted(() => {
  fetchData()
  interval = setInterval(() => { refreshing.value = true; fetchData(); countdown.value = 10 }, 10000)
  countdownInterval = setInterval(() => { if (countdown.value > 0) countdown.value-- }, 1000)
})
onUnmounted(() => { clearInterval(interval); clearInterval(countdownInterval) })

const resolveStatusColor = status => {
  const map = { 0: 'secondary', 1: 'warning', 2: 'info', 3: 'success', 4: 'error' }
  return map[status] || 'secondary'
}

const resolveStatusText = status => {
  const map = { 0: 'status.cancelled', 1: 'status.pending', 2: 'status.processing', 3: 'status.approved', 4: 'status.rejected' }
  return map[status] || 'status.pending'
}

const resolveTypeIcon = type => type === 1 ? 'tabler-arrow-bar-to-down' : 'tabler-arrow-bar-up'
const resolveTypeColor = type => type === 1 ? 'success' : 'error'
const trustColor = rate => rate >= 80 ? 'success' : rate >= 60 ? 'info' : rate >= 45 ? 'warning' : 'error'

const formatDuration = (seconds) => {
  if (seconds === null || seconds === undefined) return '-'
  const min = Math.floor(seconds / 60)
  const sec = seconds % 60
  return `${min}dk ${sec}sn`
}

const formatDate = val => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}

const isOverdue = (tx) => {
  if (!tx.created_at_raw || ![1, 2].includes(tx.status)) return false
  const elapsed = (Date.now() - new Date(tx.created_at_raw).getTime()) / 1000
  return elapsed > 600 // 10 dakika
}

const user = JSON.parse(localStorage.getItem('user') || '{}')
const isTeamMember = [2, 5].includes(user.user_type)

// Player stats
const openPlayerStats = async (tx) => {
  if (!tx.player_id || tx.type !== 1) return
  showPlayerDialog.value = true
  playerLoading.value = true
  playerData.value = null
  try {
    const token = localStorage.getItem('token')
    const res = await fetch(`/api/dashboard/player-stats/${encodeURIComponent(tx.player_id)}`, {
      headers: { 'Accept': 'application/json', 'Authorization': `Bearer ${token}` },
    })
    if (res.ok) playerData.value = await res.json()
  } finally {
    playerLoading.value = false
  }
}

// Charts
const dailyChartOptions = computed(() => {
  if (!playerData.value) return {}
  const currentTheme = vuetifyTheme.current.value.colors
  const variableTheme = vuetifyTheme.current.value.variables
  const labelColor = `rgba(${hexToRgb(currentTheme['on-surface'])},${variableTheme['disabled-opacity']})`

  return {
    chart: { type: 'bar', toolbar: { show: false }, stacked: true },
    colors: [currentTheme.success, currentTheme.error],
    plotOptions: { bar: { columnWidth: '60%', borderRadius: 3 } },
    dataLabels: { enabled: false },
    legend: { position: 'top', labels: { colors: labelColor }, fontFamily: 'Public Sans' },
    grid: { show: true, borderColor: `rgba(${hexToRgb(String(variableTheme['border-color']))},${variableTheme['border-opacity']})` },
    xaxis: {
      categories: playerData.value.daily_chart.days,
      labels: { style: { colors: labelColor, fontSize: '11px', fontFamily: 'Public Sans' } },
    },
    yaxis: { labels: { style: { colors: labelColor, fontSize: '11px', fontFamily: 'Public Sans' } } },
    tooltip: { theme: vuetifyTheme.global.name.value === 'dark' ? 'dark' : 'light' },
  }
})

const dailyChartSeries = computed(() => {
  if (!playerData.value) return []
  return [
    { name: t('dashboard.approved'), data: playerData.value.daily_chart.approved },
    { name: t('dashboard.rejected'), data: playerData.value.daily_chart.rejected },
  ]
})

const amountChartOptions = computed(() => {
  if (!playerData.value) return {}
  const currentTheme = vuetifyTheme.current.value.colors

  return {
    chart: { type: 'donut' },
    labels: playerData.value.amount_ranges.map(r => r.range_label),
    colors: [currentTheme.info, currentTheme.success, currentTheme.warning, currentTheme.primary, currentTheme.error, currentTheme.secondary],
    legend: { position: 'bottom', fontFamily: 'Public Sans' },
    dataLabels: { enabled: true, formatter: (val) => Math.round(val) + '%' },
    plotOptions: { pie: { donut: { size: '60%' } } },
    tooltip: { theme: vuetifyTheme.global.name.value === 'dark' ? 'dark' : 'light' },
  }
})

const amountChartSeries = computed(() => {
  if (!playerData.value) return []
  return playerData.value.amount_ranges.map(r => r.cnt)
})
</script>

<template>
  <VCard :loading="loading || refreshing">
    <VCardItem>
      <VCardTitle>{{ t('dashboard.recent_transactions') }}</VCardTitle>
      <template #append>
        <VChip color="primary" variant="tonal" label size="small">
          {{ countdown }}s
        </VChip>
      </template>
    </VCardItem>
    <VDivider />
    <VTable class="text-no-wrap">
      <thead>
        <tr>
          <th>ID</th>
          <th>{{ t('deposits.sender') }}</th>
          <th>{{ t('deposits.amount') }}</th>
          <th v-if="!isTeamMember">{{ t('deposits.merchant') }}</th>
          <th>{{ t('deposits.team') }}</th>
          <th>{{ t('deposits.bank') }}</th>
          <th>{{ t('deposits.status') }}</th>
          <th>{{ t('dashboard.trust_rate') }}</th>
          <th>{{ t('dashboard.avg_time') }}</th>
          <th>{{ t('deposits.date') }}</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="tx in transactions"
          :key="tx.id"
          :style="isOverdue(tx) ? 'background: rgba(var(--v-theme-error), 0.12)' : ''"
        >
          <td>
            <div class="d-flex align-center gap-2">
              <VAvatar
                :color="resolveTypeColor(tx.type)"
                variant="tonal"
                size="30"
              >
                <VIcon
                  :icon="resolveTypeIcon(tx.type)"
                  size="18"
                />
              </VAvatar>
              <span class="text-body-2 font-weight-medium">#{{ tx.id }}</span>
            </div>
          </td>
          <td>{{ tx.name || '-' }}</td>
          <td class="font-weight-medium">
            {{ formatMoney(tx.amount) }}
          </td>
          <td v-if="!isTeamMember">{{ tx.merchant }}</td>
          <td>{{ tx.team }}</td>
          <td>{{ tx.bank }}</td>
          <td>
            <VChip
              :color="resolveStatusColor(tx.status)"
              label
              size="small"
            >
              {{ t(resolveStatusText(tx.status)) }}
            </VChip>
          </td>
          <td>
            <VChip
              v-if="tx.trust_rate !== null"
              :color="trustColor(tx.trust_rate)"
              label
              size="small"
              class="cursor-pointer"
              @click="openPlayerStats(tx)"
            >
              %{{ tx.trust_rate }}
            </VChip>
            <span v-else class="text-medium-emphasis">-</span>
          </td>
          <td class="text-medium-emphasis">
            {{ formatDuration(tx.duration) }}
          </td>
          <td class="text-medium-emphasis">
            {{ tx.date }}
          </td>
        </tr>
      </tbody>
    </VTable>
  </VCard>

  <!-- Player Stats Dialog -->
  <VDialog v-model="showPlayerDialog" max-width="850">
    <VCard :loading="playerLoading">
      <VCardItem v-if="playerData">
        <VCardTitle class="d-flex align-center gap-2">
          <VIcon icon="tabler-user-search" size="24" />
          {{ playerData.player_id }}
          <VChip v-if="playerData.is_blacklisted" color="error" label size="small" class="ms-2">
            <VIcon start icon="tabler-ban" size="14" />
            Blacklist
          </VChip>
        </VCardTitle>
      </VCardItem>
      <VDivider />

      <VCardText v-if="playerData">
        <!-- Özet kartları -->
        <VRow class="mb-4">
          <VCol cols="6" sm="3">
            <div class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-success), 0.08)">
              <div class="text-h5 font-weight-bold text-success">{{ playerData.summary.approved }}</div>
              <div class="text-caption text-medium-emphasis">{{ t('dashboard.approved') }}</div>
            </div>
          </VCol>
          <VCol cols="6" sm="3">
            <div class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-error), 0.08)">
              <div class="text-h5 font-weight-bold text-error">{{ playerData.summary.rejected }}</div>
              <div class="text-caption text-medium-emphasis">{{ t('dashboard.rejected') }}</div>
            </div>
          </VCol>
          <VCol cols="6" sm="3">
            <div class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-primary), 0.08)">
              <div class="text-h5 font-weight-bold" :class="`text-${trustColor(playerData.summary.approval_rate)}`">%{{ playerData.summary.approval_rate }}</div>
              <div class="text-caption text-medium-emphasis">{{ t('dashboard.approval_rate') }}</div>
            </div>
          </VCol>
          <VCol cols="6" sm="3">
            <div class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-info), 0.08)">
              <div class="text-h5 font-weight-bold text-info">{{ formatMoney2(playerData.summary.approved_amount) }}</div>
              <div class="text-caption text-medium-emphasis">{{ t('dashboard.total_deposits') }}</div>
            </div>
          </VCol>
        </VRow>

        <!-- Detay bilgiler -->
        <VRow class="mb-4">
          <VCol cols="12" sm="6">
            <VTable density="compact" class="text-no-wrap">
              <tbody>
                <tr>
                  <td class="text-medium-emphasis">{{ t('deposits.amount') }} (Ort.)</td>
                  <td class="text-end font-weight-medium">{{ formatMoney2(playerData.summary.avg_amount) }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">{{ t('deposits.amount') }} (Min)</td>
                  <td class="text-end font-weight-medium">{{ formatMoney2(playerData.summary.min_amount) }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">{{ t('deposits.amount') }} (Max)</td>
                  <td class="text-end font-weight-medium">{{ formatMoney2(playerData.summary.max_amount) }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">{{ t('dashboard.avg_time') }}</td>
                  <td class="text-end font-weight-medium">{{ formatDuration(playerData.summary.avg_approve_time) }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">{{ t('dashboard.first_tx') }}</td>
                  <td class="text-end font-weight-medium">{{ formatDate(playerData.summary.first_tx) }}</td>
                </tr>
                <tr>
                  <td class="text-medium-emphasis">{{ t('dashboard.last_tx') }}</td>
                  <td class="text-end font-weight-medium">{{ formatDate(playerData.summary.last_tx) }}</td>
                </tr>
              </tbody>
            </VTable>
          </VCol>
          <VCol cols="12" sm="6">
            <!-- Tutar dağılımı -->
            <div v-if="amountChartSeries.length > 0" class="text-center">
              <div class="text-body-2 text-medium-emphasis mb-2">{{ t('dashboard.amount_distribution') }}</div>
              <VueApexCharts :options="amountChartOptions" :series="amountChartSeries" height="200" />
            </div>
          </VCol>
        </VRow>

        <!-- Günlük grafik -->
        <div v-if="dailyChartSeries.length > 0 && playerData.daily_chart.days.length > 0" class="mb-4">
          <div class="text-body-2 text-medium-emphasis mb-2">{{ t('dashboard.last_30_days') }}</div>
          <VueApexCharts :options="dailyChartOptions" :series="dailyChartSeries" height="200" />
        </div>

        <!-- Son 10 işlem -->
        <div class="text-body-2 text-medium-emphasis mb-2">{{ t('dashboard.recent_transactions') }}</div>
        <VTable density="compact" class="text-no-wrap">
          <thead>
            <tr>
              <th>ID</th>
              <th>{{ t('deposits.sender') }}</th>
              <th class="text-end">{{ t('deposits.amount') }}</th>
              <th>{{ t('deposits.status') }}</th>
              <th>{{ t('dashboard.avg_time') }}</th>
              <th>{{ t('deposits.date') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="r in playerData.recent" :key="r.id">
              <td class="text-body-2">#{{ r.id }}</td>
              <td class="text-body-2">{{ r.name || '-' }}</td>
              <td class="text-end font-weight-medium">{{ formatMoney(r.amount) }}</td>
              <td>
                <VChip :color="resolveStatusColor(r.status)" label size="x-small">
                  {{ t(resolveStatusText(r.status)) }}
                </VChip>
              </td>
              <td class="text-body-2 text-medium-emphasis">{{ formatDuration(r.duration) }}</td>
              <td class="text-body-2 text-medium-emphasis">{{ formatDate(r.date) }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCardText>

      <VCardText v-else-if="!playerLoading" class="text-center text-medium-emphasis py-8">
        {{ t('common.no_data') }}
      </VCardText>

      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showPlayerDialog = false">{{ t('common.cancel') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
