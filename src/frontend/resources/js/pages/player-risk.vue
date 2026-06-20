<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'
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
const yearStart = (() => { const d = new Date(); return `${d.getFullYear()}-01-01` })()
const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({ dateFormat: 'Y-m-d', altInput: true, altFormat: 'd.m.Y', locale: localeMap[locale.value] || Turkish }))

const dateFrom = ref(yearStart)
const dateTo = ref(today)
const merchantId = ref(null)
const merchants = ref([])
const activeTab = ref(0)

const suspiciousData = ref(null)
const segmentData = ref(null)
const multiNameData = ref(null)
const suspiciousLoading = ref(false)
const segmentLoading = ref(false)
const multiNameLoading = ref(false)
const tabLoaded = ref({})

const snackbar = useSnackbar()
const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 0 })
const formatMoney2 = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const formatDuration = (seconds) => {
  if (seconds === null || seconds === undefined) return '-'
  const min = Math.floor(seconds / 60)
  const sec = Math.round(seconds % 60)
  return `${min}dk ${sec}sn`
}
const formatDate = val => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}

// Player detail dialog
const showPlayerDialog = ref(false)
const playerLoading = ref(false)
const playerData = ref(null)
const playerTxLoading = ref(false)
const playerTxData = ref({ items: [], total: 0, page: 1, last_page: 1 })

const openPlayerDetail = async (playerId) => {
  showPlayerDialog.value = true
  playerLoading.value = true
  playerData.value = null
  playerTxData.value = { items: [], total: 0, page: 1, last_page: 1 }
  try {
    const res = await fetch(`/api/dashboard/player-stats/${encodeURIComponent(playerId)}`, { headers })
    if (res.ok) {
      playerData.value = await res.json()
      await fetchPlayerTransactions(playerId, 1)
    }
  } finally { playerLoading.value = false }
}

const fetchPlayerTransactions = async (playerId, page) => {
  playerTxLoading.value = true
  try {
    const res = await fetch(`/api/dashboard/player-transactions/${encodeURIComponent(playerId)}?page=${page}`, { headers })
    if (res.ok) playerTxData.value = await res.json()
  } finally { playerTxLoading.value = false }
}

const changePlayerTxPage = (page) => {
  if (playerData.value) fetchPlayerTransactions(playerData.value.player_id, page)
}

const resolveStatusColor = status => ({ 0: 'secondary', 1: 'warning', 2: 'info', 3: 'success', 4: 'error' }[status] || 'secondary')
const resolveStatusText = status => ({ 0: 'status.cancelled', 1: 'status.pending', 2: 'status.processing', 3: 'status.approved', 4: 'status.rejected' }[status] || 'status.pending')

const themeColors = computed(() => vuetifyTheme.current.value.colors)
const themeVars = computed(() => vuetifyTheme.current.value.variables)
const labelColor = computed(() => `rgba(${hexToRgb(themeColors.value['on-surface'])},${themeVars.value['disabled-opacity']})`)
const isDark = computed(() => vuetifyTheme.global.name.value === 'dark')

const fetchMerchants = async () => {
  const res = await fetch('/api/merchant-reports/filter-options', { headers })
  if (res.ok) merchants.value = await res.json()
}

const buildParams = () => {
  const params = new URLSearchParams({ date_from: dateFrom.value, date_to: dateTo.value })
  if (merchantId.value) params.append('merchant_id', merchantId.value)
  return params
}

const fetchSuspicious = async () => {
  suspiciousLoading.value = true
  try {
    const res = await fetch(`/api/player-risk/suspicious?${buildParams()}`, { headers })
    if (res.ok) { suspiciousData.value = await res.json(); tabLoaded.value[0] = true }
  } finally { suspiciousLoading.value = false }
}

const fetchSegmentation = async () => {
  segmentLoading.value = true
  try {
    const res = await fetch(`/api/player-risk/segmentation?${buildParams()}`, { headers })
    if (res.ok) { segmentData.value = await res.json(); tabLoaded.value[1] = true }
  } finally { segmentLoading.value = false }
}

const fetchMultiName = async () => {
  multiNameLoading.value = true
  try {
    const res = await fetch(`/api/player-risk/multi-name?${buildParams()}`, { headers })
    if (res.ok) { multiNameData.value = await res.json(); tabLoaded.value[2] = true }
  } finally { multiNameLoading.value = false }
}

const applyFilter = () => {
  tabLoaded.value = {}
  if (activeTab.value === 0) fetchSuspicious()
  else if (activeTab.value === 1) fetchSegmentation()
  else fetchMultiName()
}

watch(activeTab, (val) => {
  if (val === 0 && !tabLoaded.value[0]) fetchSuspicious()
  if (val === 1 && !tabLoaded.value[1]) fetchSegmentation()
  if (val === 2 && !tabLoaded.value[2]) fetchMultiName()
})

onMounted(() => { fetchMerchants(); fetchSuspicious() })

const addToBlacklist = async (player) => {
  let desc = ''
  if (player.risk_score && player.flags) {
    desc = `Risk score: ${player.risk_score}. Flags: ${player.flags.join(', ')}`
  } else if (player.name_count) {
    desc = `${player.name_count} farklı isim kullanıldı`
  }
  const res = await fetch('/api/blacklist', {
    method: 'POST', headers,
    body: JSON.stringify({ type: 1, val: String(player.player_id), desc }),
  })
  const data = await res.json()
  if (res.ok) {
    player.is_blacklisted = true
    snackbar.success(data.message)
  } else {
    snackbar.handleError(data)
  }
}

const removeFromBlacklist = async (player) => {
  // Önce blacklist ID'sini bul
  const res = await fetch(`/api/blacklist?search=${encodeURIComponent(player.player_id)}`, { headers })
  if (res.ok) {
    const items = await res.json()
    const item = items.find(i => i.val === String(player.player_id))
    if (item) {
      const delRes = await fetch(`/api/blacklist/${item.id}`, { method: 'DELETE', headers })
      const data = await delRes.json()
      if (delRes.ok) {
        player.is_blacklisted = false
        snackbar.success(data.message)
      } else {
        snackbar.handleError(data)
      }
    }
  }
}

const riskColor = score => score >= 80 ? 'error' : score >= 60 ? 'warning' : score >= 40 ? 'info' : 'success'

const flagLabels = {
  low_amount: 'player_risk.low_amount',
  same_amount: 'player_risk.same_amount',
  night_activity: 'player_risk.night_activity',
  high_rejection: 'player_risk.high_rejection',
  rapid_fire: 'player_risk.rapid_fire',
}

const segmentColors = { VIP: 'primary', Active: 'success', Normal: 'info', New: 'warning', Risky: 'error', Inactive: 'secondary' }
const segmentLabelMap = { VIP: 'vip', Active: 'active', Normal: 'normal', New: 'new_player', Risky: 'risky', Inactive: 'inactive' }
const segmentLabel = (name) => {
  const key = segmentLabelMap[name]
  return key ? t('player_risk.' + key) : name
}

const segmentChartOpts = computed(() => ({
  chart: { type: 'donut' },
  labels: (segmentData.value?.chart?.labels || []).map(l => segmentLabel(l)),
  colors: [themeColors.value.primary, themeColors.value.success, themeColors.value.info, themeColors.value.warning, themeColors.value.error, themeColors.value.secondary],
  legend: { position: 'bottom', fontFamily: 'Public Sans', labels: { colors: labelColor.value } },
  dataLabels: { enabled: true, formatter: (val) => Math.round(val) + '%' },
  plotOptions: { pie: { donut: { size: '60%' } } },
  tooltip: { theme: isDark.value ? 'dark' : 'light' },
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
          <VTab><VIcon icon="tabler-alert-triangle" size="18" class="me-1" />{{ t('player_risk.suspicious') }}</VTab>
          <VTab><VIcon icon="tabler-chart-pie" size="18" class="me-1" />{{ t('player_risk.segmentation') }}</VTab>
          <VTab><VIcon icon="tabler-users" size="18" class="me-1" />Çoklu İsim</VTab>
        </VTabs>
      </VCard>
    </VCol>

    <!-- Tab 0: Şüpheli Oyuncular -->
    <template v-if="activeTab === 0">
      <!-- Özet -->
      <VCol v-if="suspiciousData?.summary" cols="12">
        <VCard :loading="suspiciousLoading">
          <VCardText class="d-flex gap-4 flex-wrap">
            <div class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-error), 0.08); min-width: 120px;">
              <div class="text-h5 font-weight-bold text-error">{{ suspiciousData.summary.total_risky }}</div>
              <div class="text-caption">{{ t('player_risk.total_risky') }}</div>
            </div>
            <div v-for="(count, flag) in suspiciousData.summary.by_flag" :key="flag" class="text-center pa-3 rounded" style="background: rgba(var(--v-theme-warning), 0.08); min-width: 100px;">
              <div class="text-h6 font-weight-bold">{{ count }}</div>
              <div class="text-caption">{{ t(flagLabels[flag] || flag) }}</div>
            </div>
          </VCardText>
        </VCard>
      </VCol>

      <!-- Tablo -->
      <VCol cols="12">
        <VCard :loading="suspiciousLoading">
          <VTable class="text-no-wrap" density="compact">
            <thead>
              <tr>
                <th>{{ t('merchant_report.player_id') }}</th>
                <th class="text-center">{{ t('player_risk.risk_score') }}</th>
                <th class="text-center">{{ t('dashboard.approved') }}</th>
                <th class="text-center">{{ t('dashboard.rejected') }}</th>
                <th class="text-end">{{ t('merchant_report.total_amount') }}</th>
                <th class="text-end">{{ t('merchant_report.avg_amount') }}</th>
                <th>{{ t('player_risk.flags') }}</th>
                <th class="text-center">BL</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="p in suspiciousData?.players" :key="p.player_id" class="hover-row">
                <td class="font-weight-medium">
                  <a class="cursor-pointer text-primary" @click="openPlayerDetail(p.player_id)">{{ p.player_id }}</a>
                </td>
                <td class="text-center">
                  <VChip :color="riskColor(p.risk_score)" label size="small">{{ p.risk_score }}</VChip>
                </td>
                <td class="text-center text-success">{{ p.approved }}</td>
                <td class="text-center text-error">{{ p.rejected }}</td>
                <td class="text-end">{{ formatMoney(p.total_amount) }}</td>
                <td class="text-end">{{ formatMoney(p.avg_amount) }}</td>
                <td>
                  <VChip v-for="f in p.flags" :key="f" color="warning" variant="tonal" label size="x-small" class="me-1">
                    {{ t(flagLabels[f] || f) }}
                  </VChip>
                </td>
                <td class="text-center">
                  <VBtn v-if="p.is_blacklisted" icon size="x-small" variant="text" color="success" @click="removeFromBlacklist(p)">
                    <VIcon icon="tabler-shield-check" size="18" />
                    <VTooltip activator="parent" location="top">BL'den Çıkar</VTooltip>
                  </VBtn>
                  <VBtn v-else icon size="x-small" variant="text" color="error" @click="addToBlacklist(p)">
                    <VIcon icon="tabler-ban" size="18" />
                    <VTooltip activator="parent" location="top">BL'ye Ekle</VTooltip>
                  </VBtn>
                </td>
              </tr>
              <tr v-if="!suspiciousLoading && (!suspiciousData?.players || suspiciousData.players.length === 0)">
                <td colspan="8" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
              </tr>
            </tbody>
          </VTable>
        </VCard>
      </VCol>
    </template>

    <!-- Tab 1: Segmentasyon -->
    <template v-if="activeTab === 1">
      <VCol cols="12" md="5">
        <VCard :loading="segmentLoading">
          <VCardItem><VCardTitle>{{ t('player_risk.segmentation') }}</VCardTitle></VCardItem>
          <VCardText v-if="segmentData?.chart?.counts?.length > 0">
            <VueApexCharts :options="segmentChartOpts" :series="segmentData.chart.counts" height="300" />
          </VCardText>
        </VCard>
      </VCol>
      <VCol cols="12" md="7">
        <VCard :loading="segmentLoading">
          <VTable class="text-no-wrap" density="compact">
            <thead>
              <tr>
                <th>{{ t('player_risk.segment') }}</th>
                <th class="text-center">{{ t('player_risk.player_count') }}</th>
                <th class="text-end">{{ t('merchant_report.total_amount') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="seg in segmentData?.segments" :key="seg.name">
                <td>
                  <VChip :color="segmentColors[seg.name] || 'primary'" label size="small">{{ segmentLabel(seg.name) }}</VChip>
                </td>
                <td class="text-center font-weight-medium">{{ seg.count }}</td>
                <td class="text-end font-weight-medium">{{ formatMoney(seg.total_amount) }}</td>
              </tr>
            </tbody>
          </VTable>

          <!-- Segment detay: Top oyuncular -->
          <VDivider v-if="segmentData?.segments" />
          <VCardText v-for="seg in segmentData?.segments?.filter(s => s.players?.length > 0)" :key="seg.name">
            <div class="text-body-2 text-medium-emphasis mb-2">
              <VChip :color="segmentColors[seg.name] || 'primary'" label size="x-small" class="me-1">{{ seg.name }}</VChip>
              Top {{ seg.players.length }}
            </div>
            <VTable density="compact" class="text-no-wrap">
              <tbody>
                <tr v-for="p in seg.players" :key="p.player_id">
                  <td class="text-body-2">
                    <a class="cursor-pointer text-primary" @click="openPlayerDetail(p.player_id)">{{ p.player_id }}</a>
                  </td>
                  <td class="text-end text-body-2">{{ p.total_count || p.approved_count || 0 }} {{ t('merchant_report.tx_count') }}</td>
                  <td class="text-end font-weight-medium">{{ formatMoney(p.approved_amount || p.total_amount || 0) }}</td>
                </tr>
              </tbody>
            </VTable>
          </VCardText>
        </VCard>
      </VCol>
    </template>

    <!-- Tab 2: Çoklu İsim -->
    <template v-if="activeTab === 2">
      <VCol cols="12">
        <VCard :loading="multiNameLoading">
          <VCardItem>
            <VCardTitle>Aynı Player ID, Farklı İsim</VCardTitle>
            <VCardSubtitle>Aynı oyuncu ID'sine sahip ama farklı ad/soyad ile gönderilen işlemler</VCardSubtitle>
          </VCardItem>
          <VDivider />
          <VTable class="text-no-wrap" density="compact">
            <thead>
              <tr>
                <th>{{ t('merchant_report.player_id') }}</th>
                <th class="text-center">İsim Sayısı</th>
                <th class="text-center">{{ t('dashboard.approved') }}</th>
                <th class="text-center">{{ t('dashboard.rejected') }}</th>
                <th class="text-end">{{ t('merchant_report.total_amount') }}</th>
                <th>İsimler</th>
                <th class="text-center">BL</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="p in multiNameData?.players" :key="p.player_id" class="hover-row">
                <td class="font-weight-medium">
                  <a class="cursor-pointer text-primary" @click="openPlayerDetail(p.player_id)">{{ p.player_id }}</a>
                </td>
                <td class="text-center">
                  <VChip color="warning" label size="small">{{ p.name_count }}</VChip>
                </td>
                <td class="text-center text-success">{{ p.approved_count }}</td>
                <td class="text-center text-error">{{ p.rejected_count }}</td>
                <td class="text-end font-weight-medium">{{ formatMoney(p.approved_amount) }}</td>
                <td>
                  <div v-for="(n, i) in p.names" :key="i" class="text-body-2">
                    <span class="font-weight-medium">{{ n.name }}</span>
                    <span class="text-caption text-medium-emphasis ms-1">({{ n.count }})</span>
                  </div>
                </td>
                <td class="text-center">
                  <VBtn v-if="p.is_blacklisted" icon size="x-small" variant="text" color="success" @click="removeFromBlacklist(p)">
                    <VIcon icon="tabler-shield-check" size="18" />
                  </VBtn>
                  <VBtn v-else icon size="x-small" variant="text" color="error" @click="addToBlacklist(p)">
                    <VIcon icon="tabler-ban" size="18" />
                  </VBtn>
                </td>
              </tr>
              <tr v-if="!multiNameLoading && (!multiNameData?.players || multiNameData.players.length === 0)">
                <td colspan="7" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
              </tr>
            </tbody>
          </VTable>
        </VCard>
      </VCol>
    </template>
  </VRow>

  <!-- Player Detail Dialog -->
  <VDialog v-model="showPlayerDialog" max-width="900">
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
              <div class="text-h5 font-weight-bold">%{{ playerData.summary.approval_rate }}</div>
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
        </VRow>

        <!-- Tüm işlemler (sayfalama ile) -->
        <div class="text-body-2 text-medium-emphasis mb-2">
          {{ t('dashboard.recent_transactions') }}
          <span class="ms-2">({{ playerTxData.total }} {{ t('common.total') }})</span>
        </div>
        <VCard variant="outlined" :loading="playerTxLoading">
          <VTable density="compact" class="text-no-wrap">
            <thead>
              <tr>
                <th>ID</th>
                <th>Tür</th>
                <th>{{ t('deposits.sender') }}</th>
                <th class="text-end">{{ t('deposits.amount') }}</th>
                <th>{{ t('deposits.status') }}</th>
                <th>{{ t('dashboard.avg_time') }}</th>
                <th>{{ t('deposits.date') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="tx in playerTxData.items" :key="tx.id">
                <td class="text-body-2">#{{ tx.id }}</td>
                <td>
                  <VChip :color="tx.type === 1 ? 'success' : 'error'" variant="tonal" label size="x-small">
                    {{ tx.type === 1 ? 'Yatırım' : 'Çekim' }}
                  </VChip>
                </td>
                <td class="text-body-2">{{ tx.name || '-' }}</td>
                <td class="text-end font-weight-medium">{{ formatMoney(tx.amount) }}</td>
                <td>
                  <VChip :color="resolveStatusColor(tx.status)" label size="x-small">
                    {{ t(resolveStatusText(tx.status)) }}
                  </VChip>
                </td>
                <td class="text-body-2 text-medium-emphasis">{{ formatDuration(tx.duration) }}</td>
                <td class="text-body-2 text-medium-emphasis">{{ formatDate(tx.date) }}</td>
              </tr>
            </tbody>
          </VTable>
          <VDivider v-if="playerTxData.last_page > 1" />
          <VCardText v-if="playerTxData.last_page > 1" class="d-flex justify-center">
            <VPagination
              :model-value="playerTxData.page"
              :length="playerTxData.last_page"
              :total-visible="7"
              density="compact"
              @update:model-value="changePlayerTxPage"
            />
          </VCardText>
        </VCard>
      </VCardText>

      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showPlayerDialog = false">{{ t('common.cancel') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>

<style scoped>
.hover-row:hover {
  background: rgba(var(--v-theme-primary), 0.06);
}
</style>
