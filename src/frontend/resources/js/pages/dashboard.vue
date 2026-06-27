<script setup>
import DashboardStatCard from '@/views/dashboard/DashboardStatCard.vue'
import DashboardTransactionChart from '@/views/dashboard/DashboardTransactionChart.vue'
import DashboardWeeklyOverview from '@/views/dashboard/DashboardWeeklyOverview.vue'
import DashboardRecentTransactions from '@/views/dashboard/DashboardRecentTransactions.vue'
import DashboardTeamPerformance from '@/views/dashboard/DashboardTeamPerformance.vue'
import { setStatsRange, stopStats } from '@/composables/useDashboardStats'
import { useSnackbar } from '@/composables/useSnackbar'
import { useI18n } from 'vue-i18n'
import { Turkish } from 'flatpickr/dist/l10n/tr.js'
import { Russian } from 'flatpickr/dist/l10n/ru.js'
import { english } from 'flatpickr/dist/l10n/default.js'

definePage({ meta: { layout: 'default' } })

const { t, locale } = useI18n()
const snackbar = useSnackbar()

const user = JSON.parse(localStorage.getItem('user') || '{}')
const isMerchant = user.user_type === 3

const today = (() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}` })()
const dateFrom = ref(today)
const dateTo = ref(today)

const localeMap = { tr: Turkish, en: english, ru: Russian }
const dateConfig = computed(() => ({ dateFormat: 'Y-m-d', altInput: true, altFormat: 'd.m.Y', locale: localeMap[locale.value] || Turkish }))

const refreshKey = ref(0)
const applyFilter = () => { refreshKey.value++; setStatsRange(dateFrom.value, dateTo.value) }

// ---- Widget kataloğu ----
// w = 12 sütunluk gridde genişlik; h = satır birimi (1 birim ≈ 110px)
const COMPONENTS = { DashboardStatCard, DashboardTransactionChart, DashboardWeeklyOverview, DashboardRecentTransactions, DashboardTeamPerformance }
const WIDGETS = {
  stat_deposits:    { title: 'Toplam Yatırım',     comp: 'DashboardStatCard', props: { metric: 'deposits' },            w: 3, h: 1 },
  stat_withdrawals: { title: 'Toplam Çekim',        comp: 'DashboardStatCard', props: { metric: 'withdrawals' },         w: 3, h: 1 },
  stat_pending_dep: { title: 'Bekleyen Yatırım',    comp: 'DashboardStatCard', props: { metric: 'pending_deposits' },    w: 3, h: 1 },
  stat_pending_wd:  { title: 'Bekleyen Çekim',      comp: 'DashboardStatCard', props: { metric: 'pending_withdrawals' }, w: 3, h: 1 },
  stat_ibans:       { title: 'Kullanılabilir IBAN', comp: 'DashboardStatCard', props: { metric: 'available_ibans' },     w: 3, h: 1 },
  chart:            { title: 'İşlem Hacmi',         comp: 'DashboardTransactionChart',   w: 6, h: 3 },
  cases:            { title: 'Anlık Kasalar',       comp: 'DashboardWeeklyOverview',     w: 6, h: 3 },
  recent:           { title: 'Son İşlemler',        comp: 'DashboardRecentTransactions', w: 6, h: 3 },
  team_perf:        { title: 'Takım Performansı',   comp: 'DashboardTeamPerformance',    w: 6, h: 3, hideForMerchant: true },
}

// Preset boyutlar
const WIDTH_PRESETS = [
  { label: '¼', value: 3 },
  { label: '⅓', value: 4 },
  { label: '½', value: 6 },
  { label: 'Tam', value: 12 },
]
const HEIGHT_PRESETS = [
  { label: 'Kısa', value: 1 },
  { label: 'Orta', value: 2 },
  { label: 'Uzun', value: 3 },
]

const allowedIds = Object.keys(WIDGETS).filter(id => !(isMerchant && WIDGETS[id].hideForMerchant))

const defaultLayout = () => Object.keys(WIDGETS)
  .filter(id => allowedIds.includes(id))
  .map(id => ({ i: id, w: WIDGETS[id].w, h: WIDGETS[id].h }))

// ---- Durum ----
const editMode = ref(false)
const layout = ref([])
const savedSnapshot = ref('[]')
const saving = ref(false)

const availableToAdd = computed(() => allowedIds.filter(id => !layout.value.some(it => it.i === id)))

const clampW = (w) => (WIDTH_PRESETS.some(p => p.value === w) ? w : (w >= 12 ? 12 : w >= 6 ? 6 : w >= 4 ? 4 : 3))
const clampH = (h) => (h >= 3 ? 3 : h >= 2 ? 2 : 1)

const loadLayout = async () => {
  let result = null
  try {
    const token = localStorage.getItem('token')
    const res = await fetch('/api/dashboard/layout', { headers: { Authorization: `Bearer ${token}` } })
    if (res.ok) {
      const d = await res.json()
      if (d.layout) {
        const parsed = JSON.parse(d.layout)
        if (Array.isArray(parsed)) {
          const valid = parsed.filter(it => it && WIDGETS[it.i] && allowedIds.includes(it.i))
          if (valid.length) result = valid.map(it => ({ i: it.i, w: clampW(+it.w || WIDGETS[it.i].w), h: clampH(+it.h || WIDGETS[it.i].h) }))
        }
      }
    }
  } catch { /* yoksa varsayılan */ }
  layout.value = result || defaultLayout()
  savedSnapshot.value = JSON.stringify(layout.value)
}

const addWidget = (id) => { if (!layout.value.some(it => it.i === id)) layout.value.push({ i: id, w: WIDGETS[id].w, h: WIDGETS[id].h }) }
const removeWidget = (id) => { layout.value = layout.value.filter(it => it.i !== id) }
const setWidth = (item, w) => { item.w = w }
const setHeight = (item, h) => { item.h = h }

const startEdit = () => { savedSnapshot.value = JSON.stringify(layout.value); editMode.value = true }
const cancelEdit = () => { layout.value = JSON.parse(savedSnapshot.value); editMode.value = false }
const resetDefault = () => { layout.value = defaultLayout() }

const saveLayout = async () => {
  saving.value = true
  try {
    const token = localStorage.getItem('token')
    const payload = layout.value.map(it => ({ i: it.i, w: it.w, h: it.h }))
    const res = await fetch('/api/dashboard/layout', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify({ layout: JSON.stringify(payload) }),
    })
    if (res.ok) { savedSnapshot.value = JSON.stringify(layout.value); editMode.value = false; snackbar.success('Dashboard düzeni kaydedildi.') }
    else snackbar.error('Kaydedilemedi.')
  } catch { snackbar.error('Sunucu hatası.') } finally { saving.value = false }
}

// ---- Sürükle-bırak ile sırala (native DnD) ----
const dragIndex = ref(null)
const onDragStart = (idx) => { dragIndex.value = idx }
const onDragEnter = (idx) => {
  if (dragIndex.value === null || dragIndex.value === idx) return
  const arr = [...layout.value]
  const [moved] = arr.splice(dragIndex.value, 1)
  arr.splice(idx, 0, moved)
  layout.value = arr
  dragIndex.value = idx
}
const onDragEnd = () => { dragIndex.value = null }

onMounted(() => { loadLayout(); setStatsRange(dateFrom.value, dateTo.value) })
onUnmounted(() => stopStats())
</script>

<template>
  <div>
    <!-- Üst bar: tarih filtresi + düzenleme -->
    <VCard class="mb-4">
      <VCardText class="d-flex align-center gap-4 flex-wrap">
        <div style="min-width: 160px;">
          <AppDateTimePicker v-model="dateFrom" :label="t('common.start_date')" :config="dateConfig" density="compact" />
        </div>
        <div style="min-width: 160px;">
          <AppDateTimePicker v-model="dateTo" :label="t('common.end_date')" :config="dateConfig" density="compact" />
        </div>
        <VBtn color="primary" @click="applyFilter">{{ t('common.filter') }}</VBtn>
        <VBtn v-if="dateFrom !== today || dateTo !== today" variant="text" color="secondary" @click="dateFrom = today; dateTo = today; applyFilter()">
          {{ t('dashboard.today') }}
        </VBtn>

        <VSpacer />

        <template v-if="!editMode">
          <VBtn variant="tonal" color="secondary" prepend-icon="tabler-layout-dashboard" @click="startEdit">Düzenle</VBtn>
        </template>
        <template v-else>
          <VMenu v-if="availableToAdd.length">
            <template #activator="{ props }">
              <VBtn v-bind="props" variant="tonal" color="primary" prepend-icon="tabler-plus">Kutucuk Ekle</VBtn>
            </template>
            <VList density="compact">
              <VListItem v-for="id in availableToAdd" :key="id" :title="WIDGETS[id].title" @click="addWidget(id)">
                <template #prepend><VIcon icon="tabler-square-plus" size="18" class="me-2" /></template>
              </VListItem>
            </VList>
          </VMenu>
          <VBtn variant="text" color="warning" prepend-icon="tabler-refresh" @click="resetDefault">Varsayılana Dön</VBtn>
          <VBtn variant="text" @click="cancelEdit">İptal</VBtn>
          <VBtn color="success" prepend-icon="tabler-device-floppy" :loading="saving" @click="saveLayout">Kaydet</VBtn>
        </template>
      </VCardText>
    </VCard>

    <div v-if="editMode" class="text-caption text-medium-emphasis mb-2">
      <VIcon icon="tabler-info-circle" size="14" /> Kutucukları <strong>sürükleyerek sıralayın</strong>; boyutu değiştirmek için kutucuktaki <strong>genişlik/yükseklik</strong> düğmelerini kullanın. Diğer kutucuklar otomatik yerleşir.
    </div>

    <div class="dash-grid">
      <div
        v-for="(item, idx) in layout"
        :key="item.i"
        class="dash-cell"
        :class="{ 'edit-on': editMode, 'dragging': dragIndex === idx }"
        :style="{ '--w': item.w, '--h': item.h }"
        :draggable="editMode"
        @dragstart="onDragStart(idx)"
        @dragenter.prevent="onDragEnter(idx)"
        @dragover.prevent
        @dragend="onDragEnd"
      >
        <div v-if="editMode" class="widget-toolbar">
          <VIcon icon="tabler-arrows-move" size="16" />
          <span class="text-caption font-weight-medium ms-1 text-truncate flex-grow-1">{{ WIDGETS[item.i].title }}</span>
          <!-- Genişlik -->
          <div class="preset-group" draggable="false" @dragstart.stop.prevent>
            <VBtn
              v-for="p in WIDTH_PRESETS" :key="'w'+p.value"
              size="x-small" variant="text" :color="item.w === p.value ? 'primary' : undefined"
              class="preset-btn" draggable="false"
              @click.stop="setWidth(item, p.value)"
            >{{ p.label }}</VBtn>
          </div>
          <span class="preset-sep">|</span>
          <!-- Yükseklik -->
          <div class="preset-group" draggable="false" @dragstart.stop.prevent>
            <VBtn
              v-for="p in HEIGHT_PRESETS" :key="'h'+p.value"
              size="x-small" variant="text" :color="item.h === p.value ? 'primary' : undefined"
              class="preset-btn" draggable="false"
              @click.stop="setHeight(item, p.value)"
            >{{ p.label }}</VBtn>
          </div>
          <VBtn icon size="x-small" variant="text" color="error" title="Kaldır" draggable="false" @click.stop="removeWidget(item.i)">
            <VIcon icon="tabler-x" size="16" />
          </VBtn>
        </div>
        <div class="widget-content">
          <component
            :is="COMPONENTS[WIDGETS[item.i].comp]"
            v-bind="WIDGETS[item.i].props || {}"
            :date-from="dateFrom"
            :date-to="dateTo"
            :key="item.i + '-' + refreshKey"
          />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.dash-grid {
  display: grid;
  grid-template-columns: repeat(12, 1fr);
  grid-auto-rows: 110px;
  grid-auto-flow: row dense;
  gap: 12px;
}
.dash-cell {
  grid-column: span var(--w);
  grid-row: span var(--h);
  position: relative;
  min-inline-size: 0;
}
.dash-cell.edit-on { outline: 2px dashed rgba(var(--v-theme-primary), 0.45); outline-offset: 2px; border-radius: 8px; cursor: move; }
.dash-cell.dragging { opacity: 0.5; }
.widget-toolbar {
  position: absolute; inset-block-start: 0; inset-inline: 0;
  display: flex; align-items: center; gap: 1px;
  block-size: 24px; padding: 0 4px 0 6px;
  background: rgba(var(--v-theme-surface), 0.92);
  border-radius: 8px 8px 0 0;
  z-index: 5;
}
.preset-group { display: inline-flex; }
.preset-btn { min-inline-size: 22px !important; padding: 0 4px !important; }
.preset-sep { opacity: 0.4; margin: 0 2px; }
.widget-content { block-size: 100%; overflow: auto; }
.widget-content :deep(.v-card) { block-size: 100%; }

@media (max-width: 600px) {
  .dash-cell { grid-column: 1 / -1 !important; }
}
</style>
