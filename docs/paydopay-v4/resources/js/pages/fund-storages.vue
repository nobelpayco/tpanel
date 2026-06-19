<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', roles: [1] } })

const { t } = useI18n()

const loading = ref(true)
const storages = ref([])
const totalBalance = ref(0)
const statusFilter = ref('1')
const showAddDialog = ref(false)
const showEditDialog = ref(false)

const form = ref({ name: '', type: 1, balance: 0, wallet_address: '' })
const editForm = ref({ id: null, name: '', type: 1, balance: 0, status: 1, wallet_address: '' })

const statusOptions = [
  { title: t('status.active'), value: '1' },
  { title: t('status.inactive'), value: '0' },
  { title: t('common.all'), value: 'all' },
]

const { headers } = useApi()

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })
const typeLabel = type => type === 1 ? t('fund_storage.external') : t('fund_storage.internal')
const typeColor = type => type === 1 ? 'warning' : 'info'

const fetchData = async () => {
  loading.value = true
  try {
    const res = await fetch(`/api/fund-storages?status=${statusFilter.value}`, { headers })
    if (res.ok) {
      const data = await res.json()
      storages.value = data.storages
      totalBalance.value = data.total_balance
    }
  } finally {
    loading.value = false
  }
}

onMounted(fetchData)

const addStorage = async () => {
  const res = await fetch('/api/fund-storages', { method: 'POST', headers, body: JSON.stringify(form.value) })
  if (res.ok) {
    showAddDialog.value = false
    form.value = { name: '', type: 1, balance: 0, wallet_address: '' }
    fetchData()
  }
}

const openEdit = (item) => {
  editForm.value = { id: item.id, name: item.name, type: item.type, balance: item.balance, status: item.status, wallet_address: item.wallet_address || '' }
  showEditDialog.value = true
}

const updateStorage = async () => {
  const { id, ...data } = editForm.value
  const res = await fetch(`/api/fund-storages/${id}`, { method: 'PUT', headers, body: JSON.stringify(data) })
  if (res.ok) {
    showEditDialog.value = false
    fetchData()
  }
}

const deleteStorage = async (id) => {
  if (!confirm(t('fund_storage.delete_confirm'))) return
  const res = await fetch(`/api/fund-storages/${id}`, { method: 'DELETE', headers })
  if (res.ok) fetchData()
}
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle class="d-flex align-center gap-2">
            {{ t('nav.fund_storages') }}
            <VChip color="primary" label size="small" class="ms-2">
              {{ formatMoney(totalBalance) }}
            </VChip>
          </VCardTitle>
          <template #append>
            <div class="d-flex align-center gap-3">
              <VSelect
                v-model="statusFilter"
                :items="statusOptions"
                density="compact"
                style="min-width: 130px;"
                @update:model-value="fetchData"
              />
              <VBtn color="primary" size="small" @click="showAddDialog = true">
                <VIcon start icon="tabler-plus" />
                {{ t('fund_storage.add') }}
              </VBtn>
            </div>
          </template>
        </VCardItem>
        <VDivider />
        <VTable class="text-no-wrap">
          <thead>
            <tr>
              <th>{{ t('fund_storage.name') }}</th>
              <th>{{ t('fund_storage.type') }}</th>
              <th>{{ t('fund_storage.wallet_address') }}</th>
              <th class="text-end">{{ t('fund_storage.balance') }}</th>
              <th>{{ t('deposits.status') }}</th>
              <th class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in storages" :key="item.id">
              <td class="font-weight-medium">
                {{ item.name }}
                <VChip
                  v-if="item.chain_balance !== null && item.chain_balance !== undefined"
                  color="success"
                  label
                  size="x-small"
                  class="ms-2"
                >
                  {{ item.chain_balance }} USDT
                </VChip>
              </td>
              <td>
                <VChip :color="typeColor(item.type)" label size="small">
                  {{ typeLabel(item.type) }}
                </VChip>
              </td>
              <td>
                <a
                  v-if="item.wallet_address"
                  :href="`https://tronscan.org/#/address/${item.wallet_address}`"
                  target="_blank"
                  class="text-primary text-body-2"
                >
                  {{ item.wallet_address.slice(0, 8) }}...{{ item.wallet_address.slice(-6) }}
                </a>
                <span v-else class="text-medium-emphasis">-</span>
              </td>
              <td class="text-end font-weight-bold">{{ formatMoney(item.balance) }}</td>
              <td>
                <VChip :color="item.status ? 'success' : 'error'" label size="x-small">
                  {{ item.status ? t('status.active') : t('status.inactive') }}
                </VChip>
              </td>
              <td class="text-end">
                <VBtn icon size="x-small" variant="text" color="primary" @click="openEdit(item)">
                  <VIcon icon="tabler-edit" size="18" />
                </VBtn>
                <VBtn icon size="x-small" variant="text" color="error" @click="deleteStorage(item.id)">
                  <VIcon icon="tabler-trash" size="18" />
                </VBtn>
              </td>
            </tr>
            <tr v-if="!loading && storages.length === 0">
              <td colspan="6" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>
  </VRow>

  <!-- Ekle Dialog -->
  <VDialog v-model="showAddDialog" max-width="450">
    <VCard :title="t('fund_storage.add')">
      <VCardText>
        <AppTextField v-model="form.name" :label="t('fund_storage.name')" class="mb-4" />
        <VRadioGroup v-model="form.type" :label="t('fund_storage.type')" inline class="mb-4">
          <VRadio :label="t('fund_storage.external')" :value="1" />
          <VRadio :label="t('fund_storage.internal')" :value="2" />
        </VRadioGroup>
        <AppTextField v-if="form.type === 2" v-model="form.wallet_address" :label="t('fund_storage.wallet_address')" class="mb-4" placeholder="0x..." />
        <AppTextField v-model="form.balance" type="number" :label="t('fund_storage.balance')" prefix="₺" />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showAddDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="addStorage">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Düzenle Dialog -->
  <VDialog v-model="showEditDialog" max-width="450">
    <VCard :title="t('fund_storage.edit')">
      <VCardText>
        <AppTextField v-model="editForm.name" :label="t('fund_storage.name')" class="mb-4" />
        <VRadioGroup v-model="editForm.type" :label="t('fund_storage.type')" inline class="mb-4">
          <VRadio :label="t('fund_storage.external')" :value="1" />
          <VRadio :label="t('fund_storage.internal')" :value="2" />
        </VRadioGroup>
        <AppTextField v-if="editForm.type === 2" v-model="editForm.wallet_address" :label="t('fund_storage.wallet_address')" class="mb-4" placeholder="0x..." />
        <AppTextField v-model="editForm.balance" type="number" :label="t('fund_storage.balance')" prefix="₺" class="mb-4" />
        <VSwitch v-model="editForm.status" :true-value="1" :false-value="0" :label="t('status.active')" />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showEditDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="updateStorage">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
