<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'
import { useRouter } from 'vue-router'

definePage({ meta: { layout: 'default', roles: [1, 4] } })

const { t } = useI18n()
const router = useRouter()

const loading = ref(true)
const merchants = ref([])
const totalCase = ref(0)
const groupView = ref(false)

const { headers } = useApi()

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const netColor = val => val >= 0 ? 'success' : 'error'

const fetchData = async () => {
  loading.value = true
  try {
    const res = await fetch('/api/merchant-cases', { headers })
    if (res.ok) {
      const data = await res.json()
      merchants.value = data.merchants
      totalCase.value = data.total_case
    }
  } finally {
    loading.value = false
  }
}

onMounted(fetchData)

// Grup bazlı görünüm
const groupedMerchants = computed(() => {
  const groups = {}
  const ungrouped = []

  merchants.value.forEach(m => {
    if (m.group_id && m.group_name) {
      if (!groups[m.group_id]) {
        groups[m.group_id] = {
          id: m.group_id,
          name: m.group_name,
          value: 0,
          merchants: [],
        }
      }
      groups[m.group_id].value += m.value
      groups[m.group_id].merchants.push(m)
    } else {
      ungrouped.push(m)
    }
  })

  return [...Object.values(groups), ...ungrouped.map(m => ({
    id: `single-${m.id}`,
    name: m.name,
    value: m.value,
    merchants: [m],
  }))]
})

const displayList = computed(() => groupView.value ? groupedMerchants.value : merchants.value)
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
            {{ t('case_report.merchant_cases') }}
            <VChip color="primary" label size="small" class="ms-2">
              {{ formatMoney(totalCase) }}
            </VChip>
          </VCardTitle>
          <template #append>
            <div class="d-flex align-center gap-2">
              <span class="text-body-2">{{ t('merchants.group_view') }}</span>
              <VSwitch v-model="groupView" density="compact" hide-details />
            </div>
          </template>
        </VCardItem>
        <VDivider />

        <!-- Merchant bazlı -->
        <VTable v-if="!groupView" class="text-no-wrap">
          <thead>
            <tr>
              <th>{{ t('merchants.name') }}</th>
              <th>{{ t('merchants.groups') }}</th>
              <th class="text-end">{{ t('merchant_case.current_case') }}</th>
              <th class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="m in merchants"
              :key="m.id"
              class="cursor-pointer"
              @click="router.push(`/merchant-case/${m.id}${m.group_id ? '?type=group' : ''}`)"
            >
              <td class="font-weight-medium">{{ m.name }}</td>
              <td>
                <VChip v-if="m.group_name" color="primary" label size="x-small">{{ m.group_name }}</VChip>
                <span v-else class="text-medium-emphasis">-</span>
              </td>
              <td class="text-end font-weight-bold" :class="`text-${netColor(m.value)}`">
                {{ formatMoney(m.value) }}
              </td>
              <td class="text-end">
                <VBtn icon size="x-small" variant="text" color="primary">
                  <VIcon icon="tabler-chevron-right" size="18" />
                </VBtn>
              </td>
            </tr>
            <tr v-if="!loading && merchants.length === 0">
              <td colspan="4" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>

        <!-- Grup bazlı -->
        <VExpansionPanels v-else variant="accordion">
          <VExpansionPanel v-for="g in groupedMerchants" :key="g.id">
            <VExpansionPanelTitle>
              <div class="d-flex align-center justify-space-between w-100 pe-4">
                <span class="font-weight-bold">{{ g.name }}</span>
                <div class="d-flex align-center gap-3">
                  <VChip v-if="g.merchants.length > 1" color="primary" label size="x-small">
                    {{ g.merchants.length }} merchant
                  </VChip>
                  <span class="font-weight-bold" :class="`text-${netColor(g.value)}`">
                    {{ formatMoney(g.value) }}
                  </span>
                </div>
              </div>
            </VExpansionPanelTitle>
            <VExpansionPanelText>
              <VTable density="compact" class="text-no-wrap">
                <tbody>
                  <tr
                    v-for="m in g.merchants"
                    :key="m.id"
                    class="cursor-pointer"
                    @click="router.push(`/merchant-case/${m.id}${m.group_id ? '?type=group' : ''}`)"
                  >
                    <td>{{ m.name }}</td>
                    <td class="text-end font-weight-bold" :class="`text-${netColor(m.value)}`">
                      {{ formatMoney(m.value) }}
                    </td>
                    <td class="text-end">
                      <VBtn icon size="x-small" variant="text" color="primary">
                        <VIcon icon="tabler-chevron-right" size="18" />
                      </VBtn>
                    </td>
                  </tr>
                </tbody>
              </VTable>
            </VExpansionPanelText>
          </VExpansionPanel>
        </VExpansionPanels>

        <VCardText v-if="!loading && merchants.length === 0" class="text-center text-medium-emphasis">
          {{ t('common.no_data') }}
        </VCardText>
      </VCard>
    </VCol>
  </VRow>
</template>
