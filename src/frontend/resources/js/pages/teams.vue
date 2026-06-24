<script setup>
import { useI18n } from 'vue-i18n'
import { useApi } from '@/composables/useApi'
import { useSnackbar } from '@/composables/useSnackbar'

definePage({ meta: { layout: 'default', roles: [1, 4] } })

const { t } = useI18n()

const loading = ref(true)
const teams = ref([])
const statusFilter = ref('1')
const search = ref('')

const showAddDialog = ref(false)
const showEditDialog = ref(false)

const form = ref({ name: '', status: 1, min_invest: 0, max_invest: 0, wait_limit: 0, commission: 0, maxCase: 0, allow_duplicate_iban: 0, block_when_full: 1, telegram_enabled: 0, telegram_chat_id: '', telegram_withdraw_chat_id: '', telegram_reconciliation_chat_id: '', telegram_credit_low_enabled: 0, telegram_credit_low_threshold: null, telegram_pending_invest_enabled: 0, telegram_missing_receipt_enabled: 0, telegram_cash_report_enabled: 0, merchant_ids: [] })
const editForm = ref({})

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
const canManageTeams = !!user.permissions?.manage_teams

const formatMoney = val => '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 0 })

const fetchData = async () => {
  loading.value = true
  try {
    const params = new URLSearchParams({ status: statusFilter.value })
    if (search.value) params.append('search', search.value)
    const res = await fetch(`/api/teams?${params}`, { headers })
    if (res.ok) teams.value = await res.json()
  } finally { loading.value = false }
}

const merchants = ref([])
const fetchMerchants = async () => {
  const res = await fetch('/api/merchants?status=1', { headers })
  if (res.ok) merchants.value = await res.json()
}
const merchantItems = computed(() => merchants.value.map(m => ({ title: m.name, value: m.id })))

onMounted(() => { fetchData(); fetchMerchants() })

const addTeam = async () => {
  const res = await fetch('/api/teams', { method: 'POST', headers, body: JSON.stringify(form.value) })
  if (res.ok) {
    showAddDialog.value = false
    form.value = { name: '', status: 1, min_invest: 0, max_invest: 0, wait_limit: 0, commission: 0, maxCase: 0, allow_duplicate_iban: 0, block_when_full: 1, telegram_enabled: 0, telegram_chat_id: '', telegram_withdraw_chat_id: '', telegram_reconciliation_chat_id: '', telegram_credit_low_enabled: 0, telegram_credit_low_threshold: null, telegram_pending_invest_enabled: 0, telegram_missing_receipt_enabled: 0, telegram_cash_report_enabled: 0, merchant_ids: [] }
    fetchData()
  } else {
    const data = await res.json()
    snackbar.handleError(data)
  }
}

const openEdit = async (team) => {
  editForm.value = { ...team, merchant_ids: [] }
  showEditDialog.value = true
  const res = await fetch(`/api/teams/${team.id}/merchants`, { headers })
  if (res.ok) editForm.value.merchant_ids = (await res.json()).merchant_ids || []
}

const updateTeam = async () => {
  const f = editForm.value
  const data = {
    name: f.name, status: f.status,
    min_invest: f.min_invest, max_invest: f.max_invest,
    wait_limit: f.wait_limit, commission: f.commission, maxCase: f.maxCase,
    allow_duplicate_iban: Number(f.allow_duplicate_iban) || 0,
    block_when_full: Number(f.block_when_full ?? 1),
    overturn: f.overturn, withdraw: f.withdraw,
    telegram_enabled: Number(f.telegram_enabled) || 0,
    telegram_chat_id: f.telegram_chat_id || null,
    telegram_withdraw_chat_id: f.telegram_withdraw_chat_id || null,
    telegram_reconciliation_chat_id: f.telegram_reconciliation_chat_id || null,
    telegram_credit_low_enabled: Number(f.telegram_credit_low_enabled) || 0,
    telegram_credit_low_threshold: f.telegram_credit_low_threshold === '' || f.telegram_credit_low_threshold == null ? null : Number(f.telegram_credit_low_threshold),
    telegram_pending_invest_enabled: Number(f.telegram_pending_invest_enabled) || 0,
    telegram_missing_receipt_enabled: Number(f.telegram_missing_receipt_enabled) || 0,
    telegram_cash_report_enabled: Number(f.telegram_cash_report_enabled) || 0,
    merchant_ids: Array.isArray(f.merchant_ids) ? f.merchant_ids : [],
  }
  const res = await fetch(`/api/teams/${f.id}`, { method: 'PUT', headers, body: JSON.stringify(data) })
  const respData = await res.json().catch(() => ({}))
  if (res.ok) { snackbar.success(respData.message || 'Takım güncellendi.'); showEditDialog.value = false; fetchData() }
  else { snackbar.handleError(respData) }
}

const deleteTeam = async (id) => {
  if (!confirm(t('common.confirm') + '?')) return
  await fetch(`/api/teams/${id}`, { method: 'DELETE', headers })
  fetchData()
}
</script>

<template>
  <VRow>
    <VCol cols="12">
      <VCard :loading="loading">
        <VCardItem>
          <VCardTitle>{{ t('teams.title') }}</VCardTitle>
          <template #append>
            <VBtn v-if="canManageTeams" color="primary" size="small" @click="showAddDialog = true">
              <VIcon start icon="tabler-plus" /> {{ t('teams.add') }}
            </VBtn>
          </template>
        </VCardItem>
        <VDivider />

        <VCardText class="d-flex gap-3 flex-wrap">
          <AppTextField v-model="search" :placeholder="t('common.search')" density="compact" style="max-width: 200px;" @keyup.enter="fetchData" />
          <VSelect v-model="statusFilter" :items="statusOptions" density="compact" style="min-width: 130px;" @update:model-value="fetchData" />
        </VCardText>

        <VTable class="text-no-wrap" density="compact">
          <thead>
            <tr>
              <th>ID</th>
              <th>{{ t('teams.name') }}</th>
              <th class="text-end">{{ t('teams.max_case') }}</th>
              <th class="text-end">{{ t('teams.min_invest') }}</th>
              <th class="text-end">{{ t('teams.max_invest') }}</th>
              <th class="text-end">{{ t('teams.commission') }}</th>
              <th>{{ t('deposits.status') }}</th>
              <th class="text-end">{{ t('common.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="team in teams" :key="team.id">
              <td>{{ team.id }}</td>
              <td class="font-weight-medium">{{ team.name }}</td>
              <td class="text-end">{{ formatMoney(team.maxCase) }}</td>
              <td class="text-end">{{ formatMoney(team.min_invest) }}</td>
              <td class="text-end">{{ formatMoney(team.max_invest) }}</td>
              <td class="text-end">%{{ team.commission }}</td>
              <td>
                <VChip :color="statusColors[team.status]" label size="x-small">
                  {{ statusLabels[team.status] }}
                </VChip>
              </td>
              <td class="text-end">
                <VBtn icon size="x-small" variant="text" color="primary" @click="openEdit(team)">
                  <VIcon icon="tabler-edit" size="18" />
                </VBtn>
                <VBtn v-if="canManageTeams" icon size="x-small" variant="text" color="error" @click="deleteTeam(team.id)">
                  <VIcon icon="tabler-trash" size="18" />
                </VBtn>
              </td>
            </tr>
            <tr v-if="!loading && teams.length === 0">
              <td colspan="8" class="text-center text-medium-emphasis">{{ t('common.no_data') }}</td>
            </tr>
          </tbody>
        </VTable>
      </VCard>
    </VCol>
  </VRow>

  <!-- Takım Ekle -->
  <VDialog v-model="showAddDialog" max-width="720" scrollable>
    <VCard class="team-edit-card">
      <VCardItem class="pa-5 pb-3">
        <div class="d-flex align-center gap-3">
          <div class="team-edit-icon">
            <VIcon icon="tabler-users-group" size="20" />
          </div>
          <div>
            <div class="text-h6 font-weight-bold lh-1">{{ t('teams.add') }}</div>
            <div class="text-caption text-medium-emphasis">Yeni takım oluştur</div>
          </div>
          <VSpacer />
          <VBtn icon variant="text" size="small" @click="showAddDialog = false">
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
            <AppTextField v-model="form.name" :label="t('teams.name')" density="comfortable" />
          </VCol>
          <VCol cols="12" md="5">
            <VSelect
              v-model="form.status"
              :items="[{title: t('status.active'), value: 1}, {title: t('status.inactive'), value: 2}, {title: t('banks.ready'), value: 3}]"
              :label="t('deposits.status')"
              density="comfortable"
            />
          </VCol>
          <VCol cols="12">
            <VAutocomplete
              v-model="form.merchant_ids"
              :items="merchantItems"
              label="Atanan Merchant'lar"
              multiple chips closable-chips clearable
              density="comfortable"
              hint="Boş bırakılırsa tüm takımlara açık (kısıt yok). Seçilirse bu merchant'ların işlemleri bu takıma yönlendirilir/görünür."
              persistent-hint
            />
          </VCol>
        </VRow>

        <VDivider class="my-4" />

        <!-- Yatırım Limitleri -->
        <div class="section-label">Yatırım Limitleri</div>
        <VRow class="mb-2">
          <VCol cols="12" sm="6" md="4">
            <AppTextField v-model="form.min_invest" type="number" :label="t('teams.min_invest')" prefix="₺" density="comfortable" />
          </VCol>
          <VCol cols="12" sm="6" md="4">
            <AppTextField v-model="form.max_invest" type="number" :label="t('teams.max_invest')" prefix="₺" density="comfortable" />
          </VCol>
          <VCol cols="12" sm="6" md="4">
            <AppTextField v-model="form.maxCase" type="number" :label="t('teams.max_case')" prefix="₺" density="comfortable" />
          </VCol>
        </VRow>

        <VDivider class="my-4" />

        <!-- Operasyon -->
        <div class="section-label">Operasyon</div>
        <VRow class="mb-2">
          <VCol cols="12" sm="6" md="6">
            <AppTextField v-model="form.wait_limit" type="number" :label="t('teams.wait_limit')" density="comfortable" />
          </VCol>
          <VCol cols="12" sm="6" md="6">
            <AppTextField v-model="form.commission" type="number" :label="t('teams.commission')" suffix="%" density="comfortable" />
          </VCol>
        </VRow>

        <VCard variant="tonal" class="pa-3 mt-2" :color="Number(form.allow_duplicate_iban) === 1 ? 'primary' : undefined">
          <VRow no-gutters align="center">
            <VCol>
              <div class="d-flex align-center gap-2">
                <VIcon icon="tabler-copy" size="20" />
                <div>
                  <div class="font-weight-medium">Aynı IBAN'ı çoklu ekleyebilir</div>
                  <div class="text-caption text-medium-emphasis">
                    Kapalıyken (varsayılan) aynı IBAN sisteme yalnızca bir kez eklenebilir. Açıkken bu takıma sistemde başka yerde kayıtlı olan IBAN da eklenebilir.
                  </div>
                </div>
              </div>
            </VCol>
            <VCol cols="auto">
              <VSwitch
                v-model="form.allow_duplicate_iban"
                :true-value="1" :false-value="0"
                density="compact" color="primary" hide-details
              />
            </VCol>
          </VRow>
        </VCard>

        <VCard variant="tonal" class="pa-3 mt-2" :color="Number(form.block_when_full) === 1 ? 'primary' : undefined">
          <VRow no-gutters align="center">
            <VCol>
              <div class="d-flex align-center gap-2">
                <VIcon icon="tabler-lock" size="20" />
                <div>
                  <div class="font-weight-medium">Maks kasaya ulaşınca yatırımı durdur</div>
                  <div class="text-caption text-medium-emphasis">
                    Açıkken (varsayılan): takımın anlık kasası "Maks Kasa" değerine ulaştığında bu takımın IBAN'ları API ödeme ve H2H tarafında listeye çıkmaz. Anlık kasa düşene kadar yeni yatırım kabul edilmez.
                  </div>
                </div>
              </div>
            </VCol>
            <VCol cols="auto">
              <VSwitch
                v-model="form.block_when_full"
                :true-value="1" :false-value="0"
                density="compact" color="primary" hide-details
              />
            </VCol>
          </VRow>
        </VCard>

        <VDivider class="my-4" />

        <!-- Bildirimler -->
        <div class="section-label">Bildirimler</div>
        <VCard variant="tonal" class="pa-3" :color="Number(form.telegram_enabled) === 1 ? 'primary' : undefined">
          <VRow no-gutters align="center">
            <VCol cols="12" md="auto">
              <VSwitch
                v-model="form.telegram_enabled" :true-value="1" :false-value="0"
                density="compact" color="primary" hide-details
              >
                <template #label>
                  <div class="d-flex align-center gap-2">
                    <VIcon icon="tabler-brand-telegram" size="18" />
                    <span class="font-weight-medium">Telegram Bot Bildirimleri</span>
                  </div>
                </template>
              </VSwitch>
            </VCol>
            <VSpacer />
            <VCol v-if="Number(form.telegram_enabled) === 1" cols="12" md="6">
              <AppTextField
                v-model="form.telegram_chat_id"
                placeholder="-1001234567890"
                density="compact"
                hide-details
              />
            </VCol>
          </VRow>
          <div v-if="Number(form.telegram_enabled) === 1" class="text-caption text-medium-emphasis mt-2">
            Telegram Chat ID — botu gruba ekledikten sonra @userinfobot vb. ile öğrenebilirsiniz.
          </div>
        </VCard>
      </VCardText>
      <VDivider />
      <VCardActions class="pa-4">
        <VSpacer />
        <VBtn variant="text" @click="showAddDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" variant="flat" prepend-icon="tabler-device-floppy" @click="addTeam">
          {{ t('common.save') }}
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>

  <!-- Takım Düzenle -->
  <VDialog v-model="showEditDialog" max-width="720" scrollable>
    <VCard class="team-edit-card">
      <VCardItem class="pa-5 pb-3">
        <div class="d-flex align-center gap-3">
          <div class="team-edit-icon">
            <VIcon icon="tabler-users-group" size="20" />
          </div>
          <div>
            <div class="text-h6 font-weight-bold lh-1">{{ t('teams.edit') }}</div>
            <div class="text-caption text-medium-emphasis">#{{ editForm.id }} · {{ editForm.name }}</div>
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
            <AppTextField v-model="editForm.name" :label="t('teams.name')" :readonly="!canManageTeams" density="comfortable" />
          </VCol>
          <VCol cols="12" md="5">
            <VSelect
              v-model="editForm.status"
              :items="[{title: t('status.active'), value: 1}, {title: t('status.inactive'), value: 2}, {title: t('banks.ready'), value: 3}, {title: t('banks.deleted'), value: 0}]"
              :label="t('deposits.status')"
              density="comfortable"
            />
          </VCol>
          <VCol cols="12">
            <VAutocomplete
              v-model="editForm.merchant_ids"
              :items="merchantItems"
              label="Atanan Merchant'lar"
              multiple chips closable-chips clearable
              :readonly="!canManageTeams"
              density="comfortable"
              hint="Boş = tüm takımlara açık (kısıt yok). Seçili = bu merchant'ların işlemleri bu takıma yönlendirilir/görünür."
              persistent-hint
            />
          </VCol>
        </VRow>

        <VDivider class="my-4" />

        <!-- Yatırım Limitleri -->
        <div class="section-label">Yatırım Limitleri</div>
        <VRow class="mb-2">
          <VCol cols="12" sm="6" md="4">
            <AppTextField v-model="editForm.min_invest" type="number" :label="t('teams.min_invest')" prefix="₺" :readonly="!canManageTeams" density="comfortable" />
          </VCol>
          <VCol cols="12" sm="6" md="4">
            <AppTextField v-model="editForm.max_invest" type="number" :label="t('teams.max_invest')" prefix="₺" :readonly="!canManageTeams" density="comfortable" />
          </VCol>
          <VCol cols="12" sm="6" md="4">
            <AppTextField v-model="editForm.maxCase" type="number" :label="t('teams.max_case')" prefix="₺" :readonly="!canManageTeams" density="comfortable" />
          </VCol>
        </VRow>

        <VDivider class="my-4" />

        <!-- Operasyon -->
        <div class="section-label">Operasyon</div>
        <VRow class="mb-2">
          <VCol cols="12" sm="6" md="4">
            <AppTextField v-model="editForm.wait_limit" type="number" :label="t('teams.wait_limit')" :readonly="!canManageTeams" density="comfortable" />
          </VCol>
          <VCol cols="12" sm="6" md="4">
            <AppTextField v-model="editForm.commission" type="number" :label="t('teams.commission')" suffix="%" :readonly="!canManageTeams" density="comfortable" />
          </VCol>
          <VCol cols="12" sm="6" md="4">
            <AppTextField v-model="editForm.overturn" type="number" :label="t('teams.overturn')" prefix="₺" :readonly="!canManageTeams" density="comfortable" />
          </VCol>
          <VCol cols="12" sm="6" md="4">
            <AppTextField v-model="editForm.withdraw" type="number" :label="t('teams.manual_withdraw')" prefix="₺" density="comfortable" />
          </VCol>
        </VRow>

        <VCard variant="tonal" class="pa-3 mt-2" :color="Number(editForm.allow_duplicate_iban) === 1 ? 'primary' : undefined">
          <VRow no-gutters align="center">
            <VCol>
              <div class="d-flex align-center gap-2">
                <VIcon icon="tabler-copy" size="20" />
                <div>
                  <div class="font-weight-medium">Aynı IBAN'ı çoklu ekleyebilir</div>
                  <div class="text-caption text-medium-emphasis">
                    Kapalıyken (varsayılan) aynı IBAN sisteme yalnızca bir kez eklenebilir. Açıkken bu takıma sistemde başka yerde kayıtlı olan IBAN da eklenebilir.
                  </div>
                </div>
              </div>
            </VCol>
            <VCol cols="auto">
              <VSwitch
                v-model="editForm.allow_duplicate_iban"
                :true-value="1" :false-value="0"
                density="compact" color="primary" hide-details
              />
            </VCol>
          </VRow>
        </VCard>

        <VCard variant="tonal" class="pa-3 mt-2" :color="Number(editForm.block_when_full) === 1 ? 'primary' : undefined">
          <VRow no-gutters align="center">
            <VCol>
              <div class="d-flex align-center gap-2">
                <VIcon icon="tabler-lock" size="20" />
                <div>
                  <div class="font-weight-medium">Maks kasaya ulaşınca yatırımı durdur</div>
                  <div class="text-caption text-medium-emphasis">
                    Açıkken (varsayılan): takımın anlık kasası "Maks Kasa" değerine ulaştığında bu takımın IBAN'ları API ödeme ve H2H tarafında listeye çıkmaz. Anlık kasa düşene kadar yeni yatırım kabul edilmez.
                  </div>
                </div>
              </div>
            </VCol>
            <VCol cols="auto">
              <VSwitch
                v-model="editForm.block_when_full"
                :true-value="1" :false-value="0"
                density="compact" color="primary" hide-details
              />
            </VCol>
          </VRow>
        </VCard>

        <VDivider class="my-4" />

        <!-- Bildirimler -->
        <div class="section-label">Telegram Bildirimleri</div>
        <VCard variant="tonal" class="pa-3" :color="Number(editForm.telegram_enabled) === 1 ? 'primary' : undefined">
          <VRow no-gutters align="center">
            <VCol cols="12" md="auto">
              <VSwitch
                v-model="editForm.telegram_enabled" :true-value="1" :false-value="0"
                density="compact" color="primary" hide-details
              >
                <template #label>
                  <div class="d-flex align-center gap-2">
                    <VIcon icon="tabler-brand-telegram" size="18" />
                    <span class="font-weight-medium">Telegram Bot Bildirimleri</span>
                  </div>
                </template>
              </VSwitch>
            </VCol>
          </VRow>

          <template v-if="Number(editForm.telegram_enabled) === 1">
            <VDivider class="my-3" />

            <!-- 3 Chat ID -->
            <VRow dense>
              <VCol cols="12" md="4">
                <AppTextField
                  v-model="editForm.telegram_chat_id"
                  label="Destek Chat ID"
                  placeholder="-1001234567890"
                  density="compact"
                />
              </VCol>
              <VCol cols="12" md="4">
                <AppTextField
                  v-model="editForm.telegram_withdraw_chat_id"
                  label="Çekim Chat ID"
                  placeholder="-1001234567890"
                  density="compact"
                />
              </VCol>
              <VCol cols="12" md="4">
                <AppTextField
                  v-model="editForm.telegram_reconciliation_chat_id"
                  label="Mutabakat Chat ID"
                  placeholder="-1001234567890"
                  density="compact"
                />
              </VCol>
            </VRow>

            <VDivider class="my-3" />

            <!-- Switch 1: Kredi Azaldı -->
            <VRow no-gutters align="center" class="mb-2">
              <VCol cols="12" md="auto">
                <VSwitch
                  v-model="editForm.telegram_credit_low_enabled" :true-value="1" :false-value="0"
                  density="compact" color="warning" hide-details
                >
                  <template #label>
                    <span class="font-weight-medium">Kredi Azaldı Uyarısı</span>
                  </template>
                </VSwitch>
              </VCol>
              <VSpacer />
              <VCol v-if="Number(editForm.telegram_credit_low_enabled) === 1" cols="12" md="5">
                <AppTextField
                  v-model="editForm.telegram_credit_low_threshold"
                  type="number"
                  prefix="₺"
                  label="Eşik tutarı"
                  placeholder="50000"
                  density="compact"
                  hide-details
                />
              </VCol>
            </VRow>
            <div v-if="Number(editForm.telegram_credit_low_enabled) === 1" class="text-caption text-medium-emphasis mb-3 ms-2">
              Anlık kasa, Maks. Kasa rakamına bu kadar yaklaştığında <strong>Destek Chat</strong> ID'ye mesaj gönderilir.
            </div>

            <!-- Switch 2: İşlem Sonuçlandırılmadı -->
            <VRow no-gutters align="center" class="mb-2">
              <VCol cols="12">
                <VSwitch
                  v-model="editForm.telegram_pending_invest_enabled" :true-value="1" :false-value="0"
                  density="compact" color="info" hide-details
                >
                  <template #label>
                    <span class="font-weight-medium">İşlem Sonuçlandırılmadı Uyarısı</span>
                  </template>
                </VSwitch>
              </VCol>
            </VRow>
            <div v-if="Number(editForm.telegram_pending_invest_enabled) === 1" class="text-caption text-medium-emphasis mb-3 ms-2">
              10 dakika 30 saniyedir sonuçlandırılmayan yatırımlar için <strong>Destek Chat</strong> ID'ye mesaj gönderilir.
            </div>

            <!-- Switch 3: Dekont Gönderilmedi -->
            <VRow no-gutters align="center" class="mb-2">
              <VCol cols="12">
                <VSwitch
                  v-model="editForm.telegram_missing_receipt_enabled" :true-value="1" :false-value="0"
                  density="compact" color="error" hide-details
                >
                  <template #label>
                    <span class="font-weight-medium">Dekont Gönderilmedi Uyarısı</span>
                  </template>
                </VSwitch>
              </VCol>
            </VRow>
            <div v-if="Number(editForm.telegram_missing_receipt_enabled) === 1" class="text-caption text-medium-emphasis mb-3 ms-2">
              Onaylanmış ama 15 dakikadır dekont yüklenmemiş çekimler için <strong>Çekim Chat</strong> ID'ye mesaj gönderilir.
            </div>

            <!-- Switch 4: Kasa Raporu -->
            <VRow no-gutters align="center" class="mb-2">
              <VCol cols="12">
                <VSwitch
                  v-model="editForm.telegram_cash_report_enabled" :true-value="1" :false-value="0"
                  density="compact" color="success" hide-details
                >
                  <template #label>
                    <span class="font-weight-medium">Kasa Raporu</span>
                  </template>
                </VSwitch>
              </VCol>
            </VRow>
            <div v-if="Number(editForm.telegram_cash_report_enabled) === 1" class="text-caption text-medium-emphasis ms-2">
              <strong>Mutabakat Chat</strong>'inde <code>@paylira_reminder_bot kasa</code> yazıldığında bot anlık kasa raporunu cevaplar.
            </div>
          </template>
        </VCard>
      </VCardText>
      <VDivider />
      <VCardActions class="pa-4">
        <VSpacer />
        <VBtn variant="text" @click="showEditDialog = false">{{ t('common.cancel') }}</VBtn>
        <VBtn color="primary" variant="flat" prepend-icon="tabler-device-floppy" @click="updateTeam">
          {{ t('common.save') }}
        </VBtn>
      </VCardActions>
    </VCard>
  </VDialog>
</template>

<style scoped>
.team-edit-icon {
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
</style>
