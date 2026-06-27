<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default' } })

const { t, locale } = useI18n()
const { headers } = useApi()
const snackbar = useSnackbar()

const loading = ref(false)
const withdrawals = ref([])
const total = ref(0)
const totalAmount = ref(0)
const page = ref(1)
const perPage = ref(50)

const merchants = ref([])
const teams = ref([])

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({ dateFormat: 'Y-m-d', altInput: true, altFormat: 'd.m.Y', locale: localeMap[locale.value] || Turkish }))

const filters = ref({
  id: '', status: 0, merchant: null, team: null,
  name: '', player_id: '', order_id: '', u_id: '',
  min_amount: '', max_amount: '', date_from: '', date_to: '',
  missing_receipt: false, added_type: 0,
})

const showFilterDialog = ref(false)
const showDetailDialog = ref(false)
const detailData = ref(null)
const detailLoading = ref(false)

const selectedWithdraw = ref(null)
const receipts = ref([])
const receiptsLoading = ref(false)
const uploadingReceipt = ref(false)
const dragOver = ref(false)
const fileInputRef = ref(null)

const canManageReceipts = computed(() => {
  const user = JSON.parse(localStorage.getItem('user') || '{}')
  return [1, 2, 4, 5].includes(Number(user.user_type))
})

const isAdmin = computed(() => {
  const user = JSON.parse(localStorage.getItem('user') || '{}')
  return user.user_type == 1
})
const isTeamMember = computed(() => {
  const user = JSON.parse(localStorage.getItem('user') || '{}')
  return user.user_type == 2 || user.user_type == 5
})
const isMerchant = computed(() => {
  const user = JSON.parse(localStorage.getItem('user') || '{}')
  return user.user_type == 3
})
// Manuel Çekim yalnızca Super Admin (1) ve Sub Admin (4)
const canManualWithdraw = computed(() => {
  const user = JSON.parse(localStorage.getItem('user') || '{}')
  return user.user_type == 1 || user.user_type == 4
})

const monthStart = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-01` })()
const showExportDialog = ref(false)
const exportLoading = ref(false)
const exportForm = ref({
  date_from: monthStart,
  date_to: today,
  type: '2',
  status: 'all',
  merchant_id: null,
  team_id: null,
})
const exportStatusOptions = [
  { title: 'Hepsi', value: 'all' },
  { title: 'Bekliyor', value: '1' },
  { title: 'İşlemde', value: '2' },
  { title: 'Onaylandı', value: '3' },
  { title: 'Reddedildi', value: '4' },
]

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
      snackbar.success(data.message || 'Export başlatıldı. Bildirimlerden indirebilirsiniz.')
    } else {
      snackbar.error(data.message || 'Export başlatılamadı.')
    }
  } catch {
    snackbar.error('Sunucu hatası.')
  } finally {
    exportLoading.value = false
  }
}

const resendingId = ref(null)
const resendCallback = async (id) => {
  if (!confirm('Bu işlem için callback yeniden gönderilsin mi?')) return
  resendingId.value = id
  try {
    const res = await fetch(`/api/withdrawals/${id}/resend-callback`, { method: 'POST', headers })
    const data = await res.json()
    if (res.ok) snackbar.success(data.message || 'Callback gönderildi.')
    else snackbar.error(data.message || 'Gönderilemedi.')
  } catch {
    snackbar.error('Sunucu hatası.')
  } finally {
    resendingId.value = null
  }
}

// --- Manuel Çekim Ekle ---
const showManualDialog = ref(false)
const manualSaving = ref(false)
const manualMetaLoading = ref(false)
const manualTeamLoading = ref(false)
const manualMerchants = ref([])
const manualTeams = ref([])
const manualBanks = ref([])
const manualAgents = ref([])
const manualForm = ref({ merchant_id: null, team_id: null, bank_id: null, agent_id: null, name: '', amount: '', iban: '' })

const openManualDialog = async () => {
  manualForm.value = { merchant_id: null, team_id: null, bank_id: null, agent_id: null, name: '', amount: '', iban: '' }
  manualBanks.value = []
  manualAgents.value = []
  showManualDialog.value = true
  manualMetaLoading.value = true
  try {
    const res = await fetch('/api/withdrawals/manual/meta', { headers })
    if (res.ok) {
      const data = await res.json()
      manualMerchants.value = data.merchants || []
      manualTeams.value = data.teams || []
      if (manualTeams.value.length === 1) manualForm.value.team_id = manualTeams.value[0].id
    } else {
      snackbar.error('Liste yüklenemedi.')
    }
  } catch {
    snackbar.error('Sunucu hatası.')
  } finally {
    manualMetaLoading.value = false
  }
}

watch(() => manualForm.value.team_id, async (teamId) => {
  manualForm.value.bank_id = null
  manualForm.value.agent_id = null
  manualBanks.value = []
  manualAgents.value = []
  if (!teamId) return
  manualTeamLoading.value = true
  try {
    const res = await fetch(`/api/withdrawals/manual/team/${teamId}`, { headers })
    if (res.ok) {
      const data = await res.json()
      manualBanks.value = data.banks || []
      manualAgents.value = data.agents || []
    }
  } catch {
    snackbar.error('Banka/agent listesi yüklenemedi.')
  } finally {
    manualTeamLoading.value = false
  }
})

const submitManual = async () => {
  if (!manualForm.value.merchant_id) { snackbar.error('Merchant seçin.'); return }
  if (!manualForm.value.team_id) { snackbar.error('Takım seçin.'); return }
  if (!manualForm.value.name?.trim()) { snackbar.error('Müşteri adı girin.'); return }
  if (!manualForm.value.amount || Number(manualForm.value.amount) <= 0) { snackbar.error('Geçerli bir tutar girin.'); return }
  if (!manualForm.value.iban?.trim()) { snackbar.error('IBAN girin.'); return }
  manualSaving.value = true
  try {
    const res = await fetch('/api/withdrawals/manual', {
      method: 'POST', headers,
      body: JSON.stringify({
        merchant_id: manualForm.value.merchant_id,
        team_id: manualForm.value.team_id,
        bank_id: manualForm.value.bank_id,
        agent_id: manualForm.value.agent_id,
        name: manualForm.value.name.trim(),
        amount: Number(manualForm.value.amount),
        iban: manualForm.value.iban.trim(),
      }),
    })
    const data = await res.json()
    if (res.ok) {
      showManualDialog.value = false
      snackbar.success(data.message || 'Manuel çekim eklendi.')
      page.value = 1
      fetchData()
    } else {
      snackbar.error(data.message || 'Eklenemedi.')
    }
  } catch {
    snackbar.error('Sunucu hatası.')
  } finally {
    manualSaving.value = false
  }
}

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const formatDate = val => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}
const formatIban = iban => iban ? iban.replace(/(.{4})/g, '$1 ').trim() : '-'

const statusLabels = { 0: 'Havuzda', 1: 'Beklemede', 2: 'İşlemde', 3: 'Onaylandı', 4: 'Reddedildi' }
const statusColors = { 0: 'secondary', 1: 'warning', 2: 'info', 3: 'success', 4: 'error' }

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ page: page.value, per_page: perPage.value })
    Object.entries(filters.value).forEach(([k, v]) => {
      if (v !== '' && v !== null && v !== 0) params.append(k, v)
    })
    const res = await fetch(`/api/withdrawals/all?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      withdrawals.value = data.withdrawals
      total.value = data.total
      totalAmount.value = data.total_amount
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

const applyFilters = () => {
  page.value = 1
  showFilterDialog.value = false
  fetchData()
}

const resetFilters = () => {
  filters.value = {
    id: '', status: 0, merchant: null, team: null,
    name: '', player_id: '', order_id: '', u_id: '',
    min_amount: '', max_amount: '', date_from: '', date_to: '',
    missing_receipt: false, added_type: 0,
  }
  page.value = 1
  fetchData()
}

const openDetail = async (d) => {
  selectedWithdraw.value = d
  showDetailDialog.value = true
  detailData.value = null
  detailLoading.value = true
  receipts.value = []
  try {
    const [detailRes] = await Promise.all([
      fetch(`/api/withdrawals/${d.id}/detail`, { headers }),
      canManageReceipts.value ? loadReceipts(d.id) : Promise.resolve(),
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
  if (!showDetailDialog.value || !canManageReceipts.value) return
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
    setTimeout(() => loadReceipts(id), 20000)
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

const totalPages = computed(() => Math.ceil(total.value / perPage.value))

onMounted(() => {
  fetchData()
  fetchMeta()
})

watch(page, fetchData)

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

const historyStatusLabel = (h) => statusLabels[h.status] || '-'
const historyStatusColor = (h) => statusColors[h.status] || 'default'

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

// ---- Onaylı çekimi reddet (yalnız Süper Admin, sebep zorunlu, callback YOK) ----
const showForceReject = ref(false)
const forceForm = ref({ id: null, order_id: '', reason: '' })
const forceSaving = ref(false)
const openForceReject = (w) => { forceForm.value = { id: w.id, order_id: w.order_id, reason: '' }; showForceReject.value = true }
const submitForceReject = async () => {
  if (!forceForm.value.reason.trim()) { snackbar.error('Ret sebebi zorunludur.'); return }
  forceSaving.value = true
  try {
    const res = await fetch(`/api/withdrawals/${forceForm.value.id}/force-reject`, {
      method: 'POST', headers, body: JSON.stringify({ reason: forceForm.value.reason.trim() }),
    })
    const data = await res.json()
    if (res.ok) { showForceReject.value = false; snackbar.success(data.message || 'Reddedildi.'); fetchData() }
    else { snackbar.error(data.message || 'İşlem reddedilemedi.') }
  } catch { snackbar.error('Sunucu hatası.') } finally { forceSaving.value = false }
}
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle class="d-flex align-center gap-2">
            Tüm Çekimler
            <VChip color="primary" label size="small">{{ total }} kayıt</VChip>
            <VChip color="error" label size="small">{{ formatMoney(totalAmount) }}</VChip>
          </VCardTitle>
          <template #append>
            <div class="d-flex gap-2">
              <VBtn v-if="canManualWithdraw" color="primary" prepend-icon="tabler-plus" @click="openManualDialog">
                Manuel Çekim
              </VBtn>
              <VBtn color="info" variant="outlined" prepend-icon="tabler-filter" @click="showFilterDialog = true">
                Filtrele
              </VBtn>
              <VBtn color="success" variant="outlined" prepend-icon="tabler-file-spreadsheet" @click="showExportDialog = true">
                Excel
              </VBtn>
              <VBtn color="secondary" variant="text" icon @click="resetFilters">
                <VIcon icon="tabler-refresh" />
              </VBtn>
            </div>
          </template>
        </VCardItem>
        <VDivider />

        <VTable class="text-no-wrap" density="compact">
          <thead>
            <tr>
              <th>ID</th>
              <th v-if="!isTeamMember">Merchant</th>
              <th>Takım</th>
              <th>Order ID</th>
              <th>Müşteri</th>
              <th>IBAN</th>
              <th class="text-end">Tutar</th>
              <th>Durum</th>
              <th>Tarih</th>
              <th v-if="canManageReceipts" class="text-center">Dekont</th>
              <th class="text-end"></th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="w in withdrawals"
              :key="w.id"
              class="cursor-pointer"
              :class="{ 'row-approved-no-receipt': canManageReceipts && w.receipt_warning }"
              @click="openDetail(w)"
            >
              <td>{{ w.id }}</td>
              <td v-if="!isTeamMember">{{ w.merchant_name || '-' }}</td>
              <td>{{ w.team_name }}</td>
              <td class="text-caption">{{ w.order_id || '-' }}</td>
              <td>
                <div class="font-weight-medium">{{ w.name || '-' }}</div>
                <div v-if="w.player_id" class="text-caption text-medium-emphasis">{{ w.player_id }}</div>
              </td>
              <td class="text-body-2">{{ formatIban(w.iban) }}</td>
              <td class="text-end font-weight-bold">{{ formatMoney(w.amount) }}</td>
              <td>
                <VChip :color="statusColors[w.status]" label size="x-small">
                  {{ statusLabels[w.status] }}
                </VChip>
              </td>
              <td>
                <div class="text-body-2">{{ formatDate(w.created_at) }}</div>
              </td>
              <td v-if="canManageReceipts" class="text-center">
                <VBtn
                  :color="w.receipt_count > 0 ? 'success' : 'warning'"
                  :variant="w.receipt_count > 0 ? 'tonal' : 'flat'"
                  size="small"
                  density="comfortable"
                  prepend-icon="tabler-paperclip"
                  @click.stop="openDetail(w)"
                >
                  Dekont Yükle ({{ w.receipt_count || 0 }})
                </VBtn>
              </td>
              <td class="text-end">
                <VBtn icon size="x-small" variant="text" @click.stop="openDetail(w)">
                  <VIcon icon="tabler-eye" size="18" />
                </VBtn>
                <VBtn
                  v-if="isAdmin && [3, 4].includes(Number(w.status))"
                  icon size="x-small" variant="text" color="info"
                  :loading="resendingId === w.id"
                  title="Callback tekrar gönder"
                  @click.stop="resendCallback(w.id)"
                >
                  <VIcon icon="tabler-refresh-dot" size="18" />
                </VBtn>
                <VBtn
                  v-if="canManualWithdraw && [1, 2].includes(Number(w.status))"
                  icon size="x-small" variant="text" color="warning"
                  title="Başka takıma taşı"
                  @click.stop="openMoveDialog(w)"
                >
                  <VIcon icon="tabler-arrows-exchange-2" size="18" />
                </VBtn>
                <VBtn
                  v-if="isAdmin && Number(w.status) === 3"
                  icon size="x-small" variant="text" color="error"
                  title="Onaylı işlemi reddet (callback gönderilmez)"
                  @click.stop="openForceReject(w)"
                >
                  <VIcon icon="tabler-ban" size="18" />
                </VBtn>
              </td>
            </tr>
            <tr v-if="!loading && withdrawals.length === 0">
              <td :colspan="(isTeamMember ? 9 : 10) + (canManageReceipts ? 1 : 0)" class="text-center text-medium-emphasis py-4">Kayıt yok</td>
            </tr>
          </tbody>
        </VTable>

        <VDivider />
        <div class="d-flex align-center justify-space-between pa-3">
          <div class="text-caption text-medium-emphasis">
            Sayfa {{ page }} / {{ totalPages || 1 }} ({{ total }} toplam)
          </div>
          <VPagination v-model="page" :length="totalPages || 1" :total-visible="7" />
        </div>
      </VCard>
    </VCol>
  </VRow>

  <!-- Filtre Dialog -->
  <VDialog v-model="showFilterDialog" max-width="650">
    <VCard title="Filtrele">
      <VCardText>
        <VRow>
          <VCol cols="12" md="6">
            <AppTextField v-model="filters.id" type="number" label="ID" density="compact" />
          </VCol>
          <VCol cols="12" md="6">
            <VSelect
              v-model="filters.status"
              :items="[
                { title: 'Hepsi', value: 0 },
                { title: 'Havuzda', value: 0 },
                { title: 'Beklemede', value: 1 },
                { title: 'İşlemde', value: 2 },
                { title: 'Onaylandı', value: 3 },
                { title: 'Reddedildi', value: 4 },
              ]"
              label="Durum"
              density="compact"
            />
          </VCol>

          <VCol v-if="!isTeamMember" cols="12" md="6">
            <VAutocomplete
              v-model="filters.merchant"
              :items="[{ title: 'Hepsi', value: null }, ...merchants.map(m => ({ title: m.name, value: m.id }))]"
              label="Merchant"
              density="compact"
              clearable
            />
          </VCol>
          <VCol v-if="isAdmin" cols="12" md="6">
            <VAutocomplete
              v-model="filters.team"
              :items="[{ title: 'Hepsi', value: null }, ...teams.map(tm => ({ title: tm.name, value: tm.id }))]"
              label="Takım"
              density="compact"
              clearable
            />
          </VCol>

          <VCol cols="12" md="6">
            <AppTextField v-model="filters.name" label="Ad Soyad" density="compact" />
          </VCol>
          <VCol cols="12" md="6">
            <AppTextField v-model="filters.player_id" label="Player ID" density="compact" />
          </VCol>

          <VCol cols="12" md="6">
            <AppTextField v-model="filters.order_id" label="Order ID" density="compact" />
          </VCol>
          <VCol cols="12" md="6">
            <AppTextField v-model="filters.u_id" label="U ID" density="compact" />
          </VCol>

          <VCol cols="6" md="3">
            <AppTextField v-model="filters.min_amount" type="number" label="Min Tutar" prefix="₺" density="compact" />
          </VCol>
          <VCol cols="6" md="3">
            <AppTextField v-model="filters.max_amount" type="number" label="Max Tutar" prefix="₺" density="compact" />
          </VCol>

          <VCol cols="12" md="6">
            <AppDateTimePicker v-model="filters.date_from" label="Başlangıç" :config="dateConfig" density="compact" />
          </VCol>
          <VCol cols="12" md="6">
            <AppDateTimePicker v-model="filters.date_to" label="Bitiş" :config="dateConfig" density="compact" />
          </VCol>

          <VCol cols="12" md="6">
            <VSelect
              v-model="filters.added_type"
              :items="[
                { title: 'Tümü', value: 0 },
                { title: 'Otomatik', value: 1 },
                { title: 'Manuel', value: 2 },
              ]"
              label="Kaynak"
              density="compact"
            />
          </VCol>

          <VCol v-if="canManageReceipts" cols="12">
            <VSwitch
              v-model="filters.missing_receipt"
              color="warning"
              label="Sadece dekontu yüklenmeyen onaylı çekimler (sarı satır)"
              density="compact"
              hide-details
            />
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions>
        <VBtn variant="text" color="error" @click="resetFilters(); showFilterDialog = false">Temizle</VBtn>
        <VSpacer />
        <VBtn variant="text" @click="showFilterDialog = false">İptal</VBtn>
        <VBtn color="primary" @click="applyFilters">Uygula</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Excel Export Dialog -->
  <VDialog v-model="showExportDialog" max-width="500">
    <VCard title="Excel İndir (Çekimler)">
      <VCardText>
        <VRow>
          <VCol cols="6">
            <AppDateTimePicker v-model="exportForm.date_from" label="Başlangıç" :config="dateConfig" density="compact" />
          </VCol>
          <VCol cols="6">
            <AppDateTimePicker v-model="exportForm.date_to" label="Bitiş" :config="dateConfig" density="compact" />
          </VCol>
          <VCol cols="12">
            <VSelect v-model="exportForm.status" :items="exportStatusOptions" label="Durum" density="compact" />
          </VCol>
          <VCol v-if="!isTeamMember && !isMerchant" cols="12">
            <VAutocomplete
              v-model="exportForm.merchant_id"
              :items="[{ title: 'Hepsi', value: null }, ...merchants.map(m => ({ title: m.name, value: m.id }))]"
              label="Merchant"
              density="compact"
              clearable
            />
          </VCol>
          <VCol v-if="!isTeamMember && !isMerchant" cols="12">
            <VAutocomplete
              v-model="exportForm.team_id"
              :items="[{ title: 'Hepsi', value: null }, ...teams.map(t => ({ title: t.name, value: t.id }))]"
              label="Takım"
              density="compact"
              clearable
            />
          </VCol>
        </VRow>
        <div class="text-caption text-medium-emphasis mt-2">
          Export hazır olunca üst menüdeki bildirim çanından indirebilirsiniz. Tarih filtresi sonuçlanma tarihine (pending ise oluşturma tarihine) göre çalışır.
        </div>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showExportDialog = false">İptal</VBtn>
        <VBtn color="success" :loading="exportLoading" prepend-icon="tabler-file-spreadsheet" @click="submitExport">
          Başlat
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Manuel Çekim Dialog -->
  <VDialog v-model="showManualDialog" max-width="640">
    <VCard :loading="manualMetaLoading">
      <VCardItem>
        <template #prepend>
          <VAvatar color="primary" variant="tonal" rounded>
            <VIcon icon="tabler-cash-banknote-off" />
          </VAvatar>
        </template>
        <VCardTitle>Manuel Çekim Ekle</VCardTitle>
        <VCardSubtitle>Onaylanmış (status=3) olarak kaydedilir, callback gönderilmez</VCardSubtitle>
        <template #append>
          <VBtn icon size="small" variant="text" @click="showManualDialog = false">
            <VIcon icon="tabler-x" />
          </VBtn>
        </template>
      </VCardItem>
      <VDivider />
      <VCardText>
        <VRow>
          <VCol cols="12">
            <VAutocomplete
              v-model="manualForm.merchant_id"
              :items="manualMerchants.map(m => ({ title: m.name, value: m.id }))"
              label="Site (Merchant)"
              prepend-inner-icon="tabler-building-store"
              density="compact"
            />
          </VCol>
          <VCol cols="12">
            <VAutocomplete
              v-model="manualForm.team_id"
              :items="manualTeams.map(tm => ({ title: tm.name, value: tm.id }))"
              label="Takım"
              prepend-inner-icon="tabler-users-group"
              density="compact"
            />
          </VCol>
          <VCol cols="12">
            <VAutocomplete
              v-model="manualForm.bank_id"
              :items="manualBanks.map(b => ({ title: b.name, value: b.id }))"
              label="Banka Hesabı (ödenen)"
              prepend-inner-icon="tabler-building-bank"
              density="compact"
              :disabled="!manualForm.team_id"
              :loading="manualTeamLoading"
              clearable
            />
          </VCol>
          <VCol cols="12">
            <VAutocomplete
              v-model="manualForm.agent_id"
              :items="manualAgents.map(a => ({ title: a.name, value: a.id }))"
              label="Takım Agent"
              prepend-inner-icon="tabler-user"
              density="compact"
              :disabled="!manualForm.team_id"
              :loading="manualTeamLoading"
              clearable
            />
          </VCol>
          <VCol cols="12">
            <AppTextField v-model="manualForm.iban" label="IBAN (alıcı)" prepend-inner-icon="tabler-credit-card" density="compact" />
          </VCol>
          <VCol cols="12" md="6">
            <AppTextField v-model="manualForm.name" label="Müşteri Adı" prepend-inner-icon="tabler-user" density="compact" />
          </VCol>
          <VCol cols="12" md="6">
            <AppTextField v-model="manualForm.amount" type="number" label="Tutar" prefix="₺" density="compact" />
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showManualDialog = false">İptal</VBtn>
        <VBtn color="primary" prepend-icon="tabler-device-floppy" :loading="manualSaving" @click="submitManual">
          Kaydet
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Detay Dialog -->
  <VDialog v-model="showDetailDialog" max-width="950">
    <VCard v-if="detailData" :loading="detailLoading">
      <VCardItem>
        <VCardTitle class="d-flex align-center justify-space-between">
          <span># ID {{ detailData.withdraw.id }} — {{ detailData.withdraw.name || '-' }}</span>
          <VBtn icon size="small" variant="text" @click="showDetailDialog = false">
            <VIcon icon="tabler-x" />
          </VBtn>
        </VCardTitle>
      </VCardItem>
      <VDivider />
      <VCardText>
        <VRow>
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
              <tr><th>Durum</th><td><VChip :color="statusColors[detailData.withdraw.status]" label size="small">{{ statusLabels[detailData.withdraw.status] }}</VChip></td></tr>
              <tr><th>Oluşturulma</th><td>{{ formatDate(detailData.withdraw.created_at) }}</td></tr>
              <tr><th>IBAN</th><td>{{ formatIban(detailData.withdraw.iban) }}</td></tr>
              <tr v-if="!isTeamMember"><th>Merchant</th><td>{{ detailData.withdraw.merchant_name || '-' }}</td></tr>
            </table>
          </VCol>
        </VRow>

        <template v-if="canManageReceipts">
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

                  <div v-if="r.verification_status === 'pending'" class="ai-loading">
                    <VProgressCircular indeterminate size="20" width="2" color="primary" />
                    <span>Görsel analiz ediliyor…</span>
                  </div>

                  <template v-else>
                    <div class="ai-status-banner" :class="`ai-status-${r.verification_status}`">
                      <VIcon :icon="verificationMeta(r).icon" size="20" />
                      <span class="ai-status-label">{{ verificationMeta(r).label }}</span>
                      <span v-if="r.verification_score !== null" class="ai-status-score">{{ r.verification_score }}<small>/100</small></span>
                    </div>

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

                    <div v-if="parsedNotes(r).aiNote" class="ai-note-box">
                      <VIcon icon="tabler-message-circle" size="14" />
                      <span>{{ parsedNotes(r).aiNote }}</span>
                    </div>

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
        </template>

        <div v-if="detailData.history" class="mt-4">
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
        <VSpacer />
        <VBtn variant="text" @click="showDetailDialog = false">Kapat</VBtn>
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

  <!-- Onaylı Çekimi Reddet (Süper Admin) -->
  <VDialog v-model="showForceReject" max-width="480">
    <VCard>
      <VCardItem>
        <VCardTitle class="text-error d-flex align-center gap-2"><VIcon icon="tabler-ban" />Onaylı Çekimi Reddet</VCardTitle>
        <template #append>
          <VBtn icon size="small" variant="text" @click="showForceReject = false"><VIcon icon="tabler-x" /></VBtn>
        </template>
      </VCardItem>
      <VDivider />
      <VCardText>
        <VAlert type="warning" variant="tonal" density="compact" class="mb-4">
          Onaylı çekim <strong>{{ forceForm.order_id }}</strong> (#{{ forceForm.id }}) reddedilecek.
          Müşteriye <strong>callback gönderilmez</strong>. Bu işlem geri alınamaz.
        </VAlert>
        <AppTextField
          v-model="forceForm.reason"
          label="Ret Sebebi (zorunlu)"
          placeholder="Örn. hatalı onay, mutabakat düzeltmesi…"
          density="compact"
        />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showForceReject = false">İptal</VBtn>
        <VBtn color="error" prepend-icon="tabler-ban" :loading="forceSaving" @click="submitForceReject">Reddet</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>

<style scoped>
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
.ai-loading { display: flex; align-items: center; gap: 10px; padding: 16px 0; color: rgba(var(--v-theme-on-surface), 0.6); font-size: 13px; }
.ai-status-banner { display: flex; align-items: center; gap: 10px; padding: 8px 12px; border-radius: 8px; margin-bottom: 12px; font-weight: 600; }
.ai-status-banner .ai-status-label { font-size: 14px; flex: 1; }
.ai-status-banner .ai-status-score { font-size: 18px; font-weight: 700; }
.ai-status-banner .ai-status-score small { font-size: 11px; font-weight: 500; opacity: .7; }
.ai-status-verified   { background: rgba(var(--v-theme-success), 0.12); color: rgb(var(--v-theme-success)); }
.ai-status-suspicious { background: rgba(var(--v-theme-warning), 0.12); color: rgb(var(--v-theme-warning)); }
.ai-status-rejected   { background: rgba(var(--v-theme-error),   0.12); color: rgb(var(--v-theme-error)); }
.ai-status-pending    { background: rgba(var(--v-theme-on-surface), 0.05); color: rgba(var(--v-theme-on-surface), 0.65); }
.ai-data-grid { display: flex; flex-direction: column; gap: 4px; margin-bottom: 10px; }
.ai-row { display: flex; justify-content: space-between; align-items: baseline; padding: 6px 8px; border-radius: 6px; background: rgba(var(--v-theme-on-surface), 0.025); font-size: 13px; }
.ai-row:hover { background: rgba(var(--v-theme-on-surface), 0.045); }
.ai-row .ai-label { color: rgba(var(--v-theme-on-surface), 0.6); font-weight: 500; }
.ai-row .ai-value { font-weight: 600; text-align: right; }
.ai-row .ai-empty { color: rgba(var(--v-theme-on-surface), 0.35); font-weight: 400; }
.ai-row code { background: rgba(var(--v-theme-primary), 0.1); color: rgb(var(--v-theme-primary)); padding: 1px 6px; border-radius: 4px; font-family: 'SF Mono', Menlo, monospace; font-size: 12px; font-weight: 600; }
.ai-note-box { display: flex; align-items: flex-start; gap: 8px; padding: 10px 12px; margin: 10px 0; background: rgba(var(--v-theme-info), 0.08); border-left: 3px solid rgb(var(--v-theme-info)); border-radius: 4px; font-size: 12.5px; line-height: 1.45; }
.ai-note-box .v-icon { margin-top: 2px; color: rgb(var(--v-theme-info)); flex-shrink: 0; }
.ai-checks { margin-top: 8px; display: flex; flex-direction: column; gap: 4px; font-size: 12px; }
.ai-check-item { display: flex; align-items: center; gap: 6px; color: rgba(var(--v-theme-on-surface), 0.75); }
.receipt-thumb {
  display: block;
  height: 100px;
  background: rgba(var(--v-theme-on-surface), 0.04);
  text-decoration: none;
  color: inherit;
}
.receipt-thumb img { width: 100%; height: 100%; object-fit: cover; }

:deep(tr.row-approved-no-receipt) {
  background-color: rgba(255, 235, 130, 0.25) !important;
}
:deep(tr.row-approved-no-receipt:hover) {
  background-color: rgba(255, 220, 100, 0.35) !important;
}
</style>
