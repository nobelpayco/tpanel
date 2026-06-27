<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', roles: [1, 4, 5] } })

const { t } = useI18n()

const loading = ref(true)
const accounts = ref([])
const banksList = ref([])
const teamsList = ref([])

const statusFilter = ref('1')
const bankFilter = ref(null)
const teamFilter = ref(null)
const search = ref('')

const showAddDialog = ref(false)
const showEditDialog = ref(false)

const form = ref({ status: 1, account_code: '', account_holder: '', account_iban: '', bank_id: null, min_invest: 0, max_invest: 0, max_per_invest: 0, max_amount: 0, team_id: null, walletID: null, daily_count_limit: 0 })
const editForm = ref({})
const identifiedBank = ref(null)

// Sürükle-bırak state
let dragIndex = null
const onDragStart = (idx, e) => {
  dragIndex = idx
  e.dataTransfer.effectAllowed = 'move'
}
const onDragOver = (e) => { e.preventDefault() }
const onDrop = async (toIdx) => {
  if (dragIndex === null || dragIndex === toIdx) { dragIndex = null; return }
  const moved = accounts.value.splice(dragIndex, 1)[0]
  accounts.value.splice(toIdx, 0, moved)
  // Yeni sıra numaralarını anlık olarak UI'da göster
  accounts.value.forEach((a, i) => { a.sort_order = i + 1 })
  dragIndex = null
  const ids = accounts.value.map(a => a.id)
  try {
    await fetch('/api/bank-accounts/reorder', { method: 'POST', headers, body: JSON.stringify({ ids }) })
    snackbar.success('Sıralama güncellendi.')
  } catch {
    snackbar.error('Sıralama kaydedilemedi.')
    fetchData()
  }
}

// Öneri sırası dialog
const showSortDialog = ref(false)
const sortTarget = ref(null)
const sortPosition = ref(1)

const openSortDialog = (acc) => {
  sortTarget.value = acc
  sortPosition.value = acc.sort_order || 1
  showSortDialog.value = true
}

const submitSortOrder = async () => {
  if (!sortTarget.value) return
  if (!sortPosition.value || sortPosition.value < 1) {
    snackbar.error('Geçerli bir sıra girin.')
    return
  }
  const res = await fetch(`/api/bank-accounts/${sortTarget.value.id}/sort-order`, {
    method: 'POST', headers,
    body: JSON.stringify({ position: Number(sortPosition.value) }),
  })
  const data = await res.json()
  if (res.ok) {
    snackbar.success(data.message || 'Sıra güncellendi.')
    showSortDialog.value = false
    fetchData()
  } else {
    snackbar.handleError(data)
  }
}

const statusOptions = [
  { title: t('status.active'), value: '1' },
  { title: t('status.inactive'), value: '2' },
  { title: t('banks.ready'), value: '3' },
  { title: t('common.all'), value: 'all' },
]

const statusLabels = { 0: t('banks.deleted'), 1: t('status.active'), 2: t('status.inactive'), 3: t('banks.ready') }
const statusColors = { 0: 'error', 1: 'success', 2: 'error', 3: 'warning' }

const { headers } = useApi()
const snackbar = useSnackbar()
const user = JSON.parse(localStorage.getItem('user') || '{}')
const isAdmin = [1, 4].includes(user.user_type)

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 0 })
const formatIban = iban => iban ? iban.replace(/(.{4})/g, '$1 ').trim() : ''

const countLimitFull = acc => Number(acc.daily_count_limit) > 0 && Number(acc.daily_count_used) >= Number(acc.daily_count_limit)
const amountLimitFull = acc => Number(acc.max_amount) > 0 && Number(acc.max_amount_used) >= Number(acc.max_amount)
const limitFull = acc => countLimitFull(acc) || amountLimitFull(acc)
const limitFullLabel = acc => {
  if (countLimitFull(acc) && amountLimitFull(acc)) return 'Günlük adet ve tutar limiti doldu'
  if (countLimitFull(acc)) return 'Günlük adet limiti doldu'
  if (amountLimitFull(acc)) return 'Günlük maksimum tutar doldu'
  return ''
}

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ status: statusFilter.value })
    if (bankFilter.value) params.append('bank_id', bankFilter.value)
    if (teamFilter.value) params.append('team_id', teamFilter.value)
    if (search.value) params.append('search', search.value)

    const res = await fetch(`/api/bank-accounts?${params}`, { headers })
    if (res.ok) accounts.value = await res.json()
  } finally { loading.value = false }
}

const fetchMeta = async () => {
  const [banksRes, teamsRes] = await Promise.all([
    fetch('/api/bank-accounts/banks', { headers }),
    fetch('/api/bank-accounts/teams', { headers }),
  ])
  if (banksRes.ok) banksList.value = await banksRes.json()
  if (teamsRes.ok) teamsList.value = await teamsRes.json()
}

onMounted(() => { fetchData(); fetchMeta() })

// IBAN doğrulama
const validateIban = async (iban, target) => {
  if (!iban || iban.replace(/\s/g, '').length < 26) return
  const res = await fetch('/api/bank-accounts/identify', { method: 'POST', headers, body: JSON.stringify({ iban }) })
  if (res.ok) {
    const data = await res.json()
    identifiedBank.value = data.bank
    target.bank_id = data.bank.id
  }
}

// Ekle
const addAccount = async () => {
  const res = await fetch('/api/bank-accounts', { method: 'POST', headers, body: JSON.stringify(form.value) })
  if (res.ok) {
    showAddDialog.value = false
    form.value = { status: 1, account_code: '', account_holder: '', account_iban: '', bank_id: null, min_invest: 0, max_invest: 0, max_per_invest: 0, max_amount: 0, team_id: null, walletID: null, daily_count_limit: 0 }
    identifiedBank.value = null
    fetchData()
  } else {
    const data = await res.json()
    snackbar.handleError(data)
  }
}

// Düzenle
const openEdit = (acc) => {
  editForm.value = { ...acc, account_iban: formatIban(acc.account_iban) }
  showEditDialog.value = true
}

const updateAccount = async () => {
  const { id, status, account_code, account_holder, account_iban, bank_id, min_invest, max_invest, max_per_invest, max_amount, team_id, walletID, daily_count_limit } = editForm.value
  const data = { status, account_code, account_holder, account_iban, bank_id, min_invest, max_invest, max_per_invest, max_amount, team_id, walletID, daily_count_limit }
  const res = await fetch(`/api/bank-accounts/${id}`, { method: 'PUT', headers, body: JSON.stringify(data) })
  if (res.ok) { showEditDialog.value = false; fetchData() }
}

// Sil
const deleteAccount = async (id) => {
  if (!confirm(t('common.confirm') + '?')) return
  await fetch(`/api/bank-accounts/${id}`, { method: 'DELETE', headers })
  fetchData()
}
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle>{{ t('banks.title') }}</VCardTitle>
          <template #append>
            <VBtn color="primary" size="small" @click="showAddDialog = true">
              <VIcon start icon="tabler-plus" /> {{ t('banks.add') }}
            </VBtn>
          </template>
        </VCardItem>
        <VDivider />

        <!-- Filtreler -->
        <VCardText class="d-flex gap-3 flex-wrap">
          <AppTextField v-model="search" :placeholder="t('common.search')" density="compact" style="max-width: 200px;" @keyup.enter="fetchData" />
          <VSelect v-model="statusFilter" :items="statusOptions" density="compact" style="min-width: 130px;" @update:model-value="fetchData" />
          <VAutocomplete v-model="bankFilter" :no-data-text="t('common.no_data')" :items="[{ title: t('common.all'), value: null }, ...banksList.map(b => ({ title: b.name, value: b.id }))]" :label="t('banks.bank_name')" density="compact" style="min-width: 150px;" clearable @update:model-value="fetchData" />
          <VAutocomplete v-if="isAdmin" v-model="teamFilter" :no-data-text="t('common.no_data')" :items="[{ title: t('common.all'), value: null }, ...teamsList.map(t => ({ title: t.name, value: t.id }))]" :label="t('banks.team')" density="compact" style="min-width: 150px;" clearable @update:model-value="fetchData" />
        </VCardText>

        <VTable class="text-no-wrap" density="compact">
          <thead>
            <tr>
              <th v-if="isAdmin" style="width: 32px;"></th>
              <th v-if="isAdmin" style="width: 56px;">Sıra</th>
              <th v-if="isAdmin">{{ t('banks.team') }}</th>
              <th>{{ t('banks.account_holder') }}</th>
              <th>{{ t('banks.iban') }}</th>
              <th>{{ t('banks.bank_name') }}</th>
              <th class="text-end">{{ t('banks.min_amount') }}</th>
              <th class="text-end">{{ t('banks.max_amount') }}</th>
              <th class="text-center">Günlük Adet</th>
              <th class="text-center">Günlük Tutar</th>
              <th>{{ t('deposits.status') }}</th>
              <th class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(acc, idx) in accounts"
              :key="acc.id"
              :class="{ 'row-limit-full': limitFull(acc) }"
              :draggable="isAdmin"
              @dragstart="isAdmin && onDragStart(idx, $event)"
              @dragover="isAdmin && onDragOver($event)"
              @drop="isAdmin && onDrop(idx)"
            >
              <td v-if="isAdmin" class="drag-handle"><VIcon icon="tabler-grip-vertical" size="16" color="grey" /></td>
              <td v-if="isAdmin" class="text-center"><strong>{{ acc.sort_order || '-' }}</strong></td>
              <td v-if="isAdmin">{{ acc.team_name }}</td>
              <td class="font-weight-medium">
                {{ acc.account_holder }}
                <div class="text-caption text-medium-emphasis">{{ acc.account_code }}</div>
              </td>
              <td class="text-body-2"><CopyText :value="acc.account_iban" :display="formatIban(acc.account_iban)" /></td>
              <td>{{ acc.bank_name }}</td>
              <td class="text-end">{{ formatMoney(acc.min_invest) }}</td>
              <td class="text-end">{{ formatMoney(acc.max_invest) }}</td>
              <td class="text-center">
                <VChip
                  v-if="acc.daily_count_limit > 0"
                  :color="acc.daily_count_used >= acc.daily_count_limit ? 'error' : 'info'"
                  size="x-small" label
                >
                  {{ acc.daily_count_used }} / {{ acc.daily_count_limit }}
                </VChip>
                <span v-else class="text-caption text-medium-emphasis">∞</span>
              </td>
              <td class="text-center">
                <VChip
                  v-if="Number(acc.max_amount) > 0"
                  :color="Number(acc.max_amount_used) >= Number(acc.max_amount) ? 'error' : 'info'"
                  size="x-small" label
                >
                  {{ formatMoney(acc.max_amount_used || 0) }} / {{ formatMoney(acc.max_amount) }}
                </VChip>
                <span v-else class="text-caption text-medium-emphasis">∞</span>
              </td>
              <td>
                <VChip :color="statusColors[acc.status]" label size="x-small">
                  {{ statusLabels[acc.status] }}
                </VChip>
                <div v-if="limitFull(acc)" class="d-flex align-center gap-1 mt-1">
                  <VIcon icon="tabler-alert-triangle" size="14" color="warning" />
                  <span class="text-caption text-warning font-weight-medium">{{ limitFullLabel(acc) }}</span>
                </div>
              </td>
              <td class="text-end">
                <VBtn icon size="x-small" variant="text" color="primary" @click="openEdit(acc)">
                  <VIcon icon="tabler-edit" size="18" />
                </VBtn>
                <VBtn v-if="isAdmin" icon size="x-small" variant="text" color="warning" @click="openSortDialog(acc)" title="Öneri sırası">
                  <VIcon icon="tabler-arrows-sort" size="18" />
                </VBtn>
                <VBtn icon size="x-small" variant="text" color="error" @click="deleteAccount(acc.id)">
                  <VIcon icon="tabler-trash" size="18" />
                </VBtn>
              </td>
            </tr>
            <tr v-if="!loading && accounts.length === 0">
              <td :colspan="isAdmin ? 12 : 9" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>
  </VRow>

  <!-- Hesap Ekle -->
  <VDialog v-model="showAddDialog" max-width="600">
    <VCard :title="t('banks.add')">
      <VCardText>
        <VRow>
          <VCol cols="6">
            <VSelect v-model="form.status" :items="[{title: t('status.active'), value: 1}, {title: t('status.inactive'), value: 2}, {title: t('banks.ready'), value: 3}]" :label="t('deposits.status')" />
          </VCol>
          <VCol cols="6">
            <VAutocomplete v-if="isAdmin" v-model="form.team_id" :no-data-text="t('common.no_data')" :items="teamsList.map(t => ({title: t.name, value: t.id}))" :label="t('banks.team')" />
          </VCol>
          <VCol cols="12">
            <AppTextField v-model="form.account_holder" :label="t('banks.account_holder')" />
          </VCol>
          <VCol cols="12">
            <AppTextField v-model="form.account_code" :label="t('banks.account_code')" placeholder="Hesap Kodu" />
          </VCol>
          <VCol cols="9">
            <AppTextField v-model="form.account_iban" :label="t('banks.iban')" placeholder="TR__ ____ ____ ____ ____ ____ __" />
          </VCol>
          <VCol cols="3" class="d-flex align-end">
            <VBtn color="primary" block @click="validateIban(form.account_iban, form)">
              <VIcon icon="tabler-check" />
            </VBtn>
          </VCol>
          <VCol v-if="identifiedBank" cols="12">
            <VAlert type="success" variant="tonal" density="compact">{{ identifiedBank.name }}</VAlert>
          </VCol>
          <VCol cols="6">
            <VAutocomplete v-model="form.bank_id" :no-data-text="t('common.no_data')" :items="banksList.map(b => ({title: b.name, value: b.id}))" :label="t('banks.bank_name')" />
          </VCol>
          <VCol cols="6">
            <AppTextField v-model="form.min_invest" type="number" :label="t('banks.min_amount')" prefix="₺" />
          </VCol>
          <VCol cols="6">
            <AppTextField v-model="form.max_invest" type="number" :label="t('banks.max_amount')" prefix="₺" />
          </VCol>
          <VCol cols="6">
            <AppTextField v-model="form.daily_count_limit" type="number" min="0" label="Günlük Adet Limiti" hint="0 = sınırsız" persistent-hint />
          </VCol>
          <VCol cols="6">
            <AppTextField v-model="form.max_amount" type="number" label="Günlük Toplam Tutar Limiti" prefix="₺" hint="0 = sınırsız" persistent-hint />
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showAddDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="addAccount">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Hesap Düzenle -->
  <VDialog v-model="showEditDialog" max-width="720" scrollable>
    <VCard class="bank-edit-card">
      <VCardItem class="pa-5 pb-3">
        <div class="d-flex align-center gap-3">
          <div class="bank-edit-icon">
            <VIcon icon="tabler-building-bank" size="20" />
          </div>
          <div>
            <div class="text-h6 font-weight-bold lh-1">{{ t('banks.edit') }}</div>
            <div class="text-caption text-medium-emphasis">#{{ editForm.id }} · {{ editForm.account_holder }}</div>
          </div>
          <VSpacer />
          <VBtn icon variant="text" size="small" @click="showEditDialog = false">
            <VIcon icon="tabler-x" />
          </VBtn>
        </div>
      </VCardItem>
      <VDivider />
      <VCardText class="pa-5">
        <!-- Genel -->
        <div class="section-label">Genel</div>
        <VRow class="mb-2">
          <VCol cols="12" md="7">
            <VSelect
              v-model="editForm.status"
              :items="[{title: t('status.active'), value: 1}, {title: t('status.inactive'), value: 2}, {title: t('banks.ready'), value: 3}, {title: t('banks.deleted'), value: 0}]"
              :label="t('deposits.status')"
              density="comfortable"
            />
          </VCol>
          <VCol v-if="isAdmin" cols="12" md="5">
            <VAutocomplete
              v-model="editForm.team_id"
              :no-data-text="t('common.no_data')"
              :items="teamsList.map(t => ({title: t.name, value: t.id}))"
              :label="t('banks.team')"
              density="comfortable"
            />
          </VCol>
        </VRow>

        <VDivider class="my-4" />

        <!-- Hesap Bilgileri -->
        <div class="section-label">Hesap Bilgileri</div>
        <VRow class="mb-2">
          <VCol cols="12" md="7">
            <AppTextField v-model="editForm.account_holder" :label="t('banks.account_holder')" density="comfortable" />
          </VCol>
          <VCol cols="12" md="5">
            <AppTextField v-model="editForm.account_code" :label="t('banks.account_code')" density="comfortable" />
          </VCol>
          <VCol cols="12" md="9">
            <AppTextField v-model="editForm.account_iban" :label="t('banks.iban')" density="comfortable" />
          </VCol>
          <VCol cols="12" md="3" class="d-flex align-center">
            <VBtn color="primary" variant="tonal" block @click="validateIban(editForm.account_iban, editForm)">
              <VIcon start icon="tabler-search" />
              Doğrula
            </VBtn>
          </VCol>
          <VCol cols="12">
            <VAutocomplete
              v-model="editForm.bank_id"
              :no-data-text="t('common.no_data')"
              :items="banksList.map(b => ({title: b.name, value: b.id}))"
              :label="t('banks.bank_name')"
              density="comfortable"
            />
          </VCol>
        </VRow>

        <VDivider class="my-4" />

        <!-- Tutar Limitleri (işlem başına) -->
        <div class="section-label">İşlem Tutar Limitleri</div>
        <VRow class="mb-2">
          <VCol cols="6">
            <AppTextField v-model="editForm.min_invest" type="number" :label="t('banks.min_amount')" prefix="₺" density="comfortable" />
          </VCol>
          <VCol cols="6">
            <AppTextField v-model="editForm.max_invest" type="number" :label="t('banks.max_amount')" prefix="₺" density="comfortable" />
          </VCol>
        </VRow>

        <VDivider class="my-4" />

        <!-- Günlük Limitler -->
        <div class="section-label">Günlük Limitler</div>

        <VCard variant="tonal" class="pa-3 mb-2" :color="Number(editForm.daily_count_limit) > 0 ? 'info' : undefined">
          <VRow no-gutters align="center">
            <VCol cols="auto" class="me-3">
              <VIcon icon="tabler-calendar-event" size="22" />
            </VCol>
            <VCol>
              <div class="font-weight-medium">Günlük Adet Limiti</div>
              <div class="text-caption text-medium-emphasis">Bugün bu hesaba yapılan işlem sayısı limiti aşmışsa hesap görünmez. <strong>0 = sınırsız</strong></div>
            </VCol>
            <VCol cols="12" md="3">
              <AppTextField
                v-model="editForm.daily_count_limit"
                type="number" min="0"
                placeholder="0 = sınırsız"
                density="compact"
                hide-details
              />
            </VCol>
          </VRow>
        </VCard>

        <VCard variant="tonal" class="pa-3" :color="Number(editForm.max_amount) > 0 ? 'info' : undefined">
          <VRow no-gutters align="center">
            <VCol cols="auto" class="me-3">
              <VIcon icon="tabler-cash" size="22" />
            </VCol>
            <VCol>
              <div class="font-weight-medium">Günlük Toplam Tutar Limiti</div>
              <div class="text-caption text-medium-emphasis">Bugün bu hesaba yapılan toplam tutar limiti aşmışsa hesap görünmez. <strong>0 = sınırsız</strong></div>
            </VCol>
            <VCol cols="12" md="3">
              <AppTextField
                v-model="editForm.max_amount"
                type="number" min="0"
                prefix="₺"
                placeholder="0 = sınırsız"
                density="compact"
                hide-details
              />
            </VCol>
          </VRow>
        </VCard>
      </VCardText>
      <VDivider />
      <VCardActions class="pa-4">
        <VSpacer />
        <VBtn variant="text" @click="showEditDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" variant="flat" prepend-icon="tabler-device-floppy" @click="updateAccount">
          {{ t('common.save') }}
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Öneri Sırası Dialog -->
  <VDialog v-model="showSortDialog" max-width="400">
    <VCard title="Öneri Sırası">
      <VCardText>
        <div class="mb-3 text-body-2 text-medium-emphasis">
          <strong>{{ sortTarget?.account_holder }}</strong>
          <div class="text-caption"><CopyText :value="sortTarget?.account_iban" :display="formatIban(sortTarget?.account_iban)" /></div>
          <div class="text-caption">Mevcut sıra: <strong>{{ sortTarget?.sort_order || '-' }}</strong></div>
        </div>
        <AppTextField
          v-model="sortPosition"
          type="number"
          min="1"
          label="Yeni Sıra"
          density="compact"
          autofocus
        />
        <div class="text-caption text-medium-emphasis mt-2">
          Girilen pozisyondaki mevcut hesap ve sonrasındakiler 1 sıra aşağı kayar.
        </div>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showSortDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="submitSortOrder">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>

<style scoped>
.drag-handle { cursor: grab; }
tr[draggable="true"] { cursor: move; }
tr[draggable="true"]:hover { background: rgba(var(--v-theme-primary), 0.04); }

.bank-edit-icon {
  width: 38px;
  height: 38px;
  display: grid;
  place-items: center;
  border-radius: 10px;
  background: rgba(var(--v-theme-primary), 0.12);
  color: rgb(var(--v-theme-primary));
}
.section-label {
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: rgba(var(--v-theme-on-surface), 0.55);
  margin-bottom: 12px;
}
.lh-1 { line-height: 1.1; }

/* Günlük limiti dolan hesabın satırı solgun turuncu zemin ile vurgulanır */
tr.row-limit-full > td { background: rgba(var(--v-theme-warning), 0.10) !important; }
tr.row-limit-full:hover > td { background: rgba(var(--v-theme-warning), 0.16) !important; }
</style>
