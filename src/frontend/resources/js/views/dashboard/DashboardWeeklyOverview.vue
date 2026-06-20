<script setup>
import { useI18n } from 'vue-i18n'

const props = defineProps({
  dateFrom: { type: String, required: true },
  dateTo: { type: String, required: true },
})

const { t } = useI18n()

const loading = ref(true)
const refreshing = ref(false)
const data = ref({ items: [], total_case: 0, type: 'merchant' })

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 0 })
const formatDuration = (seconds) => {
  if (!seconds) return '-'
  const min = Math.floor(seconds / 60)
  const sec = seconds % 60
  return `${min}dk ${sec}sn`
}

const fetchData = async () => {
  try {
    const token = localStorage.getItem('token')
    const params = new URLSearchParams({ date_from: props.dateFrom, date_to: props.dateTo })
    const res = await fetch(`/api/dashboard/merchant-cases?${params}`, {
      headers: { 'Accept': 'application/json', 'Authorization': `Bearer ${token}` },
    })
    if (res.ok) data.value = await res.json()
  } finally {
    loading.value = false
    refreshing.value = false
  }
}

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const isToday = computed(() => props.dateFrom === today && props.dateTo === today)

let interval
onMounted(() => {
  fetchData()
  interval = setInterval(() => {
    if (isToday.value) { refreshing.value = true; fetchData() }
  }, 10000)
})
onUnmounted(() => clearInterval(interval))

const caseColor = val => val >= 0 ? 'success' : 'error'
</script>

<template>
  <VCard :loading="loading || refreshing">
    <VCardItem>
      <VCardTitle>{{ t('dashboard.merchant_cases') }}</VCardTitle>
      <VCardSubtitle>{{ t('dashboard.today') }}</VCardSubtitle>

      <template #append>
        <h4 class="text-h4 font-weight-bold" :class="`text-${caseColor(data.total_case)}`">
          {{ formatMoney(data.total_case) }}
        </h4>
      </template>
    </VCardItem>

    <VDivider />

    <VTable
      class="text-no-wrap"
      density="compact"
    >
      <thead>
        <tr>
          <th>{{ t('merchants.name') }}</th>
          <th class="text-end">{{ t('dashboard.carryover') }}</th>
          <th class="text-end">{{ t('dashboard.total_deposits') }}</th>
          <th class="text-end">{{ t('dashboard.total_withdrawals') }}</th>
          <th class="text-end">Ort. Onay</th>
          <th class="text-end">{{ t('nav.cases') }}</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="m in data.items"
          :key="m.name"
        >
          <td class="font-weight-medium">
            {{ m.name }}
          </td>
          <td class="text-end font-weight-medium">
            {{ formatMoney(m.case_now) }}
          </td>
          <td class="text-end">
            <VTooltip location="top">
              <template #activator="{ props }">
                <span
                  v-bind="props"
                  class="text-success cursor-pointer"
                >
                  {{ formatMoney(m.deposits) }}
                </span>
              </template>
              <div>{{ t('merchants.commission') }}: %{{ m.commission }}</div>
            </VTooltip>
          </td>
          <td class="text-end text-error">
            {{ formatMoney(m.withdrawals) }}
          </td>
          <td class="text-end text-info font-weight-medium">
            {{ formatDuration(m.avg_approval_sec) }}
          </td>
          <td class="text-end font-weight-bold" :class="`text-${caseColor(m.net_case)}`">
            {{ formatMoney(m.net_case) }}
          </td>
        </tr>
        <tr v-if="!loading && data.items.length === 0">
          <td
            colspan="6"
            class="text-center text-medium-emphasis"
          >
            {{ t('common.no_data') }}
          </td>
        </tr>
      </tbody>
    </VTable>
  </VCard>
</template>
