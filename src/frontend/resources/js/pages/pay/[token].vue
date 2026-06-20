<script setup>
import { onMounted, onUnmounted, ref, computed } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { getBrand } from '@/composables/useBrand'
import { useSnackbar } from '@/composables/useSnackbar'
import LanguageSwitcher from '@/components/LanguageSwitcher.vue'

definePage({
  meta: {
    layout: 'blank',
    public: true,
  },
})

const brand = getBrand()
const route = useRoute()
const token = route.params.token
const { t, locale } = useI18n()
const snackbar = useSnackbar()

// Locale tespiti: localStorage > tarayıcı dili > tr default
const detectLocale = () => {
  const saved = localStorage.getItem('locale')
  if (saved && ['tr', 'en', 'ru', 'ur'].includes(saved)) return saved
  const browser = (navigator.language || 'tr').split('-')[0].toLowerCase()
  return ['tr', 'en', 'ru', 'ur'].includes(browser) ? browser : 'tr'
}
locale.value = detectLocale()

const apiHeaders = () => ({ 'Accept': 'application/json', 'Accept-Language': locale.value })

const loading = ref(true)
const submitting = ref(false)
const tx = ref(null)
const errorMsg = ref('')
const uploadingReceipt = ref(false)

const status = computed(() => tx.value?.status)
const isFinalized = computed(() => ['approved', 'rejected'].includes(status.value))
const isProcessing = computed(() => status.value === 'processing')
const showSelectedBank = computed(() => tx.value && tx.value.bank)
const noBankAvailable = computed(() => tx.value && tx.value.iban_seen === 0 && !tx.value.bank)

const formatMoney = v => Number(v || 0).toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' TL'
const formatIban = iban => iban ? iban.replace(/(.{4})/g, '$1 ').trim() : '-'

let pollTimer = null
let redirectTimer = null
let expiryTimer = null
const redirectCountdown = ref(8)
const expirySecondsLeft = ref(null)
const copied = ref('')

const expiryTotalSeconds = ref(0)

const startExpiryCountdown = () => {
  if (expiryTimer) clearInterval(expiryTimer)
  if (!tx.value?.expires_at) { expirySecondsLeft.value = null; return }
  // Total süre = (expires_at - created_at), milisaniye → saniye.
  // tx.created_at server tarafında oluşma anı; expires_at bunu + expiryMinutes.
  // pay endpoint'i created_at göndermiyor ama biz client'a yansıyan ilk fetch'te
  // expires_at'a göre kalan süreyi total kabul ederek progress yüzdesini hesaplarız.
  const target = new Date(tx.value.expires_at).getTime()
  const initial = Math.max(1, Math.floor((target - Date.now()) / 1000))
  if (expiryTotalSeconds.value < initial) expiryTotalSeconds.value = initial
  const compute = () => {
    const left = Math.max(0, Math.floor((target - Date.now()) / 1000))
    expirySecondsLeft.value = left
    if (left === 0) {
      clearInterval(expiryTimer)
      fetchStatus()
    }
  }
  compute()
  expiryTimer = setInterval(compute, 1000)
}
const expiryDisplay = computed(() => {
  if (expirySecondsLeft.value === null) return null
  const m = Math.floor(expirySecondsLeft.value / 60)
  const s = expirySecondsLeft.value % 60
  return `${m}:${String(s).padStart(2, '0')}`
})
const expiryPercent = computed(() => {
  if (expirySecondsLeft.value === null || expiryTotalSeconds.value <= 0) return 0
  return Math.max(0, Math.min(100, (expirySecondsLeft.value / expiryTotalSeconds.value) * 100))
})
const expiryUrgency = computed(() => {
  if (expirySecondsLeft.value === null) return 'normal'
  if (expirySecondsLeft.value <= 60) return 'critical'
  if (expirySecondsLeft.value <= 180) return 'warning'
  return 'normal'
})

const goTo = (url) => {
  if (url) window.location.href = url
}

const startRedirect = (url) => {
  if (!url || redirectTimer) return
  redirectCountdown.value = 8
  redirectTimer = setInterval(() => {
    redirectCountdown.value--
    if (redirectCountdown.value <= 0) {
      clearInterval(redirectTimer)
      goTo(url)
    }
  }, 1000)
}
const copy = (text, key) => {
  navigator.clipboard?.writeText(text)
  copied.value = key
  snackbar.success(t('pay.copied'))
  setTimeout(() => { if (copied.value === key) copied.value = '' }, 1200)
}

const fetchStatus = async () => {
  try {
    const res = await fetch(`/api/v1/pay/${token}`, { headers: apiHeaders() })
    const data = await res.json()
    if (res.ok && data.data) {
      tx.value = data.data
      if (data.data.status === 'approved' && data.data.success_url) startRedirect(data.data.success_url)
      if (data.data.status === 'rejected' && data.data.fail_url) startRedirect(data.data.fail_url)
      if (data.data.expires_at && !isFinalized.value) startExpiryCountdown()
    } else {
      errorMsg.value = data.message || t('pay.err_load')
    }
  } catch {
    errorMsg.value = t('pay.err_server')
  } finally {
    loading.value = false
  }
}

const receiptInput = ref(null)
const openReceiptPicker = () => receiptInput.value?.click()

const uploadReceipt = async (e) => {
  const file = e.target.files?.[0]
  if (!file) return
  if (file.size > 10 * 1024 * 1024) {
    alert(t('pay.err_max_size'))
    e.target.value = ''
    return
  }
  const mime = file.type || ''
  if (!mime.startsWith('image/') && mime !== 'application/pdf') {
    alert(t('pay.err_only_image_pdf'))
    e.target.value = ''
    return
  }
  uploadingReceipt.value = true
  try {
    const fd = new FormData()
    fd.append('receipt', file)
    const res = await fetch(`/api/v1/pay/${token}/receipt`, {
      method: 'POST',
      headers: apiHeaders(),
      body: fd,
    })
    const data = await res.json()
    if (res.ok) {
      await fetchStatus()
    } else {
      alert(data.message || t('pay.err_upload'))
    }
  } catch {
    alert(t('pay.err_server'))
  } finally {
    uploadingReceipt.value = false
    if (receiptInput.value) receiptInput.value.value = ''
  }
}

const markPaid = async () => {
  submitting.value = true
  try {
    await fetch(`/api/v1/pay/${token}/paid`, {
      method: 'POST',
      headers: apiHeaders(),
    })
    await fetchStatus()
  } finally {
    submitting.value = false
  }
}

onMounted(async () => {
  await fetchStatus()
  pollTimer = setInterval(() => {
    if (tx.value && !isFinalized.value && tx.value.iban_seen === 1) {
      fetchStatus()
    }
  }, 10000)
})

onUnmounted(() => {
  if (pollTimer) clearInterval(pollTimer)
  if (redirectTimer) clearInterval(redirectTimer)
  if (expiryTimer) clearInterval(expiryTimer)
})
</script>

<template>
  <div class="pay-page">
    <div class="pay-card">
      <div class="pay-card-lang">
        <LanguageSwitcher />
      </div>
      <!-- Logo -->
      <div class="pay-logo">
        <div class="pay-logo-mark">
          <svg width="48" height="48" viewBox="0 0 34 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path fill-rule="evenodd" clip-rule="evenodd"
              d="M0.00183571 0.3125V7.59485C0.00183571 7.59485 -0.141502 9.88783 2.10473 11.8288L14.5469 23.6837L21.0172 23.6005L19.9794 10.8126L17.5261 7.93369L9.81536 0.3125H0.00183571Z"
              fill="#4F46E5" />
            <path fill-rule="evenodd" clip-rule="evenodd"
              d="M8.25781 17.6914L25.1339 0.3125H33.9991V7.62657C33.9991 7.62657 33.8144 10.0645 32.5743 11.3686L21.0179 23.6875H14.5487L8.25781 17.6914Z"
              fill="#F59E0B" />
          </svg>
        </div>
        <div class="pay-logo-text">
          <span class="pay-logo-name">{{ brand }}</span>
          <span class="pay-logo-sub">Havale</span>
        </div>
      </div>

      <!-- Expiry countdown -->
      <div v-if="!loading && !isFinalized && expiryDisplay" :class="['pay-expiry', `pay-expiry--${expiryUrgency}`]">
        <div class="pay-expiry-row">
          <svg class="pay-expiry-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <circle cx="12" cy="12" r="9" stroke="currentColor" stroke-width="2" />
            <path d="M12 7v5l3 2" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
          </svg>
          <span class="pay-expiry-label">Link Süresi</span>
          <span class="pay-expiry-time">{{ expiryDisplay }}</span>
        </div>
        <div class="pay-expiry-track">
          <div class="pay-expiry-fill" :style="{ width: expiryPercent + '%' }" />
        </div>
      </div>

      <!-- Loading -->
      <div v-if="loading" class="pay-loading">
        <div class="spinner"></div>
      </div>

      <!-- Not found -->
      <div v-else-if="errorMsg && !tx" class="pay-error">
        <div class="error-icon">!</div>
        <p class="pay-error-msg">{{ errorMsg }}</p>
      </div>

      <!-- Approved -->
      <div v-else-if="status === 'approved'" class="pay-status pay-status-success">
        <div class="status-icon success">✓</div>
        <h2>{{ t('pay.approved_title') }}</h2>
        <p>{{ t('pay.approved_sub') }}</p>
        <p v-if="tx.success_url" class="redirect-msg">{{ t('pay.redirect_msg', { n: redirectCountdown }) }}</p>
        <button v-if="tx.success_url" class="btn-primary mt-3" @click="goTo(tx.success_url)">
          {{ t('pay.go_site') }} →
        </button>
      </div>

      <!-- Rejected -->
      <div v-else-if="status === 'rejected'" class="pay-status pay-status-error">
        <div class="status-icon error">✕</div>
        <h2>{{ t('pay.rejected_title') }}</h2>
        <p>{{ t('pay.rejected_sub') }}</p>
        <p v-if="tx.fail_url" class="redirect-msg">{{ t('pay.redirect_msg', { n: redirectCountdown }) }}</p>
        <button v-if="tx.fail_url" class="btn-dark mt-3" @click="goTo(tx.fail_url)">
          ← {{ t('pay.go_site') }}
        </button>
      </div>

      <!-- Uygun banka bulunamadı -->
      <div v-else-if="noBankAvailable && !isFinalized" class="pay-status">
        <div class="status-icon" style="background: #fef3c7; color: #b45309;">⏱</div>
        <h2>{{ t('pay.no_bank_title') }}</h2>
        <p>{{ t('pay.no_bank_sub') }}</p>
      </div>

      <!-- Bank info shown / waiting -->
      <div v-else-if="showSelectedBank && !isFinalized" class="pay-info">
        <p class="warning-text">{{ t('pay.warning') }}</p>

        <h3 class="info-title">{{ t('pay.info_title') }}</h3>

        <div class="info-box">
          <div class="info-row">
            <span class="info-label">{{ t('pay.label_amount') }}</span>
            <button class="info-value copy-btn" @click="copy(tx.amount, 'amount')">
              <span>{{ formatMoney(tx.amount) }}</span>
              <span class="copy-icon">{{ copied === 'amount' ? '✓' : '⧉' }}</span>
            </button>
          </div>
          <div class="info-row">
            <span class="info-label">{{ t('pay.label_holder') }}</span>
            <button class="info-value copy-btn" @click="copy(tx.bank.account_holder, 'name')">
              <span>{{ tx.bank.account_holder }}</span>
              <span class="copy-icon">{{ copied === 'name' ? '✓' : '⧉' }}</span>
            </button>
          </div>
          <div class="info-row">
            <span class="info-label">{{ t('pay.label_iban') }}</span>
            <button class="info-value copy-btn" @click="copy(tx.bank.account_iban?.replace(/\s+/g, ''), 'iban')">
              <span class="iban-text">{{ formatIban(tx.bank.account_iban) }}</span>
              <span class="copy-icon">{{ copied === 'iban' ? '✓' : '⧉' }}</span>
            </button>
          </div>
        </div>

        <p class="instruction-red">{{ t('pay.fast_warning') }}</p>
        <p class="instruction-sub">{{ t('pay.after_payment') }}</p>

        <div v-if="isProcessing" class="processing-banner">
          <div class="processing-spinner"></div>
          <div>
            <div class="processing-title">{{ t('pay.checking_title') }}</div>
            <div class="processing-sub">{{ t('pay.checking_sub') }}</div>
          </div>
        </div>

        <!-- Dekont yükleme -->
        <input
          ref="receiptInput"
          type="file"
          accept="image/*,application/pdf"
          style="display: none"
          @change="uploadReceipt"
        />
        <div class="receipt-row">
          <button class="btn-receipt" :disabled="uploadingReceipt" @click="openReceiptPicker">
            <span class="receipt-icon">📎</span>
            <span v-if="tx.has_receipt">{{ t('pay.change_receipt') }}</span>
            <span v-else>{{ uploadingReceipt ? t('pay.uploading') : t('pay.upload_receipt') }}</span>
          </button>
          <span v-if="tx.has_receipt" class="receipt-uploaded">{{ t('pay.receipt_uploaded') }}</span>
        </div>
        <p class="receipt-hint">{{ t('pay.receipt_hint') }}</p>

        <button v-if="!isProcessing" class="btn-dark btn-primary-action" :disabled="submitting" @click="markPaid">
          {{ t('pay.btn_paid') }}
        </button>
      </div>
    </div>

    <div class="pay-footer">© {{ brand }}</div>
  </div>
</template>

<style scoped>
/* === Link Süresi Sayacı (kompakt) === */
.pay-expiry {
  margin: 0 0 12px;
  padding: 6px 10px;
  border-radius: 8px;
  background: linear-gradient(135deg, #f0f9ff 0%, #e0f2fe 100%);
  border: 1px solid #bae6fd;
  color: #0369a1;
  transition: background 0.3s, border-color 0.3s, color 0.3s;
}
.pay-expiry--warning {
  background: linear-gradient(135deg, #fffbeb 0%, #fef3c7 100%);
  border-color: #fcd34d;
  color: #b45309;
}
.pay-expiry--critical {
  background: linear-gradient(135deg, #fff1f2 0%, #ffe4e6 100%);
  border-color: #fca5a5;
  color: #b91c1c;
  animation: pay-expiry-pulse 1.4s ease-in-out infinite;
}
@keyframes pay-expiry-pulse {
  0%, 100% { box-shadow: 0 0 0 0 rgba(239, 68, 68, 0.25); }
  50%      { box-shadow: 0 0 0 4px rgba(239, 68, 68, 0); }
}
.pay-expiry-row {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 4px;
}
.pay-expiry-icon {
  flex-shrink: 0;
  opacity: 0.85;
  width: 14px;
  height: 14px;
}
.pay-expiry-label {
  font-size: 11px;
  font-weight: 500;
  letter-spacing: 0.02em;
  flex: 1;
  text-transform: uppercase;
  opacity: 0.8;
}
.pay-expiry-time {
  font-size: 13px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  letter-spacing: 0.01em;
}
.pay-expiry-track {
  height: 3px;
  background: rgba(0, 0, 0, 0.06);
  border-radius: 999px;
  overflow: hidden;
}
.pay-expiry-fill {
  height: 100%;
  background: currentColor;
  border-radius: 999px;
  transition: width 1s linear;
  opacity: 0.85;
}
.pay-page {
  min-height: 100vh;
  background: #eef1f6;
  color: #1a1d24;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 24px 16px;
  position: relative;
}
.pay-card-lang {
  position: absolute;
  top: 12px;
  right: 12px;
  z-index: 5;
}
.pay-card-lang :deep(.v-btn) {
  min-width: unset !important;
  padding: 0 8px !important;
  height: 36px !important;
  width: 44px !important;
}
/* Sadece bayrak görünsün — label ve chevron gizli */
.pay-card-lang :deep(.v-btn .text-body-1),
.pay-card-lang :deep(.v-btn .v-icon) {
  display: none !important;
}
.pay-card-lang :deep(.v-btn span:first-child) {
  font-size: 1.5rem !important;
  margin: 0 !important;
  line-height: 1;
}

.pay-card {
  width: 100%;
  max-width: 460px;
  background: #ffffff;
  border-radius: 16px;
  box-shadow: 0 10px 40px rgba(15, 23, 42, 0.08);
  padding: 32px 28px;
  position: relative;
}

.pay-logo {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  margin-bottom: 28px;
}
.pay-logo-mark { display: grid; place-items: center; }
.pay-logo-text {
  display: flex;
  flex-direction: column;
  line-height: 1.1;
}
.pay-logo-name {
  font-size: 1.8rem;
  font-weight: 800;
  color: #4F46E5;
  letter-spacing: -0.02em;
}
.pay-logo-sub {
  font-size: 1.4rem;
  font-weight: 700;
  color: #F59E0B;
  letter-spacing: -0.02em;
}

.pay-loading {
  display: grid;
  place-items: center;
  padding: 40px 0;
}
.spinner {
  width: 36px;
  height: 36px;
  border: 3px solid #e5e7eb;
  border-top-color: #4F46E5;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }

.pay-error { text-align: center; padding: 24px 0; }
.error-icon {
  width: 56px; height: 56px;
  background: #fee2e2; color: #dc2626;
  border-radius: 50%; font-size: 2rem; font-weight: 700;
  display: grid; place-items: center;
  margin: 0 auto 12px;
}
.pay-error-msg { color: #6b7280; }

.pay-status { text-align: center; padding: 16px 0; }
.status-icon {
  width: 64px; height: 64px;
  border-radius: 50%; font-size: 2.2rem; font-weight: 700;
  display: grid; place-items: center;
  margin: 0 auto 16px;
}
.status-icon.success { background: #dcfce7; color: #16a34a; }
.status-icon.error   { background: #fee2e2; color: #dc2626; }
.pay-status h2 {
  font-size: 1.25rem;
  font-weight: 700;
  margin-bottom: 8px;
}
.pay-status p {
  color: #6b7280;
  font-size: 0.95rem;
  line-height: 1.5;
}
.redirect-msg {
  margin-top: 8px;
  font-size: 0.85rem !important;
  color: #9ca3af !important;
  font-style: italic;
}
.mt-3 { margin-top: 16px; }

/* Bank list */
.amount-display { text-align: center; margin-bottom: 20px; }
.amount-label { font-size: 0.85rem; color: #374151; font-weight: 600; margin-bottom: 4px; }
.amount-value { font-size: 1.75rem; font-weight: 800; color: #1a1d24; letter-spacing: -0.02em; }

.pay-note { font-size: 0.92rem; color: #4b5563; margin-bottom: 12px; }
.bank-options { display: flex; flex-direction: column; gap: 8px; margin-bottom: 16px; }
.bank-option {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 14px;
  border: 1.5px solid #e5e7eb;
  border-radius: 10px;
  cursor: pointer;
  transition: all 0.15s;
}
.bank-option:hover { border-color: #c7d2fe; background: #fafbff; }
.bank-option.selected {
  border-color: #4F46E5;
  background: #f3f4ff;
}
.bank-option input[type="radio"] { accent-color: #4F46E5; }
.bank-info { display: flex; flex-direction: column; }
.bank-name { font-weight: 600; font-size: 0.95rem; }
.bank-holder { font-size: 0.82rem; color: #6b7280; }

/* Info display */
.warning-text {
  color: #dc2626;
  font-size: 0.92rem;
  font-weight: 600;
  text-align: center;
  margin-bottom: 20px;
  line-height: 1.45;
}

.info-title {
  font-size: 1.05rem;
  font-weight: 700;
  margin-bottom: 10px;
  color: #1a1d24;
}
.info-box {
  background: #f5f7fb;
  border-radius: 10px;
  padding: 6px 14px;
  margin-bottom: 18px;
}
.info-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 10px 0;
  border-bottom: 1px solid rgba(15, 23, 42, 0.06);
}
.info-row:last-child { border-bottom: none; }
.info-label {
  font-size: 0.92rem;
  font-weight: 700;
  color: #1a1d24;
  flex-shrink: 0;
}
.copy-btn {
  background: none;
  border: none;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.95rem;
  color: #1a1d24;
  font-weight: 500;
  text-align: right;
  padding: 4px 6px;
  border-radius: 4px;
  transition: background 0.15s;
}
.copy-btn:hover { background: rgba(79, 70, 229, 0.08); }
.copy-btn .iban-text {
  font-family: ui-monospace, "SF Mono", Menlo, monospace;
  font-size: 0.88rem;
  letter-spacing: 0.02em;
}
.copy-icon {
  font-size: 1rem;
  opacity: 0.6;
  display: inline-block;
  min-width: 16px;
  text-align: center;
}

.instruction-red {
  color: #dc2626;
  font-weight: 600;
  text-align: center;
  font-size: 0.95rem;
  margin-bottom: 6px;
}
.instruction-sub {
  text-align: center;
  font-size: 0.88rem;
  color: #4b5563;
  margin-bottom: 18px;
  line-height: 1.45;
}

.pay-alert {
  border-radius: 8px;
  padding: 10px 12px;
  font-size: 0.88rem;
  margin-bottom: 14px;
}

/* Processing banner */
.processing-banner {
  display: flex;
  align-items: center;
  gap: 14px;
  background: linear-gradient(135deg, #fef3c7 0%, #fde68a 100%);
  border: 1.5px solid #f59e0b;
  border-radius: 12px;
  padding: 16px 18px;
  margin-bottom: 18px;
  box-shadow: 0 4px 14px rgba(245, 158, 11, 0.15);
}
.processing-spinner {
  flex-shrink: 0;
  width: 32px;
  height: 32px;
  border: 3px solid rgba(245, 158, 11, 0.25);
  border-top-color: #b45309;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
.processing-title {
  font-size: 1.05rem;
  font-weight: 700;
  color: #78350f;
  line-height: 1.2;
}
.processing-sub {
  font-size: 0.85rem;
  color: #92400e;
  margin-top: 2px;
}

/* Receipt upload */
.receipt-row {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  margin-bottom: 4px;
  flex-wrap: wrap;
}
.receipt-hint { text-align: center; }
.btn-receipt {
  background: #f3f4ff;
  color: #4F46E5;
  border: 1.5px dashed #c7d2fe;
  border-radius: 8px;
  padding: 10px 14px;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 6px;
  transition: all 0.15s;
}
.btn-receipt:hover:not(:disabled) {
  background: #e7eaff;
  border-style: solid;
}
.btn-receipt:disabled { opacity: 0.6; cursor: not-allowed; }
.receipt-icon { font-size: 1.05rem; }
.receipt-uploaded {
  font-size: 0.85rem;
  font-weight: 600;
  color: #16a34a;
}
.receipt-hint {
  font-size: 0.78rem;
  color: #6b7280;
  margin-bottom: 16px;
  margin-top: 4px;
  line-height: 1.4;
}
.pay-alert-info  { background: #eff6ff; color: #1e40af; }
.pay-alert-error { background: #fef2f2; color: #b91c1c; }

/* Buttons */
.btn-primary {
  display: block;
  width: 100%;
  padding: 14px 18px;
  background: #4F46E5;
  color: white;
  border: none;
  border-radius: 10px;
  font-size: 0.98rem;
  font-weight: 600;
  cursor: pointer;
  transition: opacity 0.15s, transform 0.05s;
}
.btn-primary:hover:not(:disabled) { opacity: 0.92; }
.btn-primary:active { transform: scale(0.99); }
.btn-primary:disabled { opacity: 0.55; cursor: not-allowed; }

.btn-dark {
  display: block;
  width: 100%;
  padding: 15px 18px;
  background: #1f2238;
  color: white;
  border: none;
  border-radius: 10px;
  font-size: 0.98rem;
  font-weight: 600;
  cursor: pointer;
  transition: opacity 0.15s, transform 0.05s;
}
.btn-primary-action {
  background: linear-gradient(135deg, #2d2a5e 0%, #1f2238 100%);
  margin-bottom: 10px;
  letter-spacing: 0.01em;
}
.btn-secondary-action {
  background: #2a2d3e;
}
.btn-dark:hover:not(:disabled) { opacity: 0.92; }
.btn-dark:active { transform: scale(0.99); }
.btn-dark:disabled { opacity: 0.55; cursor: not-allowed; }

.pay-footer {
  margin-top: 28px;
  font-size: 0.82rem;
  color: #9ca3af;
}

@media (max-width: 540px) {
  .pay-card { padding: 24px 18px; max-width: 100%; }
  .pay-logo-name { font-size: 1.55rem; }
  .pay-logo-sub  { font-size: 1.2rem; }
  .amount-value  { font-size: 1.5rem; }
}
</style>
