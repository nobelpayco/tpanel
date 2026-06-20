<script setup>
import { useI18n } from 'vue-i18n'

const props = defineProps({
  dateFrom: { type: String, required: true },
  dateTo: { type: String, required: true },
})

const { t } = useI18n()

const loading = ref(true)
const refreshing = ref(false)
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

const formatMoney = (val) => {
  return '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 0, maximumFractionDigits: 0 })
}

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const isToday = computed(() => props.dateFrom === today && props.dateTo === today)

const fetchStats = async () => {
  try {
    const token = localStorage.getItem('token')
    const params = new URLSearchParams({ date_from: props.dateFrom, date_to: props.dateTo })
    const res = await fetch(`/api/dashboard/stats?${params}`, {
      headers: {
        'Accept': 'application/json',
        'Authorization': `Bearer ${token}`,
      },
    })
    if (res.ok) {
      data.value = await res.json()
    }
  } finally {
    loading.value = false
    refreshing.value = false
  }
}

let interval
onMounted(() => {
  fetchStats()
  interval = setInterval(() => {
    if (isToday.value) { refreshing.value = true; fetchStats() }
  }, 10000)
})
onUnmounted(() => clearInterval(interval))

const stats = computed(() => [
  {
    title: 'dashboard.total_deposits',
    stat: formatMoney(data.value.total_deposits),
    icon: 'tabler-arrow-bar-to-down',
    color: 'success',
    subtitle: 'dashboard.today',
  },
  {
    title: 'dashboard.total_withdrawals',
    stat: formatMoney(data.value.total_withdrawals),
    icon: 'tabler-arrow-bar-up',
    color: 'error',
    subtitle: 'dashboard.today',
  },
  {
    title: 'dashboard.pending_deposits',
    stat: data.value.pending_deposits.toLocaleString('tr-TR'),
    icon: 'tabler-clock',
    color: 'warning',
    subtitle: 'common.total',
  },
  {
    title: 'dashboard.pending_withdrawals',
    stat: data.value.pending_withdrawals.toLocaleString('tr-TR'),
    icon: 'tabler-clock-up',
    color: 'info',
    subtitle: formatMoney(data.value.pending_withdrawals_amount),
  },
  {
    title: 'Kullanılabilir IBAN',
    stat: data.value.available_ibans_count.toLocaleString('tr-TR'),
    icon: 'tabler-credit-card',
    color: 'primary',
    subtitle: data.value.available_ibans_count > 0
      ? `Min ${formatMoney(data.value.available_ibans_min)} · Max ${formatMoney(data.value.available_ibans_max)}`
      : '—',
    rawSubtitle: true,
  },
])
</script>

<template>
  <VCol
    v-for="item in stats"
    :key="item.title"
    cols="12"
    sm="6"
    md="4"
    class="stat-col"
  >
    <VCard :loading="loading || refreshing">
      <VCardText class="d-flex justify-space-between">
        <div>
          <p class="text-body-1 mb-1">
            {{ item.rawSubtitle ? item.title : t(item.title) }}
          </p>
          <h4 class="text-h4 font-weight-bold mb-2">
            {{ item.stat }}
          </h4>
          <span class="text-body-2 text-medium-emphasis">{{ item.rawSubtitle ? item.subtitle : t(item.subtitle) }}</span>
        </div>
        <VAvatar
          :color="item.color"
          variant="tonal"
          rounded
          size="44"
        >
          <VIcon
            :icon="item.icon"
            size="28"
          />
        </VAvatar>
      </VCardText>
    </VCard>
  </VCol>
</template>

<style scoped>
/* lg+ ekranlarda 5 kartı tek satıra sığdır (Vuetify 12-col 5'e bölünmüyor — custom flex) */
@media (min-width: 1280px) {
  .stat-col {
    flex: 0 0 20%;
    max-width: 20%;
  }
}
</style>
