<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', roles: [1] } })

const { t } = useI18n()

const loading = ref(true)
const intermediaries = ref([])
const merchants = ref([])
const teams = ref([])

// Dialogs
const showAddDialog = ref(false)
const showEditDialog = ref(false)
const showAttachDialog = ref(false)

// Forms
const newIntermediary = ref({ name: '', type: 1 })
const editIntermediary = ref({ id: null, name: '', type: 1, status: 1 })
const attachForm = ref({ intermediary_id: null, entity_id: null, commission_rate: 0, entity_type: null })

const { headers } = useApi()

const statusFilter = ref('1')

const statusOptions = [
  { title: t('status.active'), value: '1' },
  { title: t('status.inactive'), value: '0' },
  { title: t('common.all'), value: 'all' },
]

const fetchData = async () => {
  loading.value = true
  try {
    const res = await fetch(`/api/intermediaries?status=${statusFilter.value}`, { headers })
    if (res.ok) {
      const data = await res.json()
      intermediaries.value = data.intermediaries
      merchants.value = data.merchants
      teams.value = data.teams
    }
  } finally {
    loading.value = false
  }
}

onMounted(fetchData)

const typeLabel = type => type === 1 ? t('intermediary.paylira_type') : t('intermediary.merchant_type')
const typeColor = type => type === 1 ? 'primary' : 'warning'

// Aracı ekle
const addIntermediary = async () => {
  const res = await fetch('/api/intermediaries', { method: 'POST', headers, body: JSON.stringify(newIntermediary.value) })
  if (res.ok) {
    showAddDialog.value = false
    newIntermediary.value = { name: '', type: 1 }
    fetchData()
  }
}

// Aracı düzenle
const openEdit = (item) => {
  editIntermediary.value = { id: item.id, name: item.name, type: item.type, status: item.status }
  showEditDialog.value = true
}

const updateIntermediary = async () => {
  const { id, ...data } = editIntermediary.value
  const res = await fetch(`/api/intermediaries/${id}`, { method: 'PUT', headers, body: JSON.stringify(data) })
  if (res.ok) {
    showEditDialog.value = false
    fetchData()
  }
}

// Aracı sil
const deleteIntermediary = async (id) => {
  if (!confirm(t('intermediary.delete_confirm'))) return
  const res = await fetch(`/api/intermediaries/${id}`, { method: 'DELETE', headers })
  if (res.ok) fetchData()
}

// Bağla (merchant veya takım)
const openAttach = (item) => {
  attachForm.value = {
    intermediary_id: item.id,
    entity_id: null,
    commission_rate: 0,
    entity_type: item.type, // 1=takım aracısı→takım bağla, 2=merchant aracısı→merchant bağla
  }
  showAttachDialog.value = true
}

const attachEntity = async () => {
  const isTeam = attachForm.value.entity_type === 1
  const url = isTeam ? '/api/intermediaries/attach-team' : '/api/intermediaries/attach-merchant'
  const body = {
    intermediary_id: attachForm.value.intermediary_id,
    commission_rate: attachForm.value.commission_rate,
  }
  if (isTeam) body.team_id = attachForm.value.entity_id
  else body.merchant_id = attachForm.value.entity_id

  const res = await fetch(url, { method: 'POST', headers, body: JSON.stringify(body) })
  if (res.ok) {
    showAttachDialog.value = false
    fetchData()
  }
}

// Bağlantı kaldır
const detach = async (type, pivotId) => {
  const url = type === 'team' ? `/api/intermediaries/team/${pivotId}` : `/api/intermediaries/merchant/${pivotId}`
  const res = await fetch(url, { method: 'DELETE', headers })
  if (res.ok) fetchData()
}

// Tüm bağlantıları toplu kaydet
const saving = ref({})

const saveAll = async (item) => {
  saving.value[item.id] = true
  try {
    const promises = []

    for (const tm of item.teams) {
      promises.push(fetch(`/api/intermediaries/team/${tm.pivot_id}`, {
        method: 'PUT', headers,
        body: JSON.stringify({ commission_rate: tm.commission_rate, status: tm.status }),
      }))
    }

    for (const m of item.merchants) {
      promises.push(fetch(`/api/intermediaries/merchant/${m.pivot_id}`, {
        method: 'PUT', headers,
        body: JSON.stringify({ commission_rate: m.commission_rate, status: m.status }),
      }))
    }

    await Promise.all(promises)
  } finally {
    saving.value[item.id] = false
  }
}
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle>{{ t('nav.intermediaries') }}</VCardTitle>
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
                {{ t('intermediary.add') }}
              </VBtn>
            </div>
          </template>
        </VCardItem>
        <VDivider />

        <VExpansionPanels variant="accordion">
          <VExpansionPanel v-for="item in intermediaries" :key="item.id">
            <VExpansionPanelTitle>
              <div class="d-flex align-center gap-3 w-100">
                <span class="font-weight-bold">{{ item.name }}</span>
                <VChip :color="typeColor(item.type)" label size="small">
                  {{ typeLabel(item.type) }}
                </VChip>
                <VChip :color="item.status ? 'success' : 'error'" label size="x-small">
                  {{ item.status ? t('status.active') : t('status.inactive') }}
                </VChip>
                <VSpacer />
                <VBtn icon size="x-small" variant="text" color="primary" @click.stop="openEdit(item)">
                  <VIcon icon="tabler-edit" size="18" />
                </VBtn>
                <VBtn icon size="x-small" variant="text" color="error" @click.stop="deleteIntermediary(item.id)">
                  <VIcon icon="tabler-trash" size="18" />
                </VBtn>
              </div>
            </VExpansionPanelTitle>

            <VExpansionPanelText>
              <!-- Takım aracısı → takımlar tablosu -->
              <template v-if="item.type === 1">
                <h6 class="text-body-1 font-weight-bold mb-2">{{ t('nav.teams') }}</h6>
                <VTable density="compact" class="text-no-wrap mb-3">
                  <thead>
                    <tr>
                      <th>{{ t('teams.name') }}</th>
                      <th>{{ t('merchants.commission') }}</th>
                      <th>{{ t('deposits.status') }}</th>
                      <th class="text-end">{{ t('common.actions') }}</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="tm in item.teams" :key="tm.pivot_id">
                      <td>{{ tm.name }}</td>
                      <td>
                        <AppTextField
                          v-model="tm.commission_rate"
                          type="number" density="compact" style="max-width: 100px;" suffix="%"
                        />
                      </td>
                      <td>
                        <VChip
                          :color="tm.status ? 'success' : 'error'" label size="x-small" class="cursor-pointer"
                          @click="updateRate('team', tm.pivot_id, tm.commission_rate, tm.status ? 0 : 1); tm.status = tm.status ? 0 : 1"
                        >
                          {{ tm.status ? t('status.active') : t('status.inactive') }}
                        </VChip>
                      </td>
                      <td class="text-end">
                        <VBtn icon size="x-small" variant="text" color="error" @click="detach('team', tm.pivot_id)">
                          <VIcon icon="tabler-unlink" size="18" />
                        </VBtn>
                      </td>
                    </tr>
                    <tr v-if="item.teams.length === 0">
                      <td colspan="4" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
                    </tr>
                  </tbody>
                </VTable>
              </template>

              <!-- Merchant aracısı → merchantlar tablosu -->
              <template v-if="item.type === 2">
                <h6 class="text-body-1 font-weight-bold mb-2">{{ t('nav.merchants') }}</h6>
                <VTable density="compact" class="text-no-wrap mb-3">
                  <thead>
                    <tr>
                      <th>{{ t('merchants.name') }}</th>
                      <th>{{ t('merchants.commission') }}</th>
                      <th>{{ t('deposits.status') }}</th>
                      <th class="text-end">{{ t('common.actions') }}</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="m in item.merchants" :key="m.pivot_id">
                      <td>{{ m.name }}</td>
                      <td>
                        <AppTextField
                          v-model="m.commission_rate"
                          type="number" density="compact" style="max-width: 100px;" suffix="%"
                        />
                      </td>
                      <td>
                        <VChip
                          :color="m.status ? 'success' : 'error'" label size="x-small" class="cursor-pointer"
                          @click="updateRate('merchant', m.pivot_id, m.commission_rate, m.status ? 0 : 1); m.status = m.status ? 0 : 1"
                        >
                          {{ m.status ? t('status.active') : t('status.inactive') }}
                        </VChip>
                      </td>
                      <td class="text-end">
                        <VBtn icon size="x-small" variant="text" color="error" @click="detach('merchant', m.pivot_id)">
                          <VIcon icon="tabler-unlink" size="18" />
                        </VBtn>
                      </td>
                    </tr>
                    <tr v-if="item.merchants.length === 0">
                      <td colspan="4" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
                    </tr>
                  </tbody>
                </VTable>
              </template>

              <div class="d-flex gap-2">
                <VBtn size="small" variant="outlined" @click="openAttach(item)">
                  <VIcon start icon="tabler-link" />
                  {{ item.type === 1 ? t('intermediary.attach_team') : t('intermediary.attach_merchant') }}
                </VBtn>
                <VBtn size="small" color="primary" :loading="saving[item.id]" @click="saveAll(item)">
                  <VIcon start icon="tabler-device-floppy" />
                  {{ t('common.save') }}
                </VBtn>
              </div>
            </VExpansionPanelText>
          </VExpansionPanel>
        </VExpansionPanels>

        <VCardText v-if="!loading && intermediaries.length === 0" class="text-center text-medium-emphasis">
          {{ t('common.no_data') }}
        </VCardText>
      </VCard>
    </VCol>
  </VRow>

  <!-- Aracı Ekle -->
  <VDialog v-model="showAddDialog" max-width="450">
    <VCard :title="t('intermediary.add')">
      <VCardText>
        <AppTextField v-model="newIntermediary.name" :label="t('intermediary.name')" class="mb-4" />
        <VRadioGroup v-model="newIntermediary.type" :label="t('intermediary.type')" inline>
          <VRadio :label="t('intermediary.paylira_type')" :value="1" />
          <VRadio :label="t('intermediary.merchant_type')" :value="2" />
        </VRadioGroup>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showAddDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="addIntermediary">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Aracı Düzenle -->
  <VDialog v-model="showEditDialog" max-width="450">
    <VCard :title="t('intermediary.edit')">
      <VCardText>
        <AppTextField v-model="editIntermediary.name" :label="t('intermediary.name')" class="mb-4" />
        <VRadioGroup v-model="editIntermediary.type" :label="t('intermediary.type')" inline class="mb-4">
          <VRadio :label="t('intermediary.paylira_type')" :value="1" />
          <VRadio :label="t('intermediary.merchant_type')" :value="2" />
        </VRadioGroup>
        <VSwitch v-model="editIntermediary.status" :true-value="1" :false-value="0" :label="t('status.active')" />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showEditDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="updateIntermediary">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Bağla (Takım veya Merchant) -->
  <VDialog v-model="showAttachDialog" max-width="450">
    <VCard :title="attachForm.entity_type === 1 ? t('intermediary.attach_team') : t('intermediary.attach_merchant')">
      <VCardText>
        <!-- Takım seçici -->
        <VSelect
          v-if="attachForm.entity_type === 1"
          v-model="attachForm.entity_id"
          :items="teams"
          item-title="name"
          item-value="id"
          :label="t('teams.name')"
          class="mb-4"
        />
        <!-- Merchant seçici -->
        <VSelect
          v-else
          v-model="attachForm.entity_id"
          :items="merchants"
          item-title="name"
          item-value="id"
          :label="t('merchants.name')"
          class="mb-4"
        />
        <AppTextField
          v-model="attachForm.commission_rate"
          type="number"
          :label="t('merchants.commission')"
          suffix="%"
        />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showAttachDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="attachEntity">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
