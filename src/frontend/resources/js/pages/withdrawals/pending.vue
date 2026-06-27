<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', roles: [1, 2, 4, 5] } })

const { t } = useI18n()
const { headers } = useApi()
const snackbar = useSnackbar()

const loading = ref(true)
const withdrawals = ref([])
const countdown = ref(5)

const showActionDialog = ref(false)
const selectedWithdraw = ref(null)
const detailData = ref(null)
const detailLoading = ref(false)

const receipts = ref([])
const receiptsLoading = ref(false)
const uploadingReceipt = ref(false)
const dragOver = ref(false)
const fileInputRef = ref(null)

const user = JSON.parse(localStorage.getItem('user') || '{}')
const isAdmin = [1, 4].includes(user.user_type)
const isSuperAdmin = user.user_type === 1
const isTeamMember = [2, 5].includes(user.user_type)

// Toplu atama
const selectedIds = ref([])
const showAssignDialog = ref(false)
const teamCases = ref([])
const teamCasesLoading = ref(false)
const selectedTeamId = ref(null)
const assigning = ref(false)

const eligibleIds = computed(() => withdrawals.value.filter(w => w.status === 0 || w.status === 1).map(w => w.id))
const allEligibleSelected = computed(() => eligibleIds.value.length > 0 && eligibleIds.value.every(id => selectedIds.value.includes(id)))

const toggleSelectAll = () => {
  if (allEligibleSelected.value) selectedIds.value = []
  else selectedIds.value = [...eligibleIds.value]
}
const toggleSelectOne = (id) => {
  const i = selectedIds.value.indexOf(id)
  if (i >= 0) selectedIds.value.splice(i, 1)
  else selectedIds.value.push(id)
}

const openAssignDialog = async () => {
  if (selectedIds.value.length === 0) return
  selectedTeamId.value = null
  showAssignDialog.value = true
  teamCasesLoading.value = true
  try {
    const res = await fetch('/api/team-cases', { headers })
    if (res.ok) {
      const data = await res.json()
      teamCases.value = data.teams || []
    }
  } finally { teamCasesLoading.value = false }
}

const confirmAssign = async () => {
  if (!selectedTeamId.value) { snackbar.error('Bir takım seçin.'); return }
  assigning.value = true
  try {
    const res = await fetch('/api/withdrawals/bulk-assign', {
      method: 'POST', headers,
      body: JSON.stringify({ ids: selectedIds.value, team_id: selectedTeamId.value }),
    })
    const data = await res.json()
    if (res.ok) {
      snackbar.success(data.message)
      showAssignDialog.value = false
      selectedIds.value = []
      fetchData()
    } else { snackbar.handleError(data) }
  } finally { assigning.value = false }
}

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const formatIban = iban => iban ? iban.replace(/(.{4})/g, '$1 ').trim() : '-'
const formatDate = val => val ? new Date(val).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : '-'

const statusLabels = { 0: 'Havuzda', 1: 'Beklemede', 2: 'İşlemde' }
const statusColors = { 0: 'secondary', 1: 'warning', 2: 'info' }

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
    osc.connect(gain); gain.connect(ctx.destination)
    osc.frequency.value = 660
    osc.type = 'sine'
    gain.gain.setValueAtTime(0.0001, ctx.currentTime)
    gain.gain.exponentialRampToValueAtTime(0.3, ctx.currentTime + 0.02)
    gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.5)
    osc.start(ctx.currentTime); osc.stop(ctx.currentTime + 0.5)
    setTimeout(() => ctx.close(), 600)
  } catch (e) {}
}

const fetchData = async () => {
  loading.value = true
  try {
    const res = await fetch('/api/withdrawals/pending', { headers })
    if (res.ok) {
      const data = await res.json()
      let hasNew = false
      for (const w of data) {
        if ((w.status === 0 || w.status === 1) && !knownIds.has(w.id)) {
          if (!firstLoad) hasNew = true
          knownIds.add(w.id)
        }
      }
      if (hasNew) playBeep()
      withdrawals.value = data
      firstLoad = false
    }
  } finally { loading.value = false }
}

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
onMounted(() => { fetchData(); startCountdown() })
onUnmounted(() => clearInterval(countdownInterval))

const openAction = async (wd) => {
  selectedWithdraw.value = wd
  showActionDialog.value = true
  detailData.value = null
  detailLoading.value = true
  receipts.value = []
  try {
    const [detailRes] = await Promise.all([
      fetch(`/api/withdrawals/${wd.id}/detail`, { headers }),
      loadReceipts(wd.id),
    ])
    if (detailRes.ok) detailData.value = await detailRes.json()
  } finally { detailLoading.value = false }
}

const loadReceipts = async (id) => {
  receiptsLoading.value = true
  try {
    const res = await fetch(`/api/withdrawals/${id}/receipts`, { headers })
    if (res.ok) {
      const data = await res.json()
      const list = data.receipts || []
      // Auth gerektiği için her dosyayı fetch + blob URL ile hazırla (<img>, <a target=_blank> token taşımaz)
      await Promise.all(list.map(async (r) => {
        try {
          const r2 = await fetch(r.url, { headers })
          if (r2.ok) r.blob_url = URL.createObjectURL(await r2.blob())
        } catch (e) {}
      }))
      receipts.value = list
    }
  } finally { receiptsLoading.value = false }
}

const uploadReceiptFile = async (file) => {
  if (!file || !selectedWithdraw.value) return
  const id = selectedWithdraw.value.id
  const allowed = ['application/pdf', 'image/jpeg', 'image/png', 'image/webp']
  if (!allowed.includes(file.type)) {
    snackbar.error('Sadece PDF, JPG, PNG veya WEBP yükleyebilirsiniz.')
    return
  }
  if (file.size > 10 * 1024 * 1024) {
    snackbar.error('Dosya boyutu 10 MB\'ı aşamaz.')
    return
  }
  uploadingReceipt.value = true
  try {
    // Base64 JSON ile gönder — WAF multipart upload'ı blokluyor
    const base64 = await new Promise((resolve, reject) => {
      const r = new FileReader()
      r.onload = () => resolve(String(r.result))
      r.onerror = () => reject(r.error)
      r.readAsDataURL(file)
    })
    const res = await fetch(`/api/withdrawals/${id}/receipts`, {
      method: 'POST', headers,
      body: JSON.stringify({
        file_base64: base64,
        file_name: file.name || `receipt.${(file.type.split('/')[1] || 'bin')}`,
        mime_type: file.type,
      }),
    })
    let data = {}
    try { data = await res.json() } catch (e) { data = { message: `HTTP ${res.status} — yanıt JSON değil` } }
    if (res.ok) {
      snackbar.success(data.message || 'Dekont yüklendi.')
      await loadReceipts(id)
      const target = withdrawals.value.find(w => w.id === id)
      if (target) target.receipt_count = (target.receipt_count || 0) + 1
    } else {
      console.error('Upload failed:', res.status, data)
      snackbar.handleError({ message: `[HTTP ${res.status}] ${data.message || 'Yükleme başarısız'}` })
    }
  } catch (err) {
    console.error('Upload error:', err)
    snackbar.error('Yükleme hatası: ' + (err?.message || err))
  } finally { uploadingReceipt.value = false }
}

const onFileSelect = (e) => {
  const file = e.target.files && e.target.files[0]
  if (file) uploadReceiptFile(file)
  if (fileInputRef.value) fileInputRef.value.value = ''
}

const onDrop = (e) => {
  e.preventDefault()
  dragOver.value = false
  const file = e.dataTransfer?.files?.[0]
  if (file) uploadReceiptFile(file)
}

const onPaste = (e) => {
  if (!showActionDialog.value) return
  const items = e.clipboardData?.items || []
  for (const item of items) {
    if (item.type.startsWith('image/')) {
      const blob = item.getAsFile()
      if (blob) {
        const ext = blob.type.split('/')[1] || 'png'
        const file = new File([blob], `pasted-${Date.now()}.${ext}`, { type: blob.type })
        uploadReceiptFile(file)
        e.preventDefault()
        return
      }
    }
  }
}

const verificationMeta = (r) => {
  const st = r.verification_status || 'pending'
  const map = {
    verified:   { color: 'success', icon: 'tabler-shield-check',    label: 'Doğrulandı' },
    suspicious: { color: 'warning', icon: 'tabler-alert-triangle',  label: 'Şüpheli' },
    rejected:   { color: 'error',   icon: 'tabler-shield-x',        label: 'Reddedildi' },
    pending:    { color: 'grey',    icon: 'tabler-loader-2',        label: 'Analiz ediliyor…' },
  }
  return map[st] || map.pending
}

const parsedNotes = (r) => {
  const notes = r.verification_notes || ''
  const lines = notes.split('\n').map(l => l.trim()).filter(Boolean)
  let aiNote = ''
  const checks = []
  for (const line of lines) {
    if (line.startsWith('AI:')) aiNote = line.replace(/^AI:\s*/, '')
    else checks.push(line)
  }
  return { aiNote, checks }
}

const checkMeta = (text) => {
  const l = text.toLowerCase()
  if (l.includes('manipülasyon') || l.includes('tampering')) return { icon: 'tabler-alert-triangle', color: 'error' }
  if (l.includes('uyumsuz') || l.includes('edilemedi') || l.includes('aşam') || l.includes('hata')) return { icon: 'tabler-x', color: 'error' }
  if (l.includes('eşleşiyor') || l.includes('tespit edildi') || l.includes('tanındı')) return { icon: 'tabler-check', color: 'success' }
  return { icon: 'tabler-point', color: 'warning' }
}

const formatAiAmount = (v) => v !== null && v !== undefined ? '₺' + Number(v).toLocaleString('tr-TR', { minimumFractionDigits: 2 }) : '—'
const formatAiDate = (s) => s ? new Date(s).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—'

const reverifyReceipt = async (r) => {
  if (!selectedWithdraw.value) return
  const id = selectedWithdraw.value.id
  r.verification_status = 'pending'
  r.verification_score = null
  r.verification_notes = null
  const res = await fetch(`/api/withdrawals/${id}/receipts/${r.id}/verify`, { method: 'POST', headers })
  const data = await res.json().catch(() => ({}))
  if (res.ok) {
    snackbar.success(data.message || 'Yeniden doğrulama başlatıldı.')
    setTimeout(() => loadReceipts(id), 20000) // 20sn sonra yenile (job kuyruğunu bekle)
  } else {
    snackbar.handleError(data)
  }
}

const formatFileSize = (bytes) => {
  if (!bytes) return ''
  if (bytes < 1024) return bytes + ' B'
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB'
  return (bytes / 1024 / 1024).toFixed(2) + ' MB'
}

onMounted(() => window.addEventListener('paste', onPaste))
onUnmounted(() => window.removeEventListener('paste', onPaste))

const takeWithdraw = async (id) => {
  const res = await fetch('/api/withdrawals/take', { method: 'POST', headers, body: JSON.stringify({ id }) })
  const data = await res.json()
  if (res.ok) { snackbar.success(data.message); fetchData() }
  else { snackbar.handleError(data) }
}

const releaseWithdraw = async (id) => {
  const res = await fetch('/api/withdrawals/release', { method: 'POST', headers, body: JSON.stringify({ id }) })
  const data = await res.json()
  if (res.ok) { snackbar.success(data.message); showActionDialog.value = false; fetchData() }
  else { snackbar.handleError(data) }
}

const quickApprove = async (w) => {
  if (!w.receipt_count || w.receipt_count === 0) {
    snackbar.error('Onay için en az bir dekont yüklemeniz gerekiyor. Detay penceresinden ekleyebilirsiniz.')
    openAction(w)
    return
  }
  if (!confirm('Bu çekimi onaylamak istediğinize emin misiniz?')) return
  const res = await fetch('/api/withdrawals/approve', { method: 'POST', headers, body: JSON.stringify({ id: w.id }) })
  const data = await res.json()
  if (res.ok) { snackbar.success(data.message); showActionDialog.value = false; fetchData() }
  else { snackbar.handleError(data) }
}

const approveFromDialog = async () => {
  if (!selectedWithdraw.value) return
  if (!receipts.value.length) {
    snackbar.error('Onay için en az bir dekont yüklemeniz gerekiyor.')
    return
  }
  if (!confirm('Bu çekimi onaylamak istediğinize emin misiniz?')) return
  const res = await fetch('/api/withdrawals/approve', { method: 'POST', headers, body: JSON.stringify({ id: selectedWithdraw.value.id }) })
  const data = await res.json()
  if (res.ok) { snackbar.success(data.message); showActionDialog.value = false; fetchData() }
  else { snackbar.handleError(data) }
}

const quickReject = async (w, rejectType = 1) => {
  if (!confirm('Bu çekimi reddetmek istediğinize emin misiniz?')) return
  const res = await fetch('/api/withdrawals/reject', { method: 'POST', headers, body: JSON.stringify({ id: w.id, reject_type: rejectType }) })
  const data = await res.json()
  if (res.ok) { snackbar.success(data.message); fetchData() }
  else { snackbar.handleError(data) }
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

const allStatusLabel = { 0: 'Havuzda', 1: 'Beklemede', 2: 'İşlemde', 3: 'Onaylandı', 4: 'Reddedildi' }
const allStatusColor = { 0: 'secondary', 1: 'warning', 2: 'info', 3: 'success', 4: 'error' }
const historyStatusLabel = (h) => allStatusLabel[h.status] || '-'
const historyStatusColor = (h) => allStatusColor[h.status] || 'default'

// ---- Bekleyen çekimi başka takıma taşı (Super/Sub Admin; pasif takım dahil) ----
const showMoveDialog = ref(false)
const moveForm = ref({ id: null, order_id: '', team_id: null })
const moveTeams = ref([])
const moveSaving = ref(false)
const openMoveDialog = async (w) => {
  moveForm.value = { id: w.id, order_id: w.order_id, team_id: null }
  moveTeams.value = []
  showMoveDialog.value = true
  try {
    const res = await fetch('/api/withdrawals/manual/meta', { headers })
    if (res.ok) { const d = await res.json(); moveTeams.value = d.teams || [] }
    else snackbar.error('Takım listesi yüklenemedi.')
  } catch { snackbar.error('Sunucu hatası.') }
}
const submitMove = async () => {
  if (!moveForm.value.team_id) { snackbar.error('Hedef takım seçin.'); return }
  moveSaving.value = true
  try {
    const res = await fetch(`/api/withdrawals/${moveForm.value.id}/move-team`, {
      method: 'POST', headers, body: JSON.stringify({ team_id: moveForm.value.team_id }),
    })
    const data = await res.json()
    if (res.ok) { showMoveDialog.value = false; snackbar.success(data.message || 'Çekim taşındı.'); fetchData() }
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
            {{ t('withdrawals.pending') }}
            <VChip color="warning" label size="small">{{ withdrawals.length }}</VChip>
            <VChip v-if="selectedIds.length > 0" color="primary" label size="small">{{ selectedIds.length }} seçili</VChip>
          </VCardTitle>
          <template #append>
            <div class="d-flex align-center gap-3">
              <VBtn
                v-if="isSuperAdmin && selectedIds.length > 0"
                color="primary"
                size="small"
                prepend-icon="tabler-users"
                @click="openAssignDialog"
              >
                Çekimleri Ata
              </VBtn>
              <div class="d-flex align-center gap-2">
                <VIcon icon="tabler-refresh" size="18" color="success" />
                <span class="text-body-2">Otomatik Yenileme</span>
                <VChip color="success" label size="small">{{ countdown }} sn</VChip>
              </div>
            </div>
          </template>
        </VCardItem>
        <VDivider />

        <VTable class="text-no-wrap pending-table">
          <thead>
            <tr>
              <th v-if="isSuperAdmin" style="width: 44px;">
                <VCheckbox
                  :model-value="allEligibleSelected"
                  :indeterminate="selectedIds.length > 0 && !allEligibleSelected"
                  :disabled="eligibleIds.length === 0"
                  hide-details
                  density="compact"
                  @update:model-value="toggleSelectAll"
                />
              </th>
              <th>ID</th>
              <th v-if="!isTeamMember">{{ t('deposits.merchant') }}</th>
              <th>{{ t('deposits.team') }}</th>
              <th>{{ t('deposits.order_id') }}</th>
              <th>{{ t('withdrawals.receiver') }}</th>
              <th>{{ t('withdrawals.iban') }}</th>
              <th class="text-end">{{ t('deposits.amount') }}</th>
              <th>{{ t('deposits.status') }}</th>
              <th>{{ t('deposits.date') }}</th>
              <th class="text-center">Dekont</th>
              <th class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="w in withdrawals" :key="w.id">
              <td v-if="isSuperAdmin">
                <VCheckbox
                  v-if="w.status === 0 || w.status === 1"
                  :model-value="selectedIds.includes(w.id)"
                  hide-details
                  density="compact"
                  @update:model-value="toggleSelectOne(w.id)"
                />
              </td>
              <td>{{ w.id }}</td>
              <td v-if="!isTeamMember">{{ w.merchant_name || '-' }}</td>
              <td>{{ w.team_name }}</td>
              <td class="text-caption">{{ w.order_id || '-' }}</td>
              <td>
                <div class="font-weight-medium">{{ w.name || '-' }}</div>
                <div v-if="w.player_id" class="text-caption text-medium-emphasis">{{ w.player_id }}</div>
              </td>
              <td class="text-body-2"><CopyText :value="w.iban" :display="formatIban(w.iban)" /></td>
              <td class="text-end font-weight-bold">{{ formatMoney(w.amount) }}</td>
              <td>
                <VChip :color="statusColors[w.status]" label size="x-small">
                  {{ statusLabels[w.status] }}
                </VChip>
                <div v-if="w.agent_name" class="text-caption text-medium-emphasis">{{ w.agent_name }}</div>
              </td>
              <td>
                <div class="text-body-2">{{ formatDate(w.created_at) }}</div>
                <div class="text-caption text-medium-emphasis">{{ timeSince(w.form_at || w.created_at) }}</div>
              </td>
              <td class="text-center">
                <VBtn
                  :color="w.receipt_count > 0 ? 'success' : 'warning'"
                  :variant="w.receipt_count > 0 ? 'tonal' : 'flat'"
                  size="small"
                  density="comfortable"
                  prepend-icon="tabler-paperclip"
                  @click="openAction(w)"
                >
                  Dekont Yükle ({{ w.receipt_count || 0 }})
                </VBtn>
              </td>
              <td class="text-end">
                <VBtn v-if="w.status === 0 || w.status === 1" color="primary" size="x-small" variant="flat" @click="takeWithdraw(w.id)">
                  {{ t('deposits.take') }}
                </VBtn>
                <div v-else-if="w.status === 2" class="d-flex flex-column gap-1 align-end py-1">
                  <div class="d-flex gap-1">
                    <VBtn
                      color="success"
                      size="small"
                      variant="flat"
                      min-width="80"
                      density="comfortable"
                      :disabled="!w.receipt_count"
                      :title="!w.receipt_count ? 'Önce dekont yükleyin (Detay)' : ''"
                      @click="quickApprove(w)"
                    >Onayla</VBtn>
                    <VBtn color="error" size="small" variant="flat" min-width="80" density="comfortable" @click="quickReject(w, 1)">Reddet</VBtn>
                  </div>
                  <div class="d-flex gap-1">
                    <VBtn color="warning" size="small" variant="outlined" min-width="80" density="comfortable" @click="releaseWithdraw(w.id)">Bırak</VBtn>
                    <VBtn color="info" size="small" variant="outlined" min-width="80" density="comfortable" @click="openAction(w)">Detay</VBtn>
                  </div>
                </div>
                <VBtn v-if="isAdmin && (w.status === 1 || w.status === 2)" color="warning" size="x-small" variant="text" prepend-icon="tabler-arrows-exchange-2" title="Başka takıma taşı" class="mt-1" @click="openMoveDialog(w)">Taşı</VBtn>
              </td>
            </tr>
            <tr v-if="!loading && withdrawals.length === 0">
              <td :colspan="(isAdmin ? 1 : 0) + (isTeamMember ? 10 : 11)" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>
  </VRow>

  <VDialog v-model="showActionDialog" max-width="950">
    <VCard v-if="selectedWithdraw" :loading="detailLoading">
      <VCardItem>
        <VCardTitle class="d-flex align-center justify-space-between">
          <span># ID {{ selectedWithdraw.id }} — {{ detailData?.withdraw?.name || selectedWithdraw.name || '-' }}</span>
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
              <tr><th>Ekip</th><td>{{ detailData.withdraw.team_name || '-' }}</td></tr>
              <tr><th>Müşteri</th><td><div>{{ detailData.withdraw.name || '-' }}</div><div class="text-caption text-medium-emphasis">{{ detailData.withdraw.player_id || '-' }}</div></td></tr>
              <tr><th>Tutar</th><td class="text-h6 font-weight-bold">{{ formatMoney(detailData.withdraw.amount) }}</td></tr>
              <tr><th>Order ID</th><td>{{ detailData.withdraw.order_id || '-' }}</td></tr>
            </table>
          </VCol>
          <VCol cols="12" md="6">
            <table class="detail-table">
              <tr><th>Durum</th><td><VChip :color="allStatusColor[detailData.withdraw.status]" label size="small">{{ allStatusLabel[detailData.withdraw.status] }}</VChip></td></tr>
              <tr><th>Oluşturulma</th><td>{{ formatDate(detailData.withdraw.created_at) }}</td></tr>
              <tr><th>IBAN</th><td><CopyText :value="detailData.withdraw.iban" :display="formatIban(detailData.withdraw.iban)" /></td></tr>
              <tr v-if="!isTeamMember"><th>Merchant</th><td>{{ detailData.withdraw.merchant_name || '-' }}</td></tr>
            </table>
          </VCol>
        </VRow>

        <VDivider class="my-4" />

        <div class="d-flex align-center justify-space-between mb-2">
          <div class="text-body-2 font-weight-medium">
            Dekontlar
            <VChip size="x-small" class="ml-1" :color="receipts.length ? 'success' : 'default'" label>{{ receipts.length }}</VChip>
          </div>
          <div class="text-caption text-medium-emphasis">PDF / JPG / PNG / WEBP • max 10 MB • Ctrl+V ile yapıştırabilirsiniz</div>
        </div>

        <div
          class="receipt-dropzone d-flex align-center justify-center pa-4 mb-3"
          :class="{ 'dropzone-active': dragOver }"
          @click="fileInputRef?.click()"
          @dragover.prevent="dragOver = true"
          @dragleave.prevent="dragOver = false"
          @drop="onDrop"
        >
          <input
            ref="fileInputRef"
            type="file"
            accept="application/pdf,image/jpeg,image/png,image/webp"
            class="d-none"
            @change="onFileSelect"
          />
          <div v-if="uploadingReceipt" class="d-flex align-center gap-2">
            <VProgressCircular indeterminate size="20" width="2" /> Yükleniyor…
          </div>
          <div v-else class="text-center">
            <VIcon icon="tabler-cloud-upload" size="32" color="primary" />
            <div class="text-body-2 mt-1">Dosya seçmek için tıkla, sürükle bırak veya Ctrl+V ile yapıştır</div>
          </div>
        </div>

        <div v-if="receiptsLoading" class="text-center py-3">
          <VProgressCircular indeterminate size="24" />
        </div>
        <div v-else-if="receipts.length === 0" class="text-center text-medium-emphasis py-3 text-caption">
          Henüz dekont yüklenmemiş.
        </div>
        <VRow v-else dense>
          <VCol v-for="r in receipts" :key="r.id" cols="12" md="6">
            <VCard variant="outlined" class="receipt-card">
              <div class="position-relative">
                <a :href="r.blob_url || r.url" target="_blank" class="d-block receipt-thumb">
                  <img v-if="r.is_image && r.blob_url" :src="r.blob_url" :alt="r.original_name" />
                  <div v-else class="d-flex flex-column align-center justify-center pa-3">
                    <VIcon :icon="r.is_pdf ? 'tabler-file-type-pdf' : 'tabler-file'" size="32" color="error" />
                    <div class="text-caption mt-1 text-truncate" style="max-width: 100%;">{{ r.original_name }}</div>
                  </div>
                </a>
                <VChip
                  :color="verificationMeta(r).color"
                  :prepend-icon="verificationMeta(r).icon"
                  size="small"
                  label
                  class="ai-badge"
                >
                  {{ verificationMeta(r).label }}<span v-if="r.verification_score !== null"> · {{ r.verification_score }}/100</span>
                </VChip>
              </div>

              <div class="pa-2 text-caption">
                <div class="text-truncate" :title="r.original_name">{{ r.original_name }}</div>
                <div class="text-medium-emphasis d-flex justify-space-between">
                  <span>{{ formatFileSize(r.file_size) }}</span>
                  <span>{{ formatDate(r.uploaded_at) }}</span>
                </div>
              </div>

              <VDivider />
              <div class="pa-3 ai-panel">
                <div class="d-flex align-center justify-space-between mb-3">
                  <div class="d-flex align-center gap-2">
                    <VIcon icon="tabler-robot" size="18" color="primary" />
                    <span class="font-weight-medium">AI Analiz</span>
                  </div>
                  <VBtn
                    v-if="isAdmin"
                    size="x-small" variant="text" color="primary"
                    prepend-icon="tabler-refresh"
                    @click="reverifyReceipt(r)"
                  >Yeniden Doğrula</VBtn>
                </div>

                <!-- Pending: Loading -->
                <div v-if="r.verification_status === 'pending'" class="ai-loading">
                  <VProgressCircular indeterminate size="20" width="2" color="primary" />
                  <span>Görsel analiz ediliyor…</span>
                </div>

                <template v-else>
                  <!-- Durum Banner'ı -->
                  <div class="ai-status-banner" :class="`ai-status-${r.verification_status}`">
                    <VIcon :icon="verificationMeta(r).icon" size="20" />
                    <span class="ai-status-label">{{ verificationMeta(r).label }}</span>
                    <span v-if="r.verification_score !== null" class="ai-status-score">{{ r.verification_score }}<small>/100</small></span>
                  </div>

                  <!-- Veri Grid -->
                  <div v-if="r.verification_data" class="ai-data-grid">
                    <div class="ai-row">
                      <span class="ai-label">Tutar</span>
                      <span class="ai-value">{{ formatAiAmount(r.verification_data.amount) }}</span>
                    </div>
                    <div class="ai-row">
                      <span class="ai-label">IBAN Son 4</span>
                      <span class="ai-value"><code v-if="r.verification_data.iban_last4">{{ r.verification_data.iban_last4 }}</code><span v-else class="ai-empty">—</span></span>
                    </div>
                    <div class="ai-row">
                      <span class="ai-label">Alıcı</span>
                      <span class="ai-value">{{ r.verification_data.recipient_name || '—' }}</span>
                    </div>
                    <div v-if="r.verification_data.sender_name" class="ai-row">
                      <span class="ai-label">Gönderen</span>
                      <span class="ai-value">{{ r.verification_data.sender_name }}</span>
                    </div>
                    <div class="ai-row">
                      <span class="ai-label">Banka</span>
                      <span class="ai-value">{{ r.verification_data.bank_name || '—' }}</span>
                    </div>
                    <div v-if="r.verification_data.transaction_date" class="ai-row">
                      <span class="ai-label">İşlem Zamanı</span>
                      <span class="ai-value">{{ formatAiDate(r.verification_data.transaction_date) }}</span>
                    </div>
                    <div v-if="r.verification_data.transaction_id" class="ai-row">
                      <span class="ai-label">Ref No</span>
                      <span class="ai-value"><code>{{ r.verification_data.transaction_id }}</code></span>
                    </div>
                  </div>

                  <!-- AI Notu (varsa, ayrı bir blok) -->
                  <div v-if="parsedNotes(r).aiNote" class="ai-note-box">
                    <VIcon icon="tabler-message-circle" size="14" />
                    <span>{{ parsedNotes(r).aiNote }}</span>
                  </div>

                  <!-- Kontrol Checklist -->
                  <div v-if="parsedNotes(r).checks.length" class="ai-checks">
                    <div v-for="(item, i) in parsedNotes(r).checks" :key="i" class="ai-check-item">
                      <VIcon :icon="checkMeta(item).icon" :color="checkMeta(item).color" size="14" />
                      <span>{{ item }}</span>
                    </div>
                  </div>

                  <div v-if="!r.verification_data && !r.verification_notes" class="text-caption text-medium-emphasis">
                    Henüz analiz sonucu yok.
                  </div>
                </template>
              </div>
            </VCard>
          </VCol>
        </VRow>

        <div v-if="detailData?.history" class="mt-4">
          <div class="text-body-2 text-medium-emphasis mb-2">Üyenin Son Çekimleri</div>
          <VTable density="compact" class="text-no-wrap">
            <thead>
              <tr>
                <th>Ad Soyad</th>
                <th class="text-end">Tutar</th>
                <th>Tarih</th>
                <th>Durum</th>
              </tr>
            </thead>
            <tbody>
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
        <VBtn
          v-if="selectedWithdraw && selectedWithdraw.status === 2"
          color="warning"
          variant="outlined"
          @click="releaseWithdraw(selectedWithdraw.id)"
        >Bırak</VBtn>
        <VSpacer />
        <VBtn
          v-if="selectedWithdraw && selectedWithdraw.status === 2"
          color="error"
          variant="flat"
          @click="quickReject(selectedWithdraw, 1)"
        >Reddet</VBtn>
        <VBtn
          v-if="selectedWithdraw && selectedWithdraw.status === 2"
          color="success"
          variant="flat"
          :disabled="receipts.length === 0"
          :title="receipts.length === 0 ? 'Önce dekont yükleyin' : ''"
          @click="approveFromDialog"
        >Onayla</VBtn>
        <VBtn variant="text" @click="showActionDialog = false">Kapat</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Toplu Atama Dialog -->
  <VDialog v-model="showAssignDialog" max-width="640" persistent>
    <VCard :loading="teamCasesLoading">
      <VCardItem>
        <VCardTitle class="d-flex align-center justify-space-between">
          <span>Çekimleri Takıma Ata</span>
          <VBtn icon size="small" variant="text" @click="showAssignDialog = false">
            <VIcon icon="tabler-x" />
          </VBtn>
        </VCardTitle>
        <VCardSubtitle>
          {{ selectedIds.length }} çekim seçildi. Atanacak takımı seçin — işlemler o takımın ilk kullanıcısına atanır.
        </VCardSubtitle>
      </VCardItem>
      <VDivider />
      <VCardText>
        <div v-if="!teamCasesLoading && teamCases.length === 0" class="text-center text-medium-emphasis py-6">
          Aktif kasası olan takım bulunamadı.
        </div>
        <VList v-else select-strategy="single-leaf" class="team-list">
          <VListItem
            v-for="t in teamCases"
            :key="t.id"
            :active="selectedTeamId === t.id"
            color="primary"
            rounded="lg"
            class="mb-2"
            @click="selectedTeamId = t.id"
          >
            <template #prepend>
              <VAvatar color="primary" variant="tonal" size="36">
                <VIcon icon="tabler-users" size="20" />
              </VAvatar>
            </template>
            <VListItemTitle class="font-weight-semibold">{{ t.name }}</VListItemTitle>
            <VListItemSubtitle>
              <span :class="t.current_case >= 0 ? 'text-success' : 'text-error'">
                Anlık Kasa: ₺{{ Number(t.current_case).toLocaleString('tr-TR', { minimumFractionDigits: 2 }) }}
              </span>
            </VListItemSubtitle>
            <template #append>
              <VIcon v-if="selectedTeamId === t.id" icon="tabler-circle-check" color="primary" />
              <VIcon v-else icon="tabler-circle" color="surface-variant" />
            </template>
          </VListItem>
        </VList>
      </VCardText>
      <VDivider />
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" :disabled="assigning" @click="showAssignDialog = false">İptal</VBtn>
        <VBtn color="primary" :loading="assigning" :disabled="!selectedTeamId" @click="confirmAssign">
          {{ selectedIds.length }} Çekimi Ata
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Çekimi Başka Takıma Taşı -->
  <VDialog v-model="showMoveDialog" max-width="480">
    <VCard>
      <VCardItem>
        <VCardTitle>Çekimi Başka Takıma Taşı</VCardTitle>
        <template #append>
          <VBtn icon size="small" variant="text" @click="showMoveDialog = false"><VIcon icon="tabler-x" /></VBtn>
        </template>
      </VCardItem>
      <VDivider />
      <VCardText>
        <div class="text-body-2 text-medium-emphasis mb-4">
          Çekim: <strong>{{ moveForm.order_id }}</strong> (#{{ moveForm.id }}) — yalnızca bekleyen çekimler. Pasif (maks. kasa) takımlar da seçilebilir.
        </div>
        <VAutocomplete
          v-model="moveForm.team_id"
          :items="moveTeams.map(t => ({ title: t.status === 1 ? t.name : `${t.name} (pasif)`, value: t.id }))"
          label="Hedef Takım"
          prepend-inner-icon="tabler-users-group"
          density="compact"
        />
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
.receipt-dropzone {
  border: 2px dashed rgba(var(--v-border-color), 0.4);
  border-radius: 8px;
  cursor: pointer;
  transition: all .15s ease;
  background: rgba(var(--v-theme-on-surface), 0.02);
}
.receipt-dropzone:hover, .dropzone-active {
  border-color: rgb(var(--v-theme-primary));
  background: rgba(var(--v-theme-primary), 0.05);
}
.receipt-card { overflow: hidden; }
.ai-badge {
  position: absolute;
  top: 8px;
  right: 8px;
  box-shadow: 0 2px 6px rgba(0,0,0,0.15);
}
.ai-panel { background: rgba(var(--v-theme-on-surface), 0.02); }
.ai-loading {
  display: flex; align-items: center; gap: 10px;
  padding: 16px 0; color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 13px;
}
.ai-status-banner {
  display: flex; align-items: center; gap: 10px;
  padding: 8px 12px;
  border-radius: 8px;
  margin-bottom: 12px;
  font-weight: 600;
}
.ai-status-banner .ai-status-label { font-size: 14px; flex: 1; }
.ai-status-banner .ai-status-score { font-size: 18px; font-weight: 700; }
.ai-status-banner .ai-status-score small { font-size: 11px; font-weight: 500; opacity: .7; }
.ai-status-verified   { background: rgba(var(--v-theme-success), 0.12); color: rgb(var(--v-theme-success)); }
.ai-status-suspicious { background: rgba(var(--v-theme-warning), 0.12); color: rgb(var(--v-theme-warning)); }
.ai-status-rejected   { background: rgba(var(--v-theme-error),   0.12); color: rgb(var(--v-theme-error)); }
.ai-status-pending    { background: rgba(var(--v-theme-on-surface), 0.05); color: rgba(var(--v-theme-on-surface), 0.65); }
.ai-data-grid { display: flex; flex-direction: column; gap: 4px; margin-bottom: 10px; }
.ai-row {
  display: flex; justify-content: space-between; align-items: baseline;
  padding: 6px 8px;
  border-radius: 6px;
  background: rgba(var(--v-theme-on-surface), 0.025);
  font-size: 13px;
}
.ai-row:hover { background: rgba(var(--v-theme-on-surface), 0.045); }
.ai-row .ai-label { color: rgba(var(--v-theme-on-surface), 0.6); font-weight: 500; }
.ai-row .ai-value { font-weight: 600; text-align: right; }
.ai-row .ai-empty { color: rgba(var(--v-theme-on-surface), 0.35); font-weight: 400; }
.ai-row code {
  background: rgba(var(--v-theme-primary), 0.1);
  color: rgb(var(--v-theme-primary));
  padding: 1px 6px;
  border-radius: 4px;
  font-family: 'SF Mono', Menlo, monospace;
  font-size: 12px;
  font-weight: 600;
}
.ai-note-box {
  display: flex; align-items: flex-start; gap: 8px;
  padding: 10px 12px;
  margin: 10px 0;
  background: rgba(var(--v-theme-info), 0.08);
  border-left: 3px solid rgb(var(--v-theme-info));
  border-radius: 4px;
  font-size: 12.5px;
  line-height: 1.45;
}
.ai-note-box .v-icon { margin-top: 2px; color: rgb(var(--v-theme-info)); flex-shrink: 0; }
.ai-checks {
  margin-top: 8px;
  display: flex; flex-direction: column; gap: 4px;
  font-size: 12px;
}
.ai-check-item { display: flex; align-items: center; gap: 6px; color: rgba(var(--v-theme-on-surface), 0.75); }
.receipt-thumb {
  display: block;
  height: 100px;
  background: rgba(var(--v-theme-on-surface), 0.04);
  text-decoration: none;
  color: inherit;
}
.receipt-thumb img {
  width: 100%; height: 100%; object-fit: cover;
}
</style>
