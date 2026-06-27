import { ref } from 'vue'

// Dashboard stat kartları için paylaşılan tekil veri kaynağı.
// 5 ayrı stat kutucuğu tek /api/dashboard/stats çağrısını paylaşır.
const data = ref({
  total_deposits: 0,
  total_withdrawals: 0,
  pending_deposits: 0,
  pending_withdrawals: 0,
  pending_withdrawals_amount: 0,
  available_ibans_count: 0,
  available_ibans_min: 0,
  available_ibans_max: 0,
})
const loading = ref(true)

const from = ref(null)
const to = ref(null)
let timer = null

const todayStr = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const fetchNow = async () => {
  if (!from.value || !to.value) return
  try {
    const token = localStorage.getItem('token')
    const params = new URLSearchParams({ date_from: from.value, date_to: to.value })
    const res = await fetch(`/api/dashboard/stats?${params}`, {
      headers: { Accept: 'application/json', Authorization: `Bearer ${token}` },
    })
    if (res.ok) data.value = await res.json()
  } finally {
    loading.value = false
  }
}

// Tarih aralığını ayarla + hemen çek; bugünse 10 sn'de bir yenile.
export const setStatsRange = (f, t) => {
  from.value = f
  to.value = t
  fetchNow()
  if (!timer) {
    timer = setInterval(() => { if (from.value === todayStr() && to.value === todayStr()) fetchNow() }, 10000)
  }
}

export const stopStats = () => {
  if (timer) { clearInterval(timer); timer = null }
}

export const useDashboardStats = () => ({ data, loading })
