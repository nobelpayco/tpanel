<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', roles: [1, 2, 4, 5] } })

const { t } = useI18n()
const { headers } = useApi()
const snackbar = useSnackbar()

const loading = ref(true)
const deposits = ref([])
const merchants = ref([])
const teams = ref([])
const autoReload = ref(true)
const countdown = ref(5)

// Filtreler
const filters = ref({ merchant: null, team: null, name: '', player_id: '', order_id: '', min_amount: null, max_amount: null })

// Dialogs
const showActionDialog = ref(false)
const selectedDeposit = ref(null)
const editAmount = ref(false)
const newAmount = ref(0)

const user = JSON.parse(localStorage.getItem('user') || '{}')
const isAdmin = [1, 4].includes(user.user_type)
const isTeamMember = [2, 5].includes(user.user_type)

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const formatIban = iban => iban ? iban.replace(/(.{4})/g, '$1 ').trim() : ''
// Banka kısa adı: yasal ekleri (A.Ş./T.A.Ş./T.A.O.) ve baştaki "Türkiye[ Cumhuriyeti]" kısmını at
const shortBank = (name) => {
  if (!name) return ''
  return name
    .replace(/\s*(T\.A\.Ş\.|A\.Ş\.|T\.A\.O\.|A\.O\.)\s*$/i, '')
    .replace(/^Türkiye Cumhuriyeti\s+/i, '')
    .replace(/^Türkiye\s+/i, '')
    .trim()
}
const formatDate = val => val ? new Date(val).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : '-'

const statusLabels = { 1: t('status.pending'), 2: t('status.processing') }
const statusColors = { 1: 'warning', 2: 'info' }

const timeSince = (dateStr) => {
  if (!dateStr) return ''
  const diff = Math.floor((Date.now() - new Date(dateStr).getTime()) / 1000)
  const min = Math.floor(diff / 60)
  const sec = diff % 60
  return `${min}dk ${sec}sn`
}

const knownIds = new Set()
let firstLoad = true

const playBeep = () => {
  try {
    const ctx = new (window.AudioContext || window.webkitAudioContext)()
    const osc = ctx.createOscillator()
    const gain = ctx.createGain()
    osc.connect(gain)
    gain.connect(ctx.destination)
    osc.frequency.value = 880
    osc.type = 'sine'
    gain.gain.setValueAtTime(0.0001, ctx.currentTime)
    gain.gain.exponentialRampToValueAtTime(0.3, ctx.currentTime + 0.02)
    gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.5)
    osc.start(ctx.currentTime)
    osc.stop(ctx.currentTime + 0.5)
    setTimeout(() => ctx.close(), 600)
  } catch (e) {}
}

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams()
    Object.entries(filters.value).forEach(([k, v]) => { if (v) params.append(k, v) })
    const res = await fetch(`/api/deposits/pending?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      // Yeni bekleyen (status=1) işlem var mı?
      let hasNew = false
      for (const d of data) {
        if (d.status === 1 && !knownIds.has(d.id)) {
          if (!firstLoad) hasNew = true
          knownIds.add(d.id)
        }
      }
      if (hasNew) playBeep()
      deposits.value = data
      firstLoad = false
    }
  } finally { loading.value = false }
}

const fetchMeta = async () => {
  const res = await fetch('/api/deposits/filter-meta', { headers })
  if (res.ok) {
    const data = await res.json()
    merchants.value = data.merchants
    teams.value = data.teams
  }
}

onMounted(() => {
  fetchData()
  fetchMeta()
})

// Otomatik yenileme — bu sayfada kapatılamaz, 10 saniyede bir
let countdownInterval
const startCountdown = () => {
  clearInterval(countdownInterval)
  countdown.value = 5
  countdownInterval = setInterval(() => {
    countdown.value--
    if (countdown.value <= 0) {
      fetchData()
      countdown.value = 5
    }
  }, 1000)
}
onMounted(startCountdown)
onUnmounted(() => clearInterval(countdownInterval))

// Aksiyonlar
const detailData = ref(null)
const detailLoading = ref(false)

const openReceipt = async (depositId) => {
  try {
    const res = await fetch(`/api/deposits/${depositId}/receipt`, { headers })
    if (!res.ok) { snackbar.error('Dekont yüklenemedi.'); return }
    const blob = await res.blob()
    const url = URL.createObjectURL(blob)
    const win = window.open(url, '_blank')
    // Yeni sekme açılırken blob URL'i temizleyemiyoruz; popup blocker varsa fallback
    if (!win) snackbar.error('Yeni sekme açılamadı, popup engelleyici olabilir.')
    setTimeout(() => URL.revokeObjectURL(url), 60_000)
  } catch (e) {
    snackbar.error('Dekont yüklenemedi.')
  }
}

const openAction = async (deposit) => {
  selectedDeposit.value = deposit
  newAmount.value = deposit.amount
  editAmount.value = false
  showActionDialog.value = true
  detailData.value = null
  detailLoading.value = true
  try {
    const res = await fetch(`/api/deposits/${deposit.id}/detail`, { headers })
    if (res.ok) detailData.value = await res.json()
  } finally { detailLoading.value = false }
}

const formatDuration = (dateStr) => {
  if (!dateStr) return '-'
  const diff = Math.floor((Date.now() - new Date(dateStr).getTime()) / 1000)
  if (diff < 60) return diff + ' Saniye'
  const min = Math.floor(diff / 60)
  if (min < 60) return min + ' Dakika ' + (diff % 60) + ' Saniye'
  const hr = Math.floor(min / 60)
  if (hr < 24) return hr + ' Saat ' + (min % 60) + ' Dakika'
  const day = Math.floor(hr / 24)
  return day + ' Gün ' + (hr % 24) + ' Saat'
}

const historyStatusLabel = (h) => {
  if (h.status == 3) return 'Onaylandı'
  if (h.status == 4) return 'Reddedildi'
  if (h.status == 1) return 'Beklemede'
  if (h.status == 2) return 'İşleme Alındı'
  return '-'
}
const historyStatusColor = (h) => {
  if (h.status == 3) return 'success'
  if (h.status == 4) return 'error'
  return 'warning'
}

const approveDeposit = async () => {
  const body = { id: selectedDeposit.value.id }
  if (editAmount.value) body.amount = newAmount.value

  const res = await fetch('/api/deposits/approve', { method: 'POST', headers, body: JSON.stringify(body) })
  const data = await res.json()
  if (res.ok) { snackbar.success(data.message); showActionDialog.value = false; fetchData() }
  else { snackbar.handleError(data) }
}

const rejectDeposit = async (rejectType) => {
  const res = await fetch('/api/deposits/reject', { method: 'POST', headers, body: JSON.stringify({ id: selectedDeposit.value.id, reject_type: rejectType }) })
  const data = await res.json()
  if (res.ok) { snackbar.success(data.message); showActionDialog.value = false; fetchData() }
  else { snackbar.handleError(data) }
}

const showApproveDialog = ref(false)
const approveTarget = ref(null)
const approveAmount = ref(0)
const approving = ref(false)

const quickApprove = (d) => {
  approveTarget.value = d
  approveAmount.value = d.amount
  showApproveDialog.value = true
}

const confirmApprove = async () => {
  if (!approveTarget.value) return
  if (!approveAmount.value || Number(approveAmount.value) <= 0) {
    snackbar.error('Geçerli bir tutar girin.')
    return
  }
  approving.value = true
  try {
    const res = await fetch('/api/deposits/approve', {
      method: 'POST',
      headers,
      body: JSON.stringify({ id: approveTarget.value.id, amount: Number(approveAmount.value) }),
    })
    const data = await res.json()
    if (res.ok) {
      snackbar.success(data.message)
      showApproveDialog.value = false
      approveTarget.value = null
      fetchData()
    } else {
      snackbar.handleError(data)
    }
  } finally {
    approving.value = false
  }
}

const quickReject = async (d, rejectType = 1) => {
  if (!confirm('Bu işlemi reddetmek istediğinize emin misiniz?')) return
  const res = await fetch('/api/deposits/reject', { method: 'POST', headers, body: JSON.stringify({ id: d.id, reject_type: rejectType }) })
  const data = await res.json()
  if (res.ok) { snackbar.success(data.message); fetchData() }
  else { snackbar.handleError(data) }
}

// ---- Bekleyen yatırımı başka takıma taşı (yalnızca Super/Sub Admin) ----
const showMoveDialog = ref(false)
const moveForm = ref({ id: null, order_id: '', team_id: null, bank_id: null })
const moveTeams = ref([])
const moveBanks = ref([])
const moveTeamLoading = ref(false)
const moveSaving = ref(false)

const openMoveDialog = async (d) => {
  moveForm.value = { id: d.id, order_id: d.order_id, team_id: null, bank_id: null }
  moveTeams.value = []; moveBanks.value = []
  showMoveDialog.value = true
  try {
    const res = await fetch('/api/deposits/manual/meta', { headers })
    if (res.ok) { const data = await res.json(); moveTeams.value = data.teams || [] }
    else snackbar.error('Takım listesi yüklenemedi.')
  } catch { snackbar.error('Sunucu hatası.') }
}

watch(() => moveForm.value.team_id, async (teamId) => {
  moveForm.value.bank_id = null; moveBanks.value = []
  if (!teamId) return
  moveTeamLoading.value = true
  try {
    const res = await fetch(`/api/deposits/manual/team/${teamId}`, { headers })
    if (res.ok) { const data = await res.json(); moveBanks.value = data.banks || [] }
  } finally { moveTeamLoading.value = false }
})

const submitMove = async () => {
  if (!moveForm.value.team_id) { snackbar.error('Hedef takım seçin.'); return }
  if (!moveForm.value.bank_id) { snackbar.error('IBAN (banka hesabı) seçin.'); return }
  moveSaving.value = true
  try {
    const res = await fetch(`/api/deposits/${moveForm.value.id}/move-team`, {
      method: 'POST', headers,
      body: JSON.stringify({ team_id: moveForm.value.team_id, bank_id: moveForm.value.bank_id }),
    })
    const data = await res.json()
    if (res.ok) { showMoveDialog.value = false; snackbar.success(data.message || 'Yatırım taşındı.'); fetchData() }
    else { snackbar.error(data.message || 'Taşınamadı.') }
  } catch { snackbar.error('Sunucu hatası.') } finally { moveSaving.value = false }
}
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle class="d-flex align-center gap-2">
            {{ t('deposits.pending') }}
            <VChip color="warning" label size="small">{{ deposits.length }}</VChip>
          </VCardTitle>
          <template #append>
            <div class="d-flex align-center gap-2">
              <VIcon icon="tabler-refresh" size="18" color="success" />
              <span class="text-body-2">Otomatik Yenileme</span>
              <VChip color="success" label size="small">{{ countdown }} sn</VChip>
            </div>
          </template>
        </VCardItem>
        <VDivider />

        <!-- Tablo -->
        <VTable class="text-no-wrap pending-table">
          <thead>
            <tr>
              <th>ID</th>
              <th v-if="!isTeamMember">{{ t('deposits.merchant') }}</th>
              <th>{{ t('deposits.team') }}</th>
              <th>{{ t('deposits.order_id') }}</th>
              <th>{{ t('deposits.sender') }}</th>
              <th class="text-center">Güven</th>
              <th>{{ t('deposits.bank') }}</th>
              <th class="text-end">{{ t('deposits.amount') }}</th>
              <th class="text-center">Dekont</th>
              <th>{{ t('deposits.status') }}</th>
              <th>{{ t('deposits.date') }}</th>
              <th class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="d in deposits" :key="d.id">
              <td>{{ d.id }}</td>
              <td v-if="!isTeamMember">{{ d.merchant_name || '-' }}</td>
              <td>{{ d.team_name }}</td>
              <td class="text-caption">{{ d.order_id || '-' }}</td>
              <td>
                <div class="font-weight-medium">{{ d.name || '-' }}</div>
                <div v-if="d.player_id" class="text-caption text-medium-emphasis">{{ d.player_id }}</div>
              </td>
              <td class="text-center">
                <VChip
                  v-if="d.trust_rate !== null"
                  :color="d.trust_rate >= 70 ? 'success' : d.trust_rate >= 50 ? 'warning' : 'error'"
                  label
                  size="x-small"
                >
                  %{{ d.trust_rate }}
                </VChip>
                <span v-else class="text-caption text-medium-emphasis">-</span>
              </td>
              <td>
                <div class="text-body-2">{{ d.account_holder || '-' }}</div>
                <div v-if="d.account_iban" class="text-caption text-medium-emphasis"><CopyText :value="d.account_iban" :display="formatIban(d.account_iban)" /></div>
                <div v-if="d.bank_name" class="text-caption text-disabled">{{ shortBank(d.bank_name) }}</div>
              </td>
              <td class="text-end">
                <div class="font-weight-bold">{{ formatMoney(d.amount) }}</div>
                <div
                  v-if="d.original_amount !== null && Number(d.original_amount) !== Number(d.amount)"
                  class="text-caption d-flex align-center justify-end gap-1 mt-1"
                  :class="Number(d.amount) >= Number(d.original_amount) ? 'text-success' : 'text-warning'"
                >
                  <VIcon
                    :icon="Number(d.amount) >= Number(d.original_amount) ? 'tabler-arrow-up-right' : 'tabler-arrow-down-right'"
                    size="14"
                  />
                  <span>Talep: {{ formatMoney(d.original_amount) }}</span>
                </div>
              </td>
              <td class="text-center">
                <VBtn
                  v-if="d.has_receipt"
                  icon size="x-small" variant="tonal" color="info"
                  title="Dekont görüntüle"
                  @click="openReceipt(d.id)"
                >
                  <VIcon icon="tabler-file-check" size="18" />
                </VBtn>
                <span v-else class="text-caption text-medium-emphasis">—</span>
              </td>
              <td>
                <VChip :color="statusColors[d.status]" label size="x-small">
                  {{ statusLabels[d.status] }}
                </VChip>
                <div v-if="d.agent_name" class="text-caption text-medium-emphasis">{{ d.agent_name }}</div>
              </td>
              <td>
                <div class="text-body-2">{{ formatDate(d.created_at) }}</div>
                <div class="text-caption text-medium-emphasis">{{ timeSince(d.form_at || d.created_at) }}</div>
              </td>
              <td class="text-end">
                <div v-if="d.status === 1 || d.status === 2" class="d-flex gap-1 justify-end py-1">
                  <VBtn color="success" size="small" variant="flat" min-width="80" density="comfortable" @click="quickApprove(d)">Onayla</VBtn>
                  <VBtn color="error" size="small" variant="flat" min-width="80" density="comfortable" @click="quickReject(d, 1)">Reddet</VBtn>
                  <VBtn v-if="isAdmin && d.status === 1" color="warning" size="small" variant="flat" min-width="80" density="comfortable" prepend-icon="tabler-arrows-exchange-2" title="Başka takıma taşı" @click="openMoveDialog(d)">Taşı</VBtn>
                </div>
              </td>
            </tr>
            <tr v-if="!loading && deposits.length === 0">
              <td :colspan="isTeamMember ? 10 : 11" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>
  </VRow>

  <!-- Aksiyon Dialog -->
  <VDialog v-model="showActionDialog" max-width="950">
    <VCard v-if="selectedDeposit" :loading="detailLoading">
      <VCardItem>
        <VCardTitle class="d-flex align-center justify-space-between">
          <span># ID {{ selectedDeposit.id }} — {{ detailData?.deposit?.name || selectedDeposit.name || '-' }}</span>
          <VBtn icon size="small" variant="text" @click="showActionDialog = false">
            <VIcon icon="tabler-x" />
          </VBtn>
        </VCardTitle>
      </VCardItem>
      <VDivider />
      <VCardText>
        <VRow v-if="detailData">
          <VCol cols="12" md="6">
            <table class="detail-table">
              <tr><th>Ekip</th><td>{{ detailData.deposit.team_name || '-' }}</td></tr>
              <tr>
                <th>Oluşturulma</th>
                <td>
                  <div>{{ formatDuration(detailData.deposit.created_at) }}</div>
                  <div class="text-caption text-medium-emphasis">{{ formatDate(detailData.deposit.created_at) }}</div>
                </td>
              </tr>
              <tr>
                <th>Müşteri</th>
                <td>
                  <div class="d-flex align-center gap-2">
                    <div>
                      <div>{{ detailData.deposit.name || '-' }}</div>
                      <div class="text-caption text-medium-emphasis">{{ detailData.deposit.player_id || '-' }}</div>
                    </div>
                    <VChip
                      v-if="detailData.deposit.trust_rate !== null"
                      :color="detailData.deposit.trust_rate >= 70 ? 'success' : detailData.deposit.trust_rate >= 50 ? 'warning' : 'error'"
                      label
                      size="small"
                    >
                      Güven: %{{ detailData.deposit.trust_rate }}
                      <span class="text-caption ms-1">({{ detailData.deposit.trust_count }}/10)</span>
                    </VChip>
                  </div>
                </td>
              </tr>
              <tr>
                <th>Tutar</th>
                <td>
                  <div v-if="!editAmount" class="d-flex align-center gap-2">
                    <span class="text-h6 font-weight-bold">{{ formatMoney(detailData.deposit.amount) }}</span>
                    <VChip
                      v-if="detailData.deposit.original_amount !== null && Number(detailData.deposit.original_amount) !== Number(detailData.deposit.amount)"
                      size="x-small" label
                      :color="Number(detailData.deposit.amount) >= Number(detailData.deposit.original_amount) ? 'success' : 'warning'"
                    >
                      <VIcon
                        :icon="Number(detailData.deposit.amount) >= Number(detailData.deposit.original_amount) ? 'tabler-arrow-up-right' : 'tabler-arrow-down-right'"
                        size="12" start
                      />
                      API: {{ formatMoney(detailData.deposit.original_amount) }}
                    </VChip>
                    <VBtn icon size="x-small" variant="text" @click="editAmount = true; newAmount = detailData.deposit.amount">
                      <VIcon icon="tabler-edit" size="16" />
                    </VBtn>
                  </div>
                  <AppTextField v-else v-model="newAmount" type="number" prefix="₺" density="compact" style="max-width: 200px;" />
                  <div
                    v-if="!editAmount && detailData.deposit.original_amount !== null && Number(detailData.deposit.original_amount) !== Number(detailData.deposit.amount)"
                    class="text-caption text-medium-emphasis mt-1"
                  >
                    Oyuncu pay sayfasında tutarı değiştirdi (API talep: {{ formatMoney(detailData.deposit.original_amount) }} → deklare: {{ formatMoney(detailData.deposit.amount) }}). Lütfen dekontla karşılaştırın.
                  </div>
                </td>
              </tr>
              <tr>
                <th>İşlem ID</th>
                <td>
                  <div>{{ detailData.deposit.order_id || '-' }}</div>
                  <div class="text-caption text-medium-emphasis">Eklenme Yöntemi: {{ detailData.deposit.added_type == 2 ? 'Manuel' : 'API' }}</div>
                </td>
              </tr>
            </table>
          </VCol>
          <VCol cols="12" md="6">
            <table class="detail-table">
              <tr>
                <th>Durum</th>
                <td>
                  <VChip :color="statusColors[detailData.deposit.status]" label size="small">{{ statusLabels[detailData.deposit.status] }}</VChip>
                </td>
              </tr>
              <tr><th>Oluşturulma</th><td>{{ formatDate(detailData.deposit.created_at) }}</td></tr>
              <tr>
                <th>Yatırılan Hesap</th>
                <td>
                  <div class="font-weight-bold">{{ detailData.deposit.account_holder || '-' }}</div>
                  <div class="text-caption"><CopyText :value="detailData.deposit.account_iban" :display="formatIban(detailData.deposit.account_iban)" /></div>
                </td>
              </tr>
              <tr>
                <th>Banka</th>
                <td>
                  <img v-if="detailData.deposit.bank_logo" :src="detailData.deposit.bank_logo" :alt="detailData.deposit.bank_name" style="max-height: 24px;" />
                  <span v-else>{{ detailData.deposit.bank_name || '-' }}</span>
                </td>
              </tr>
              <tr v-if="detailData.deposit.receipt_path">
                <th>Dekont</th>
                <td>
                  <a href="#" @click.prevent="openReceipt(detailData.deposit.id)" style="color: rgb(var(--v-theme-primary)); font-weight: 600;">
                    <VIcon icon="tabler-file-text" size="18" class="me-1" />
                    Dekontu Görüntüle
                    <VIcon icon="tabler-external-link" size="14" class="ms-1" />
                  </a>
                </td>
              </tr>
            </table>
          </VCol>
        </VRow>

        <!-- Üye son 10 işlem -->
        <div v-if="detailData?.history" class="mt-4">
          <VTable density="compact" class="text-no-wrap history-table">
            <thead>
              <tr>
                <th>Ad Soyad</th>
                <th class="text-end">Tutar</th>
                <th>Tarih</th>
                <th>Durum</th>
              </tr>
            </thead>
            <tbody>
              <tr style="background: rgba(var(--v-theme-primary), 0.15);">
                <td class="font-weight-medium">{{ detailData.deposit.name || '-' }}</td>
                <td class="text-end font-weight-bold">{{ formatMoney(detailData.deposit.amount) }}</td>
                <td>
                  <div>{{ formatDuration(detailData.deposit.created_at) }}</div>
                  <div class="text-caption text-medium-emphasis">{{ formatDate(detailData.deposit.created_at) }}</div>
                </td>
                <td>
                  <VChip :color="statusColors[detailData.deposit.status]" label size="x-small">{{ statusLabels[detailData.deposit.status] }}</VChip>
                </td>
              </tr>
              <tr v-for="h in detailData.history" :key="h.id">
                <td>{{ h.name || '-' }}</td>
                <td class="text-end">{{ formatMoney(h.amount) }}</td>
                <td>
                  <div>{{ formatDuration(h.created_at) }}</div>
                  <div class="text-caption text-medium-emphasis">{{ formatDate(h.created_at) }}</div>
                </td>
                <td>
                  <VChip :color="historyStatusColor(h)" label size="x-small">{{ historyStatusLabel(h) }}</VChip>
                </td>
              </tr>
              <tr v-if="detailData.history.length === 0">
                <td colspan="4" class="text-center text-medium-emphasis">Geçmiş işlem yok</td>
              </tr>
            </tbody>
          </VTable>
        </div>

      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showActionDialog = false">{{ t('common.cancel') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Hızlı Onay Dialog (tutar düzenlenebilir) -->
  <VDialog v-model="showApproveDialog" max-width="420" persistent>
    <VCard title="Yatırımı Onayla">
      <VCardText>
        <div class="mb-3 text-body-2 text-medium-emphasis">
          <div><strong>{{ approveTarget?.name || '-' }}</strong> ({{ approveTarget?.player_id || '-' }})</div>
          <div>Order ID: <code>{{ approveTarget?.order_id || '-' }}</code></div>
        </div>
        <AppTextField
          v-model="approveAmount"
          type="number"
          step="0.01"
          label="Onaylanacak Tutar"
          prefix="₺"
          density="compact"
          autofocus
        />
        <div v-if="approveTarget && Number(approveAmount) !== Number(approveTarget.amount)" class="text-caption text-warning mt-2">
          ⚠ Orijinal tutar: ₺{{ approveTarget.amount }} — değiştirildi
        </div>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" :disabled="approving" @click="showApproveDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="success" :loading="approving" @click="confirmApprove">Onayla</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Başka Takıma Taşı (Bekleyen yatırım) -->
  <VDialog v-model="showMoveDialog" max-width="520">
    <VCard>
      <VCardItem>
        <VCardTitle>Başka Takıma Taşı</VCardTitle>
        <template #append>
          <VBtn icon size="small" variant="text" @click="showMoveDialog = false"><VIcon icon="tabler-x" /></VBtn>
        </template>
      </VCardItem>
      <VDivider />
      <VCardText>
        <div class="text-body-2 text-medium-emphasis mb-4">
          İşlem: <strong>{{ moveForm.order_id }}</strong> (#{{ moveForm.id }}) — yalnızca <strong>bekleyen</strong> yatırımlar taşınır. Yatırım seçilen takıma ve o takımın IBAN'ına aktarılır.
        </div>
        <VRow>
          <VCol cols="12">
            <VAutocomplete
              v-model="moveForm.team_id"
              :items="moveTeams.map(t => ({ title: t.name, value: t.id }))"
              label="Hedef Takım"
              prepend-inner-icon="tabler-users-group"
              density="compact"
            />
          </VCol>
          <VCol cols="12">
            <VAutocomplete
              v-model="moveForm.bank_id"
              :items="moveBanks.map(b => ({ title: b.name, value: b.id }))"
              label="IBAN (Banka Hesabı)"
              prepend-inner-icon="tabler-building-bank"
              density="compact"
              :disabled="!moveForm.team_id"
              :loading="moveTeamLoading"
            />
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showMoveDialog = false">İptal</VBtn>
        <VBtn color="primary" prepend-icon="tabler-arrows-exchange-2" :loading="moveSaving" @click="submitMove">Taşı</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>

<style scoped>
.pending-table :deep(tbody td) {
  padding-block: 12px !important;
  vertical-align: middle;
}
.detail-table { width: 100%; border-collapse: collapse; }
.detail-table th { text-align: left; padding: 12px 8px; width: 35%; font-weight: 500; color: rgba(var(--v-theme-on-surface), 0.7); border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); }
.detail-table td { padding: 12px 8px; border-bottom: 1px solid rgba(var(--v-border-color), var(--v-border-opacity)); }
</style>
