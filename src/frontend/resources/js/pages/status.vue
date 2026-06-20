<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { getBrand } from '@/composables/useBrand'

const brand = getBrand()

definePage({
  meta: {
    layout: 'blank',
    public: true,
  },
})

const loading = ref(false)
const data = ref(null)
const error = ref(null)
const lastChecked = ref(null)
const refreshIn = ref(10)
let pollInterval = null
let countdownInterval = null

const fetchHealth = async () => {
  loading.value = true
  error.value = null
  try {
    const res = await fetch('/api/v1/health', { headers: { Accept: 'application/json' } })
    data.value = await res.json()
    lastChecked.value = new Date()
  } catch (e) {
    error.value = e.message || 'Network error'
    data.value = null
  } finally {
    loading.value = false
    refreshIn.value = 10
  }
}

onMounted(() => {
  fetchHealth()
  pollInterval = setInterval(fetchHealth, 10_000)
  countdownInterval = setInterval(() => {
    if (refreshIn.value > 0) refreshIn.value -= 1
  }, 1000)
})
onUnmounted(() => {
  clearInterval(pollInterval)
  clearInterval(countdownInterval)
})

const overall = computed(() => {
  if (error.value) return 'down'
  return data.value?.status || 'unknown'
})

const overallColor = computed(() => {
  if (overall.value === 'ok') return 'success'
  if (overall.value === 'down') return 'error'
  return 'warning'
})

const overallLabel = computed(() => {
  if (overall.value === 'ok') return 'Tüm Sistemler Çalışıyor'
  if (overall.value === 'down') return 'Servis Kesintisi'
  return 'Kontrol Ediliyor…'
})

const dbStatus = computed(() => data.value?.services?.database?.status || 'unknown')
const dbLatency = computed(() => data.value?.services?.database?.latency_ms)
const apiStatus = computed(() => data.value?.services?.api?.status || 'unknown')

const formatTime = d => d ? d.toLocaleTimeString('tr-TR') : '-'
const formatIso = iso => iso ? new Date(iso).toLocaleString('tr-TR') : '-'
const statusChipColor = s => s === 'ok' ? 'success' : (s === 'error' ? 'error' : 'warning')
const statusChipLabel = s => s === 'ok' ? 'Çalışıyor' : (s === 'error' ? 'Hata' : '-')
</script>

<template>
  <div class="status-page">
    <div class="status-container">
      <header class="status-header">
        <h1 class="status-brand">{{ brand }} Status</h1>
        <p class="status-sub">Sistem ve API sağlık durumu — her 10 saniyede bir güncellenir.</p>
      </header>

      <div class="overall-card" :class="`overall-${overallColor}`">
        <div class="overall-icon">
          <VIcon
            :icon="overall === 'ok' ? 'tabler-circle-check-filled' : overall === 'down' ? 'tabler-alert-octagon-filled' : 'tabler-loader-2'"
            size="44"
          />
        </div>
        <div class="overall-body">
          <div class="overall-title">{{ overallLabel }}</div>
          <div class="overall-meta">
            <span v-if="lastChecked">Son kontrol: {{ formatTime(lastChecked) }}</span>
            <span class="dot" />
            <span>{{ refreshIn }} sn içinde yenilenecek</span>
          </div>
        </div>
        <VBtn variant="text" :loading="loading" prepend-icon="tabler-refresh" @click="fetchHealth">Yenile</VBtn>
      </div>

      <div class="service-grid">
        <div class="service-card">
          <div class="service-head">
            <VIcon icon="tabler-server-bolt" size="20" />
            <span class="service-name">API</span>
            <VChip :color="statusChipColor(apiStatus)" size="x-small" label class="ms-auto">{{ statusChipLabel(apiStatus) }}</VChip>
          </div>
          <div class="service-meta">Merchant API v1 endpoint'leri</div>
        </div>

        <div class="service-card">
          <div class="service-head">
            <VIcon icon="tabler-database" size="20" />
            <span class="service-name">Veritabanı</span>
            <VChip :color="statusChipColor(dbStatus)" size="x-small" label class="ms-auto">{{ statusChipLabel(dbStatus) }}</VChip>
          </div>
          <div class="service-meta">
            <span v-if="dbLatency !== null && dbLatency !== undefined">Ping: <strong>{{ dbLatency }} ms</strong></span>
            <span v-else>—</span>
          </div>
        </div>
      </div>

      <div class="metrics-grid" v-if="data">
        <div class="metric-card">
          <div class="metric-label">Sunucu Saati</div>
          <div class="metric-value">{{ data.time ? new Date(data.time).toLocaleTimeString('tr-TR') : '—' }}</div>
          <div class="metric-sub">{{ data.version || 'v1' }}</div>
        </div>
      </div>

      <footer class="status-footer">
        <a href="/developer" class="footer-link">
          <VIcon icon="tabler-book" size="16" />
          API Dokümantasyonu
        </a>
        <span class="dot" />
        <code class="footer-endpoint">GET /api/v1/health</code>
      </footer>
    </div>
  </div>
</template>

<style scoped>
.status-page {
  min-height: 100vh;
  background: linear-gradient(180deg, #f6f8fc 0%, #eef2f8 100%);
  display: flex;
  justify-content: center;
  padding: 48px 16px;
}
.status-container {
  width: 100%;
  max-width: 880px;
}
.status-header {
  text-align: center;
  margin-bottom: 32px;
}
.status-brand {
  font-size: 2rem;
  font-weight: 700;
  letter-spacing: -0.02em;
  margin: 0;
  color: rgba(var(--v-theme-on-surface), 0.92);
}
.status-sub {
  margin: 8px 0 0;
  color: rgba(var(--v-theme-on-surface), 0.6);
  font-size: 0.95rem;
}

.overall-card {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 20px 24px;
  border-radius: 14px;
  background: #fff;
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  box-shadow: 0 1px 2px rgba(0,0,0,0.04);
  margin-bottom: 20px;
  border-left-width: 5px;
}
.overall-card.overall-success { border-left-color: rgb(var(--v-theme-success)); }
.overall-card.overall-error   { border-left-color: rgb(var(--v-theme-error)); }
.overall-card.overall-warning { border-left-color: rgb(var(--v-theme-warning)); }
.overall-card.overall-success .overall-icon { color: rgb(var(--v-theme-success)); }
.overall-card.overall-error   .overall-icon { color: rgb(var(--v-theme-error)); }
.overall-card.overall-warning .overall-icon { color: rgb(var(--v-theme-warning)); }

.overall-body { flex: 1; }
.overall-title {
  font-size: 1.25rem;
  font-weight: 600;
  color: rgba(var(--v-theme-on-surface), 0.92);
}
.overall-meta {
  margin-top: 4px;
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
  color: rgba(var(--v-theme-on-surface), 0.6);
}
.overall-meta .dot {
  width: 4px; height: 4px; border-radius: 50%;
  background: rgba(var(--v-theme-on-surface), 0.3);
}

.service-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 12px;
  margin-bottom: 20px;
}
.service-card {
  background: #fff;
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 10px;
  padding: 14px 16px;
}
.service-head {
  display: flex;
  align-items: center;
  gap: 10px;
}
.service-name {
  font-weight: 600;
  color: rgba(var(--v-theme-on-surface), 0.88);
}
.service-meta {
  margin-top: 6px;
  font-size: 0.85rem;
  color: rgba(var(--v-theme-on-surface), 0.6);
}

.metrics-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px;
  margin-bottom: 24px;
}
.metric-card {
  background: #fff;
  border: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  border-radius: 10px;
  padding: 14px 16px;
}
.metric-label {
  font-size: 0.78rem;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: rgba(var(--v-theme-on-surface), 0.55);
}
.metric-value {
  font-size: 1.5rem;
  font-weight: 700;
  margin-top: 4px;
  color: rgba(var(--v-theme-on-surface), 0.92);
}
.metric-sub {
  margin-top: 2px;
  font-size: 0.78rem;
  color: rgba(var(--v-theme-on-surface), 0.55);
}

.status-footer {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  font-size: 0.85rem;
  color: rgba(var(--v-theme-on-surface), 0.55);
  padding: 16px 0;
}
.status-footer .dot {
  width: 4px; height: 4px; border-radius: 50%;
  background: rgba(var(--v-theme-on-surface), 0.3);
}
.footer-link {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  color: inherit;
  text-decoration: none;
}
.footer-link:hover { color: rgb(var(--v-theme-primary)); }
.footer-endpoint {
  font-family: ui-monospace, monospace;
  background: rgba(var(--v-theme-on-surface), 0.06);
  padding: 2px 8px;
  border-radius: 6px;
  font-size: 0.78rem;
}
</style>
