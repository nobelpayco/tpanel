<script setup>
import DashboardStatCards from '@/views/dashboard/DashboardStatCards.vue'
import DashboardTransactionChart from '@/views/dashboard/DashboardTransactionChart.vue'
import DashboardWeeklyOverview from '@/views/dashboard/DashboardWeeklyOverview.vue'
import DashboardRecentTransactions from '@/views/dashboard/DashboardRecentTransactions.vue'
import DashboardTeamPerformance from '@/views/dashboard/DashboardTeamPerformance.vue'
import { useI18n } from 'vue-i18n'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default' } })

const { t, locale } = useI18n()

const user = JSON.parse(localStorage.getItem('user') || '{}')
const isTeamMember = [2, 5].includes(user.user_type)
const isMerchant = user.user_type === 3

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const dateFrom = ref(today)
const dateTo = ref(today)

const localeMap = { tr: Turkish, en: english, ru: Russian }

const dateConfig = computed(() => ({
  dateFormat: 'Y-m-d',
  altInput: true,
  altFormat: 'd.m.Y',
  locale: localeMap[locale.value] || Turkish,
}))

const refreshKey = ref(0)

const applyFilter = () => {
  refreshKey.value++
}
</script>

<template>
  <VRow class="match-height">
    <!-- Tarih filtresi -->
    <VCol cols="12">
      <VCard>
        <VCardText class="d-flex align-center gap-4 flex-wrap">
          <div style="min-width: 160px;">
            <AppDateTimePicker
              v-model="dateFrom"
              :label="t('common.start_date')"
              :config="dateConfig"
              density="compact"
            />
          </div>
          <div style="min-width: 160px;">
            <AppDateTimePicker
              v-model="dateTo"
              :label="t('common.end_date')"
              :config="dateConfig"
              density="compact"
            />
          </div>
          <VBtn color="primary" @click="applyFilter">
            {{ t('common.filter') }}
          </VBtn>
          <VBtn
            v-if="dateFrom !== today || dateTo !== today"
            variant="text"
            color="secondary"
            @click="dateFrom = today; dateTo = today; applyFilter()"
          >
            {{ t('dashboard.today') }}
          </VBtn>
        </VCardText>
      </VCard>
    </VCol>

    <!-- Stat kartları -->
    <DashboardStatCards :key="'stats-' + refreshKey" :date-from="dateFrom" :date-to="dateTo" />

    <!-- İşlem hacmi grafiği -->
    <VCol cols="12" lg="8">
      <DashboardTransactionChart :key="'chart-' + refreshKey" :date-from="dateFrom" :date-to="dateTo" />
    </VCol>

    <!-- Anlık kasalar -->
    <VCol cols="12" lg="4">
      <DashboardWeeklyOverview :key="'cases-' + refreshKey" :date-from="dateFrom" :date-to="dateTo" />
    </VCol>

    <!-- Son işlemler -->
    <VCol cols="12" :lg="isMerchant ? 12 : 7">
      <DashboardRecentTransactions :key="'recent-' + refreshKey" :date-from="dateFrom" :date-to="dateTo" />
    </VCol>

    <!-- Takım performans (merchant'a gizli) -->
    <VCol v-if="!isMerchant" cols="12" lg="5">
      <DashboardTeamPerformance :key="'perf-' + refreshKey" :date-from="dateFrom" :date-to="dateTo" />
    </VCol>
  </VRow>
</template>
