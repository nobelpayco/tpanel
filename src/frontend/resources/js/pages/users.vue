<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', roles: [1, 4, 5] } })

const { t } = useI18n()
const { headers } = useApi()
const snackbar = useSnackbar()

const authUser = JSON.parse(localStorage.getItem('user') || '{}')
const isTeamAdmin = computed(() => authUser.user_type === 5)
const isSuperAdmin = computed(() => authUser.user_type === 1)

const loading = ref(false)
const users = ref([])

const filters = ref({ user_type: 'all', status: 'all', search: '' })

const allowedRoles = ref([])
const teams = ref([])
const merchants = ref([])
const roleLabels = ref({})

const showFormDialog = ref(false)
const editingId = ref(null)
const formLoading = ref(false)
const form = ref({
  name: '',
  username: '',
  password: '',
  user_type: null,
  team_id: null,
  firm_id: null,
  status: 1,
})
const showPass = ref(false)

const showDeleteDialog = ref(false)
const deleteTarget = ref(null)

const statusOptions = computed(() => [
  { title: t('users.status_active'), value: 1 },
  { title: t('users.status_passive'), value: 0 },
])

const statusFilterOptions = computed(() => [
  { title: t('common.all'), value: 'all' },
  ...statusOptions.value,
])

const userTypeFilterOptions = computed(() => [
  { title: t('common.all'), value: 'all' },
  ...Object.entries(roleLabels.value).map(([id, label]) => ({ title: label, value: Number(id) })),
])

const allowedRoleOptions = computed(() =>
  allowedRoles.value.map(r => ({ title: r.label, value: r.id }))
)

const teamOptions = computed(() =>
  teams.value.map(team => ({
    title: team.status === 1 ? team.name : `${team.name} (pasif)`,
    value: team.id,
  }))
)

const merchantOptions = computed(() =>
  merchants.value.map(m => ({
    title: m.status === '1' ? m.name : `${m.name} (pasif)`,
    value: m.id,
  }))
)

const showTeamSelect = computed(() => [2, 5].includes(form.value.user_type) && !isTeamAdmin.value)
const showMerchantSelect = computed(() => form.value.user_type === 3)

const fetchUsers = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams()
    if (filters.value.user_type !== 'all') params.append('user_type', filters.value.user_type)
    if (filters.value.status !== 'all') params.append('status', filters.value.status)
    if (filters.value.search) params.append('search', filters.value.search)
    const res = await fetch(`/api/users?${params}`, { headers })
    if (res.ok) users.value = await res.json()
    else snackbar.error('Liste yüklenemedi.')
  } finally { loading.value = false }
}

const fetchOptions = async () => {
  const res = await fetch('/api/users/options', { headers })
  if (res.ok) {
    const data = await res.json()
    allowedRoles.value = data.allowed_roles || []
    teams.value = data.teams || []
    merchants.value = data.merchants || []
    roleLabels.value = data.role_labels || {}
  }
}

const resetForm = () => {
  form.value = {
    name: '', username: '', password: '',
    user_type: allowedRoles.value[0]?.id ?? null,
    team_id: null, firm_id: null, status: 1,
  }
  editingId.value = null
  showPass.value = false
}

const openCreate = () => {
  resetForm()
  showFormDialog.value = true
}

const openEdit = (u) => {
  editingId.value = u.id
  form.value = {
    name: u.name || '',
    username: u.username || '',
    password: '',
    user_type: u.user_type,
    team_id: u.team_id || null,
    firm_id: u.firm_id || null,
    status: Number(u.status),
  }
  showPass.value = false
  showFormDialog.value = true
}

const passwordIsValid = (pw) => pw.length >= 6 && /[A-Za-z]/.test(pw) && /\d/.test(pw)

const submitForm = async () => {
  if (!form.value.name || !form.value.username) {
    snackbar.error('Ad ve kullanıcı adı zorunludur.')
    return
  }
  if (!editingId.value && !form.value.password) {
    snackbar.error('Şifre zorunludur.')
    return
  }
  if (form.value.password && !passwordIsValid(form.value.password)) {
    snackbar.error('Şifre en az 6 karakter, en az bir harf ve bir rakam içermelidir.')
    return
  }
  if ([2, 5].includes(form.value.user_type) && !isTeamAdmin.value && !form.value.team_id) {
    snackbar.error('Takım seçimi zorunludur.')
    return
  }
  if (form.value.user_type === 3 && !form.value.firm_id) {
    snackbar.error('Merchant seçimi zorunludur.')
    return
  }

  formLoading.value = true
  try {
    const url = editingId.value ? `/api/users/${editingId.value}` : '/api/users'
    const method = editingId.value ? 'PUT' : 'POST'
    const body = { ...form.value }
    if (editingId.value && !body.password) delete body.password
    const res = await fetch(url, { method, headers, body: JSON.stringify(body) })
    const data = await res.json()
    if (res.ok) {
      snackbar.success(data.message || 'Başarılı.')
      showFormDialog.value = false
      fetchUsers()
    } else {
      snackbar.error(data.message || 'İşlem başarısız.')
    }
  } catch {
    snackbar.error('Sunucu hatası.')
  } finally { formLoading.value = false }
}

const confirmDelete = (u) => {
  deleteTarget.value = u
  showDeleteDialog.value = true
}

const submitDelete = async () => {
  if (!deleteTarget.value) return
  try {
    const res = await fetch(`/api/users/${deleteTarget.value.id}`, { method: 'DELETE', headers })
    const data = await res.json()
    if (res.ok) {
      snackbar.success(data.message || 'Silindi.')
      fetchUsers()
    } else {
      snackbar.error(data.message || 'Silme başarısız.')
    }
  } finally {
    showDeleteDialog.value = false
    deleteTarget.value = null
  }
}

const formatDate = val => {
  if (!val) return '-'
  const d = new Date(val)
  return d.toLocaleDateString('tr-TR') + ' ' + d.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })
}

const roleColor = (t) => ({ 1: 'error', 2: 'info', 3: 'warning', 4: 'primary', 5: 'success' }[t] || 'default')

// user_type değişince team_id/firm_id'yi sıfırla
watch(() => form.value.user_type, () => {
  form.value.team_id = null
  form.value.firm_id = null
})

onMounted(() => {
  fetchOptions()
  fetchUsers()
})
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle class="d-flex align-center gap-2">
            {{ t('nav.users') }}
            <VChip color="primary" label size="small">{{ users.length }}</VChip>
          </VCardTitle>
          <template #append>
            <VBtn color="primary" prepend-icon="tabler-plus" @click="openCreate">
              {{ t('users.new') }}
            </VBtn>
          </template>
        </VCardItem>

        <VCardText class="d-flex flex-wrap gap-3 align-center">
          <VSelect
            v-model="filters.user_type"
            :items="userTypeFilterOptions"
            :label="t('users.role')"
            density="compact"
            style="min-width: 180px;"
            @update:model-value="fetchUsers"
          />
          <VSelect
            v-model="filters.status"
            :items="statusFilterOptions"
            label="Durum"
            density="compact"
            style="min-width: 150px;"
            @update:model-value="fetchUsers"
          />
          <AppTextField
            v-model="filters.search"
            placeholder="Ad veya kullanıcı adı..."
            density="compact"
            style="min-width: 250px;"
            clearable
            @update:model-value="fetchUsers"
          />
        </VCardText>

        <VDivider />

        <VTable density="compact" class="text-no-wrap">
          <thead>
            <tr>
              <th>ID</th>
              <th>Ad Soyad</th>
              <th>Kullanıcı Adı</th>
              <th>Rol</th>
              <th>{{ t('users.team_or_merchant') }}</th>
              <th>Durum</th>
              <th>Oluşturulma</th>
              <th class="text-end">Aksiyon</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="u in users" :key="u.id">
              <td>{{ u.id }}</td>
              <td>{{ u.name }}</td>
              <td class="text-caption">{{ u.username }}</td>
              <td>
                <VChip :color="roleColor(u.user_type)" size="x-small" label>{{ u.role_label }}</VChip>
              </td>
              <td>
                <span v-if="u.team_name">{{ u.team_name }}</span>
                <span v-else-if="u.merchant_name">{{ u.merchant_name }}</span>
                <span v-else class="text-medium-emphasis">-</span>
              </td>
              <td>
                <VChip :color="u.status == '1' ? 'success' : 'error'" size="x-small" label>
                  {{ u.status == '1' ? t('users.status_active') : t('users.status_passive') }}
                </VChip>
              </td>
              <td class="text-caption">{{ formatDate(u.created_at) }}</td>
              <td class="text-end">
                <template v-if="u.user_type !== 1">
                  <VBtn icon size="x-small" variant="text" @click="openEdit(u)">
                    <VIcon icon="tabler-edit" size="18" />
                  </VBtn>
                  <VBtn icon size="x-small" variant="text" color="error" @click="confirmDelete(u)">
                    <VIcon icon="tabler-trash" size="18" />
                  </VBtn>
                </template>
                <span v-else class="text-medium-emphasis text-caption">-</span>
              </td>
            </tr>
            <tr v-if="!loading && users.length === 0">
              <td colspan="8" class="text-center text-medium-emphasis py-4">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>
  </VRow>

  <!-- Form Dialog -->
  <VDialog v-model="showFormDialog" max-width="600" persistent>
    <VCard :title="editingId ? 'Kullanıcı Düzenle' : t('users.new')">
      <VCardText>
        <VRow>
          <VCol cols="12" md="6">
            <VSelect
              v-model="form.user_type"
              :items="allowedRoleOptions"
              :label="t('users.role')"
              density="compact"
              :disabled="editingId && !isSuperAdmin"
            />
          </VCol>
          <VCol cols="12" md="6">
            <VSelect
              v-model="form.status"
              :items="statusOptions"
              label="Durum"
              density="compact"
            />
          </VCol>
          <VCol cols="12" md="6">
            <AppTextField v-model="form.name" label="Ad Soyad" density="compact" />
          </VCol>
          <VCol cols="12" md="6">
            <AppTextField v-model="form.username" label="Kullanıcı Adı" density="compact" />
          </VCol>
          <VCol cols="12">
            <AppTextField
              v-model="form.password"
              :type="showPass ? 'text' : 'password'"
              :label="editingId ? 'Yeni Şifre (boş bırakılırsa değişmez)' : 'Şifre'"
              density="compact"
              :append-inner-icon="showPass ? 'tabler-eye-off' : 'tabler-eye'"
              @click:append-inner="showPass = !showPass"
            />
            <div class="text-caption text-medium-emphasis mt-1">
              Şifre en az 6 karakter olmalı, en az bir harf ve bir rakam içermelidir.
            </div>
          </VCol>

          <VCol v-if="showTeamSelect" cols="12">
            <VAutocomplete
              v-model="form.team_id"
              :items="teamOptions"
              :label="'Takım' + (form.user_type === 2 ? ' (Team Agent için)' : ' (Team Admin için)')"
              density="compact"
              clearable
              :no-data-text="t('common.no_data')"
            />
          </VCol>

          <VCol v-if="isTeamAdmin && form.user_type === 2" cols="12">
            <VAlert type="info" variant="tonal" density="compact">
              Yeni Team Agent otomatik olarak kendi takımınıza atanacak.
            </VAlert>
          </VCol>

          <VCol v-if="showMerchantSelect" cols="12">
            <VAutocomplete
              v-model="form.firm_id"
              :items="merchantOptions"
              label="Merchant"
              density="compact"
              clearable
              :no-data-text="t('common.no_data')"
            />
          </VCol>
        </VRow>
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showFormDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" :loading="formLoading" @click="submitForm">
          {{ editingId ? 'Güncelle' : 'Ekle' }}
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Delete confirm -->
  <VDialog v-model="showDeleteDialog" max-width="400">
    <VCard title="Kullanıcıyı sil">
      <VCardText>
        <strong>{{ deleteTarget?.name }}</strong> ({{ deleteTarget?.username }}) kullanıcısı silinecek. Bu işlem geri alınamaz.
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showDeleteDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="error" @click="submitDelete">Sil</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
