<script setup>
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', roles: [1, 4] } })

const { headers } = useApi()
const snackbar = useSnackbar()

const loading = ref(false)
const items = ref([])
const total = ref(0)
const page = ref(1)
const perPage = ref(50)
const counts = ref({ pending: 0, verified: 0, suspicious: 0, rejected: 0 })
const statusFilter = ref('all')

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ page: page.value, per_page: perPage.value })
    if (statusFilter.value && statusFilter.value !== 'all') params.append('status', statusFilter.value)
    const res = await fetch(`/api/withdrawals/receipt-review?${params}`, { headers })
    if (res.ok) {
      const data = await res.json()
      items.value = data.items
      total.value = data.total
      counts.value = data.counts
    } else {
      snackbar.error('Liste yüklenemedi.')
    }
  } finally {
    loading.value = false
  }
}

// Detay açma (mevcut çekim listelerine yönlendirme yerine inline modal)
const showDetailDialog = ref(false)
const selectedItem = ref(null)
const detailReceipts = ref([])
const detailLoading = ref(false)

const openDetail = async (item) => {
  selectedItem.value = item
  showDetailDialog.value = true
  detailLoading.value = true
  detailReceipts.value = []
  try {
    const res = await fetch(`/api/withdrawals/${item.invest_id}/receipts`, { headers })
    if (res.ok) {
      const data = await res.json()
      const list = data.receipts || []
      await Promise.all(list.map(async (r) => {
        try {
          const r2 = await fetch(r.url, { headers })
          if (r2.ok) r.blob_url = URL.createObjectURL(await r2.blob())
        } catch (e) {}
      }))
      detailReceipts.value = list
    }
  } finally { detailLoading.value = false }
}

const totalPages = computed(() => Math.ceil(total.value / perPage.value))

watch([page, statusFilter], fetchData)
onMounted(fetchData)

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const formatDate = val => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}
const formatIban = iban => iban ? iban.replace(/(.{4})/g, '$1 ').trim() : '-'

const rowClass = (item) => {
  const s = item.verification_status
  if (s === 'verified' || s === 'manually_verified') return 'row-verified'
  if (s === 'suspicious') return 'row-suspicious'
  if (s === 'rejected') return 'row-rejected'
  return ''
}

const statusMeta = (s, verifierName = null) => {
  const map = {
    verified:           { color: 'success', icon: 'tabler-shield-check',   label: 'Doğrulandı' },
    manually_verified:  { color: 'success', icon: 'tabler-user-check',     label: verifierName ? `Manuel Doğrulandı (${verifierName})` : 'Manuel Doğrulandı' },
    suspicious:         { color: 'warning', icon: 'tabler-alert-triangle', label: 'Şüpheli' },
    rejected:           { color: 'error',   icon: 'tabler-shield-x',       label: 'Reddedildi' },
  }
  return map[s] || { color: 'grey', icon: 'tabler-help', label: s }
}

const canManualVerify = (status) => status === 'suspicious' || status === 'rejected' || status === 'pending'

const manualVerifying = ref({})
const manualVerify = async (investId, receiptId) => {
  if (!confirm('Bu dekontu manuel olarak doğrulamak istediğinize emin misiniz?')) return
  manualVerifying.value[receiptId] = true
  try {
    const res = await fetch(`/api/withdrawals/${investId}/receipts/${receiptId}/manual-verify`, {
      method: 'POST',
      headers: { ...headers, 'Content-Type': 'application/json' },
    })
    const data = await res.json().catch(() => ({}))
    if (res.ok) {
      snackbar.success(data.message || 'Manuel doğrulandı.')
      await fetchData()
      if (showDetailDialog.value && selectedItem.value) {
        await openDetail(selectedItem.value)
      }
    } else {
      snackbar.error(data.message || 'İşlem başarısız.')
    }
  } catch (e) {
    snackbar.error('Bağlantı hatası.')
  } finally {
    manualVerifying.value[receiptId] = false
  }
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
  if (l.includes('uyumsuz') || l.includes('edilemedi') || l.includes('hata')) return { icon: 'tabler-x', color: 'error' }
  if (l.includes('eşleşiyor') || l.includes('tespit edildi') || l.includes('tanındı')) return { icon: 'tabler-check', color: 'success' }
  return { icon: 'tabler-point', color: 'warning' }
}
</script>

<template>
  <VRow>
    <!-- Özet kartlar -->
    <VCol cols="6" md="3">
      <VCard color="info" variant="tonal" class="pa-4">
        <div class="d-flex align-center gap-3">
          <VIcon icon="tabler-loader-2" size="32" />
          <div>
            <div class="text-caption">Analiz Ediliyor</div>
            <div class="text-h4 font-weight-bold">{{ counts.pending }}</div>
          </div>
        </div>
      </VCard>
    </VCol>
    <VCol cols="6" md="3">
      <VCard color="success" variant="tonal" class="pa-4 cursor-pointer" @click="statusFilter = 'verified'">
        <div class="d-flex align-center gap-3">
          <VIcon icon="tabler-shield-check" size="32" />
          <div>
            <div class="text-caption">Doğrulandı</div>
            <div class="text-h4 font-weight-bold">{{ counts.verified }}</div>
          </div>
        </div>
      </VCard>
    </VCol>
    <VCol cols="6" md="3">
      <VCard color="warning" variant="tonal" class="pa-4 cursor-pointer" @click="statusFilter = 'suspicious'">
        <div class="d-flex align-center gap-3">
          <VIcon icon="tabler-alert-triangle" size="32" />
          <div>
            <div class="text-caption">Şüpheli</div>
            <div class="text-h4 font-weight-bold">{{ counts.suspicious }}</div>
          </div>
        </div>
      </VCard>
    </VCol>
    <VCol cols="6" md="3">
      <VCard color="error" variant="tonal" class="pa-4 cursor-pointer" @click="statusFilter = 'rejected'">
        <div class="d-flex align-center gap-3">
          <VIcon icon="tabler-shield-x" size="32" />
          <div>
            <div class="text-caption">Reddedildi</div>
            <div class="text-h4 font-weight-bold">{{ counts.rejected }}</div>
          </div>
        </div>
      </VCard>
    </VCol>

    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle class="d-flex align-center gap-2">
            <VIcon icon="tabler-shield-search" />
            Dekont Doğrulama
            <VChip color="primary" label size="small">{{ total }}</VChip>
          </VCardTitle>
          <template #append>
            <VBtnToggle v-model="statusFilter" mandatory density="compact" color="primary" variant="outlined">
              <VBtn value="all">Hepsi</VBtn>
              <VBtn value="verified">Doğrulandı</VBtn>
              <VBtn value="suspicious">Şüpheli</VBtn>
              <VBtn value="rejected">Reddedildi</VBtn>
            </VBtnToggle>
          </template>
        </VCardItem>
        <VDivider />

        <VTable class="text-no-wrap review-table">
          <thead>
            <tr>
              <th>Çekim ID</th>
              <th>Takım</th>
              <th>Merchant</th>
              <th>Üye</th>
              <th>IBAN</th>
              <th class="text-end">Tutar</th>
              <th>Sonuç</th>
              <th class="text-center">Skor</th>
              <th>Onay Tarihi</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in items" :key="item.invest_id" :class="rowClass(item)" class="cursor-pointer" @click="openDetail(item)">
              <td><code class="order-code">{{ item.order_id || ('#' + item.invest_id) }}</code></td>
              <td>{{ item.team_name || '-' }}</td>
              <td>{{ item.merchant_name || '-' }}</td>
              <td>
                <div class="font-weight-medium">{{ item.recipient || '-' }}</div>
                <div v-if="item.agent_name" class="text-caption text-medium-emphasis">Agent: {{ item.agent_name }}</div>
              </td>
              <td class="text-body-2">{{ formatIban(item.iban) }}</td>
              <td class="text-end font-weight-bold">{{ formatMoney(item.amount) }}</td>
              <td>
                <VChip :color="statusMeta(item.verification_status, item.manual_verifier_name).color" :prepend-icon="statusMeta(item.verification_status, item.manual_verifier_name).icon" size="small" label>
                  {{ statusMeta(item.verification_status, item.manual_verifier_name).label }}
                </VChip>
              </td>
              <td class="text-center">
                <div class="score-pill" :class="`score-${item.verification_status}`">
                  {{ item.verification_score !== null ? item.verification_score : '—' }}
                </div>
              </td>
              <td class="text-body-2">{{ formatDate(item.verified_at) }}</td>
              <td class="text-end">
                <div class="d-flex align-center justify-end gap-1">
                  <VBtn
                    v-if="canManualVerify(item.verification_status) && item.receipt_id"
                    size="x-small"
                    color="success"
                    variant="tonal"
                    :loading="manualVerifying[item.receipt_id]"
                    prepend-icon="tabler-user-check"
                    @click.stop="manualVerify(item.invest_id, item.receipt_id)"
                  >
                    Doğrula
                  </VBtn>
                  <VBtn icon size="x-small" variant="text" @click.stop="openDetail(item)">
                    <VIcon icon="tabler-eye" size="18" />
                  </VBtn>
                </div>
              </td>
            </tr>
            <tr v-if="!loading && items.length === 0">
              <td colspan="10" class="text-center text-medium-emphasis py-4">Kayıt yok</td>
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

  <!-- Detay Dialog -->
  <VDialog v-model="showDetailDialog" max-width="900">
    <VCard v-if="selectedItem" :loading="detailLoading">
      <VCardItem>
        <VCardTitle class="d-flex align-center justify-space-between">
          <span><code class="order-code">{{ selectedItem.order_id || ('#' + selectedItem.invest_id) }}</code> · {{ selectedItem.recipient }}</span>
          <VBtn icon size="small" variant="text" @click="showDetailDialog = false">
            <VIcon icon="tabler-x" />
          </VBtn>
        </VCardTitle>
      </VCardItem>
      <VDivider />
      <VCardText>
        <VRow class="mb-2">
          <VCol cols="12" md="4"><div class="text-caption text-medium-emphasis">Tutar</div><div class="text-h5 font-weight-bold">{{ formatMoney(selectedItem.amount) }}</div></VCol>
          <VCol cols="12" md="4"><div class="text-caption text-medium-emphasis">IBAN</div><div class="text-body-2">{{ formatIban(selectedItem.iban) }}</div></VCol>
          <VCol cols="12" md="4"><div class="text-caption text-medium-emphasis">Takım</div><div class="text-body-2">{{ selectedItem.team_name || '-' }}</div></VCol>
        </VRow>

        <VDivider class="mb-3" />

        <div v-if="detailLoading" class="text-center py-4">
          <VProgressCircular indeterminate />
        </div>
        <VRow v-else dense>
          <VCol v-for="r in detailReceipts" :key="r.id" cols="12" md="6">
            <VCard variant="outlined" class="receipt-card">
              <div class="position-relative">
                <a :href="r.blob_url || r.url" target="_blank" class="d-block receipt-thumb">
                  <img v-if="r.is_image && r.blob_url" :src="r.blob_url" :alt="r.original_name" />
                  <div v-else class="d-flex flex-column align-center justify-center pa-3">
                    <VIcon :icon="r.is_pdf ? 'tabler-file-type-pdf' : 'tabler-file'" size="32" color="error" />
                    <div class="text-caption mt-1">{{ r.original_name }}</div>
                  </div>
                </a>
              </div>

              <VDivider />
              <div class="pa-3 ai-panel" v-if="r.verification_data || r.verification_notes">
                <div class="ai-status-banner" :class="`ai-status-${r.verification_status === 'manually_verified' ? 'verified' : r.verification_status}`">
                  <VIcon :icon="statusMeta(r.verification_status, r.manual_verifier_name).icon" size="20" />
                  <span class="ai-status-label">{{ statusMeta(r.verification_status, r.manual_verifier_name).label }}</span>
                  <span v-if="r.verification_score !== null" class="ai-status-score">{{ r.verification_score }}<small>/100</small></span>
                </div>

                <div v-if="r.verification_data" class="ai-data-grid">
                  <div class="ai-row"><span class="ai-label">Tutar</span><span class="ai-value">{{ r.verification_data.amount !== null ? '₺' + Number(r.verification_data.amount).toLocaleString('tr-TR', {minimumFractionDigits: 2}) : '—' }}</span></div>
                  <div class="ai-row"><span class="ai-label">IBAN Son 4</span><span class="ai-value"><code v-if="r.verification_data.iban_last4">{{ r.verification_data.iban_last4 }}</code><span v-else>—</span></span></div>
                  <div class="ai-row"><span class="ai-label">Alıcı</span><span class="ai-value">{{ r.verification_data.recipient_name || '—' }}</span></div>
                  <div v-if="r.verification_data.sender_name" class="ai-row"><span class="ai-label">Gönderen</span><span class="ai-value">{{ r.verification_data.sender_name }}</span></div>
                  <div class="ai-row"><span class="ai-label">Banka</span><span class="ai-value">{{ r.verification_data.bank_name || '—' }}</span></div>
                  <div v-if="r.verification_data.transaction_id" class="ai-row"><span class="ai-label">Ref No</span><span class="ai-value"><code>{{ r.verification_data.transaction_id }}</code></span></div>
                </div>

                <div v-if="parsedNotes(r).aiNote" class="ai-note-box">
                  <VIcon icon="tabler-message-circle" size="14" />
                  <span>{{ parsedNotes(r).aiNote }}</span>
                </div>
                <div v-if="parsedNotes(r).checks.length" class="ai-checks">
                  <div v-for="(it, i) in parsedNotes(r).checks" :key="i" class="ai-check-item">
                    <VIcon :icon="checkMeta(it).icon" :color="checkMeta(it).color" size="14" />
                    <span>{{ it }}</span>
                  </div>
                </div>
              </div>

              <VDivider v-if="canManualVerify(r.verification_status)" />
              <div v-if="canManualVerify(r.verification_status)" class="pa-3 d-flex justify-end manual-verify-bar">
                <VBtn
                  size="small"
                  color="success"
                  variant="flat"
                  :loading="manualVerifying[r.id]"
                  prepend-icon="tabler-user-check"
                  @click="manualVerify(selectedItem.invest_id, r.id)"
                >
                  Manuel Doğrula
                </VBtn>
              </div>
            </VCard>
          </VCol>
        </VRow>
      </VCardText>
    </VCard>
  </VDialog>
</template>

<style scoped>
.review-table :deep(tbody tr) { transition: background-color .12s ease; }
.review-table :deep(tbody tr.row-verified)   { background-color: rgba(76, 175, 80, 0.08); }
.review-table :deep(tbody tr.row-suspicious) { background-color: rgba(255, 167, 38, 0.12); }
.review-table :deep(tbody tr.row-rejected)   { background-color: rgba(239, 68, 68, 0.10); }
.review-table :deep(tbody tr.row-verified:hover)   { background-color: rgba(76, 175, 80, 0.14); }
.review-table :deep(tbody tr.row-suspicious:hover) { background-color: rgba(255, 167, 38, 0.18); }
.review-table :deep(tbody tr.row-rejected:hover)   { background-color: rgba(239, 68, 68, 0.16); }

.order-code {
  font-family: 'SF Mono', Menlo, monospace;
  background: rgba(var(--v-theme-primary), 0.1);
  color: rgb(var(--v-theme-primary));
  padding: 1px 6px;
  border-radius: 4px;
  font-size: 12px;
  font-weight: 600;
}

.score-pill {
  display: inline-block;
  min-width: 44px;
  padding: 4px 10px;
  border-radius: 16px;
  font-weight: 700;
  font-size: 13px;
}
.score-verified   { background: rgb(var(--v-theme-success)); color: white; }
.score-suspicious { background: rgb(var(--v-theme-warning)); color: white; }
.score-rejected   { background: rgb(var(--v-theme-error));   color: white; }

/* AI panel (detay modal) — withdrawals/all.vue ile aynı */
.receipt-card { overflow: hidden; }
.receipt-thumb { display: block; height: 100px; background: rgba(var(--v-theme-on-surface), 0.04); }
.receipt-thumb img { width: 100%; height: 100%; object-fit: cover; }
.ai-panel { background: rgba(var(--v-theme-on-surface), 0.02); }
.ai-status-banner { display: flex; align-items: center; gap: 10px; padding: 8px 12px; border-radius: 8px; margin-bottom: 12px; font-weight: 600; }
.ai-status-banner .ai-status-label { font-size: 14px; flex: 1; }
.ai-status-banner .ai-status-score { font-size: 18px; font-weight: 700; }
.ai-status-banner .ai-status-score small { font-size: 11px; font-weight: 500; opacity: .7; }
.ai-status-verified   { background: rgba(var(--v-theme-success), 0.12); color: rgb(var(--v-theme-success)); }
.ai-status-suspicious { background: rgba(var(--v-theme-warning), 0.12); color: rgb(var(--v-theme-warning)); }
.ai-status-rejected   { background: rgba(var(--v-theme-error),   0.12); color: rgb(var(--v-theme-error)); }
.ai-data-grid { display: flex; flex-direction: column; gap: 4px; margin-bottom: 10px; }
.ai-row { display: flex; justify-content: space-between; padding: 6px 8px; border-radius: 6px; background: rgba(var(--v-theme-on-surface), 0.025); font-size: 13px; }
.ai-row .ai-label { color: rgba(var(--v-theme-on-surface), 0.6); font-weight: 500; }
.ai-row .ai-value { font-weight: 600; }
.ai-row code { background: rgba(var(--v-theme-primary), 0.1); color: rgb(var(--v-theme-primary)); padding: 1px 6px; border-radius: 4px; font-family: monospace; font-size: 12px; }
.ai-note-box { display: flex; gap: 8px; padding: 10px 12px; margin: 10px 0; background: rgba(var(--v-theme-info), 0.08); border-left: 3px solid rgb(var(--v-theme-info)); border-radius: 4px; font-size: 12.5px; }
.ai-note-box .v-icon { margin-top: 2px; color: rgb(var(--v-theme-info)); flex-shrink: 0; }
.ai-checks { margin-top: 8px; display: flex; flex-direction: column; gap: 4px; font-size: 12px; }
.ai-check-item { display: flex; align-items: center; gap: 6px; color: rgba(var(--v-theme-on-surface), 0.75); }
.manual-verify-bar { background: rgba(var(--v-theme-success), 0.04); }
</style>
