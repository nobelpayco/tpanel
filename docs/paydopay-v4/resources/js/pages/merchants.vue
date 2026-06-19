<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', roles: [1, 4] } })

const { t } = useI18n()
const activeTab = ref(0)

const loading = ref(true)
const groupsLoading = ref(true)
const merchants = ref([])
const groups = ref([])
const ungrouped = ref([])
const statusFilter = ref('1')

// Dialogs
const showAddDialog = ref(false)
const showEditDialog = ref(false)
const showGroupDialog = ref(false)
const showAssignDialog = ref(false)

// Forms
const form = ref({ name: '', email: '', commission: 0, withdrawCommission: 0, deliveryCommission: 0, depositLimit: 0, minDeposit: 0, maxDeposit: 0, group_id: null, approved_ip: '' })
const editForm = ref({})
const groupForm = ref({ name: '' })
const assignForm = ref({ merchant_id: null, group_id: null })

const { headers } = useApi()
const snackbar = useSnackbar()

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2 })

const statusOptions = [
  { title: t('status.active'), value: '1' },
  { title: t('status.inactive'), value: '0' },
  { title: t('common.all'), value: 'all' },
]

// Merchant CRUD
const fetchMerchants = async () => {
  loading.value = true
  try {
    const res = await fetch(`/api/merchants?status=${statusFilter.value}`, { headers })
    if (res.ok) merchants.value = await res.json()
  } finally { loading.value = false }
}

const addMerchant = async () => {
  const res = await fetch('/api/merchants', { method: 'POST', headers, body: JSON.stringify(form.value) })
  if (res.ok) {
    const data = await res.json()
    showAddDialog.value = false
    form.value = { name: '', email: '', commission: 0, withdrawCommission: 0, deliveryCommission: 0, depositLimit: 0, minDeposit: 0, maxDeposit: 0, group_id: null, approved_ip: '' }
    fetchMerchants()
    fetchGroups()
    snackbar.success('API Key: ' + data.api_key)
  }
}

const openEdit = (m) => {
  editForm.value = { ...m }
  showEditDialog.value = true
}

const updateMerchant = async () => {
  const { id, ...data } = editForm.value
  const res = await fetch(`/api/merchants/${id}`, { method: 'PUT', headers, body: JSON.stringify(data) })
  if (res.ok) { showEditDialog.value = false; fetchMerchants(); fetchGroups() }
}

const deleteMerchant = async (id) => {
  if (!confirm(t('common.confirm') + '?')) return
  await fetch(`/api/merchants/${id}`, { method: 'DELETE', headers })
  fetchMerchants()
}

// API Credentials
const showCredentialsDialog = ref(false)
const credMerchant = ref(null)
const credData = ref({ api_key: '', has_secret: false })
const newCreds = ref(null)   // rotate sonrası bir kerelik gösterilir

const openCredentials = async (m) => {
  credMerchant.value = m
  newCreds.value = null
  showCredentialsDialog.value = true
  const res = await fetch(`/api/merchants/${m.id}/credentials`, { headers })
  if (res.ok) credData.value = await res.json()
}

const rotateSecret = async () => {
  if (!confirm('Mevcut API Secret iptal edilip yenisi üretilecek. Devam edilsin mi?')) return
  const res = await fetch(`/api/merchants/${credMerchant.value.id}/rotate-secret`, { method: 'POST', headers })
  const data = await res.json()
  if (res.ok) {
    newCreds.value = data
    credData.value.has_secret = true
    snackbar.success(data.message)
  } else snackbar.error(data.message || 'Hata')
}

const rotateKey = async () => {
  if (!confirm('Mevcut API Key VE Secret iptal edilip yenileri üretilecek. Eski apiKey artık çalışmaz. Devam?')) return
  const res = await fetch(`/api/merchants/${credMerchant.value.id}/rotate-key`, { method: 'POST', headers })
  const data = await res.json()
  if (res.ok) {
    newCreds.value = data
    credData.value.api_key = data.api_key
    credData.value.has_secret = true
    snackbar.success(data.message)
  } else snackbar.error(data.message || 'Hata')
}

const copyText = (v) => navigator.clipboard?.writeText(v)

// Grup CRUD
const fetchGroups = async () => {
  groupsLoading.value = true
  try {
    const res = await fetch('/api/merchant-groups', { headers })
    if (res.ok) {
      const data = await res.json()
      groups.value = data.groups
      ungrouped.value = data.ungrouped
    }
  } finally { groupsLoading.value = false }
}

const addGroup = async () => {
  const res = await fetch('/api/merchant-groups', { method: 'POST', headers, body: JSON.stringify(groupForm.value) })
  if (res.ok) { showGroupDialog.value = false; groupForm.value = { name: '' }; fetchGroups() }
}

const deleteGroup = async (id) => {
  if (!confirm(t('common.confirm') + '?')) return
  await fetch(`/api/merchant-groups/${id}`, { method: 'DELETE', headers })
  fetchGroups()
  fetchMerchants()
}

const openAssign = (groupId) => {
  assignForm.value = { merchant_id: null, group_id: groupId }
  showAssignDialog.value = true
}

const assignMerchant = async () => {
  const res = await fetch('/api/merchant-groups/assign', { method: 'POST', headers, body: JSON.stringify(assignForm.value) })
  if (res.ok) { showAssignDialog.value = false; fetchGroups(); fetchMerchants() }
}

const removeMerchantFromGroup = async (merchantId) => {
  await fetch('/api/merchant-groups/assign', { method: 'POST', headers, body: JSON.stringify({ merchant_id: merchantId, group_id: null }) })
  fetchGroups()
  fetchMerchants()
}

onMounted(() => { fetchMerchants(); fetchGroups() })
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VTabs v-model="activeTab">
        <VTab>{{ t('merchants.title') }}</VTab>
        <VTab>{{ t('merchants.groups') }}</VTab>
      </VTabs>

      <VDivider />

      <VWindow v-model="activeTab">
        <!-- Tab 1: Merchantlar -->
        <VWindowItem>
          <VCard :loading="loading" class="mt-4">
            <VCardItem>
              <VCardTitle>{{ t('merchants.title') }}</VCardTitle>
              <template #append>
                <div class="d-flex align-center gap-3">
                  <VSelect
                    v-model="statusFilter"
                    :items="statusOptions"
                    density="compact"
                    style="min-width: 130px;"
                    @update:model-value="fetchMerchants"
                  />
                  <VBtn color="primary" size="small" @click="showAddDialog = true">
                    <VIcon start icon="tabler-plus" /> {{ t('merchants.add') }}
                  </VBtn>
                </div>
              </template>
            </VCardItem>
            <VDivider />
            <VTable class="text-no-wrap" density="compact">
              <thead>
                <tr>
                  <th>{{ t('merchants.name') }}</th>
                  <th>{{ t('merchants.groups') }}</th>
                  <th class="text-end">{{ t('merchants.commission') }}</th>
                  <th class="text-end">{{ t('merchants.withdraw_commission') }}</th>
                  <th class="text-end">{{ t('merchant_case.delivery_commission') }}</th>
                  <th>{{ t('deposits.status') }}</th>
                  <th class="text-end">{{ t('common.actions') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="m in merchants" :key="m.id">
                  <td class="font-weight-medium">{{ m.name }}</td>
                  <td>
                    <VChip v-if="m.group_name" color="primary" label size="x-small">{{ m.group_name }}</VChip>
                    <span v-else class="text-medium-emphasis">-</span>
                  </td>
                  <td class="text-end">%{{ m.commission }}</td>
                  <td class="text-end">%{{ m.withdrawCommission }}</td>
                  <td class="text-end">%{{ m.deliveryCommission }}</td>
                  <td>
                    <VChip :color="m.status == '1' ? 'success' : 'error'" label size="x-small">
                      {{ m.status == '1' ? t('status.active') : t('status.inactive') }}
                    </VChip>
                  </td>
                  <td class="text-end">
                    <VBtn icon size="x-small" variant="text" color="info" @click="openCredentials(m)">
                      <VIcon icon="tabler-key" size="18" />
                    </VBtn>
                    <VBtn icon size="x-small" variant="text" color="primary" @click="openEdit(m)">
                      <VIcon icon="tabler-edit" size="18" />
                    </VBtn>
                    <VBtn icon size="x-small" variant="text" color="error" @click="deleteMerchant(m.id)">
                      <VIcon icon="tabler-trash" size="18" />
                    </VBtn>
                  </td>
                </tr>
                <tr v-if="!loading && merchants.length === 0">
                  <td colspan="7" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
                </tr>
              </tbody>
            </VTable>
          </VCard>
        </VWindowItem>

        <!-- Tab 2: Gruplar -->
        <VWindowItem>
          <VCard :loading="groupsLoading" class="mt-4">
            <VCardItem>
              <VCardTitle>{{ t('merchants.groups') }}</VCardTitle>
              <template #append>
                <VBtn color="primary" size="small" @click="showGroupDialog = true">
                  <VIcon start icon="tabler-plus" /> {{ t('merchants.add_group') }}
                </VBtn>
              </template>
            </VCardItem>
            <VDivider />

            <VExpansionPanels variant="accordion">
              <VExpansionPanel v-for="g in groups" :key="g.id">
                <VExpansionPanelTitle>
                  <div class="d-flex align-center gap-3 w-100">
                    <span class="font-weight-bold">{{ g.name }}</span>
                    <VChip color="primary" label size="x-small">{{ g.merchants.length }} merchant</VChip>
                    <VSpacer />
                    <VBtn icon size="x-small" variant="text" color="error" @click.stop="deleteGroup(g.id)">
                      <VIcon icon="tabler-trash" size="18" />
                    </VBtn>
                  </div>
                </VExpansionPanelTitle>
                <VExpansionPanelText>
                  <VTable density="compact" class="text-no-wrap mb-3">
                    <thead>
                      <tr>
                        <th>{{ t('merchants.name') }}</th>
                        <th>{{ t('deposits.status') }}</th>
                        <th class="text-end">{{ t('common.actions') }}</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="m in g.merchants" :key="m.id">
                        <td>{{ m.name }}</td>
                        <td>
                          <VChip :color="m.status == '1' ? 'success' : 'error'" label size="x-small">
                            {{ m.status == '1' ? t('status.active') : t('status.inactive') }}
                          </VChip>
                        </td>
                        <td class="text-end">
                          <VBtn icon size="x-small" variant="text" color="error" @click="removeMerchantFromGroup(m.id)">
                            <VIcon icon="tabler-unlink" size="18" />
                          </VBtn>
                        </td>
                      </tr>
                      <tr v-if="g.merchants.length === 0">
                        <td colspan="3" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
                      </tr>
                    </tbody>
                  </VTable>
                  <VBtn size="small" variant="outlined" @click="openAssign(g.id)">
                    <VIcon start icon="tabler-link" /> {{ t('merchants.assign_merchant') }}
                  </VBtn>
                </VExpansionPanelText>
              </VExpansionPanel>
            </VExpansionPanels>

            <VCardText v-if="!groupsLoading && groups.length === 0" class="text-center text-medium-emphasis">
              {{ t('common.no_data') }}
            </VCardText>
          </VCard>
        </VWindowItem>
      </VWindow>
    </VCol>
  </VRow>

  <!-- Merchant Ekle -->
  <VDialog v-model="showAddDialog" max-width="550">
    <VCard :title="t('merchants.add')">
      <VCardText>
        <VRow>
          <VCol cols="12">
            <AppTextField v-model="form.name" :label="t('merchants.name')" />
          </VCol>
          <VCol cols="12">
            <AppTextField v-model="form.email" :label="t('merchants.email')" />
          </VCol>
          <VCol cols="4">
            <AppTextField v-model="form.commission" type="number" :label="t('merchants.commission')" suffix="%" />
          </VCol>
          <VCol cols="4">
            <AppTextField v-model="form.withdrawCommission" type="number" :label="t('merchants.withdraw_commission')" suffix="%" />
          </VCol>
          <VCol cols="4">
            <AppTextField v-model="form.deliveryCommission" type="number" :label="t('merchant_case.delivery_commission')" suffix="%" />
          </VCol>
          <VCol cols="4">
            <AppTextField v-model="form.minDeposit" type="number" :label="t('merchants.min_deposit')" prefix="₺" />
          </VCol>
          <VCol cols="4">
            <AppTextField v-model="form.maxDeposit" type="number" :label="t('merchants.max_deposit')" prefix="₺" />
          </VCol>
          <VCol cols="4">
            <AppTextField v-model="form.depositLimit" type="number" :label="t('merchants.deposit_limit')" prefix="₺" />
          </VCol>
          <VCol cols="6">
            <VSelect v-model="form.group_id" :items="[{ title: '-', value: null }, ...groups.map(g => ({ title: g.name, value: g.id }))]" :label="t('merchants.groups')" />
          </VCol>
          <VCol cols="6">
            <AppTextField v-model="form.approved_ip" :label="t('merchants.approved_ip')" placeholder="1.2.3.4,5.6.7.8" />
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showAddDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="addMerchant">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Merchant Düzenle -->
  <VDialog v-model="showEditDialog" max-width="550">
    <VCard :title="t('merchants.edit')">
      <VCardText>
        <VRow>
          <VCol cols="12">
            <AppTextField v-model="editForm.name" :label="t('merchants.name')" />
          </VCol>
          <VCol cols="12">
            <AppTextField v-model="editForm.email" :label="t('merchants.email')" />
          </VCol>
          <VCol cols="4">
            <AppTextField v-model="editForm.commission" type="number" :label="t('merchants.commission')" suffix="%" />
          </VCol>
          <VCol cols="4">
            <AppTextField v-model="editForm.withdrawCommission" type="number" :label="t('merchants.withdraw_commission')" suffix="%" />
          </VCol>
          <VCol cols="4">
            <AppTextField v-model="editForm.deliveryCommission" type="number" :label="t('merchant_case.delivery_commission')" suffix="%" />
          </VCol>
          <VCol cols="4">
            <AppTextField v-model="editForm.minDeposit" type="number" :label="t('merchants.min_deposit')" prefix="₺" />
          </VCol>
          <VCol cols="4">
            <AppTextField v-model="editForm.maxDeposit" type="number" :label="t('merchants.max_deposit')" prefix="₺" />
          </VCol>
          <VCol cols="4">
            <AppTextField v-model="editForm.depositLimit" type="number" :label="t('merchants.deposit_limit')" prefix="₺" />
          </VCol>
          <VCol cols="6">
            <VSelect v-model="editForm.group_id" :items="[{ title: '-', value: null }, ...groups.map(g => ({ title: g.name, value: g.id }))]" :label="t('merchants.groups')" />
          </VCol>
          <VCol cols="6">
            <AppTextField v-model="editForm.approved_ip" :label="t('merchants.approved_ip')" />
          </VCol>
          <VCol cols="12" md="6">
            <VSwitch v-model="editForm.status" true-value="1" false-value="0" :label="t('status.active')" />
          </VCol>
          <VCol cols="12" md="6">
            <VSwitch v-model="editForm.new_api" :true-value="1" :false-value="0" label="Yeni API (JSON callback)" color="primary" />
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showEditDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="updateMerchant">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Grup Ekle -->
  <VDialog v-model="showGroupDialog" max-width="400">
    <VCard :title="t('merchants.add_group')">
      <VCardText>
        <AppTextField v-model="groupForm.name" :label="t('merchants.group_name')" />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showGroupDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="addGroup">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Merchant Gruba Ata -->
  <VDialog v-model="showAssignDialog" max-width="400">
    <VCard :title="t('merchants.assign_merchant')">
      <VCardText>
        <VSelect
          v-model="assignForm.merchant_id"
          :items="ungrouped"
          item-title="name"
          item-value="id"
          :label="t('merchants.name')"
        />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showAssignDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="assignMerchant">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- API Credentials Dialog -->
  <VDialog v-model="showCredentialsDialog" max-width="600">
    <VCard :title="'API Anahtarları — ' + (credMerchant?.name || '')">
      <VCardText>
        <div class="text-body-2 text-medium-emphasis mb-2">API Key (kimlik)</div>
        <div class="d-flex align-center gap-2 mb-4">
          <code class="text-body-2">{{ credData.api_key || '—' }}</code>
          <VBtn icon size="x-small" variant="text" @click="copyText(credData.api_key)">
            <VIcon icon="tabler-copy" size="16" />
          </VBtn>
        </div>

        <div class="text-body-2 text-medium-emphasis mb-2">API Secret (HMAC imzalama)</div>
        <div class="mb-4">
          <VChip v-if="credData.has_secret && !newCreds" color="success" size="small">
            Üretilmiş ve gizli (yeniden gösterilemez)
          </VChip>
          <VChip v-else-if="!credData.has_secret && !newCreds" color="warning" size="small">
            Henüz üretilmemiş
          </VChip>
        </div>

        <VAlert v-if="newCreds" type="warning" variant="tonal" class="mb-3">
          <div class="font-weight-medium mb-2">⚠ Bu bilgiler bir daha gösterilmeyecek. Merchant'a iletip kaydetmesini sağlayın.</div>
          <div class="text-body-2 mb-1">API Key:</div>
          <div class="d-flex align-center gap-2 mb-2">
            <code>{{ newCreds.api_key }}</code>
            <VBtn icon size="x-small" variant="text" @click="copyText(newCreds.api_key)">
              <VIcon icon="tabler-copy" size="16" />
            </VBtn>
          </div>
          <div class="text-body-2 mb-1">API Secret:</div>
          <div class="d-flex align-center gap-2">
            <code>{{ newCreds.api_secret }}</code>
            <VBtn icon size="x-small" variant="text" @click="copyText(newCreds.api_secret)">
              <VIcon icon="tabler-copy" size="16" />
            </VBtn>
          </div>
        </VAlert>

        <div class="text-caption text-medium-emphasis">
          • Yeni Secret oluştur: yalnız Secret değişir, eski apiKey çalışır.<br>
          • Tüm anahtarları yenile: hem apiKey hem Secret değişir, eski apiKey artık geçersiz.
        </div>
      </VCardText>
      <VCardActions>
        <VBtn color="warning" variant="tonal" @click="rotateSecret">Yeni Secret Üret</VBtn>
        <VBtn color="error" variant="tonal" @click="rotateKey">Tüm Anahtarları Yenile</VBtn>
        <VSpacer />
        <VBtn variant="text" @click="showCredentialsDialog = false">Kapat</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
