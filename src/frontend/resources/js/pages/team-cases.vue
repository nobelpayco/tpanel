<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'
import { useRouter } from 'vue-router'

definePage({ meta: { layout: 'default', roles: [1, 4] } })

const { t } = useI18n()
const router = useRouter()

const loading = ref(true)
const teams = ref([])
const totalCase = ref(0)

const { headers } = useApi()

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const netColor = val => val >= 0 ? 'success' : 'error'

const fetchData = async () => {
  loading.value = true
  try {
    const res = await fetch('/api/team-cases', { headers })
    if (res.ok) {
      const data = await res.json()
      teams.value = data.teams
      totalCase.value = data.total_case
    }
  } finally { loading.value = false }
}

onMounted(fetchData)
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle class="d-flex align-center gap-2">
            <VBtn icon variant="text" size="small" @click="router.push('/case-report')">
              <VIcon icon="tabler-arrow-left" />
            </VBtn>
            {{ t('case_report.team_balances') }}
            <VChip color="warning" label size="small" class="ms-2">{{ formatMoney(totalCase) }}</VChip>
          </VCardTitle>
        </VCardItem>
        <VDivider />
        <VTable class="text-no-wrap">
          <thead>
            <tr>
              <th>{{ t('teams.name') }}</th>
              <th class="text-end">{{ t('merchant_case.current_case') }}</th>
              <th class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="tm in teams" :key="tm.id" class="cursor-pointer" @click="router.push(`/team-case/${tm.id}`)">
              <td class="font-weight-medium">{{ tm.name }}</td>
              <td class="text-end font-weight-bold" :class="`text-${netColor(tm.current_case)}`">{{ formatMoney(tm.current_case) }}</td>
              <td class="text-end">
                <VBtn icon size="x-small" variant="text" color="primary"><VIcon icon="tabler-chevron-right" size="18" /></VBtn>
              </td>
            </tr>
            <tr v-if="!loading && teams.length === 0">
              <td colspan="3" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>
  </VRow>
</template>
