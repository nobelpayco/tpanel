<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'
import { useRouter } from 'vue-router'

definePage({ meta: { layout: 'default', roles: [1, 4] } })

const { t } = useI18n()
const router = useRouter()

const loading = ref(true)
const intermediaries = ref([])

const { headers } = useApi()

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const netColor = val => val >= 0 ? 'success' : 'error'
const typeLabel = type => type === 1 ? t('intermediary.paylira_type') : t('intermediary.merchant_type')
const typeColor = type => type === 1 ? 'primary' : 'warning'

const fetchData = async () => {
  loading.value = true
  try {
    const res = await fetch('/api/intermediary-cases', { headers })
    if (res.ok) intermediaries.value = await res.json()
  } finally { loading.value = false }
}

onMounted(fetchData)

const totalCase = computed(() => intermediaries.value.reduce((sum, i) => sum + i.current_case, 0))
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
            {{ t('case_report.intermediary_total') }}
            <VChip color="info" label size="small" class="ms-2">{{ formatMoney(totalCase) }}</VChip>
          </VCardTitle>
        </VCardItem>
        <VDivider />
        <VTable class="text-no-wrap">
          <thead>
            <tr>
              <th>{{ t('intermediary.name') }}</th>
              <th>{{ t('intermediary.type') }}</th>
              <th class="text-end">{{ t('merchant_case.current_case') }}</th>
              <th class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="i in intermediaries" :key="i.id" class="cursor-pointer" @click="router.push(`/intermediary-case/${i.id}`)">
              <td class="font-weight-medium">{{ i.name }}</td>
              <td><VChip :color="typeColor(i.type)" label size="x-small">{{ typeLabel(i.type) }}</VChip></td>
              <td class="text-end font-weight-bold" :class="`text-${netColor(i.current_case)}`">{{ formatMoney(i.current_case) }}</td>
              <td class="text-end">
                <VBtn icon size="x-small" variant="text" color="primary"><VIcon icon="tabler-chevron-right" size="18" /></VBtn>
              </td>
            </tr>
            <tr v-if="!loading && intermediaries.length === 0">
              <td colspan="4" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>
  </VRow>
</template>
