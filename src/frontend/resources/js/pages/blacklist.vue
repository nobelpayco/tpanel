<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', roles: [1, 4] } })

const { t } = useI18n()
const { headers } = useApi()
const snackbar = useSnackbar()

const loading = ref(true)
const items = ref([])
const search = ref('')
const typeFilter = ref('all')

const showAddDialog = ref(false)
const showEditDialog = ref(false)
const editItem = ref(null)

const form = ref({ type: 1, val: '', desc: '' })

const typeLabels = { 1: 'blacklist_page.type_player', 2: 'blacklist_page.type_name' }
const typeItems = computed(() => [
  { title: t('blacklist_page.type_player'), value: 1 },
  { title: t('blacklist_page.type_name'), value: 2 },
])

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams()
    if (search.value) params.append('search', search.value)
    if (typeFilter.value !== 'all') params.append('type', typeFilter.value)
    const res = await fetch(`/api/blacklist?${params}`, { headers })
    if (res.ok) items.value = await res.json()
  } finally { loading.value = false }
}

const addItem = async () => {
  const res = await fetch('/api/blacklist', { method: 'POST', headers, body: JSON.stringify(form.value) })
  const data = await res.json()
  if (res.ok) {
    showAddDialog.value = false
    form.value = { type: 1, val: '', desc: '' }
    snackbar.success(data.message)
    fetchData()
  } else {
    snackbar.handleError(data)
  }
}

const openEdit = (item) => {
  editItem.value = { ...item }
  showEditDialog.value = true
}

const updateItem = async () => {
  const res = await fetch(`/api/blacklist/${editItem.value.id}`, { method: 'PUT', headers, body: JSON.stringify({ desc: editItem.value.desc }) })
  const data = await res.json()
  if (res.ok) {
    showEditDialog.value = false
    snackbar.success(data.message)
    fetchData()
  } else {
    snackbar.handleError(data)
  }
}

const deleteItem = async (id) => {
  if (!confirm(t('blacklist_page.confirm_delete'))) return
  const res = await fetch(`/api/blacklist/${id}`, { method: 'DELETE', headers })
  const data = await res.json()
  if (res.ok) {
    snackbar.success(data.message)
    fetchData()
  } else {
    snackbar.handleError(data)
  }
}

onMounted(fetchData)
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle class="d-flex align-center gap-2">
            {{ t('blacklist_page.title') }}
            <VChip color="error" label size="small">{{ items.length }}</VChip>
          </VCardTitle>
          <template #append>
            <VBtn color="error" @click="showAddDialog = true">
              <VIcon start icon="tabler-plus" />
              {{ t('blacklist_page.add') }}
            </VBtn>
          </template>
        </VCardItem>
        <VDivider />

        <VCardText class="d-flex gap-3 flex-wrap">
          <AppTextField
            v-model="search"
            :placeholder="t('blacklist_page.search_placeholder')"
            density="compact"
            style="min-width: 250px;"
            prepend-inner-icon="tabler-search"
            @keyup.enter="fetchData"
          />
          <VBtn color="primary" variant="tonal" @click="fetchData">{{ t('common.filter') }}</VBtn>
        </VCardText>

        <VTable class="text-no-wrap" density="compact">
          <thead>
            <tr>
              <th>ID</th>
              <th>{{ t('blacklist_page.type') }}</th>
              <th>{{ t('blacklist_page.value') }}</th>
              <th>{{ t('blacklist_page.description') }}</th>
              <th class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in items" :key="item.id">
              <td class="text-body-2">{{ item.id }}</td>
              <td>
                <VChip color="error" label size="x-small">
                  {{ t(typeLabels[item.type] || 'blacklist_page.type_player') }}
                </VChip>
              </td>
              <td class="font-weight-medium">{{ item.val }}</td>
              <td class="text-body-2 text-medium-emphasis" style="max-width: 400px; white-space: normal;">
                {{ item.desc || '-' }}
              </td>
              <td class="text-end">
                <VBtn icon size="x-small" variant="text" color="primary" class="me-1" @click="openEdit(item)">
                  <VIcon icon="tabler-edit" size="18" />
                </VBtn>
                <VBtn icon size="x-small" variant="text" color="error" @click="deleteItem(item.id)">
                  <VIcon icon="tabler-trash" size="18" />
                </VBtn>
              </td>
            </tr>
            <tr v-if="!loading && items.length === 0">
              <td colspan="5" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>
  </VRow>

  <!-- Ekle Dialog -->
  <VDialog v-model="showAddDialog" max-width="500">
    <VCard :title="t('blacklist_page.add')">
      <VCardText>
        <VSelect
          v-model="form.type"
          :items="typeItems"
          :label="t('blacklist_page.type')"
          class="mb-4"
        />
        <AppTextField
          v-model="form.val"
          :label="t('blacklist_page.value')"
          :placeholder="form.type === 2 ? t('blacklist_page.type_name') : t('blacklist_page.type_player')"
          class="mb-4"
        />
        <AppTextField
          v-model="form.desc"
          :label="t('blacklist_page.description')"
        />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showAddDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="error" @click="addItem">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Düzenle Dialog -->
  <VDialog v-model="showEditDialog" max-width="500">
    <VCard v-if="editItem" :title="`${editItem.val}`">
      <VCardText>
        <AppTextField
          v-model="editItem.desc"
          :label="t('blacklist_page.description')"
        />
      </VCardText>
      <VCardActions>
        <VSpacer />
        <VBtn variant="text" @click="showEditDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" @click="updateItem">{{ t('common.save') }}</VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>
